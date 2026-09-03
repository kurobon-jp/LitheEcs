using Friflo.Engine.ECS;

namespace LitheEcsBenchmark
{
    public struct Position : IComponent { public float X, Y, Z; }
    public struct Velocity : IComponent { public float X, Y, Z; }
    public struct Acceleration : IComponent { public float X, Y, Z; }
    public struct Health : IComponent { public int Value; }
    public struct Mana : IComponent { public int Value; }

    public struct Player : ITag { }
    public struct Disabled : ITag { }
    public struct Grounded : ITag { }
    public struct Flying : ITag { }
    public struct Excluded90 : ITag { }
    public struct LocalPlayer : LitheEcs.ISingleton { }

    public readonly struct BindingKey : System.IEquatable<BindingKey>
    {
        public readonly int Value;
        public BindingKey(int value) => Value = value;
        public bool Equals(BindingKey other) => Value == other.Value;
        public override bool Equals(object obj) => obj is BindingKey other && Equals(other);
        public override int GetHashCode() => Value;
    }
}
