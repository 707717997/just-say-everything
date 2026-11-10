using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SayAllVoice
{
    /// <summary>
    /// 记录用户平时在用什么，用来决定该做哪些语音指令。只在本机保存，30 天后自动删。
    /// 记的是：前台程序和窗口标题（每 5 秒看一次）、窗口切换次数、带 Ctrl/Alt/Win 的快捷键次数。
    /// 不记打字内容、不截图、不录音。托盘菜单可以随时关。
    /// </summary>
    public static class ActivityLog
    {
        private const int SampleSec = 5;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, int> Seconds = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> Combos = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> Hops = new Dictionary<string, int>();
        private static System.Threading.Timer _timer;
        private static string _proc = "";
        private static int _ticks;
        public static bool Enabled = true;

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO i);

        public static string Dir { get { return Path.Combine(Settings.DataDir, "activity"); } }

        public static void Start()
        {
            if (_timer != null) return;
            Directory.CreateDirectory(Dir);
            Prune();
            _timer = new System.Threading.Timer(Tick, null, SampleSec * 1000, SampleSec * 1000);
        }

        private static int IdleSec()
        {
            LASTINPUTINFO i = new LASTINPUTINFO();
            i.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref i)) return 0;
            return (int)((uint)Environment.TickCount - i.dwTime) / 1000;
        }

        private static void Tick(object state)
        {
            if (!Enabled) return;
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                string p = Native.ProcessNameOfPid(pid);
                string t = Native.WindowTitle(h);
                t = Regex.Replace(t, "^\\(\\d+\\)\\s*", "");
                if (t.Length > 60) t = t.Substring(0, 60);
                string key = p + "\t" + t + "\t" + (IdleSec() >= 120 ? "1" : "0");
                lock (Gate)
                {
                    int n;
                    Seconds.TryGetValue(key, out n);
                    Seconds[key] = n + SampleSec;
                    if (p.Length > 0 && p != _proc)
                    {
                        if (_proc.Length > 0)
                        {
                            string hop = _proc + ">" + p;
                            Hops.TryGetValue(hop, out n);
                            Hops[hop] = n + 1;
                        }
                        _proc = p;
                    }
                }
                if (++_ticks * SampleSec >= 60) { _ticks = 0; Flush(); }
            }
            catch (Exception ex) { Log.Warn("activity: " + ex.Message); }
        }

        /// <summary>在键盘钩子里调用，要快。普通打字（没按 Ctrl/Alt/Win）在这里就返回了。</summary>
        public static void Key(int vk)
        {
            if (!Enabled) return;
            bool ctrl = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool alt = (Native.GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool win = (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0;
            if (!ctrl && !alt && !win) return;
            string k = KeyName(vk);
            if (k == null) return;
            bool shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
            string combo = (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (win ? "Win+" : "") + (shift ? "Shift+" : "") + k;
            lock (Gate)
            {
                string key = _proc + "\t" + combo;
                int n;
                Combos.TryGetValue(key, out n);
                Combos[key] = n + 1;
            }
        }

        private static string KeyName(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            switch (vk)
            {
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x08: return "Backspace";
                case 0x2E: return "Delete";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2C: return "PrintScreen";
            }
            return null;
        }

        public static void Flush()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                string m = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                lock (Gate)
                {
                    foreach (KeyValuePair<string, int> kv in Seconds)
                    {
                        string[] a = kv.Key.Split('\t');
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["m"] = m; d["p"] = a[0]; d["t"] = a[1]; d["idle"] = a[2] == "1"; d["s"] = kv.Value;
                        sb.AppendLine(Json.Stringify(d, false));
                    }
                    foreach (KeyValuePair<string, int> kv in Combos)
                    {
                        string[] a = kv.Key.Split('\t');
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["m"] = m; d["p"] = a[0]; d["k"] = a[1]; d["n"] = kv.Value;
                        sb.AppendLine(Json.Stringify(d, false));
                    }
                    foreach (KeyValuePair<string, int> kv in Hops)
                    {
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["m"] = m; d["hop"] = kv.Key; d["n"] = kv.Value;
                        sb.AppendLine(Json.Stringify(d, false));
                    }
                    Seconds.Clear(); Combos.Clear(); Hops.Clear();
                }
                if (sb.Length > 0)
                    File.AppendAllText(Path.Combine(Dir, DateTime.Now.ToString("yyyy-MM-dd") + ".jsonl"), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Log.Warn("activity flush: " + ex.Message); }
        }

        private static void Prune()
        {
            try
            {
                foreach (string f in Directory.GetFiles(Dir, "*.jsonl"))
                    if ((DateTime.Now - File.GetLastWriteTime(f)).TotalDays > 30) File.Delete(f);
            }
            catch { }
        }
    }
}
