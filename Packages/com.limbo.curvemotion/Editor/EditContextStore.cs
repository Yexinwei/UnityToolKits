using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if !UNITY_2021_2_OR_NEWER
using UnityEditor.Experimental.SceneManagement;
#endif

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// 把上次编辑用的场景/预制体和预览目标、落点对象、3D 相机记进 Motion Asset（<see cref="MotionAsset.editContext"/>），
    /// 打开资产时找回它们，必要时先打开那个场景或进入那个预制体的预制体模式。
    /// 找对象先用 GlobalObjectId（场景对象，改名挪层级也能找回），再按层级路径（预制体模式里只有路径）。
    /// </summary>
    internal static class EditContextStore
    {
        const string AutoOpenKey = "Limbo.CurveMotion.AutoOpenEditContext";

        /// <summary>打开或拖入资产时，是否自动回到上次编辑的场景/预制体。本机设置，默认开。</summary>
        public static bool AutoOpen
        {
            get => EditorPrefs.GetBool(AutoOpenKey, true);
            set => EditorPrefs.SetBool(AutoOpenKey, value);
        }

        [System.Flags]
        public enum Slots { Mover = 1, End = 2, Camera = 4, All = Mover | End | Camera }

        /// <summary>
        /// 记下 slots 指定的对象；有变化才写进资产并标脏。未保存场景里的对象和工程里的资产没法记，记为空。
        /// 只写用户改了的那一项：对象被删时别的项不会被顺手清掉，撤销删除后还能找回。
        /// </summary>
        public static bool Save(MotionAsset asset, Slots slots, Transform mover, Transform end, Camera camera)
        {
            if (asset == null) return false;
            var ctx = asset.editContext ?? (asset.editContext = new MotionEditContext());
            if (ctx.mover == null) ctx.mover = new MotionObjectRef();
            if (ctx.endTarget == null) ctx.endTarget = new MotionObjectRef();
            if (ctx.camera == null) ctx.camera = new MotionObjectRef();
            bool changed = false;
            if ((slots & Slots.Mover) != 0) changed |= Write(ctx.mover, mover);
            if ((slots & Slots.End) != 0) changed |= Write(ctx.endTarget, end);
            if ((slots & Slots.Camera) != 0) changed |= Write(ctx.camera, camera != null ? camera.transform : null);
            if (changed) EditorUtility.SetDirty(asset);
            return changed;
        }

        static bool Write(MotionObjectRef saved, Transform t)
        {
            var fresh = Capture(t);
            if (fresh.SameAs(saved)) return false;
            saved.CopyFrom(fresh);
            return true;
        }

        public static MotionObjectRef Capture(Transform t)
        {
            var r = new MotionObjectRef();
            if (t == null || EditorUtility.IsPersistent(t)) return r;
            var stage = PrefabStageUtility.GetPrefabStage(t.gameObject);
            if (stage != null)
            {
                var root = stage.prefabContentsRoot != null ? stage.prefabContentsRoot.transform : null;
                if (root == null || !t.IsChildOf(root)) return r;
                r.container = AssetDatabase.AssetPathToGUID(stage.assetPath) ?? "";
                r.prefab = true;
                r.path = PathOf(t, root);
            }
            else
            {
                var scene = t.gameObject.scene;
                if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) return r;
                r.container = AssetDatabase.AssetPathToGUID(scene.path) ?? "";
                r.path = PathOf(t, null);
                var id = GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject);
                r.objectId = id.identifierType == 2 ? id.ToString() : ""; // 2 = 场景对象
            }
            if (string.IsNullOrEmpty(r.container)) r.Clear();
            return r;
        }

        /// <summary>记录所在的场景/预制体资产路径；资产已被删除时返回空串。</summary>
        public static string ContainerPath(MotionObjectRef r) => r == null || r.IsEmpty ? "" : AssetDatabase.GUIDToAssetPath(r.container) ?? "";

        /// <summary>
        /// 记录所在的地方当前是否打开着：预制体要正处在它的预制体模式里；场景要已加载且当前不在预制体模式里
        /// （预制体模式下场景对象不可编辑，算没打开）。
        /// </summary>
        public static bool IsOpen(MotionObjectRef r)
        {
            if (r == null || r.IsEmpty) return false;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (r.prefab) return stage != null && AssetDatabase.AssetPathToGUID(stage.assetPath) == r.container;
            if (stage != null) return false;
            var scene = SceneManager.GetSceneByPath(ContainerPath(r));
            return scene.IsValid() && scene.isLoaded;
        }

        /// <summary>打开记录所在的场景（有未保存修改时先问）或进入那个预制体的预制体模式。返回之后是否已打开。</summary>
        public static bool Open(MotionObjectRef r)
        {
            if (r == null || r.IsEmpty || IsOpen(r)) return IsOpen(r);
            string path = ContainerPath(r);
            if (string.IsNullOrEmpty(path)) return false;
            if (r.prefab)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) return false;
                AssetDatabase.OpenAsset(prefab);
                return IsOpen(r);
            }
            // 先退出预制体模式（预制体有未保存修改时 Unity 会问），再按需打开场景。
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) StageUtility.GoToMainStage();
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) return false;
            var scene = SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }
            return IsOpen(r);
        }

        /// <summary>在当前打开的场景/预制体里找回对象；没打开或找不到返回 null。</summary>
        public static Transform Resolve(MotionObjectRef r)
        {
            if (!IsOpen(r)) return null;
            var segments = HierarchyPath.Split(r.path);
            if (r.prefab)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                var current = stage.prefabContentsRoot != null ? stage.prefabContentsRoot.transform : null;
                for (int i = 0; i < segments.Length && current != null; i++)
                    current = FindChild(current, default, segments[i]);
                return current;
            }

            if (!string.IsNullOrEmpty(r.objectId) && GlobalObjectId.TryParse(r.objectId, out var id))
            {
                var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                var byId = obj is GameObject go ? go.transform : obj as Transform;
                if (byId != null) return byId;
            }
            var scene = SceneManager.GetSceneByPath(ContainerPath(r));
            Transform found = null;
            for (int i = 0; i < segments.Length; i++)
            {
                found = FindChild(found, scene, segments[i]);
                if (found == null) return null;
            }
            return found;
        }

        /// <summary>parent 为 null 时在场景根对象里找。</summary>
        static Transform FindChild(Transform parent, Scene scene, string segment)
        {
            if (!HierarchyPath.TryDecodeSegment(segment, out var name, out int dup)) return null;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (child.name == name && dup-- == 0) return child;
                }
                return null;
            }
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == name && dup-- == 0) return root.transform;
            return null;
        }

        /// <summary>从 root（不含）到 t 的层级路径；root 为 null 时从场景根对象开始。</summary>
        static string PathOf(Transform t, Transform root)
        {
            var segments = new List<string>();
            for (var current = t; current != null && current != root; current = current.parent)
                segments.Add(HierarchyPath.EncodeSegment(current.name, DuplicateIndex(current)));
            segments.Reverse();
            return HierarchyPath.Join(segments);
        }

        /// <summary>t 是同一父节点下第几个同名对象（从 0 数）。</summary>
        static int DuplicateIndex(Transform t)
        {
            int index = 0;
            if (t.parent != null)
            {
                int sibling = t.GetSiblingIndex();
                for (int i = 0; i < sibling; i++)
                    if (t.parent.GetChild(i).name == t.name) index++;
                return index;
            }
            foreach (var root in t.gameObject.scene.GetRootGameObjects())
            {
                if (root.transform == t) break;
                if (root.name == t.name) index++;
            }
            return index;
        }
    }
}
