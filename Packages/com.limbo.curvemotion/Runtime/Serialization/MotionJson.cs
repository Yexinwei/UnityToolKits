using System;
using System.Collections.Generic;
using UnityEngine;

namespace Limbo.CurveMotion.Serialization
{
    /// <summary>
    /// MotionData 与 JSON 互转。JSON 是唯一数据源，也是给 AI 解析的格式：
    /// 除了可编辑字段，还写入计算结果（各点到达时间、片段实际起止、段弧长、总时长）和语义说明，
    /// 读取时这些计算字段会被忽略。
    /// </summary>
    public static class MotionJson
    {
        public static string ToJson(MotionData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var eval = new MotionEvaluator(data);
            var w = new JsonWriter();
            w.BeginObject();
            w.Field("schemaVersion", CurveMotionInfo.SchemaVersion);
            w.Field("generator", CurveMotionInfo.PackageName);
            w.Field("name", data.name ?? "");
            w.Field("description", data.description ?? "");
            w.Field("target", data.target.ToString());

            w.Name("conventions").BeginObject();
            w.Field("space", "All positions are offsets from the first path point; apply relative to the object's start position.");
            w.Field("positionUnit", data.target == TargetKind.RectTransform ? "pixels (anchoredPosition)" : "Unity units (localPosition)");
            w.Field("rotationUnit", "degrees (local euler angles)");
            w.Field("timeUnit", "seconds");
            w.Field("pathTiming", "Each segment moves at constant speed along its arc length, shaped by the segment ease.");
            w.Field("clipTiming", "start = arrival time of anchor point + offset. A later clip on the same track overrides an earlier one; the earlier one is cut at the later one's start.");
            w.Field("clipValue", "scale: multiplier of initial localScale; rotation: euler delta added to initial; alpha/color: absolute 0-1. Each clip animates from the current value to its target.");
            w.Field("ease", "Ease names match DOTween's Ease enum with default overshoot 1.70158. Custom = AnimationCurve keys [time, value, inTangent, outTangent].");
            w.EndObject();

            w.Field("duration", eval.Duration);
            w.Name("loop").BeginObject()
                .Field("count", data.loop.count)
                .Field("type", data.loop.type.ToString())
                .EndObject();

            WritePath(w, data, eval);
            WriteTracks(w, data, eval);
            WriteEvents(w, data, eval);

            w.EndObject();
            return w.ToString();
        }

        static void WritePath(JsonWriter w, MotionData data, MotionEvaluator eval)
        {
            var points = data.points;
            w.Name("path").BeginObject();
            w.Name("points").BeginArray();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                PathMath.ResolveTangents(points, i, out var inT, out var outT);
                w.BeginObject()
                    .Field("id", p.id ?? "")
                    .Field("type", p.type.ToString())
                    .Numbers("position", p.position.x, p.position.y, p.position.z)
                    .Numbers("inTangent", inT.x, inT.y, inT.z)
                    .Numbers("outTangent", outT.x, outT.y, outT.z)
                    .Field("time", eval.GetPointTime(i))
                    .EndObject();
            }
            w.EndArray();

            w.Name("segments").BeginArray();
            for (int i = 0; i < eval.SegmentCount; i++)
            {
                var seg = points[i].segment ?? new SegmentSettings();
                w.BeginObject()
                    .Field("from", points[i].id ?? "")
                    .Field("to", points[i + 1].id ?? "")
                    .Field("timing", seg.timing.ToString())
                    .Field("duration", eval.GetSegmentDuration(i))
                    .Field("speed", seg.speed)
                    .Field("length", eval.GetSegmentLength(i));
                WriteEase(w, seg.ease);
                w.Field("desc", seg.desc ?? "").EndObject();
            }
            w.EndArray();
            w.EndObject();
        }

