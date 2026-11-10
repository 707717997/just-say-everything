using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;

namespace SayAllVoice
{
    public static class TextUtil
    {
        private static readonly char[] Drop =
            " \t\r\n，。！？、；：“”‘’（）《》【】…—·,.!?;:\"'()<>[]{}~`@#$%^&*_+=|\\/－".ToCharArray();

        private static readonly string[] PolitePrefix =
            { "请帮我", "麻烦你", "麻烦", "帮我", "帮忙", "给我", "我要", "我想", "能不能", "可不可以", "可以", "请你", "请", "快", "立即", "马上" };

        private static readonly string[] Particles =
            { "谢谢", "一下吧", "一下", "吧", "啊", "呢", "呀", "嘛", "哦", "哈", "了", "呗", "吗" };

        /// <summary>Fillers that may sit anywhere in the sentence (打开一下微信 / 帮我打开).</summary>
        private static readonly string[] Infixes =
            { "一下", "帮我", "帮忙", "给我", "麻烦你", "麻烦", "请你", "我要", "我想", "能不能", "可不可以" };

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 0xFF01 && c <= 0xFF5E) c = (char)(c - 0xFEE0);
                if (c == '\u3000') continue;
                if (Array.IndexOf(Drop, c) >= 0) continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            string t = sb.ToString();
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < PolitePrefix.Length; i++)
                {
                    if (t.StartsWith(PolitePrefix[i], StringComparison.Ordinal) && t.Length > PolitePrefix[i].Length)
                    {
                        if (t.StartsWith("快进", StringComparison.Ordinal) || t.StartsWith("快退", StringComparison.Ordinal)) continue;
                        t = t.Substring(PolitePrefix[i].Length);
                        changed = true;
                    }
                }
            }
            // Fillers anywhere in the sentence: 打开一下微信 -> 打开微信
            for (int i = 0; i < Infixes.Length; i++)
            {
                string f = Infixes[i];
                while (t.Length > f.Length && t.IndexOf(f, StringComparison.Ordinal) >= 0)
                    t = t.Replace(f, "");
            }
            bool trimmed = true;
            while (trimmed)
            {
                trimmed = false;
                for (int i = 0; i < Particles.Length; i++)
                {
                    if (t.EndsWith(Particles[i], StringComparison.Ordinal) && t.Length > Particles[i].Length)
                    {
                        t = t.Substring(0, t.Length - Particles[i].Length);
                        trimmed = true;
                    }
                }
            }
            return t;
        }

        public static Dictionary<string, int> Bigrams(string s)
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            if (s.Length == 0) return d;
            if (s.Length == 1) { d[s] = 1; return d; }
            for (int i = 0; i < s.Length - 1; i++)
            {
                string g = s.Substring(i, 2);
                int n;
                d.TryGetValue(g, out n);
                d[g] = n + 1;
            }
            return d;
        }

        public static double Dice(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            if (a == b) return 1;
            Dictionary<string, int> x = Bigrams(a);
            Dictionary<string, int> y = Bigrams(b);
            int inter = 0, total = 0;
            foreach (KeyValuePair<string, int> kv in x)
            {
                total += kv.Value;
                int n;
                if (y.TryGetValue(kv.Key, out n)) inter += Math.Min(n, kv.Value);
            }
            foreach (KeyValuePair<string, int> kv in y) total += kv.Value;
            if (total == 0) return 0;
            return 2.0 * inter / total;
        }

        /// <summary>Strips a leading verb such as 打开 / 启动 and returns the rest.</summary>
        public static string StripOpenVerb(string norm, out bool matched)
        {
            string[] verbs = { "帮我打开", "打开一下", "打开", "启动", "运行", "唤起", "调用", "进入", "切换到", "切换到", "切到", "开一下", "开" };
            matched = false;
            for (int i = 0; i < verbs.Length; i++)
            {
                string v = verbs[i];
                if (norm.StartsWith(v, StringComparison.Ordinal) && norm.Length > v.Length)
                {
                    matched = true;
                    return norm.Substring(v.Length);
                }
            }
            return norm;
        }

        private static readonly Regex UrlSpoken = new Regex(
            "(?:打开|进入|访问|去|上)\\s*((?:https?://)?[a-z0-9-]+(?:\\.[a-z0-9-]+)+(?:/\\S*)?)", RegexOptions.IgnoreCase);

        /// <summary>Normalize 会去掉点号，所以网址要在原文里找。「打开baidu点com」也算。</summary>
        public static string UrlIn(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string s = Regex.Replace(raw, "点(?=com|cn|net|org|ai|io)", ".", RegexOptions.IgnoreCase).TrimEnd('。', '.', ' ');
            System.Text.RegularExpressions.Match m = UrlSpoken.Match(s);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static readonly Regex CommandLike = new Regex(
            "^(打开|关闭|关掉|切换|切到|调|回到|进入|启动|运行|播放|暂停|截图|读|念)|置顶|音量|声音|会话|桌面");

        /// <summary>短句而且像在下指令：没匹配上时该追问，不该当听写粘进当前窗口。</summary>
        public static bool LooksLikeCommand(string raw)
        {
            string n = Normalize(raw);
            return n.Length > 0 && n.Length <= 10 && CommandLike.IsMatch(n);
        }

        public static bool LooksLikeUrl(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 4) return false;
            if (s.IndexOf(' ') >= 0) return false;
            return s.IndexOf('.') > 0 && s.IndexOf('.') < s.Length - 1;
        }
    }

    public class CommandStore
    {
        public List<Command> Builtins = new List<Command>();
        public List<Command> User = new List<Command>();
        public AppIndex Apps;

        public CommandStore(AppIndex apps)
        {
            Apps = apps;
            LoadBuiltins();
            LoadUser();
            LoadLearned();
        }

        // 用户确认过的说法：「打开cheering」问过一次是 Chrome，下次直接执行。
        private readonly Dictionary<string, string> _learned = new Dictionary<string, string>();

        private void LoadLearned()
        {
            try
            {
                if (!File.Exists(Settings.LearnedPath)) return;
                Dictionary<string, object> d = J.Obj(Json.Parse(File.ReadAllText(Settings.LearnedPath)));
                if (d == null) return;
                foreach (KeyValuePair<string, object> kv in d) _learned[kv.Key] = kv.Value as string ?? "";
                Log.Write("learned phrases: " + _learned.Count);
            }
            catch (Exception ex) { Log.Warn("learned load: " + ex.Message); }
        }

        public void Learn(string heard, Command c)
        {
            if (c == null || c.Id.StartsWith("dyn_", StringComparison.Ordinal)) return;
            string n = Phonetic.Canon(TextUtil.Normalize(heard));
            if (n.Length < 2 || n.Length > 16) return;
            _learned[n] = c.Id;
            try
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                foreach (KeyValuePair<string, string> kv in _learned) d[kv.Key] = kv.Value;
                File.WriteAllText(Settings.LearnedPath, Json.Stringify(d, true), Encoding.UTF8);
                Log.Write("learned '" + n + "' -> " + c.Id);
            }
            catch (Exception ex) { Log.Warn("learned save: " + ex.Message); }
        }

        public IEnumerable<Command> All
        {
            get
            {
                foreach (Command c in Builtins) yield return c;
                foreach (Command c in User) yield return c;
            }
        }

        public List<Command> Enabled
        {
            get
            {
                List<Command> r = new List<Command>();
                foreach (Command c in All) if (c.Enabled) r.Add(c);
                return r;
            }
        }

        /// <summary>
        /// What to show while the palette is still empty: a few starter commands,
        /// then whatever the user reaches for most often.
        /// </summary>
        public List<Match> Suggest(int n)
        {
            List<Match> r = new List<Match>();
            string[] ids = new string[] { "sys_desktop", "sys_switch", "sys_focus_input", "pin_1", "pin_2", "app_wechat", "app_chrome" };
            foreach (string id in ids)
            {
                if (r.Count >= n) return r;
                Command c = FindById(id);
                if (c != null && c.Enabled) r.Add(new Match(c, 0));
            }
            List<Command> used = Enabled;
            used.Sort(delegate(Command a, Command b) { return b.Uses.CompareTo(a.Uses); });
            foreach (Command c in used)
            {
                if (r.Count >= n) break;
                if (c.Uses <= 0) break;
                bool dup = false;
                foreach (Match m in r) if (m.Cmd == c) { dup = true; break; }
                if (!dup) r.Add(new Match(c, 0));
            }
            return r;
        }

        private void LoadBuiltins()        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string name = null;
                foreach (string n in asm.GetManifestResourceNames())
                {
                    if (n.EndsWith("commands.default.json", StringComparison.OrdinalIgnoreCase)) { name = n; break; }
                }
                if (name == null) { Log.Warn("builtin commands resource missing"); return; }
                using (Stream st = asm.GetManifestResourceStream(name))
                using (StreamReader sr = new StreamReader(st, Encoding.UTF8))
                {
                    List<object> arr = J.Arr(Json.Parse(sr.ReadToEnd()));
                    if (arr == null) return;
                    foreach (object o in arr)
                    {
                        Dictionary<string, object> d = J.Obj(o);
                        if (d == null) continue;
                        Command c = Command.FromJson(d, true);
                        Builtins.Add(c);
                    }
                }
                Log.Write("builtin commands loaded: " + Builtins.Count);
            }
            catch (Exception ex) { Log.Warn("builtin load failed: " + ex.Message); }
        }

        private void LoadUser()
        {
            try
            {
                if (!File.Exists(Settings.CommandsPath)) return;
                List<object> arr = J.Arr(Json.Parse(File.ReadAllText(Settings.CommandsPath)));
                if (arr == null) return;
                foreach (object o in arr)
                {
                    Dictionary<string, object> d = J.Obj(o);
                    if (d == null) continue;
                    User.Add(Command.FromJson(d, false));
                }
                Log.Write("user commands loaded: " + User.Count);
            }
            catch (Exception ex) { Log.Warn("user commands load failed: " + ex.Message); }
        }

        public void SaveUser()
        {
            List<object> arr = new List<object>();
            foreach (Command c in User) arr.Add(c.ToJson());
            try
            {
                File.WriteAllText(Settings.CommandsPath, Json.Stringify(arr, true), Encoding.UTF8);
                Log.Write("user commands saved: " + User.Count);
            }
            catch (Exception ex) { Log.Warn("user commands save failed: " + ex.Message); }
        }

        public Command FindById(string id)
        {
            foreach (Command c in All) if (c.Id == id) return c;
            return null;
        }

        public void BumpUse(Command c)
        {
            c.Uses++;
            if (!c.Builtin) SaveUser();
        }

        // ---------------------------------------------------------------- match

        public List<Match> Match(string rawText, int limit)
        {
            string norm = Phonetic.Canon(TextUtil.Normalize(rawText));
            List<Match> results = new List<Match>();
            if (norm.Length == 0) return results;

            string url = TextUtil.UrlIn(rawText);
            if (url != null)
            {
                Command uc = new Command();
                uc.Id = "dyn_url";
                uc.Name = "打开 " + url;
                uc.Category = "网页";
                uc.Action = "url";
                uc.Target = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
                uc.Hint = uc.Name;
                results.Add(new Match(uc, 0.97));
                return results;
            }

            string learnedId;
            if (_learned.TryGetValue(norm, out learnedId))
            {
                Command lc = FindById(learnedId);
                if (lc != null && lc.Enabled) results.Add(new Match(lc, 0.985));
            }

            foreach (Command c in Enabled)
            {
                double best = 0;
                for (int i = -1; i < c.Aliases.Count; i++)
                {
                    string alias = i < 0 ? c.Name : c.Aliases[i];
                    if (string.IsNullOrEmpty(alias)) continue;
                    double s = ScoreAlias(norm, alias);
                    if (s > best) best = s;
                }
                if (best >= 0.42) results.Add(new Match(c, best));
            }

            Match dyn = DynamicCommand(norm);
            if (dyn != null) results.Add(dyn);

            results.Sort(delegate(Match a, Match b)
            {
                if (Math.Abs(a.Score - b.Score) > 0.0001) return b.Score.CompareTo(a.Score);
                if (a.Cmd.Builtin != b.Cmd.Builtin) return a.Cmd.Builtin ? 1 : -1;
                return b.Cmd.Uses.CompareTo(a.Cmd.Uses);
            });

            if (results.Count > limit)
            {
                List<Match> cut = new List<Match>();
                for (int i = 0; i < limit; i++) cut.Add(results[i]);
                return cut;
            }
            return results;
        }

        private static double ScoreAlias(string norm, string aliasRaw)
        {
            string a = Phonetic.Canon(TextUtil.Normalize(aliasRaw));
            if (a.Length == 0) return 0;
            if (norm == a) return 1.0;
            bool openVerb;
            string rest = TextUtil.StripOpenVerb(norm, out openVerb);
            if (openVerb && rest.Length >= 1)
            {
                // "打开浏览器" should hit the 浏览器 alias of 打开Chrome rather than a
                // fuzzy guess from the application index.
                if (rest == a) return 0.94;
                if (rest.StartsWith(a, StringComparison.Ordinal) && rest.Length - a.Length <= 2)
                    return 0.92;
            }
            if (norm.StartsWith(a, StringComparison.Ordinal))
            {
                int extra = norm.Length - a.Length;
                if (extra <= 3) return 0.95 - extra * 0.02;
            }
            if (a.Length > norm.Length && norm.Length >= 2 && a.StartsWith(norm, StringComparison.Ordinal))
                return 0.86 * norm.Length / a.Length;
            if (a.Length >= 2 && norm.IndexOf(a, StringComparison.Ordinal) >= 0)
                return 0.80;
            double dice = TextUtil.Dice(norm, a);
            double s = dice * 0.85;
            // reward exact-length neighbours a little
            if (Math.Abs(norm.Length - a.Length) <= 1) s += 0.04;
            // 字不一样但读音几乎一样（打开威信 / 回到桌棉）：读音全同时够得上自动执行，差一点的只进候选。
            if (a.Length >= 3 && norm.Length >= 3 && norm.Length <= 14 && Math.Abs(norm.Length - a.Length) <= 2)
            {
                double sim = Phonetic.Similarity(norm, a);
                if (sim >= 0.75) s = Math.Max(s, sim * 0.93);
            }
            return s;
        }

        // 语音识别常把「会话」写成同音词，「置顶」写成「至顶/指定」。
        private const string PinNum = "([一二两三四五1-5])";
        private const string PinNoun = "(?:会话|对话|聊天|绘画|回话|汇话)";
        private static readonly Regex[] PinPatterns = new Regex[]
        {
            new Regex("第" + PinNum + "个?(?:置顶|至顶|指定)?的?" + PinNoun),
            new Regex("(?:置顶|至顶|指定)的?第?" + PinNum + "个?" + PinNoun + "?$"),
            new Regex("^(?:打开|切到|切换到|进入|回到)?" + PinNoun + "第?" + PinNum + "$"),
            new Regex("^(?:打开|切到|切换到|进入|回到)?第" + PinNum + "个$"),
            new Regex("(?:claude|左栏)[^第]*第?" + PinNum + "个?" + PinNoun + "?$"),
        };

        private const string ReadWords = "(?:说了什么|说啥|回复|回了什么|到哪了|进度|怎么样)";
        private static readonly Regex[] ReadPatterns = new Regex[]
        {
            new Regex("(?:读|念|播报|听听)一?下?(?:置顶)?的?第?" + PinNum + "个?" + PinNoun + "?"),
            new Regex("第?" + PinNum + "个?" + PinNoun + "的?" + ReadWords),
        };

        /// <summary>「读第二个会话」「第二个会话说了什么」：把最新回复念出来。</summary>
        private Match ReadCommand(string norm)
        {
            if (norm.Length > 16) return null;
            foreach (Regex re in ReadPatterns)
            {
                System.Text.RegularExpressions.Match m = re.Match(norm);
                if (!m.Success) continue;
                string g = m.Groups[1].Value;
                int n = g == "两" ? 2 : "一二三四五".IndexOf(g, StringComparison.Ordinal) + 1;
                if (n == 0) n = int.Parse(g);
                Command c = new Command();
                c.Id = "dyn_read";
                c.Name = "读第" + n + "个会话";
                c.Category = "助手";
                c.Action = "system";
                c.Op = "ask:read:" + n;
                return new Match(c, 0.995);
            }
            return null;
        }

        /// <summary>「打开置顶的第二个会话」「第2个对话」「置顶三」都归到 pin_N。</summary>
        private Match PinnedCommand(string norm)
        {
            if (norm.Length > 16) return null;   // 长句是在听写，不当指令
            foreach (Regex re in PinPatterns)
            {
                System.Text.RegularExpressions.Match m = re.Match(norm);
                if (!m.Success) continue;
                string g = m.Groups[1].Value;
                int n = g == "两" ? 2 : "一二三四五".IndexOf(g, StringComparison.Ordinal) + 1;
                if (n == 0) n = int.Parse(g);
                Command c = FindById("pin_" + n);
                if (c != null && c.Enabled) return new Match(c, 0.99);
            }
            return null;
        }

        // ---- 抖音：按键来自抖音网页自己的快捷键表（2026-11-07 从 douyin.com 的脚本里读的）
        public bool InDouyin;

        private static readonly string[][] DouyinKeys =
        {
            new string[] { "down", "下一个视频", "下一个", "下一条", "下个视频", "下一页", "换一个", "换一条", "下滑" },
            new string[] { "up", "上一个视频", "上一个", "上一条", "上个视频", "上一页", "上滑" },
            new string[] { "space", "暂停", "继续播放", "播放", "继续", "继续看", "暂停播放" },
            new string[] { "z", "点赞", "赞", "取消赞", "取消点赞", "喜欢", "赞一个" },
            new string[] { "c", "收藏", "取消收藏" },
            new string[] { "g", "关注", "取消关注", "关注作者" },
            new string[] { "x", "评论", "看评论", "打开评论", "关闭评论" },
            new string[] { "h", "全屏", "退出全屏", "全屏播放" },
            new string[] { "y", "网页全屏", "网页内全屏", "退出网页全屏" },
            new string[] { "j", "清屏", "取消清屏" },
            new string[] { "k", "连播", "自动连播", "关闭连播", "打开连播" },
            new string[] { "b", "弹幕", "打开弹幕", "关闭弹幕", "开弹幕", "关弹幕" },
            new string[] { "r", "不感兴趣", "不喜欢" },
            new string[] { "f", "作者主页", "进入主页", "看作者", "进入作者主页" },
            new string[] { "n", "相关推荐" },
            new string[] { "l", "稍后再看" },
            new string[] { "right", "快进" },
            new string[] { "left", "快退", "后退" },
            new string[] { "u", "小窗", "小窗模式" },
        };

        /// <summary>这些说法含义明确，不在抖音页面也算抖音指令。其余的只在抖音页面上生效，否则「暂停」「全屏」就抢了别的程序。</summary>
        private static readonly HashSet<string> DouyinAnywhere = new HashSet<string>(
            new string[] { "下一个视频", "上一个视频", "下个视频", "上个视频", "点赞", "不感兴趣" });

        private Match DouyinCommand(string norm)
        {
            bool named = norm.StartsWith("抖音", StringComparison.Ordinal) && norm.Length > 2;
            string w = named ? norm.Substring(2) : norm;
            foreach (string[] row in DouyinKeys)
            {
                for (int i = 1; i < row.Length; i++)
                {
                    if (w != row[i]) continue;
                    if (!named && !InDouyin && !DouyinAnywhere.Contains(w)) return null;
                    Command c = new Command();
                    c.Id = "dyn_douyin";
                    c.Name = "抖音 " + row[1];
                    c.Category = "抖音";
                    c.Action = "douyin";
                    c.Keys = row[0];
                    c.Hint = c.Name;
                    return new Match(c, 1.0);
                }
            }
            return null;
        }

        // ---- 本机网页面板（%APPDATA%\SayAllVoice\panels.json）和常用网站：已开就切过去
        // panels.json：{"panels":[{"port":3000,"label":"前端开发","aliases":["前端","dev"]}]}
        private static string PanelsPath { get { return Path.Combine(Settings.DataDir, "panels.json"); } }
        private readonly Dictionary<int, string> _panels = new Dictionary<int, string>();
        private readonly List<KeyValuePair<string, int>> PanelNames = new List<KeyValuePair<string, int>>();
        private DateTime _panelsAt = DateTime.MinValue;

        private static readonly string[][] Sites =
        {
            // 说法, 标题里的字, 网址
            new string[] { "抖音", "抖音", "https://www.douyin.com/" },
            new string[] { "微云", "微云", "https://www.weiyun.com/" },
            new string[] { "chatgpt", "ChatGPT", "https://chatgpt.com/" },
            new string[] { "opencode", "OpenCode", "https://opencode.ai/" },
            new string[] { "推特", " / X", "https://x.com/" },
        };

        private void LoadPanels()
        {
            if ((DateTime.Now - _panelsAt).TotalMinutes < 5) return;
            _panelsAt = DateTime.Now;
            try
            {
                _panels.Clear();
                PanelNames.Clear();
                if (!File.Exists(PanelsPath)) return;
                Dictionary<string, object> d = J.Obj(Json.Parse(File.ReadAllText(PanelsPath)));
                if (d == null) return;
                foreach (object o in J.ObjList(d, "panels"))
                {
                    Dictionary<string, object> p = J.Obj(o);
                    if (p == null) continue;
                    int port = J.Int(p, "port", 0);
                    if (port <= 0) continue;
                    _panels[port] = J.Str(p, "label", port.ToString());
                    foreach (string a in J.StrList(p, "aliases"))
                        PanelNames.Add(new KeyValuePair<string, int>(Phonetic.Canon(TextUtil.Normalize(a)), port));
                }
            }
            catch (Exception ex) { Log.Warn("panels: " + ex.Message); }
        }

        private static Match Tab(string name, string url, string key)
        {
            Command c = new Command();
            c.Id = "dyn_tab";
            c.Name = "打开 " + name;
            c.Category = "网页";
            c.Action = "tab";
            c.Target = url;
            c.Text = key;
            c.Hint = c.Name;
            return new Match(c, 1.0);
        }

        private Match TabCommand(string norm)
        {
            bool verb;
            string rest = TextUtil.StripOpenVerb(norm, out verb);
            rest = Regex.Replace(rest, "(面板|页面|网页|后台)$", "");
            if (rest.Length < 2) return null;
            LoadPanels();

            int port = 0;
            if (Regex.IsMatch(rest, "^\\d{4,5}$")) int.TryParse(rest, out port);
            else if (verb)
            {
                foreach (string[] s in Sites)
                    if (rest == s[0]) return Tab(s[0], s[2], s[1]);
                foreach (KeyValuePair<string, int> kv in PanelNames)
                    if (rest == kv.Key) { port = kv.Value; break; }
                if (port == 0)
                {
                    foreach (KeyValuePair<int, string> kv in _panels)
                    {
                        string label = Phonetic.Canon(TextUtil.Normalize(kv.Value));
                        bool close = rest == label || (rest.Length >= 3 && label.IndexOf(rest, StringComparison.Ordinal) >= 0)
                            || (rest.Length >= 3 && Phonetic.Similarity(rest, label) >= 0.85);
                        if (close) { port = kv.Key; break; }
                    }
                }
            }
            string name;
            if (port == 0 || !_panels.TryGetValue(port, out name)) return null;
            return Tab(name, "http://127.0.0.1:" + port + "/", "");
        }

        private Match DynamicCommand(string norm)
        {
            Match read = ReadCommand(norm);
            if (read != null) return read;
            Match dy = DouyinCommand(norm);
            if (dy != null) return dy;
            Match tab = TabCommand(norm);
            if (tab != null) return tab;
            Match pin = PinnedCommand(norm);
            if (pin != null) return pin;

            bool openVerb;
            string rest = TextUtil.StripOpenVerb(norm, out openVerb);
            if (openVerb && rest.Length >= 1)
            {
                AppEntry app = Apps.BestMatch(rest);
                if (app != null)
                {
                    Command c = new Command();
                    c.Id = "dyn_app";
                    c.Name = "打开 " + app.Name;
                    c.Category = "应用";
                    c.Action = "open_app";
                    c.Target = app.Target;
                    c.Builtin = false;
                    c.Hint = "打开 " + app.Name;
                    double s = app.Exact ? (app.KeywordOnly ? 0.91 : 0.99) : 0.90;
                    return new Match(c, s);
                }
                if (TextUtil.LooksLikeUrl(rest))
                {
                    Command c = new Command();
                    c.Id = "dyn_url";
                    c.Name = "打开 " + rest;
                    c.Category = "网页";
                    c.Action = "url";
                    c.Target = "https://" + rest;
                    c.Hint = "打开 " + rest;
                    return new Match(c, 0.95);
                }
            }

            string q;
            if (TryPrefix(norm, new string[] { "百度一下", "百度搜", "百度" }, out q))
                return Search("百度搜索 " + q, "https://www.baidu.com/s?wd=" + Uri.EscapeDataString(q));
            if (TryPrefix(norm, new string[] { "谷歌一下", "谷歌搜", "google", "谷歌" }, out q))
                return Search("谷歌搜索 " + q, "https://www.google.com/search?q=" + Uri.EscapeDataString(q));
            if (TryPrefix(norm, new string[] { "必应搜", "必应" }, out q))
                return Search("必应搜索 " + q, "https://www.bing.com/search?q=" + Uri.EscapeDataString(q));
            if (TryPrefix(norm, new string[] { "b站搜", "bilibili搜", "哔哩哔哩搜", "b站搜索" }, out q))
                return Search("B站搜索 " + q, "https://search.bilibili.com/all?keyword=" + Uri.EscapeDataString(q));
            if (TryPrefix(norm, new string[] { "淘宝搜", "淘宝搜索" }, out q))
                return Search("淘宝搜索 " + q, "https://s.taobao.com/search?q=" + Uri.EscapeDataString(q));
            if (TryPrefix(norm, new string[] { "京东搜", "京东搜索" }, out q))
                return Search("京东搜索 " + q, "https://search.jd.com/Search?keyword=" + Uri.EscapeDataString(q));
            if (TryPrefix(norm, new string[] { "知乎搜", "知乎搜索" }, out q))
                return Search("知乎搜索 " + q, "https://www.zhihu.com/search?type=content&q=" + Uri.EscapeDataString(q));
            if (TryPrefix(norm, new string[] { "搜索", "搜一下", "搜" }, out q))
                return Search("搜索 " + q, "https://www.baidu.com/s?wd=" + Uri.EscapeDataString(q));

            // "浏览器搜索机械键盘" / "用谷歌搜索 X" -- the verb sits mid-sentence.
            int si = norm.IndexOf("搜索", StringComparison.Ordinal);
            if (si > 0 && si + 2 < norm.Length)
            {
                string head = norm.Substring(0, si);
                string query = norm.Substring(si + 2);
                if (head.IndexOf("谷歌", StringComparison.Ordinal) >= 0 || head.IndexOf("google", StringComparison.Ordinal) >= 0)
                    return Search("谷歌搜索 " + query, "https://www.google.com/search?q=" + Uri.EscapeDataString(query));
                if (head.IndexOf("必应", StringComparison.Ordinal) >= 0)
                    return Search("必应搜索 " + query, "https://www.bing.com/search?q=" + Uri.EscapeDataString(query));
                if (head.IndexOf("b站", StringComparison.Ordinal) >= 0 || head.IndexOf("哔哩", StringComparison.Ordinal) >= 0)
                    return Search("B站搜索 " + query, "https://search.bilibili.com/all?keyword=" + Uri.EscapeDataString(query));
                if (head.IndexOf("淘宝", StringComparison.Ordinal) >= 0)
                    return Search("淘宝搜索 " + query, "https://s.taobao.com/search?q=" + Uri.EscapeDataString(query));
                if (head.IndexOf("京东", StringComparison.Ordinal) >= 0)
                    return Search("京东搜索 " + query, "https://search.jd.com/Search?keyword=" + Uri.EscapeDataString(query));
                if (head.IndexOf("知乎", StringComparison.Ordinal) >= 0)
                    return Search("知乎搜索 " + query, "https://www.zhihu.com/search?type=content&q=" + Uri.EscapeDataString(query));
                return Search("搜索 " + query, "https://www.baidu.com/s?wd=" + Uri.EscapeDataString(query));
            }
            return null;
        }

        private static bool TryPrefix(string norm, string[] prefixes, out string rest)
        {
            rest = "";
            for (int i = 0; i < prefixes.Length; i++)
            {
                string p = prefixes[i];
                if (norm.StartsWith(p, StringComparison.Ordinal) && norm.Length > p.Length + 1)
                {
                    rest = norm.Substring(p.Length);
                    return rest.Length > 0;
                }
            }
            return false;
        }

        private static Match Search(string shown, string url)
        {
            Command c = new Command();
            c.Id = "dyn_search";
            c.Name = shown;
            c.Category = "搜索";
            c.Action = "url";
            c.Target = url;
            c.Hint = shown;
            return new Match(c, 0.97);
        }
    }
}
