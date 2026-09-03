#if !RELEASE && !DISABLE_LITHEECS_DIAGNOSTICS
#define _INTERNAL_DERIVED_USE_DIAGNOSTICS
#endif
#if !RELEASE && !DISABLE_LITHEECS_VALIDATION
#define _INTERNAL_DERIVED_USE_VALIDATION
#endif

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

[assembly: InternalsVisibleTo("LitheEcs.Unity.Jobs")]

namespace LitheEcs
{
    internal readonly struct ParallelQueryWorkItem
    {
        internal readonly ArchetypeChunk Chunk;
        internal Archetype Archetype => Chunk.Owner;
        internal readonly int Start;
        internal readonly int End;
        internal readonly int QueryOffset;

        internal ParallelQueryWorkItem(ArchetypeChunk chunk, int start, int end, int queryOffset)
        {
            Chunk = chunk;
            Start = start;
            End = end;
            QueryOffset = queryOffset;
        }
    }

    internal abstract class ParallelQueryJob
    {
        private ParallelQueryWorkItem[] _items = Array.Empty<ParallelQueryWorkItem>();
        private int _count;
        private int _next;

        internal void EnsureItemCapacity(int capacity)
        {
            if (_items.Length < capacity) _items = new ParallelQueryWorkItem[capacity];
        }

        internal int Prepare(List<Archetype> matches, int batchSize)
        {
            var itemCount = 0;
            var entityCount = 0;
            for (var i = 0; i < matches.Count; i++)
            {
                var chunks = matches[i].Chunks;
                for (var c = 0; c < chunks.Count; c++)
                {
                    var count = chunks[c].Count;
                    entityCount += count;
                    itemCount += (count + batchSize - 1) / batchSize;
                }
            }

            if (_items.Length < itemCount)
                _items = new ParallelQueryWorkItem[Math.Max(itemCount, _items.Length == 0 ? 4 : _items.Length * 2)];

            var destination = 0;
            var queryOffset = 0;
            for (var i = 0; i < matches.Count; i++)
            {
                var archetype = matches[i];
                for (var c = 0; c < archetype.Chunks.Count; c++)
                {
                    var chunk = archetype.Chunks[c];
                    for (var start = 0; start < chunk.Count; start += batchSize)
                        _items[destination++] = new ParallelQueryWorkItem(
                            chunk, start, Math.Min(start + batchSize, chunk.Count), queryOffset + start);
                    queryOffset += chunk.Count;
                }
            }

            _count = destination;
            _next = -1;
            return entityCount;
        }

        internal void ExecuteWorker()
        {
            while (true)
            {
                var index = Interlocked.Increment(ref _next);
                if (index >= _count) return;
                Execute(in _items[index]);
            }
        }

        protected abstract void Execute(in ParallelQueryWorkItem item);
    }

    internal sealed class ParallelQueryRunner : IDisposable
    {
        private readonly object _gate = new();
        private readonly Thread[] _workers;
        private readonly Exception?[] _exceptions;
        private ParallelQueryJob? _job;
        private int _generation;
        private int _remaining;
        private bool _disposed;

        internal ParallelQueryRunner(int workerCount)
        {
            _workers = new Thread[workerCount];
            _exceptions = new Exception?[workerCount + 1];
            for (var i = 0; i < workerCount; i++)
            {
                var workerIndex = i;
                var thread = new Thread(() => WorkerLoop(workerIndex))
                {
                    IsBackground = true,
                    Name = $"LitheEcs Parallel {i + 1}"
                };
                _workers[i] = thread;
                thread.Start();
            }
        }

        internal void Run(ParallelQueryJob job)
        {
            Array.Clear(_exceptions, 0, _exceptions.Length);
            lock (_gate)
            {
                _job = job;
                _remaining = _workers.Length;
                _generation++;
                Monitor.PulseAll(_gate);
            }

            try
            {
                job.ExecuteWorker();
            }
            catch (Exception exception)
            {
                _exceptions[_workers.Length] = exception;
            }

            lock (_gate)
                while (_remaining != 0)
                    Monitor.Wait(_gate);

            for (var i = 0; i < _exceptions.Length; i++)
                if (_exceptions[i] != null)
                    throw _exceptions[i]!;
        }

        private void WorkerLoop(int workerIndex)
        {
            var observedGeneration = 0;
            while (true)
            {
                ParallelQueryJob job;
                lock (_gate)
                {
                    while (!_disposed && observedGeneration == _generation)
                        Monitor.Wait(_gate);
                    if (_disposed) return;
                    observedGeneration = _generation;
                    job = _job!;
                }

                try
                {
                    job.ExecuteWorker();
                }
                catch (Exception exception)
                {
                    _exceptions[workerIndex] = exception;
                }

                lock (_gate)
                    if (--_remaining == 0)
                        Monitor.PulseAll(_gate);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                Monitor.PulseAll(_gate);
            }
            for (var i = 0; i < _workers.Length; i++) _workers[i].Join();
        }
    }

    #region --- 1. Core Data Structures & BitSet ---

    /// <summary>Marks a component whose owning Entity must be unique within a World.</summary>
    public interface ISingleton
    {
    }

    /// <summary>
    /// World-local unmanaged Entity identifier. It is only meaningful together with the World that produced it.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public readonly int Index;
        public readonly uint Version;

        internal EntityId(int index, uint version)
        {
            Index = index;
            Version = version;
        }

