using System;
using System.Collections.Generic;
using System.Reflection;

namespace Limbo.CurveMotion
{
    /// <summary>扫描所有带 <see cref="MotionTrackAttribute"/> 的轨道类型，按 typeId 创建实例。</summary>
    public static class MotionTrackRegistry
    {
        static Dictionary<string, Type> s_ById;
        static Dictionary<Type, MotionTrackAttribute> s_ByType;

        public static IEnumerable<string> TypeIds
        {
            get { EnsureLoaded(); return s_ById.Keys; }
        }

        public static IEnumerable<Type> TrackTypes
        {
            get { EnsureLoaded(); return s_ById.Values; }
        }

        public static bool TryCreate(string typeId, out MotionTrack track)
        {
            EnsureLoaded();
            track = null;
            if (typeId == null || !s_ById.TryGetValue(typeId, out var type)) return false;
            track = (MotionTrack)Activator.CreateInstance(type);
            track.name = s_ByType[type].DisplayName;
            return true;
        }

        public static string GetTypeId(Type type)
        {
            EnsureLoaded();
            return s_ByType.TryGetValue(type, out var attr) ? attr.Id : type.Name;
        }

        public static string GetDisplayName(Type type)
        {
            EnsureLoaded();
            return s_ByType.TryGetValue(type, out var attr) ? attr.DisplayName : type.Name;
        }

        static void EnsureLoaded()
        {
            if (s_ById != null) return;
            var byId = new Dictionary<string, Type>();
            var byType = new Dictionary<Type, MotionTrackAttribute>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || !typeof(MotionTrack).IsAssignableFrom(type)) continue;
                    var attr = type.GetCustomAttribute<MotionTrackAttribute>();
                    if (attr == null || byId.ContainsKey(attr.Id)) continue;
                    byId.Add(attr.Id, type);
                    byType.Add(type, attr);
                }
            }
            s_ByType = byType;
            s_ById = byId;
        }
    }
}
