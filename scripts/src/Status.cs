// Status.cs —status 命令：一次性报出"环境是否具备前提条件"
//
// 动机：UIA 自动化失败的原因高度集中在几件事上——没提权、目标在别的会话、
// 桌面被锁、DPI 模式不对、OCR 语言包缺失、shell 表面不可用。
// 这些以前只能靠文档猜，现在一条命令全查出来。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace WinHarness {
    static class Status {
        public static Dictionary<string, object> Collect(bool includeSurfaces) {
            var d = new Dictionary<string, object>();

            // 进程/会话
            d["pid"] = Process.GetCurrentProcess().Id;
            d["arch"] = Environment.Is64BitProcess ? "x64" : "x86";
            d["clr"] = Environment.Version.ToString();
            d["os"] = Environment.OSVersion.Version.ToString();
            // Environment.OSVersion 在无 manifest 时会谎报 6.2（Win8）；真实 build 从注册表取
            try {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) {
                    if (k != null) {
                        string build = k.GetValue("CurrentBuild") as string;
                        object ubr = k.GetValue("UBR");
                        string disp = k.GetValue("DisplayVersion") as string;
                        string name = k.GetValue("ProductName") as string;
                        d["os_version"] = (name ?? "") + " " + (disp ?? "");
                        d["os_build"] = (build ?? "") + (ubr == null ? "" : "." + ubr);
                    }
                }
            } catch { }
            d["session_id"] = Native.SessionId();
            d["interactive_desktop"] = Native.DesktopName();
            d["elevated"] = Native.IsElevated();
            if (!Native.IsElevated())
                d["elevation_note"] = "未提权：无法驱动以管理员运行的窗口（UIPI 会拦输入）；需要时从管理员终端启动";

            // 坐标/显示
            d["dpi_mode"] = Native.DpiMode;
            var mons = new List<object>();
            foreach (var s in Screen.AllScreens) {
                var m = new Dictionary<string, object>();
                m["device"] = s.DeviceName;
                m["primary"] = s.Primary;
                m["bounds"] = new Dictionary<string, object> {
                    { "x", s.Bounds.X }, { "y", s.Bounds.Y }, { "w", s.Bounds.Width }, { "h", s.Bounds.Height }
                };
                try {
                    var r = new Native.RECT { Left = s.Bounds.Left, Top = s.Bounds.Top, Right = s.Bounds.Right, Bottom = s.Bounds.Bottom };
                    m["dpi"] = (int)Math.Round(Native.DpiOfRect(r));
                } catch { }
                mons.Add(m);
            }
            d["monitors"] = mons;
            d["virtual_screen"] = new Dictionary<string, object> {
                { "x", SystemInformation.VirtualScreen.X }, { "y", SystemInformation.VirtualScreen.Y },
                { "w", SystemInformation.VirtualScreen.Width }, { "h", SystemInformation.VirtualScreen.Height }
            };

            // OCR 能力
            var langs = OcrRunner.AvailableLanguages();
            d["ocr_languages"] = langs;
            if (langs.Count == 0)
                d["ocr_note"] = "系统未安装任何 OCR 语言包：ocr / clicktext 不可用，请在 设置→时间和语言→语言 中添加语言功能";

            // 前台窗口
            try {
                var fg = Native.GetForegroundWindow();
                if (fg != IntPtr.Zero) {
                    var sb = new StringBuilder(256);
                    Native.GetWindowText(fg, sb, 256);
                    uint pid;
                    Native.GetWindowThreadProcessId(fg, out pid);
                    string proc = "";
                    try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { }
                    d["foreground"] = new Dictionary<string, object> {
                        { "hwnd", fg.ToInt64() }, { "title", sb.ToString() }, { "pid", pid }, { "proc", proc }
                    };
                }
            } catch { }

            // trace
            d["trace_enabled"] = TraceLog.Enabled;
            d["trace_dir"] = TraceLog.Dir ?? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "win-harness", "trace");

            if (includeSurfaces) d["surfaces"] = ShellSurface.List();
            return d;
        }
    }
}