        public bool Equals(EntityId other) => Index == other.Index && Version == other.Version;
        public override bool Equals(object? obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Index, Version);
        public override string ToString() => Version == 0
            ? "EntityId(None)"
            : $"EntityId(Index: {Index}, Version: {Version})";
        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }

    /// <summary>
    /// Entity handle containing a 64-bit generational ID (Index: 32bit, Version: 32bit) and its owning World.
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly int Index;
        public readonly uint Version;
        public readonly World World;

        public EntityId Id
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(Index, Version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Entity(int index, uint version, World world)
        {
            Index = index;
            Version = version;
            World = world;
        }

        public bool IsAlive => World is { } world && world.IsAlive(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Despawn() => RequireWorld().Despawn(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<T>(T component = default) where T : struct => RequireWorld().AddComponent(this, component);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<T1, T2>(T1 component1 = default, T2 component2 = default)
            where T1 : struct where T2 : struct =>
            RequireWorld().AddComponents(this, component1, component2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<T1, T2, T3>(T1 component1 = default, T2 component2 = default, T3 component3 = default)
            where T1 : struct where T2 : struct where T3 : struct =>
            RequireWorld().AddComponents(this, component1, component2, component3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<T1, T2, T3, T4>(T1 component1 = default, T2 component2 = default,
            T3 component3 = default, T4 component4 = default)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
            RequireWorld().AddComponents(this, component1, component2, component3, component4);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<T1, T2, T3, T4, T5>(T1 component1 = default, T2 component2 = default,
            T3 component3 = default, T4 component4 = default, T5 component5 = default)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct =>
            RequireWorld().AddComponents(this, component1, component2, component3, component4, component5);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Bind<T>(T externalObject) => RequireWorld().Bind(externalObject, this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRelation<TRelation>(Entity target) where TRelation : struct =>
            RequireWorld().AddRelation<TRelation>(this, target);

        /// <summary>
        /// Replaces this entity's outgoing relation with a single target.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRelation<TRelation>(Entity target) where TRelation : struct =>
            RequireWorld().SetRelation<TRelation>(this, target);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get<T>() where T : struct => ref RequireWorld().GetComponent<T>(this);

        public EntityData Data
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RequireWorld().GetData(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet<T>(out T component) where T : struct
        {
            if (World is { } world) return world.TryGetComponent(this, out component);
            component = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetRef<T>(out Ref<T> component) where T : struct
        {
            if (World is { } world) return world.TryGetComponentRef(this, out component);
            component = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TGet GetLink<TGet>() where TGet : class => RequireWorld().GetManagedComponent<TGet>(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Has<T>() => RequireWorld().HasComponent<T>(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasRelation<TRelation>(Entity target) where TRelation : struct =>
            RequireWorld().HasRelation<TRelation>(this, target);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity GetRelation<TRelation>() where TRelation : struct =>
            RequireWorld().GetRelation<TRelation>(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetRelation<TRelation>(out Entity target) where TRelation : struct
        {
            if (World is { } world) return world.TryGetRelation<TRelation>(this, out target);
            target = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove<T>() => RequireWorld().RemoveComponent<T>(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove<T1, T2>() where T1 : struct where T2 : struct =>
            RequireWorld().RemoveComponents<T1, T2>(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove<T1, T2, T3>() where T1 : struct where T2 : struct where T3 : struct =>
            RequireWorld().RemoveComponents<T1, T2, T3>(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove<T1, T2, T3, T4>()
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
            RequireWorld().RemoveComponents<T1, T2, T3, T4>(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove<T1, T2, T3, T4, T5>()
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct =>
            RequireWorld().RemoveComponents<T1, T2, T3, T4, T5>(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Unbind<T>(T externalObject) => RequireWorld().Unbind(externalObject, this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<Entity> GetRelations<TRelation>() where TRelation : struct =>
            RequireWorld().GetRelations<TRelation>(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RemoveRelation<TRelation>() where TRelation : struct =>
            RequireWorld().RemoveRelation<TRelation>(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RemoveRelation<TRelation>(Entity target) where TRelation : struct =>
            RequireWorld().RemoveRelation<TRelation>(this, target);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private World RequireWorld() =>
            World ?? throw new InvalidOperationException("Entity is not associated with a World.");

        public bool Equals(Entity other) => Index == other.Index && Version == other.Version && World == other.World;
        public override bool Equals(object? obj) => obj is Entity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Index, Version, World);
        public override string ToString() => World == null
            ? "Entity(None)"
            : $"Entity(Index: {Index}, Version: {Version}, World: {RuntimeHelpers.GetHashCode(World):X8})";
        public static bool operator ==(Entity left, Entity right) => left.Equals(right);
        public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
    }

    /// <summary>
    /// Cached component access for one entity. Structural changes invalidate this view; acquire it again afterwards.
    /// </summary>
    public readonly ref struct EntityData
    {
        private readonly EntityLocation _location;

        internal EntityData(in EntityLocation location) => _location = location;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get<T>() where T : struct => ref _location.Archetype.Get<T>(_location);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Has<T>() where T : struct =>
            _location.IsValid && _location.Archetype.Has(ComponentType<T>.Id);
    }

    public readonly ref struct Ref<T> where T : struct
    {
        private readonly T[]? _values;
        private readonly int _index;
#if _INTERNAL_DERIVED_USE_VALIDATION
        private readonly Entity _entity;
        private readonly int _componentVersion;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if _INTERNAL_DERIVED_USE_VALIDATION
        internal Ref(T[] values, int index, in Entity entity, int componentVersion)
#else
        internal Ref(T[] values, int index)
#endif
        {
            _values = values;
            _index = index;
#if _INTERNAL_DERIVED_USE_VALIDATION
            _entity = entity;
            _componentVersion = componentVersion;
#endif
        }

        public ref T Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if _INTERNAL_DERIVED_USE_VALIDATION
                var world = _entity.World;
                if (world != null)
                    world.ValidateComponentReference<T>(_entity, _componentVersion);
#endif
                return ref _values![_index];
            }
        }
    }

    /// <summary>
    /// Inline 256-bit component filter mask with dynamically sized overflow words.
    /// </summary>
    internal struct ComponentMask
    {
        public ulong B0, B1, B2, B3;
        private ulong[]? _overflow;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int bitIndex)
        {
            var block = bitIndex >> 6;
            var bit = bitIndex & 63;
            switch (block)
            {
                case 0: B0 |= (1UL << bit); break;
                case 1: B1 |= (1UL << bit); break;
                case 2: B2 |= (1UL << bit); break;
                case 3: B3 |= (1UL << bit); break;
                default:
                    var overflowIndex = block - 4;
                    var overflow = _overflow;
                    if (overflow == null)
                    {
                        overflow = new ulong[overflowIndex + 1];
                        _overflow = overflow;
                    }
                    else if (overflowIndex >= overflow.Length)
                    {
                        Array.Resize(ref overflow, overflowIndex + 1);
                        _overflow = overflow;
                    }
                    overflow[overflowIndex] |= 1UL << bit;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unset(int bitIndex)
        {
            var block = bitIndex >> 6;
            var bit = bitIndex & 63;
            switch (block)
            {
                case 0: B0 &= ~(1UL << bit); break;
                case 1: B1 &= ~(1UL << bit); break;
                case 2: B2 &= ~(1UL << bit); break;
                case 3: B3 &= ~(1UL << bit); break;
                default:
                    var overflowIndex = block - 4;
                    if (_overflow == null || overflowIndex >= _overflow.Length) break;
                    _overflow[overflowIndex] &= ~(1UL << bit);
                    break;
            }
        }

        internal void EnsureBitCapacity(int bitIndex)
        {
            var overflowIndex = (bitIndex >> 6) - 4;
            if (overflowIndex < 0) return;
            if (_overflow == null) _overflow = new ulong[overflowIndex + 1];
            else if (_overflow.Length <= overflowIndex) Array.Resize(ref _overflow, overflowIndex + 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Has(int bitIndex)
        {
            var block = bitIndex >> 6;
            var bit = bitIndex & 63;
            return block switch
            {
                0 => (B0 & (1UL << bit)) != 0,
                1 => (B1 & (1UL << bit)) != 0,
                2 => (B2 & (1UL << bit)) != 0,
                3 => (B3 & (1UL << bit)) != 0,
                _ => _overflow != null && block - 4 < _overflow.Length &&
                     (_overflow[block - 4] & (1UL << bit)) != 0,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool ContainsAll(in ComponentMask other)
        {
            if ((B0 & other.B0) != other.B0 ||
                   (B1 & other.B1) != other.B1 ||
                   (B2 & other.B2) != other.B2 ||
                   (B3 & other.B3) != other.B3) return false;
            var otherOverflow = other._overflow;
            if (otherOverflow == null) return true;
            for (var i = 0; i < otherOverflow.Length; i++)
                if ((GetOverflowWord(i) & otherOverflow[i]) != otherOverflow[i]) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool ContainsNone(in ComponentMask other)
        {
            if ((B0 & other.B0) != 0 ||
                   (B1 & other.B1) != 0 ||
                   (B2 & other.B2) != 0 ||
                   (B3 & other.B3) != 0) return false;
            var otherOverflow = other._overflow;
            if (otherOverflow == null || _overflow == null) return true;
            var length = Math.Min(_overflow.Length, otherOverflow.Length);
            for (var i = 0; i < length; i++)
                if ((_overflow[i] & otherOverflow[i]) != 0) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Intersects(in ComponentMask other)
        {
            if (((B0 & other.B0) | (B1 & other.B1) |
                 (B2 & other.B2) | (B3 & other.B3)) != 0) return true;
            if (_overflow == null || other._overflow == null) return false;
            var length = Math.Min(_overflow.Length, other._overflow.Length);
            for (var i = 0; i < length; i++)
                if ((_overflow[i] & other._overflow[i]) != 0) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool SameAs(in ComponentMask other)
        {
            if (B0 != other.B0 || B1 != other.B1 || B2 != other.B2 || B3 != other.B3) return false;
            var length = Math.Max(_overflow?.Length ?? 0, other._overflow?.Length ?? 0);
            for (var i = 0; i < length; i++)
                if (GetOverflowWord(i) != other.GetOverflowWord(i)) return false;
            return true;
        }

        public readonly ComponentMask Union(in ComponentMask other)
        {
            var result = new ComponentMask
                { B0 = B0 | other.B0, B1 = B1 | other.B1, B2 = B2 | other.B2, B3 = B3 | other.B3 };
            var length = Math.Max(_overflow?.Length ?? 0, other._overflow?.Length ?? 0);
            if (length == 0) return result;
            result._overflow = new ulong[length];
            for (var i = 0; i < length; i++) result._overflow[i] = GetOverflowWord(i) | other.GetOverflowWord(i);
            return result;
        }

        public readonly ComponentMask Intersection(in ComponentMask other)
        {
            var result = new ComponentMask
                { B0 = B0 & other.B0, B1 = B1 & other.B1, B2 = B2 & other.B2, B3 = B3 & other.B3 };
            var length = Math.Min(_overflow?.Length ?? 0, other._overflow?.Length ?? 0);
            if (length == 0) return result;
            result._overflow = new ulong[length];
            for (var i = 0; i < length; i++) result._overflow[i] = _overflow![i] & other._overflow![i];
            return result;
        }

        public readonly bool IsEmpty
        {
            get
            {
                if ((B0 | B1 | B2 | B3) != 0) return false;
                if (_overflow == null) return true;
                for (var i = 0; i < _overflow.Length; i++) if (_overflow[i] != 0) return false;
                return true;
            }
        }

        internal readonly int OverflowWordCount => _overflow?.Length ?? 0;
        internal readonly ulong GetOverflowWord(int index) =>
            _overflow != null && (uint)index < (uint)_overflow.Length ? _overflow[index] : 0;

        internal readonly int[] ToTypeIds()
        {
            var count = 0;
            CountWord(B0, ref count);
            CountWord(B1, ref count);
            CountWord(B2, ref count);
            CountWord(B3, ref count);
            for (var i = 0; i < OverflowWordCount; i++) CountWord(GetOverflowWord(i), ref count);
            if (count == 0) return Array.Empty<int>();

            var result = new int[count];
            var index = 0;
            CopyWord(B0, 0, result, ref index);
            CopyWord(B1, 64, result, ref index);
            CopyWord(B2, 128, result, ref index);
            CopyWord(B3, 192, result, ref index);
            for (var i = 0; i < OverflowWordCount; i++)
                CopyWord(GetOverflowWord(i), 256 + i * 64, result, ref index);
            return result;
        }

        private static void CountWord(ulong word, ref int count)
        {
            while (word != 0)
            {
                count++;
                word &= word - 1;
            }
        }

        private static void CopyWord(ulong word, int offset, int[] target, ref int index)
        {
            var bit = 0;
            while (word != 0)
            {
                if ((word & 1UL) != 0) target[index++] = offset + bit;
                bit++;
                word >>= 1;
            }
        }
    }

    /// <summary>
    /// Manages process-wide component type IDs without a fixed upper bound.
    /// </summary>
    public static class ComponentType<T>
    {
        // ReSharper disable once StaticMemberInGenericType
        public static readonly int Id;

        static ComponentType()
        {
            var targetType = typeof(T);
            Id = ComponentTypeRegistry.Register(targetType,
                RuntimeHelpers.IsReferenceOrContainsReferences<T>(),
                capacity => new ArchetypeColumn<T>(capacity));
        }
    }

    internal static class SingletonType<T>
    {
        internal static readonly bool IsSingleton = typeof(ISingleton).IsAssignableFrom(typeof(T));
    }

    internal static class ComponentTypeRegistry
    {
        private static int _counter;
        private static readonly Dictionary<Type, int> Types = new();
        private static readonly List<Func<int, IArchetypeColumn>> ColumnFactories = new();
        private static readonly List<Type> TypesById = new();
        private static readonly List<bool> RequiresClearById = new();

        internal static int Register(Type type, bool requiresClear,
            Func<int, IArchetypeColumn> columnFactory)
        {
            lock (Types)
            {
                if (Types.TryGetValue(type, out var id)) return id;
                id = _counter++;
                Types[type] = id;
                ColumnFactories.Add(columnFactory);
                TypesById.Add(type);
                RequiresClearById.Add(requiresClear);
                return id;
            }
        }

        internal static Func<int, IArchetypeColumn> GetColumnFactory(int typeId)
        {
            lock (Types) return ColumnFactories[typeId];
        }

        internal static Type GetType(int typeId)
        {
            lock (Types) return TypesById[typeId];
        }

        internal static int Count
        {
            get { lock (Types) return TypesById.Count; }
        }

        internal static bool RequiresClear(int typeId)
        {
            lock (Types) return RequiresClearById[typeId];
        }
    }

    #endregion

    #region --- 2. Managed Link & Helper ---

    public readonly struct Link<T> where T : class
    {
        public readonly T Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Link(T value) => Value = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator T(Link<T> link) => link.Value;

        public override string ToString() => $"Link<{typeof(T).Name}>({Value?.ToString() ?? "null"})";
    }

    public static class Link
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Link<T> With<T>(T obj) where T : class => new(obj);
    }

    #endregion

    #region --- 3. Component Storage & GC-Free Sparse Relation ---

    internal interface IIncrementalQueryPlan
    {
        void OnFilterComponentChanged(int entityIndex);
    }

    /// <summary>
    /// 1:N sparse relation storage using pooled nodes. Allocations occur when capacity grows.
    /// </summary>
    internal sealed class RelationStorage
    {
        private struct Node
        {
            public Entity Target;
            public int Next;
        }

        private struct BackNode
        {
            public Entity Source;
            public int Next;
        }

        private int[] _headForward = new int[64];
        private int[] _tailForward = new int[64];
        private int[] _headBackward = new int[64];
        private int[] _tailBackward = new int[64];

        private Node[] _nodes = new Node[64];
        private int _nodeCount;
        private int _freeNodeHead = -1;

        private BackNode[] _backNodes = new BackNode[64];
        private int _backNodeCount;
        private int _freeBackNodeHead = -1;
        private int _count;
        private Entity[] _tempForwardBuffer = new Entity[16];
        private Entity[] _tempBackwardBuffer = new Entity[16];

        internal RelationStorage(int entityCapacity = 64, int relationCapacity = 64)
        {
            entityCapacity = Math.Max(1, entityCapacity);
            relationCapacity = Math.Max(1, relationCapacity);
            if (_headForward.Length != entityCapacity)
            {
                _headForward = new int[entityCapacity];
                _tailForward = new int[entityCapacity];
                _headBackward = new int[entityCapacity];
                _tailBackward = new int[entityCapacity];
            }
            if (_nodes.Length != relationCapacity)
            {
                _nodes = new Node[relationCapacity];
                _backNodes = new BackNode[relationCapacity];
            }
            Array.Fill(_headForward, -1);
            Array.Fill(_tailForward, -1);
            Array.Fill(_headBackward, -1);
            Array.Fill(_tailBackward, -1);
        }

        internal void Add(Entity source, Entity target)
        {
            EnsureEntityCapacity(Math.Max(source.Index, target.Index));

            // Append to the forward list in FIFO order.
            if (!ContainsForward(source.Index, target))
            {
                var nodeIdx = AllocateNode();
                _nodes[nodeIdx] = new Node { Target = target, Next = -1 };
                if (_tailForward[source.Index] == -1)
                {
                    _headForward[source.Index] = nodeIdx;
                }
                else
                {
                    _nodes[_tailForward[source.Index]].Next = nodeIdx;
                }

                _tailForward[source.Index] = nodeIdx;
                _count++;
            }

            // Append to the backward list in FIFO order.
            if (!ContainsBackward(target.Index, source))
            {
                var backIdx = AllocateBackNode();
                _backNodes[backIdx] = new BackNode { Source = source, Next = -1 };
                if (_tailBackward[target.Index] == -1)
                {
                    _headBackward[target.Index] = backIdx;
                }
                else
                {
                    _backNodes[_tailBackward[target.Index]].Next = backIdx;
                }

                _tailBackward[target.Index] = backIdx;
            }
        }

        internal void EnsureCapacity(int entityCapacity, int relationCapacity)
        {
            if (entityCapacity > 0) EnsureEntityCapacity(entityCapacity - 1);
            if (_nodes.Length < relationCapacity) Array.Resize(ref _nodes, relationCapacity);
            if (_backNodes.Length < relationCapacity) Array.Resize(ref _backNodes, relationCapacity);
        }

        internal void EnsureSearchCapacity(int forwardCapacity, int backwardCapacity)
        {
            if (_tempForwardBuffer.Length < forwardCapacity)
                Array.Resize(ref _tempForwardBuffer, forwardCapacity);
            if (_tempBackwardBuffer.Length < backwardCapacity)
                Array.Resize(ref _tempBackwardBuffer, backwardCapacity);
        }

        internal ReadOnlySpan<Entity> GetForward(Entity source)
        {
            if (source.Index >= _headForward.Length) return ReadOnlySpan<Entity>.Empty;
            var curr = _headForward[source.Index];
            var count = 0;
            var temp = curr;
            while (temp != -1)
            {
                count++;
                temp = _nodes[temp].Next;
            }

            if (count == 0) return ReadOnlySpan<Entity>.Empty;

            if (_tempForwardBuffer.Length < count) Array.Resize(ref _tempForwardBuffer, count * 2);
            temp = curr;
            for (var i = 0; i < count; i++)
            {
                _tempForwardBuffer[i] = _nodes[temp].Target;
                temp = _nodes[temp].Next;
            }

            return new ReadOnlySpan<Entity>(_tempForwardBuffer, 0, count);
        }

        internal ReadOnlySpan<Entity> GetBackward(Entity target)
        {
            if (target.Index >= _headBackward.Length) return ReadOnlySpan<Entity>.Empty;
            var curr = _headBackward[target.Index];
            var count = 0;
            var temp = curr;
            while (temp != -1)
            {
                count++;
                temp = _backNodes[temp].Next;
            }

            if (count == 0) return ReadOnlySpan<Entity>.Empty;

            if (_tempBackwardBuffer.Length < count) Array.Resize(ref _tempBackwardBuffer, count * 2);
            temp = curr;
            for (var i = 0; i < count; i++)
            {
                _tempBackwardBuffer[i] = _backNodes[temp].Source;
                temp = _backNodes[temp].Next;
            }

            return new ReadOnlySpan<Entity>(_tempBackwardBuffer, 0, count);
        }

        internal bool Remove(Entity source, Entity target)
        {
            var removed = RemoveForward(source.Index, target);
            RemoveBackward(target.Index, source);
            return removed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool Contains(Entity source, Entity target)
        {
            return source.Index < _headForward.Length && ContainsForward(source.Index, target);
        }

        internal void RemoveAll(Entity source)
        {
            if (source.Index >= _headForward.Length) return;
            var curr = _headForward[source.Index];
            while (curr != -1)
            {
                var next = _nodes[curr].Next;
                RemoveBackward(_nodes[curr].Target.Index, source);
                FreeNode(curr);
                _count--;
                curr = next;
            }

            _headForward[source.Index] = -1;
            _tailForward[source.Index] = -1;
        }

        internal void RemoveAllTarget(Entity target)
        {
            if (target.Index >= _headBackward.Length) return;
            var curr = _headBackward[target.Index];
            while (curr != -1)
            {
                var next = _backNodes[curr].Next;
                RemoveForward(_backNodes[curr].Source.Index, target);
                FreeBackNode(curr);
                curr = next;
            }

            _headBackward[target.Index] = -1;
            _tailBackward[target.Index] = -1;
        }

        internal bool HasForward(int entityIndex) =>
            entityIndex < _headForward.Length && _headForward[entityIndex] != -1;

        internal bool HasBackward(int entityIndex) =>
            entityIndex < _headBackward.Length && _headBackward[entityIndex] != -1;

        private bool ContainsForward(int srcIndex, Entity target)
        {
            var curr = _headForward[srcIndex];
            while (curr != -1)
            {
                if (_nodes[curr].Target == target) return true;
                curr = _nodes[curr].Next;
            }

            return false;
        }

        private bool ContainsBackward(int tgtIndex, Entity source)
        {
            var curr = _headBackward[tgtIndex];
            while (curr != -1)
            {
                if (_backNodes[curr].Source == source) return true;
                curr = _backNodes[curr].Next;
            }

            return false;
        }

        private bool RemoveForward(int srcIndex, Entity target)
        {
            if (srcIndex >= _headForward.Length) return false;
            var curr = _headForward[srcIndex];
            var prev = -1;
            while (curr != -1)
            {
                if (_nodes[curr].Target == target)
                {
                    if (prev == -1) _headForward[srcIndex] = _nodes[curr].Next;
                    else _nodes[prev].Next = _nodes[curr].Next;
                    if (_tailForward[srcIndex] == curr) _tailForward[srcIndex] = prev;
                    FreeNode(curr);
                    _count--;
                    return true;
                }

                prev = curr;
                curr = _nodes[curr].Next;
            }

            return false;
        }

        private void RemoveBackward(int tgtIndex, Entity source)
        {
            if (tgtIndex >= _headBackward.Length) return;
            var curr = _headBackward[tgtIndex];
            var prev = -1;
            while (curr != -1)
            {
                if (_backNodes[curr].Source == source)
                {
                    if (prev == -1) _headBackward[tgtIndex] = _backNodes[curr].Next;
                    else _backNodes[prev].Next = _backNodes[curr].Next;
                    if (_tailBackward[tgtIndex] == curr) _tailBackward[tgtIndex] = prev;
                    FreeBackNode(curr);
                    return;
                }

                prev = curr;
                curr = _backNodes[curr].Next;
            }
        }

        private void EnsureEntityCapacity(int maxEntityIndex)
        {
            if (maxEntityIndex >= _headForward.Length)
            {
                var newSize = Math.Max(maxEntityIndex + 1, _headForward.Length * 2);
                var oldSize = _headForward.Length;
                Array.Resize(ref _headForward, newSize);
                Array.Resize(ref _tailForward, newSize);
                Array.Resize(ref _headBackward, newSize);
                Array.Resize(ref _tailBackward, newSize);
                for (var i = oldSize; i < newSize; i++)
                {
                    _headForward[i] = -1;
                    _tailForward[i] = -1;
                    _headBackward[i] = -1;
                    _tailBackward[i] = -1;
                }
            }
        }

        private int AllocateNode()
        {
            if (_freeNodeHead != -1)
            {
                var idx = _freeNodeHead;
                _freeNodeHead = _nodes[idx].Next;
                return idx;
            }

            if (_nodeCount >= _nodes.Length) Array.Resize(ref _nodes, _nodes.Length * 2);
            return _nodeCount++;
        }

        private void FreeNode(int index)
        {
            _nodes[index].Next = _freeNodeHead;
            _freeNodeHead = index;
        }

        private int AllocateBackNode()
        {
            if (_freeBackNodeHead != -1)
            {
                var idx = _freeBackNodeHead;
                _freeBackNodeHead = _backNodes[idx].Next;
                return idx;
            }

            if (_backNodeCount >= _backNodes.Length) Array.Resize(ref _backNodes, _backNodes.Length * 2);
            return _backNodeCount++;
        }

        private void FreeBackNode(int index)
        {
            _backNodes[index].Next = _freeBackNodeHead;
            _freeBackNodeHead = index;
        }

    }

    #endregion

    #region --- 4. Query Delegates ---

    public readonly ref struct EntityRange
    {
        private readonly World _world;
        private readonly ReadOnlySpan<int> _entityIds;
        private readonly int _offset;

        internal EntityRange(World world, int[] entityIds, int start, int length, int offset)
        {
            _world = world;
            _entityIds = new ReadOnlySpan<int>(entityIds, start, length);
            _offset = offset;
        }

        public int Length => _entityIds.Length;
        /// <summary>The first index of this range in the complete Query result.</summary>
        public int Offset => _offset;
        public Entity this[int index] => _world.GetEntity(_entityIds[index]);
    }

    public delegate void ParallelRangeAction<T1>(Span<T1> c1, EntityRange entities) where T1 : struct;

    public delegate void ParallelRangeAction<T1, T2>(Span<T1> c1, Span<T2> c2, EntityRange entities)
        where T1 : struct where T2 : struct;

    public delegate void ParallelRangeAction<T1, T2, T3>(Span<T1> c1, Span<T2> c2, Span<T3> c3,
        EntityRange entities) where T1 : struct where T2 : struct where T3 : struct;

    public delegate void ParallelRangeAction<T1, T2, T3, T4>(Span<T1> c1, Span<T2> c2, Span<T3> c3, Span<T4> c4,
        EntityRange entities) where T1 : struct where T2 : struct where T3 : struct where T4 : struct;

    public delegate void QueryAction<T1>(in Entity entity, ref T1 c1) where T1 : struct;

    public delegate void QueryAction<T1, T2>(in Entity entity, ref T1 c1, ref T2 c2)
        where T1 : struct where T2 : struct;

    public delegate void QueryAction<T1, T2, T3>(in Entity entity, ref T1 c1, ref T2 c2, ref T3 c3)
        where T1 : struct where T2 : struct where T3 : struct;

    public delegate void QueryAction<T1, T2, T3, T4>(in Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4)
        where T1 : struct where T2 : struct where T3 : struct where T4 : struct;

    public interface IQueryAction<T1> where T1 : struct
    {
        void Execute(in Entity entity, ref T1 c1);
    }

    public interface IQueryAction<T1, T2> where T1 : struct where T2 : struct
    {
        void Execute(in Entity entity, ref T1 c1, ref T2 c2);
    }

    public interface IComponentAction<T1> where T1 : struct
    {
        void Execute(ref T1 c1);
    }

    public interface IComponentAction<T1, T2> where T1 : struct where T2 : struct
    {
        void Execute(ref T1 c1, ref T2 c2);
    }

    public interface IQueryAction<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct
    {
        void Execute(in Entity entity, ref T1 c1, ref T2 c2, ref T3 c3);
    }

    public interface IQueryAction<T1, T2, T3, T4>
        where T1 : struct where T2 : struct where T3 : struct where T4 : struct
    {
        void Execute(in Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4);
    }

    /// <summary>A Query configured for World-owned managed parallel range execution.</summary>
    public readonly struct ParallelQuery<T1> where T1 : struct
    {
        private readonly Query<T1> _source;
        public int MinimumEntityCount { get; }
        public int BatchSize { get; }

        internal ParallelQuery(Query<T1> source, int minimumEntityCount, int batchSize)
        {
            if (minimumEntityCount < 1) throw new ArgumentOutOfRangeException(nameof(minimumEntityCount));
            if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
            _source = source;
            MinimumEntityCount = minimumEntityCount;
            BatchSize = batchSize;
        }

        public void Reserve(int maximumEntityCount) =>
            _source.ReserveParallelRangesCore(maximumEntityCount, BatchSize);

        public void Run(ParallelRangeAction<T1> action) =>
            _source.ParallelForRanges(action, MinimumEntityCount, BatchSize);
    }

    public static partial class ParallelQueryExtensions
    {
        public static ParallelQuery<T1> AsParallelQuery<T1>(this Query<T1> query,
            int minimumEntityCount = 4096, int batchSize = 4096) where T1 : struct =>
            new(query, minimumEntityCount, batchSize);
    }

    /// <summary>
    /// A query whose projected component types are safe to cross a native-job boundary.
    /// Filters are intentionally not part of this type: they are evaluated while creating
    /// the native execution view, so a managed component (for example Link&lt;T&gt;) may still
    /// be used by With/Without/Any.
    /// </summary>
    public readonly struct JobQuery<T1> where T1 : unmanaged
    {
        internal readonly Query<T1> Source;
        internal JobQuery(Query<T1> source) => Source = source;
    }

    public readonly struct JobQuery<T1, T2> where T1 : unmanaged where T2 : unmanaged
    {
        internal readonly Query<T1, T2> Source;
        internal JobQuery(Query<T1, T2> source) => Source = source;
    }

    public readonly struct JobQuery<T1, T2, T3>
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    {
        internal readonly Query<T1, T2, T3> Source;
        internal JobQuery(Query<T1, T2, T3> source) => Source = source;
    }

    public readonly struct JobQueryRange<T1> where T1 : struct
    {
        public readonly Memory<T1> Components1;
        internal JobQueryRange(Memory<T1> components1) => Components1 = components1;
        public int Length => Components1.Length;
    }

    public readonly struct JobQueryRange<T1, T2> where T1 : struct where T2 : struct
    {
        public readonly Memory<T1> Components1;
        public readonly Memory<T2> Components2;
        internal JobQueryRange(Memory<T1> components1, Memory<T2> components2)
        {
            Components1 = components1;
            Components2 = components2;
        }
        public int Length => Components1.Length;
    }

    public readonly struct JobQueryRange<T1, T2, T3>
        where T1 : struct where T2 : struct where T3 : struct
    {
        public readonly Memory<T1> Components1;
        public readonly Memory<T2> Components2;
        public readonly Memory<T3> Components3;
        internal JobQueryRange(Memory<T1> components1, Memory<T2> components2, Memory<T3> components3)
        {
            Components1 = components1;
            Components2 = components2;
            Components3 = components3;
        }
        public int Length => Components1.Length;
    }

    public ref struct JobQueryRangeLease<T1> where T1 : struct
    {
        private World? _world;
        private readonly List<Archetype> _ranges;

        internal JobQueryRangeLease(World world, List<Archetype> ranges)
        {
            _world = world;
            _ranges = ranges;
        }

        public int RangeCount { get { var count = 0; for (var i = 0; i < _ranges.Count; i++) for (var c = 0; c < _ranges[i].Chunks.Count; c++) if (_ranges[i].Chunks[c].Count != 0) count++; return count; } }

        public JobQueryRange<T1> GetRange(int index)
        {
            if (_world == null) throw new ObjectDisposedException(nameof(JobQueryRangeLease<T1>));
            for (var i = 0; i < _ranges.Count; i++) for (var c = 0; c < _ranges[i].Chunks.Count; c++) { var chunk = _ranges[i].Chunks[c]; if (chunk.Count == 0) continue; if (index-- == 0) return new JobQueryRange<T1>(_ranges[i].GetColumn<T1>(chunk).AsMemory(0, chunk.Count)); }
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        public void Dispose()
        {
            if (_world == null) return;
            _world.ExitParallelQuery();
            _world = null;
        }
    }

    public ref struct JobQueryRangeLease<T1, T2> where T1 : struct where T2 : struct
    {
        private World? _world;
        private readonly List<Archetype> _ranges;

        internal JobQueryRangeLease(World world, List<Archetype> ranges)
        {
            _world = world;
            _ranges = ranges;
        }

        public int RangeCount { get { var count = 0; for (var i = 0; i < _ranges.Count; i++) for (var c = 0; c < _ranges[i].Chunks.Count; c++) if (_ranges[i].Chunks[c].Count != 0) count++; return count; } }

        public JobQueryRange<T1, T2> GetRange(int index)
        {
            if (_world == null) throw new ObjectDisposedException(nameof(JobQueryRangeLease<T1, T2>));
            for (var i = 0; i < _ranges.Count; i++) for (var c = 0; c < _ranges[i].Chunks.Count; c++) { var archetype = _ranges[i]; var chunk = archetype.Chunks[c]; if (chunk.Count == 0) continue; if (index-- == 0) return new JobQueryRange<T1, T2>(archetype.GetColumn<T1>(chunk).AsMemory(0, chunk.Count), archetype.GetColumn<T2>(chunk).AsMemory(0, chunk.Count)); }
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        public void Dispose()
        {
            if (_world == null) return;
            _world.ExitParallelQuery();
            _world = null;
        }
    }

    public ref struct JobQueryRangeLease<T1, T2, T3>
        where T1 : struct where T2 : struct where T3 : struct
    {
        private World? _world;
        private readonly List<Archetype> _ranges;

        internal JobQueryRangeLease(World world, List<Archetype> ranges)
        {
            _world = world;
            _ranges = ranges;
        }

        public int RangeCount { get { var count = 0; for (var i = 0; i < _ranges.Count; i++) for (var c = 0; c < _ranges[i].Chunks.Count; c++) if (_ranges[i].Chunks[c].Count != 0) count++; return count; } }

        public JobQueryRange<T1, T2, T3> GetRange(int index)
        {
            if (_world == null) throw new ObjectDisposedException(nameof(JobQueryRangeLease<T1, T2, T3>));
            for (var i = 0; i < _ranges.Count; i++) for (var c = 0; c < _ranges[i].Chunks.Count; c++) { var archetype = _ranges[i]; var chunk = archetype.Chunks[c]; if (chunk.Count == 0) continue; if (index-- == 0) return new JobQueryRange<T1, T2, T3>(archetype.GetColumn<T1>(chunk).AsMemory(0, chunk.Count), archetype.GetColumn<T2>(chunk).AsMemory(0, chunk.Count), archetype.GetColumn<T3>(chunk).AsMemory(0, chunk.Count)); }
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        public void Dispose()
        {
            if (_world == null) return;
            _world.ExitParallelQuery();
            _world = null;
        }
    }

    /// <summary>Compile-time checked entry points used by platform-specific job adapters.</summary>
    public static class JobQueryExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static JobQuery<T1> AsJobQuery<T1>(this Query<T1> query) where T1 : unmanaged => new(query);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static JobQuery<T1, T2> AsJobQuery<T1, T2>(this Query<T1, T2> query)
            where T1 : unmanaged where T2 : unmanaged => new(query);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static JobQuery<T1, T2, T3> AsJobQuery<T1, T2, T3>(this Query<T1, T2, T3> query)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged => new(query);

        public static JobQueryRangeLease<T1> AcquireRanges<T1>(this JobQuery<T1> query) where T1 : unmanaged =>
            query.Source.AcquireJobRanges();

        public static JobQueryRangeLease<T1, T2> AcquireRanges<T1, T2>(this JobQuery<T1, T2> query)
            where T1 : unmanaged where T2 : unmanaged => query.Source.AcquireJobRanges();

        public static JobQueryRangeLease<T1, T2, T3> AcquireRanges<T1, T2, T3>(this JobQuery<T1, T2, T3> query)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged => query.Source.AcquireJobRanges();
    }

    #endregion

    #region --- 5. Reactive Entity Collector ---

    [Flags]
    public enum ComponentEvent : byte
    {
        KeyAdded = 1,
        KeyRemoved = 2,
        KeyChanged = 4,
    }

    public sealed class EntityCollector : IDisposable
    {
        private World? _world;
        private readonly List<Entity> _entities = new();
        private readonly HashSet<Entity> _entitySet = new();

        internal EntityCollector(World world) => _world = world;

        public int Count
        {
            get
            {
                ThrowIfDisposed();
                return _entities.Count;
            }
        }

        public Entity this[int index]
        {
            get
            {
                ThrowIfDisposed();
                return _entities[index];
            }
        }

        public EntityCollector Or<T>(ComponentEvent events) where T : struct
        {
            var world = _world ?? throw new ObjectDisposedException(nameof(EntityCollector));
            world.RegisterCollector<T>(this, events);
            return this;
        }

        /// <summary>Reserves storage for entities collected before the next Clear.</summary>
        public EntityCollector EnsureCapacity(int capacity)
        {
            ThrowIfDisposed();
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (_entities.Capacity < capacity) _entities.Capacity = capacity;
            _entitySet.EnsureCapacity(capacity);
            return this;
        }

        public List<Entity>.Enumerator GetEnumerator()
        {
            ThrowIfDisposed();
            return _entities.GetEnumerator();
        }

        public void Clear()
        {
            ThrowIfDisposed();
            _entities.Clear();
            _entitySet.Clear();
        }

        internal void Collect(in Entity entity)
        {
            if (_entitySet.Add(entity)) _entities.Add(entity);
        }

        internal void Invalidate()
        {
            _world = null;
            _entities.Clear();
            _entitySet.Clear();
        }

        public void Dispose()
        {
            var world = _world;
            if (world == null) return;
            world.UnregisterCollector(this);
            Invalidate();
        }

        private void ThrowIfDisposed()
        {
            if (_world == null) throw new ObjectDisposedException(nameof(EntityCollector));
        }
    }

    internal sealed class CollectorRegistry
    {
        private struct Subscription
        {
            public EntityCollector Collector;
            public ComponentEvent Events;
        }

        private List<Subscription>?[] _subscriptions = new List<Subscription>[256];
        private readonly HashSet<EntityCollector> _collectors = new();
        internal ComponentMask ObservedTypes;

        internal int CollectorCount => _collectors.Count;

        internal void Register(int typeId, EntityCollector collector, ComponentEvent events)
        {
            if (typeId >= _subscriptions.Length)
                Array.Resize(ref _subscriptions, Math.Max(typeId + 1, _subscriptions.Length * 2));
            var subscriptions = _subscriptions[typeId] ??= new List<Subscription>();
            for (var i = 0; i < subscriptions.Count; i++)
            {
                if (!ReferenceEquals(subscriptions[i].Collector, collector)) continue;
                var subscription = subscriptions[i];
                subscription.Events |= events;
                subscriptions[i] = subscription;
                return;
            }

            subscriptions.Add(new Subscription { Collector = collector, Events = events });
            _collectors.Add(collector);
            ObservedTypes.Set(typeId);
        }

        internal void Unregister(EntityCollector collector)
        {
            for (var typeId = 0; typeId < _subscriptions.Length; typeId++)
            {
                var subscriptions = _subscriptions[typeId];
                if (subscriptions == null) continue;
                for (var i = subscriptions.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(subscriptions[i].Collector, collector)) subscriptions.RemoveAt(i);
                if (subscriptions.Count != 0) continue;
                _subscriptions[typeId] = null;
                ObservedTypes.Unset(typeId);
            }

            _collectors.Remove(collector);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Publish(int typeId, in Entity entity, ComponentEvent componentEvent)
        {
            if ((uint)typeId >= (uint)_subscriptions.Length) return;
            var subscriptions = _subscriptions[typeId];
            if (subscriptions == null) return;
            for (var i = 0; i < subscriptions.Count; i++)
            {
                var subscription = subscriptions[i];
                if ((subscription.Events & componentEvent) != 0)
                    subscription.Collector.Collect(entity);
            }
        }

        internal void InvalidateAll()
        {
            foreach (var collector in _collectors) collector.Invalidate();
            _collectors.Clear();
        }
    }

    #endregion

    #region --- 6. EntityCommandBuffer (ECB) ---

    public readonly struct DeferredEntity
    {
        internal readonly EntityCommandBuffer Owner;
        internal readonly int Id;
        internal readonly int Generation;

        internal DeferredEntity(EntityCommandBuffer owner, int id, int generation)
        {
            Owner = owner;
            Id = id;
            Generation = generation;
        }
    }

    /// <summary>
    /// Deferred command buffer for managed ECS usage. Storing commands as ICommand causes boxing,
    /// so this implementation is intended for main-thread scenarios where GC allocations are acceptable.
    /// </summary>
    public sealed class EntityCommandBuffer
    {
        private readonly World _world;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        private readonly AllocationDiagnostics _allocationDiagnostics;
#endif
        private readonly int _ownerThreadId;

        internal EntityCommandBuffer(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            _allocationDiagnostics = world.AllocationDiagnostics;
#endif
            _ownerThreadId = Environment.CurrentManagedThreadId;
        }

        private enum CommandKind : byte
        {
            KeySpawn,
            KeyDespawn,
            KeyAdd,
            KeyAddBatch,
            KeyRemove,
            KeyAddRelation,
            KeyRemoveRelation,
            KeyRemoveAllRelations,
        }

        private interface IComponentCommandBuffer
        {
            int TypeId { get; }
            void Add(World world, in Entity entity, int payloadIndex);
            void AddBatch(World world, List<Entity> entities, int start, int count, int payloadIndex);
            void Remove(World world, in Entity entity);
            void AddRelation(World world, in Entity source, in Entity target);
            void RemoveRelation(World world, in Entity source);
            void RemoveRelation(World world, in Entity source, in Entity target);
            void SetAfterMove(World world, in Entity entity, int payloadIndex, bool isNew);
            void CompleteRemove(World world, in Entity entity, bool removed);
            void Clear();
        }

        private sealed class ComponentCommandBuffer<T> : IComponentCommandBuffer where T : struct
        {
            private readonly List<T> _components = new();
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            private readonly AllocationDiagnostics _allocationDiagnostics;
            internal ComponentCommandBuffer(AllocationDiagnostics allocationDiagnostics) =>
                _allocationDiagnostics = allocationDiagnostics;
#endif
            public int TypeId => ComponentType<T>.Id;

            public void EnsureCapacity(int capacity)
            {
                if (_components.Capacity < capacity) _components.Capacity = capacity;
            }

            public int Record(in T component)
            {
                var index = _components.Count;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
                if (_allocationDiagnostics.Enabled && _components.Count == _components.Capacity)
                {
                    _allocationDiagnostics.CommandPayloadGrowths++;
                    _allocationDiagnostics.LastCommandPayloadTypeId = TypeId;
                }
#endif
                _components.Add(component);
                return index;
            }

            public void Add(World world, in Entity entity, int payloadIndex) =>
                world.AddComponent(entity, _components[payloadIndex]);

            public void AddBatch(World world, List<Entity> entities, int start, int count, int payloadIndex)
            {
                var component = _components[payloadIndex];
                var end = start + count;
                for (var i = start; i < end; i++)
                    world.AddComponent(entities[i], component);
            }

            public void Remove(World world, in Entity entity) => world.RemoveComponent<T>(entity);

            public void AddRelation(World world, in Entity source, in Entity target) =>
                world.AddRelation<T>(source, target);

            public void RemoveRelation(World world, in Entity source) =>
                world.RemoveRelation<T>(source);

            public void RemoveRelation(World world, in Entity source, in Entity target) =>
                world.RemoveRelation<T>(source, target);

            public void SetAfterMove(World world, in Entity entity, int payloadIndex, bool isNew) =>
                world.SetAddedComponent(entity, _components[payloadIndex], isNew);

            public void CompleteRemove(World world, in Entity entity, bool removed) =>
                world.CompleteRemovedComponent<T>(entity, removed);

            public void Clear() => _components.Clear();
        }

        private struct Command
        {
            public CommandKind Kind;
            public Entity Target;
            public IComponentCommandBuffer? ComponentBuffer;
            public int PayloadIndex;
            public int BatchStart;
            public int BatchCount;
            public int DeferredEntityId;
            public bool TargetIsDeferred;
            public Entity RelationTarget;
        }

        private readonly List<Command> _commands = new();
        private readonly List<Entity> _batchEntities = new();
        private IComponentCommandBuffer?[] _componentBuffers = new IComponentCommandBuffer[256];
        private bool[] _activeComponentBufferFlags = new bool[256];
        private readonly List<int> _activeComponentBufferTypeIds = new();
        private Entity[] _resolvedEntities = Array.Empty<Entity>();
        private int _nextDeferredEntityId;
        private int _generation;

        internal bool HasPendingCommands => _commands.Count != 0;

        internal void EnsureComponentCapacity<T>(int capacity) where T : struct
        {
            ValidateThread();
            GetOrCreateComponentBuffer<T>().EnsureCapacity(capacity);
        }

        internal void EnsureCommandCapacity(int capacity)
        {
            ValidateThread();
            if (_commands.Capacity < capacity) _commands.Capacity = capacity;
        }

        internal void EnsureDeferredEntityCapacity(int capacity)
        {
            ValidateThread();
            EnsureResolvedCapacity(capacity);
        }

        /// <summary>Preallocates command, deferred-entity, and batch-entity bookkeeping.</summary>
        public void Reserve(int commandCapacity, int deferredEntityCapacity = 0, int batchEntityCapacity = 0)
        {
            ValidateThread();
            if (commandCapacity < 0) throw new ArgumentOutOfRangeException(nameof(commandCapacity));
            if (deferredEntityCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(deferredEntityCapacity));
            if (batchEntityCapacity < 0) throw new ArgumentOutOfRangeException(nameof(batchEntityCapacity));
            EnsureCommandCapacity(commandCapacity);
            EnsureResolvedCapacity(deferredEntityCapacity);
            if (_batchEntities.Capacity < batchEntityCapacity) _batchEntities.Capacity = batchEntityCapacity;
        }

        /// <summary>Preallocates payload storage for one component type recorded in this buffer.</summary>
        public void ReservePayload<T>(int capacity) where T : struct
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            EnsureComponentCapacity<T>(capacity);
        }

        public DeferredEntity Spawn()
        {
            ValidateThread();
            return RecordSpawn();
        }

        public DeferredEntity Spawn<T1, T2>(T1 component1, T2 component2)
            where T1 : struct where T2 : struct
        {
            ValidateThread();
            var entity = RecordSpawn();
            RecordDeferredComponent(entity.Id, component1);
            RecordDeferredComponent(entity.Id, component2);
            return entity;
        }

        public DeferredEntity Spawn<T1, T2, T3>(T1 component1, T2 component2, T3 component3)
            where T1 : struct where T2 : struct where T3 : struct
        {
            ValidateThread();
            var entity = RecordSpawn();
            RecordDeferredComponent(entity.Id, component1);
            RecordDeferredComponent(entity.Id, component2);
            RecordDeferredComponent(entity.Id, component3);
            return entity;
        }

        public DeferredEntity Spawn<T1, T2, T3, T4>(T1 component1, T2 component2, T3 component3, T4 component4)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            ValidateThread();
            var entity = RecordSpawn();
            RecordDeferredComponent(entity.Id, component1);
            RecordDeferredComponent(entity.Id, component2);
            RecordDeferredComponent(entity.Id, component3);
            RecordDeferredComponent(entity.Id, component4);
            return entity;
        }

        public void Despawn(Entity entity)
        {
            ValidateThread();
            ValidateWorld(entity);
            RecordCommand(new Command { Kind = CommandKind.KeyDespawn, Target = entity });
        }

        public void AddComponent<T>(Entity entity, T component = default) where T : struct
        {
            ValidateThread();
            ValidateWorld(entity);
            RecordEntityComponent(entity, component);
        }

        public void AddComponent<T1, T2>(Entity entity, T1 component1, T2 component2)
            where T1 : struct where T2 : struct
        {
            ValidateThread();
            ValidateWorld(entity);
            RecordEntityComponent(entity, component1);
            RecordEntityComponent(entity, component2);
        }

        public void AddComponent<T1, T2, T3>(Entity entity, T1 component1, T2 component2, T3 component3)
            where T1 : struct where T2 : struct where T3 : struct
        {
            ValidateThread();
            ValidateWorld(entity);
            RecordEntityComponent(entity, component1);
            RecordEntityComponent(entity, component2);
            RecordEntityComponent(entity, component3);
        }

        public void AddComponent<T1, T2, T3, T4>(Entity entity, T1 component1, T2 component2, T3 component3, T4 component4)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            ValidateThread();
            ValidateWorld(entity);
            RecordEntityComponent(entity, component1);
            RecordEntityComponent(entity, component2);
            RecordEntityComponent(entity, component3);
            RecordEntityComponent(entity, component4);
        }

        public void AddComponent<T>(DeferredEntity entity, T component = default) where T : struct
        {
            ValidateThread();
            ValidateDeferred(entity);
            RecordDeferredComponent(entity.Id, component);
        }

        public void AddComponent<T1, T2>(DeferredEntity entity, T1 component1, T2 component2)
            where T1 : struct where T2 : struct
        {
            ValidateThread();
            ValidateDeferred(entity);
            RecordDeferredComponent(entity.Id, component1);
            RecordDeferredComponent(entity.Id, component2);
        }

        public void AddComponent<T1, T2, T3>(DeferredEntity entity, T1 component1, T2 component2, T3 component3)
            where T1 : struct where T2 : struct where T3 : struct
        {
            ValidateThread();
            ValidateDeferred(entity);
            RecordDeferredComponent(entity.Id, component1);
            RecordDeferredComponent(entity.Id, component2);
            RecordDeferredComponent(entity.Id, component3);
        }

        public void AddComponent<T1, T2, T3, T4>(DeferredEntity entity, T1 component1, T2 component2, T3 component3, T4 component4)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            ValidateThread();
            ValidateDeferred(entity);
            RecordDeferredComponent(entity.Id, component1);
            RecordDeferredComponent(entity.Id, component2);
            RecordDeferredComponent(entity.Id, component3);
            RecordDeferredComponent(entity.Id, component4);
        }

        public void AddComponentBatch<T>(ReadOnlySpan<Entity> entities, T component) where T : struct
        {
            ValidateThread();
            if (entities.Length == 0) return;

            for (var i = 0; i < entities.Length; i++)
                ValidateWorld(entities[i]);

            var buffer = GetOrCreateComponentBuffer<T>();
            var payloadIndex = buffer.Record(component);
            var batchStart = _batchEntities.Count;
            for (var i = 0; i < entities.Length; i++)
            {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
                if (_allocationDiagnostics.Enabled && _batchEntities.Count == _batchEntities.Capacity)
                    _allocationDiagnostics.BatchEntityBufferGrowths++;
#endif
                _batchEntities.Add(entities[i]);
            }

            RecordCommand(new Command
            {
                Kind = CommandKind.KeyAddBatch,
                ComponentBuffer = buffer,
                PayloadIndex = payloadIndex,
                BatchStart = batchStart,
                BatchCount = entities.Length,
            });
        }

        public void RemoveComponent<T>(Entity entity) where T : struct
        {
            ValidateThread();
            ValidateWorld(entity);
            var buffer = GetOrCreateComponentBuffer<T>();
            RecordCommand(new Command
            {
                Kind = CommandKind.KeyRemove,
                Target = entity,
                ComponentBuffer = buffer,
            });
        }

        public void AddRelation<TRelation>(Entity source, Entity target) where TRelation : struct
        {
            ValidateThread();
            ValidateWorld(source);
            ValidateWorld(target);
            var buffer = GetOrCreateComponentBuffer<TRelation>();
            RecordCommand(new Command
            {
                Kind = CommandKind.KeyAddRelation,
                Target = source,
                ComponentBuffer = buffer,
                RelationTarget = target,
            });
        }

        public void AddRelation<TRelation>(DeferredEntity source, Entity target) where TRelation : struct
        {
            ValidateThread();
            ValidateDeferred(source);
            ValidateWorld(target);
            var buffer = GetOrCreateComponentBuffer<TRelation>();
            RecordCommand(new Command
            {
                Kind = CommandKind.KeyAddRelation,
                DeferredEntityId = source.Id,
                TargetIsDeferred = true,
                ComponentBuffer = buffer,
                RelationTarget = target,
            });
        }

        public void RemoveRelation<TRelation>(Entity source, Entity target) where TRelation : struct
        {
            ValidateThread();
            ValidateWorld(source);
            ValidateWorld(target);
            var buffer = GetOrCreateComponentBuffer<TRelation>();
            RecordCommand(new Command
            {
                Kind = CommandKind.KeyRemoveRelation,
                Target = source,
                ComponentBuffer = buffer,
                RelationTarget = target,
            });
        }

        public void RemoveRelation<TRelation>(Entity source) where TRelation : struct
        {
            ValidateThread();
            ValidateWorld(source);
            var buffer = GetOrCreateComponentBuffer<TRelation>();
            RecordCommand(new Command
            {
                Kind = CommandKind.KeyRemoveAllRelations,
                Target = source,
                ComponentBuffer = buffer,
            });
        }

        public void Playback()
        {
            ValidateThread();
            try
            {
                for (var i = 0; i < _commands.Count; i++)
                {
                    var command = _commands[i];
                    switch (command.Kind)
                    {
                        case CommandKind.KeySpawn:
                            _resolvedEntities[command.DeferredEntityId] = _world.Spawn();
                            break;
                        case CommandKind.KeyDespawn:
                            _world.Despawn(command.Target);
                            break;
                        case CommandKind.KeyAdd:
                            var addTarget = command.TargetIsDeferred
                                ? _resolvedEntities[command.DeferredEntityId]
                                : command.Target;
                            var addEnd = i + 1;
                            while (addEnd < _commands.Count && IsSameAddTarget(command, _commands[addEnd])) addEnd++;
                            ApplyAddRange(addTarget, i, addEnd);
                            i = addEnd - 1;
                            break;
                        case CommandKind.KeyAddBatch:
                            var batchEnd = i + 1;
                            while (batchEnd < _commands.Count && IsSameBatch(command, _commands[batchEnd])) batchEnd++;
                            ApplyAddBatchRange(i, batchEnd, command.BatchStart, command.BatchCount);
                            i = batchEnd - 1;
                            break;
                        case CommandKind.KeyRemove:
                            var removeEnd = i + 1;
                            while (removeEnd < _commands.Count && _commands[removeEnd].Kind == CommandKind.KeyRemove
                                   && _commands[removeEnd].Target == command.Target) removeEnd++;
                            ApplyRemoveRange(command.Target, i, removeEnd);
                            i = removeEnd - 1;
                            break;
                        case CommandKind.KeyAddRelation:
                            var relationSource = command.TargetIsDeferred
                                ? _resolvedEntities[command.DeferredEntityId]
                                : command.Target;
                            command.ComponentBuffer!.AddRelation(_world, relationSource, command.RelationTarget);
                            break;
                        case CommandKind.KeyRemoveRelation:
                            command.ComponentBuffer!.RemoveRelation(_world, command.Target, command.RelationTarget);
                            break;
                        case CommandKind.KeyRemoveAllRelations:
                            command.ComponentBuffer!.RemoveRelation(_world, command.Target);
                            break;
                    }
                }
            }
            finally
            {
                _commands.Clear();
                _batchEntities.Clear();
                for (var i = 0; i < _activeComponentBufferTypeIds.Count; i++)
                {
                    var typeId = _activeComponentBufferTypeIds[i];
                    _componentBuffers[typeId]!.Clear();
                    _activeComponentBufferFlags[typeId] = false;
                }

                _activeComponentBufferTypeIds.Clear();
                _nextDeferredEntityId = 0;
                _generation++;
            }
        }

        private static bool IsSameAddTarget(in Command first, in Command next) =>
            next.Kind == CommandKind.KeyAdd
            && next.TargetIsDeferred == first.TargetIsDeferred
            && (first.TargetIsDeferred
                ? next.DeferredEntityId == first.DeferredEntityId
                : next.Target == first.Target);

        private bool IsSameBatch(in Command first, in Command next)
        {
            if (next.Kind != CommandKind.KeyAddBatch || next.BatchCount != first.BatchCount) return false;
            for (var i = 0; i < first.BatchCount; i++)
                if (_batchEntities[first.BatchStart + i] != _batchEntities[next.BatchStart + i]) return false;
            return true;
        }

        private void ApplyAddRange(in Entity target, int start, int end)
        {
            Span<int> typeIds = end - start <= 64 ? stackalloc int[end - start] : new int[end - start];
            for (var i = start; i < end; i++) typeIds[i - start] = _commands[i].ComponentBuffer!.TypeId;
            var source = _world.MoveForAddedComponents(target, typeIds);
            for (var i = start; i < end; i++)
            {
                var item = _commands[i];
                var firstOfType = IsFirstTypeInRange(start, i, item.ComponentBuffer!.TypeId);
                item.ComponentBuffer!.SetAfterMove(
                    _world, target, item.PayloadIndex, firstOfType && !source.Has(item.ComponentBuffer.TypeId));
            }
        }

        private void ApplyAddBatchRange(int start, int end, int batchStart, int batchCount)
        {
            Span<int> typeIds = end - start <= 64 ? stackalloc int[end - start] : new int[end - start];
            for (var i = start; i < end; i++) typeIds[i - start] = _commands[i].ComponentBuffer!.TypeId;
            var batchEnd = batchStart + batchCount;
            for (var entityIndex = batchStart; entityIndex < batchEnd; entityIndex++)
            {
                var target = _batchEntities[entityIndex];
                var source = _world.MoveForAddedComponents(target, typeIds);
                for (var i = start; i < end; i++)
                {
                    var item = _commands[i];
                    var firstOfType = IsFirstTypeInRange(start, i, item.ComponentBuffer!.TypeId);
                    item.ComponentBuffer!.SetAfterMove(
                        _world, target, item.PayloadIndex, firstOfType && !source.Has(item.ComponentBuffer.TypeId));
                }
            }
        }

        private void ApplyRemoveRange(in Entity target, int start, int end)
        {
            Span<int> typeIds = end - start <= 64 ? stackalloc int[end - start] : new int[end - start];
            for (var i = start; i < end; i++) typeIds[i - start] = _commands[i].ComponentBuffer!.TypeId;
            var source = _world.MoveForRemovedComponents(target, typeIds, out _);
            for (var i = start; i < end; i++)
            {
                var buffer = _commands[i].ComponentBuffer!;
                buffer.CompleteRemove(
                    _world, target, IsFirstTypeInRange(start, i, buffer.TypeId) && source.Has(buffer.TypeId));
            }
        }

        private bool IsFirstTypeInRange(int start, int current, int typeId)
        {
            for (var i = start; i < current; i++)
                if (_commands[i].ComponentBuffer!.TypeId == typeId) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
                throw new InvalidOperationException(
                    "EntityCommandBuffer can only be used from the thread that created it. " +
                    "Thread-safe command recording is not currently supported.");
        }

        private DeferredEntity RecordSpawn()
        {
            var id = _nextDeferredEntityId++;
            EnsureResolvedCapacity(_nextDeferredEntityId);
            _resolvedEntities[id] = default;
            RecordCommand(new Command { Kind = CommandKind.KeySpawn, DeferredEntityId = id });
            return new DeferredEntity(this, id, _generation);
        }

        private void RecordEntityComponent<T>(in Entity entity, in T component) where T : struct
        {
            var buffer = GetOrCreateComponentBuffer<T>();
            var payloadIndex = buffer.Record(component);
            RecordCommand(new Command
            {
                Kind = CommandKind.KeyAdd,
                Target = entity,
                ComponentBuffer = buffer,
                PayloadIndex = payloadIndex,
            });
        }

        private void RecordDeferredComponent<T>(int deferredEntityId, in T component) where T : struct
        {
            var buffer = GetOrCreateComponentBuffer<T>();
            var payloadIndex = buffer.Record(component);
            RecordCommand(new Command
            {
                Kind = CommandKind.KeyAdd,
                DeferredEntityId = deferredEntityId,
                TargetIsDeferred = true,
                ComponentBuffer = buffer,
                PayloadIndex = payloadIndex,
            });
        }

        private void EnsureResolvedCapacity(int required)
        {
            if (_resolvedEntities.Length >= required) return;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled) _allocationDiagnostics.DeferredEntityBufferGrowths++;
#endif
            var capacity = _resolvedEntities.Length == 0 ? 4 : _resolvedEntities.Length * 2;
            if (capacity < required) capacity = required;
            Array.Resize(ref _resolvedEntities, capacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordCommand(in Command command)
        {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled && _commands.Count == _commands.Capacity)
                _allocationDiagnostics.CommandBufferGrowths++;
#endif
            _commands.Add(command);
        }

        private void ValidateDeferred(in DeferredEntity entity)
        {
            if (!ReferenceEquals(entity.Owner, this))
                throw new InvalidOperationException(
                    "DeferredEntity belongs to a different EntityCommandBuffer.");
            if (entity.Generation != _generation || (uint)entity.Id >= (uint)_nextDeferredEntityId)
                throw new InvalidOperationException(
                    "DeferredEntity is no longer valid. Record all commands before Playback.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateWorld(in Entity entity)
        {
            if (!ReferenceEquals(entity.World, _world))
                throw new InvalidOperationException("Entity belongs to a different World.");
        }

        private ComponentCommandBuffer<T> GetOrCreateComponentBuffer<T>() where T : struct
        {
            var typeId = ComponentType<T>.Id;
            if (typeId >= _componentBuffers.Length)
            {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
                if (_allocationDiagnostics.Enabled) _allocationDiagnostics.ComponentBufferRegistryGrowths++;
#endif
                var capacity = Math.Max(typeId + 1, _componentBuffers.Length * 2);
                Array.Resize(ref _componentBuffers, capacity);
                Array.Resize(ref _activeComponentBufferFlags, capacity);
            }
            var untypedBuffer = _componentBuffers[typeId];
            if (untypedBuffer == null)
            {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
                if (_allocationDiagnostics.Enabled) _allocationDiagnostics.ComponentBufferCreations++;
                var newBuffer = new ComponentCommandBuffer<T>(_allocationDiagnostics);
#else
                var newBuffer = new ComponentCommandBuffer<T>();
#endif
                _componentBuffers[typeId] = newBuffer;
                _activeComponentBufferFlags[typeId] = true;
                _activeComponentBufferTypeIds.Add(typeId);
                return newBuffer;
            }

            var buffer = (ComponentCommandBuffer<T>)untypedBuffer;
            if (!_activeComponentBufferFlags[typeId])
            {
                _activeComponentBufferFlags[typeId] = true;
                _activeComponentBufferTypeIds.Add(typeId);
            }

            return buffer;
        }
    }

    #endregion

    #region --- 6. EntityTemplate (Prefab / True Batch Creation) ---

    /// <summary>Collects the component types of an Archetype whose storage will be reserved.</summary>
    public sealed class ArchetypeBuilder
    {
        private readonly World _world;
        private ComponentMask _componentMask;

        internal ArchetypeBuilder(World world)
        {
            _world = world;
        }

        public ArchetypeBuilder Add<T>() where T : struct
        {
            _world.EnsureComponentTypeRegistered<T>();
            _componentMask.Set(ComponentType<T>.Id);
            return this;
        }

        internal int[] GetTypeIds() => _componentMask.ToTypeIds();
    }

    /// <summary>Collects Archetype layouts that share one total reserved capacity.</summary>
    public sealed class ArchetypeGroupBuilder
    {
        private readonly World _world;
        private readonly List<int[]> _layouts = new List<int[]>();
        private ComponentMask _componentMask;
        private int[]? _commonTypeIds;

        internal ArchetypeGroupBuilder(World world)
        {
            _world = world;
        }

        /// <summary>Defines components shared by every subsequently added layout.</summary>
        public ArchetypeGroupBuilder Common(Action<ArchetypeBuilder> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            if (_commonTypeIds != null) throw new InvalidOperationException("Common can only be configured once.");
            if (_layouts.Count != 0) throw new InvalidOperationException("Common must be configured before layouts are added.");
            var builder = new ArchetypeBuilder(_world);
            configure(builder);
            _commonTypeIds = builder.GetTypeIds();
            if (_commonTypeIds.Length == 0)
                throw new InvalidOperationException("At least one common component type is required.");
            return this;
        }

        /// <summary>Adds the Common layout itself.</summary>
        public ArchetypeGroupBuilder Add()
        {
            if (_commonTypeIds == null)
                throw new InvalidOperationException("A parameterless layout requires Common to be configured.");
            AddCompletedLayout((int[])_commonTypeIds.Clone());
            return this;
        }

        /// <summary>Adds one completed layout, or a difference to Common when Common is configured.</summary>
        public ArchetypeGroupBuilder Add(Action<ArchetypeBuilder> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            var builder = new ArchetypeBuilder(_world);
            configure(builder);
            var configured = builder.GetTypeIds();
            if (_commonTypeIds == null && configured.Length == 0)
                throw new InvalidOperationException("At least one component type is required per Archetype.");
            var layout = _commonTypeIds == null ? configured : MergeTypeIds(_commonTypeIds, configured);
            AddCompletedLayout(layout);
            return this;
        }

        private void AddCompletedLayout(int[] layout)
        {
            for (var i = 0; i < _layouts.Count; i++)
                if (LayoutsEqual(_layouts[i], layout))
                    throw new InvalidOperationException("The same Archetype layout was added more than once.");
            _layouts.Add(layout);
            for (var i = 0; i < layout.Length; i++) _componentMask.Set(layout[i]);
        }

        private static int[] MergeTypeIds(int[] left, int[] right)
        {
            var result = new int[left.Length + right.Length];
            var leftIndex = 0;
            var rightIndex = 0;
            var resultIndex = 0;
            while (leftIndex < left.Length || rightIndex < right.Length)
            {
                if (rightIndex >= right.Length || leftIndex < left.Length && left[leftIndex] < right[rightIndex])
                    result[resultIndex++] = left[leftIndex++];
                else if (leftIndex >= left.Length || right[rightIndex] < left[leftIndex])
                    result[resultIndex++] = right[rightIndex++];
                else
                {
                    result[resultIndex++] = left[leftIndex++];
                    rightIndex++;
                }
            }
            if (resultIndex == result.Length) return result;
            Array.Resize(ref result, resultIndex);
            return result;
        }

        private static bool LayoutsEqual(int[] left, int[] right)
        {
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        internal List<int[]> Layouts => _layouts;
        internal int[] GetSharedTypeIds() => _componentMask.ToTypeIds();
    }


    public sealed class EntityTemplate
    {
        private interface ITemplateComponent
        {
            int TypeId { get; }
            void ApplyBatch(ReadOnlySpan<int> entityIndices, Archetype archetype);
            void ApplySingle(int entityIndex, Archetype archetype);
        }

        private sealed class TemplateComponent<T> : ITemplateComponent where T : struct
        {
            private readonly T _defaultValue;
            private readonly World _world;
            private Archetype? _archetype;
            private int _columnIndex;

            public TemplateComponent(World world, in T defaultValue)
            {
                _world = world;
                _defaultValue = defaultValue;
                world.EnsureComponentTypeRegistered<T>();
            }

            public int TypeId => ComponentType<T>.Id;

            public void ApplyBatch(ReadOnlySpan<int> entityIndices, Archetype archetype)
            {
                EnsureColumn(archetype);
                for (var i = 0; i < entityIndices.Length; i++)
                    _world.SetTemplateArchetypeComponent(entityIndices[i], archetype, _columnIndex, _defaultValue);
            }

            public void ApplySingle(int entityIndex, Archetype archetype)
            {
                EnsureColumn(archetype);
                _world.SetTemplateArchetypeComponent(entityIndex, archetype, _columnIndex, _defaultValue);
            }

            private void EnsureColumn(Archetype archetype)
            {
                if (ReferenceEquals(_archetype, archetype)) return;
                _archetype = archetype;
                _columnIndex = archetype.GetColumnIndex(TypeId);
            }
        }

        private readonly World _world;
        private readonly List<ITemplateComponent> _components = new();
        private ComponentMask _componentMask;
        private ComponentMask _singletonMask;
        private Archetype? _archetype;

        internal EntityTemplate(World world) => _world = world;

        public EntityTemplate Add<T>(T defaultComponent) where T : struct
        {
            var component = new TemplateComponent<T>(_world, defaultComponent);
            for (var i = 0; i < _components.Count; i++)
            {
                if (_components[i].TypeId != component.TypeId) continue;
                _components[i] = component;
                return this;
            }

            _components.Add(component);
            _componentMask.Set(component.TypeId);
            if (SingletonType<T>.IsSingleton) _singletonMask.Set(component.TypeId);
            _archetype = null;
            return this;
        }

        public Entity Spawn()
        {
            _world.ValidateTemplateSpawn(_singletonMask, 1);
            var archetype = GetArchetype();
            var entity = _world.SpawnTemplate(archetype);
            for (var i = 0; i < _components.Count; i++)
            {
                _components[i].ApplySingle(entity.Index, archetype);
            }

            _world.FinalizeTemplateSpawn(entity, _componentMask, _singletonMask);
            return entity;
        }

        public void SpawnBatch(Span<Entity> resultEntities)
        {
            SpawnBatch(resultEntities.Length, resultEntities);
        }

        public void SpawnBatch(int count, Span<Entity> resultEntities = default)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (resultEntities.Length > 0 && resultEntities.Length < count)
                throw new ArgumentException("resultEntities must be large enough for count.", nameof(resultEntities));

            var targetCount = count;
            if (targetCount <= 0) return;

            var rentedIndices = System.Buffers.ArrayPool<int>.Shared.Rent(targetCount);
            var entityIndices = rentedIndices.AsSpan(0, targetCount);

            try
            {
                _world.ValidateTemplateSpawn(_singletonMask, targetCount);
                var archetype = GetArchetype();
                _world.SpawnTemplateBatch(targetCount, resultEntities, entityIndices, archetype);
                for (var i = 0; i < _components.Count; i++)
                {
                    _components[i].ApplyBatch(entityIndices, archetype);
                }

                _world.FinalizeTemplateBatch(entityIndices, _componentMask, _singletonMask);
            }
            finally
            {
                System.Buffers.ArrayPool<int>.Shared.Return(rentedIndices);
            }
        }

        private Archetype GetArchetype() =>
            _archetype ??= _world.GetOrCreateTemplateArchetype(_componentMask.ToTypeIds());
    }

    #endregion

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
    #region --- 7. Diagnostics ---

    /// <summary>Describes one component storage at the time a World diagnostics snapshot was captured.</summary>
    public readonly struct ComponentStorageDiagnostics
    {
        internal ComponentStorageDiagnostics(int typeId, Type componentType, int count, int capacity,
            bool isSingleton)
        {
            TypeId = typeId;
            ComponentType = componentType;
            Count = count;
            Capacity = capacity;
            IsSingleton = isSingleton;
        }

        public int TypeId { get; }
        public Type ComponentType { get; }
        public int Count { get; }
        public int Capacity { get; }
        public bool IsSingleton { get; }
    }

    /// <summary>
    /// A point-in-time, read-only view of a World's current allocation and storage state.
    /// Creating a snapshot performs work and allocates; normal World operations do not maintain diagnostic data.
    /// </summary>
    public sealed class WorldDiagnosticsSnapshot
    {
        private readonly ComponentStorageDiagnostics[] _componentStorages;

        internal WorldDiagnosticsSnapshot(int aliveEntityCount, int allocatedEntitySlotCount, int entityCapacity,
            int structuralVersion, int archetypeCount, int cachedQueryPlanCount, int relationStorageCount, int bindingCount,
            ComponentStorageDiagnostics[] componentStorages)
        {
            AliveEntityCount = aliveEntityCount;
            AllocatedEntitySlotCount = allocatedEntitySlotCount;
            EntityCapacity = entityCapacity;
            StructuralVersion = structuralVersion;
            ArchetypeCount = archetypeCount;
            CachedQueryPlanCount = cachedQueryPlanCount;
            RelationStorageCount = relationStorageCount;
            BindingCount = bindingCount;
            _componentStorages = componentStorages;
        }

        public int AliveEntityCount { get; }
        public int AllocatedEntitySlotCount { get; }
        public int RecycledEntitySlotCount => AllocatedEntitySlotCount - AliveEntityCount;
        public int EntityCapacity { get; }
        public int StructuralVersion { get; }
        public int ArchetypeCount { get; }
        public int ComponentStorageCount => _componentStorages.Length;
        public int CachedQueryPlanCount { get; }
        public int RelationStorageCount { get; }
        public int BindingCount { get; }
        public ReadOnlySpan<ComponentStorageDiagnostics> ComponentStorages => _componentStorages;

        public override string ToString()
        {
            var builder = new System.Text.StringBuilder(256 + _componentStorages.Length * 64);
            builder.Append("WorldDiagnosticsSnapshot")
                .Append(" { AliveEntities: ").Append(AliveEntityCount)
                .Append(", AllocatedEntitySlots: ").Append(AllocatedEntitySlotCount)
                .Append(", RecycledEntitySlots: ").Append(RecycledEntitySlotCount)
                .Append(", EntityCapacity: ").Append(EntityCapacity)
                .Append(", StructuralVersion: ").Append(StructuralVersion)
                .Append(", Archetypes: ").Append(ArchetypeCount)
                .Append(", ComponentStorages: ").Append(ComponentStorageCount)
                .Append(", CachedQueryPlans: ").Append(CachedQueryPlanCount)
                .Append(", RelationStorages: ").Append(RelationStorageCount)
                .Append(", Bindings: ").Append(BindingCount)
                .Append(" }");

            for (var i = 0; i < _componentStorages.Length; i++)
            {
                ref readonly var storage = ref _componentStorages[i];
                builder.AppendLine()
                    .Append("[").Append(storage.TypeId).Append("] ")
                    .Append(storage.ComponentType.FullName ?? storage.ComponentType.Name)
                    .Append(" { Count: ").Append(storage.Count)
                    .Append(", Capacity: ").Append(storage.Capacity)
                    .Append(", Singleton: ").Append(storage.IsSingleton)
                    .Append(" }");
            }

            return builder.ToString();
        }
    }

    /// <summary>Describes one live Entity and its range in an Entity-list diagnostics snapshot.</summary>
    public readonly struct EntityDiagnostics
    {
        internal EntityDiagnostics(in Entity entity, int componentStartIndex, int componentCount,
            int componentMaskHash)
        {
            Entity = entity;
            ComponentStartIndex = componentStartIndex;
            ComponentCount = componentCount;
            ComponentMaskHash = componentMaskHash;
        }

        public Entity Entity { get; }
        internal int ComponentStartIndex { get; }
        public int ComponentCount { get; }
        public int ComponentMaskHash { get; }

        public override string ToString() =>
            $"Entity {Entity.Index}:{Entity.Version} {{ Components: {ComponentCount}, MaskHash: {ComponentMaskHash:X8} }}";
    }

    /// <summary>
    /// A point-in-time list of all live Entities and the component types owned by each Entity.
    /// Component type IDs are stored in one flattened array instead of allocating an array per Entity.
    /// </summary>
    public sealed class EntityListDiagnosticsSnapshot
    {
        private readonly EntityDiagnostics[] _entities;
        private readonly int[] _componentTypeIds;
        private readonly Type?[] _componentTypesById;

        internal EntityListDiagnosticsSnapshot(EntityDiagnostics[] entities, int[] componentTypeIds,
            Type?[] componentTypesById)
        {
            _entities = entities;
            _componentTypeIds = componentTypeIds;
            _componentTypesById = componentTypesById;
        }

        public int EntityCount => _entities.Length;
        public ReadOnlySpan<EntityDiagnostics> Entities => _entities;

        public ReadOnlySpan<int> GetComponentTypeIds(in EntityDiagnostics entity)
        {
            if ((uint)entity.ComponentStartIndex > (uint)_componentTypeIds.Length
                || (uint)entity.ComponentCount > (uint)(_componentTypeIds.Length - entity.ComponentStartIndex))
                throw new ArgumentException("Entity diagnostics do not belong to this snapshot.", nameof(entity));
            return _componentTypeIds.AsSpan(entity.ComponentStartIndex, entity.ComponentCount);
        }

        public Type GetComponentType(int typeId)
        {
            if ((uint)typeId >= (uint)_componentTypesById.Length || _componentTypesById[typeId] == null)
                throw new ArgumentOutOfRangeException(nameof(typeId));
            return _componentTypesById[typeId]!;
        }

        /// <summary>Formats one Entity and its component type names as a single diagnostic line.</summary>
        public string FormatEntity(in EntityDiagnostics entity)
        {
            var builder = new System.Text.StringBuilder(48 + entity.ComponentCount * 16);
            AppendFormattedEntity(builder, entity);
            return builder.ToString();
        }

        /// <summary>Appends one Entity and its component type names to an existing builder.</summary>
        public void AppendFormattedEntity(System.Text.StringBuilder builder, in EntityDiagnostics entity)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            AppendEntity(builder, entity);
        }

        public override string ToString()
        {
            var builder = new System.Text.StringBuilder(64 + _entities.Length * 64);
            builder.Append("EntityListDiagnosticsSnapshot { Entities: ")
                .Append(EntityCount)
                .Append(" }");

            for (var entityIndex = 0; entityIndex < _entities.Length; entityIndex++)
            {
                ref readonly var entity = ref _entities[entityIndex];
                builder.AppendLine();
                AppendEntity(builder, entity);
            }

            return builder.ToString();
        }

        private void AppendEntity(System.Text.StringBuilder builder, in EntityDiagnostics entity)
        {
            var componentTypeIds = GetComponentTypeIds(entity);
            builder.Append("Entity ").Append(entity.Entity.Index);
            if (entity.Entity.Version != 0)
                builder.Append(':').Append(entity.Entity.Version);

            builder.Append("  [").Append(entity.ComponentCount).Append("]  ");
            for (var i = 0; i < componentTypeIds.Length; i++)
            {
                if (i != 0) builder.Append(", ");
                AppendTypeName(builder, GetComponentType(componentTypeIds[i]));
            }
        }

        private static void AppendTypeName(System.Text.StringBuilder builder, Type type)
        {
            if (type.IsArray)
            {
                AppendTypeName(builder, type.GetElementType()!);
                builder.Append('[').Append(',', type.GetArrayRank() - 1).Append(']');
                return;
            }

            var name = type.Name;
            var genericMarkerIndex = name.IndexOf('`');
            builder.Append(genericMarkerIndex < 0 ? name : name.Substring(0, genericMarkerIndex));
            if (!type.IsGenericType) return;

            builder.Append('<');
            var genericArguments = type.GetGenericArguments();
            for (var i = 0; i < genericArguments.Length; i++)
            {
                if (i != 0) builder.Append(", ");
                AppendTypeName(builder, genericArguments[i]);
            }

            builder.Append('>');
        }
    }

    /// <summary>A boxed copy of one component owned by a selected Entity.</summary>
    public readonly struct EntityComponentDiagnostics
    {
        internal EntityComponentDiagnostics(int typeId, Type componentType, object value)
        {
            TypeId = typeId;
            ComponentType = componentType;
            Value = value;
        }

        public int TypeId { get; }
        public Type ComponentType { get; }
        public object Value { get; }

        public override string ToString() => $"{ComponentType.Name}: {Value}";
    }

    /// <summary>
    /// A point-in-time, read-only view of the component values owned by one selected Entity.
    /// Values are boxed copies and modifying them does not modify the World.
    /// </summary>
    public sealed class EntityDiagnosticsSnapshot
    {
        private readonly EntityComponentDiagnostics[] _components;

        internal EntityDiagnosticsSnapshot(in Entity entity, bool isAlive,
            EntityComponentDiagnostics[] components)
        {
            Entity = entity;
            IsAlive = isAlive;
            _components = components;
        }

        public Entity Entity { get; }
        public bool IsAlive { get; }
        public int ComponentCount => _components.Length;
        public ReadOnlySpan<EntityComponentDiagnostics> Components => _components;
    }

    #endregion
#endif

    #region --- 7. World & Fast Dense Query Engine ---

    internal sealed class ObjectReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ObjectReferenceComparer Instance = new();

        private ObjectReferenceComparer()
        {
        }

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    internal interface IStructBindingStorage
    {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        int Count { get; }
#endif
        void RemoveEntity(int entityIndex);
        void Clear();
    }

    internal sealed class StructBindingStorage<T> : IStructBindingStorage
    {
        private readonly Dictionary<HashedKey, Entity> _bindings;
        private readonly Dictionary<int, int> _entityBindingHeads;
        private HashedKey[] _entityBindingKeys;
        private int[] _entityBindingNext;
        private int _entityBindingCount;
        private int _freeEntityBinding = -1;

        private readonly struct HashedKey
        {
            internal readonly T Value;
            internal readonly int HashCode;

            internal HashedKey(T value, int hashCode)
            {
                Value = value;
                HashCode = hashCode;
            }
        }

        private sealed class HashedKeyComparer : IEqualityComparer<HashedKey>
        {
            internal static readonly HashedKeyComparer Instance = new();

            private HashedKeyComparer()
            {
            }

            public bool Equals(HashedKey x, HashedKey y) =>
                EqualityComparer<T>.Default.Equals(x.Value, y.Value);

            public int GetHashCode(HashedKey key) => key.HashCode;
        }

        internal StructBindingStorage(int capacity)
        {
            _bindings = new Dictionary<HashedKey, Entity>(capacity, HashedKeyComparer.Instance);
            _entityBindingHeads = new Dictionary<int, int>(capacity);
            _entityBindingKeys = new HashedKey[capacity];
            _entityBindingNext = new int[capacity];
        }

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        public int Count => _bindings.Count;
#endif

        public bool Bind(T key, Entity entity)
        {
            var hashedKey = CreateKey(key);
            if (_bindings.TryGetValue(hashedKey, out var existing))
            {
                if (existing == entity) return false;
                throw new InvalidOperationException("The external value is already bound to another Entity.");
            }

            _bindings.Add(hashedKey, entity);
            var firstForEntity = !_entityBindingHeads.TryGetValue(entity.Index, out var head);
            var node = AllocateEntityBinding();
            _entityBindingKeys[node] = hashedKey;
            _entityBindingNext[node] = firstForEntity ? -1 : head;
            _entityBindingHeads[entity.Index] = node;
            return firstForEntity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetEntity(T key, out Entity entity) => _bindings.TryGetValue(CreateKey(key), out entity);

        public bool Unbind(T key, Entity entity, out bool removedLastForEntity)
        {
            removedLastForEntity = false;
            var hashedKey = CreateKey(key);
            if (!_bindings.TryGetValue(hashedKey, out var bound) || bound != entity) return false;
            _bindings.Remove(hashedKey);
            var previous = -1;
            var node = _entityBindingHeads[entity.Index];
            while (node >= 0)
            {
                var next = _entityBindingNext[node];
                if (HashedKeyComparer.Instance.Equals(_entityBindingKeys[node], hashedKey))
                {
                    if (previous < 0) _entityBindingHeads[entity.Index] = next;
                    else _entityBindingNext[previous] = next;
                    ReleaseEntityBinding(node);
                    if (next >= 0 || previous >= 0) return true;
                    _entityBindingHeads.Remove(entity.Index);
                    removedLastForEntity = true;
                    return true;
                }
                previous = node;
                node = next;
            }
            return true;
        }

        public void RemoveEntity(int entityIndex)
        {
            if (!_entityBindingHeads.TryGetValue(entityIndex, out var node)) return;
            while (node >= 0)
            {
                var next = _entityBindingNext[node];
                _bindings.Remove(_entityBindingKeys[node]);
                ReleaseEntityBinding(node);
                node = next;
            }
            _entityBindingHeads.Remove(entityIndex);
        }

        private int AllocateEntityBinding()
        {
            if (_freeEntityBinding >= 0)
            {
                var node = _freeEntityBinding;
                _freeEntityBinding = _entityBindingNext[node];
                return node;
            }
            if (_entityBindingCount == _entityBindingKeys.Length)
            {
                var newSize = _entityBindingKeys.Length * 2;
                Array.Resize(ref _entityBindingKeys, newSize);
                Array.Resize(ref _entityBindingNext, newSize);
            }
            return _entityBindingCount++;
        }

        private void ReleaseEntityBinding(int node)
        {
            _entityBindingKeys[node] = default;
            _entityBindingNext[node] = _freeEntityBinding;
            _freeEntityBinding = node;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static HashedKey CreateKey(T value) => new(value, value!.GetHashCode());

        public void Clear()
        {
            _bindings.Clear();
            _entityBindingHeads.Clear();
            Array.Clear(_entityBindingKeys, 0, _entityBindingKeys.Length);
            _entityBindingCount = 0;
            _freeEntityBinding = -1;
        }
    }

    public sealed partial class World : IDisposable
    {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        private readonly AllocationDiagnostics _allocationDiagnostics = new AllocationDiagnostics();
        internal AllocationDiagnostics AllocationDiagnostics => _allocationDiagnostics;

        /// <summary>Enables allocation-path counters. Updating the counters does not allocate.</summary>
        public bool AllocationDiagnosticsEnabled
        {
            get => _allocationDiagnostics.Enabled;
            set => _allocationDiagnostics.Enabled = value;
        }

        /// <summary>Clears the counters without changing whether collection is enabled.</summary>
        public void ResetAllocationDiagnostics() => _allocationDiagnostics.Reset();

        /// <summary>Captures the counters in an allocation-free value snapshot.</summary>
        public AllocationDiagnosticsSnapshot GetAllocationDiagnostics() =>
            new AllocationDiagnosticsSnapshot(_allocationDiagnostics);

        /// <summary>
        /// Formats allocation counters together with the last capacity-miss Archetype and Component type.
        /// Call only after <see cref="AllocationDiagnosticsSnapshot.HasEvents"/> becomes true.
        /// </summary>
        public string FormatAllocationDiagnostics(in AllocationDiagnosticsSnapshot snapshot)
        {
            var builder = new System.Text.StringBuilder(snapshot.ToString());
            var archetypeIndex = snapshot.LastChunkArchetypeIndex;
            if ((uint)archetypeIndex < (uint)_archetypes.All.Count)
            {
                var archetype = _archetypes.All[archetypeIndex];
                builder.Append(" ChunkCapacityMiss={ArchetypeIndex:").Append(archetypeIndex)
                    .Append(", PreviousCapacity:").Append(snapshot.LastChunkEntityCount)
                    .Append(", Components:[");
                for (var i = 0; i < archetype.TypeIds.Length; i++)
                {
                    if (i != 0) builder.Append(", ");
                    var type = ComponentTypeRegistry.GetType(archetype.TypeIds[i]);
                    builder.Append(type.FullName ?? type.Name);
                }
                builder.Append("]}");
            }

            var componentTypeId = snapshot.LastComponentPageTypeId;
            if (componentTypeId >= 0 && componentTypeId < ComponentTypeRegistry.Count)
            {
                var type = ComponentTypeRegistry.GetType(componentTypeId);
                builder.Append(" ComponentPageCapacityMiss={TypeId:").Append(componentTypeId)
                    .Append(", Type:").Append(type.FullName ?? type.Name).Append('}');
            }
            return builder.ToString();
        }
#endif

        /// <summary>
        /// Combines consecutive Entity.Add calls into one final Archetype transition per Entity.
        /// The World command buffer supplies the reusable, typed payload storage.
        /// </summary>
        public ref struct StructuralBatchScope
        {
            private World? _world;
            internal StructuralBatchScope(World world) => _world = world;

            public void Dispose()
            {
                var world = _world;
                if (world == null) return;
                _world = null;
                world.EndStructuralBatch();
            }
        }

        private uint[] _versions;
        private EntityLocation[] _locations;
        private readonly ArchetypeCatalog _archetypes;
        private EntityCommandBuffer? _commandBuffer;
        private ComponentMask[]? _relationForwardMasks;
        private ComponentMask[]? _relationBackwardMasks;
        private int _reservedRelationTypeId = -1;
        private int[] _componentVersions = new int[256];
        private Stack<int> _freeIndices;
        private int _entityCount;
        private readonly int _defaultCapacity;

        private Entity[]? _singletonEntities;
        private ComponentMask _singletonTypeMask;
        private Dictionary<int, RelationStorage>? _relationStorages;

        private Dictionary<object, Entity>? _bindings;
        private Dictionary<int, int>? _entityBindingHeads;
        private object?[]? _entityBindingObjects;
        private int[]? _entityBindingNext;
        private int _entityBindingCount;
        private int _freeEntityBinding = -1;
        private Dictionary<Type, IStructBindingStorage>? _structBindingStorages;
        private Dictionary<Type, ArchetypeQueryPlan>? _baseArchetypeQueryPlans;
        private List<EntityQueryPlan>? _entityQueryPlans;
        private List<IIncrementalQueryPlan>?[]? _incrementalQueryPlans;
        private CollectorRegistry? _collectors;
        private int _parallelQueryActive;
        private ParallelQueryRunner? _parallelQueryRunner;
        private bool _disposed;
        private int _structuralBatchDepth;

        /// <summary>Gets the version incremented whenever the Entity/component structure changes.</summary>
        public int StructuralVersion { get; private set; }

        /// <summary>
        /// Defers Entity.Add calls until this scope is disposed. Reserve the World command buffer and
        /// its component payloads during warmup to keep recording and playback allocation-free.
        /// Component reads, removals, and Queries flush pending additions before continuing.
        /// </summary>
        public StructuralBatchScope BeginStructuralBatch()
        {
            ThrowIfDisposed();
            ThrowIfParallelQueryActive();
            if (_structuralBatchDepth == 0 && _commandBuffer is { HasPendingCommands: true })
                throw new InvalidOperationException(
                    "The World command buffer must be empty before beginning a structural batch.");
            _structuralBatchDepth++;
            return new StructuralBatchScope(this);
        }

        private void EndStructuralBatch()
        {
            if (_structuralBatchDepth <= 0)
                throw new InvalidOperationException("Structural batch scopes must be disposed exactly once.");
            _structuralBatchDepth--;
            if (_structuralBatchDepth == 0) _commandBuffer?.Playback();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void FlushStructuralBatch()
        {
            if (_structuralBatchDepth == 0 || _commandBuffer is not { HasPendingCommands: true }) return;
            var depth = _structuralBatchDepth;
            _structuralBatchDepth = 0;
            try
            {
                _commandBuffer.Playback();
            }
            finally
            {
                _structuralBatchDepth = depth;
            }
        }

        internal int EntityCapacity => _versions.Length;

        internal int GetComponentVersion(int typeId) => _componentVersions[typeId];

        internal int GetComponentVersion(in ComponentMask mask)
        {
            unchecked
            {
                var hash = 17;
                AccumulateVersionWord(mask.B0, 0, ref hash);
                AccumulateVersionWord(mask.B1, 64, ref hash);
                AccumulateVersionWord(mask.B2, 128, ref hash);
                AccumulateVersionWord(mask.B3, 192, ref hash);
                for (var i = 0; i < mask.OverflowWordCount; i++)
                    AccumulateVersionWord(mask.GetOverflowWord(i), 256 + i * 64, ref hash);
                return hash;
            }
        }

        private void AccumulateVersionWord(ulong word, int offset, ref int hash)
        {
            while (word != 0)
            {
                var bit = TrailingZeroCount(word);
                var typeId = offset + bit;
                hash = hash * 31 + typeId;
                hash = hash * 31 + _componentVersions[typeId];
                word &= word - 1;
            }
        }

        internal void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(World));
        }

        internal void EnterParallelQuery()
        {
            ThrowIfDisposed();
            if (Interlocked.CompareExchange(ref _parallelQueryActive, 1, 0) != 0)
                throw new InvalidOperationException("A protected Query operation is already executing on this World.");
        }

        internal void ExitParallelQuery() => Volatile.Write(ref _parallelQueryActive, 0);

        internal void ExecuteParallelQuery(ParallelQueryJob job) => EnsureParallelQueryRunner().Run(job);

        /// <summary>
        /// Creates this World's reusable parallel Query worker threads during initialization.
        /// Calling this more than once has no effect. Query-specific jobs and work-item buffers
        /// remain lazily allocated by their first ParallelQuery.Run invocation.
        /// </summary>
        public void WarmParallelQueryWorkers()
        {
            ThrowIfDisposed();
            ThrowIfParallelQueryActive();
            if (Environment.ProcessorCount <= 1) return;
            EnsureParallelQueryRunner();
        }

        private ParallelQueryRunner EnsureParallelQueryRunner() =>
            _parallelQueryRunner ??= new ParallelQueryRunner(
                Math.Max(1, Environment.ProcessorCount - 1));

        internal static int GetParallelRangeReservationCount(int maximumEntityCount, int matchingArchetypeCount,
            int batchSize)
        {
            var unit = Math.Min(batchSize, ComponentPageManager.PageCapacity);
            if (maximumEntityCount == 0) return 0;
            // Each matching Archetype can contribute one partially filled final unit.
            return checked((maximumEntityCount + unit - 1) / unit
                + Math.Max(0, matchingArchetypeCount - 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfParallelQueryActive()
        {
            if (Volatile.Read(ref _parallelQueryActive) != 0)
                throw new InvalidOperationException(
                    "World structure cannot be changed while a protected Query operation is executing. " +
                    "Record structural changes in an EntityCommandBuffer and call Playback after the Query operation completes.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ValidateQueryStructuralVersion(int expectedVersion)
        {
#if _INTERNAL_DERIVED_USE_VALIDATION
            if (StructuralVersion != expectedVersion)
                throw new InvalidOperationException(
                    "World structure changed while a Query was executing. Record structural changes in an EntityCommandBuffer and call Playback after the Query completes.");
#endif
        }

        internal ArchetypeQueryPlan GetOrCreateBaseArchetypeQueryPlan(Type queryType, int[] required)
        {
            var plans = _baseArchetypeQueryPlans ??= new Dictionary<Type, ArchetypeQueryPlan>();
            if (plans.TryGetValue(queryType, out var existing)) return existing;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled) _allocationDiagnostics.QueryPlanCreations++;
            var plan = new ArchetypeQueryPlan(_archetypes, _allocationDiagnostics, required);
#else
            var plan = new ArchetypeQueryPlan(_archetypes, required);
#endif
            plans.Add(queryType, plan);
            return plan;
        }

        internal ArchetypeQueryPlan CreateArchetypeQueryPlan(int[] required, int[] excluded, int[] any)
        {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled) _allocationDiagnostics.QueryPlanCreations++;
            return new ArchetypeQueryPlan(_archetypes, _allocationDiagnostics, required, excluded, any);
#else
            return new ArchetypeQueryPlan(_archetypes, required, excluded, any);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool Matches(Entity entity, ArchetypeQueryPlan plan) =>
            MatchesAfterStructuralFlush(entity, plan);

        private bool MatchesAfterStructuralFlush(Entity entity, ArchetypeQueryPlan plan)
        {
            FlushStructuralBatch();
            return IsAlive(entity) && _locations[entity.Index].IsValid && plan.IsMatch(_locations[entity.Index].Archetype);
        }

        internal void RegisterIncrementalQueryPlan(IIncrementalQueryPlan plan, in ComponentMask observedTypes)
        {
            _incrementalQueryPlans ??= new List<IIncrementalQueryPlan>?[_componentVersions.Length];
            RegisterIncrementalQueryPlanWord(plan, observedTypes.B0, 0);
            RegisterIncrementalQueryPlanWord(plan, observedTypes.B1, 64);
            RegisterIncrementalQueryPlanWord(plan, observedTypes.B2, 128);
            RegisterIncrementalQueryPlanWord(plan, observedTypes.B3, 192);
            for (var i = 0; i < observedTypes.OverflowWordCount; i++)
                RegisterIncrementalQueryPlanWord(plan, observedTypes.GetOverflowWord(i), 256 + i * 64);
        }

        private void RegisterIncrementalQueryPlanWord(IIncrementalQueryPlan plan, ulong word, int offset)
        {
            while (word != 0)
            {
                var bit = TrailingZeroCount(word);
                var typeId = offset + bit;
                EnsureComponentTypeCapacity(typeId);
                (_incrementalQueryPlans![typeId] ??= new List<IIncrementalQueryPlan>()).Add(plan);
                word &= word - 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NotifyFilterComponentChanged(int typeId, int entityIndex)
        {
            var plans = _incrementalQueryPlans?[typeId];
            if (plans == null) return;
            for (var i = 0; i < plans.Count; i++) plans[i].OnFilterComponentChanged(entityIndex);
        }

        public World(int defaultCapacity = 64)
        {
            if (defaultCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(defaultCapacity));

            _defaultCapacity = defaultCapacity;
            var initialCapacity = Math.Max(1, defaultCapacity);
            _versions = new uint[initialCapacity];
            _locations = new EntityLocation[initialCapacity];
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            _archetypes = new ArchetypeCatalog(_allocationDiagnostics);
#else
            _archetypes = new ArchetypeCatalog();
#endif
            _freeIndices = new Stack<int>(initialCapacity);
            _bindings = new Dictionary<object, Entity>(initialCapacity, ObjectReferenceComparer.Instance);
            _entityBindingHeads = new Dictionary<int, int>(initialCapacity);
            _entityBindingObjects = new object?[initialCapacity];
            _entityBindingNext = new int[initialCapacity];
        }

        public EntityTemplate CreateTemplate()
        {
            ThrowIfDisposed();
            return new EntityTemplate(this);
        }

        /// <summary>Preallocates Entity IDs, locations, and active Relation masks. Existing storage is never shrunk.</summary>
        public void ReserveEntities(int capacity)
        {
            ThrowIfDisposed();
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            ThrowIfParallelQueryActive();
            if (capacity <= _versions.Length) return;
            Array.Resize(ref _versions, capacity);
            Array.Resize(ref _locations, capacity);
            if (_relationForwardMasks != null) Array.Resize(ref _relationForwardMasks, capacity);
            if (_relationBackwardMasks != null) Array.Resize(ref _relationBackwardMasks, capacity);
            var freeIndices = _freeIndices.ToArray();
            _freeIndices = new Stack<int>(capacity);
            for (var i = freeIndices.Length - 1; i >= 0; i--) _freeIndices.Push(freeIndices[i]);
        }

        /// <summary>The single command buffer owned by this World.</summary>
        public EntityCommandBuffer CommandBuffer
        {
            get
            {
                ThrowIfDisposed();
                return _commandBuffer ??= new EntityCommandBuffer(this);
            }
        }

       private Action<string>? _archetypeCreatedLogger;
       private Action<string>? _transitionCreatedLogger;

        /// <summary>
        /// Optional diagnostic logger invoked only when a new Archetype is created. Assigning a logger adds
        /// formatting and logging allocations to Archetype creation; leaving it null has no logging cost.
        /// </summary>
        public Action<string>? ArchetypeCreatedLogger
        {
            get => _archetypeCreatedLogger;
            set
            {
                ThrowIfDisposed();
                _archetypeCreatedLogger = value;
                _archetypes.ArchetypeCreated = value == null ? null : LogArchetypeCreated;
            }
        }

        /// <summary>
        /// Optional diagnostic logger invoked only when an uncached Archetype transition is created.
        /// Assigning a logger adds formatting and logging allocations to transition creation; leaving it null has no logging cost.
        /// </summary>
        public Action<string>? TransitionCreatedLogger
        {
            get => _transitionCreatedLogger;
            set
            {
                ThrowIfDisposed();
                _transitionCreatedLogger = value;
                _archetypes.TransitionCreated = value == null ? null : LogTransitionCreated;
            }
        }

        internal int AvailableEntityPageCount => _archetypes.Pages.AvailableEntityPageCount;

        private void LogArchetypeCreated(Archetype archetype)
        {
            var logger = _archetypeCreatedLogger;
            if (logger == null) return;
            var typeIds = archetype.TypeIds;
            var builder = new System.Text.StringBuilder(32 + typeIds.Length * 24);
            builder.Append("LitheEcs Archetype created: {");
            for (var i = 0; i < typeIds.Length; i++)
            {
                if (i != 0) builder.Append(", ");
                var type = ComponentTypeRegistry.GetType(typeIds[i]);
                builder.Append(type.FullName ?? type.Name);
            }
            builder.Append('}');
            logger(builder.ToString());
        }

        private void LogTransitionCreated(Archetype source, Archetype destination)
        {
            var logger = _transitionCreatedLogger;
            if (logger == null) return;
            var builder = new System.Text.StringBuilder(128);
            builder.Append("LitheEcs transition created:");
            AppendComponentDifference(builder, " Added={", destination.TypeIds, source.TypeIds);
            AppendComponentDifference(builder, " Removed={", source.TypeIds, destination.TypeIds);
            AppendArchetypeTypes(builder, " From={", source.TypeIds);
            AppendArchetypeTypes(builder, " To={", destination.TypeIds);
            logger(builder.ToString());
        }

        private static void AppendComponentDifference(System.Text.StringBuilder builder, string prefix,
            int[] candidates, int[] excluded)
        {
            builder.Append(prefix);
            var written = false;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (Array.BinarySearch(excluded, candidates[i]) >= 0) continue;
                if (written) builder.Append(", ");
                AppendComponentType(builder, candidates[i]);
                written = true;
            }
            builder.Append('}');
        }

        private static void AppendArchetypeTypes(System.Text.StringBuilder builder, string prefix, int[] typeIds)
        {
            builder.Append(prefix);
            for (var i = 0; i < typeIds.Length; i++)
            {
                if (i != 0) builder.Append(", ");
                AppendComponentType(builder, typeIds[i]);
            }
            builder.Append('}');
        }

        private static void AppendComponentType(System.Text.StringBuilder builder, int typeId)
        {
            var type = ComponentTypeRegistry.GetType(typeId);
            builder.Append(type.FullName ?? type.Name);
        }

        /// <summary>Preallocates storage for a component layout. Existing storage is never shrunk.</summary>
        public void ReserveArchetype(int capacity, Action<ArchetypeBuilder> configure)
        {
            ThrowIfDisposed();
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            ThrowIfParallelQueryActive();

            var builder = new ArchetypeBuilder(this);
            configure(builder);
            var typeIds = builder.GetTypeIds();
            if (typeIds.Length == 0)
                throw new InvalidOperationException("At least one component type is required.");
            var archetype = _archetypes.WithMany(_archetypes.Empty, typeIds);
            // Dedicated Archetype storage must be added to the pool before it is rented.
            // Otherwise a later dedicated reservation can consume pages previously set
            // aside by ReserveArchetypeGroup, making the shared guarantee depend
            // on call order.
            _archetypes.Pages.ReserveAdditional(typeIds, capacity);
            archetype.EnsureCapacity(capacity);
            _archetypes.WarmAddTransitionsTo(archetype);
            _archetypes.WarmRemoveTransitionsTo(archetype);
        }

        /// <summary>
        /// Preallocates one total capacity shared by a group of Archetype layouts.
        /// The sum of entities in all layouts is expected not to exceed totalCapacity.
        /// </summary>
        public void ReserveArchetypeGroup(int totalCapacity, Action<ArchetypeGroupBuilder> configure)
        {
            ThrowIfDisposed();
            if (totalCapacity < 0) throw new ArgumentOutOfRangeException(nameof(totalCapacity));
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            ThrowIfParallelQueryActive();

            var builder = new ArchetypeGroupBuilder(this);
            configure(builder);
            var layouts = builder.Layouts;
            if (layouts.Count == 0)
                throw new InvalidOperationException("At least one Archetype layout is required.");
            ReserveArchetypeLayouts(totalCapacity, layouts, builder.GetSharedTypeIds());
        }

        private void ReserveArchetypeLayouts(int totalCapacity, List<int[]> layouts, int[] sharedTypeIds)
        {
            var layoutTypeCounts = new int[sharedTypeIds.Length];
            for (var layoutIndex = 0; layoutIndex < layouts.Count; layoutIndex++)
            {
                var layout = layouts[layoutIndex];
                for (var typeIndex = 0; typeIndex < sharedTypeIds.Length; typeIndex++)
                    if (Array.BinarySearch(layout, sharedTypeIds[typeIndex]) >= 0)
                        layoutTypeCounts[typeIndex]++;
            }
            _archetypes.Pages.ReserveShared(
                sharedTypeIds, layoutTypeCounts, layouts.Count, totalCapacity);

            for (var i = 0; i < layouts.Count; i++) RegisterArchetype(layouts[i]);
        }

        private void RegisterArchetype(int[] typeIds)
        {
            var archetype = _archetypes.GetOrCreate(typeIds);
            archetype.EnsureChunkShellCount(_archetypes.Pages.GetSharedUsablePageCount(typeIds));
            _archetypes.WarmAddTransitionsTo(archetype);
            _archetypes.WarmRemoveTransitionsTo(archetype);
        }

        /// <summary>Preallocates one Relation type's forward and backward storage.</summary>
        public void ReserveRelation<TRelation>(int capacity) where TRelation : struct
        {
            ReserveRelation<TRelation>(capacity, 0, 0);
        }

        /// <summary>
        /// Preallocates one Relation type's forward and backward storage together with its search result buffers.
        /// Search capacities are the maximum results returned for one source or target, not the total relation count.
        /// </summary>
        public void ReserveRelation<TRelation>(
            int capacity,
            int forwardSearchCapacity,
            int backwardSearchCapacity) where TRelation : struct
        {
            ThrowIfDisposed();
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (forwardSearchCapacity < 0) throw new ArgumentOutOfRangeException(nameof(forwardSearchCapacity));
            if (backwardSearchCapacity < 0) throw new ArgumentOutOfRangeException(nameof(backwardSearchCapacity));
            ThrowIfParallelQueryActive();

            var typeId = ComponentType<TRelation>.Id;
            if (typeId > _reservedRelationTypeId) _reservedRelationTypeId = typeId;
            EnsureComponentTypeCapacity(typeId);
            EnsureRelationMasks();
            for (var i = 0; i < _relationForwardMasks!.Length; i++)
            {
                _relationForwardMasks[i].EnsureBitCapacity(typeId);
                _relationBackwardMasks![i].EnsureBitCapacity(typeId);
            }
            var storages = _relationStorages ??= new Dictionary<int, RelationStorage>();
            if (!storages.TryGetValue(typeId, out var storage))
            {
                storage = new RelationStorage(_versions.Length, capacity);
                storages.Add(typeId, storage);
            }
            else
            {
                storage.EnsureCapacity(_versions.Length, capacity);
            }
            storage.EnsureSearchCapacity(forwardSearchCapacity, backwardSearchCapacity);
        }

        public EntityCollector Observe<T>(ComponentEvent events) where T : struct
        {
            ThrowIfDisposed();
            var collector = new EntityCollector(this);
            try
            {
                return collector.Or<T>(events);
            }
            catch
            {
                collector.Dispose();
                throw;
            }
        }

        internal void RegisterCollector<T>(EntityCollector collector, ComponentEvent events) where T : struct
        {
            ThrowIfDisposed();
            const ComponentEvent allEvents = ComponentEvent.KeyAdded | ComponentEvent.KeyRemoved | ComponentEvent.KeyChanged;
            if (events == 0 || (events & ~allEvents) != 0)
                throw new ArgumentOutOfRangeException(nameof(events));
            (_collectors ??= new CollectorRegistry()).Register(ComponentType<T>.Id, collector, events);
        }

        internal void UnregisterCollector(EntityCollector collector)
        {
            var registry = _collectors;
            if (registry == null) return;
            registry.Unregister(collector);
            if (registry.CollectorCount == 0) _collectors = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PublishComponentEvent(int typeId, in Entity entity, ComponentEvent componentEvent)
        {
            _collectors?.Publish(typeId, entity, componentEvent);
        }

        public Entity Spawn()
        {
            ThrowIfDisposed();
            ThrowIfParallelQueryActive();
            // Allocate an alive entity without inserting into the Empty archetype.
            // The first Add/AddComponents places it directly into the destination archetype.
            return SpawnCore(null);
        }

        internal Entity SpawnTemplate(Archetype archetype)
        {
            ThrowIfDisposed();
            ThrowIfParallelQueryActive();
            return SpawnCore(archetype);
        }

        private Entity SpawnCore(Archetype? archetype)
        {
            var index = AllocateEntityIndex();
            StructuralVersion++;
            var entity = new Entity(index, _versions[index], this);
            _locations[index] = archetype != null ? archetype.Add(index) : default;
            return entity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AllocateEntityIndex()
        {
            var index = _freeIndices.Count > 0 ? _freeIndices.Pop() : _entityCount++;
            if (index >= _versions.Length)
            {
                var newSize = _versions.Length * 2;
                Array.Resize(ref _versions, newSize);
                Array.Resize(ref _locations, newSize);
                if (_relationForwardMasks != null) Array.Resize(ref _relationForwardMasks, newSize);
                if (_relationBackwardMasks != null) Array.Resize(ref _relationBackwardMasks, newSize);
            }

            // Version zero is reserved for default(EntityId), so every live Entity starts at one or later.
            if (_versions[index] == 0) _versions[index] = 1;

            if (_relationForwardMasks != null) _relationForwardMasks[index] = default;
            if (_relationBackwardMasks != null) _relationBackwardMasks[index] = default;
            if (_reservedRelationTypeId >= 256)
            {
                _relationForwardMasks![index].EnsureBitCapacity(_reservedRelationTypeId);
                _relationBackwardMasks![index].EnsureBitCapacity(_reservedRelationTypeId);
            }
            return index;
        }

        /// <summary>
        /// Batch-spawns Entities while reserving memory and Entity IDs up front.
        /// </summary>
        public void SpawnBatch(int count, Span<Entity> resultEntities)
        {
            SpawnBatchCore(count, resultEntities, default, null);
        }

        // Used by EntityTemplate. The returned indices are known to be alive and
        // component-free, allowing a storage-level bulk insertion path.
        internal void SpawnTemplateBatch(int count, Span<Entity> resultEntities, Span<int> entityIndices,
            Archetype archetype)
        {
            if (entityIndices.Length < count)
                throw new ArgumentException("entityIndices must be large enough for count.", nameof(entityIndices));

            SpawnBatchCore(count, resultEntities, entityIndices, archetype);
        }

        internal void ValidateTemplateSpawn(in ComponentMask singletonMask, int count)
        {
            ThrowIfDisposed();
            ThrowIfParallelQueryActive();
            if (singletonMask.IsEmpty) return;
            if (count > 1)
                throw new InvalidOperationException("A template containing a singleton component cannot spawn multiple Entities.");

            ValidateTemplateSingletonWord(singletonMask.B0, 0);
            ValidateTemplateSingletonWord(singletonMask.B1, 64);
            ValidateTemplateSingletonWord(singletonMask.B2, 128);
            ValidateTemplateSingletonWord(singletonMask.B3, 192);
            for (var i = 0; i < singletonMask.OverflowWordCount; i++)
                ValidateTemplateSingletonWord(singletonMask.GetOverflowWord(i), 256 + i * 64);
        }

        private void ValidateTemplateSingletonWord(ulong word, int offset)
        {
            while (word != 0)
            {
                var bit = TrailingZeroCount(word);
                var typeId = offset + bit;
                var existing = _singletonEntities?[typeId] ?? default;
                if (IsAlive(existing))
                    throw new InvalidOperationException(
                        $"Singleton component type {typeId} already exists on Entity {existing.Index}.");
                word &= word - 1;
            }
        }

        internal void FinalizeTemplateSpawn(in Entity entity, in ComponentMask componentMask,
            in ComponentMask singletonMask)
        {
            if (_incrementalQueryPlans == null && _collectors == null
                && singletonMask.IsEmpty) return;
            FinalizeTemplateComponentWord(entity, componentMask.B0, singletonMask.B0, 0);
            FinalizeTemplateComponentWord(entity, componentMask.B1, singletonMask.B1, 64);
            FinalizeTemplateComponentWord(entity, componentMask.B2, singletonMask.B2, 128);
            FinalizeTemplateComponentWord(entity, componentMask.B3, singletonMask.B3, 192);
            for (var i = 0; i < componentMask.OverflowWordCount; i++)
                FinalizeTemplateComponentWord(entity, componentMask.GetOverflowWord(i),
                    singletonMask.GetOverflowWord(i), 256 + i * 64);
        }

        private void FinalizeTemplateComponentWord(in Entity entity, ulong componentWord, ulong singletonWord,
            int offset)
        {
            while (componentWord != 0)
            {
                var bit = TrailingZeroCount(componentWord);
                var typeId = offset + bit;
                if (_incrementalQueryPlans != null) NotifyFilterComponentChanged(typeId, entity.Index);
                if ((singletonWord & (1UL << bit)) != 0)
                {
                    (_singletonEntities ??= new Entity[_componentVersions.Length])[typeId] = entity;
                    _singletonTypeMask.Set(typeId);
                }
                if (_collectors != null) PublishComponentEvent(typeId, entity, ComponentEvent.KeyAdded);
                componentWord &= componentWord - 1;
            }
        }

        internal void FinalizeTemplateBatch(ReadOnlySpan<int> entityIndices, in ComponentMask componentMask,
            in ComponentMask singletonMask)
        {
            if (_incrementalQueryPlans == null && _collectors == null
                && singletonMask.IsEmpty) return;
            FinalizeTemplateBatchWord(entityIndices, componentMask.B0, singletonMask.B0, 0);
            FinalizeTemplateBatchWord(entityIndices, componentMask.B1, singletonMask.B1, 64);
            FinalizeTemplateBatchWord(entityIndices, componentMask.B2, singletonMask.B2, 128);
            FinalizeTemplateBatchWord(entityIndices, componentMask.B3, singletonMask.B3, 192);
            for (var i = 0; i < componentMask.OverflowWordCount; i++)
                FinalizeTemplateBatchWord(entityIndices, componentMask.GetOverflowWord(i),
                    singletonMask.GetOverflowWord(i), 256 + i * 64);
        }

        private void FinalizeTemplateBatchWord(ReadOnlySpan<int> entityIndices, ulong componentWord,
            ulong singletonWord, int offset)
        {
            while (componentWord != 0)
            {
                var bit = TrailingZeroCount(componentWord);
                var typeId = offset + bit;
                for (var i = 0; i < entityIndices.Length; i++)
                {
                    var entity = GetEntity(entityIndices[i]);
                    if (_incrementalQueryPlans != null) NotifyFilterComponentChanged(typeId, entity.Index);
                    if ((singletonWord & (1UL << bit)) != 0)
                    {
                        (_singletonEntities ??= new Entity[_componentVersions.Length])[typeId] = entity;
                        _singletonTypeMask.Set(typeId);
                    }
                    if (_collectors != null) PublishComponentEvent(typeId, entity, ComponentEvent.KeyAdded);
                }

                componentWord &= componentWord - 1;
            }
        }

        private void SpawnBatchCore(int count, Span<Entity> resultEntities, Span<int> entityIndices,
            Archetype? archetype)
        {
            ThrowIfDisposed();
            ThrowIfParallelQueryActive();
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            var requiredCapacity = _entityCount + count;
            if (requiredCapacity > _versions.Length)
            {
                var newSize = Math.Max(requiredCapacity, _versions.Length * 2);
                Array.Resize(ref _versions, newSize);
                Array.Resize(ref _locations, newSize);
                if (_relationForwardMasks != null) Array.Resize(ref _relationForwardMasks, newSize);
                if (_relationBackwardMasks != null) Array.Resize(ref _relationBackwardMasks, newSize);
            }

            if (count > 0) StructuralVersion++;

            for (var i = 0; i < count; i++)
            {
                var index = AllocateEntityIndex();
                var entity = new Entity(index, _versions[index], this);
                _locations[index] = archetype != null ? archetype.Add(index) : default;
                if (i < resultEntities.Length) resultEntities[i] = entity;
                if (i < entityIndices.Length) entityIndices[i] = index;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddComponentBatch<T>(ReadOnlySpan<Entity> entities, in T defaultComponent) where T : struct
        {
            ThrowIfDisposed();
            ThrowIfParallelQueryActive();
            ValidateSingletonBatch<T>(entities);
            var typeId = ComponentType<T>.Id;
            EnsureComponentTypeCapacity(typeId);
            var changed = false;

            if (entities.Length > 1 && TryMoveBatchFast(entities, typeId))
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    _locations[entity.Index].Archetype.Get<T>(_locations[entity.Index]) = defaultComponent;
                    NotifyFilterComponentChanged(typeId, entity.Index);
                    PublishComponentEvent(typeId, entity, ComponentEvent.KeyAdded);
                }
                StructuralVersion++;
                _componentVersions[typeId]++;
                return;
            }

            for (var i = 0; i < entities.Length; i++)
            {
                ValidateAlive(entities[i]);
                var entityIdx = entities[i].Index;
                var location = _locations[entityIdx];
                var isNew = !location.IsValid || !location.Archetype.Has(typeId);
                changed |= isNew;
                if (isNew)
                {
                    var destination = location.IsValid
                        ? _archetypes.With(location.Archetype, typeId)
                        : _archetypes.With(_archetypes.Empty, typeId);
                    MoveEntityTo(entities[i], destination);
                    NotifyFilterComponentChanged(typeId, entityIdx);
                }
                _locations[entityIdx].Archetype.Get<T>(_locations[entityIdx]) = defaultComponent;
                PublishComponentEvent(typeId, entities[i],
                    isNew ? ComponentEvent.KeyAdded : ComponentEvent.KeyChanged);
            }

            if (changed)
            {
                StructuralVersion++;
                _componentVersions[typeId]++;
            }

            if (SingletonType<T>.IsSingleton && entities.Length == 1)
            {
                (_singletonEntities ??= new Entity[_componentVersions.Length])[typeId] = entities[0];
                _singletonTypeMask.Set(typeId);
            }
        }

        private bool TryMoveBatchFast(ReadOnlySpan<Entity> entities, int typeId)
        {
            if ((_bindings?.Count ?? 0) != 0 || _structBindingStorages != null || _relationStorages != null ||
                _collectors != null || _incrementalQueryPlans != null)
                return false;

            var first = entities[0];
            ValidateAlive(first);
            var firstLocation = _locations[first.Index];
            if (!firstLocation.IsValid) return false;
            var source = firstLocation.Archetype;
            if (source.Has(typeId)) return false;
            var destination = _archetypes.With(source, typeId);
            var locations = new EntityLocation[entities.Length];
            var sourceLocations = new EntityLocation[entities.Length];
            for (var i = 0; i < entities.Length; i++)
            {
                ValidateAlive(entities[i]);
                var location = _locations[entities[i].Index];
                if (!location.IsValid || !ReferenceEquals(location.Archetype, source)) return false;
                sourceLocations[i] = location;
                locations[i] = destination.Add(entities[i].Index);
                source.CopySharedComponents(location, destination, locations[i]);
            }

            var order = new int[entities.Length];
            for (var i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (left, right) =>
                sourceLocations[right].Row.CompareTo(sourceLocations[left].Row));
            for (var i = 0; i < order.Length; i++)
            {
                var index = order[i];
                var movedIndex = source.RemoveAt(sourceLocations[index]);
                if (movedIndex >= 0)
                    _locations[movedIndex] = new EntityLocation(sourceLocations[index].Chunk, sourceLocations[index].Row);
            }
            for (var i = 0; i < entities.Length; i++) _locations[entities[i].Index] = locations[i];
            return true;
        }

        public bool IsAlive(Entity entity)
        {
            return !_disposed
                   && ReferenceEquals(entity.World, this)
                   && entity.Index >= 0
                   && entity.Index < _entityCount
                   && _versions[entity.Index] == entity.Version;
        }

        /// <summary>Checks a World-local Entity ID against this World.</summary>
        public bool IsAlive(EntityId id)
        {
            return !_disposed
                   && id.Version != 0
                   && id.Index >= 0
                   && id.Index < _entityCount
                   && _versions[id.Index] == id.Version;
        }

        /// <summary>Resolves a World-local Entity ID in this World.</summary>
        public bool TryGetEntity(EntityId id, out Entity entity)
        {
            if (IsAlive(id))
            {
                entity = new Entity(id.Index, id.Version, this);
                return true;
            }

            entity = default;
            return false;
        }

        public void Bind<T>(T externalObject, Entity entity)
        {
            if (!typeof(T).IsValueType && externalObject is null)
                throw new ArgumentNullException(nameof(externalObject));
            ValidateAlive(entity);
            if (typeof(T).IsValueType)
            {
                BindStruct(externalObject, entity);
                return;
            }

            var bindings = _bindings!;
            if (bindings.TryGetValue(externalObject!, out var existing))
            {
                if (existing == entity) return;
                throw new InvalidOperationException("The external object is already bound to another Entity.");
            }

            bindings.Add(externalObject!, entity);
            AddObjectBinding(entity.Index, externalObject!);
        }

        public bool TryGetEntity<T>(T externalObject, out Entity entity)
        {
            ThrowIfDisposed();
            if (typeof(T).IsValueType)
            {
                if (_structBindingStorages != null &&
                    _structBindingStorages.TryGetValue(typeof(T), out var untypedStorage) &&
                    ((StructBindingStorage<T>)untypedStorage).TryGetEntity(externalObject, out entity) &&
                    IsAlive(entity))
                    return true;
                entity = default;
                return false;
            }

            if (externalObject is not null && _bindings != null &&
                _bindings.TryGetValue(externalObject, out entity) && IsAlive(entity))
                return true;
            entity = default;
            return false;
        }

        public Entity GetEntity<T>(T externalObject)
        {
            if (TryGetEntity(externalObject, out var entity)) return entity;
            throw new KeyNotFoundException("The external object is not bound to a live Entity.");
        }

        public bool Unbind<T>(T externalObject)
        {
            ThrowIfDisposed();
            if (typeof(T).IsValueType)
            {
                if (_structBindingStorages == null ||
                    !_structBindingStorages.TryGetValue(typeof(T), out var untypedStorage) ||
                    !((StructBindingStorage<T>)untypedStorage).TryGetEntity(externalObject, out var bound))
                    return false;
                return UnbindStruct(externalObject, bound, (StructBindingStorage<T>)untypedStorage);
            }

            if (externalObject is null || _bindings == null ||
                !_bindings.TryGetValue(externalObject, out var entity)) return false;
            return UnbindCore(externalObject, entity);
        }

        internal bool Unbind<T>(T externalObject, Entity entity)
        {
            ThrowIfDisposed();
            if (typeof(T).IsValueType)
            {
                if (_structBindingStorages == null ||
                    !_structBindingStorages.TryGetValue(typeof(T), out var untypedStorage))
                    return false;
                return UnbindStruct(externalObject, entity, (StructBindingStorage<T>)untypedStorage);
            }

            if (externalObject is null || _bindings == null ||
                !_bindings.TryGetValue(externalObject, out var bound) ||
                bound != entity) return false;
            return UnbindCore(externalObject, entity);
        }

        private bool UnbindCore(object externalObject, Entity entity)
        {
            if (_bindings == null || !_bindings.Remove(externalObject)) return false;
            if (_entityBindingHeads == null || !_entityBindingHeads.TryGetValue(entity.Index, out var node))
                return true;
            var previous = -1;
            while (node >= 0)
            {
                var next = _entityBindingNext![node];
                if (ReferenceEquals(_entityBindingObjects![node], externalObject))
                {
                    if (previous < 0)
                    {
                        if (next < 0) _entityBindingHeads.Remove(entity.Index);
                        else _entityBindingHeads[entity.Index] = next;
                    }
                    else _entityBindingNext[previous] = next;
                    ReleaseObjectBinding(node);
                    return true;
                }
                previous = node;
                node = next;
            }
            return true;
        }

        private void RemoveBindings(Entity entity)
        {
            if (_entityBindingHeads == null || !_entityBindingHeads.TryGetValue(entity.Index, out var node)) return;
            while (node >= 0)
            {
                var next = _entityBindingNext![node];
                _bindings!.Remove(_entityBindingObjects![node]!);
                ReleaseObjectBinding(node);
                node = next;
            }
            _entityBindingHeads.Remove(entity.Index);
        }

        private void AddObjectBinding(int entityIndex, object externalObject)
        {
            int node;
            if (_freeEntityBinding >= 0)
            {
                node = _freeEntityBinding;
                _freeEntityBinding = _entityBindingNext![node];
            }
            else
            {
                node = _entityBindingCount++;
                if (node == _entityBindingObjects!.Length)
                {
                    var newSize = _entityBindingObjects.Length * 2;
                    Array.Resize(ref _entityBindingObjects, newSize);
                    Array.Resize(ref _entityBindingNext, newSize);
                }
            }
            var hasHead = _entityBindingHeads!.TryGetValue(entityIndex, out var head);
            _entityBindingObjects![node] = externalObject;
            _entityBindingNext![node] = hasHead ? head : -1;
            _entityBindingHeads[entityIndex] = node;
        }

        private void ReleaseObjectBinding(int node)
        {
            _entityBindingObjects![node] = null;
            _entityBindingNext![node] = _freeEntityBinding;
            _freeEntityBinding = node;
        }

        private void BindStruct<T>(T key, Entity entity)
        {
            var storages = _structBindingStorages ??= new Dictionary<Type, IStructBindingStorage>();
            if (!storages.TryGetValue(typeof(T), out var untypedStorage))
            {
                untypedStorage = new StructBindingStorage<T>(Math.Max(1, _defaultCapacity));
                storages.Add(typeof(T), untypedStorage);
            }

            var storage = (StructBindingStorage<T>)untypedStorage;
            storage.Bind(key, entity);
        }

        private bool UnbindStruct<T>(T key, Entity entity, StructBindingStorage<T> storage)
        {
            return storage.Unbind(key, entity, out _);
        }

        private void RemoveStructBindings(Entity entity)
        {
            if (_structBindingStorages == null) return;
            foreach (var storage in _structBindingStorages.Values) storage.RemoveEntity(entity.Index);
        }

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        private int GetBindingCount()
        {
            var count = _bindings?.Count ?? 0;
            if (_structBindingStorages == null) return count;
            foreach (var storage in _structBindingStorages.Values) count += storage.Count;
            return count;
        }
#endif

        public void Despawn(Entity entity)
        {
            FlushStructuralBatch();
            ThrowIfDisposed();
            ThrowIfParallelQueryActive();
            if (!IsAlive(entity)) return;
            StructuralVersion++;

            if (CanDespawnFast())
            {
                DespawnFast(entity.Index);
                return;
            }

            DespawnCore(entity);
        }

        public void DespawnBatch(ReadOnlySpan<Entity> entities)
        {
            FlushStructuralBatch();
            ThrowIfDisposed();
            ThrowIfParallelQueryActive();
            if (CanDespawnFast())
            {
                DespawnBatchFast(entities);
                return;
            }

            var changed = false;
            for (var i = 0; i < entities.Length; i++)
            {
                if (!IsAlive(entities[i])) continue;
                DespawnCore(entities[i]);
                changed = true;
            }

            if (changed) StructuralVersion++;
        }

        private void DespawnBatchFast(ReadOnlySpan<Entity> entities)
        {
            if (entities.Length > 1 && TryClearArchetypeBatch(entities))
            {
                StructuralVersion++;
                return;
            }

            var changed = false;
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!IsAlive(entity)) continue;
                DespawnFast(entity.Index);
                changed = true;
            }

            if (changed) StructuralVersion++;
        }

        private bool TryClearArchetypeBatch(ReadOnlySpan<Entity> entities)
        {
            var first = entities[0];
            if (!IsAlive(first)) return false;
            var sourceLocation = _locations[first.Index];
            if (!sourceLocation.IsValid) return false;
            var source = sourceLocation.Archetype;
            if (source.EntityCount != entities.Length) return false;
            for (var i = 0; i < entities.Length; i++)
            {
                if (!IsAlive(entities[i]) || !ReferenceEquals(_locations[entities[i].Index].Archetype, source))
                    return false;
            }

            source.ClearAll();
            for (var i = 0; i < entities.Length; i++)
            {
                var index = entities[i].Index;
                _locations[index] = default;
                _versions[index]++;
                _freeIndices.Push(index);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanDespawnFast() =>
            (_bindings?.Count ?? 0) == 0 && _structBindingStorages == null && _relationStorages == null && _collectors == null
            && _singletonTypeMask.IsEmpty && _incrementalQueryPlans == null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DespawnFast(int index)
        {
            RemoveFromArchetype(index);
            _versions[index]++;
            _freeIndices.Push(index);
        }

        private void DespawnCore(Entity entity)
        {
            var index = entity.Index;
            if (_entityBindingHeads != null && _entityBindingHeads.ContainsKey(entity.Index)) RemoveBindings(entity);
            if (_structBindingStorages != null)
                RemoveStructBindings(entity);
            var location = _locations[index];
            var componentTypeIds = location.IsValid ? location.Archetype.TypeIds : Array.Empty<int>();
            var forwardRelations = _relationForwardMasks?[index] ?? default;
            var backwardRelations = _relationBackwardMasks?[index] ?? default;
            var registry = _collectors;
            if (registry != null)
                PublishRemovedComponents(entity, componentTypeIds, registry.ObservedTypes);
            RemoveFromArchetype(index);
            _versions[index]++;
            if (_relationForwardMasks != null) _relationForwardMasks[index] = default;
            if (_relationBackwardMasks != null) _relationBackwardMasks[index] = default;

            RemoveComponents(index, componentTypeIds);
            ClearSingletons(entity, componentTypeIds);
            RemoveRelations(entity, forwardRelations, true);
            RemoveRelations(entity, backwardRelations, false);

            _freeIndices.Push(index);
        }

        private void PublishRemovedComponents(in Entity entity, int[] typeIds, in ComponentMask observedTypes)
        {
            for (var i = 0; i < typeIds.Length; i++)
                if (observedTypes.Has(typeIds[i]))
                    PublishComponentEvent(typeIds[i], entity, ComponentEvent.KeyRemoved);
        }

        private void RemoveComponents(int index, int[] typeIds)
        {
            var notifyPlans = _incrementalQueryPlans != null;
            for (var i = 0; i < typeIds.Length; i++)
            {
                var typeId = typeIds[i];
                if (notifyPlans) NotifyFilterComponentChanged(typeId, index);
            }
        }

        private void ClearSingletons(in Entity entity, int[] typeIds)
        {
            for (var i = 0; i < typeIds.Length; i++)
            {
                var typeId = typeIds[i];
                if (_singletonTypeMask.Has(typeId) && _singletonEntities != null
                    && _singletonEntities[typeId] == entity)
                    _singletonEntities[typeId] = default;
            }
        }

        private void RemoveRelations(Entity entity, ComponentMask mask, bool forward)
        {
            RemoveRelationWord(entity, mask.B0, 0, forward);
            RemoveRelationWord(entity, mask.B1, 64, forward);
            RemoveRelationWord(entity, mask.B2, 128, forward);
            RemoveRelationWord(entity, mask.B3, 192, forward);
            for (var i = 0; i < mask.OverflowWordCount; i++)
                RemoveRelationWord(entity, mask.GetOverflowWord(i), 256 + i * 64, forward);
        }

        private void RemoveRelationWord(Entity entity, ulong word, int offset, bool forward)
        {
            while (word != 0)
            {
                var bit = TrailingZeroCount(word);
                if (_relationStorages != null && _relationStorages.TryGetValue(offset + bit, out var storage))
                {
                    if (forward) storage.RemoveAll(entity);
                    else storage.RemoveAllTarget(entity);
                }

                word &= word - 1;
            }
        }

        internal EntityQueryResult GetEntityQueryResult(in ComponentMask requiredMask,
            in ComponentMask excludedMask, in ComponentMask anyMask)
        {
            ThrowIfDisposed();
            var plans = _entityQueryPlans ??= new List<EntityQueryPlan>();
            EntityQueryPlan? plan = null;
            for (var i = 0; i < plans.Count; i++)
            {
                if (!plans[i].HasFilter(requiredMask, excludedMask, anyMask)) continue;
                plan = plans[i];
                break;
            }

            if (plan == null)
            {
                plan = new EntityQueryPlan(this, requiredMask, excludedMask, anyMask);
                plans.Add(plan);
            }

            plan.Ensure();
            return new EntityQueryResult(this, plan, plan.Generation, plan.Count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int TrailingZeroCount(ulong value)
        {
            var count = 0;
            while ((value & 1UL) == 0)
            {
                count++;
                value >>= 1;
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddComponent<T>(Entity entity, T component) where T : struct
        {
            ValidateAlive(entity);
            EnsureSingletonAvailable<T>(entity);
            if (_structuralBatchDepth > 0)
            {
                ReservePendingSingleton<T>(entity);
                CommandBuffer.AddComponent(entity, component);
                return;
            }
            var typeId = ComponentType<T>.Id;
            var location = _locations[entity.Index];
            var isNew = !location.IsValid || !location.Archetype.Has(typeId);
            if (isNew) ThrowIfParallelQueryActive();
            EnsureComponentTypeCapacity(typeId);
            if (isNew)
            {
                var destination = location.IsValid
                    ? _archetypes.With(location.Archetype, typeId)
                    : _archetypes.With(_archetypes.Empty, typeId);
                MoveEntityTo(entity, destination);
                StructuralVersion++;
                _componentVersions[typeId]++;
                NotifyFilterComponentChanged(typeId, entity.Index);
            }

            _locations[entity.Index].Archetype.Get<T>(_locations[entity.Index]) = component;

            if (SingletonType<T>.IsSingleton)
            {
                (_singletonEntities ??= new Entity[_componentVersions.Length])[typeId] = entity;
                _singletonTypeMask.Set(typeId);
            }
            PublishComponentEvent(typeId, entity, isNew ? ComponentEvent.KeyAdded : ComponentEvent.KeyChanged);
        }

        public void AddComponents<T1, T2>(Entity entity, T1 component1, T2 component2)
            where T1 : struct where T2 : struct
        {
            ValidateAlive(entity);
            EnsureSingletonAvailable<T1>(entity);
            EnsureSingletonAvailable<T2>(entity);
            if (_structuralBatchDepth > 0)
            {
                ReservePendingSingleton<T1>(entity);
                ReservePendingSingleton<T2>(entity);
                CommandBuffer.AddComponent(entity, component1, component2);
                return;
            }
            Span<int> typeIds = stackalloc int[2] { ComponentType<T1>.Id, ComponentType<T2>.Id };
            var source = MoveForAddedComponents(entity, typeIds);
            SetAddedComponent(entity, component1, !source.Has(typeIds[0]));
            SetAddedComponent(entity, component2, !source.Has(typeIds[1]));
        }

        public void AddComponents<T1, T2, T3>(Entity entity, T1 component1, T2 component2, T3 component3)
            where T1 : struct where T2 : struct where T3 : struct
        {
            ValidateAlive(entity);
            EnsureSingletonAvailable<T1>(entity);
            EnsureSingletonAvailable<T2>(entity);
            EnsureSingletonAvailable<T3>(entity);
            if (_structuralBatchDepth > 0)
            {
                ReservePendingSingleton<T1>(entity);
                ReservePendingSingleton<T2>(entity);
                ReservePendingSingleton<T3>(entity);
                CommandBuffer.AddComponent(entity, component1, component2, component3);
                return;
            }
            Span<int> typeIds = stackalloc int[3]
                { ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id };
            var source = MoveForAddedComponents(entity, typeIds);
            SetAddedComponent(entity, component1, !source.Has(typeIds[0]));
            SetAddedComponent(entity, component2, !source.Has(typeIds[1]));
            SetAddedComponent(entity, component3, !source.Has(typeIds[2]));
        }

        public void AddComponents<T1, T2, T3, T4>(Entity entity, T1 component1, T2 component2,
            T3 component3, T4 component4)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            ValidateAlive(entity);
            EnsureSingletonAvailable<T1>(entity);
            EnsureSingletonAvailable<T2>(entity);
            EnsureSingletonAvailable<T3>(entity);
            EnsureSingletonAvailable<T4>(entity);
            if (_structuralBatchDepth > 0)
            {
                ReservePendingSingleton<T1>(entity);
                ReservePendingSingleton<T2>(entity);
                ReservePendingSingleton<T3>(entity);
                ReservePendingSingleton<T4>(entity);
                CommandBuffer.AddComponent(entity, component1, component2, component3, component4);
                return;
            }
            Span<int> typeIds = stackalloc int[4]
                { ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id };
            var source = MoveForAddedComponents(entity, typeIds);
            SetAddedComponent(entity, component1, !source.Has(typeIds[0]));
            SetAddedComponent(entity, component2, !source.Has(typeIds[1]));
            SetAddedComponent(entity, component3, !source.Has(typeIds[2]));
            SetAddedComponent(entity, component4, !source.Has(typeIds[3]));
        }

        public void AddComponents<T1, T2, T3, T4, T5>(Entity entity, T1 component1, T2 component2,
            T3 component3, T4 component4, T5 component5)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            ValidateAlive(entity);
            EnsureSingletonAvailable<T1>(entity);
            EnsureSingletonAvailable<T2>(entity);
            EnsureSingletonAvailable<T3>(entity);
            EnsureSingletonAvailable<T4>(entity);
            EnsureSingletonAvailable<T5>(entity);
            if (_structuralBatchDepth > 0)
            {
                ReservePendingSingleton<T1>(entity);
                ReservePendingSingleton<T2>(entity);
                ReservePendingSingleton<T3>(entity);
                ReservePendingSingleton<T4>(entity);
                ReservePendingSingleton<T5>(entity);
                CommandBuffer.AddComponent(entity, component1);
                CommandBuffer.AddComponent(entity, component2);
                CommandBuffer.AddComponent(entity, component3);
                CommandBuffer.AddComponent(entity, component4);
                CommandBuffer.AddComponent(entity, component5);
                return;
            }
            if (CanMutateComponentsFast()
                && !SingletonType<T1>.IsSingleton && !SingletonType<T2>.IsSingleton
                && !SingletonType<T3>.IsSingleton && !SingletonType<T4>.IsSingleton
                && !SingletonType<T5>.IsSingleton)
            {
                AddComponentsFast(entity, component1, component2, component3, component4, component5);
                return;
            }

            Span<int> typeIds = stackalloc int[5]
            {
                ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id,
                ComponentType<T4>.Id, ComponentType<T5>.Id
            };
            var source = MoveForAddedComponents(entity, typeIds);
            SetAddedComponent(entity, component1, !source.Has(typeIds[0]));
            SetAddedComponent(entity, component2, !source.Has(typeIds[1]));
            SetAddedComponent(entity, component3, !source.Has(typeIds[2]));
            SetAddedComponent(entity, component4, !source.Has(typeIds[3]));
            SetAddedComponent(entity, component5, !source.Has(typeIds[4]));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanMutateComponentsFast() =>
            _collectors == null && _incrementalQueryPlans == null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddComponentsFast<T1, T2, T3, T4, T5>(Entity entity, in T1 component1, in T2 component2,
            in T3 component3, in T4 component4, in T5 component5)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            ValidateAlive(entity);
            var id1 = ComponentType<T1>.Id;
            var id2 = ComponentType<T2>.Id;
            var id3 = ComponentType<T3>.Id;
            var id4 = ComponentType<T4>.Id;
            var id5 = ComponentType<T5>.Id;
            var maxTypeId = id1;
            if (id2 > maxTypeId) maxTypeId = id2;
            if (id3 > maxTypeId) maxTypeId = id3;
            if (id4 > maxTypeId) maxTypeId = id4;
            if (id5 > maxTypeId) maxTypeId = id5;
            EnsureComponentTypeCapacity(maxTypeId);

            var index = entity.Index;
            var location = _locations[index];
            var source = location.IsValid ? location.Archetype : _archetypes.Empty;
            Span<int> typeIds = stackalloc int[5] { id1, id2, id3, id4, id5 };
            var destination = _archetypes.WithMany(source, typeIds);

            var isNew1 = !source.Has(id1);
            var isNew2 = !source.Has(id2);
            var isNew3 = !source.Has(id3);
            var isNew4 = !source.Has(id4);
            var isNew5 = !source.Has(id5);
            if (!location.IsValid || !ReferenceEquals(source, destination))
            {
                ThrowIfParallelQueryActive();
                MoveEntityTo(entity, destination);
                StructuralVersion++;
            }

            location = _locations[index];
            var archetype = location.Archetype;
            var columns = location.Chunk.Columns;
            var row = location.Row;
            ((ArchetypeColumn<T1>)columns[archetype.GetColumnIndex(id1)]).Values[row] = component1;
            ((ArchetypeColumn<T2>)columns[archetype.GetColumnIndex(id2)]).Values[row] = component2;
            ((ArchetypeColumn<T3>)columns[archetype.GetColumnIndex(id3)]).Values[row] = component3;
            ((ArchetypeColumn<T4>)columns[archetype.GetColumnIndex(id4)]).Values[row] = component4;
            ((ArchetypeColumn<T5>)columns[archetype.GetColumnIndex(id5)]).Values[row] = component5;

            if (isNew1) _componentVersions[id1]++;
            if (isNew2) _componentVersions[id2]++;
            if (isNew3) _componentVersions[id3]++;
            if (isNew4) _componentVersions[id4]++;
            if (isNew5) _componentVersions[id5]++;
        }

        internal Archetype MoveForAddedComponents(in Entity entity, ReadOnlySpan<int> typeIds)
        {
            ValidateAlive(entity);
            var sourceLocation = _locations[entity.Index];
            var source = sourceLocation.IsValid ? sourceLocation.Archetype : _archetypes.Empty;
            var maxTypeId = -1;
            for (var i = 0; i < typeIds.Length; i++)
                if (typeIds[i] > maxTypeId) maxTypeId = typeIds[i];
            if (maxTypeId >= 0) EnsureComponentTypeCapacity(maxTypeId);
            var destination = _archetypes.WithMany(source, typeIds);
            if (!sourceLocation.IsValid || !ReferenceEquals(source, destination))
            {
                ThrowIfParallelQueryActive();
                MoveEntityTo(entity, destination);
                StructuralVersion++;
            }
            return source;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetAddedComponent<T>(in Entity entity, in T component, bool isNew) where T : struct
        {
            var typeId = ComponentType<T>.Id;
            _locations[entity.Index].Archetype.Get<T>(_locations[entity.Index]) = component;
            if (isNew)
            {
                _componentVersions[typeId]++;
                NotifyFilterComponentChanged(typeId, entity.Index);
            }
            if (SingletonType<T>.IsSingleton)
            {
                (_singletonEntities ??= new Entity[_componentVersions.Length])[typeId] = entity;
                _singletonTypeMask.Set(typeId);
            }
            PublishComponentEvent(typeId, entity, isNew ? ComponentEvent.KeyAdded : ComponentEvent.KeyChanged);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(Entity entity) where T : struct
        {
            FlushStructuralBatch();
            ValidateAlive(entity);
            var location = _locations[entity.Index];
            return ref location.Archetype.Get<T>(location);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityData GetData(Entity entity)
        {
            FlushStructuralBatch();
            ValidateAlive(entity);
            return new EntityData(_locations[entity.Index]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetComponent<T>(Entity entity, out T component) where T : struct
        {
            FlushStructuralBatch();
            if (!IsAlive(entity))
            {
                component = default;
                return false;
            }

            var typeId = ComponentType<T>.Id;
            var location = _locations[entity.Index];
            if (!location.IsValid || !location.Archetype.Has(typeId))
            {
                component = default;
                return false;
            }

            component = location.Archetype.Get<T>(location);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetComponentRef<T>(Entity entity, out Ref<T> component) where T : struct
        {
            FlushStructuralBatch();
            if (!IsAlive(entity))
            {
                component = default;
                return false;
            }

            var typeId = ComponentType<T>.Id;
            var location = _locations[entity.Index];
            if (!location.IsValid || !location.Archetype.Has(typeId))
            {
                component = default;
                return false;
            }

            var dense = location.Archetype.GetColumn<T>(location.Chunk);
            var denseIndex = location.Row;

#if _INTERNAL_DERIVED_USE_VALIDATION
            component = new Ref<T>(dense, denseIndex, entity, _componentVersions[typeId]);
#else
            component = new Ref<T>(dense, denseIndex);
#endif
            return true;
        }

#if _INTERNAL_DERIVED_USE_VALIDATION
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ValidateComponentReference<T>(in Entity entity, int expectedVersion) where T : struct
        {
            var typeId = ComponentType<T>.Id;
            if (!IsAlive(entity) || _componentVersions[typeId] != expectedVersion ||
                !_locations[entity.Index].IsValid ||
                !_locations[entity.Index].Archetype.Has(typeId))
                throw new InvalidOperationException(
                    $"The {typeof(T).Name} reference was invalidated by a structural change. " +
                    "Acquire the reference again with TryGetRef<T>().");
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TClass GetManagedComponent<TClass>(Entity entity) where TClass : class
        {
            FlushStructuralBatch();
            ValidateAlive(entity);
            var location = _locations[entity.Index];
            return location.Archetype.Get<Link<TClass>>(location).Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent<T>(Entity entity)
        {
            FlushStructuralBatch();
            if (!IsAlive(entity)) return false;
            var location = _locations[entity.Index];
            return location.IsValid && location.Archetype.Has(ComponentType<T>.Id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool Matches(in Entity entity, in ComponentMask required, in ComponentMask excluded,
            in ComponentMask any)
        {
            if (!IsAlive(entity)) return false;
            var location = _locations[entity.Index];
            if (!location.IsValid)
                return required.IsEmpty && any.IsEmpty;
            var archetype = location.Archetype;
            return ArchetypeContainsAll(archetype, required)
                   && ArchetypeContainsNone(archetype, excluded)
                   && (any.IsEmpty || ArchetypeIntersects(archetype, any));
        }

        private static bool ArchetypeContainsAll(Archetype archetype, in ComponentMask mask)
        {
            return ArchetypeContainsAllWord(archetype, mask.B0, 0)
                   && ArchetypeContainsAllWord(archetype, mask.B1, 64)
                   && ArchetypeContainsAllWord(archetype, mask.B2, 128)
                   && ArchetypeContainsAllWord(archetype, mask.B3, 192)
                   && ArchetypeContainsAllOverflow(archetype, mask);
        }

        private static bool ArchetypeContainsAllOverflow(Archetype archetype, in ComponentMask mask)
        {
            for (var i = 0; i < mask.OverflowWordCount; i++)
                if (!ArchetypeContainsAllWord(archetype, mask.GetOverflowWord(i), 256 + i * 64)) return false;
            return true;
        }

        private static bool ArchetypeContainsAllWord(Archetype archetype, ulong word, int offset)
        {
            while (word != 0)
            {
                var bit = TrailingZeroCount(word);
                if (!archetype.Has(offset + bit)) return false;
                word &= word - 1;
            }
            return true;
        }

        private static bool ArchetypeContainsNone(Archetype archetype, in ComponentMask mask) =>
            !ArchetypeIntersects(archetype, mask);

        private static bool ArchetypeIntersects(Archetype archetype, in ComponentMask mask)
        {
            if (ArchetypeIntersectsWord(archetype, mask.B0, 0)
                || ArchetypeIntersectsWord(archetype, mask.B1, 64)
                || ArchetypeIntersectsWord(archetype, mask.B2, 128)
                || ArchetypeIntersectsWord(archetype, mask.B3, 192)) return true;
            for (var i = 0; i < mask.OverflowWordCount; i++)
                if (ArchetypeIntersectsWord(archetype, mask.GetOverflowWord(i), 256 + i * 64)) return true;
            return false;
        }

        private static bool ArchetypeIntersectsWord(Archetype archetype, ulong word, int offset)
        {
            while (word != 0)
            {
                var bit = TrailingZeroCount(word);
                if (archetype.Has(offset + bit)) return true;
                word &= word - 1;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RemoveComponent<T>(Entity entity)
        {
            FlushStructuralBatch();
            ThrowIfDisposed();
            if (!IsAlive(entity)) return false;
            var typeId = ComponentType<T>.Id;
            var location = _locations[entity.Index];
            if (!location.IsValid || !location.Archetype.Has(typeId)) return false;
            ThrowIfParallelQueryActive();

            MoveEntityTo(entity, _archetypes.Without(location.Archetype, typeId));
            _componentVersions[typeId]++;
            if (SingletonType<T>.IsSingleton && _singletonEntities != null && _singletonEntities[typeId] == entity)
                _singletonEntities[typeId] = default;
            StructuralVersion++;
            NotifyFilterComponentChanged(typeId, entity.Index);
            PublishComponentEvent(typeId, entity, ComponentEvent.KeyRemoved);
            return true;
        }

        public bool RemoveComponents<T1, T2>(Entity entity) where T1 : struct where T2 : struct
        {
            FlushStructuralBatch();
            Span<int> typeIds = stackalloc int[2] { ComponentType<T1>.Id, ComponentType<T2>.Id };
            var source = MoveForRemovedComponents(entity, typeIds, out var changed);
            CompleteRemovedComponent<T1>(entity, source.Has(typeIds[0]));
            CompleteRemovedComponent<T2>(entity, typeIds[1] != typeIds[0] && source.Has(typeIds[1]));
            return changed;
        }

        public bool RemoveComponents<T1, T2, T3>(Entity entity)
            where T1 : struct where T2 : struct where T3 : struct
        {
            FlushStructuralBatch();
            Span<int> typeIds = stackalloc int[3]
                { ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id };
            var source = MoveForRemovedComponents(entity, typeIds, out var changed);
            CompleteRemovedComponent<T1>(entity, source.Has(typeIds[0]));
            CompleteRemovedComponent<T2>(entity, typeIds[1] != typeIds[0] && source.Has(typeIds[1]));
            CompleteRemovedComponent<T3>(entity, typeIds[2] != typeIds[0] && typeIds[2] != typeIds[1] && source.Has(typeIds[2]));
            return changed;
        }

        public bool RemoveComponents<T1, T2, T3, T4>(Entity entity)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            FlushStructuralBatch();
            Span<int> typeIds = stackalloc int[4]
                { ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id };
            var source = MoveForRemovedComponents(entity, typeIds, out var changed);
            CompleteRemovedComponent<T1>(entity, source.Has(typeIds[0]));
            CompleteRemovedComponent<T2>(entity, typeIds[1] != typeIds[0] && source.Has(typeIds[1]));
            CompleteRemovedComponent<T3>(entity, typeIds[2] != typeIds[0] && typeIds[2] != typeIds[1] && source.Has(typeIds[2]));
            CompleteRemovedComponent<T4>(entity, typeIds[3] != typeIds[0] && typeIds[3] != typeIds[1] && typeIds[3] != typeIds[2] && source.Has(typeIds[3]));
            return changed;
        }

        public bool RemoveComponents<T1, T2, T3, T4, T5>(Entity entity)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            FlushStructuralBatch();
            if (CanMutateComponentsFast()
                && !SingletonType<T1>.IsSingleton && !SingletonType<T2>.IsSingleton
                && !SingletonType<T3>.IsSingleton && !SingletonType<T4>.IsSingleton
                && !SingletonType<T5>.IsSingleton)
                return RemoveComponentsFast<T1, T2, T3, T4, T5>(entity);

            Span<int> typeIds = stackalloc int[5]
            {
                ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id,
                ComponentType<T4>.Id, ComponentType<T5>.Id
            };
            var source = MoveForRemovedComponents(entity, typeIds, out var changed);
            CompleteRemovedComponent<T1>(entity, source.Has(typeIds[0]));
            CompleteRemovedComponent<T2>(entity, typeIds[1] != typeIds[0] && source.Has(typeIds[1]));
            CompleteRemovedComponent<T3>(entity, typeIds[2] != typeIds[0] && typeIds[2] != typeIds[1] && source.Has(typeIds[2]));
            CompleteRemovedComponent<T4>(entity, typeIds[3] != typeIds[0] && typeIds[3] != typeIds[1] && typeIds[3] != typeIds[2] && source.Has(typeIds[3]));
            CompleteRemovedComponent<T5>(entity, typeIds[4] != typeIds[0] && typeIds[4] != typeIds[1] && typeIds[4] != typeIds[2] && typeIds[4] != typeIds[3] && source.Has(typeIds[4]));
            return changed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool RemoveComponentsFast<T1, T2, T3, T4, T5>(Entity entity)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            ValidateAlive(entity);
            var location = _locations[entity.Index];
            if (!location.IsValid) return false;

            var source = location.Archetype;
            var id1 = ComponentType<T1>.Id;
            var id2 = ComponentType<T2>.Id;
            var id3 = ComponentType<T3>.Id;
            var id4 = ComponentType<T4>.Id;
            var id5 = ComponentType<T5>.Id;
            var has1 = source.Has(id1);
            var has2 = source.Has(id2);
            var has3 = source.Has(id3);
            var has4 = source.Has(id4);
            var has5 = source.Has(id5);
            Span<int> typeIds = stackalloc int[5] { id1, id2, id3, id4, id5 };
            var presentCount = 0;
            for (var i = 0; i < typeIds.Length; i++)
            {
                if (!source.Has(typeIds[i])) continue;
                var duplicate = false;
                for (var previous = 0; previous < i; previous++)
                    if (typeIds[previous] == typeIds[i]) { duplicate = true; break; }
                if (!duplicate) presentCount++;
            }
            if (presentCount == 0) return false;

            ThrowIfParallelQueryActive();
            if (presentCount == source.TypeIds.Length)
            {
                var movedIndex = source.RemoveAt(location);
                _locations[entity.Index] = default;
                if (movedIndex >= 0)
                    _locations[movedIndex] = new EntityLocation(location.Chunk, location.Row);
            }
            else
            {
                var destination = _archetypes.WithoutMany(source, typeIds);
                MoveEntityTo(entity, destination);
            }

            StructuralVersion++;
            if (has1) _componentVersions[id1]++;
            if (has2 && id2 != id1) _componentVersions[id2]++;
            if (has3 && id3 != id1 && id3 != id2) _componentVersions[id3]++;
            if (has4 && id4 != id1 && id4 != id2 && id4 != id3) _componentVersions[id4]++;
            if (has5 && id5 != id1 && id5 != id2 && id5 != id3 && id5 != id4) _componentVersions[id5]++;
            return true;
        }

        internal Archetype MoveForRemovedComponents(in Entity entity, ReadOnlySpan<int> typeIds, out bool changed)
        {
            ValidateAlive(entity);
            var location = _locations[entity.Index];
            if (!location.IsValid)
            {
                changed = false;
                return _archetypes.Empty;
            }

            var source = location.Archetype;
            var presentCount = 0;
            for (var i = 0; i < typeIds.Length; i++)
            {
                if (!source.Has(typeIds[i])) continue;
                var duplicate = false;
                for (var previous = 0; previous < i; previous++)
                    if (typeIds[previous] == typeIds[i]) { duplicate = true; break; }
                if (!duplicate) presentCount++;
            }
            if (presentCount == 0)
            {
                changed = false;
                return source;
            }

            // Removing every component on the entity: detach without routing through Empty.
            if (presentCount == source.TypeIds.Length)
            {
                ThrowIfParallelQueryActive();
                var movedIndex = source.RemoveAt(location);
                _locations[entity.Index] = default;
                if (movedIndex >= 0)
                    _locations[movedIndex] = new EntityLocation(location.Chunk, location.Row);
                StructuralVersion++;
                changed = true;
                return source;
            }

            var destination = _archetypes.WithoutMany(source, typeIds);
            changed = !ReferenceEquals(source, destination);
            if (changed)
            {
                ThrowIfParallelQueryActive();
                MoveEntityTo(entity, destination);
                StructuralVersion++;
            }
            return source;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CompleteRemovedComponent<T>(in Entity entity, bool removed) where T : struct
        {
            if (!removed) return;
            var typeId = ComponentType<T>.Id;
            _componentVersions[typeId]++;
            if (SingletonType<T>.IsSingleton && _singletonEntities != null && _singletonEntities[typeId] == entity)
                _singletonEntities[typeId] = default;
            NotifyFilterComponentChanged(typeId, entity.Index);
            PublishComponentEvent(typeId, entity, ComponentEvent.KeyRemoved);
        }

        public Entity Singleton<T>() where T : struct, ISingleton
        {
            ThrowIfDisposed();
            var typeId = ComponentType<T>.Id;
            var entity = _singletonEntities != null && typeId < _singletonEntities.Length
                ? _singletonEntities[typeId]
                : default;
            if (IsAlive(entity) && HasComponent<T>(entity)) return entity;
            throw new InvalidOperationException($"Singleton {typeof(T).Name} does not exist in this World.");
        }

        public bool TryGetSingleton<T>(out Entity entity) where T : struct, ISingleton
        {
            ThrowIfDisposed();
            var typeId = ComponentType<T>.Id;
            entity = _singletonEntities != null && typeId < _singletonEntities.Length
                ? _singletonEntities[typeId]
                : default;
            if (IsAlive(entity) && HasComponent<T>(entity)) return true;
            entity = default;
            return false;
        }

        public bool HasSingleton<T>() where T : struct, ISingleton => TryGetSingleton<T>(out _);

        private void EnsureSingletonAvailable<T>(Entity entity)
        {
            if (!SingletonType<T>.IsSingleton) return;
            var typeId = ComponentType<T>.Id;
            EnsureComponentTypeCapacity(typeId);
            var existing = _singletonEntities?[typeId] ?? default;
            if (IsAlive(existing) && existing != entity)
                throw new InvalidOperationException(
                    $"Singleton {typeof(T).Name} already exists on Entity {existing.Index}.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReservePendingSingleton<T>(Entity entity) where T : struct
        {
            if (!SingletonType<T>.IsSingleton) return;
            var typeId = ComponentType<T>.Id;
            (_singletonEntities ??= new Entity[_componentVersions.Length])[typeId] = entity;
            _singletonTypeMask.Set(typeId);
        }

        private void ValidateSingletonBatch<T>(ReadOnlySpan<Entity> entities)
        {
            if (!SingletonType<T>.IsSingleton) return;
            if (entities.Length > 1)
                throw new InvalidOperationException(
                    $"Singleton component {typeof(T).Name} cannot be added to multiple Entities.");
            if (entities.Length == 1)
            {
                ValidateAlive(entities[0]);
                EnsureSingletonAvailable<T>(entities[0]);
            }
        }

        public void AddRelation<TRelation>(Entity source, Entity target) where TRelation : struct
        {
            ValidateAlive(source);
            ValidateAlive(target);
            var typeId = ComponentType<TRelation>.Id;
            EnsureRelationMasks();
            var relationStorages = _relationStorages ??= new Dictionary<int, RelationStorage>();
            if (!relationStorages.TryGetValue(typeId, out var relStorage))
            {
                relStorage = new RelationStorage();
                relationStorages[typeId] = relStorage;
            }

            var existed = relStorage.Contains(source, target);
            relStorage.Add(source, target);
            _relationForwardMasks![source.Index].Set(typeId);
            _relationBackwardMasks![target.Index].Set(typeId);
            if (!existed) StructuralVersion++;
        }

        /// <summary>
        /// Replaces all outgoing relations of the specified type with one target.
        /// Passing a default Entity clears the outgoing relations.
        /// </summary>
        public void SetRelation<TRelation>(Entity source, Entity target) where TRelation : struct
        {
            ValidateAlive(source);

            // A default Entity is the explicit "no target" value used by this API.
            // Non-default entities are still validated below, including world identity
            // and generation, so stale or foreign entities cannot be linked accidentally.
            if (target.World == null)
            {
                RemoveRelation<TRelation>(source);
                return;
            }

            ValidateAlive(target);
            var relations = GetRelations<TRelation>(source);
            if (relations.Length == 1 && relations[0] == target) return;

            RemoveRelation<TRelation>(source);
            AddRelation<TRelation>(source, target);
        }

        public ReadOnlySpan<Entity> GetRelations<TRelation>(Entity source) where TRelation : struct
        {
            ValidateAlive(source);
            var typeId = ComponentType<TRelation>.Id;
            if (_relationStorages != null && _relationStorages.TryGetValue(typeId, out var relStorage))
            {
                return relStorage.GetForward(source);
            }

            return ReadOnlySpan<Entity>.Empty;
        }

        public Entity GetRelation<TRelation>(Entity source) where TRelation : struct
        {
            var relations = GetRelations<TRelation>(source);
            if (relations.Length == 1) return relations[0];

            throw new InvalidOperationException(relations.Length == 0
                ? $"Entity has no {typeof(TRelation).Name} relation."
                : $"Entity has multiple {typeof(TRelation).Name} relations. Use GetRelations<{typeof(TRelation).Name}>() instead.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetRelation<TRelation>(Entity source, out Entity target) where TRelation : struct
        {
            if (!IsAlive(source))
            {
                target = default;
                return false;
            }

            var typeId = ComponentType<TRelation>.Id;
            if (_relationStorages != null && _relationStorages.TryGetValue(typeId, out var relationStorage))
            {
                var relations = relationStorage.GetForward(source);
                if (relations.Length == 1)
                {
                    target = relations[0];
                    return true;
                }
            }

            target = default;
            return false;
        }

        public ReadOnlySpan<Entity> GetEntitiesWithTarget<TRelation>(Entity target) where TRelation : struct
        {
            ValidateAlive(target);
            var typeId = ComponentType<TRelation>.Id;
            if (_relationStorages != null && _relationStorages.TryGetValue(typeId, out var relStorage))
            {
                return relStorage.GetBackward(target);
            }

            return ReadOnlySpan<Entity>.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasRelation<TRelation>(Entity source, Entity target) where TRelation : struct
        {
            if (!IsAlive(source) || !IsAlive(target)) return false;
            var typeId = ComponentType<TRelation>.Id;
            return _relationStorages != null && _relationStorages.TryGetValue(typeId, out var relStorage) &&
                   relStorage.Contains(source, target);
        }

        public bool RemoveRelation<TRelation>(Entity source, Entity target) where TRelation : struct
        {
            if (!IsAlive(source) || !IsAlive(target)) return false;
            var typeId = ComponentType<TRelation>.Id;
            if (_relationStorages != null && _relationStorages.TryGetValue(typeId, out var relStorage))
            {
                var removed = relStorage.Remove(source, target);
                if (removed && !relStorage.HasForward(source.Index)) _relationForwardMasks![source.Index].Unset(typeId);
                if (removed && !relStorage.HasBackward(target.Index))
                    _relationBackwardMasks![target.Index].Unset(typeId);
                if (removed) StructuralVersion++;
                return removed;
            }

            return false;
        }

        public bool RemoveRelation<TRelation>(Entity source) where TRelation : struct
        {
            if (!IsAlive(source)) return false;
            var typeId = ComponentType<TRelation>.Id;
            if (_relationStorages == null || !_relationStorages.TryGetValue(typeId, out var relStorage))
                return false;

            var targets = relStorage.GetForward(source);
            if (targets.Length == 0) return false;

            relStorage.RemoveAll(source);
            _relationForwardMasks![source.Index].Unset(typeId);
            for (var i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                if (!relStorage.HasBackward(target.Index))
                    _relationBackwardMasks![target.Index].Unset(typeId);
            }

            StructuralVersion++;
            return true;
        }

        private void EnsureComponentTypeCapacity(int typeId)
        {
            if (typeId < _componentVersions.Length) return;
            var capacity = Math.Max(typeId + 1, _componentVersions.Length * 2);
            Array.Resize(ref _componentVersions, capacity);
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            Array.Resize(ref _componentEntityCounts, capacity);
            Array.Resize(ref _peakComponentEntityCounts, capacity);
#endif
            if (_singletonEntities != null) Array.Resize(ref _singletonEntities, capacity);
            if (_incrementalQueryPlans != null) Array.Resize(ref _incrementalQueryPlans, capacity);
        }

        internal void EnsureComponentTypeRegistered<T>() where T : struct
        {
            ThrowIfDisposed();
            EnsureComponentTypeCapacity(ComponentType<T>.Id);
        }

        private void EnsureRelationMasks()
        {
            if (_relationForwardMasks != null) return;
            _relationForwardMasks = new ComponentMask[_versions.Length];
            _relationBackwardMasks = new ComponentMask[_versions.Length];
        }

        private void MoveEntityTo(in Entity entity, Archetype destination)
        {
            var sourceLocation = _locations[entity.Index];
            if (sourceLocation.IsValid && ReferenceEquals(sourceLocation.Archetype, destination)) return;

            // Removing the last component leaves the entity alive but unassigned,
            // so the next Add can place it directly without Empty-archetype churn.
            if (destination.TypeIds.Length == 0)
            {
                if (!sourceLocation.IsValid) return;
                var movedFromEmptyPath = sourceLocation.Archetype.RemoveAt(sourceLocation);
                _locations[entity.Index] = default;
                if (movedFromEmptyPath >= 0)
                    _locations[movedFromEmptyPath] = new EntityLocation(sourceLocation.Chunk, sourceLocation.Row);
                return;
            }

            if (!sourceLocation.IsValid)
            {
                _locations[entity.Index] = destination.Add(entity.Index);
                return;
            }

            var targetLocation = destination.Add(entity.Index);
            sourceLocation.Archetype.CopySharedComponents(sourceLocation, destination, targetLocation);
            var movedIndex = sourceLocation.Archetype.RemoveAt(sourceLocation);
            _locations[entity.Index] = targetLocation;
            if (movedIndex >= 0)
                _locations[movedIndex] = new EntityLocation(sourceLocation.Chunk, sourceLocation.Row);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetTemplateArchetypeComponent<T>(int entityIndex, Archetype archetype,
            int columnIndex, in T component) where T : struct =>
            archetype.Get<T>(_locations[entityIndex], columnIndex) = component;

        internal Archetype GetOrCreateTemplateArchetype(int[] typeIds) => _archetypes.GetOrCreate(typeIds);


        private void RemoveFromArchetype(int entityIndex)
        {
            var location = _locations[entityIndex];
            if (!location.IsValid) return;
            var movedIndex = location.Archetype.RemoveAt(location);
            _locations[entityIndex] = default;
            if (movedIndex >= 0)
                _locations[movedIndex] = new EntityLocation(location.Chunk, location.Row);
        }

       [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Entity GetEntity(int entityIndex) => new(entityIndex, _versions[entityIndex], this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateAlive(in Entity entity)
        {
            if (_disposed) ThrowDisposedEntityAccess();
            if (!ReferenceEquals(entity.World, this)
                || (uint)entity.Index >= (uint)_entityCount
                || _versions[entity.Index] != entity.Version)
                ThrowInvalidEntity();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowDisposedEntityAccess() => throw new ObjectDisposedException(nameof(World));

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidEntity() =>
            throw new InvalidOperationException("Entity is not alive in this World.");

        public EntityQueryBuilder Query()
        {
            FlushStructuralBatch();
            ThrowIfDisposed();
            return new EntityQueryBuilder(this);
        }

        public Query<T1> Query<T1>() where T1 : struct { FlushStructuralBatch(); return new(this); }
        public Query<T1, T2> Query<T1, T2>() where T1 : struct where T2 : struct { FlushStructuralBatch(); return new(this); }

        public Query<T1, T2, T3> Query<T1, T2, T3>() where T1 : struct where T2 : struct where T3 : struct { FlushStructuralBatch(); return new(this); }

        public Query<T1, T2, T3, T4> Query<T1, T2, T3, T4>()
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
            CreateQuery<T1, T2, T3, T4>();

        private Query<T1, T2, T3, T4> CreateQuery<T1, T2, T3, T4>()
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            FlushStructuralBatch();
            return new Query<T1, T2, T3, T4>(this);
        }

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        /// <summary>
        /// Captures the World's current state for diagnostics. This method allocates the snapshot and its
        /// component-storage array, but adds no tracking cost to Spawn, Despawn, component, or Query operations.
        /// Call it from the same thread that owns the World and not while a parallel Query is executing.
        /// </summary>
        public WorldDiagnosticsSnapshot CreateDiagnosticsSnapshot()
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _parallelQueryActive) != 0)
                throw new InvalidOperationException(
                    "World diagnostics cannot be captured while a parallel Query is executing.");

            var typeCount = ComponentTypeRegistry.Count;
            var counts = new int[typeCount];
            var capacities = new int[typeCount];
            var present = new bool[typeCount];
            var storageCount = 0;
            var archetypes = _archetypes.All;
            for (var archetypeIndex = 0; archetypeIndex < archetypes.Count; archetypeIndex++)
            {
                var archetype = archetypes[archetypeIndex];
                for (var typeIndex = 0; typeIndex < archetype.TypeIds.Length; typeIndex++)
                {
                    var typeId = archetype.TypeIds[typeIndex];
                    if (!present[typeId]) { present[typeId] = true; storageCount++; }
                    counts[typeId] += archetype.EntityCount;
                    var chunks = archetype.Chunks;
                    for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                        capacities[typeId] += chunks[chunkIndex].EntityIds.Length;
                }
            }

            var storages = new ComponentStorageDiagnostics[storageCount];
            var destinationIndex = 0;
            for (var typeId = 0; typeId < typeCount; typeId++)
            {
                if (!present[typeId]) continue;
                storages[destinationIndex++] = new ComponentStorageDiagnostics(
                    typeId,
                    ComponentTypeRegistry.GetType(typeId),
                    counts[typeId],
                    capacities[typeId],
                    _singletonTypeMask.Has(typeId));
            }

            return new WorldDiagnosticsSnapshot(
                _entityCount - _freeIndices.Count,
                _entityCount,
                _versions.Length,
                StructuralVersion,
                _archetypes.All.Count,
                _entityQueryPlans?.Count ?? 0,
                _relationStorages?.Count ?? 0,
                GetBindingCount(),
                storages);
        }

        /// <summary>
        /// Captures every live Entity and its component type IDs. Component IDs are flattened into one array;
        /// component values are not copied or boxed. This method performs work and allocates only when called.
        /// </summary>
        public EntityListDiagnosticsSnapshot CreateEntityDiagnosticsSnapshot()
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _parallelQueryActive) != 0)
                throw new InvalidOperationException(
                    "Entity diagnostics cannot be captured while a parallel Query is executing.");

            var aliveCount = _entityCount - _freeIndices.Count;
            var freeFlagsArray = System.Buffers.ArrayPool<bool>.Shared.Rent(Math.Max(1, _entityCount));
            var freeFlags = freeFlagsArray.AsSpan(0, _entityCount);
            freeFlags.Clear();

            try
            {
                foreach (var freeIndex in _freeIndices) freeFlags[freeIndex] = true;

                var totalComponentCount = 0;
                for (var entityIndex = 0; entityIndex < _entityCount; entityIndex++)
                {
                    if (freeFlags[entityIndex]) continue;
                    var location = _locations[entityIndex];
                    if (location.IsValid) totalComponentCount += location.Archetype.TypeIds.Length;
                }

                var entities = new EntityDiagnostics[aliveCount];
                var componentTypeIds = new int[totalComponentCount];
                var entityDestination = 0;
                var componentDestination = 0;

                for (var entityIndex = 0; entityIndex < _entityCount; entityIndex++)
                {
                    if (freeFlags[entityIndex]) continue;
                    var location = _locations[entityIndex];
                    var typeIds = location.IsValid ? location.Archetype.TypeIds : Array.Empty<int>();
                    var componentStart = componentDestination;
                    if (typeIds.Length != 0)
                        Array.Copy(typeIds, 0, componentTypeIds, componentDestination, typeIds.Length);
                    componentDestination += typeIds.Length;
                    entities[entityDestination++] = new EntityDiagnostics(
                        GetEntity(entityIndex),
                        componentStart,
                        componentDestination - componentStart,
                        GetComponentTypeHash(typeIds));
                }

                var componentTypesById = new Type?[ComponentTypeRegistry.Count];
                for (var typeId = 0; typeId < componentTypesById.Length; typeId++)
                    componentTypesById[typeId] = ComponentTypeRegistry.GetType(typeId);

                return new EntityListDiagnosticsSnapshot(entities, componentTypeIds, componentTypesById);
            }
            finally
            {
                freeFlags.Clear();
                System.Buffers.ArrayPool<bool>.Shared.Return(freeFlagsArray);
            }
        }

        /// <summary>
        /// Captures boxed copies of every component value owned by one selected Entity. A stale or foreign Entity
        /// produces an empty snapshot with IsAlive set to false. Modifying boxed values does not modify the World.
        /// </summary>
        public EntityDiagnosticsSnapshot CreateEntityDiagnosticsSnapshot(Entity entity)
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _parallelQueryActive) != 0)
                throw new InvalidOperationException(
                    "Entity diagnostics cannot be captured while a parallel Query is executing.");
            if (!IsAlive(entity))
                return new EntityDiagnosticsSnapshot(entity, false, Array.Empty<EntityComponentDiagnostics>());

            var location = _locations[entity.Index];
            if (!location.IsValid)
                return new EntityDiagnosticsSnapshot(entity, true, Array.Empty<EntityComponentDiagnostics>());
            var typeIds = location.Archetype.TypeIds;
            var components = new EntityComponentDiagnostics[typeIds.Length];
            var columns = location.Chunk.Columns;
            for (var i = 0; i < typeIds.Length; i++)
            {
                var typeId = typeIds[i];
                var value = columns[i].GetBoxed(location.Row);
                components[i] = new EntityComponentDiagnostics(
                    typeId, ComponentTypeRegistry.GetType(typeId), value!);
            }
            return new EntityDiagnosticsSnapshot(entity, true, components);
        }

        private static int GetComponentTypeHash(int[] typeIds)
        {
            unchecked
            {
                var hash = 17;
                for (var i = 0; i < typeIds.Length; i++) hash = hash * 31 + typeIds[i];
                return hash;
            }
        }
#endif

        public void Dispose()
        {
            if (_disposed) return;
            ThrowIfParallelQueryActive();
            _disposed = true;

            _collectors?.InvalidateAll();
            _collectors = null;

            _baseArchetypeQueryPlans?.Clear();
            _baseArchetypeQueryPlans = null;
            _entityQueryPlans?.Clear();
            _entityQueryPlans = null;
            _incrementalQueryPlans = null;
            _relationStorages?.Clear();
            _relationStorages = null;
            _bindings?.Clear();
            _bindings = null;
            _entityBindingHeads?.Clear();
            _entityBindingHeads = null;
            _entityBindingObjects = null;
            _entityBindingNext = null;
            _entityBindingCount = 0;
            _freeEntityBinding = -1;
            if (_structBindingStorages != null)
                foreach (var storage in _structBindingStorages.Values) storage.Clear();
            _structBindingStorages?.Clear();
            _structBindingStorages = null;
            _parallelQueryRunner?.Dispose();
            _parallelQueryRunner = null;
            if (_singletonEntities != null) Array.Clear(_singletonEntities, 0, _singletonEntities.Length);
            _singletonEntities = null;
            _freeIndices.Clear();
            _versions = Array.Empty<uint>();
            _relationForwardMasks = null;
            _relationBackwardMasks = null;
            _entityCount = 0;
        }
    }

    #endregion

    #region --- 8. Optimized Dense Component Query Iterators ---

    /// <summary>Starts an Entity-returning query with a required component.</summary>
    public readonly struct EntityQueryBuilder
    {
        private readonly World _world;

        internal EntityQueryBuilder(World world) => _world = world;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityQuery<T> With<T>() where T : struct
        {
            return new EntityQuery<T>(_world);
        }
    }

    /// <summary>An Entity-returning query specialized for one required component.</summary>
    public readonly struct EntityQuery<T1> where T1 : struct
    {
        private readonly World _world;
        private readonly ArchetypeQueryPlan _plan;
        private static readonly int[] RequiredTypes = { ComponentType<T1>.Id };

        internal EntityQuery(World world)
        {
            world.ThrowIfDisposed();
            _world = world;
            _plan = world.GetOrCreateBaseArchetypeQueryPlan(typeof(EntityQuery<T1>), RequiredTypes);
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                _world.ThrowIfDisposed();
                _plan.Ensure();
                var count = 0;
                var matches = _plan.Matches;
                for (var i = 0; i < matches.Count; i++) count += matches[i].EntityCount;
                return count;
            }
        }

        public Entity this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                _world.ThrowIfDisposed();
                _plan.Ensure();
                if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
                var matches = _plan.Matches;
                for (var i = 0; i < matches.Count; i++)
                {
                    var archetype = matches[i];
                    if (index >= archetype.EntityCount)
                    {
                        index -= archetype.EntityCount;
                        continue;
                    }

                    var chunkIndex = index / ComponentPageManager.PageCapacity;
                    var row = index % ComponentPageManager.PageCapacity;
                    return _world.GetEntity(archetype.Chunks[chunkIndex].EntityIds[row]);
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityQuery With<T>() where T : struct
        {
            var required = default(ComponentMask);
            required.Set(ComponentType<T1>.Id);
            required.Set(ComponentType<T>.Id);
            return new EntityQuery(_world, required, default, default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityQuery Without<T>() where T : struct
        {
            var required = default(ComponentMask);
            required.Set(ComponentType<T1>.Id);
            var excluded = default(ComponentMask);
            excluded.Set(ComponentType<T>.Id);
            return new EntityQuery(_world, required, excluded, default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityQuery Any<TAny1, TAny2>() where TAny1 : struct where TAny2 : struct
        {
            var required = default(ComponentMask);
            required.Set(ComponentType<T1>.Id);
            var any = default(ComponentMask);
            any.Set(ComponentType<TAny1>.Id);
            any.Set(ComponentType<TAny2>.Id);
            return new EntityQuery(_world, required, default, any);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Matches(Entity entity)
        {
            var required = default(ComponentMask);
            required.Set(ComponentType<T1>.Id);
            return _world.Matches(entity, required, default, default);
        }

        /// <summary>Creates a stable EntityQueryResult for this single-component query.</summary>
        public EntityQueryResult Result()
        {
            var required = default(ComponentMask);
            required.Set(ComponentType<T1>.Id);
            return _world.GetEntityQueryResult(required, default, default);
        }

        /// <summary>Builds this Query's current Archetype match cache without visiting entities.</summary>
        public EntityQuery<T1> Warmup()
        {
            _world.FlushStructuralBatch();
            _world.ThrowIfDisposed();
            _plan.Ensure();
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new(_world, _plan);

        public ref struct Enumerator
        {
            private readonly World _world;
            private readonly int _structuralVersion;
            private readonly ArchetypeQueryPlan _plan;
            private int _archetypeIndex;
            private int _chunkIndex;
            private int _row;
            private Entity _current;

            internal Enumerator(World world, ArchetypeQueryPlan plan)
            {
                _world = world;
                _structuralVersion = world.StructuralVersion;
                _plan = plan;
                plan.Ensure();
                _archetypeIndex = 0;
                _chunkIndex = 0;
                _row = -1;
                _current = default;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                _world.ThrowIfDisposed();
                var matches = _plan.Matches;
                while (_archetypeIndex < matches.Count)
                {
                    var archetype = matches[_archetypeIndex];
                    if (_chunkIndex >= archetype.Chunks.Count) { _archetypeIndex++; _chunkIndex = 0; _row = -1; continue; }
                    var chunk = archetype.Chunks[_chunkIndex];
                    if (++_row < chunk.Count)
                    {
                        _world.ValidateQueryStructuralVersion(_structuralVersion);
                        _current = _world.GetEntity(chunk.EntityIds[_row]);
                        return true;
                    }

                    _chunkIndex++;
                    _row = -1;
                }

                return false;
            }

            public Entity Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _current;
            }
        }
    }

    /// <summary>An allocation-free query that returns matching entities.</summary>
    public readonly struct EntityQuery
    {
        private readonly World _world;
        private readonly ComponentMask _requiredMask;
        private readonly ComponentMask _excludedMask;
        private readonly ComponentMask _anyMask;

        internal EntityQuery(World world, in ComponentMask requiredMask, in ComponentMask excludedMask,
            in ComponentMask anyMask)
        {
            world.ThrowIfDisposed();
            _world = world;
            _requiredMask = requiredMask;
            _excludedMask = excludedMask;
            _anyMask = anyMask;
        }

        public int Count => Result().Count;

        public Entity this[int index] => Result()[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityQuery With<T>() where T : struct
        {
            var required = _requiredMask;
            required.Set(ComponentType<T>.Id);
            return new EntityQuery(_world, required, _excludedMask, _anyMask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityQuery Without<T>() where T : struct
        {
            var excluded = _excludedMask;
            excluded.Set(ComponentType<T>.Id);
            return new EntityQuery(_world, _requiredMask, excluded, _anyMask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityQuery Any<TAny1, TAny2>() where TAny1 : struct where TAny2 : struct
        {
            var any = _anyMask;
            any.Set(ComponentType<TAny1>.Id);
            any.Set(ComponentType<TAny2>.Id);
            return new EntityQuery(_world, _requiredMask, _excludedMask, any);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Matches(Entity entity) =>
            _world.Matches(entity, _requiredMask, _excludedMask, _anyMask);

        public EntityQueryResult Result() =>
            _world.GetEntityQueryResult(_requiredMask, _excludedMask, _anyMask);

        /// <summary>Builds this Query's current filtered Entity cache.</summary>
        public EntityQuery Warmup()
        {
            _world.FlushStructuralBatch();
            _world.ThrowIfDisposed();
            _world.GetEntityQueryResult(_requiredMask, _excludedMask, _anyMask);
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityQueryResult.Enumerator GetEnumerator() => Result().GetEnumerator();

    }

    internal sealed class EntityQueryPlan : IIncrementalQueryPlan
    {
        private readonly World _world;
        private readonly ComponentMask _requiredMask;
        private readonly ComponentMask _excludedMask;
        private readonly ComponentMask _anyMask;
        private readonly ComponentMask _observedMask;
        private readonly ArchetypeQueryPlan _archetypePlan;
        private int _componentVersion;
        private bool _initialized;
        private bool _dirty = true;

        internal int[] Entities = Array.Empty<int>();
        internal int Count;
        internal int Generation { get; private set; }

        internal EntityQueryPlan(World world, in ComponentMask requiredMask,
            in ComponentMask excludedMask, in ComponentMask anyMask)
        {
            _world = world;
            _requiredMask = requiredMask;
            _excludedMask = excludedMask;
            _anyMask = anyMask;
            _observedMask = requiredMask.Union(excludedMask).Union(anyMask);
            _archetypePlan = world.CreateArchetypeQueryPlan(
                requiredMask.ToTypeIds(), excludedMask.ToTypeIds(), anyMask.ToTypeIds());
            world.RegisterIncrementalQueryPlan(this, _observedMask);
        }

        internal bool HasFilter(in ComponentMask requiredMask, in ComponentMask excludedMask,
            in ComponentMask anyMask) =>
            MaskEquals(_requiredMask, requiredMask)
            && MaskEquals(_excludedMask, excludedMask)
            && MaskEquals(_anyMask, anyMask);

        internal void Ensure()
        {
            var componentVersion = _world.GetComponentVersion(_observedMask);
            if (_initialized && !_dirty && _componentVersion == componentVersion) return;
            if (!_initialized || !_dirty) Generation++;

            Count = 0;
            _archetypePlan.Ensure();
            var matches = _archetypePlan.Matches;
            var capacity = 0;
            for (var i = 0; i < matches.Count; i++) capacity += matches[i].EntityCount;
            EnsureCapacity(capacity);
            for (var i = 0; i < matches.Count; i++)
            {
                var chunks = matches[i].Chunks;
                for (var c = 0; c < chunks.Count; c++)
                {
                    var chunk = chunks[c];
                    for (var row = 0; row < chunk.Count; row++) Entities[Count++] = chunk.EntityIds[row];
                }
            }

            _componentVersion = componentVersion;
            _initialized = true;
            _dirty = false;
        }

        internal void Validate(int expectedGeneration)
        {
            _world.ThrowIfDisposed();
            if (Generation != expectedGeneration)
                throw new InvalidOperationException(
                    "The EntityQuery result is no longer valid because a relevant component changed. Call Result() again.");
        }

        void IIncrementalQueryPlan.OnFilterComponentChanged(int entityIndex)
        {
            if (_dirty) return;
            _dirty = true;
            Generation++;
        }

        private void EnsureCapacity(int required)
        {
            if (Entities.Length >= required) return;
            var capacity = Entities.Length == 0 ? 4 : Entities.Length * 2;
            if (capacity < required) capacity = required;
            Array.Resize(ref Entities, capacity);
        }

        private static bool MaskEquals(in ComponentMask left, in ComponentMask right) => left.SameAs(right);
    }

    public readonly struct EntityQueryResult
    {
        private readonly World _world;
        private readonly EntityQueryPlan _plan;
        private readonly int _generation;
        private readonly int _count;

        internal EntityQueryResult(World world, EntityQueryPlan plan, int generation, int count)
        {
            _world = world;
            _plan = plan;
            _generation = generation;
            _count = count;
        }

        public int Count
        {
            get
            {
                Validate();
                return _count;
            }
        }

        public Entity this[int index]
        {
            get
            {
                Validate();
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                return _world.GetEntity(_plan.Entities[index]);
            }
        }

        public Enumerator GetEnumerator()
        {
            Validate();
            return new Enumerator(_world, _plan, _generation, _count);
        }

        private void Validate()
        {
            _plan.Validate(_generation);
        }

        public struct Enumerator
        {
            private readonly World _world;
            private readonly EntityQueryPlan _plan;
            private readonly int _generation;
            private readonly int _count;
            private int _index;

            internal Enumerator(World world, EntityQueryPlan plan, int generation, int count)
            {
                _world = world;
                _plan = plan;
                _generation = generation;
                _count = count;
                _index = -1;
            }

            public bool MoveNext()
            {
                _plan.Validate(_generation);
                return ++_index < _count;
            }

            public Entity Current => _world.GetEntity(_plan.Entities[_index]);
        }
    }

    /// <summary>One-component Query over matching Archetype chunks.</summary>
    public readonly struct Query<T1> where T1 : struct
    {
        private static readonly int[] RequiredTypes = { ComponentType<T1>.Id };
        private readonly World _world;
        private readonly ArchetypeQueryPlan _plan;

        public Query(World world)
        {
            world.FlushStructuralBatch();
            world.ThrowIfDisposed();
            _world = world;
            _plan = world.GetOrCreateBaseArchetypeQueryPlan(typeof(Query<T1>), RequiredTypes);
        }

        private Query(World world, ArchetypeQueryPlan plan)
        {
            _world = world;
            _plan = plan;
        }

        public Query<T1> With<T>() where T : struct => WithRequired(ComponentType<T>.Id);

        private Query<T1> WithRequired(int typeId)
        {
            var required = ComponentTypeIdList.Add(_plan.Required, typeId);
            return new Query<T1>(_world,
                _world.CreateArchetypeQueryPlan(required, _plan.Excluded, _plan.Any));
        }

        public Query<T1> Without<T>() where T : struct
        {
            var excluded = ComponentTypeIdList.Add(_plan.Excluded, ComponentType<T>.Id);
            return new Query<T1>(_world,
                _world.CreateArchetypeQueryPlan(_plan.Required, excluded, _plan.Any));
        }

        public Query<T1> Any<TAny1, TAny2>() where TAny1 : struct where TAny2 : struct
        {
            var any = ComponentTypeIdList.Add(_plan.Any, ComponentType<TAny1>.Id);
            any = ComponentTypeIdList.Add(any, ComponentType<TAny2>.Id);
            return new Query<T1>(_world,
                _world.CreateArchetypeQueryPlan(_plan.Required, _plan.Excluded, any));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator()
        {
            _world.FlushStructuralBatch();
            _world.ThrowIfDisposed();
            return new Enumerator(_world, _plan);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Matches(Entity entity) => _world.Matches(entity, _plan);

        /// <summary>Builds this Query's current Archetype match cache without visiting entities.</summary>
        public Query<T1> Warmup()
        {
            _world.FlushStructuralBatch();
            _world.ThrowIfDisposed();
            _plan.Ensure();
            return this;
        }

        public void ForEach(QueryAction<T1> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            _world.FlushStructuralBatch();
            _world.ThrowIfDisposed();
            _plan.Ensure();
            var structuralVersion = _world.StructuralVersion;
            var matches = _plan.Matches;
            var world = _world;
            for (var a = 0; a < matches.Count; a++)
            {
                var archetype = matches[a];
                for (var c = 0; c < archetype.Chunks.Count; c++)
                {
                    var chunk = archetype.Chunks[c];
                    var count = chunk.Count;
                    if (count == 0) continue;
                    world.ValidateQueryStructuralVersion(structuralVersion);
                    var components = archetype.GetColumn<T1>(chunk);
                    var entityIds = chunk.EntityIds;
                    for (var i = 0; i < count; i++)
                    {
                        var entity = world.GetEntity(entityIds[i]);
                        action(in entity, ref components[i]);
                    }
                }
            }
        }

        public void ForEach<TAction>(ref TAction action) where TAction : struct, IQueryAction<T1>
        {
            _world.FlushStructuralBatch();
            _world.ThrowIfDisposed();
            _plan.Ensure();
            var structuralVersion = _world.StructuralVersion;
            var matches = _plan.Matches;
            var world = _world;
            for (var a = 0; a < matches.Count; a++)
            {
                var archetype = matches[a];
                for (var c = 0; c < archetype.Chunks.Count; c++)
                {
                    var chunk = archetype.Chunks[c];
                    var count = chunk.Count;
                    if (count == 0) continue;
                    world.ValidateQueryStructuralVersion(structuralVersion);
                    var components = archetype.GetColumn<T1>(chunk);
                    var entityIds = chunk.EntityIds;
                    for (var i = 0; i < count; i++)
                    {
                        var entity = world.GetEntity(entityIds[i]);
                        action.Execute(in entity, ref components[i]);
                    }
                }
            }
        }

        internal JobQueryRangeLease<T1> AcquireJobRanges()
        {
            _world.FlushStructuralBatch();
            _world.ThrowIfDisposed();
            _plan.Ensure();
            _world.EnterParallelQuery();
            return new JobQueryRangeLease<T1>(_world, _plan.Matches);
        }

        internal void GetJobRangeReservationCounts(int maximumEntityCount, int batchSize,
            out int rangeCount, out int workCount)
        {
            _world.ThrowIfDisposed();
            if (maximumEntityCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumEntityCount));
            if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
            _plan.Ensure();
            var matchingArchetypeCount = _plan.Matches.Count;
            rangeCount = World.GetParallelRangeReservationCount(maximumEntityCount, matchingArchetypeCount,
                ComponentPageManager.PageCapacity);
            var workItemsPerRange = (ComponentPageManager.PageCapacity - 1) / batchSize + 1;
            workCount = checked(rangeCount * workItemsPerRange);
        }

        internal void ReserveParallelRangesCore(int maximumEntityCount, int batchSize)
        {
            _world.ThrowIfDisposed();
            if (maximumEntityCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumEntityCount));
            if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
            _plan.Ensure();
            var job = _plan.ParallelRangeJob as ParallelRangeJob;
            if (job == null) _plan.ParallelRangeJob = job = new ParallelRangeJob();
            job.EnsureItemCapacity(World.GetParallelRangeReservationCount(
                maximumEntityCount, _plan.Matches.Count, batchSize));
        }

        [Obsolete("Use ForEach(ref action) instead. Omitting Entity access does not justify a separate iteration API.")]
        public void ForEachComponents<TAction>(ref TAction action) where TAction : struct, IComponentAction<T1>
        {
            _plan.Ensure();
            var matches = _plan.Matches;
            for (var a = 0; a < matches.Count; a++)
            {
                var archetype = matches[a];
                for (var c = 0; c < archetype.Chunks.Count; c++)
                {
                    var chunk = archetype.Chunks[c];
                    var components = archetype.GetColumn<T1>(chunk);
                    for (var i = 0; i < chunk.Count; i++) action.Execute(ref components[i]);
                }
            }
        }

        internal void ParallelForRanges(ParallelRangeAction<T1> action, int minimumEntityCount, int batchSize)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            _world.ThrowIfDisposed();
            _plan.Ensure();
            var matches = _plan.Matches;
            var entityCount = 0;
            for (var i = 0; i < matches.Count; i++) entityCount += matches[i].EntityCount;
            _world.EnterParallelQuery();
            try
            {
                if (entityCount < minimumEntityCount || Environment.ProcessorCount <= 1)
                {
                    var queryOffset = 0;
                    for (var a = 0; a < matches.Count; a++)
                    {
                        var archetype = matches[a];
                        for (var c = 0; c < archetype.Chunks.Count; c++)
                        {
                            var chunk = archetype.Chunks[c];
                            if (chunk.Count == 0) continue;
                            action(archetype.GetColumn<T1>(chunk).AsSpan(0, chunk.Count),
                                new EntityRange(_world, chunk.EntityIds, 0, chunk.Count, queryOffset));
                            queryOffset += chunk.Count;
                        }
                    }
                    return;
                }

                var job = _plan.ParallelRangeJob as ParallelRangeJob;
                if (job == null) _plan.ParallelRangeJob = job = new ParallelRangeJob();
                job.World = _world;
                job.Action = action;
                job.Prepare(matches, batchSize);
                _world.ExecuteParallelQuery(job);
            }
            catch (AggregateException) { throw; }
            catch (Exception exception) { throw new AggregateException(exception); }
            finally { _world.ExitParallelQuery(); }
        }

        private sealed class ParallelRangeJob : ParallelQueryJob
        {
            internal World World = null!;
            internal ParallelRangeAction<T1> Action = null!;

            protected override void Execute(in ParallelQueryWorkItem item)
            {
                var archetype = item.Archetype;
                Action(archetype.GetColumn<T1>(item.Chunk).AsSpan(item.Start, item.End - item.Start),
                    new EntityRange(World, item.Chunk.EntityIds, item.Start, item.End - item.Start,
                        item.QueryOffset));
            }
        }

        public ref struct Enumerator
        {
            private readonly World _world;
            private readonly int _structuralVersion;
            private readonly ArchetypeQueryPlan _plan;
            private int _archetypeIndex;
            private int _chunkIndex;
            private int _row;
            private T1[]? _components;
            private int _count;

            internal Enumerator(World world, ArchetypeQueryPlan plan)
            {
                plan.Ensure();
                _world = world;
                _structuralVersion = world.StructuralVersion;
                _plan = plan;
                _archetypeIndex = 0;
                _chunkIndex = 0;
                _row = -1;
                _components = null;
                _count = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (++_row < _count) return true;
                var matches = _plan.Matches;
                while (_archetypeIndex < matches.Count)
                {
                    var archetype = matches[_archetypeIndex];
                    if (_chunkIndex >= archetype.Chunks.Count)
                    {
                        _archetypeIndex++;
                        _chunkIndex = 0;
                        continue;
                    }
                    var chunk = archetype.Chunks[_chunkIndex++];
                    _count = chunk.Count;
                    if (_count == 0) continue;
                    _world.ValidateQueryStructuralVersion(_structuralVersion);
                    _components = archetype.GetColumn<T1>(chunk);
                    _row = 0;
                    return true;
                }
                return false;
            }

            public ref T1 Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _components![_row];
            }
        }
    }

    #endregion
}
