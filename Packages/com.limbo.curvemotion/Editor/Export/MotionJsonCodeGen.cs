using System;
using System.Collections.Generic;
using System.IO;
using Limbo.CurveMotion.Serialization;
using L = Limbo.CurveMotion.Editor.Export.CSharpLiterals;

namespace Limbo.CurveMotion.Editor.Export
{
    /// <summary>
    /// 工程里的 &lt;名字&gt;.motion.json → 导出代码（只做计算，不读写文件）。
    /// 类名取 JSON 里 export.className，没有时由文件名生成（coin-fly.motion.json → CoinFlyMotion）；
    /// 命名空间只取 export.namespace，不用编辑器里的设置，保证每台机器生成的一样。
    /// </summary>
    public static class MotionJsonCodeGen
    {
        public const string Suffix = ".motion.json";

        public sealed class Result
        {
            public MotionData data;
            public string className;
            public string ns;
            public string code;
            public readonly List<string> warnings = new List<string>();
        }

        public static bool IsMotionJson(string path) => path != null && path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);

        /// <summary>coin-fly.motion.json → coin-fly。</summary>
        public static string BaseName(string path)
        {
            string file = Path.GetFileName(path);
            return file.Length > Suffix.Length ? file.Substring(0, file.Length - Suffix.Length) : file;
        }

        /// <summary>JSON 格式错误时抛 FormatException。</summary>
        public static Result Generate(string json, string jsonPath)
        {
            var r = new Result();
            r.data = MotionJson.FromJson(json, r.warnings);
            MotionJson.ReadExport(json, out string ns, out string cls);

            r.ns = (ns ?? "").Trim();
            if (r.ns.Length > 0 && !IsNamespace(r.ns))
            {
                r.warnings.Add($"export.namespace “{r.ns}” 不是合法的命名空间，已改用全局命名空间");
                r.ns = "";
            }

            string fallback = L.ClassName(BaseName(jsonPath));
            r.className = string.IsNullOrWhiteSpace(cls) ? fallback : cls.Trim();
            if (!L.IsIdentifier(r.className))
            {
                r.warnings.Add($"export.className “{r.className}” 不是合法的类名，已改用 {fallback}");
                r.className = fallback;
            }

            r.code = DotweenExporter.Generate(r.data, new DotweenExportOptions
            {
                ClassName = r.className,
                Namespace = r.ns,
                SourceFile = Path.GetFileName(jsonPath),
                WarningsInHeader = true,
            }, r.warnings);
            return r;
        }

        static bool IsNamespace(string ns)
        {
            foreach (var part in ns.Split('.'))
                if (!L.IsIdentifier(part)) return false;
            return true;
        }
    }
}
