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

        public static void Clear()
        {
            Worlds.Clear();
            OnRegistered = null;
        }
    }
}
#endif