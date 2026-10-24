using System;

namespace Limbo.CurveMotion
{
    /// <summary>
    /// 上次编辑时用到的场景对象。资产不能直接引用场景对象，所以记的是所在场景/预制体的 GUID 和层级路径。
    /// 只给编辑器用。
    /// </summary>
    [Serializable]
    public class MotionEditContext
    {
        /// <summary>预览目标（动的那个对象）。</summary>
        public MotionObjectRef mover = new MotionObjectRef();
        /// <summary>落点对象。</summary>
        public MotionObjectRef endTarget = new MotionObjectRef();
        /// <summary>UI 与 3D 换算用的相机。</summary>
        public MotionObjectRef camera = new MotionObjectRef();

        public bool IsEmpty => (mover == null || mover.IsEmpty) && (endTarget == null || endTarget.IsEmpty) && (camera == null || camera.IsEmpty);

        /// <summary>“上次编辑的地方”以预览目标所在的场景/预制体为准，没记预览目标时依次用落点对象、相机。</summary>
        public MotionObjectRef Main
        {
            get
            {
                if (mover != null && !mover.IsEmpty) return mover;
                if (endTarget != null && !endTarget.IsEmpty) return endTarget;
                return camera != null && !camera.IsEmpty ? camera : null;
            }
        }
    }

    /// <summary>对场景或预制体里某个对象的记录。</summary>
    [Serializable]
    public class MotionObjectRef
    {
        /// <summary>所在场景或预制体资产的 GUID。空表示没有记录。</summary>
        public string container = "";
        /// <summary>true：在预制体模式里编辑的预制体；false：场景。</summary>
        public bool prefab;
        /// <summary>层级路径，格式见 HierarchyPath。场景里从根对象开始；预制体里相对预制体根节点，根节点本身为空串。</summary>
        public string path = "";
        /// <summary>场景对象的 GlobalObjectId，对象改名、挪层级后仍能找回；预制体模式里的对象没有，只靠路径。</summary>
        public string objectId = "";

        public bool IsEmpty => string.IsNullOrEmpty(container);

        /// <summary>对象名（路径最后一段）。预制体根节点为空串。</summary>
        public string Name => HierarchyPath.LastName(path);

        public bool SameAs(MotionObjectRef other)
        {
            if (other == null) return IsEmpty;
            if (IsEmpty || other.IsEmpty) return IsEmpty == other.IsEmpty;
            return container == other.container && prefab == other.prefab && (path ?? "") == (other.path ?? "") && (objectId ?? "") == (other.objectId ?? "");
        }

        public void CopyFrom(MotionObjectRef other)
        {
            container = other?.container ?? "";
            prefab = other != null && other.prefab;
            path = other?.path ?? "";
            objectId = other?.objectId ?? "";
        }

        public void Clear() => CopyFrom(null);
    }
}
