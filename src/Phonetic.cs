using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SayAllVoice
{
    /// <summary>
    /// 听写常把字写成同音字，比字面不如比读音。表是 pinyin.txt（每行：汉字+拼音）。
    /// 平翘舌、前后鼻音、n/l 都算同一个音，这几组最容易听混。
    /// </summary>
    public static class Phonetic
    {
        private static Dictionary<char, string> _map;
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        private static void Load()
        {
            _map = new Dictionary<char, string>();
            try
            {
                using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("pinyin.txt"))
                using (StreamReader r = new StreamReader(st, Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                        if (line.Length > 1) _map[line[0]] = Soften(line.Substring(1));
                }
            }
            catch (Exception ex) { Log.Warn("pinyin load: " + ex.Message); }
        }

        private static string Soften(string p)
        {
            if (p.StartsWith("zh")) p = "z" + p.Substring(2);
            else if (p.StartsWith("ch")) p = "c" + p.Substring(2);
            else if (p.StartsWith("sh")) p = "s" + p.Substring(2);
            if (p.StartsWith("n")) p = "l" + p.Substring(1);
            if (p.EndsWith("ng")) p = p.Substring(0, p.Length - 1);
            return p;
        }

        public static string Of(string s)
        {
            string r;
            lock (Cache)
            {
                if (Cache.TryGetValue(s, out r)) return r;
                if (_map == null) Load();
                StringBuilder sb = new StringBuilder();
                foreach (char c in s)
                {
                    string p;
                    if (_map.TryGetValue(c, out p)) sb.Append(p);
                    else sb.Append(c);
                }
                r = sb.ToString();
                if (Cache.Count > 5000) Cache.Clear();
                Cache[s] = r;
            }
            return r;
        }

        /// <summary>读音相似度，0 到 1。</summary>
        public static double Similarity(string a, string b)
        {
            string x = Of(a), y = Of(b);
            if (x == y) return 1;
            int max = Math.Max(x.Length, y.Length);
            if (max == 0) return 0;
            return 1.0 - (double)Edit(x, y) / max;
        }

        private static int Edit(string a, string b)
        {
            int[] prev = new int[b.Length + 1];
            int[] cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int c = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + c);
                }
                int[] t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }

        // 这台电脑上实际听错过的词。只用来匹配指令，不影响听写出来的文字。
        private static readonly KeyValuePair<Regex, string>[] Fixes = new KeyValuePair<Regex, string>[]
        {
            Fix("克劳德|克劳得|科劳德|可劳德|克老德|clouds?|clawed|clod|claud(?!e)", "claude"),
            Fix("cheering|chroma|chrom(?!e)|克罗姆|克洛姆|克隆姆", "chrome"),
            Fix("deepeek|deepsick|deepsik|deepsike|deepsk|deepsea?k|deeps(?!eek)|迪普西克|迪普seek|deep西克", "deepseek"),
            Fix("harnessness|harnes(?!s)|hanes|哈尼斯|哈内斯|哈奈斯", "harness"),
            Fix("左蓝|佐兰|左兰|左篮", "左栏"),
            Fix("绘画|回话|汇话", "会话"),
            Fix("至顶", "置顶"),
        };

        private static KeyValuePair<Regex, string> Fix(string p, string to)
        {
            return new KeyValuePair<Regex, string>(new Regex(p, RegexOptions.Compiled), to);
        }

        private static readonly Regex SpokenDigits = new Regex("[零一二三四五六七八九]{4,5}");
        private static readonly Regex SpokenThousand = new Regex("([一二三四五六七八九])千([一二三四五六七八九])百(?:([一二三四五六七八九])十([一二三四五六七八九])?|零([一二三四五六七八九]))?");
        private const string Digits = "零一二三四五六七八九";

        private static int D(System.Text.RegularExpressions.Group g)
        {
            return g.Success ? Digits.IndexOf(g.Value, StringComparison.Ordinal) : 0;
        }

        /// <summary>「八八八八」「八千八百八十一」都写成数字，面板靠端口号认。</summary>
        private static string NumbersOf(string s)
        {
            s = SpokenThousand.Replace(s, delegate(System.Text.RegularExpressions.Match m)
            {
                return (D(m.Groups[1]) * 1000 + D(m.Groups[2]) * 100 + D(m.Groups[3]) * 10 + D(m.Groups[4]) + D(m.Groups[5])).ToString();
            });
            return SpokenDigits.Replace(s, delegate(System.Text.RegularExpressions.Match m)
            {
                StringBuilder sb = new StringBuilder();
                foreach (char c in m.Value) sb.Append(Digits.IndexOf(c));
                return sb.ToString();
            });
        }

        public static string Canon(string norm)
        {
            norm = NumbersOf(norm);
            foreach (KeyValuePair<Regex, string> f in Fixes) norm = f.Key.Replace(norm, f.Value);
            return norm;
        }
    }
}
