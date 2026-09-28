#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace LitheEcs.Unity.EntityVisualize
{
    /// <summary>
    /// The entity visualizer class
    /// </summary>
    public static class EntityVisualizer
    {
        public static Dictionary<string, World> Worlds { get; } = new();

        public static event Action<string, World> OnRegistered;
        public static event Action<string, World> OnUnregistered;

        public static void Register(string name, World world)
        {
            if (Worlds.TryGetValue(name, out var previous))
            {
                Worlds.Remove(name);
                OnUnregistered?.Invoke(name, previous);
            }

            Worlds[name] = world;
            OnRegistered?.Invoke(name, world);
        }

        public static bool Unregister(string name)
        {
            if (!Worlds.TryGetValue(name, out var world)) return false;
            Worlds.Remove(name);
            OnUnregistered?.Invoke(name, world);
            return true;
        }

        public static int Unregister(World world)
        {
            var removed = 0;
            foreach (var pair in new List<KeyValuePair<string, World>>(Worlds))
            {
                if (!ReferenceEquals(pair.Value, world)) continue;
                if (!Worlds.Remove(pair.Key)) continue;
                removed++;
                OnUnregistered?.Invoke(pair.Key, pair.Value);
            }
            return removed;
        }

        public static void Clear()
        {
            foreach (var pair in new List<KeyValuePair<string, World>>(Worlds))
            {
                Worlds.Remove(pair.Key);
                OnUnregistered?.Invoke(pair.Key, pair.Value);
            }
        }
    }
}
#endif
