using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SayAllVoice
{
    /// <summary>
    /// 从 VB-Cable 的「CABLE Output」旁听遥控器的声音。SayAll 把遥控器音频送进这条虚拟线，
    /// 微信输入法听的也是它，共享录音互不影响。只在按住语音键时才打开声卡：
    /// 2026-11-07 常驻录音上线 6 分钟后蓝屏（0xA，没留转储，没法排除驱动），平时不碰这个驱动。
    /// </summary>
    public sealed class AudioTap
    {
        private const int Rate = 16000;
        private const int BytesPerSec = Rate * 2;
        private const int RingBytes = BytesPerSec * 30;
        private const int BufBytes = BytesPerSec / 20;   // 50ms
        private const int BufCount = 8;

        private readonly byte[] _ring = new byte[RingBytes];
        private readonly object _gate = new object();
        private long _total;
        private long _mark;
        private int _gen;
        private int _deviceId = -2;

        public bool Available
        {
            get
            {
                if (_deviceId == -2) _deviceId = FindDevice();
                return _deviceId >= 0;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WAVEINCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        private const uint WHDR_DONE = 1;
        private const uint WAVE_MAPPED = 4;

        [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")]
        private static extern uint waveInGetDevCaps(UIntPtr id, ref WAVEINCAPS caps, uint size);
        [DllImport("winmm.dll")]
        private static extern uint waveInOpen(out IntPtr h, uint id, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr h);
        [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr h);
        [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr h);

        /// <summary>按键按下：开一次录音。旧的那次（如果还在）自己收尾。</summary>
        public void Begin()
        {
            int my;
            lock (_gate)
            {
                _mark = _total;
                my = ++_gen;
            }
            if (!Available) { _deviceId = -2; return; }
            Thread t = new Thread(delegate() { Session(my); });
            t.IsBackground = true;
            t.Name = "audio-tap";
            t.Start();
        }

        public void End()
        {
            lock (_gate) { _gen++; }
        }

        private static int FindDevice()
        {
            uint n = waveInGetNumDevs();
            for (uint i = 0; i < n; i++)
            {
                WAVEINCAPS c = new WAVEINCAPS();
                if (waveInGetDevCaps(new UIntPtr(i), ref c, (uint)Marshal.SizeOf(typeof(WAVEINCAPS))) != 0) continue;
                if (c.szPname != null && c.szPname.IndexOf("CABLE Output", StringComparison.OrdinalIgnoreCase) >= 0) return (int)i;
            }
            return -1;
        }

        private void Session(int my)
        {
            IntPtr dev = IntPtr.Zero;
            uint hs = (uint)Marshal.SizeOf(typeof(WAVEHDR));
            IntPtr[] hdrs = new IntPtr[BufCount];
            IntPtr[] data = new IntPtr[BufCount];
            try
            {
                WAVEFORMATEX f = new WAVEFORMATEX();
                f.wFormatTag = 1; f.nChannels = 1; f.nSamplesPerSec = Rate; f.wBitsPerSample = 16;
                f.nBlockAlign = 2; f.nAvgBytesPerSec = BytesPerSec; f.cbSize = 0;
                uint r = waveInOpen(out dev, (uint)_deviceId, ref f, IntPtr.Zero, IntPtr.Zero, WAVE_MAPPED);
                if (r != 0) { dev = IntPtr.Zero; Log.Warn("audio tap: open failed " + r); return; }

                int flagsOff = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");
                int recOff = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwBytesRecorded");
                for (int i = 0; i < BufCount; i++)
                {
                    data[i] = Marshal.AllocHGlobal(BufBytes);
                    hdrs[i] = Marshal.AllocHGlobal((int)hs);
                    WAVEHDR w = new WAVEHDR();
                    w.lpData = data[i];
                    w.dwBufferLength = BufBytes;
                    Marshal.StructureToPtr(w, hdrs[i], false);
                    waveInPrepareHeader(dev, hdrs[i], hs);
                    waveInAddBuffer(dev, hdrs[i], hs);
                }
                waveInStart(dev);
                byte[] tmp = new byte[BufBytes];
                int next = 0;
                DateTime deadline = DateTime.Now.AddSeconds(60);
                while (my == _gen && DateTime.Now < deadline)
                {
                    uint flags = (uint)Marshal.ReadInt32(hdrs[next], flagsOff);
                    if ((flags & WHDR_DONE) == 0) { Thread.Sleep(15); continue; }
                    int got = Marshal.ReadInt32(hdrs[next], recOff);
                    if (got > 0)
                    {
                        Marshal.Copy(data[next], tmp, 0, got);
                        Append(tmp, got);
                    }
                    Marshal.WriteInt32(hdrs[next], flagsOff, (int)(flags & ~WHDR_DONE));
                    waveInAddBuffer(dev, hdrs[next], hs);
                    next = (next + 1) % BufCount;
                }
            }
            catch (Exception ex) { Log.Warn("audio tap: " + ex.Message); }
            finally
            {
                if (dev != IntPtr.Zero)
                {
                    waveInReset(dev);
                    for (int i = 0; i < BufCount; i++)
                        if (hdrs[i] != IntPtr.Zero) waveInUnprepareHeader(dev, hdrs[i], hs);
                    waveInClose(dev);
                }
                for (int i = 0; i < BufCount; i++)
                {
                    if (hdrs[i] != IntPtr.Zero) Marshal.FreeHGlobal(hdrs[i]);
                    if (data[i] != IntPtr.Zero) Marshal.FreeHGlobal(data[i]);
                }
            }
        }

        private void Append(byte[] b, int n)
        {
            lock (_gate)
            {
                int pos = (int)(_total % RingBytes);
                int first = Math.Min(n, RingBytes - pos);
                Buffer.BlockCopy(b, 0, _ring, pos, first);
                if (first < n) Buffer.BlockCopy(b, first, _ring, 0, n - first);
                _total += n;
            }
        }

        /// <summary>取出打点之后的声音。peak 是最大采样值，用来判断是不是根本没声音。</summary>
        public byte[] Take(out int peak)
        {
            peak = 0;
            byte[] r;
            lock (_gate)
            {
                long from = Math.Max(_mark, _total - RingBytes + BytesPerSec / 10);
                int n = (int)(_total - from);
                if (n <= 0) return new byte[0];
                r = new byte[n];
                int pos = (int)(from % RingBytes);
                int first = Math.Min(n, RingBytes - pos);
                Buffer.BlockCopy(_ring, pos, r, 0, first);
                if (first < n) Buffer.BlockCopy(_ring, 0, r, first, n - first);
            }
            for (int i = 0; i + 1 < r.Length; i += 2)
            {
                int v = Math.Abs((int)(short)(r[i] | (r[i + 1] << 8)));
                if (v > peak) peak = v;
            }
            return r;
        }
    }
}
