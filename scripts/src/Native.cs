// Native.cs — win-harness 原生 API 封装（P/Invoke）
// 编译进 win-harness.exe，避免 PowerShell 脚本文本触发 AMSI 启发式拦截。
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinHarness {
    public static class Native {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        // ---- 窗口管理 ----
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        // ---- 鼠标 ----
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

        // ---- 键盘 ----
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        // ---- SendInput（现代输入 API，WinUI 兼容性优于 keybd_event）----
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        // ---- 前台激活辅助 ----
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);

        // ---- DPI 感知 ----
        // 必须显式声明，否则进程为 DPI-unaware：UIA 返回物理像素，而 GetWindowRect/CopyFromScreen
        // 走系统虚拟化坐标，两者混用会导致截图截错区域（多显示器 + 缩放环境必现）。
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        // 实际生效的 DPI 模式（none / system / per-monitor / per-monitor-v2）
        public static string DpiMode = "unknown";
        public const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

        // 按能力从高到低尝试；老系统缺新 API 会抛 DllNotFound/EntryPointNotFound，逐级回退。
        public static void EnableDpiAwareness() {
            DpiMode = "none";
            try {
                if (SetProcessDpiAwarenessContext(new IntPtr(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))) {
                    DpiMode = "per-monitor-v2"; return;
                }
            } catch { }
            try {
                if (SetProcessDpiAwareness(2) == 0) { DpiMode = "per-monitor"; return; } // PROCESS_PER_MONITOR_DPI_AWARE
            } catch { }
            try {
                if (SetProcessDPIAware()) { DpiMode = "system"; return; }
            } catch { }
        }

        // ---- 进程/会话信息（status 命令）----
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int ret);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool ProcessIdToSessionId(int pid, out int sessionId);
        [DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(uint threadId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int len, out int needed);
        public const uint TOKEN_QUERY = 0x0008;
        public const int TokenElevation = 20;

        // 当前进程是否已提权（UIPI 决定能否驱动提权窗口）
        public static bool IsElevated() {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out token)) return false;
            try {
                int info, ret;
                if (GetTokenInformation(token, TokenElevation, out info, 4, out ret)) return info != 0;
                return false;
            } finally { CloseHandle(token); }
        }

        // 进程所在会话 + 桌面名（交互式桌面才能做 UIA/截图）
        public static int SessionId() {
            int sid;
            return ProcessIdToSessionId(System.Diagnostics.Process.GetCurrentProcess().Id, out sid) ? sid : -1;
        }

        public static string DesktopName() {
            try {
                var sb = new StringBuilder(256);
                int needed;
                if (GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2 /*UOI_NAME*/, sb, 512, out needed))
                    return sb.ToString();
            } catch { }
            return "";
        }

        // ---- 遮挡检测（Occlusion.cs）----
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        public const uint GA_ROOT = 2;
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
        [DllImport("user32.dll")] public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        // ---- shell 表面定位 ----
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        // ---- 监视器 DPI（ocr 自适应上采样用）----
        // 高 DPI 屏上系统已按缩放比渲染文字（150% 屏 12px 字实际 18px），
        // 再不识别地放大 2 倍只会把字糊掉、并让耗时线性增长。
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmon, int dpiType, out uint dpiX, out uint dpiY);
        public const uint MONITOR_DEFAULTTONEAREST = 0x0002;

        // 窗口自身 DPI；老系统缺 GetDpiForWindow 时回退到所在监视器
        public static double DpiOfWindow(IntPtr hWnd) {
            try {
                uint d = GetDpiForWindow(hWnd);
                if (d > 0) return d;
            } catch { }
            RECT r;
            if (GetWindowRect(hWnd, out r)) return DpiOfRect(r);
            return 96.0;
        }

        // 矩形中心所在监视器的有效 DPI；任何一步失败都退回 96（100%）
        public static double DpiOfRect(RECT r) {
            try {
                var mon = MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST);
                uint dx, dy;
                if (GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx;
            } catch { }
            return 96.0;
        }

        // ---- MSAA(IAccessible) 回退通道 ----
        // 用途：Chromium/Electron/WebView2 的 UIA 树极浅（实测 6-13 个元素），
        // 因为它们对"无条件全树遍历"不激活渲染进程的无障碍树；
        // 而传统 MSAA 通道（screen reader 老通道）往往能挖出更多内容。
        [DllImport("oleacc.dll")]
        public static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint dwId, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] ref object ppvObject);
        public const uint OBJID_CLIENT = 0xFFFFFFFC;
        public static readonly Guid IID_IAccessible = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");

        // ---- 剪贴板（Win32 直操作，避开 OLE/消息泵依赖）----
        [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] public static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr GetClipboardData(uint uFormat);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalFree(IntPtr hMem);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr hMem);
        public const uint CF_UNICODETEXT = 13;
        public const uint GMEM_MOVEABLE = 0x0002;

        // ---- 常量 ----
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const int SW_RESTORE = 9;
        public const int SW_MINIMIZE = 6;
        public const int SW_MAXIMIZE = 3;
        public const int SW_HIDE = 0;
        public const int SW_NORMAL = 1;
        public const int SW_SHOW = 5;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
    }
}
