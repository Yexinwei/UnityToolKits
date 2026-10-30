using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using L = Limbo.CurveMotion.Editor.Export.CSharpLiterals;

namespace Limbo.CurveMotion.Editor.Export
{
    /// <summary>
    /// 事件处理文件（X.Events.cs）：导出的类是 partial 的，每个事件名对应一个 static void OnXxx(Transform target)，
    /// 方法写在这个单独的文件里，开发只填方法体。
    /// 这个文件只在第一次导出时整份生成，之后重新导出只追加新事件的方法，已有代码不动；
    /// 事件改名或删除后留下的旧方法不会报错（没人调用而已），导出时提示一下。
    /// </summary>
    public static class EventStubs
    {
        public struct Handler
        {
            public string method;
            public string eventName;
            public float time;
            public string message;
        }

        public sealed class MergeResult
        {
            public string code;
            public readonly List<string> added = new List<string>();
            public readonly List<string> unused = new List<string>();
        }

        static readonly Regex s_MethodPattern = new Regex(@"\bstatic\s+void\s+(\w+)\s*\(\s*Transform\s+\w+\s*\)");

        /// <summary>事件名对应的方法名：PascalCase，前面加 On（名字本身已是 OnXxx 时不再加）。</summary>
        public static string MethodName(string eventName)
        {
            string pascal = L.Pascal(string.IsNullOrEmpty(eventName) ? "Event" : eventName);
            if (pascal.Length == 0) pascal = "Event";
            if (pascal.Length > 2 && pascal.StartsWith("On", StringComparison.Ordinal) && char.IsUpper(pascal[2])) return pascal;
            return "On" + pascal;
        }

        /// <summary>按出现顺序列出事件处理方法，同名事件（含转换后同名）合成一个，时间和说明取第一个。</summary>
        public static List<Handler> Handlers(MotionData data, MotionEvaluator eval)
        {
            var list = new List<Handler>();
            var seen = new HashSet<string>();
            for (int i = 0; i < data.events.Count; i++)
            {
                var e = data.events[i];
                string method = MethodName(e.name);
                if (!seen.Add(method)) continue;
                list.Add(new Handler
                {
                    method = method,
                    eventName = string.IsNullOrEmpty(e.name) ? "Event" : e.name,
                    time = eval.GetEventTime(i),
                    message = e.message ?? "",
                });
            }
            return list;
        }

        /// <summary>事件处理文件的默认文件名：Foo.cs → Foo.Events.cs。</summary>
        public static string FileName(string className) => className + ".Events.cs";

        /// <summary>整份生成事件处理文件。withUsings 为 false 时不写 using，用于拼在导出代码后面一起复制。</summary>
        public static string Generate(MotionData data, string className, string ns, bool withUsings = true)
        {
            var eval = new MotionEvaluator(data);
            var sb = new StringBuilder();
            if (withUsings)
            {
                sb.Append("// ").Append(className).Append(" 的事件处理：每个事件一个方法，在方法里写要做的事（播特效、音效、震屏、加分……）。\n");
                sb.Append("// 这个文件只在第一次导出时生成，之后重新导出只会在末尾追加新事件的方法，不会覆盖这里写的代码。\n");
                sb.Append("using UnityEngine;\n\n");
            }
            string indent = "";
            if (!string.IsNullOrWhiteSpace(ns))
            {
                sb.Append("namespace ").Append(ns.Trim()).Append("\n{\n");
                indent = "    ";
            }
            sb.Append(indent).Append("public static partial class ").Append(className).Append("\n");
            sb.Append(indent).Append("{\n");
            var handlers = Handlers(data, eval);
            for (int i = 0; i < handlers.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                AppendMethod(sb, indent + "    ", className, handlers[i]);
            }
            sb.Append(indent).Append("}\n");
            if (indent.Length > 0) sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// 在已有的事件处理文件里补上缺的方法（插在类的右括号前），并找出已没有对应事件的方法。
        /// 找不到类的右括号时代码原样返回，added 里仍列出缺的方法（调用方据此提示手动补）。
        /// </summary>
        public static MergeResult Merge(string existing, MotionData data, string className, string ns)
        {
            var result = new MergeResult { code = existing };
            var eval = new MotionEvaluator(data);
            var handlers = Handlers(data, eval);

            var present = new HashSet<string>();
            foreach (Match m in s_MethodPattern.Matches(existing)) present.Add(m.Groups[1].Value);
            var wanted = new HashSet<string>();
            foreach (var h in handlers) wanted.Add(h.method);
            foreach (var name in present)
                if (!wanted.Contains(name)) result.unused.Add(name);
            result.unused.Sort(StringComparer.Ordinal);

            var missing = handlers.FindAll(h => !present.Contains(h.method));
            if (missing.Count == 0) return result;

            // 类的右括号：有命名空间时是倒数第二个 }，否则是最后一个。
            int close = existing.LastIndexOf('}');
            if (close >= 0 && !string.IsNullOrWhiteSpace(ns)) close = existing.LastIndexOf('}', close - 1);
            if (close < 0)
            {
                foreach (var h in missing) result.added.Add(h.method);
                return result;
            }
            int lineStart = existing.LastIndexOf('\n', close) + 1;
            string indent = existing.Substring(lineStart, close - lineStart);
            if (indent.Trim().Length > 0) indent = "";

            var sb = new StringBuilder();
            foreach (var h in missing)
            {
                sb.Append('\n');
                AppendMethod(sb, indent + "    ", className, h);
                result.added.Add(h.method);
            }
            // 插在右括号所在行之前：前一个方法和新方法之间空一行。
            string before = existing.Substring(0, lineStart).TrimEnd('\n', '\r', ' ', '\t') + "\n";
            result.code = before + sb + existing.Substring(lineStart);
            return result;
        }

        static void AppendMethod(StringBuilder sb, string indent, string className, Handler h)
        {
            sb.Append(indent).Append("/// <summary>\n");
            sb.Append(indent).Append("/// 事件 ").Append(Xml(h.eventName)).Append("：").Append(Seconds(h.time)).Append(" 秒触发\n");
            if (h.message.Length > 0)
                sb.Append(indent).Append("/// 特效说明：").Append(Xml(L.Comment(h.message))).Append('\n');
            sb.Append(indent).Append("/// </summary>\n");
            sb.Append(indent).Append("/// <param name=\"target\">正在播放动效的对象。</param>\n");
            sb.Append(indent).Append("static void ").Append(h.method).Append("(Transform target)\n");
            sb.Append(indent).Append("{\n");
            sb.Append(indent).Append("    // TODO 在这里写事件要做的事，写好后删掉下面这行提示。\n");
            var log = new StringBuilder();
            log.Append("<color=#FF8C00><b>【动效事件未接入】</b></color> ").Append(className).Append(" · ").Append(h.eventName).Append("（").Append(Seconds(h.time)).Append("s）");
            if (h.message.Length > 0) log.Append("\n特效说明：").Append(h.message);
            log.Append("\n在 ").Append(FileName(className)).Append(" 的 ").Append(h.method).Append(" 里实现");
            sb.Append(indent).Append("    Debug.LogWarning(").Append(L.String(log.ToString())).Append(", target);\n");
            sb.Append(indent).Append("}\n");
        }

        static string Seconds(float t) => t.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        static string Xml(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
