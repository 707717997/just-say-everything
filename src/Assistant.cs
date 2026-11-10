using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace SayAllVoice
{
    /// <summary>问答类指令：答案只读本机信息，不改动任何东西。</summary>
    public static class Assistant
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME { public uint Low; public uint High; public ulong V { get { return ((ulong)High << 32) | Low; } } }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength; public uint dwMemoryLoad;
            public ulong ullTotalPhys; public ulong ullAvailPhys;
            public ulong ullTotalPageFile; public ulong ullAvailPageFile;
            public ulong ullTotalVirtual; public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        private static readonly string[] Week = { "日", "一", "二", "三", "四", "五", "六" };

        public static string Answer(string what, IntPtr target)
        {
            DateTime now = DateTime.Now;
            if (what.StartsWith("read:", StringComparison.Ordinal))
            {
                int n;
                int.TryParse(what.Substring(5), out n);
                return ReadSession(n);
            }
            switch (what)
            {
                case "time": return "现在 " + now.Hour + " 点 " + now.Minute + " 分";
                case "date": return "今天 " + now.Month + " 月 " + now.Day + " 日，星期" + Week[(int)now.DayOfWeek];
                case "window":
                    {
                        string t = Native.WindowTitle(target);
                        return t.Length == 0 ? "现在在桌面" : "当前窗口是 " + Clean(t, 40);
                    }
                case "pins":
                    {
                        string[] names = WindowOps.PinnedTitles();
                        if (names.Length == 0) return "现在没有置顶的会话";
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i < names.Length && i < 5; i++) sb.Append("第" + (i + 1) + "个，" + names[i] + "。");
                        return sb.ToString();
                    }
                case "status": return Status();
                case "clipboard":
                    {
                        string t = "";
                        try { if (Clipboard.ContainsText()) t = Clipboard.GetText(); } catch { }
                        return t.Length == 0 ? "剪贴板里没有文字" : "剪贴板内容：" + Clean(t, 200);
                    }
                case "about":
                    return "我是 " + Origin.Name + "，" + Origin.Version + " 版，作者 " + Origin.Author + "，在 GitHub 开源。按住说一句，电脑就去做。";
                case "help":
                    return "你可以说打开程序、回到桌面、切到上一个程序、聚焦输入框、打开第二个会话、读第二个会话、现在几点、电脑状态、音量加、再来一次。两件事可以用然后连起来说。";
            }
            return "这个问题我还不会";
        }

        private static string Status()
        {
            FILETIME i0, k0, u0, i1, k1, u1;
            GetSystemTimes(out i0, out k0, out u0);
            Thread.Sleep(250);
            GetSystemTimes(out i1, out k1, out u1);
            double total = (k1.V - k0.V) + (u1.V - u0.V);
            int cpu = total <= 0 ? 0 : (int)Math.Round(100.0 * (1 - (i1.V - i0.V) / total));
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            GlobalMemoryStatusEx(ref m);
            long free = new DriveInfo("C").AvailableFreeSpace >> 30;
            return "CPU 占用百分之 " + cpu + "，内存占用百分之 " + m.dwMemoryLoad + "，C 盘还剩 " + free + " G";
        }

        /// <summary>读置顶第 n 个会话最后一条回复。读的是会话记录文件，不碰 Claude 窗口。</summary>
        private static string ReadSession(int n)
        {
            string title;
            string text = WindowOps.LastReply(n, out title);
            if (title == null) return "没有第 " + n + " 个置顶会话";
            if (string.IsNullOrEmpty(text)) return title + " 还没有可读的回复";
            string c = Clean(text, 220);
            return title + " 最新回复：" + c + (text.Length > 230 ? "。后面还有，到会话里看全文。" : "");
        }

        /// <summary>去掉不适合念出来的 Markdown、链接和路径。</summary>
        public static string Clean(string s, int max)
        {
            s = Regex.Replace(s, "```[\\s\\S]*?```", "（代码略）");
            s = Regex.Replace(s, "\\[([^\\]]*)\\]\\([^)]*\\)", "$1");
            s = Regex.Replace(s, "https?://\\S+", "链接");
            s = Regex.Replace(s, "[A-Za-z]:\\\\\\S+", "某个路径");
            s = Regex.Replace(s, "^\\s*\\|.*$", "", RegexOptions.Multiline);
            s = Regex.Replace(s, "[#*`|>_]+", " ");
            s = Regex.Replace(s, "\\s+", " ").Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }
    }
}
