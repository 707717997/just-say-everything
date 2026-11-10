using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace SayAllVoice
{
    /// <summary>
    /// 窗口类指令：切到上一个程序、回桌面、聚焦输入框、打开 Claude 置顶会话。
    /// 都直接操作窗口，不靠模拟 Alt+Tab 这类快捷键，所以结果是确定的。
    ///
    /// Electron/Chromium 窗口一律不走 UI 自动化：.NET Framework 的 UIA 客户端读它们要十几秒，
    /// 2026-11-07 两次测试期间 Claude 主进程无记录地消失，所以置顶会话走官方深链，
    /// 输入框走窗口几何位置。
    /// </summary>
    public static class WindowOps
    {
        private static readonly string[] Browsers = { "chrome", "msedge", "firefox", "qqbrowser", "360se", "360chrome", "brave", "opera" };

        // ------------------------------------------------------------ 窗口枚举

        private static bool IsDesktopClass(string cls)
        {
            return cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
        }

        /// <summary>和 Alt+Tab 列表同一口径：可见、没被隐藏、不是工具窗口、不是常驻置顶的小挂件。</summary>
        private static bool IsAppWindow(IntPtr h)
        {
            if (!Native.IsWindowVisible(h)) return false;
            if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return false;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST)) != 0) return false;
            int cloaked;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return false;
            if (Native.WindowTitle(h).Length == 0) return false;
            return !IsDesktopClass(Native.ClassName(h));
        }

        /// <summary>按 Z 序（最近用过的在前）列出所有程序窗口。</summary>
        public static List<IntPtr> AppWindows()
        {
            List<IntPtr> r = new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (IsAppWindow(h)) r.Add(h);
                return true;
            }, IntPtr.Zero);
            return r;
        }

        private static uint PidOf(IntPtr h)
        {
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            return pid;
        }

        private static string ProcessNameOf(IntPtr h)
        {
            return Native.ProcessNameOfPid(PidOf(h));
        }

        public static void Activate(IntPtr h)
        {
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.ForceForeground(h);
        }

        private static IntPtr RootOf(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h)) return IntPtr.Zero;
            IntPtr r = Native.GetAncestor(h, Native.GA_ROOT);
            return r == IntPtr.Zero ? h : r;
        }

        // ------------------------------------------------------------ 浏览器标签页

        private static bool IsBrowser(IntPtr h)
        {
            return Array.IndexOf(Browsers, ProcessNameOf(h).ToLowerInvariant()) >= 0;
        }

        private static bool TitleHas(IntPtr h, string key)
        {
            return Native.WindowTitle(h).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>当前窗口是抖音网页。创作者后台标题也带「抖音」，那里的按键不是这一套。</summary>
        public static bool IsDouyin(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h) || !IsBrowser(RootOf(h))) return false;
            string t = Native.WindowTitle(RootOf(h));
            return t.IndexOf("抖音", StringComparison.Ordinal) >= 0 && t.IndexOf("创作", StringComparison.Ordinal) < 0;
        }

        /// <summary>
        /// 在已开的浏览器窗口里找标题含 key 的标签页并切过去。窗口标题就是当前标签的标题，
        /// 所以用 Ctrl+Tab 转一圈看标题，不走 UI 自动化。转完没找到时正好回到原来的标签。
        /// </summary>
        public static bool FindTab(string key)
        {
            List<IntPtr> browsers = new List<IntPtr>();
            foreach (IntPtr h in AppWindows())
            {
                if (!IsBrowser(h)) continue;
                if (TitleHas(h, key)) { Activate(h); Log.Write("tab found on top: " + key); return true; }
                browsers.Add(h);
            }
            foreach (IntPtr w in browsers)
            {
                Activate(w);
                Thread.Sleep(80);
                if (Native.GetForegroundWindow() != w) continue;
                string start = Native.WindowTitle(w);
                for (int i = 0; i < 40; i++)
                {
                    if (Native.GetForegroundWindow() != w) return false;   // 用户切走了就别再按键
                    Executor.SendCombo("ctrl+tab");
                    Thread.Sleep(90);
                    if (TitleHas(w, key)) { Log.Write("tab found after " + (i + 1) + " steps: " + key); return true; }
                    if (Native.WindowTitle(w) == start) break;
                }
            }
            return false;
        }

        private static string PanelTitle(string url)
        {
            try
            {
                System.Net.HttpWebRequest q = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                q.Proxy = null;
                q.Timeout = 1500;
                q.ReadWriteTimeout = 1500;
                using (System.Net.WebResponse r = q.GetResponse())
                using (StreamReader sr = new StreamReader(r.GetResponseStream(), System.Text.Encoding.UTF8))
                {
                    char[] buf = new char[8192];
                    int n = sr.Read(buf, 0, buf.Length);
                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                        new string(buf, 0, n), "<title>\\s*([^<]+?)\\s*</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!m.Success) return "";
                    return System.Text.RegularExpressions.Regex.Split(m.Groups[1].Value, "\\s+[·—|-]\\s+")[0].Trim();
                }
            }
            catch (Exception ex) { Log.Warn("panel title " + url + ": " + ex.Message); return null; }
        }

        /// <summary>打开网页：已经开着就切过去，没开才新开。key 为空时是本机面板，用它自己的网页标题找。</summary>
        public static string OpenTab(string url, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                key = PanelTitle(url);
                if (key == null) return "面板没有响应";
            }
            if (key.Length > 0 && FindTab(key)) return null;
            Log.Write("tab not found, opening " + url);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return null;
        }

        /// <summary>按抖音网页的快捷键。当前不在抖音就先找到抖音的标签页。</summary>
        public static string DouyinKey(string key, IntPtr current)
        {
            if (IsDouyin(current)) Activate(RootOf(current));
            else if (!FindTab("抖音")) return "抖音没有打开";
            Thread.Sleep(70);
            Executor.SendCombo(key);
            return null;
        }

        // ------------------------------------------------------------ 切换 / 桌面

        /// <summary>切到上一个程序：Z 序里排在当前窗口之后、属于别的进程的第一个窗口。</summary>
        public static string SwitchPrevious(IntPtr current)
        {
            IntPtr cur = RootOf(current);
            uint curPid = cur == IntPtr.Zero ? 0 : PidOf(cur);
            IntPtr sameProcess = IntPtr.Zero;
            foreach (IntPtr h in AppWindows())
            {
                if (h == cur) continue;
                if (PidOf(h) != curPid)
                {
                    Log.Write("switch previous -> '" + Native.WindowTitle(h) + "'");
                    Activate(h);
                    return null;
                }
                if (sameProcess == IntPtr.Zero) sameProcess = h;
            }
            if (sameProcess == IntPtr.Zero) return "没有可切换的程序";
            Activate(sameProcess);
            return null;
        }

        /// <summary>回到桌面。Win+D 是开关，已经在桌面时再按会把窗口全翻回来，所以这里先判断。</summary>
        public static string ShowDesktop(IntPtr current)
        {
            if (IsDesktopClass(Native.ClassName(Native.GetForegroundWindow()))) return null;
            Executor.SendCombo("win+d");
            return null;
        }

        /// <summary>打开程序时，已经开着窗口的直接切过去，不再起第二份。</summary>
        public static bool ActivateRunning(string exePath)
        {
            string name = Path.GetFileNameWithoutExtension(exePath);
            foreach (IntPtr h in AppWindows())
            {
                if (string.Equals(ProcessNameOf(h), name, StringComparison.OrdinalIgnoreCase))
                {
                    Activate(h);
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------ 点击

        private static void ClickAt(int x, int y)
        {
            Native.POINT old;
            bool had = Native.GetCursorPos(out old);
            Native.SetCursorPos(x, y);
            Thread.Sleep(40);
            Native.SendMouseClick();
            Thread.Sleep(40);
            if (had) Native.SetCursorPos(old.X, old.Y);
        }

        /// <summary>
        /// 只在目标窗口确实在前台、且这个点确实属于它时才点。语音指令是在用户别的操作中间
        /// 执行的，窗口随时可能被换掉，点错地方的后果比「没点上」严重得多。
        /// </summary>
        private static bool SafeClick(IntPtr top, int x, int y)
        {
            for (int i = 0; i < 3 && Native.GetForegroundWindow() != top; i++)
            {
                Activate(top);
                Thread.Sleep(80);
            }
            Native.POINT p;
            p.X = x;
            p.Y = y;
            if (Native.GetForegroundWindow() != top || RootOf(Native.WindowFromPoint(p)) != top)
            {
                Log.Warn("click skipped: target not under point");
                return false;
            }
            ClickAt(x, y);
            return true;
        }

        // ------------------------------------------------------------ 聚焦输入框

        /// <summary>
        /// 聊天类应用的输入框固定贴着窗口底部。widthRatio 是横向比例，bottomDip 是离窗口
        /// 底边的距离（按 100% 缩放的像素），取自实测的控件位置。
        /// </summary>
        private static string ClickByLayout(IntPtr top, double widthRatio, int bottomDip)
        {
            Native.RECT r;
            if (!Native.GetWindowRect(top, out r)) return "读不到窗口位置";
            double scale = Native.DpiScalePercent() / 100.0;
            int x = r.Left + (int)((r.Right - r.Left) * widthRatio);
            int y = r.Bottom - (int)(bottomDip * scale);
            return SafeClick(top, x, y) ? null : "窗口被挡住了，没点到输入框";
        }

        public static string FocusInput(IntPtr target)
        {
            IntPtr top = RootOf(target);
            if (top == IntPtr.Zero || IsDesktopClass(Native.ClassName(top))) return "当前没有打开的程序窗口";
            Activate(top);

            string proc = ProcessNameOf(top).ToLowerInvariant();
            if (Array.IndexOf(Browsers, proc) >= 0)
            {
                Executor.SendCombo("ctrl+l");
                return null;
            }
            if (proc == "weixin" || proc == "wechat") return ClickByLayout(top, 0.65, 100);
            if (Native.ClassName(top) == "Chrome_WidgetWin_1") return ClickByLayout(top, 0.45, 72);
            return FocusByAutomation(top);
        }

        /// <summary>普通桌面程序：UI 自动化找最靠下的输入框。放在 MTA 线程并限时，卡住的程序拖不死指令。</summary>
        private static string FocusByAutomation(IntPtr top)
        {
            AutomationElement box = null;
            Thread worker = new Thread(delegate()
            {
                try { box = FindInputBox(AutomationElement.FromHandle(top)); }
                catch (Exception ex) { Log.Warn("find input: " + ex.Message); }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            worker.Join(2500);
            if (box == null) return "这个窗口里没找到输入框";

            Rect r = box.Cached.BoundingRectangle;
            return SafeClick(top, (int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2)) ? null : "窗口被挡住了，没点到输入框";
        }

        private static AutomationElement FindInputBox(AutomationElement root)
        {
            CacheRequest cr = new CacheRequest();
            cr.Add(AutomationElement.IsEnabledProperty);
            cr.Add(AutomationElement.BoundingRectangleProperty);
            AutomationElementCollection edits;
            using (cr.Activate())
            {
                edits = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            }
            AutomationElement best = null;
            double bestBottom = double.MinValue;
            foreach (AutomationElement e in edits)
            {
                if (!e.Cached.IsEnabled) continue;
                Rect r = e.Cached.BoundingRectangle;
                if (r.IsEmpty || double.IsInfinity(r.X) || r.Width < 60 || r.Height < 8) continue;
                if (r.Bottom > bestBottom) { bestBottom = r.Bottom; best = e; }
            }
            return best;
        }

        // ------------------------------------------------------------ Claude 置顶会话

        private class PinnedSession
        {
            public string Id;
            public string Title;
            public string Cli;
            public double LastActivity;
        }

        /// <summary>
        /// 从 Claude 的会话文件读置顶列表，顺序同左栏（最近活动在前）。
        /// 展开的子会话带 spawnedFrom，不计入，这样「第三个」不随展开收起变化。
        /// </summary>
        private static List<PinnedSession> LoadPinned()
        {
            List<PinnedSession> r = new List<PinnedSession>();
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Claude\claude-code-sessions");
            if (!Directory.Exists(root)) return r;
            foreach (string f in Directory.GetFiles(root, "local_*.json", SearchOption.AllDirectories))
            {
                try
                {
                    string text = File.ReadAllText(f);
                    if (text.IndexOf("\"isStarred\":true", StringComparison.Ordinal) < 0) continue;
                    Dictionary<string, object> d = J.Obj(Json.Parse(text));
                    if (d == null || J.Bool(d, "isArchived", false) || d.ContainsKey("spawnedFrom")) continue;
                    PinnedSession s = new PinnedSession();
                    s.Id = J.Str(d, "sessionId", "");
                    s.Title = J.Str(d, "title", "");
                    s.Cli = J.Str(d, "lastAssistantCliSessionId", J.Str(d, "cliSessionId", ""));
                    object t;
                    s.LastActivity = d.TryGetValue("lastActivityAt", out t) && t != null ? Convert.ToDouble(t) : 0;
                    if (s.Id.Length > 0) r.Add(s);
                }
                catch (Exception ex) { Log.Warn("pinned read " + Path.GetFileName(f) + ": " + ex.Message); }
            }
            r.Sort(delegate(PinnedSession a, PinnedSession b) { return b.LastActivity.CompareTo(a.LastActivity); });
            return r;
        }

        public static void LogPinned()
        {
            List<PinnedSession> ps = LoadPinned();
            for (int i = 0; i < ps.Count; i++) Log.Write("pinned " + (i + 1) + ": " + ps[i].Title + " " + ps[i].Id);
            if (ps.Count == 0) Log.Write("pinned: none");
        }

        /// <summary>最近一次语音打开的会话名，用来念给用户听：序号会随活动变，听到名字才知道到了哪。</summary>
        public static string LastOpened = "";

        public static string[] PinnedTitles()
        {
            List<string> r = new List<string>();
            foreach (PinnedSession s in LoadPinned()) r.Add(s.Title);
            return r.ToArray();
        }

        /// <summary>置顶第 n 个会话的最后一条文字回复，从会话记录文件读，不碰 Claude 窗口。</summary>
        public static string LastReply(int n, out string title)
        {
            title = null;
            List<PinnedSession> ps = LoadPinned();
            if (n < 1 || ps.Count < n) return null;
            PinnedSession s = ps[n - 1];
            title = s.Title;
            if (string.IsNullOrEmpty(s.Cli)) return "";
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".claude\projects");
            if (!Directory.Exists(root)) return "";
            foreach (string dir in Directory.GetDirectories(root))
            {
                string f = Path.Combine(dir, s.Cli + ".jsonl");
                if (File.Exists(f)) return LastAssistantText(f);
            }
            return "";
        }

        private static string LastAssistantText(string file)
        {
            byte[] buf;
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long take = Math.Min(fs.Length, 2L * 1024 * 1024);
                fs.Seek(-take, SeekOrigin.End);
                buf = new byte[take];
                int got = 0, n;
                while (got < buf.Length && (n = fs.Read(buf, got, buf.Length - got)) > 0) got += n;
            }
            string[] lines = System.Text.Encoding.UTF8.GetString(buf).Split('\n');
            for (int i = lines.Length - 1; i >= 1; i--)   // 第 0 行可能只读到半截
            {
                string l = lines[i];
                if (l.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0) continue;
                Dictionary<string, object> d = J.Obj(Json.TryParse(l));
                if (d == null || J.Str(d, "type", "") != "assistant") continue;
                object mo;
                if (!d.TryGetValue("message", out mo)) continue;
                Dictionary<string, object> m = J.Obj(mo);
                if (m == null) continue;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (object o in J.ObjList(m, "content"))
                {
                    Dictionary<string, object> c = J.Obj(o);
                    if (c != null && J.Str(c, "type", "") == "text") sb.Append(J.Str(c, "text", ""));
                }
                if (sb.ToString().Trim().Length > 0) return sb.ToString().Trim();
            }
            return "";
        }

        public static string OpenPinned(int n)
        {
            List<PinnedSession> ps = LoadPinned();
            if (ps.Count < n) return "置顶只有 " + ps.Count + " 个会话";
            PinnedSession s = ps[n - 1];
            LastOpened = s.Title;

            // Claude 自己的深链：窗口收在托盘时也能打开，不动鼠标。链接靠第二实例转交，
            // 以 Claude 日志里真正切到目标会话为准，没切到就重发。
            // 注意：本程序要从普通用户目录运行（build.ps1 -Install 部署到 %USERPROFILE%\SayAllVoice）。
            // 从带沙箱 ACL 的目录启动时进程权限受限，子进程继承后 Claude 第二实例会崩、链接丢失。
            string url = "claude://code/continue?session=" + s.Id + "&source=desktop_action";
            string exe = ClaudeExe();
            if (exe == null) return "找不到 Claude 程序";
            Stopwatch sw = Stopwatch.StartNew();
            bool arrived = FocusedSession() == s.Id;
            int sent = 0;
            while (!arrived && sent < 4)
            {
                sent++;
                ProcessStartInfo psi = new ProcessStartInfo(exe, "\"" + url + "\"");
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi).Dispose();
                for (int i = 0; i < 25 && !arrived; i++)
                {
                    Thread.Sleep(100);
                    arrived = FocusedSession() == s.Id;
                }
            }
            Log.Write("open pinned #" + n + " '" + s.Title + "' arrived=" + arrived + " sent=" + sent + " " + sw.ElapsedMilliseconds + "ms");

            IntPtr w = FindClaudeWindow();
            if (w != IntPtr.Zero && Native.GetForegroundWindow() != w) Activate(w);
            return arrived ? null : "Claude 没有响应，会话没打开";
        }

        /// <summary>claude:// 协议登记的程序路径，Claude 升级后这里会跟着变。</summary>
        private static string ClaudeExe()
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\claude\shell\open\command"))
            {
                string cmd = k == null ? null : k.GetValue("") as string;
                if (string.IsNullOrEmpty(cmd) || cmd[0] != '"') return null;
                int end = cmd.IndexOf('"', 1);
                if (end <= 1) return null;
                string exe = cmd.Substring(1, end - 1);
                return File.Exists(exe) ? exe : null;
            }
        }

        /// <summary>Claude 当前打开的会话，取自它日志里最后一条 setFocusedSession。</summary>
        private static string FocusedSession()
        {
            const string key = "setFocusedSession: sessionId=";
            try
            {
                string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Claude\logs\main.log");
                using (FileStream fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long n = Math.Min(fs.Length, 262144);
                    fs.Seek(-n, SeekOrigin.End);
                    byte[] buf = new byte[n];
                    int got = fs.Read(buf, 0, buf.Length);
                    string text = System.Text.Encoding.UTF8.GetString(buf, 0, got);
                    int at = text.LastIndexOf(key, StringComparison.Ordinal);
                    if (at < 0) return "";
                    int start = at + key.Length;
                    int end = start;
                    while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
                    return text.Substring(start, end - start);
                }
            }
            catch (Exception ex) { Log.Warn("read claude log: " + ex.Message); return ""; }
        }

        private static IntPtr FindClaudeWindow()
        {
            foreach (IntPtr h in AppWindows())
            {
                if (Native.ClassName(h) == "Chrome_WidgetWin_1"
                    && string.Equals(ProcessNameOf(h), "claude", StringComparison.OrdinalIgnoreCase))
                    return h;
            }
            return IntPtr.Zero;
        }
    }
}
