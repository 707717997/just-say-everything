using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SayAllVoice
{
    public static class Executor
    {
        /// <summary>Set once at start-up so "@app:名称" targets can be resolved.</summary>
        public static AppIndex Apps;

        /// <summary>Wired to the tray app so a spoken "帮助" can open the library window.</summary>
        public static Action ShowLibrary;

        /// <summary>问答类指令的回答从这里出去（念出来并显示）。</summary>
        public static Action<string> Answer;

        public class KeySpec
        {
            public ushort Vk;
            public bool Ext;
            public KeySpec(ushort vk, bool ext) { Vk = vk; Ext = ext; }
        }

        private static readonly Dictionary<string, KeySpec> Names = BuildNames();

        private static Dictionary<string, KeySpec> BuildNames()
        {
            Dictionary<string, KeySpec> d = new Dictionary<string, KeySpec>();
            for (char c = 'a'; c <= 'z'; c++) d[c.ToString()] = new KeySpec((ushort)(0x41 + c - 'a'), false);
            for (char c = '0'; c <= '9'; c++) d[c.ToString()] = new KeySpec((ushort)(0x30 + c - '0'), false);
            for (int i = 1; i <= 24; i++) d["f" + i] = new KeySpec((ushort)(0x6F + i), false);

            d["ctrl"] = new KeySpec(0x11, false);
            d["control"] = new KeySpec(0x11, false);
            d["shift"] = new KeySpec(0x10, false);
            d["alt"] = new KeySpec(0x12, false);
            d["menu"] = new KeySpec(0x12, false);
            d["win"] = new KeySpec(0x5B, true);
            d["lwin"] = new KeySpec(0x5B, true);
            d["rwin"] = new KeySpec(0x5C, true);
            d["esc"] = new KeySpec(0x1B, false);
            d["escape"] = new KeySpec(0x1B, false);
            d["tab"] = new KeySpec(0x09, false);
            d["enter"] = new KeySpec(0x0D, false);
            d["return"] = new KeySpec(0x0D, false);
            d["space"] = new KeySpec(0x20, false);
            d["backspace"] = new KeySpec(0x08, false);
            d["back"] = new KeySpec(0x08, false);
            d["delete"] = new KeySpec(0x2E, true);
            d["del"] = new KeySpec(0x2E, true);
            d["insert"] = new KeySpec(0x2D, true);
            d["ins"] = new KeySpec(0x2D, true);
            d["home"] = new KeySpec(0x24, true);
            d["end"] = new KeySpec(0x23, true);
            d["pageup"] = new KeySpec(0x21, true);
            d["pgup"] = new KeySpec(0x21, true);
            d["pagedown"] = new KeySpec(0x22, true);
            d["pgdn"] = new KeySpec(0x22, true);
            d["up"] = new KeySpec(0x26, true);
            d["down"] = new KeySpec(0x28, true);
            d["left"] = new KeySpec(0x25, true);
            d["right"] = new KeySpec(0x27, true);
            d["printscreen"] = new KeySpec(0x2C, true);
            d["prtsc"] = new KeySpec(0x2C, true);
            d["capslock"] = new KeySpec(0x14, false);
            d["numlock"] = new KeySpec(0x90, true);
            d["scrolllock"] = new KeySpec(0x91, false);
            d["minus"] = new KeySpec(0xBD, false);
            d["equals"] = new KeySpec(0xBB, false);
            d["comma"] = new KeySpec(0xBC, false);
            d["period"] = new KeySpec(0xBE, false);
            d["slash"] = new KeySpec(0xBF, false);
            d["backslash"] = new KeySpec(0xDC, false);
            d["semicolon"] = new KeySpec(0xBA, false);
            d["quote"] = new KeySpec(0xDE, false);
            d["grave"] = new KeySpec(0xC0, false);
            d["lbracket"] = new KeySpec(0xDB, false);
            d["rbracket"] = new KeySpec(0xDD, false);
            d["volup"] = new KeySpec(0xAF, true);
            d["voldown"] = new KeySpec(0xAE, true);
            d["mute"] = new KeySpec(0xAD, true);
            d["playpause"] = new KeySpec(0xB3, true);
            d["nexttrack"] = new KeySpec(0xB0, true);
            d["prevtrack"] = new KeySpec(0xB1, true);
            d["stopmedia"] = new KeySpec(0xB2, true);
            return d;
        }

        public static bool TryParseCombo(string combo, out List<KeySpec> keys)
        {
            keys = new List<KeySpec>();
            if (string.IsNullOrEmpty(combo)) return false;
            string[] parts = combo.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim().ToLowerInvariant();
                if (p.Length == 0) continue;
                KeySpec k;
                if (!Names.TryGetValue(p, out k)) { Log.Warn("unknown key: " + p); return false; }
                keys.Add(k);
            }
            return keys.Count > 0;
        }

        public static void SendCombo(string combo)
        {
            List<KeySpec> keys;
            if (!TryParseCombo(combo, out keys)) return;
            for (int i = 0; i < keys.Count; i++) Native.SendKeyRaw(keys[i].Vk, false, keys[i].Ext);
            Thread.Sleep(18);
            for (int i = keys.Count - 1; i >= 0; i--) Native.SendKeyRaw(keys[i].Vk, true, keys[i].Ext);
        }

        public static void SendCombo(List<KeySpec> keys)
        {
            if (keys == null || keys.Count == 0) return;
            for (int i = 0; i < keys.Count; i++) Native.SendKeyRaw(keys[i].Vk, false, keys[i].Ext);
            Thread.Sleep(18);
            for (int i = keys.Count - 1; i >= 0; i--) Native.SendKeyRaw(keys[i].Vk, true, keys[i].Ext);
        }

        private static void Focus(IntPtr target)
        {
            if (target == IntPtr.Zero) return;
            Native.ForceForeground(target);
            Thread.Sleep(70);
        }

        /// <summary>Runs a command. Returns null on success or a human readable error.</summary>
        public static string Run(Command c, IntPtr target, bool restoreFocus)
        {
            if (c == null) return "空指令";
            try
            {
                switch (c.Action)
                {
                    case "open_app":
                        return OpenApp(c.Target);
                    case "url":
                        Process.Start(new ProcessStartInfo(c.Target) { UseShellExecute = true });
                        return null;
                    case "tab":
                        return WindowOps.OpenTab(c.Target, c.Text);
                    case "douyin":
                        return WindowOps.DouyinKey(c.Keys, target);
                    case "keys":
                        if (restoreFocus) Focus(target);
                        SendCombo(c.Keys);
                        return null;
                    case "text":
                        if (restoreFocus) Focus(target);
                        Native.SendText(c.Text);
                        return null;
                    case "system":
                        return SystemOp(c.Op, target);
                    case "wait":
                        {
                            int ms;
                            int.TryParse(c.Target, out ms);
                            if (ms > 0) Thread.Sleep(ms);
                            return null;
                        }
                    case "sequence":
                        for (int i = 0; i < c.Steps.Count; i++)
                        {
                            // 前一步可能换了窗口（打开微信然后聚焦输入框），后面的步骤跟着新窗口走。
                            IntPtr t = i == 0 ? target : Native.GetForegroundWindow();
                            string err = Run(c.Steps[i], t, restoreFocus);
                            if (err != null) return err;
                            Thread.Sleep(220);
                        }
                        return null;
                    case "none":
                        return null;
                }
                return "未知动作: " + c.Action;
            }
            catch (Exception ex)
            {
                Log.Warn("run failed: " + ex.Message);
                return ex.Message;
            }
        }

        private static string OpenApp(string target)
        {
            if (string.IsNullOrEmpty(target)) return "没有目标";
            if (target.StartsWith("@app:", StringComparison.OrdinalIgnoreCase))
            {
                string name = target.Substring(5);
                if (Apps == null) return "应用索引未就绪";
                AppEntry e = Apps.BestMatch(TextUtil.Normalize(name));
                if (e == null) return "找不到应用：" + name;
                target = e.Target;
            }
            if (string.IsNullOrEmpty(target)) return "没有目标";
            if (target.StartsWith("ms-", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return null;
            }
            if (!System.IO.File.Exists(target) && !System.IO.Directory.Exists(target))
            {
                // last resort: let the shell resolve it (app path / PATH)
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return null;
            }
            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && WindowOps.ActivateRunning(target)) return null;
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(target) });
            return null;
        }

        private static string SystemOp(string op, IntPtr target)
        {
            if (string.IsNullOrEmpty(op)) return "没有系统操作";
            if (op.StartsWith("ask:", StringComparison.OrdinalIgnoreCase))
            {
                string a = Assistant.Answer(op.Substring(4), target);
                if (Answer != null) Answer(a);
                return null;
            }
            if (op.StartsWith("pinned:", StringComparison.OrdinalIgnoreCase))
            {
                int n;
                if (!int.TryParse(op.Substring(7), out n) || n < 1) return "置顶序号不对: " + op;
                return WindowOps.OpenPinned(n);
            }
            switch (op.ToLowerInvariant())
            {
                case "focusinput": return WindowOps.FocusInput(target);
                case "lock": Native.LockWorkStation(); return null;
                case "sleep": Native.SetSuspendState(false, false, false); return null;
                case "hibernate": Native.SetSuspendState(true, false, false); return null;
                case "shutdown": Process.Start("shutdown.exe", "/s /t 0"); return null;
                case "restart": Process.Start("shutdown.exe", "/r /t 0"); return null;
                case "logoff": Process.Start("shutdown.exe", "/l"); return null;
                case "mute": SendCombo("mute"); return null;
                // 系统一格只有 2%，一次说话调 10%，不然要说好几遍。
                case "volup": for (int i = 0; i < 5; i++) SendCombo("volup"); return null;
                case "voldown": for (int i = 0; i < 5; i++) SendCombo("voldown"); return null;
                case "playpause": SendCombo("playpause"); return null;
                case "next": SendCombo("nexttrack"); return null;
                case "prev": SendCombo("prevtrack"); return null;
                case "alwaysontop": ToggleTopmost(); return null;
                case "closewindow": SendCombo("alt+f4"); return null;
                case "maximize": SendCombo("win+up"); return null;
                case "minimize": SendCombo("win+down"); return null;
                case "showdesktop": return WindowOps.ShowDesktop(target);
                case "taskview": SendCombo("win+tab"); return null;
                case "switchwindow": return WindowOps.SwitchPrevious(target);
                case "screenshot": SendCombo("win+shift+s"); return null;
                case "fullscreenshot": SendCombo("win+printscreen"); return null;
                case "record": SendCombo("win+alt+r"); return null;
                case "run": SendCombo("win+r"); return null;
                case "search": SendCombo("win+s"); return null;
                case "explorer": SendCombo("win+e"); return null;
                case "settings": Process.Start("ms-settings:"); return null;
                case "notifications": SendCombo("win+a"); return null;
                case "clipboard": SendCombo("win+v"); return null;
                case "emoji": SendCombo("win+period"); return null;
                case "project": SendCombo("win+p"); return null;
                case "gamebar": SendCombo("win+g"); return null;
                case "lockui": SendCombo("win+l"); return null;
                case "library":
                    if (ShowLibrary != null) ShowLibrary();
                    return null;
                case "copy": SendCombo("ctrl+c"); return null;
                case "paste": SendCombo("ctrl+v"); return null;
                case "cut": SendCombo("ctrl+x"); return null;
                case "undo": SendCombo("ctrl+z"); return null;
                case "redo": SendCombo("ctrl+y"); return null;
                case "selectall": SendCombo("ctrl+a"); return null;
                case "save": SendCombo("ctrl+s"); return null;
            }
            return "未知系统操作: " + op;
        }

        private static void ToggleTopmost()
        {
            IntPtr h = Native.GetForegroundWindow();
            if (h == IntPtr.Zero) return;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            bool top = (ex & Native.WS_EX_TOPMOST) != 0;
            Native.SetWindowPos(h, top ? new IntPtr(-2) : new IntPtr(-1), 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE);
        }
    }
}
