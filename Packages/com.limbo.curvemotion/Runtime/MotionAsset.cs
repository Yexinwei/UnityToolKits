using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>保存在工程里的曲线运动资产。</summary>
    [CreateAssetMenu(menuName = "Curve Motion/Motion Asset", fileName = "NewMotion")]
    public class MotionAsset : ScriptableObject
    {
        public MotionData data = new MotionData();
    }
}
