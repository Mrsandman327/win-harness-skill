// ShellSurface.cs — 任务栏 / 桌面 / 开始菜单 / 托盘溢出 / 通知中心
//
// 为什么单独做：这些窗口不出现于 UIA RootElement 的 Window 子节点里（实测 windows 命令只列出
// 7 个普通窗口，Shell_TrayWnd 不在其中），调用方拿不到 hwnd 就只能硬编码，版本一变即失效。
// 定位到 hwnd 后，tree / find / click / ocr 等现有命令全部可直接使用。
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace WinHarness {
    static class ShellSurface {
        public static readonly string[] Names = {
            "taskbar", "taskbar2", "desktop", "start", "overflow", "notifications"
        };

        static readonly string[] ChevronWords = { "隐藏", "hidden", "Hidden", "顯示", "显示隐藏" };

        public static long Find(string name) {
            name = (name ?? "").ToLowerInvariant();
            switch (name) {
                case "taskbar":   return Native.FindWindow("Shell_TrayWnd", null).ToInt64();
                case "taskbar2":  return Native.FindWindow("Shell_SecondaryTrayWnd", null).ToInt64();
                case "desktop": {
                    long h = Native.FindWindow("Progman", null).ToInt64();
                    return h != 0 ? h : Native.FindWindow("WorkerW", null).ToInt64();
                }
                case "overflow": {
                    long h = Native.FindWindow("NotifyIconOverflowWindow", null).ToInt64();
                    if (h == 0) h = Native.FindWindow("TopLevelWindowForOverflowXamlIsland", null).ToInt64();
                    return h;
                }
                case "start": {
                    // Win11 24H2+ 开始菜单是 Wnd_StartFeed（旧式 CoreWindow 已不再是开始菜单，
                    // 实测同标题的 CoreWindow 其实是「设置」）。两种都试，取可见者。
                    long h = Native.FindWindow("Wnd_StartFeed", null).ToInt64();
                    if (h != 0 && !Native.IsWindowVisible(new IntPtr(h))) h = 0;
                    if (h == 0) h = FindCoreWindow(new[] { "开始", "Start" }, null);
                    return h;
                }
                case "notifications": return FindCoreWindow(null, new[] { "通知", "Notification", "Action Center" });
                default: return 0;
            }
        }

        // 枚举 XAML 宿主窗口（开始菜单 / 通知中心都是 Windows.UI.Core.CoreWindow）
        static long FindCoreWindow(string[] exactTitles, string[] titleContains) {
            long found = 0;
            Native.EnumWindows(delegate(IntPtr h, IntPtr l) {
                if (!Native.IsWindowVisible(h)) return true;
                var sb = new StringBuilder(256);
                Native.GetClassName(h, sb, 256);
                if (sb.ToString() != "Windows.UI.Core.CoreWindow") return true;
                var t = new StringBuilder(256);
                Native.GetWindowText(h, t, 256);
                string title = t.ToString();
                if (string.IsNullOrEmpty(title)) return true;

                bool match = false;
                if (exactTitles != null) {
                    foreach (var e in exactTitles) if (title == e) { match = true; break; }
                }
                if (!match && titleContains != null) {
                    foreach (var e in titleContains)
                        if (title.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
                }
                if (match) { found = h.ToInt64(); return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static List<object> List() {
            var list = new List<object>();
            foreach (var n in Names) {
                var d = new Dictionary<string, object>();
                long h = Find(n);
                d["name"] = n;
                d["present"] = h != 0;
                if (h != 0) {
                    d["hwnd"] = h;
                    Native.RECT r;
                    if (Native.GetWindowRect(new IntPtr(h), out r)) {
                        d["rect"] = new Dictionary<string, object> {
                            { "x", r.Left }, { "y", r.Top }, { "w", r.Right - r.Left }, { "h", r.Bottom - r.Top }
                        };
                    }
                    var sb = new StringBuilder(256);
                    Native.GetClassName(new IntPtr(h), sb, 256);
                    d["class"] = sb.ToString();
                    var tb = new StringBuilder(256);
                    Native.GetWindowText(new IntPtr(h), tb, 256);
                    if (tb.Length > 0) d["title"] = tb.ToString();
                } else {
                    d["openable_by"] = n == "start" ? "open -Surface start（点任务栏开始按钮）"
                        : n == "notifications" ? "open -Surface notifications（点任务栏时钟；Win11 合并了日历）"
                        : n == "overflow" ? "open -Surface overflow（点任务栏『显示隐藏的图标』）"
                        : "通常由系统常驻";
                    // 开始菜单 / 通知中心在本机由 XAML 合成层绘制，没有可寻址窗口
                    if (n == "start" || n == "notifications") d["addressing"] = "none";
                }
                list.Add(d);
            }
            return list;
        }

        // 打开某个 shell 表面。
        // hwnd != 0 → 可寻址，之后用 -Window shell:<name> 操作；
        // hwnd == 0 → 该表面是 XAML 合成层（开始菜单 / 通知中心），没有可寻址窗口，
        //             只能确认"已点击唤起"，后续用 clicktext / screenshot 操作。
        public static void Open(string name, int timeoutMs, out long hwnd, out bool clicked) {
            name = (name ?? "").ToLowerInvariant();
            hwnd = Find(name);
            clicked = false;
            if (hwnd != 0 && name != "start" && name != "notifications" && name != "overflow") return;

            switch (name) {
                case "start":
                    // 合成 WIN 键在本机不生效（SendInput 的 VK_LWIN 未被 shell 采纳），
                    // 改为点任务栏的开始按钮；两者都失败才放弃。
                    if (!ClickTaskbar(new[] { "开始", "Start" }, "StartButton") && !ClickTaskbar(new[] { "开始", "Start" }, "Start"))
                        InputSim.KeyPress("WIN");
                    clicked = true;
                    break;
                case "notifications":
                    // 通知中心 = 点任务栏时钟（Win11 合并了日历/通知）
                    if (!ClickTaskbar(new[] { "时钟", "通知", "Notification", "Clock" }, "NotificationsButton"))
                        InputSim.KeyPress("WIN+N");
                    clicked = true;
                    break;
                case "overflow":
                    ClickChevron();
                    clicked = true;
                    break;
                case "taskbar": case "taskbar2": case "desktop":
                    return;
                default:
                    throw new WhException(ErrorCodes.BadArgs, "never",
                        "未知 -Surface '" + name + "'，可用: " + string.Join(" / ", Names));
            }

            int waited = 0;
            while (waited < timeoutMs) {
                Thread.Sleep(120);
                waited += 120;
                hwnd = Find(name);
                if (hwnd != 0) return;
            }
            hwnd = 0;   // 唤起动作已执行，但没有可寻址窗口（属于预期，不是失败）
        }

        // 点任务栏上名字命中 words（或 AutomationId 命中 aid）的按钮。
        // 返回 false 表示没找到；找不到不猜坐标，避免点到别的托盘图标。
        static bool ClickTaskbar(string[] words, string aid) {
            long tb = Find("taskbar");
            if (tb == 0) return false;
            var root = AutomationElement.FromHandle(new IntPtr(tb));
            var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement e in all) {
                try {
                    if (e.Current.ControlType != ControlType.Button) continue;
                    string n = e.Current.Name ?? "";
                    string a = e.Current.AutomationId ?? "";
                    bool hit = (!string.IsNullOrEmpty(aid) && a == aid);
                    if (!hit) foreach (var w in words) {
                        if (n.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                    }
                    if (!hit) continue;

                    var r = e.Current.BoundingRectangle;
                    if (r.Width <= 0 || r.Height <= 0) continue;
                    int cx = Uia.F(r.X + r.Width / 2), cy = Uia.F(r.Y + r.Height / 2);
                    InputSim.MoveTo(cx, cy);
                    Thread.Sleep(40);
                    Native.mouse_event(Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(30);
                    Native.mouse_event(Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    return true;
                } catch { }
            }
            return false;
        }

        // 点任务栏上的「显示隐藏的图标」chevron。
        // 按名称匹配（中/英/繁），失败才报错——不猜坐标，避免点错到别的托盘图标。
        static void ClickChevron() {
            long tb = Find("taskbar");
            if (tb == 0) throw new WhException(ErrorCodes.NotFound, "never", "找不到任务栏（Shell_TrayWnd）");

            var root = AutomationElement.FromHandle(new IntPtr(tb));
            var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            AutomationElement hit = null;
            foreach (AutomationElement e in all) {
                try {
                    if (e.Current.ControlType != ControlType.Button) continue;
                    string n = e.Current.Name ?? "";
                    foreach (var w in ChevronWords) {
                        if (n.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) { hit = e; break; }
                    }
                    if (hit != null) break;
                } catch { }
            }
            if (hit == null)
                throw new WhException(ErrorCodes.NotFound, "never",
                    "任务栏上找不到『显示隐藏的图标』按钮（系统语言可能不同）。请先 screenshot 确认，或直接 click 该按钮。");

            var r = hit.Current.BoundingRectangle;
            int cx = Uia.F(r.X + r.Width / 2), cy = Uia.F(r.Y + r.Height / 2);
            InputSim.MoveTo(cx, cy);
            Thread.Sleep(40);
            Native.mouse_event(Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);
            Native.mouse_event(Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
    }
}
