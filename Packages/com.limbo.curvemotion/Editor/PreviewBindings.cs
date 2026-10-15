using UnityEditor;
using UnityEngine;

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// 记住每个 Motion Asset 上次用的预览目标、落点对象和 3D 相机（存在本机 EditorPrefs，按资产 GUID）。
    /// 资产里不能引用场景对象，所以重新打开资产时靠它自动填回去；对象所在场景没打开时找不到，忽略即可。
    /// </summary>
    internal static class PreviewBindings
    {
        const string Prefix = "Limbo.CurveMotion.Binding.";

        public static void Save(MotionAsset asset, Transform mover, Transform end, Camera camera)
        {
            string key = Key(asset);
            if (key == null) return;
            EditorPrefs.SetString(key, string.Join("\n", Id(mover), Id(end), Id(camera)));
        }

        public static bool TryLoad(MotionAsset asset, out Transform mover, out Transform end, out Camera camera)
        {
            mover = end = null;
            camera = null;
            string key = Key(asset);
            if (key == null || !EditorPrefs.HasKey(key)) return false;
            var parts = EditorPrefs.GetString(key).Split('\n');
            mover = Resolve(parts, 0) as Transform;
            end = Resolve(parts, 1) as Transform;
            camera = Resolve(parts, 2) as Camera;
            return mover != null || end != null || camera != null;
        }

        static string Key(MotionAsset asset)
        {
            if (asset == null) return null;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return null;
            string guid = AssetDatabase.AssetPathToGUID(path);
            return string.IsNullOrEmpty(guid) ? null : Prefix + guid;
        }

        static string Id(Object obj)
        {
            if (obj == null) return "";
            var id = GlobalObjectId.GetGlobalObjectIdSlow(obj);
            // 未保存场景里的对象没有稳定 id，不记。
            return id.identifierType == 0 || id.assetGUID.Empty() ? "" : id.ToString();
        }

        static Object Resolve(string[] parts, int index)
        {
            if (index >= parts.Length || string.IsNullOrEmpty(parts[index])) return null;
            return GlobalObjectId.TryParse(parts[index], out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null;
        }
    }
}
