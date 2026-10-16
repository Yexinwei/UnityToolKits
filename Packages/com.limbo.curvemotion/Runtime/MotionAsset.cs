using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>保存在工程里的曲线运动资产。</summary>
    [CreateAssetMenu(menuName = "Curve Motion/Motion Asset", fileName = "NewMotion")]
    public class MotionAsset : ScriptableObject
    {
        public MotionData data = new MotionData();

        /// <summary>
        /// 编辑器用：上次在哪个场景/预制体里编辑，预览目标、落点对象、3D 相机是谁。打开资产时据此回到那里并填回对象。
        /// 不属于动效本身，不写进 JSON，运行时不读。
        /// </summary>
        [HideInInspector] public MotionEditContext editContext = new MotionEditContext();
    }
}
