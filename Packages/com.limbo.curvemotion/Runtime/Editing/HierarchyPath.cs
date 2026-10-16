using System.Collections.Generic;
using System.Text;

namespace Limbo.CurveMotion
{
    /// <summary>
    /// 层级路径：用 / 分隔的对象名，例如 "Canvas/Reward Card/Coin"。
    /// 同一父节点下有同名兄弟时，第 n 个（从 0 数）写成 "Coin#n"（第 0 个不加后缀）；名字里的 % / # 转义成 %25 %2F %23。
    /// </summary>
    public static class HierarchyPath
    {
        public static string EncodeSegment(string name, int duplicateIndex)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "")
            {
                if (c == '%') sb.Append("%25");
                else if (c == '/') sb.Append("%2F");
                else if (c == '#') sb.Append("%23");
                else sb.Append(c);
            }
            if (duplicateIndex > 0) sb.Append('#').Append(duplicateIndex);
            return sb.ToString();
        }

        public static bool TryDecodeSegment(string segment, out string name, out int duplicateIndex)
        {
            name = "";
            duplicateIndex = 0;
            if (segment == null) return false;
            int hash = segment.LastIndexOf('#');
            string encoded = segment;
            if (hash >= 0)
            {
                if (!int.TryParse(segment.Substring(hash + 1), out duplicateIndex) || duplicateIndex < 0) return false;
                encoded = segment.Substring(0, hash);
            }
            var sb = new StringBuilder();
            for (int i = 0; i < encoded.Length; i++)
            {
                char c = encoded[i];
                if (c != '%') { sb.Append(c); continue; }
                if (i + 2 >= encoded.Length) return false;
                string code = encoded.Substring(i + 1, 2).ToUpperInvariant();
                if (code == "25") sb.Append('%');
                else if (code == "2F") sb.Append('/');
                else if (code == "23") sb.Append('#');
                else return false;
                i += 2;
            }
            name = sb.ToString();
            return true;
        }

        public static string Join(IEnumerable<string> segments) => string.Join("/", segments);

        public static string[] Split(string path) => string.IsNullOrEmpty(path) ? new string[0] : path.Split('/');

        /// <summary>路径最后一段的对象名；空路径（预制体根节点）返回空串。</summary>
        public static string LastName(string path)
        {
            var segments = Split(path);
            if (segments.Length == 0) return "";
            return TryDecodeSegment(segments[segments.Length - 1], out var name, out _) ? name : segments[segments.Length - 1];
        }
    }
}