        static void WriteTracks(JsonWriter w, MotionData data, MotionEvaluator eval)
        {
            w.Name("tracks").BeginArray();
            for (int ti = 0; ti < data.tracks.Count; ti++)
            {
                var track = data.tracks[ti];
                if (track == null) continue;
                w.BeginObject()
                    .Field("type", track.TypeId)
                    .Field("name", string.IsNullOrEmpty(track.name) ? track.DisplayName : track.name)
                    .Field("enabled", track.enabled)
                    .Field("mode", track.Mode.ToString());
                w.Name("components").BeginArray(true);
                var names = track.ComponentNames;
                for (int c = 0; c < track.Dimension && c < names.Length; c++) w.Value(names[c]);
                w.EndArray();

                w.Name("clips").BeginArray();
                for (int ci = 0; ci < track.clips.Count; ci++)
                {
                    var clip = track.clips[ci];
                    w.BeginObject()
                        .Field("anchor", clip.anchor ?? "")
                        .Field("offset", clip.offset)
                        .Field("start", eval.GetClipStart(ti, ci))
                        .Field("end", eval.GetClipEnd(ti, ci))
                        .Field("duration", clip.duration);
                    WriteEase(w, clip.ease);
                    w.Name("value").BeginArray(true);
                    for (int c = 0; c < track.Dimension; c++) w.Value(clip.value[c]);
                    w.EndArray();
                    w.Field("desc", clip.desc ?? "").EndObject();
                }
                w.EndArray();
                w.EndObject();
            }
            w.EndArray();
        }

        static void WriteEvents(JsonWriter w, MotionData data, MotionEvaluator eval)
        {
            w.Name("events").BeginArray();
            for (int i = 0; i < data.events.Count; i++)
            {
                var e = data.events[i];
                w.BeginObject()
                    .Field("anchor", e.anchor ?? "")
                    .Field("offset", e.offset)
                    .Field("time", eval.GetEventTime(i))
                    .Field("name", e.name ?? "")
                    .Field("message", e.message ?? "")
                    .EndObject();
            }
            w.EndArray();
        }

        static void WriteEase(JsonWriter w, EaseSpec ease)
        {
            ease ??= new EaseSpec();
            w.Name("ease").BeginObject().Field("type", ease.type.ToString());
            if (ease.type == EaseType.Custom && ease.curve != null)
            {
                w.Name("keys").BeginArray();
                foreach (var k in ease.curve.keys)
                {
                    w.BeginArray(true).Value(k.time).Value(k.value).Value(k.inTangent).Value(k.outTangent).EndArray();
                }
                w.EndArray();
            }
            w.EndObject();
        }

        /// <summary>从 JSON 读取。格式错误抛 FormatException；可恢复的问题（如未知轨道类型）写入 warnings。</summary>
        public static MotionData FromJson(string json, List<string> warnings = null)
        {
            if (!(Json.Parse(json) is Dictionary<string, object> root))
                throw new FormatException("根节点必须是对象");

            int version = GetInt(root, "schemaVersion", 0);
            if (version <= 0) throw new FormatException("缺少 schemaVersion");
            if (version > CurveMotionInfo.SchemaVersion)
                throw new FormatException($"数据版本 {version} 比当前包支持的 {CurveMotionInfo.SchemaVersion} 新，请升级 {CurveMotionInfo.PackageName}");
            root = Migrate(root, version);

            var data = new MotionData
            {
                name = GetString(root, "name", "Motion"),
                description = GetString(root, "description", ""),
                target = GetEnum(root, "target", TargetKind.RectTransform),
            };

            if (GetObject(root, "loop") is Dictionary<string, object> loop)
            {
                data.loop.count = GetInt(loop, "count", 1);
                data.loop.type = GetEnum(loop, "type", MotionLoopType.Restart);
            }

            ReadPath(root, data);
            ReadTracks(root, data, warnings);
            ReadEvents(root, data);
            return data;
        }

        /// <summary>旧版本数据升级到当前结构。目前只有 v1。</summary>
        static Dictionary<string, object> Migrate(Dictionary<string, object> root, int version) => root;

        static void ReadPath(Dictionary<string, object> root, MotionData data)
        {
            if (!(GetObject(root, "path") is Dictionary<string, object> path)) return;
            foreach (var item in GetList(path, "points"))
            {
                if (!(item is Dictionary<string, object> p)) continue;
                data.points.Add(new PathPoint
                {
                    id = GetString(p, "id", null) ?? MotionData.NewId(),
                    type = GetEnum(p, "type", PointType.Auto),
                    position = GetVector(p, "position"),
                    inTangent = GetVector(p, "inTangent"),
                    outTangent = GetVector(p, "outTangent"),
                });
            }

            var segments = GetList(path, "segments");
            for (int i = 0; i < segments.Count && i < data.points.Count - 1; i++)
            {
                if (!(segments[i] is Dictionary<string, object> s)) continue;
                data.points[i].segment = new SegmentSettings
                {
                    timing = GetEnum(s, "timing", TimingMode.Duration),
                    duration = GetFloat(s, "duration", 0.5f),
                    speed = GetFloat(s, "speed", 1f),
                    ease = ReadEase(s, EaseType.Linear),
                    desc = GetString(s, "desc", ""),
                };
            }
        }

