// Codes.cs — 错误码 / 重试建议 / NDJSON 轨迹日志
//
// 目的：让 agent 能机械决策，而不是读自然语言猜。
//   - 错误码：区分「改条件重查」「等一会重试」「必须人来处理」
//   - retry：safe / reprobe / never，决定该不该自动重试
//   - trace：把一次调用链（含 do 批处理每一步）落 NDJSON，长流程可回溯
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WinHarness {
    // 带错误码的异常：捕获处能直接拿到 code/retry，不必解析消息文本
    class WhException : Exception {
        public string Code;
        public string Retry;
        public WhException(string code, string retry, string msg) : base(msg) {
            Code = code;
            Retry = retry;
        }
    }

    static class ErrorCodes {
        public const string NotFound = "NOT_FOUND";
        public const string Ambiguous = "AMBIGUOUS";
        public const string Offscreen = "OFFSCREEN";
        public const string PermDenied = "PERM_DENIED";
        public const string EmptyOcr = "EMPTY_OCR";
        public const string OcrMinimized = "OCR_MINIMIZED";
        public const string BlockedShortcut = "BLOCKED_SHORTCUT";
        public const string BadArgs = "BAD_ARGS";
        public const string Timeout = "TIMEOUT";
        public const string Internal = "INTERNAL";

        // 兜底分类：老 throw 站点还没改成 WhException 时，按消息/HRESULT 猜一个可用的码。
        // 宁可给个粗粒度码，也不要让调用方只能读中文。
        public static void Classify(Exception ex, out string code, out string retry) {
            var we = ex as WhException;
            if (we != null) { code = we.Code; retry = we.Retry; return; }

            string m = ex.Message ?? "";
            uint hr = (uint)ex.HResult;
            code = Internal; retry = "never";

            if (hr == 0x80070005 || m.Contains("拒绝访问") || m.Contains("Access is denied")) {
                code = PermDenied; retry = "never";
            } else if (m.Contains("匹配到") || m.Contains("歧义") || m.Contains("候选")) {
                code = Ambiguous; retry = "never";      // 必须人来收敛条件
            } else if (m.Contains("未找到") || m.Contains("找不到") || m.Contains("不存在")) {
                code = NotFound; retry = "reprobe";     // 界面变了，重新拍树
            } else if (m.Contains("已最小化")) {
                code = OcrMinimized; retry = "safe";    // focus 还原后可重试
            } else if (m.Contains("超时")) {
                code = Timeout; retry = "safe";
            } else if (m.Contains("缺少 -") || m.Contains("坐标不完整") || m.Contains("需要 -")) {
                code = BadArgs; retry = "never";
            }
        }
    }

    // NDJSON 轨迹：每次调用一行，按天分文件。
    // 开启方式：命令加 -Trace，或环境变量 WIN_HARNESS_TRACE=1
    static class TraceLog {
        public static bool Enabled;
        static string _dir;
        static readonly string TraceId = Guid.NewGuid().ToString("N").Substring(0, 8);
        static int _seq;

        // 会被脱敏的参数：文本内容与命令行可能含凭据；定位用的 Name/AutomationId 保留
        static readonly string[] SecretKeys = { "text", "textb64", "args", "value", "clipboard", "stdin" };

        public static void Init(Dictionary<string, string> opts) {
            Enabled = opts.ContainsKey("Trace")
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WIN_HARNESS_TRACE"));
            if (!Enabled) return;

            string d;
            opts.TryGetValue("TraceDir", out d);
            _dir = string.IsNullOrEmpty(d)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "win-harness", "trace")
                : d;
            try { Directory.CreateDirectory(_dir); } catch { }
            Prune();
        }

        public static string Dir { get { return _dir; } }

        public static void Write(string cmd, IDictionary<string, string> args, bool ok, long ms,
                                 string code, string retry, string error,
                                 int step, int of, Dictionary<string, object> extra) {
            if (!Enabled || string.IsNullOrEmpty(_dir)) return;
            try {
                var sb = new StringBuilder();
                sb.Append("{\"ts\":\"").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff")).Append('"');
                sb.Append(",\"seq\":").Append(++_seq);
                sb.Append(",\"trace\":\"").Append(TraceId).Append('"');
                sb.Append(",\"cmd\":\"").Append(Esc(cmd)).Append('"');
                if (step > 0) { sb.Append(",\"step\":").Append(step).Append(",\"of\":").Append(of); }
                if (args != null && args.Count > 0) {
                    sb.Append(",\"args\":{");
                    bool first = true;
                    foreach (var kv in args) {
                        if (kv.Key.Equals("Trace", StringComparison.OrdinalIgnoreCase)
                            || kv.Key.Equals("TraceDir", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append('"').Append(Esc(kv.Key)).Append("\":");
                        if (IsSecret(kv.Key))
                            sb.Append("\"<redacted:").Append(kv.Value == null ? 0 : kv.Value.Length).Append(">\"");
                        else
                            sb.Append('"').Append(Esc(kv.Value)).Append('"');
                    }
                    sb.Append('}');
                }
                sb.Append(",\"ok\":").Append(ok ? "true" : "false");
                sb.Append(",\"ms\":").Append(ms);
                if (!string.IsNullOrEmpty(code)) sb.Append(",\"code\":\"").Append(Esc(code)).Append('"');
                if (!string.IsNullOrEmpty(retry)) sb.Append(",\"retry\":\"").Append(Esc(retry)).Append('"');
                if (!string.IsNullOrEmpty(error)) sb.Append(",\"error\":\"").Append(Esc(error)).Append('"');
                if (extra != null) {
                    foreach (var kv in extra) {
                        sb.Append(",\"").Append(Esc(kv.Key)).Append("\":").Append(MiniJson.Serialize(kv.Value));
                    }
                }
                sb.Append("}\n");
                File.AppendAllText(PathFor(DateTime.Now), sb.ToString(), new UTF8Encoding(false));
            } catch { }   // trace 绝不能影响主流程
        }

        static bool IsSecret(string k) {
            string lk = k.ToLowerInvariant();
            return SecretKeys.Any(s => lk == s || lk == s + "b64");
        }

        static string Esc(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s) {
                switch (c) {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        static string PathFor(DateTime d) {
            return Path.Combine(_dir, d.ToString("yyyyMMdd") + ".ndjson");
        }

        // 只保留最近 7 天，避免无限增长
        static void Prune() {
            try {
                foreach (var f in Directory.GetFiles(_dir, "*.ndjson")) {
                    if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7)) File.Delete(f);
                }
            } catch { }
        }

        // ---- 回读 ----
        public static string[] ReadLines(DateTime from) { return ReadFrom(_dir, from).ToArray(); }

        public static List<string> ReadFrom(string dir, DateTime from) {
            var list = new List<string>();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;
            for (var d = from.Date; d <= DateTime.Now.Date; d = d.AddDays(1)) {
                string p = Path.Combine(dir, d.ToString("yyyyMMdd") + ".ndjson");
                if (!File.Exists(p)) continue;
                try { list.AddRange(File.ReadAllLines(p)); } catch { }
            }
            return list;
        }

        // 把结果里的关键字段提到 trace 行顶层，便于直接 grep（occluded / capture / count ...）
        public static Dictionary<string, object> ExtraFrom(object data) {
            var d = data as Dictionary<string, object>;
            if (d == null) return null;
            var e = new Dictionary<string, object>();
            foreach (var k in new[] { "occluded", "capture", "count", "upscale", "engine", "backend", "code" }) {
                object v;
                if (d.TryGetValue(k, out v) && v != null) e[k] = v;
            }
            return e.Count == 0 ? null : e;
        }
    }
}
