using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SayAllVoice
{
    internal static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_APP = 0x8000;

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int SW_RESTORE = 9;
        public const uint GW_OWNER = 4;
        public const uint GA_ROOT = 2;
        public const int DWMWA_CLOAKED = 14;

        public const int SW_SHOW = 5;
        public const int SW_HIDE = 0;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public const uint LLKHF_INJECTED = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public INPUTUNION u; }

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("user32.dll")]
        public static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
            public uint time; public int ptX; public int ptY;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder s, int nMaxCount);
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")]
        public static extern short GetKeyState(int vKey);
        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);
        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]
        public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
        [DllImport("oleacc.dll")]
        public static extern int AccessibleObjectFromWindow(IntPtr hWnd, uint objectId, ref Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object acc);

        [DllImport("user32.dll")]
        public static extern bool LockWorkStation();
        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public static int InputSize = Marshal.SizeOf(typeof(INPUT));

        public static void SendKeyRaw(ushort vk, bool up, bool extended)
        {
            INPUT[] a = new INPUT[1];
            a[0].type = INPUT_KEYBOARD;
            a[0].u.ki.wVk = vk;
            a[0].u.ki.wScan = 0;
            a[0].u.ki.dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (extended ? 0x0001u : 0u);
            SendInput(1, a, InputSize);
        }

        public static void SendUnicodeChar(char c)
        {
            INPUT[] a = new INPUT[2];
            a[0].type = INPUT_KEYBOARD;
            a[0].u.ki.wVk = 0;
            a[0].u.ki.wScan = c;
            a[0].u.ki.dwFlags = KEYEVENTF_UNICODE;
            a[1].type = INPUT_KEYBOARD;
            a[1].u.ki.wVk = 0;
            a[1].u.ki.wScan = c;
            a[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            SendInput(2, a, InputSize);
        }

        public static void SendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r') continue;
                if (c == '\n') { SendKeyRaw(0x0D, false, true); SendKeyRaw(0x0D, true, true); continue; }
                SendUnicodeChar(c);
                if ((i & 15) == 15) System.Threading.Thread.Sleep(8);
            }
        }

        public static string WindowTitle(IntPtr h)
        {
            if (h == IntPtr.Zero) return "";
            StringBuilder sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string ClassName(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static RECT WorkAreaForCursor()
        {
            POINT p;
            GetCursorPos(out p);
            IntPtr mon = MonitorFromPoint(p, 2 /*MONITOR_DEFAULTTONEAREST*/);
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi)) return mi.rcWork;
            RECT r = new RECT();
            r.Left = 0; r.Top = 0; r.Right = 1920; r.Bottom = 1080;
            return r;
        }

        public static void ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            IntPtr fg = GetForegroundWindow();
            uint fgPid;
            uint fgThread = 0;
            if (fg != IntPtr.Zero) fgThread = GetWindowThreadProcessId(fg, out fgPid);
            uint myThread = GetCurrentThreadId();
            bool attached = false;
            if (fgThread != 0 && fgThread != myThread)
                attached = AttachThreadInput(fgThread, myThread, true);
            try
            {
                ShowWindow(hwnd, SW_SHOW);
                // HWND_TOP, not TOPMOST: this also runs on the user's own windows, which must
                // not stay always-on-top. The palette is topmost through Form.TopMost.
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                SetFocus(hwnd);
            }
            finally
            {
                if (attached) AttachThreadInput(fgThread, myThread, false);
            }
            if (GetForegroundWindow() == hwnd) return;

            // Foreground lock: the process that produced the most recent input event
            // is allowed to take the foreground. Press ALT (hold it down across the
            // call), then try again.
            for (int attempt = 0; attempt < 3 && GetForegroundWindow() != hwnd; attempt++)
            {
                keybd_event(0x12, 0, 0, UIntPtr.Zero);        // ALT down
                System.Threading.Thread.Sleep(12);
                SetForegroundWindow(hwnd);
                BringWindowToTop(hwnd);
                SetFocus(hwnd);
                System.Threading.Thread.Sleep(12);
                keybd_event(0x12, 0, 2, UIntPtr.Zero);        // ALT up
                if (GetForegroundWindow() == hwnd) return;
                System.Threading.Thread.Sleep(30);
            }

            // Last resort: the undocumented (but very effective) switch.
            SwitchToThisWindow(hwnd, true);
        }

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")]
        public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        /// <summary>
        /// Make sure hwnd really owns the foreground. Falls back to a synthetic
        /// click on the window (the only method Windows cannot refuse), then puts
        /// the mouse back where it was.
        /// </summary>
        public static bool EnsureForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            ForceForeground(hwnd);
            if (GetForegroundWindow() == hwnd) return true;

            RECT r;
            if (!GetWindowRect(hwnd, out r)) return false;
            POINT old;
            bool hadCursor = GetCursorPos(out old);
            int cx = (r.Left + r.Right) / 2;
            int cy = r.Top + Math.Min(24, Math.Max(8, (r.Bottom - r.Top) / 6));
            SetCursorPos(cx, cy);
            System.Threading.Thread.Sleep(30);
            SendMouseClick();
            System.Threading.Thread.Sleep(30);
            if (hadCursor) SetCursorPos(old.X, old.Y);

            bool ok = GetForegroundWindow() == hwnd;
            if (!ok)
            {
                ForceForeground(hwnd);
                ok = GetForegroundWindow() == hwnd;
            }
            return ok;
        }

        public static void SendMouseClick()
        {
            INPUT[] a = new INPUT[2];
            a[0].type = 0;
            a[0].u.mi.dwFlags = 0x0002; // LEFTDOWN
            a[1].type = 0;
            a[1].u.mi.dwFlags = 0x0004; // LEFTUP
            SendInput(2, a, Marshal.SizeOf(typeof(INPUT)));
        }

        public static string ForegroundProcessName()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return "";
            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            if (pid == 0) return "";
            return ProcessNameOfPid(pid).ToLowerInvariant();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref uint size);

        /// <summary>
        /// 进程名（不含 .exe）。不用 Process.GetProcessById：它在这台机器上会随机抛异常，
        /// 同一个 Claude 进程有时能取到名字有时取不到。
        /// </summary>
        public static string ProcessNameOfPid(uint pid)
        {
            IntPtr h = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
            if (h == IntPtr.Zero) return "";
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref size)) return "";
                return System.IO.Path.GetFileNameWithoutExtension(sb.ToString());
            }
            finally { CloseHandle(h); }
        }

        public static int DpiScalePercent()
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            int dpi = 96;
            if (dc != IntPtr.Zero)
            {
                dpi = GetDeviceCaps(dc, 88);
                ReleaseDC(IntPtr.Zero, dc);
            }
            if (dpi <= 0) dpi = 96;
            return (int)Math.Round(dpi * 100.0 / 96.0);
        }
    }
}
