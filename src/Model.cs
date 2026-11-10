using System;
using System.Collections.Generic;
using System.IO;

namespace SayAllVoice
{
    public class Command
    {
        public string Id = "";
        public string Name = "";
        public string Category = "自定义";
        public string Action = "keys";
        public string Target = "";
        public string Op = "";
        public string Text = "";
        public string Keys = "";
        public string Hint = "";
        public List<string> Aliases = new List<string>();
        public List<Command> Steps = new List<Command>();
        public bool Builtin;
        public bool Enabled = true;
        public int Uses;

        public string Display
        {
            get
            {
                if (!string.IsNullOrEmpty(Hint)) return Hint;
                switch (Action)
                {
                    case "open_app": return "打开 " + Target;
                    case "url": return "打开 " + Target;
                    case "keys": return Keys;
                    case "text": return "输入文字";
                    case "system": return Op;
                    case "sequence": return "组合动作 (" + Steps.Count + " 步)";
                }
                return Action;
            }
        }

        public Command Clone()
        {
            Command c = new Command();
            c.Id = Id; c.Name = Name; c.Category = Category; c.Action = Action;
            c.Target = Target; c.Op = Op; c.Text = Text; c.Keys = Keys; c.Hint = Hint;
            c.Aliases = new List<string>(Aliases.ToArray());
            for (int i = 0; i < Steps.Count; i++) c.Steps.Add(Steps[i].Clone());
            c.Builtin = Builtin; c.Enabled = Enabled; c.Uses = Uses;
            return c;
        }

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["id"] = Id;
            d["name"] = Name;
            d["category"] = Category;
            d["action"] = Action;
            if (!string.IsNullOrEmpty(Target)) d["target"] = Target;
            if (!string.IsNullOrEmpty(Op)) d["op"] = Op;
            if (!string.IsNullOrEmpty(Text)) d["text"] = Text;
            if (!string.IsNullOrEmpty(Keys)) d["keys"] = Keys;
            if (!string.IsNullOrEmpty(Hint)) d["hint"] = Hint;
            if (Aliases.Count > 0) d["aliases"] = new List<object>(Aliases.ToArray());
            if (!Enabled) d["enabled"] = false;
            if (Uses > 0) d["uses"] = Uses;
            if (Steps.Count > 0)
            {
                List<object> a = new List<object>();
                foreach (Command s in Steps) a.Add(s.ToJson());
                d["steps"] = a;
            }
            return d;
        }

        public static Command FromJson(Dictionary<string, object> d, bool builtin)
        {
            Command c = new Command();
            c.Id = J.Str(d, "id", "");
            c.Name = J.Str(d, "name", "");
            c.Category = J.Str(d, "category", "自定义");
            c.Action = J.Str(d, "action", "keys");
            c.Target = J.Str(d, "target", "");
            c.Op = J.Str(d, "op", "");
            c.Text = J.Str(d, "text", "");
            c.Keys = J.Str(d, "keys", "");
            c.Hint = J.Str(d, "hint", "");
            c.Aliases = J.StrList(d, "aliases");
            c.Builtin = builtin;
            c.Enabled = J.Bool(d, "enabled", true);
            c.Uses = J.Int(d, "uses", 0);
            foreach (object o in J.ObjList(d, "steps"))
            {
                Dictionary<string, object> sd = J.Obj(o);
                if (sd != null) c.Steps.Add(FromJson(sd, builtin));
            }
            if (string.IsNullOrEmpty(c.Id)) c.Id = "cmd_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (string.IsNullOrEmpty(c.Name)) c.Name = c.Id;
            return c;
        }
    }

    public class Settings
    {
        public List<string> HotkeyKeys = new List<string>(new string[] { "left_control", "left_windows" });
        public string ManualHotkey = "ctrl+alt+m";
        public bool HookEnabled = true;
        public bool CommandOnly;
        public bool PasteBack = true;
        public bool StartWithWindows = true;
        public bool Beep = true;
        public bool RestoreFocus = true;
        public int AutoThreshold = 86;
        public int ShowCandidates = 5;
        public int HideDelayMs = 900;
        public bool KeyTrace;
        public bool AutoStartSayAll = true;
        public bool Speak = true;
        public bool RecordActivity = true;

        public static string DataDir
        {
            get
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SayAllVoice");
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string SettingsPath { get { return Path.Combine(DataDir, "settings.json"); } }
        public static string CommandsPath { get { return Path.Combine(DataDir, "commands.json"); } }
        public static string LogPath { get { return Path.Combine(DataDir, "sayallvoice.log"); } }
        public static string LearnedPath { get { return Path.Combine(DataDir, "learned.json"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (File.Exists(SettingsPath))
                {
                    Dictionary<string, object> d = J.Obj(Json.Parse(File.ReadAllText(SettingsPath)));
                    if (d != null)
                    {
                        List<string> keys = J.StrList(d, "hotkey_keys");
                        if (keys.Count > 0) s.HotkeyKeys = keys;
                        s.ManualHotkey = J.Str(d, "manual_hotkey", s.ManualHotkey);
                        s.HookEnabled = J.Bool(d, "hook_enabled", true);
                        s.CommandOnly = J.Bool(d, "command_only", false);
                        s.PasteBack = J.Bool(d, "paste_back", true);
                        s.StartWithWindows = J.Bool(d, "start_with_windows", true);
                        s.Beep = J.Bool(d, "beep", true);
                        s.RestoreFocus = J.Bool(d, "restore_focus", true);
                        s.AutoThreshold = J.Int(d, "auto_threshold", 86);
                        s.ShowCandidates = J.Int(d, "show_candidates", 5);
                        s.HideDelayMs = J.Int(d, "hide_delay_ms", 900);
                        s.KeyTrace = J.Bool(d, "key_trace", false);
                        s.AutoStartSayAll = J.Bool(d, "auto_start_sayall", true);
                        s.Speak = J.Bool(d, "speak", true);
                        s.RecordActivity = J.Bool(d, "record_activity", true);
                    }
                }
            }
            catch (Exception ex) { Log.Warn("settings load failed: " + ex.Message); }
            return s;
        }

        public void Save()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["hotkey_keys"] = new List<object>(HotkeyKeys.ToArray());
            d["manual_hotkey"] = ManualHotkey;
            d["hook_enabled"] = HookEnabled;
            d["command_only"] = CommandOnly;
            d["paste_back"] = PasteBack;
            d["start_with_windows"] = StartWithWindows;
            d["beep"] = Beep;
            d["restore_focus"] = RestoreFocus;
            d["auto_threshold"] = AutoThreshold;
            d["show_candidates"] = ShowCandidates;
            d["hide_delay_ms"] = HideDelayMs;
            d["key_trace"] = KeyTrace;
            d["auto_start_sayall"] = AutoStartSayAll;
            d["speak"] = Speak;
            d["record_activity"] = RecordActivity;
            try { File.WriteAllText(SettingsPath, Json.Stringify(d, true), System.Text.Encoding.UTF8); }
            catch (Exception ex) { Log.Warn("settings save failed: " + ex.Message); }
        }
    }

    public class Match
    {
        public Command Cmd;
        public double Score;
        public string Shown = "";

        public Match(Command c, double s)
        {
            Cmd = c;
            Score = s;
            Shown = c.Name;
        }
    }

    public static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string msg)
        {
            try
            {
                lock (Gate)
                {
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + Environment.NewLine;
                    File.AppendAllText(Settings.LogPath, line, System.Text.Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void Warn(string msg) { Write("WARN " + msg); }
    }
}
