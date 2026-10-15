using UnityEngine;

namespace Limbo.CurveMotion.Tests
{
    static class TestData
    {
        /// <summary>沿 x 轴的直线路径，点为 Corner，各段线性缓动。</summary>
        public static MotionData Line(params float[] segmentDurations)
        {
            var data = new MotionData();
            for (int i = 0; i <= segmentDurations.Length; i++)
            {
                var p = data.AddPoint(new Vector3(i * 10f, 0f, 0f), PointType.Corner);
                p.id = "p" + i;
                if (i < segmentDurations.Length) p.segment.duration = segmentDurations[i];
            }
            return data;
        }
    }
}
