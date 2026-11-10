using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SayAllVoice
{
    public class AppEntry
    {
        public string Name = "";
        public string Norm = "";
        public string Target = "";
        public string Source = "";
        public bool Exact;
        /// <summary>True when the hit came only from a keyword (浏览器), not from the app's name.</summary>
        public bool KeywordOnly;
        public List<string> Keywords = new List<string>();

        public AppEntry(string name, string target, string source)
        {
            Name = name;
            Target = target;
            Source = source;
            Norm = TextUtil.Normalize(name);
        }
    }

    /// <summary>Index of launchable apps: start menu shortcuts + App Paths + a few known ones.</summary>
    public class AppIndex
    {
        public List<AppEntry> Apps = new List<AppEntry>();
        private static readonly string CachePath = Path.Combine(Settings.DataDir, "appindex.json");

        public void Load()
        {
            if (LoadCache()) return;
            Rebuild();
        }

        private bool LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return false;
                Dictionary<string, object> d = J.Obj(Json.Parse(File.ReadAllText(CachePath)));
                if (d == null) return false;
                long built = 0;
                object bv;
                if (d.TryGetValue("built", out bv)) built = Convert.ToInt64(bv);
                TimeSpan age = TimeSpan.FromTicks(DateTime.Now.Ticks - built);
                if (age.TotalDays > 7) return false;
                foreach (object o in J.ObjList(d, "apps"))
                {
                    Dictionary<string, object> ad = J.Obj(o);
                    if (ad == null) continue;
                    AppEntry e = new AppEntry(J.Str(ad, "name", ""), J.Str(ad, "target", ""), J.Str(ad, "source", ""));
                    e.Keywords = J.StrList(ad, "keywords");
                    if (e.Norm.Length > 0 && e.Target.Length > 0) Apps.Add(e);
                }
                return Apps.Count > 0;
            }
            catch { return false; }
        }

        private void SaveCache()
        {
            try
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["built"] = DateTime.Now.Ticks;
                List<object> arr = new List<object>();
                foreach (AppEntry e in Apps)
                {
                    Dictionary<string, object> ad = new Dictionary<string, object>();
                    ad["name"] = e.Name;
                    ad["target"] = e.Target;
                    ad["source"] = e.Source;
                    if (e.Keywords.Count > 0) ad["keywords"] = new List<object>(e.Keywords.ToArray());
                    arr.Add(ad);
                }
                d["apps"] = arr;
                File.WriteAllText(CachePath, Json.Stringify(d, true), Encoding.UTF8);
            }
            catch (Exception ex) { Log.Warn("appindex save failed: " + ex.Message); }
        }

        public void Rebuild()
        {
            Apps.Clear();
            ScanStartMenu(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
            ScanStartMenu(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            ScanAppPaths();
            AddKnown();
            Dedupe();
            Apps.Sort(delegate(AppEntry a, AppEntry b) { return string.CompareOrdinal(a.Name, b.Name); });
            Log.Write("app index rebuilt: " + Apps.Count);
            SaveCache();
        }

        private void ScanStartMenu(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            string[] files;
            try { files = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories); }
            catch { return; }
            for (int i = 0; i < files.Length; i++)
            {
                string f = files[i];
                string name = Path.GetFileNameWithoutExtension(f);
                if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.IndexOf("卸载", StringComparison.Ordinal) >= 0) continue;
                Add(new AppEntry(name, f, "lnk"));
            }
        }

        private void ScanAppPaths()
        {
            string[] roots = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"
            };
            for (int r = 0; r < roots.Length; r++)
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(roots[r]))
                    {
                        if (k == null) continue;
                        string[] subs = k.GetSubKeyNames();
                        for (int i = 0; i < subs.Length; i++)
                        {
                            using (Microsoft.Win32.RegistryKey sk = k.OpenSubKey(subs[i]))
                            {
                                if (sk == null) continue;
                                string path = sk.GetValue("") as string;
                                if (string.IsNullOrEmpty(path)) continue;
                                string name = Path.GetFileNameWithoutExtension(subs[i]);
                                Add(new AppEntry(name, path, "reg"));
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Warn("app paths scan: " + ex.Message); }
            }
        }

        private void AddKnown()
        {
            AddKnown("微信", new string[] { "wechat", "weixin", "微信" }, new string[]
            {
                @"C:\Program Files\Tencent\Weixin\Weixin.exe",
                @"C:\Program Files (x86)\Tencent\WeChat\WeChat.exe",
                @"C:\Program Files\Tencent\WeChat\WeChat.exe"
            });
            AddKnown("企业微信", new string[] { "wxwork", "企业微信" }, new string[]
            {
                @"C:\Program Files (x86)\WXWork\WXWork.exe",
                @"C:\Program Files\Tencent\WXWork\WXWork.exe"
            });
            AddKnown("QQ", new string[] { "qq", "腾讯qq" }, new string[]
            {
                @"C:\Program Files\Tencent\QQNT\QQ.exe",
                @"C:\Program Files (x86)\Tencent\QQ\Bin\QQ.exe"
            });
            AddKnown("Chrome", new string[] { "谷歌浏览器", "浏览器", "chrome", "谷歌" }, new string[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
            });
            AddKnown("Edge", new string[] { "浏览器", "edge", "微软浏览器" }, new string[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
            });
            AddKnown("记事本", new string[] { "notepad", "文本编辑器", "记事本" }, new string[] { @"C:\Windows\notepad.exe" });
            AddKnown("计算器", new string[] { "calc", "计算器" }, new string[] { @"C:\Windows\System32\calc.exe" });
            AddKnown("画图", new string[] { "mspaint", "画图" }, new string[] { @"C:\Windows\System32\mspaint.exe" });
            AddKnown("资源管理器", new string[] { "explorer", "我的电脑", "文件管理器", "此电脑" }, new string[] { @"C:\Windows\explorer.exe" });
            AddKnown("命令提示符", new string[] { "cmd", "终端", "命令行" }, new string[] { @"C:\Windows\System32\cmd.exe" });
            AddKnown("PowerShell", new string[] { "powershell", "终端" }, new string[] { @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" });
            AddKnown("任务管理器", new string[] { "taskmgr", "任务管理器" }, new string[] { @"C:\Windows\System32\Taskmgr.exe" });
            AddKnown("控制面板", new string[] { "control", "控制面板" }, new string[] { @"C:\Windows\System32\control.exe" });
            AddKnown("设置", new string[] { "settings", "系统设置" }, new string[] { "ms-settings:" });
            AddKnown("截图工具", new string[] { "snippingtool", "截图" }, new string[] { @"C:\Windows\System32\SnippingTool.exe" });
            AddKnown("Word", new string[] { "word", "文档" }, new string[] { @"C:\Program Files\Microsoft Office\Root\Office16\WINWORD.EXE" });
            AddKnown("Excel", new string[] { "excel", "表格" }, new string[] { @"C:\Program Files\Microsoft Office\Root\Office16\EXCEL.EXE" });
            AddKnown("PowerPoint", new string[] { "ppt", "幻灯片", "演示" }, new string[] { @"C:\Program Files\Microsoft Office\Root\Office16\POWERPNT.EXE" });
            AddKnown("VS Code", new string[] { "vscode", "代码编辑器", "code" }, new string[]
            {
                @"C:\Users\" + Environment.UserName + @"\AppData\Local\Programs\Microsoft VS Code\Code.exe",
                @"C:\Program Files\Microsoft VS Code\Code.exe"
            });
            AddKnown("钉钉", new string[] { "dingtalk", "钉钉" }, new string[] { @"C:\Program Files (x86)\DingDing\DingtalkLauncher.exe" });
            AddKnown("飞书", new string[] { "feishu", "lark", "飞书" }, new string[]
            {
                @"C:\Users\" + Environment.UserName + @"\AppData\Local\Feishu\Feishu.exe",
                @"C:\Program Files\Feishu\Feishu.exe"
            });
            AddKnown("网易云音乐", new string[] { "netease", "网易云", "音乐" }, new string[]
            {
                @"C:\Program Files (x86)\Netease\CloudMusic\cloudmusic.exe",
                @"C:\Program Files\Netease\CloudMusic\cloudmusic.exe"
            });
            AddKnown("Bandizip", new string[] { "bandizip", "解压", "压缩" }, new string[] { @"C:\Program Files\Bandizip\Bandizip.exe" });
        }

        private void AddKnown(string name, string[] keywords, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                string p = candidates[i];
                if (p.StartsWith("ms-", StringComparison.Ordinal) || File.Exists(p))
                {
                    AppEntry e = new AppEntry(name, p, "known");
                    for (int k = 0; k < keywords.Length; k++) e.Keywords.Add(keywords[k]);
                    Add(e);
                    return;
                }
            }
        }

        private void Add(AppEntry e)
        {
            if (string.IsNullOrEmpty(e.Name) || string.IsNullOrEmpty(e.Target)) return;
            if (e.Norm.Length == 0) return;
            Apps.Add(e);
        }

        private void Dedupe()
        {
            Dictionary<string, AppEntry> seen = new Dictionary<string, AppEntry>();
            List<AppEntry> keep = new List<AppEntry>();
            foreach (AppEntry e in Apps)
            {
                string key = e.Norm + "|" + e.Target.ToLowerInvariant();
                if (seen.ContainsKey(key)) continue;
                seen[key] = e;
                keep.Add(e);
            }
            Apps = keep;
        }

        public AppEntry BestMatch(string norm)
        {
            AppEntry best = null;
            double bestScore = 0;
            bool bestNameHit = false;
            for (int i = 0; i < Apps.Count; i++)
            {
                AppEntry e = Apps[i];
                double s = 0;
                if (e.Norm == norm) { e.Exact = true; e.KeywordOnly = false; return e; }
                bool nameHit = false;
                if (e.Norm.Length >= 2 && (e.Norm.IndexOf(norm, StringComparison.Ordinal) >= 0 || norm.IndexOf(e.Norm, StringComparison.Ordinal) >= 0))
                {
                    s = 0.90 - Math.Abs(e.Norm.Length - norm.Length) * 0.01;
                    nameHit = true;
                }
                else
                {
                    s = TextUtil.Dice(norm, e.Norm) * 0.82;
                }
                for (int k = 0; k < e.Keywords.Count; k++)
                {
                    string kw = TextUtil.Normalize(e.Keywords[k]);
                    if (kw.Length == 0) continue;
                    double ks;
                    if (kw == norm) ks = 0.995;
                    else if (kw.Length >= 2 && (norm.IndexOf(kw, StringComparison.Ordinal) >= 0 || kw.IndexOf(norm, StringComparison.Ordinal) >= 0)) ks = 0.92;
                    else ks = TextUtil.Dice(norm, kw) * 0.8;
                    if (ks > s) s = ks;
                }
                if (s > bestScore) { bestScore = s; best = e; bestNameHit = nameHit; }
            }
            if (best != null)
            {
                best.Exact = bestScore >= 0.995;
                best.KeywordOnly = !bestNameHit;
                if (bestScore < 0.55) return null;
            }
            return best;
        }
    }
}
