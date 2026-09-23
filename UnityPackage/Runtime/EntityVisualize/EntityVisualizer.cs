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

        public static void Register(string name, World world)
        {
            Worlds[name] = world;
            OnRegistered?.Invoke(name, world);
        }

        public static bool Unregister(string name) => Worlds.Remove(name);

        public static int Unregister(World world)
        {
            var removed = 0;
            foreach (var pair in new List<KeyValuePair<string, World>>(Worlds))
            {
                if (!ReferenceEquals(pair.Value, world)) continue;
                if (Worlds.Remove(pair.Key)) removed++;
            }
            return removed;
        }

        public static void Clear()
        {
            Worlds.Clear();
        }
    }
}
#endif
