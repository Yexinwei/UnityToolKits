using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Limbo.CurveMotion.Editor.Export
{
    /// <summary>生成 C# 代码用的字面量与标识符。数字用能精确还原的最短写法，与区域设置无关。</summary>
    public static class CSharpLiterals
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>float 字面量，例如 0.5f、-12f、0.4166667f。</summary>
        public static string Float(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0f";
            if (value == 0f) return "0f";
            for (int decimals = 0; decimals <= 9; decimals++)
            {
                string s = value.ToString("F" + decimals, Invariant);
                if (float.Parse(s, Invariant) == value) return Trim(s) + "f";
            }
            return value.ToString("R", Invariant) + "f";
        }

        static string Trim(string s) => s.Contains(".") ? s.TrimEnd('0').TrimEnd('.') : s;

        /// <summary>按维度返回值类型：1 = float，2~4 = Vector2~Vector4。</summary>
        public static string ValueType(int dimension)
        {
            switch (dimension)
            {
                case 1: return "float";
                case 2: return "Vector2";
                case 3: return "Vector3";
                default: return "Vector4";
            }
        }

        /// <summary>取 value 的前 dimension 个分量写成字面量，例如 new Vector3(1.2f, 1.2f, 1f)。</summary>
        public static string Value(Vector4 value, int dimension)
        {
            if (dimension <= 1) return Float(value.x);
            var sb = new StringBuilder("new ").Append(ValueType(dimension)).Append('(');
            for (int i = 0; i < dimension && i < 4; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Float(value[i]));
            }
            return sb.Append(')').ToString();
        }

        public static string Vector3(Vector3 v) => "new Vector3(" + Float(v.x) + ", " + Float(v.y) + ", " + Float(v.z) + ")";

        /// <summary>C# 字符串字面量（带引号和转义）。</summary>
        public static string String(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", Invariant));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>注释里的一行文字：去掉换行，避免提前结束注释。</summary>
        public static string Comment(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("*/", "* /").Trim();

        /// <summary>
        /// 由动效名得到类名：按非字母数字拆开、每段首字母大写，末尾加 Motion，例如 demo-coin-fly → DemoCoinFlyMotion。
        /// 中文等非 ASCII 字母也保留（C# 允许）；什么都不剩时用 CurveMotion。
        /// </summary>
        public static string ClassName(string name)
        {
            var sb = new StringBuilder();
            bool upper = true;
            foreach (char c in name ?? "")
            {
                if (!char.IsLetterOrDigit(c) || c > 0x7f && !char.IsLetter(c))
                {
                    upper = true;
                    continue;
                }
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            if (sb.Length == 0) return "CurveMotion";
            if (char.IsDigit(sb[0])) sb.Insert(0, "Motion");
            string result = sb.ToString();
            return result.EndsWith("Motion", StringComparison.Ordinal) ? result : result + "Motion";
        }

        /// <summary>是否是合法的 C# 标识符（不检查关键字以外的冲突）。</summary>
        public static bool IsIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s) || !(char.IsLetter(s[0]) || s[0] == '_')) return false;
            foreach (char c in s)
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return Array.IndexOf(Keywords, s) < 0;
        }

        static readonly string[] Keywords =
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
            "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
            "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void",
            "volatile", "while",
        };
    }
}
