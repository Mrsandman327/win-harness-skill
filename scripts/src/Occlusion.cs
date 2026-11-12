// Occlusion.cs — 遮挡检测
//
// 为什么需要：截图与 OCR 都走 Graphics.CopyFromScreen（抓屏幕像素），
// 目标一旦被其他窗口盖住，就会**静默读到别人的内容**，调用方无从察觉。
// 这里用网格采样 + WindowFromPoint 判断每个采样点的真实归属窗口，算出遮挡比例。
//
// 采样而非矩形相交的原因：真实窗口常是非矩形（圆角、异形、分层窗口、伴随 DWM 阴影），
// 直接做矩形交集会把"看起来覆盖但穿透"的区域算错；采样按实际命中算，代价是几十毫秒。
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace WinHarness {
    static class Occlusion {
        // 采样密度：24x24 = 576 次 WindowFromPoint，实测 <15ms，精度对"是否被盖住"足够
        const int GX = 24, GY = 24;

        public static Dictionary<string, object> Measure(IntPtr target, Native.RECT rect) {
            int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0) return null;

            var targetRoot = Root(target);
            uint targetPid;
            Native.GetWindowThreadProcessId(targetRoot, out targetPid);
            string targetTitle = TitleOf(targetRoot);

            int covered = 0, total = 0;
            var hits = new Dictionary<IntPtr, int>();

            for (int iy = 0; iy < GY; iy++) {
                int y = rect.Top + (int)((iy + 0.5) * h / GY);
                for (int ix = 0; ix < GX; ix++) {
                    int x = rect.Left + (int)((ix + 0.5) * w / GX);
                    var p = new Native.POINT { X = x, Y = y };
                    IntPtr hw = Native.WindowFromPoint(p);
                    if (hw == IntPtr.Zero) continue;          // 该点无窗口（桌面空白）
                    var root = Root(hw);
                    total++;

                    // 判定「同一应用」：UWP/WinUI 的一个应用常由多个顶层窗口组成
                    // （ApplicationFrameHost 框架 + CoreWindow + XAML 弹出层），
                    // 若不排除，会把应用自己的部件当成遮挡者，产生假阳性。
                    if (root == targetRoot) continue;
                    uint pid;
                    Native.GetWindowThreadProcessId(root, out pid);
                    if (pid == targetPid) continue;
                    if (Native.IsChild(targetRoot, hw) || Native.IsChild(root, target)) continue;
                    string t = TitleOf(root);
                    if (!string.IsNullOrEmpty(targetTitle) && t == targetTitle) continue;

                    covered++;
                    int c;
                    hits.TryGetValue(root, out c);
                    hits[root] = c + 1;
                }
            }
            if (total == 0) return null;

            double ratio = (double)covered / total;

            // 覆盖者详情，按覆盖点数降序，最多 3 个
            var list = new List<KeyValuePair<IntPtr, int>>(hits);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));

            var covered_by = new List<object>();
            for (int i = 0; i < list.Count && i < 3; i++) {
                var hw = list[i].Key;
                var d = new Dictionary<string, object>();
                d["hwnd"] = hw.ToInt64();
                uint pid;
                Native.GetWindowThreadProcessId(hw, out pid);
                var sb = new System.Text.StringBuilder(256);
                Native.GetWindowText(hw, sb, 256);
                d["title"] = sb.ToString();
                string proc = "";
                try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { }
                d["proc"] = proc;
                Native.RECT r;
                if (Native.GetWindowRect(hw, out r)) {
                    d["rect"] = new Dictionary<string, object> {
                        { "x", r.Left }, { "y", r.Top },
                        { "w", r.Right - r.Left }, { "h", r.Bottom - r.Top }
                    };
                }
                d["ratio"] = Math.Round((double)list[i].Value / total, 3);
                covered_by.Add(d);
            }

            var res = new Dictionary<string, object>();
            res["occluded"] = Math.Round(ratio, 3);
            res["covered_samples"] = covered + "/" + total;
            if (covered_by.Count > 0) res["covered_by"] = covered_by;
            return res;
        }

        static IntPtr Root(IntPtr h) {
            var r = Native.GetAncestor(h, Native.GA_ROOT);
            return r == IntPtr.Zero ? h : r;
        }

        static string TitleOf(IntPtr h) {
            var sb = new System.Text.StringBuilder(256);
            Native.GetWindowText(h, sb, 256);
            return sb.ToString();
        }

        // -Raise：先把目标置前台，等 DWM 完成合成，再重测。
        // 必须复用 Uia.FocusWindow（ALT 键技巧 + AttachThreadInput）：裸 SetForegroundWindow
        // 受前台锁限制，从后台进程调用几乎必然失败——实测它返回后窗口仍在原位。
        public static void Raise(IntPtr hwnd) {
            try {
                if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
                var el = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                if (el != null) Uia.FocusWindow(el);
                else Native.SetForegroundWindow(hwnd);
            } catch { }
            System.Threading.Thread.Sleep(220);
        }
    }
}
