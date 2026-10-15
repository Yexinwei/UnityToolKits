using System.Collections.Generic;
using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>路径几何：切线解析、三次贝塞尔求值、弧长表。</summary>
    public static class PathMath
    {
        /// <summary>每段弧长表的采样数。</summary>
        public const int SamplesPerSegment = 64;

        /// <summary>按点类型算出该点实际使用的入/出切线（相对点位置的偏移）。</summary>
        public static void ResolveTangents(IList<PathPoint> points, int index, out Vector3 inTangent, out Vector3 outTangent)
        {
            var p = points[index];
            switch (p.type)
            {
                case PointType.Corner:
                    inTangent = Vector3.zero;
                    outTangent = Vector3.zero;
                    break;
                case PointType.Smooth:
                    outTangent = p.outTangent;
                    inTangent = -p.outTangent;
                    break;
                case PointType.Free:
                    inTangent = p.inTangent;
                    outTangent = p.outTangent;
                    break;
                default:
                {
                    var prev = index > 0 ? points[index - 1].position : p.position;
                    var next = index < points.Count - 1 ? points[index + 1].position : p.position;
                    var t = (next - prev) * (0.5f / 3f);
                    inTangent = -t;
                    outTangent = t;
                    break;
                }
            }
        }

        /// <summary>第 segment 段（points[segment] → points[segment+1]）的 4 个贝塞尔控制点。</summary>
        public static void GetSegmentControls(IList<PathPoint> points, int segment,
            out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
        {
            ResolveTangents(points, segment, out _, out var outT);
            ResolveTangents(points, segment + 1, out var inT, out _);
            p0 = points[segment].position;
            p3 = points[segment + 1].position;
            p1 = p0 + outT;
            p2 = p3 + inT;
        }

        public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float v = 1f - u;
            return p0 * (v * v * v) + p1 * (3f * v * v * u) + p2 * (3f * v * u * u) + p3 * (u * u * u);
        }

        /// <summary>累积弧长表，长度 SamplesPerSegment + 1，最后一项为段总长。</summary>
        public static float[] BuildLengthTable(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            var table = new float[SamplesPerSegment + 1];
            var prev = p0;
            for (int i = 1; i <= SamplesPerSegment; i++)
            {
                var pt = Bezier(p0, p1, p2, p3, i / (float)SamplesPerSegment);
                table[i] = table[i - 1] + Vector3.Distance(prev, pt);
                prev = pt;
            }
            return table;
        }

        /// <summary>弧长比例 (0~1) → 贝塞尔参数 u。路径按弧长匀速，与 DOTween 路径一致。</summary>
        public static float DistanceFractionToParam(float[] table, float fraction)
        {
            float total = table[table.Length - 1];
            if (total <= 0f) return Mathf.Clamp01(fraction);
            float target = Mathf.Clamp01(fraction) * total;
            int lo = 0, hi = table.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (table[mid] < target) lo = mid; else hi = mid;
            }
            float span = table[hi] - table[lo];
            float local = span > 0f ? (target - table[lo]) / span : 0f;
            return (lo + local) / (table.Length - 1);
        }
    }
}
