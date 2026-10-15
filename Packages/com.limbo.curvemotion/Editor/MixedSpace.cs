using UnityEngine;

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// UI 与 3D 之间的坐标换算：把落点对象的位置换成“动效对象父节点空间里、相对起点的偏移”，也就是路径终点应在的位置。
    /// 同一类对象直接按世界坐标换算；UI 与 3D 之间经过屏幕坐标：
    /// UI 用它所在 Canvas 的相机（Overlay 为 null），3D 用世界相机；3D 动效对象保持起点离相机的距离。
    /// </summary>
    internal static class MixedSpace
    {
        /// <summary>指定了就用指定的，否则主相机，再否则场景里第一个启用的相机。</summary>
        public static Camera ResolveWorldCamera(Camera preferred)
        {
            if (preferred != null) return preferred;
            if (Camera.main != null) return Camera.main;
            var cameras = Camera.allCameras;
            return cameras.Length > 0 ? cameras[0] : null;
        }

        /// <summary>
        /// 挂在屏幕空间 Canvas（Overlay 或 Screen Space - Camera）下的 RectTransform 视为 UI；
        /// 其余视为 3D，包括 World Space Canvas 里的 UI 和 3D 文本，它们直接按世界坐标换算。
        /// </summary>
        public static bool IsUi(Transform t)
        {
            if (!(t is RectTransform)) return false;
            var canvas = t.GetComponentInParent<Canvas>();
            return canvas != null && canvas.rootCanvas.renderMode != RenderMode.WorldSpace;
        }

        /// <summary>两个对象一个是 UI、一个是 3D，需要经过屏幕坐标换算（此时会用到 3D 相机）。</summary>
        public static bool IsCrossSpace(Transform a, Transform b) => a != null && b != null && IsUi(a) != IsUi(b);

        /// <summary>UI 元素所在根 Canvas 换算屏幕坐标用的相机；Overlay（或 Screen Space - Camera 没指定相机）返回 null。</summary>
        public static Camera CanvasCamera(Transform uiElement)
        {
            var canvas = uiElement != null ? uiElement.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return null;
            canvas = canvas.rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        public static bool TryGetScreenPoint(Transform target, Camera worldCamera, out Vector2 screen)
        {
            screen = default;
            if (target == null) return false;
            if (IsUi(target))
            {
                screen = RectTransformUtility.WorldToScreenPoint(CanvasCamera(target), target.position);
                return true;
            }
            if (worldCamera == null) return false;
            var p = worldCamera.WorldToScreenPoint(target.position);
            if (p.z <= 0f) return false; // 在相机背后
            screen = p;
            return true;
        }

        /// <summary>
        /// 落点对象相对动效对象起点的偏移（动效对象父节点空间）。startLocal 是动效对象的起始 localPosition。
        /// 换算不了时返回 false：没有相机、落点在相机背后、UI 动效对象没有 RectTransform 父节点等。
        /// </summary>
        public static bool TryGetEndOffset(Transform mover, Vector3 startLocal, Transform target, Camera worldCamera, out Vector3 offset)
        {
            offset = Vector3.zero;
            if (mover == null || target == null) return false;
            var parent = mover.parent;
            bool targetUi = IsUi(target);

            if (IsUi(mover))
            {
                var parentRect = parent as RectTransform;
                if (parentRect == null) return false;
                Vector2 local;
                if (targetUi && SameRootCanvas(mover, target))
                {
                    local = parentRect.InverseTransformPoint(target.position);
                }
                else
                {
                    if (!TryGetScreenPoint(target, worldCamera, out var screen)) return false;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screen, CanvasCamera(mover), out local)) return false;
                }
                // UI 的路径用 anchoredPosition，锚点不变时它和 localPosition 的增量相同。
                offset = new Vector3(local.x - startLocal.x, local.y - startLocal.y, 0f);
                return true;
            }

            Vector3 world;
            if (!targetUi)
            {
                world = target.position;
            }
            else
            {
                if (worldCamera == null || !TryGetScreenPoint(target, worldCamera, out var screen)) return false;
                var startWorld = parent != null ? parent.TransformPoint(startLocal) : startLocal;
                float depth = worldCamera.WorldToScreenPoint(startWorld).z;
                if (depth <= 0f) return false;
                world = worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
            }
            var localPosition = parent != null ? parent.InverseTransformPoint(world) : world;
            offset = localPosition - startLocal;
            return true;
        }

        static bool SameRootCanvas(Transform a, Transform b)
        {
            var ca = a.GetComponentInParent<Canvas>();
            var cb = b.GetComponentInParent<Canvas>();
            return ca != null && cb != null && ca.rootCanvas == cb.rootCanvas;
        }
    }
}
