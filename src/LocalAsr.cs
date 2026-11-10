using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SayAllVoice
{
    /// <summary>
    /// 第二只耳朵：常驻一个 node 小进程跑 SenseVoice（asr\asr.cjs），走标准输入输出，不开端口。
    /// 父进程一退，它的 stdin 断开就自己退出。
    /// </summary>
    public sealed class LocalAsr
    {
        private Process _p;
        private volatile bool _ready;
        private readonly Queue<Action<string>> _waiting = new Queue<Action<string>>();
        private int _seq;

        public bool Ready { get { return _ready; } }

        private static string FindNode()
        {
            string[] tries =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"nodejs\node.exe"),
                @"C:\Program Files\nodejs\node.exe",
            };
            foreach (string t in tries) if (File.Exists(t)) return t;
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string d in path.Split(';'))
            {
                try
                {
                    string f = Path.Combine(d.Trim(), "node.exe");
                    if (File.Exists(f)) return f;
                }
                catch { }
            }
            return null;
        }

        public void Start()
        {
            if (_p != null && !_p.HasExited) return;
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "asr");
                string script = Path.Combine(dir, "asr.cjs");
                string node = FindNode();
                if (!File.Exists(script) || node == null) { Log.Warn("local asr: missing " + (node == null ? "node" : script)); return; }
                ProcessStartInfo si = new ProcessStartInfo(node, "\"" + script + "\"");
                si.WorkingDirectory = dir;
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.RedirectStandardInput = true;
                si.RedirectStandardOutput = true;
                si.RedirectStandardError = true;
                si.StandardOutputEncoding = Encoding.UTF8;
                _ready = false;
                _p = Process.Start(si);
                try { _p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
                Process proc = _p;
                Thread t = new Thread(delegate() { ReadLoop(proc); });
                t.IsBackground = true;
                t.Name = "asr-out";
                t.Start();
                proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) Log.Warn("asr stderr: " + e.Data); };
                proc.BeginErrorReadLine();
            }
            catch (Exception ex) { Log.Warn("local asr start: " + ex.Message); }
        }

        private void ReadLoop(Process p)
        {
            try
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    Dictionary<string, object> d = J.Obj(Json.TryParse(line));
                    if (d == null) continue;
                    if (d.ContainsKey("ready")) { _ready = true; Log.Write("local asr ready"); continue; }
                    string text = J.Str(d, "text", "");
                    Log.Write("local asr '" + text + "' " + J.Int(d, "ms", 0) + "ms" + (d.ContainsKey("error") ? " err=" + J.Str(d, "error", "") : ""));
                    Action<string> cb = null;
                    lock (_waiting) { if (_waiting.Count > 0) cb = _waiting.Dequeue(); }
                    if (cb != null) cb(text);
                }
            }
            catch (Exception ex) { Log.Warn("local asr read: " + ex.Message); }
            _ready = false;
            lock (_waiting)
            {
                while (_waiting.Count > 0) { try { _waiting.Dequeue()(""); } catch { } }
            }
        }

        /// <summary>回调在后台线程上。声音只写进临时文件给小进程读，读完立刻删。</summary>
        public void Recognize(byte[] pcm, Action<string> done)
        {
            if (!_ready) { done(""); return; }
            string f = Path.Combine(Settings.DataDir, "asr-" + (Interlocked.Increment(ref _seq) % 4) + ".pcm");
            try
            {
                File.WriteAllBytes(f, pcm);
                Action<string> wrapped = delegate(string text)
                {
                    try { File.Delete(f); } catch { }
                    done(text);
                };
                lock (_waiting) { _waiting.Enqueue(wrapped); }
                _p.StandardInput.WriteLine(f);
                _p.StandardInput.Flush();
            }
            catch (Exception ex) { Log.Warn("local asr send: " + ex.Message); done(""); }
        }

        public void Stop()
        {
            try { if (_p != null && !_p.HasExited) _p.StandardInput.Close(); } catch { }
        }
    }
}