        static void ReadTracks(Dictionary<string, object> root, MotionData data, List<string> warnings)
        {
            foreach (var item in GetList(root, "tracks"))
            {
                if (!(item is Dictionary<string, object> t)) continue;
                string type = GetString(t, "type", null);
                if (!MotionTrackRegistry.TryCreate(type, out var track))
                {
                    warnings?.Add($"未知轨道类型 '{type}'，已跳过");
                    continue;
                }
                track.name = GetString(t, "name", track.name);
                track.enabled = GetBool(t, "enabled", true);
                foreach (var c in GetList(t, "clips"))
                {
                    if (!(c is Dictionary<string, object> clip)) continue;
                    track.clips.Add(new MotionClip
                    {
                        anchor = GetString(clip, "anchor", null),
                        offset = GetFloat(clip, "offset", 0f),
                        duration = GetFloat(clip, "duration", 0.3f),
                        ease = ReadEase(clip, EaseType.OutQuad),
                        value = GetVector4(clip, "value", track.DefaultValue),
                        desc = GetString(clip, "desc", ""),
                    });
                }
                data.tracks.Add(track);
            }
        }

        static void ReadEvents(Dictionary<string, object> root, MotionData data)
        {
            foreach (var item in GetList(root, "events"))
            {
                if (!(item is Dictionary<string, object> e)) continue;
                data.events.Add(new MotionEvent
                {
                    anchor = GetString(e, "anchor", null),
                    offset = GetFloat(e, "offset", 0f),
                    name = GetString(e, "name", "Event"),
                    message = GetString(e, "message", ""),
                });
            }
        }

        static EaseSpec ReadEase(Dictionary<string, object> owner, EaseType fallback)
        {
            var ease = new EaseSpec { type = fallback };
            if (!(GetObject(owner, "ease") is Dictionary<string, object> e)) return ease;
            ease.type = GetEnum(e, "type", fallback);
            if (ease.type == EaseType.Custom)
            {
                var keys = new List<Keyframe>();
                foreach (var k in GetList(e, "keys"))
                {
                    if (!(k is List<object> arr) || arr.Count < 2) continue;
                    keys.Add(new Keyframe(ToFloat(arr[0]), ToFloat(arr[1]),
                        arr.Count > 2 ? ToFloat(arr[2]) : 0f, arr.Count > 3 ? ToFloat(arr[3]) : 0f));
                }
                ease.curve = new AnimationCurve(keys.ToArray());
            }
            return ease;
        }

        static object GetObject(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v : null;

        static List<object> GetList(Dictionary<string, object> d, string key) =>
            GetObject(d, key) as List<object> ?? new List<object>();

        static string GetString(Dictionary<string, object> d, string key, string fallback) =>
            GetObject(d, key) is string s ? s : fallback;

        static bool GetBool(Dictionary<string, object> d, string key, bool fallback) =>
            GetObject(d, key) is bool b ? b : fallback;

        static float GetFloat(Dictionary<string, object> d, string key, float fallback) =>
            GetObject(d, key) is double n ? (float)n : fallback;

        static int GetInt(Dictionary<string, object> d, string key, int fallback) =>
            GetObject(d, key) is double n ? (int)Math.Round(n) : fallback;

        static T GetEnum<T>(Dictionary<string, object> d, string key, T fallback) where T : struct =>
            GetObject(d, key) is string s && Enum.TryParse<T>(s, true, out var v) ? v : fallback;

        static float ToFloat(object o) => o is double n ? (float)n : 0f;

        static Vector3 GetVector(Dictionary<string, object> d, string key)
        {
            var v = GetVector4(d, key, Vector4.zero);
            return new Vector3(v.x, v.y, v.z);
        }

        static Vector4 GetVector4(Dictionary<string, object> d, string key, Vector4 fallback)
        {
            if (!(GetObject(d, key) is List<object> arr)) return fallback;
            var v = fallback;
            for (int i = 0; i < arr.Count && i < 4; i++) v[i] = ToFloat(arr[i]);
            return v;
        }
    }
}
