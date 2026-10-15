using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Limbo.CurveMotion.Serialization
{
    /// <summary>
    /// 极简 JSON 读写，避免依赖 Newtonsoft（手游工程常因广告 SDK 自带的 Newtonsoft dll 冲突）。
    /// 解析结果：对象 = Dictionary&lt;string, object&gt;，数组 = List&lt;object&gt;，数字 = double。
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var parser = new Parser(text);
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Error("多余的内容");
            return value;
        }

        public static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "0";
            value = Math.Round(value, 5);
            if (value == 0) return "0";
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string m_Text;
            int m_Pos;

            public Parser(string text) { m_Text = text; }

            public bool AtEnd => m_Pos >= m_Text.Length;

            public FormatException Error(string message) => new FormatException($"JSON 解析失败（位置 {m_Pos}）：{message}");

            public void SkipWhitespace()
            {
                while (m_Pos < m_Text.Length && char.IsWhiteSpace(m_Text[m_Pos])) m_Pos++;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw Error("意外结束");
                char c = m_Text[m_Pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error($"无法识别的字符 '{c}'");
                }
            }

            Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>();
                m_Pos++;
                SkipWhitespace();
                if (Peek() == '}') { m_Pos++; return dict; }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw Error("需要字段名");
                    string key = ParseString();
                    SkipWhitespace();
                    if (Peek() != ':') throw Error("需要 ':'");
                    m_Pos++;
                    dict[key] = ParseValue();
                    SkipWhitespace();
                    char c = Peek();
                    m_Pos++;
                    if (c == '}') return dict;
                    if (c != ',') throw Error("需要 ',' 或 '}'");
                }
            }

            List<object> ParseArray()
            {
                var list = new List<object>();
                m_Pos++;
                SkipWhitespace();
                if (Peek() == ']') { m_Pos++; return list; }
                while (true)
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    char c = Peek();
                    m_Pos++;
                    if (c == ']') return list;
                    if (c != ',') throw Error("需要 ',' 或 ']'");
                }
            }

            string ParseString()
            {
                var sb = new StringBuilder();
                m_Pos++;
                while (true)
                {
                    if (AtEnd) throw Error("字符串未结束");
                    char c = m_Text[m_Pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("字符串未结束");
                    char e = m_Text[m_Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (m_Pos + 4 > m_Text.Length) throw Error("\\u 转义不完整");
                            sb.Append((char)int.Parse(m_Text.Substring(m_Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            m_Pos += 4;
                            break;
                        default: throw Error($"未知转义 \\{e}");
                    }
                }
            }

            double ParseNumber()
            {
                int start = m_Pos;
                while (m_Pos < m_Text.Length && "+-0123456789.eE".IndexOf(m_Text[m_Pos]) >= 0) m_Pos++;
                var s = m_Text.Substring(start, m_Pos - start);
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    throw Error($"无效数字 '{s}'");
                return d;
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(m_Text, m_Pos, word, 0, word.Length) != 0) throw Error($"需要 '{word}'");
                m_Pos += word.Length;
            }

            char Peek() => AtEnd ? '\0' : m_Text[m_Pos];
        }
    }

    /// <summary>按写入顺序输出的 JSON 写入器，数字数组写在一行便于阅读。</summary>
    public sealed class JsonWriter
    {
        readonly StringBuilder m_Sb = new StringBuilder();
        readonly Stack<bool> m_First = new Stack<bool>();
        readonly Stack<bool> m_Inline = new Stack<bool>();
        bool m_AfterName;

        public override string ToString() => m_Sb.ToString();

        public JsonWriter BeginObject() { Open('{', false); return this; }
        public JsonWriter EndObject() { Close('}'); return this; }
        public JsonWriter BeginArray(bool inline = false) { Open('[', inline); return this; }
        public JsonWriter EndArray() { Close(']'); return this; }

        public JsonWriter Name(string name)
        {
            Separator();
            Json.WriteString(m_Sb, name);
            m_Sb.Append(": ");
            m_AfterName = true;
            return this;
        }

        public JsonWriter Value(string value)
        {
            Separator();
            if (value == null) m_Sb.Append("null"); else Json.WriteString(m_Sb, value);
            return this;
        }

        public JsonWriter Value(double value) { Separator(); m_Sb.Append(Json.FormatNumber(value)); return this; }
        public JsonWriter Value(int value) { Separator(); m_Sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(bool value) { Separator(); m_Sb.Append(value ? "true" : "false"); return this; }

        public JsonWriter Field(string name, string value) => Name(name).Value(value);
        public JsonWriter Field(string name, double value) => Name(name).Value(value);
        public JsonWriter Field(string name, int value) => Name(name).Value(value);
        public JsonWriter Field(string name, bool value) => Name(name).Value(value);

        public JsonWriter Numbers(string name, params float[] values)
        {
            Name(name).BeginArray(true);
            foreach (var v in values) Value(v);
            return EndArray();
        }

        void Open(char c, bool inline)
        {
            Separator();
            m_Sb.Append(c);
            m_First.Push(true);
            m_Inline.Push(inline || (m_Inline.Count > 0 && m_Inline.Peek()));
        }

        void Close(char c)
        {
            bool empty = m_First.Pop();
            bool inline = m_Inline.Pop();
            if (!empty && !inline) NewLine();
            m_Sb.Append(c);
        }

        void Separator()
        {
            if (m_AfterName) { m_AfterName = false; return; }
            if (m_First.Count == 0) return;
            bool first = m_First.Pop();
            if (!first) m_Sb.Append(m_Inline.Peek() ? ", " : ",");
            m_First.Push(false);
            if (!m_Inline.Peek()) NewLine();
        }

        void NewLine()
        {
            m_Sb.Append('\n');
            m_Sb.Append(' ', m_First.Count * 2);
        }
    }
}
