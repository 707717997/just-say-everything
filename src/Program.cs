using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SayAllVoice
{
    public class TrayApp : ApplicationContext
    {
        private readonly Settings S;
        private readonly AppIndex Apps;
        private readonly CommandStore Store;
        private PaletteForm Palette;
        private NotifyIcon Tray;
        private ToolStripMenuItem _toggleItem;
        private ToolStripMenuItem _startupItem;
        private System.Windows.Forms.Timer _poll;
        private LibraryForm _library;

        // hook state
        private Thread _hookThread;
        private uint _hookThreadId;
        private IntPtr _hookId = IntPtr.Zero;
        private Native.LowLevelKeyboardProc _hookProc;
        // Debug aid: when set, the hook also reacts to synthetic (SendInput) keys,
        // so the whole flow can be exercised without the remote control.
        private readonly bool _allowInjected = Environment.GetEnvironmentVariable("SAYALLVOICE_ALLOW_INJECTED") == "1";
        private volatile bool _injecting;
        private string _sayAllLog;
        private readonly HashSet<int> _held = new HashSet<int>();
        private int[] _hotkeyVks;
        private bool _hotkeyActive;
        private int[] _manualVks = new int[0];
        private bool _manualActive;

        // session state
        private bool _voiceActive;
        private IntPtr _prevWindow = IntPtr.Zero;
        private string _lastText = "";
        private DateTime _lastChange = DateTime.Now;
        private DateTime _upTime = DateTime.MaxValue;
        private bool _manualMode;

        // 第二只耳朵
        private readonly AudioTap Tap = new AudioTap();
        private readonly LocalAsr Asr = new LocalAsr();
        private int _sid;
        private bool _asrSent;
        private bool _asrDone;
        private string _asrText;
        private volatile bool _micSeen;
        private DateTime _downTime = DateTime.MinValue;
        private long _focusAt;
        private long _chordAt;

        // 对话状态：问了用户一句，等回答
        private class Pending
        {
            public string Heard;
            public List<Command> Options = new List<Command>();
            public bool Learn;
        }
        private Pending _pending;
        private System.Windows.Forms.Timer _pendingTimer;
        private Command _lastCmd;
        private Toast _toast;
        private static readonly HashSet<string> Dangerous = new HashSet<string>(
            new string[] { "sys_shutdown", "sys_restart", "sys_logoff", "sys_sleep", "sys_hibernate" });

        private System.Windows.Forms.Timer _guard;
        private int _guardTicks;
        private System.Windows.Forms.Timer _sayAllKeeper;
        private bool _sayAllMissingShown;

        public TrayApp()
        {
            S = Settings.Load();
            Apps = new AppIndex();
            Apps.Load();
            Executor.Apps = Apps;
            Executor.ShowLibrary = ShowLibrary;
            Executor.Answer = delegate(string a) { Reply(a, 0); };
            Store = new CommandStore(Apps);
            Speaker.Enabled = S.Speak;
            ActivityLog.Enabled = S.RecordActivity;
            ActivityLog.Start();

            Palette = new PaletteForm();
            IntPtr force = Palette.Handle; // create the window up front so the hook can show it instantly
            _toast = new Toast();
            IntPtr forceToast = _toast.Handle;
            _pendingTimer = new System.Windows.Forms.Timer();
            _pendingTimer.Interval = 12000;
            _pendingTimer.Tick += delegate
            {
                _pendingTimer.Stop();
                if (_pending != null && !_voiceActive) { _pending = null; HidePalette(true); }
            };
            Palette.SubmitRequested += OnSubmit;
            Palette.CancelRequested += OnCancel;
            Palette.Typed += delegate { if (_manualMode) LiveUpdate(Palette.TextValue); };
            Palette.LibraryRequested += delegate { HidePalette(true); ShowLibrary(); };

            _poll = new System.Windows.Forms.Timer();
            _poll.Interval = 80;
            _poll.Tick += OnPoll;

            _guard = new System.Windows.Forms.Timer();
            _guard.Interval = 150;
            _guard.Tick += OnGuard;

            BuildTray();
            ApplyStartup();
            if (S.AutoStartSayAll) EnsureSayAll(false);
            RebuildHotkey();
            StartHook();
            StartSayAllWatch();
            Asr.Start();
            // SayAll 主程序一退，遥控器语音键就成了死键，所以一直看着，掉了就拉起来。
            _sayAllKeeper = new System.Windows.Forms.Timer();
            _sayAllKeeper.Interval = 20000;
            _sayAllKeeper.Tick += delegate
            {
                if (S.AutoStartSayAll) EnsureSayAll(false);
                if (!Asr.Ready) Asr.Start();
            };
            _sayAllKeeper.Start();
            Log.Write(Origin.Name + " " + Origin.Version + " (" + Origin.Url + ", " + Origin.Mark + ")");
            Log.Write("SayAllVoice started, hotkey=" + string.Join("+", S.HotkeyKeys.ToArray()) + " hook=" + S.HookEnabled);
        }

        // ------------------------------------------------------------------ tray

        private void BuildTray()
        {
            Tray = new NotifyIcon();
            Tray.Icon = LoadAppIcon(SystemInformation.SmallIconSize);
            Tray.Text = Origin.Name;
            Tray.Visible = true;
            Tray.DoubleClick += delegate { ShowLibrary(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9.5f);

            _toggleItem = new ToolStripMenuItem("语音指令：已开启");
            _toggleItem.Click += delegate
            {
                S.HookEnabled = !S.HookEnabled;
                S.Save();
                RebuildHotkey();
                UpdateMenu();
            };
            menu.Items.Add(_toggleItem);

            ToolStripMenuItem lib = new ToolStripMenuItem("打开指令库…");
            lib.Click += delegate { ShowLibrary(); };
            menu.Items.Add(lib);

            ToolStripMenuItem manual = new ToolStripMenuItem("手动输入指令 (Ctrl+Alt+M)");
            manual.Click += delegate { BeginManual(); };
            menu.Items.Add(manual);

            ToolStripMenuItem help = new ToolStripMenuItem("使用说明");
            help.Click += delegate
            {
                try { Process.Start("explorer.exe", "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "使用说明.md") + "\""); }
                catch (Exception ex) { Log.Warn("open help failed: " + ex.Message); }
            };
            menu.Items.Add(help);

            ToolStripMenuItem about = new ToolStripMenuItem("关于 " + Origin.Name);
            about.Click += delegate
            {
                if (MessageBox.Show(Origin.Name + " " + Origin.Version + "\n\n作者：" + Origin.Author + "\n许可证：" + Origin.License
                        + "\n" + Origin.Url + "\n\n打开项目主页？", "关于", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Process.Start(Origin.Url);
            };
            menu.Items.Add(about);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem rebuild = new ToolStripMenuItem("重建应用索引");
            rebuild.Click += delegate
            {
                Apps.Rebuild();
                Tray.ShowBalloonTip(2500, "SayAll 语音指令", "已找到 " + Apps.Apps.Count + " 个可启动的应用", ToolTipIcon.Info);
            };
            menu.Items.Add(rebuild);

            ToolStripMenuItem sayall = new ToolStripMenuItem("启动 SayAll（无线麦）");
            sayall.Click += delegate { EnsureSayAll(true); };
            menu.Items.Add(sayall);

            _startupItem = new ToolStripMenuItem("开机自动启动");
            _startupItem.CheckOnClick = true;
            _startupItem.Checked = IsStartupEnabled();
            _startupItem.Click += delegate
            {
                SetStartup(_startupItem.Checked);
                S.StartWithWindows = _startupItem.Checked;
                S.Save();
            };
            menu.Items.Add(_startupItem);

            ToolStripMenuItem speak = new ToolStripMenuItem("语音回答");
            speak.CheckOnClick = true;
            speak.Checked = S.Speak;
            speak.Click += delegate { S.Speak = speak.Checked; Speaker.Enabled = S.Speak; S.Save(); };
            menu.Items.Add(speak);

            ToolStripMenuItem record = new ToolStripMenuItem("记录使用情况（只存本机）");
            record.CheckOnClick = true;
            record.Checked = S.RecordActivity;
            record.Click += delegate { S.RecordActivity = record.Checked; ActivityLog.Enabled = S.RecordActivity; S.Save(); };
            menu.Items.Add(record);

            ToolStripMenuItem folder = new ToolStripMenuItem("打开配置文件夹");
            folder.Click += delegate { Process.Start("explorer.exe", Settings.DataDir); };
            menu.Items.Add(folder);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { Shutdown(); };
            menu.Items.Add(quit);

            Tray.ContextMenuStrip = menu;
            UpdateMenu();
        }

        private void UpdateMenu()
        {
            if (_toggleItem != null)
                _toggleItem.Text = S.HookEnabled ? "语音指令：已开启" : "语音指令：已暂停";
        }

        /// <summary>和 exe 同一份图标（assets\\app.ico），按需要的尺寸取，托盘不是放缩出来的糊图。</summary>
        private static Icon LoadAppIcon(Size size)
        {
            using (Stream st = typeof(TrayApp).Assembly.GetManifestResourceStream("app.ico"))
            {
                return new Icon(st, size);
            }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    return k != null && k.GetValue("SayAllVoice") != null;
                }
            }
            catch { return false; }
        }

        private void ApplyStartup()
        {
            try { SetStartup(S.StartWithWindows); }
            catch (Exception ex) { Log.Warn("startup key: " + ex.Message); }
        }

        private const string SayAllApp = "sayall-windows-app.exe";

        /// <summary>安装位置不写死：先问安装器记的注册表，再看正在跑的 helper，最后才扫目录。</summary>
        private static string FindSayAll()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\GetSayAll\无线麦 SayAll"))
                {
                    string dir = k == null ? null : k.GetValue("") as string;
                    if (!string.IsNullOrEmpty(dir))
                    {
                        string f = Path.Combine(dir.Trim('"'), SayAllApp);
                        if (File.Exists(f)) return f;
                    }
                }
            }
            catch (Exception ex) { Log.Warn("sayall registry: " + ex.Message); }

            foreach (Process p in Process.GetProcessesByName("sayall-helper"))
            {
                try
                {
                    string f = Path.Combine(Path.GetDirectoryName(p.MainModule.FileName), SayAllApp);
                    if (File.Exists(f)) return f;
                }
                catch { }
            }

            string[] roots = new string[] { @"C:\Program Files (x86)", @"C:\Program Files", @"E:\Program Files (x86)", @"E:\Program Files", @"D:\Program Files (x86)", @"D:\Program Files" };
            foreach (string r in roots)
            {
                try
                {
                    if (!Directory.Exists(r)) continue;
                    foreach (string d in Directory.GetDirectories(r, "*SayAll*"))
                    {
                        string f = Path.Combine(d, SayAllApp);
                        if (File.Exists(f)) return f;
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>SayAll 是遥控器语音键的接管者，它没跑的时候按键不会有任何反应。</summary>
        private void EnsureSayAll(bool announce)
        {
            try
            {
                if (Process.GetProcessesByName("sayall-windows-app").Length > 0)
                {
                    if (announce) Tray.ShowBalloonTip(2000, "SayAll", "SayAll 已经在运行", ToolTipIcon.Info);
                    return;
                }
                string exe = FindSayAll();
                if (exe == null)
                {
                    Log.Warn("sayall exe not found");
                    if (announce || !_sayAllMissingShown)
                        Tray.ShowBalloonTip(4000, "SayAll", "找不到 SayAll 主程序，遥控器语音键用不了", ToolTipIcon.Warning);
                    _sayAllMissingShown = true;
                    return;
                }
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                Log.Write("sayall launched: " + exe);
                if (announce) Tray.ShowBalloonTip(2500, "SayAll", "已启动 SayAll，遥控器语音键现在可用", ToolTipIcon.Info);
            }
            catch (Exception ex) { Log.Warn("ensure sayall: " + ex.Message); }
        }

        private static void SetStartup(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (k == null) return;
                if (on) k.SetValue("SayAllVoice", "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue("SayAllVoice", false);
            }
        }

        private void StartSayAllWatch()
        {
            _sayAllLog = Program.SayAllLogOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"SayAll\Logs\sayall-diagnostic.log");
            Thread t = new Thread(WatchLoop);
            t.IsBackground = true;
            t.Priority = ThreadPriority.AboveNormal;
            t.Name = "sayall-watch";
            t.Start();
        }

            Stopwatch idle = Stopwatch.StartNew();
        /// <summary>
        /// 盯 SayAll 的日志判断语音键。它在按下时写 voice_begin，约 100ms 后才合成听写键，
        /// 面板必须赶在听写键之前拿到焦点，否则输入法的麦克风打不开，所以这里用独立线程高频读。
        /// 注意 suppressor_ask dir=up 不是松手：按下的一瞬间它就会写一条，按住时 dir=down 还会连发。
        /// </summary>
        private void WatchLoop()
        {
            FileStream fs = null;
            long pos = -1;
            byte[] buf = new byte[1 << 16];
            while (true)
            {
                try
                {
                    if (fs == null)
                    {
                        if (!File.Exists(_sayAllLog)) { Thread.Sleep(2000); continue; }
                        fs = new FileStream(_sayAllLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
                        if (pos < 0) { pos = fs.Length; Log.Write("sayall watch on, pos=" + pos); }
                    }
                    long len = fs.Length;
                    if (len < pos) pos = 0;
                    if (len == pos)
                    {
                        // SayAll 写满约 10MB 会把日志改名存档、另起新文件；手里的句柄还指着旧文件，
                        // 必须换到新文件，否则从此收不到按键。新文件比旧文件短就说明轮换过。
                        if (idle.ElapsedMilliseconds > 1000)
                        {
                            idle.Restart();
                            FileInfo fi = new FileInfo(_sayAllLog);
                            if (fi.Exists && fi.Length < len)
                            {
                                Log.Write("sayall log rotated, reopening");
                                fs.Dispose();
                                fs = new FileStream(_sayAllLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
                                pos = 0;
                                continue;
                            }
                        }
                        Thread.Sleep(8);
                        continue;
                    }
                    fs.Seek(pos, SeekOrigin.Begin);
                    int n = fs.Read(buf, 0, buf.Length);
                    int end = Array.LastIndexOf(buf, (byte)'\n', Math.Max(0, n - 1));
                    if (n <= 0 || end < 0) { Thread.Sleep(8); continue; }
                    pos += end + 1;
                    foreach (string line in Encoding.UTF8.GetString(buf, 0, end).Split('\n')) OnSayAllLine(line);
                }
                catch (Exception ex)
                {
                    Log.Warn("sayall watch: " + ex.Message);
                    try { if (fs != null) fs.Dispose(); } catch { }
                    fs = null;
                    Thread.Sleep(1000);
                }
            }
        }

        private void OnSayAllLine(string l)
        {
            if (l.IndexOf("usage_stats action=voice_begin", StringComparison.Ordinal) >= 0)
            {
                _chordAt = 0;
                BeginInvokeUI(delegate
                {
                    if (!S.HookEnabled || _manualMode) return;
                    Log.Write("trigger: sayall voice key down");
                    BeginVoice();
                });
            }
            else if (l.IndexOf("transcript_recording action=finish", StringComparison.Ordinal) >= 0
                  || l.IndexOf("usage_stats action=voice_finish", StringComparison.Ordinal) >= 0)
            {
                BeginInvokeUI(delegate
                {
                    if (_voiceActive && _upTime == DateTime.MaxValue) { Log.Write("trigger: sayall voice key up"); EndVoice(); }
                });
            }
            else if (l.IndexOf(" chord_press result=ok", StringComparison.Ordinal) >= 0)
            {
                _chordAt = Stopwatch.GetTimestamp();
                CheckHookAlive(_chordAt);
                long focus = _focusAt;
                BeginInvokeUI(delegate
                {
                    long ms = (_focusAt - _chordAt) * 1000 / Stopwatch.Frequency;
                    Log.Write(_focusAt >= _chordAt ? "race LOST, panel focused " + ms + "ms after chord" : "race won by " + (-ms) + "ms");
                });
            }
            else if (l.IndexOf(" voice_verify ", StringComparison.Ordinal) >= 0 && l.IndexOf("mic=observed", StringComparison.Ordinal) >= 0)
            {
                _micSeen = true;
            }
        }

        // ------------------------------------------------------------------ hook

        private static int Canonical(int vk)
        {
            switch (vk)
            {
                case 0xA2: case 0xA3: return 0x11;
                case 0xA0: case 0xA1: return 0x10;
                case 0xA4: case 0xA5: return 0x12;
                case 0x5C: return 0x5B;
            }
            return vk;
        }

        private void RebuildHotkey()
        {
            List<int> vks = new List<int>();
            foreach (string k in S.HotkeyKeys)
            {
                int vk = VkFromName(k);
                if (vk != 0) vks.Add(Canonical(vk));
            }
            _hotkeyVks = vks.ToArray();
            Log.Write("hotkey vks: " + string.Join(",", Array.ConvertAll(_hotkeyVks, delegate(int v) { return v.ToString("X2"); })));

            List<int> mv = new List<int>();
            foreach (string part in (S.ManualHotkey ?? "").Split('+'))
            {
                int vk = VkFromName(part);
                if (vk != 0) mv.Add(Canonical(vk));
            }
            _manualVks = mv.ToArray();
            Log.Write("manual hotkey vks: " + string.Join(",", Array.ConvertAll(_manualVks, delegate(int v) { return v.ToString("X2"); })));
        }

        private static int VkFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string n = name.Trim().ToLowerInvariant();
            switch (n)
            {
                case "left_control": case "control": case "ctrl": case "left_ctrl": return 0x11;
                case "right_control": case "right_ctrl": return 0x11;
                case "left_windows": case "win": case "lwin": case "left_win": return 0x5B;
                case "right_windows": case "rwin": case "right_win": return 0x5B;
                case "left_alt": case "alt": return 0x12;
                case "left_shift": case "shift": return 0x10;
                case "space": case "空格": return 0x20;
                case "tab": return 0x09;
                case "enter": case "return": case "回车": return 0x0D;
                case "esc": case "escape": return 0x1B;
                case "backspace": return 0x08;
                case "delete": case "del": return 0x2E;
                case "insert": return 0x2D;
                case "home": return 0x24;
                case "end": return 0x23;
                case "pgup": case "pageup": return 0x21;
                case "pgdn": case "pagedown": return 0x22;
                case "left": case "leftarrow": return 0x25;
                case "up": case "uparrow": return 0x26;
                case "right": case "rightarrow": return 0x27;
                case "down": case "downarrow": return 0x28;
                case "printscreen": case "snapshot": return 0x2C;
                case "capslock": return 0x14;
                case "minus": return 0xBD;
                case "equals": return 0xBB;
                case "comma": return 0xBC;
                case "period": return 0xBE;
                case "slash": return 0xBF;
                case "backslash": return 0xDC;
                case "semicolon": return 0xBA;
                case "quote": return 0xDE;
                case "grave": return 0xC0;
            }
            if (n.Length == 1)
            {
                char c = n[0];
                if (c >= 'a' && c <= 'z') return 0x41 + c - 'a';
                if (c >= '0' && c <= '9') return 0x30 + c - '0';
            }
            if (n.Length >= 2 && n[0] == 'f')
            {
                int f;
                if (int.TryParse(n.Substring(1), out f) && f >= 1 && f <= 24) return 0x6F + f;
            }
            return 0;
        }

        private void StartHook()
        {
            _hookProc = HookCallback;
            _hookThread = new Thread(HookLoop);
            _hookThread.IsBackground = true;
            _hookThread.Name = "SayAllVoiceHook";
            _hookThread.Start();
        }

        private void HookLoop()
        {
            _hookThreadId = Native.GetCurrentThreadId();
            _hookId = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _hookProc, Native.GetModuleHandle(null), 0);
            Log.Write("hook installed id=" + _hookId);
            Native.MSG msg;
            while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_REHOOK)
                {
                    if (_hookId != IntPtr.Zero) Native.UnhookWindowsHookEx(_hookId);
                    _hookId = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _hookProc, Native.GetModuleHandle(null), 0);
                    Log.Write("hook reinstalled id=" + _hookId);
                    continue;
                }
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
        }

        // 回调偶尔超时，Windows 会不打招呼地摘掉低层钩子。SayAll 合成听写键后钩子却一直没动静，就重装。
        private const uint WM_REHOOK = 0x8007;
        private long _hookSeenAt;

        private void CheckHookAlive(long chordAt)
        {
            System.Threading.Timer t = null;
            t = new System.Threading.Timer(delegate
            {
                t.Dispose();
                if (_hookSeenAt >= chordAt || _hookThreadId == 0 || !S.HookEnabled) return;
                Log.Warn("hook silent after sayall chord, reinstalling");
                Native.PostThreadMessage(_hookThreadId, WM_REHOOK, IntPtr.Zero, IntPtr.Zero);
            }, null, 600, Timeout.Infinite);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            _hookSeenAt = Stopwatch.GetTimestamp();
            if (nCode >= 0 && S.HookEnabled)
            {
                try
                {
                    Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    int rawVk = (int)k.vkCode;
                    bool injected = (k.flags & Native.LLKHF_INJECTED) != 0;
                    if (S.KeyTrace && IsTraceKey(rawVk))
                        Log.Write("keytrace vk=" + rawVk.ToString("X2") + " flags=" + k.flags.ToString("X2") + (injected ? " injected" : " physical"));
                    if (!injected && (wParam == (IntPtr)Native.WM_KEYDOWN || wParam == (IntPtr)Native.WM_SYSKEYDOWN))
                        ActivityLog.Key(rawVk);
                    bool accept = _allowInjected || !injected || (!_injecting && IsModifierKey(rawVk));
                    if (accept)
                    {
                        bool down = wParam == (IntPtr)Native.WM_KEYDOWN || wParam == (IntPtr)Native.WM_SYSKEYDOWN;
                        bool up = wParam == (IntPtr)Native.WM_KEYUP || wParam == (IntPtr)Native.WM_SYSKEYUP;
                        int vk = Canonical((int)k.vkCode);
                        if (down || up) HandleKey(vk, down);
                    }
                }
                catch (Exception ex) { Log.Warn("hook: " + ex.Message); }
            }
            return Native.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static bool IsTraceKey(int vk)
        {
            return vk == 0x11 || vk == 0xA2 || vk == 0xA3 || vk == 0x5B || vk == 0x5C || vk == 0x74;
        }

        private static bool IsModifierKey(int vk)
        {
            return vk == 0x11 || vk == 0xA2 || vk == 0xA3 || vk == 0x10 || vk == 0xA0 || vk == 0xA1
                || vk == 0x12 || vk == 0xA4 || vk == 0xA5 || vk == 0x5B || vk == 0x5C;
        }

        private void HandleKey(int vk, bool down)
        {
            if (down) _held.Add(vk);
            else _held.Remove(vk);

            // 锁屏、UAC 这类场景钩子收不到松键，_held 会留下幽灵按键，下次只按一个键就误触发。
            if (down)
            {
                foreach (int h in new List<int>(_held))
                    if (h != vk && (Native.GetAsyncKeyState(h) & 0x8000) == 0) _held.Remove(h);
            }

            bool all = _hotkeyVks.Length > 0;
            for (int i = 0; i < _hotkeyVks.Length; i++) if (!_held.Contains(_hotkeyVks[i])) { all = false; break; }

            if (all && !_hotkeyActive)
            {
                _hotkeyActive = true;
                BeginInvokeUI(delegate { if (!_voiceActive) BeginVoice(); });
            }
            else if (!all && _hotkeyActive)
            {
                _hotkeyActive = false;
                BeginInvokeUI(EndVoice);
            }

            // manual input hotkey (default ctrl+alt+space)
            bool m = _manualVks.Length > 0;
            for (int i = 0; i < _manualVks.Length; i++) if (!_held.Contains(_manualVks[i])) { m = false; break; }
            if (m && !_manualActive)
            {
                _manualActive = true;
                BeginInvokeUI(BeginManual);
            }
            else if (!m && _manualActive)
            {
                _manualActive = false;
            }
        }

        private void BeginInvokeUI(Action a)
        {
            if (Palette != null && Palette.IsHandleCreated)
            {
                try { Palette.BeginInvoke(a); return; } catch { }
            }
        }

        // ------------------------------------------------------------------ voice session

        private void BeginVoice()
        {
            // 先抢焦点，别的都放后面：这一步和 SayAll 的听写键在赛跑。
            Speaker.Stop();
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != Palette.Handle) _prevWindow = fg;
            if (!Palette.Visible) { Palette.PlaceOnScreen(); Palette.Show(); }
            Palette.TextValue = "";
            bool got = Native.EnsureForeground(Palette.Handle);
            Palette.FocusInput();
            _focusAt = Stopwatch.GetTimestamp();

            _sid++;
            _manualMode = false;
            _voiceActive = true;
            _downTime = DateTime.Now;
            _upTime = DateTime.MaxValue;
            _lastText = "";
            _lastChange = DateTime.Now;
            _asrSent = false;
            _asrDone = false;
            _asrText = null;
            _micSeen = false;
            Tap.Begin();
            Store.InDouyin = WindowOps.IsDouyin(_prevWindow);
            _pendingTimer.Stop();

            if (_pending == null)
            {
                Palette.ResetForSession();
                Palette.SetCandidates(Store.Suggest(S.ShowCandidates));
            }
            _poll.Start();
            _guardTicks = 0;
            _guard.Start();
            Log.Write("voice begin, prev=" + _prevWindow + " '" + Native.WindowTitle(_prevWindow) + "' focus=" + got);
        }

        private void EndVoice()
        {
            if (!_voiceActive || _upTime != DateTime.MaxValue) return;
            _upTime = DateTime.Now;
            Palette.SetStatus("正在识别…", 0);
        }

        private void SendToAsr()
        {
            _asrSent = true;
            int peak;
            byte[] pcm = Tap.Take(out peak);
            Tap.End();
            Log.Write("audio " + pcm.Length / 32 + "ms peak=" + peak);
            if (!Asr.Ready || pcm.Length < 8000 || peak < 400) { _asrDone = true; return; }
            int sid = _sid;
            Asr.Recognize(pcm, delegate(string text)
            {
                BeginInvokeUI(delegate
                {
                    if (sid != _sid) return;
                    _asrText = text.Trim();
                    _asrDone = true;
                });
            });
        }

        private void OnPoll(object sender, EventArgs e)
        {
            if (!_voiceActive || _manualMode) return;
            DateTime now = DateTime.Now;
            string t = Palette.TextValue;
            string trimmed = t.Trim();
            if (t != _lastText)
            {
                _lastText = t;
                _lastChange = now;
                LiveUpdate(trimmed);
            }
            if (_upTime == DateTime.MaxValue)
            {
                // 收不到松手（SayAll 中途退出）时别把面板挂在屏幕上。
                if ((now - _downTime).TotalSeconds > 45) { Log.Write("voice: no release, aborting"); HidePalette(true); }
                return;
            }
            double since = (now - _upTime).TotalMilliseconds;
            if (!_asrSent && since >= 250) SendToAsr();
            bool asrFinal = _asrSent && (_asrDone || !Asr.Ready);

            if (trimmed.Length > 0)
            {
                bool settled = (now - _lastChange).TotalMilliseconds >= 430;
                if (settled && since >= 480 && (asrFinal || since >= 1300)) Decide(trimmed, _asrText);
                return;
            }
            // 输入法没动静。麦克风没开就不用等，开了的话它还在出字，多等一会儿。
            double wait = _micSeen ? 2600 : 900;
            if (asrFinal && !string.IsNullOrEmpty(_asrText) && since >= wait) { Decide(null, _asrText); return; }
            if (since > (asrFinal ? 3500 : 6000))
            {
                Log.Write("voice: no text, aborting");
                if (_pending != null) { _pending = null; }
                HidePalette(true);
                Flash("没听到内容，再说一次", 2);
            }
        }

        private void OnGuard(object sender, EventArgs e)
        {
            _guardTicks++;
            if (_guardTicks > 6) { _guard.Stop(); return; }
            if (!Palette.Visible) { _guard.Stop(); return; }
            if (Palette.TextValue.Length > 0) { _guard.Stop(); return; }
            if (Native.GetForegroundWindow() == Palette.Handle) return;
            string p = Native.ForegroundProcessName();
            if (p.Length == 0 || p == "sayallvoice" || p.StartsWith("wetype")) return;
            Native.ForceForeground(Palette.Handle);
            Palette.FocusInput();
            Log.Write("guard: focus re-asserted (fg was " + p + ")");
        }

        private void LiveUpdate(string text)
        {
            if (text.Length == 0)
            {
                Palette.SetCandidates(Store.Suggest(S.ShowCandidates));
                Palette.SetStatus("正在聆听…", 0);
                return;
            }
            List<Match> ms = Store.Match(text, S.ShowCandidates);
            Palette.SetCandidates(ms);
            Match top = ms.Count > 0 ? ms[0] : null;
            if (top != null && top.Score * 100 >= S.AutoThreshold)
                Palette.SetStatus("✓  " + top.Cmd.Name, 1);
            else if (ms.Count > 0)
                Palette.SetStatus("✍  " + (text.Length > 22 ? text.Substring(0, 22) + "…" : text), 0);
        }

        private void Flash(string text, int tone)
        {
            _toast.Flash(text, tone, tone == 0 ? 6000 : 1800);
        }

        /// <summary>说出来，同时在屏幕下方显示。</summary>
        private void Reply(string text, int tone)
        {
            Log.Write("say: " + text);
            Flash(text, tone);
            Speaker.Say(text);
        }

        private Match Top(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            List<Match> ms = Store.Match(text, 1);
            return ms.Count > 0 ? ms[0] : null;
        }

        /// <summary>两只耳朵的结果都在这里定。ime 是输入法，asr 是本地识别，都可能为空。</summary>
        private void Decide(string ime, string asr)
        {
            _poll.Stop();
            _voiceActive = false;
            string said = !string.IsNullOrEmpty(ime) ? ime : asr;
            Match a = Top(ime), b = Top(asr);
            Match top = a;
            string heard = ime;
            if (b != null && (a == null || b.Score > a.Score + 0.02)) { top = b; heard = asr; }
            Log.Write("finalize ime='" + ime + "' asr='" + asr + "' top=" + (top == null ? "none" : top.Cmd.Name + " " + top.Score.ToString("0.000")));

            if (_pending != null && AnswerPending(ime, asr)) return;
            _pending = null;

            Command chain = Chain(ime) ?? Chain(asr);
            if (chain != null) { Run(chain); return; }

            double need = S.AutoThreshold / 100.0;
            if (top != null && top.Score >= need) { Run(top.Cmd); return; }

            // 没到自动执行的分数：聊天窗口里多半是在听写，其他地方短句像指令就问一句。
            bool chat = Native.ProcessNameOfPid(PidOf(_prevWindow)).StartsWith("weixin", StringComparison.OrdinalIgnoreCase);
            bool commandLike = TextUtil.LooksLikeCommand(said);
            if (top != null && (commandLike || top.Score >= 0.70) && !(chat && !commandLike))
            {
                List<Match> ms = Store.Match(heard, 3);
                Pending p = new Pending();
                p.Heard = heard;
                p.Learn = true;
                foreach (Match m in ms) p.Options.Add(m.Cmd);
                Ask("你是想" + ms[0].Cmd.Name + "吗？", p, ms);
                return;
            }
            if (commandLike && !chat)
            {
                // 也可能是在听写，别吞掉：问一句，说「是」就把这句话打进去。
                Command type = new Command();
                type.Id = "dyn_type";
                type.Name = "输入：" + said;
                type.Action = "text";
                type.Text = said;
                Pending p = new Pending();
                p.Options.Add(type);
                List<Match> shown = new List<Match>();
                shown.Add(new Match(type, 0));
                Ask("没听懂，要输入这句话吗？", p, shown);
                return;
            }
            if (!S.CommandOnly && S.PasteBack) { PasteBack(said); return; }
            HidePalette(true);
            Flash("没有匹配的指令", 2);
        }

        private static uint PidOf(IntPtr h)
        {
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            return pid;
        }

        /// <summary>「打开微信然后聚焦输入框」：每一段都得是明确的指令才算，否则当普通一句话。</summary>
        private Command Chain(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] parts = Regex.Split(text, "然后|接着|之后再|完了再");
            if (parts.Length < 2 || parts.Length > 4) return null;
            Command seq = new Command();
            seq.Id = "dyn_chain";
            seq.Action = "sequence";
            List<string> names = new List<string>();
            foreach (string part in parts)
            {
                Match m = Top(part);
                if (m == null || m.Score < S.AutoThreshold / 100.0 || Dangerous.Contains(m.Cmd.Id)) return null;
                if (seq.Steps.Count > 0)
                {
                    Command w = new Command();
                    w.Action = "wait";
                    w.Target = "450";
                    seq.Steps.Add(w);
                }
                seq.Steps.Add(m.Cmd);
                names.Add(m.Cmd.Name);
            }
            seq.Name = string.Join("，然后", names.ToArray());
            return seq;
        }

        private void Ask(string question, Pending p, List<Match> shown)
        {
            _pending = p;
            Log.Write("ask: " + question);
            Palette.SetCandidates(shown ?? new List<Match>());
            Palette.SetStatus(question, 2);
            Palette.SetFooter(p.Options.Count > 1 ? "说「是」或「第二个」· 说「不是」取消 · 回车确认 · Esc 取消" : "说「是」执行 · 说「不是」取消 · 回车确认 · Esc 取消");
            Speaker.Say(question);
            _pendingTimer.Stop();
            _pendingTimer.Start();
        }

        private static readonly Regex YesWords = new Regex("^(是|是的|是啊|对|对的|对啊|好|好的|嗯|确认|确定|执行|可以|行|没错|就是|要)$");
        private static readonly Regex NoWords = new Regex("^(不是|不对|不要|不用|别|取消|算了|不|错了|没有)$");
        private static readonly Regex PickWord = new Regex("第?([一二三123])个?$");

        /// <summary>用户回答了上一句追问就返回 true；说的是别的就返回 false，当新指令处理。</summary>
        private bool AnswerPending(string ime, string asr)
        {
            Pending p = _pending;
            foreach (string raw in new string[] { ime, asr })
            {
                if (string.IsNullOrEmpty(raw)) continue;
                string n = TextUtil.Normalize(raw);
                if (YesWords.IsMatch(n)) { Accept(p, 0); return true; }
                if (NoWords.IsMatch(n))
                {
                    _pending = null;
                    HidePalette(true);
                    Reply("好的，取消了", 0);
                    return true;
                }
                System.Text.RegularExpressions.Match m = PickWord.Match(n);
                if (m.Success && n.Length <= 3)
                {
                    int i = "一二三".IndexOf(m.Groups[1].Value, StringComparison.Ordinal);
                    if (i < 0) i = int.Parse(m.Groups[1].Value) - 1;
                    if (i < p.Options.Count) { Accept(p, i); return true; }
                }
            }
            return false;
        }

        private void Accept(Pending p, int i)
        {
            _pending = null;
            _pendingTimer.Stop();
            Command c = p.Options[i];
            if (p.Learn && !string.IsNullOrEmpty(p.Heard)) Store.Learn(p.Heard, c);
            Execute(c);
        }

        /// <summary>所有执行都走这里：关机这类不可逆的先问一句。</summary>
        private void Run(Command cmd)
        {
            if (Dangerous.Contains(cmd.Id))
            {
                Pending p = new Pending();
                p.Options.Add(cmd);
                List<Match> shown = new List<Match>();
                shown.Add(new Match(cmd, 1));
                Ask("确定要" + cmd.Name + "吗？", p, shown);
                return;
            }
            Execute(cmd);
        }

        private void Execute(Command cmd)
        {
            bool quiet = cmd.Op == "stopspeech";
            if (cmd.Op == "again")
            {
                if (_lastCmd == null) { HidePalette(true); Reply("还没有可以重复的指令", 2); return; }
                cmd = _lastCmd;
            }
            else if (cmd.Op == "repeatspeech")
            {
                HidePalette(true);
                Reply(Speaker.Last.Length > 0 ? Speaker.Last : "刚才没说什么", 0);
                return;
            }
            else if (!quiet && !Dangerous.Contains(cmd.Id)) _lastCmd = cmd;   // 「再来一次」不能绕过关机确认

            Log.Write("execute " + cmd.Id + " " + cmd.Name);
            Store.BumpUse(cmd);
            HidePalette(true);
            if (quiet) return;
            bool answers = cmd.Op.StartsWith("ask:", StringComparison.Ordinal);
            string err;
            _injecting = true;
            try { err = Executor.Run(cmd, _prevWindow, S.RestoreFocus); }
            finally { _injecting = false; }
            if (err != null)
            {
                Log.Warn("execute failed: " + err);
                Reply(cmd.Name + "失败：" + err, 2);
            }
            else if (cmd.Op.StartsWith("pinned:", StringComparison.Ordinal)) Reply(WindowOps.LastOpened, 1);
            else if (!answers) Flash("✓ " + cmd.Name, 1);
        }

        private void PasteBack(string text)
        {
            Log.Write("paste back " + text.Length + " chars");
            HidePalette(true);
            if (S.RestoreFocus && _prevWindow != IntPtr.Zero)
            {
                Native.ForceForeground(_prevWindow);
                Thread.Sleep(90);
            }
            Native.SendText(text);
        }

        private void OnSubmit(object sender, EventArgs e)
        {
            if (_pending != null)
            {
                Match sel = Palette.Selected;
                int i = sel == null ? 0 : Math.Max(0, _pending.Options.IndexOf(sel.Cmd));
                Accept(_pending, i);
                return;
            }
            string t = Palette.TextValue.Trim();
            if (t.Length == 0) return;
            if (_manualMode)
            {
                List<Match> ms = Store.Match(t, S.ShowCandidates);
                if (ms.Count > 0 && ms[0].Score * 100 >= 55) { Run(ms[0].Cmd); return; }
                Palette.SetStatus("没有匹配的指令", 2);
                return;
            }
            Decide(t, null);
        }

        private void OnCancel(object sender, EventArgs e)
        {
            _voiceActive = false;
            _manualMode = false;
            _pending = null;
            _pendingTimer.Stop();
            Speaker.Stop();
            _poll.Stop();
            HidePalette(true);
            Log.Write("cancelled");
        }

        private void HidePalette(bool restoreFocus)
        {
            Tap.End();
            _poll.Stop();
            _guard.Stop();
            _voiceActive = false;
            _manualMode = false;
            if (Palette.Visible) Palette.Hide();
            if (restoreFocus && S.RestoreFocus && _prevWindow != IntPtr.Zero && Native.IsWindow(_prevWindow))
            {
                Native.ForceForeground(_prevWindow);
            }
        }

        // ------------------------------------------------------------------ manual

        private void BeginManual()
        {
            _manualMode = true;
            _voiceActive = false;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != Palette.Handle) _prevWindow = fg;
            Store.InDouyin = WindowOps.IsDouyin(_prevWindow);
            Palette.ResetForSession();
            Palette.SetStatus("输入指令…", 0);
            Palette.SetCandidates(Store.Suggest(S.ShowCandidates));
            Palette.SetFooter("回车执行 · Esc 取消 · F1 指令库");
            Palette.PlaceOnScreen();
            Palette.Show();
            bool got = Native.EnsureForeground(Palette.Handle);
            Palette.FocusInput();
            _guardTicks = 0;
            _guard.Start();
            Log.Write("manual begin, prev=" + _prevWindow + " '" + Native.WindowTitle(_prevWindow) + "' focus=" + got
                + " fg='" + Native.WindowTitle(Native.GetForegroundWindow()) + "'");
        }

        // ------------------------------------------------------------------ library

        private void ShowLibrary()
        {
            if (_library != null && !_library.IsDisposed)
            {
                _library.Show();
                Native.EnsureForeground(_library.Handle);
                return;
            }
            _library = new LibraryForm(Store, Apps);
            _library.Icon = LoadAppIcon(new Size(32, 32));
            _library.FormClosed += delegate { _library = null; };
            _library.Show();
            Native.EnsureForeground(_library.Handle);
        }

        // ------------------------------------------------------------------ exit

        public void OpenLibraryAtStartup()
        {
            ShowLibrary();
        }

        /// <summary>Test hook: open the palette as if the hotkey had been pressed.</summary>
        public void OpenPaletteAtStartup()
        {
            BeginManual();
        }

        private void Shutdown()
        {
            try
            {
                if (_hookId != IntPtr.Zero) Native.UnhookWindowsHookEx(_hookId);
                if (_hookThreadId != 0) Native.PostThreadMessage(_hookThreadId, 0x0012 /*WM_QUIT*/, IntPtr.Zero, IntPtr.Zero);
                if (Tray != null) { Tray.Visible = false; Tray.Dispose(); }
                ActivityLog.Flush();
                Asr.Stop();
            }
            catch { }
            ExitThread();
        }
    }

    internal static class Program
    {
        /// <summary>测试用：不接遥控器，盯一份假日志模拟按键。</summary>
        public static string SayAllLogOverride;

        /// <summary>Diagnostic: resolve one utterance and dump the ranking to the log.</summary>
        private static void RunMatchProbe(string text)
        {
            try
            {
                AppIndex apps = new AppIndex();
                apps.Load();
                CommandStore store = new CommandStore(apps);
                Executor.Apps = apps;
                store.InDouyin = Environment.GetEnvironmentVariable("SAYALLVOICE_DOUYIN") == "1";
                List<Match> ms = store.Match(text, 6);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("LOOKS-LIKE-COMMAND=" + TextUtil.LooksLikeCommand(text));
                sb.AppendLine("MATCH PROBE: [" + text + "] norm=[" + TextUtil.Normalize(text) + "]");
                for (int i = 0; i < ms.Count; i++)
                    sb.AppendLine("  " + (i + 1) + ". " + ms[i].Score.ToString("0.000") + "  " + ms[i].Cmd.Display + "  (" + ms[i].Cmd.Action + " " + ms[i].Cmd.Id + ")");
                if (ms.Count == 0) sb.AppendLine("  (no match)");
                File.AppendAllText(Settings.LogPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Settings.LogPath, "probe failed: " + ex + "\r\n", Encoding.UTF8); } catch { }
            }
        }

        /// <summary>Diagnostic: run one utterance against the current foreground window.</summary>
        private static void RunExecProbe(string text)
        {
            try
            {
                Native.SetProcessDPIAware();
                AppIndex apps = new AppIndex();
                apps.Load();
                CommandStore store = new CommandStore(apps);
                Executor.Apps = apps;
                List<Match> ms = store.Match(text, 1);
                if (ms.Count == 0) { Log.Write("exec probe: no match for " + text); return; }
                Executor.Answer = delegate(string a) { Log.Write("answer: " + a); };
                store.InDouyin = WindowOps.IsDouyin(Native.GetForegroundWindow());
                ms = store.Match(text, 1);
                string err = Executor.Run(ms[0].Cmd, Native.GetForegroundWindow(), true);
                Log.Write("exec probe [" + text + "] -> " + ms[0].Cmd.Id + " err=" + (err ?? "none"));
            }
            catch (Exception ex) { Log.Warn("exec probe failed: " + ex); }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            bool wantLibrary = false;
            bool wantPalette = false;
            string matchText = null;
            string execText = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--library", StringComparison.OrdinalIgnoreCase)) wantLibrary = true;
                if (string.Equals(args[i], "--palette", StringComparison.OrdinalIgnoreCase)) wantPalette = true;
                if (string.Equals(args[i], "--match", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) matchText = args[i + 1];
                if (string.Equals(args[i], "--exec", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) execText = args[i + 1];
                if (string.Equals(args[i], "--sayall-log", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) SayAllLogOverride = args[i + 1];
            }
            if (matchText != null) { RunMatchProbe(matchText); return; }
            if (execText != null) { RunExecProbe(execText); return; }
            foreach (string a in args)
                if (string.Equals(a, "--pins", StringComparison.OrdinalIgnoreCase)) { Native.SetProcessDPIAware(); WindowOps.LogPinned(); return; }
            try { Native.SetProcessDPIAware(); } catch { }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Log.Warn("UI exception: " + e.Exception);
                try { MessageBox.Show(e.Exception.Message, "SayAll 语音指令", MessageBoxButtons.OK, MessageBoxIcon.Warning); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Warn("fatal: " + e.ExceptionObject);
            };
            bool created;
            using (Mutex m = new Mutex(true, "SayAllVoice_SingleInstance_v1", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                TrayApp app = new TrayApp();
                if (wantLibrary) app.OpenLibraryAtStartup();
                if (wantPalette) app.OpenPaletteAtStartup();
                Application.Run(app);
                GC.KeepAlive(m);
            }
        }
    }
}
