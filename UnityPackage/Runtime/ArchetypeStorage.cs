#if !RELEASE && !DISABLE_LITHEECS_DIAGNOSTICS
#define _INTERNAL_DERIVED_USE_DIAGNOSTICS
#endif

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LitheEcs
{
    internal readonly struct EntityLocation
    {
        internal readonly ArchetypeChunk Chunk;
        internal readonly int Row;

        internal EntityLocation(ArchetypeChunk chunk, int row)
        {
            Chunk = chunk;
            Row = row;
        }

        internal Archetype Archetype => Chunk.Owner;
        internal bool IsValid => Chunk != null;
    }

    internal readonly struct ArchetypeKey : IEquatable<ArchetypeKey>
    {
        private readonly int[] _typeIds;
        private readonly int _hashCode;

        internal ArchetypeKey(int[] sortedTypeIds)
        {
            _typeIds = sortedTypeIds;
            unchecked
            {
                var hash = 17;
                for (var i = 0; i < _typeIds.Length; i++) hash = hash * 31 + _typeIds[i];
                _hashCode = hash;
            }
        }

        public bool Equals(ArchetypeKey other)
        {
            var left = _typeIds;
            var right = other._typeIds;
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is ArchetypeKey other && Equals(other);
        public override int GetHashCode() => _hashCode;
    }

    internal interface IArchetypeColumn
    {
        Type ComponentType { get; }
        object? GetBoxed(int row);
        void CopyTo(int sourceRow, IArchetypeColumn destination, int destinationRow);
        void Clear(int row);
        void ClearAll(int count);
        void MoveLast(int sourceRow, int destinationRow);
    }

    internal sealed class ArchetypeColumn<T> : IArchetypeColumn
    {
        internal T[] Values;

        public ArchetypeColumn(int capacity) => Values = new T[capacity];
        Type IArchetypeColumn.ComponentType => typeof(T);
        object? IArchetypeColumn.GetBoxed(int row) => Values[row];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void IArchetypeColumn.CopyTo(int sourceRow, IArchetypeColumn destination, int destinationRow) =>
            ((ArchetypeColumn<T>)destination).Values[destinationRow] = Values[sourceRow];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void IArchetypeColumn.Clear(int row) => Values[row] = default!;

        void IArchetypeColumn.ClearAll(int count)
        {
            if (count > 0) Array.Clear(Values, 0, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void IArchetypeColumn.MoveLast(int sourceRow, int destinationRow)
        {
            Values[destinationRow] = Values[sourceRow];
            Values[sourceRow] = default!;
        }

    }

    /// <summary>World-owned pool of fixed-size managed component pages.</summary>
    internal sealed class ComponentPageManager
    {
        internal const int PageCapacity = 256;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        private readonly AllocationDiagnostics _allocationDiagnostics;
#endif
        private ComponentPagePool?[] _pools = Array.Empty<ComponentPagePool?>();
        private int[] _sharedUsablePageCounts = Array.Empty<int>();
        private readonly Stack<int[]> _entityPages = new Stack<int[]>();
        internal int AvailableEntityPageCount => _entityPages.Count;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        internal ComponentPageManager(AllocationDiagnostics allocationDiagnostics) =>
            _allocationDiagnostics = allocationDiagnostics;
#else
        internal ComponentPageManager() { }
#endif

        private ComponentPagePool GetPool(int typeId)
        {
            if (typeId >= _pools.Length)
                Array.Resize(ref _pools, Math.Max(typeId + 1, Math.Max(4, _pools.Length * 2)));
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            return _pools[typeId] ??= new ComponentPagePool(typeId,
                ComponentTypeRegistry.GetColumnFactory(typeId), _allocationDiagnostics);
#else
            return _pools[typeId] ??= new ComponentPagePool(ComponentTypeRegistry.GetColumnFactory(typeId));
#endif
        }

        internal IArchetypeColumn RentColumn(int typeId) => GetPool(typeId).Rent();
        internal void ReturnColumn(int typeId, IArchetypeColumn column) => GetPool(typeId).Return(column);

        internal int[] RentEntityPage()
        {
            int[] page;
            if (_entityPages.Count != 0) page = _entityPages.Pop();
            else
            {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
                if (_allocationDiagnostics.Enabled) _allocationDiagnostics.EntityPageAllocations++;
#endif
                page = new int[PageCapacity];
                // Allocate Stack's backing storage on the cold rent path, not on a later hot return.
                _entityPages.Push(page);
                page = _entityPages.Pop();
            }
            return page;
        }

        internal void ReturnEntityPage(int[] page)
        {
            Array.Clear(page, 0, page.Length);
            _entityPages.Push(page);
        }


        internal void ReserveShared(ReadOnlySpan<int> typeIds, ReadOnlySpan<int> layoutTypeCounts,
            int layoutCount, int capacity)
        {
            var usablePageCount = (capacity + PageCapacity - 1) / PageCapacity;
            for (var i = 0; i < typeIds.Length; i++)
            {
                var typeId = typeIds[i];
                // Each non-empty layout can retain one partially filled page. The final
                // additional page also lets a destination activate before its source empties.
                var pageCount = usablePageCount + Math.Min(layoutTypeCounts[i], capacity);
                GetPool(typeId).EnsureAvailable(pageCount);
                if (typeId >= _sharedUsablePageCounts.Length)
                    Array.Resize(ref _sharedUsablePageCounts,
                        Math.Max(typeId + 1, Math.Max(4, _sharedUsablePageCounts.Length * 2)));
                if (_sharedUsablePageCounts[typeId] < usablePageCount)
                    _sharedUsablePageCounts[typeId] = usablePageCount;
            }
            var entityPageCount = usablePageCount + Math.Min(layoutCount, capacity);
            while (_entityPages.Count < entityPageCount) _entityPages.Push(new int[PageCapacity]);
        }

        internal int GetSharedUsablePageCount(ReadOnlySpan<int> typeIds)
        {
            if (typeIds.Length == 0) return 0;
            var result = int.MaxValue;
            for (var i = 0; i < typeIds.Length; i++)
            {
                var typeId = typeIds[i];
                if (typeId >= _sharedUsablePageCounts.Length || _sharedUsablePageCounts[typeId] == 0) return 0;
                if (_sharedUsablePageCounts[typeId] < result) result = _sharedUsablePageCounts[typeId];
            }
            return result;
        }

        internal void ReserveAdditional(ReadOnlySpan<int> typeIds, int capacity)
        {
            var pageCount = (capacity + PageCapacity - 1) / PageCapacity;
            for (var i = 0; i < typeIds.Length; i++) GetPool(typeIds[i]).AddPages(pageCount);
            for (var i = 0; i < pageCount; i++) _entityPages.Push(new int[PageCapacity]);
        }

        private sealed class ComponentPagePool
        {
            private readonly Func<int, IArchetypeColumn> _factory;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            private readonly AllocationDiagnostics _allocationDiagnostics;
            private readonly int _typeId;
#endif
            private readonly Stack<IArchetypeColumn> _available = new Stack<IArchetypeColumn>();

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            internal ComponentPagePool(int typeId, Func<int, IArchetypeColumn> factory,
                AllocationDiagnostics allocationDiagnostics)
            {
                _typeId = typeId;
                _factory = factory;
                _allocationDiagnostics = allocationDiagnostics;
            }
#else
            internal ComponentPagePool(Func<int, IArchetypeColumn> factory) => _factory = factory;
#endif
            internal IArchetypeColumn Rent()
            {
                IArchetypeColumn page;
                if (_available.Count != 0) page = _available.Pop();
                else
                {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
                    if (_allocationDiagnostics.Enabled)
                    {
                        _allocationDiagnostics.ComponentPageAllocations++;
                        _allocationDiagnostics.LastComponentPageTypeId = _typeId;
                    }
#endif
                    page = _factory(PageCapacity);
                    // Allocate Stack's backing storage on the cold rent path, not on a later hot return.
                    _available.Push(page);
                    page = _available.Pop();
                }
                return page;
            }
            internal void Return(IArchetypeColumn column)
            {
                column.ClearAll(PageCapacity);
                _available.Push(column);
            }
            internal void EnsureAvailable(int count)
            {
                while (_available.Count < count) _available.Push(_factory(PageCapacity));
            }
            internal void AddPages(int count)
            {
                for (var i = 0; i < count; i++) _available.Push(_factory(PageCapacity));
            }
        }
    }

    /// <summary>
    /// Contiguous component storage for one archetype. Exposed as a single logical chunk
    /// so existing Query APIs (including TryGetAlignedChunk) keep working.
    /// </summary>
    internal sealed class ArchetypeChunk
    {
        internal readonly Archetype Owner;
        internal readonly int ArchetypeIndex;
        internal int[] EntityIds;
        internal IArchetypeColumn[] Columns;
        internal int Count;

        internal ArchetypeChunk(Archetype owner, int[] entityIds, IArchetypeColumn[] columns)
        {
            Owner = owner;
            ArchetypeIndex = owner.Index;
            EntityIds = entityIds;
            Columns = columns;
            Count = 0;
        }
    }

    internal sealed class Archetype
    {
        private const int DirectColumnLookupSize = 256;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        private readonly AllocationDiagnostics _allocationDiagnostics;
#endif

        private readonly int[] _directColumnByTypeId;
        private readonly Dictionary<int, int>? _overflowColumnByTypeId;
        private readonly Dictionary<int, ColumnCopy[]> _copyPlans = new();
        private readonly int[] _clearColumnIndices;
        private readonly ComponentPageManager _pages;
        internal readonly Dictionary<int, Archetype> AddTransitions = new Dictionary<int, Archetype>();
        internal readonly Dictionary<int, Archetype> RemoveTransitions = new Dictionary<int, Archetype>();
        internal Dictionary<AddManyKey, Archetype>? AddManyTransitions;
        internal List<AddManyTransition>? LargeAddManyTransitions;
        internal List<RemoveManyTransition>? RemoveManyTransitions;
        private readonly int _index;
        internal int Index => _index;
        internal readonly int[] TypeIds;
        internal readonly List<ArchetypeChunk> Chunks;
        internal int EntityCount;
        private int _reservedChunkCount;

        private readonly struct ColumnCopy
        {
            internal readonly int Source;
            internal readonly int Destination;

            internal ColumnCopy(int source, int destination)
            {
                Source = source;
                Destination = destination;
            }
        }

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        internal Archetype(int index, int[] typeIds, ComponentPageManager pages,
            AllocationDiagnostics allocationDiagnostics)
#else
        internal Archetype(int index, int[] typeIds, ComponentPageManager pages)
#endif
        {
            _index = index;
            _pages = pages;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            _allocationDiagnostics = allocationDiagnostics;
#endif
            TypeIds = typeIds;
            _directColumnByTypeId = new int[DirectColumnLookupSize];
            Array.Fill(_directColumnByTypeId, -1);
            var overflowCount = 0;
            for (var i = 0; i < typeIds.Length; i++)
                if ((uint)typeIds[i] >= DirectColumnLookupSize) overflowCount++;
            if (overflowCount != 0) _overflowColumnByTypeId = new Dictionary<int, int>(overflowCount);
            for (var i = 0; i < typeIds.Length; i++)
            {
                var typeId = typeIds[i];
                if ((uint)typeId < DirectColumnLookupSize) _directColumnByTypeId[typeId] = i;
                else _overflowColumnByTypeId!.Add(typeId, i);
            }
            var clearCount = 0;
            for (var i = 0; i < typeIds.Length; i++)
                if (ComponentTypeRegistry.RequiresClear(typeIds[i])) clearCount++;
            _clearColumnIndices = new int[clearCount];
            for (int i = 0, destination = 0; i < typeIds.Length; i++)
                if (ComponentTypeRegistry.RequiresClear(typeIds[i]))
                    _clearColumnIndices[destination++] = i;

            Chunks = new List<ArchetypeChunk>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool Has(int typeId) => GetColumnIndexOrMissing(typeId) >= 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetColumnIndex(int typeId)
        {
            var index = GetColumnIndexOrMissing(typeId);
            if (index < 0) throw new KeyNotFoundException($"Component type id {typeId} is not in the archetype.");
            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetColumnIndexOrMissing(int typeId)
        {
            if ((uint)typeId < DirectColumnLookupSize) return _directColumnByTypeId[typeId];
            return _overflowColumnByTypeId != null && _overflowColumnByTypeId.TryGetValue(typeId, out var index)
                ? index
                : -1;
        }

        internal bool ContainsAll(ReadOnlySpan<int> required)
        {
            for (var i = 0; i < required.Length; i++)
                if (!Has(required[i])) return false;
            return true;
        }

        internal bool ContainsNone(ReadOnlySpan<int> excluded)
        {
            for (var i = 0; i < excluded.Length; i++)
                if (Has(excluded[i])) return false;
            return true;
        }

        internal bool Intersects(ReadOnlySpan<int> any)
        {
            for (var i = 0; i < any.Length; i++)
                if (Has(any[i])) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal EntityLocation Add(int entityIndex)
        {
            var chunkIndex = EntityCount / ComponentPageManager.PageCapacity;
            var chunk = chunkIndex < Chunks.Count ? Chunks[chunkIndex] : AddChunk();
            if (chunk.EntityIds.Length == 0) ActivateChunk(chunk);
            var row = chunk.Count;
            chunk.EntityIds[row] = entityIndex;
            chunk.Count = row + 1;
            EntityCount++;
            return new EntityLocation(chunk, row);
        }

        internal void EnsureCapacity(int capacity)
        {
            var requiredChunks = (capacity + ComponentPageManager.PageCapacity - 1) / ComponentPageManager.PageCapacity;
            if (Chunks.Capacity < requiredChunks) Chunks.Capacity = requiredChunks;
            while (Chunks.Count < requiredChunks) AddChunk();
            if (_reservedChunkCount < requiredChunks) _reservedChunkCount = requiredChunks;
        }

        internal void EnsureChunkShellCount(int requiredChunks)
        {
            if (Chunks.Capacity < requiredChunks) Chunks.Capacity = requiredChunks;
            while (Chunks.Count < requiredChunks)
                Chunks.Add(new ArchetypeChunk(this, Array.Empty<int>(),
                    new IArchetypeColumn[TypeIds.Length]));
        }

        private ArchetypeChunk AddChunk()
        {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled)
            {
                _allocationDiagnostics.ChunkCreations++;
                _allocationDiagnostics.LastChunkArchetypeIndex = Index;
                _allocationDiagnostics.LastChunkEntityCount = EntityCount;
            }
#endif
            var columns = new IArchetypeColumn[TypeIds.Length];
            for (var i = 0; i < columns.Length; i++) columns[i] = _pages.RentColumn(TypeIds[i]);
            var chunk = new ArchetypeChunk(this, _pages.RentEntityPage(), columns);
            Chunks.Add(chunk);
            return chunk;
        }

        private void ActivateChunk(ArchetypeChunk chunk)
        {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled) _allocationDiagnostics.ChunkActivations++;
#endif
            var columns = chunk.Columns;
            if (columns.Length != TypeIds.Length) columns = new IArchetypeColumn[TypeIds.Length];
            for (var i = 0; i < columns.Length; i++) columns[i] = _pages.RentColumn(TypeIds[i]);
            chunk.Columns = columns;
            chunk.EntityIds = _pages.RentEntityPage();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref T Get<T>(EntityLocation location) where T : struct
        {
            var columnIndex = GetColumnIndex(ComponentType<T>.Id);
            return ref ((ArchetypeColumn<T>)location.Chunk.Columns[columnIndex]).Values[location.Row];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref T Get<T>(EntityLocation location, int columnIndex) where T : struct =>
            ref ((ArchetypeColumn<T>)location.Chunk.Columns[columnIndex]).Values[location.Row];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Set<T>(EntityLocation location, int columnIndex, in T value) where T : struct =>
            ((ArchetypeColumn<T>)location.Chunk.Columns[columnIndex]).Values[location.Row] = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T[] GetColumn<T>() where T : struct => GetColumn<T>(Chunks[0]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T[] GetColumn<T>(int columnIndex) where T : struct =>
            ((ArchetypeColumn<T>)Chunks[0].Columns[columnIndex]).Values;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T[] GetColumn<T>(ArchetypeChunk chunk) where T : struct
        {
            var columnIndex = GetColumnIndex(ComponentType<T>.Id);
            return ((ArchetypeColumn<T>)chunk.Columns[columnIndex]).Values;
        }

        internal void CopySharedComponents(in EntityLocation source, Archetype destination,
            in EntityLocation target)
        {
            var sourceColumns = source.Chunk.Columns;
            var targetColumns = target.Chunk.Columns;
            var plan = GetOrCreateCopyPlan(destination);

            for (var i = 0; i < plan.Length; i++)
            {
                var copy = plan[i];
                sourceColumns[copy.Source].CopyTo(source.Row, targetColumns[copy.Destination], target.Row);
            }
        }

        internal void EnsureCopyPlan(Archetype destination) => GetOrCreateCopyPlan(destination);

        private ColumnCopy[] GetOrCreateCopyPlan(Archetype destination)
        {
            if (_copyPlans.TryGetValue(destination._index, out var plan)) return plan;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled) _allocationDiagnostics.CopyPlanCreations++;
#endif
            var copies = new List<ColumnCopy>(Math.Min(TypeIds.Length, destination.TypeIds.Length));
            for (var i = 0; i < TypeIds.Length; i++)
            {
                var targetColumn = destination.GetColumnIndexOrMissing(TypeIds[i]);
                if (targetColumn >= 0) copies.Add(new ColumnCopy(i, targetColumn));
            }
            plan = copies.ToArray();
            _copyPlans.Add(destination._index, plan);
            return plan;
        }

        /// <summary>Removes the entity at <paramref name="location"/>. Returns the entity index moved into the hole, or -1.</summary>
        internal int RemoveAt(in EntityLocation location)
        {
            var chunk = location.Chunk;
            var lastChunkIndex = (EntityCount - 1) / ComponentPageManager.PageCapacity;
            var lastChunk = Chunks[lastChunkIndex];
            var lastRow = lastChunk.Count - 1;
            var movedIndex = -1;

            if (!ReferenceEquals(chunk, lastChunk) || location.Row != lastRow)
            {
                movedIndex = lastChunk.EntityIds[lastRow];
                chunk.EntityIds[location.Row] = movedIndex;
                for (var i = 0; i < chunk.Columns.Length; i++)
                    lastChunk.Columns[i].CopyTo(lastRow, chunk.Columns[i], location.Row);
            }

            lastChunk.EntityIds[lastRow] = 0;
            for (var i = 0; i < _clearColumnIndices.Length; i++)
                lastChunk.Columns[_clearColumnIndices[i]].Clear(lastRow);
            lastChunk.Count = lastRow;
            EntityCount--;
            if (lastChunk.Count == 0 && lastChunkIndex >= _reservedChunkCount) ReturnLastChunk(lastChunk);
            return movedIndex;
        }

        private void ReturnLastChunk(ArchetypeChunk chunk)
        {
            for (var i = 0; i < TypeIds.Length; i++) _pages.ReturnColumn(TypeIds[i], chunk.Columns[i]);
            _pages.ReturnEntityPage(chunk.EntityIds);
            chunk.EntityIds = Array.Empty<int>();
        }

        internal void ClearAll()
        {
            for (var chunkIndex = Chunks.Count - 1; chunkIndex >= 0; chunkIndex--)
            {
                var chunk = Chunks[chunkIndex];
                if (chunk.EntityIds.Length == 0) continue;
                Array.Clear(chunk.EntityIds, 0, chunk.Count);
                for (var c = 0; c < chunk.Columns.Length; c++) chunk.Columns[c].ClearAll(chunk.Count);
                chunk.Count = 0;
                if (chunkIndex >= _reservedChunkCount) ReturnLastChunk(chunk);
            }
            EntityCount = 0;
        }
    }

    internal readonly struct RemoveManyTransition
    {
        internal readonly int[] TypeIds;
        internal readonly Archetype Destination;

        internal RemoveManyTransition(int[] typeIds, Archetype destination)
        {
            TypeIds = typeIds;
            Destination = destination;
        }

        internal bool Matches(ReadOnlySpan<int> typeIds)
        {
            if (TypeIds.Length != typeIds.Length) return false;
            for (var i = 0; i < TypeIds.Length; i++)
                if (TypeIds[i] != typeIds[i]) return false;
            return true;
        }
    }

    internal readonly struct AddManyTransition
    {
        internal readonly int[] TypeIds;
        internal readonly Archetype Destination;

        internal AddManyTransition(int[] typeIds, Archetype destination)
        {
            TypeIds = typeIds;
            Destination = destination;
        }

        internal bool Matches(ReadOnlySpan<int> typeIds)
        {
            if (TypeIds.Length != typeIds.Length) return false;
            for (var i = 0; i < TypeIds.Length; i++)
                if (TypeIds[i] != typeIds[i]) return false;
            return true;
        }
    }

    internal readonly struct AddManyKey : IEquatable<AddManyKey>
    {
        private readonly int _count;
        private readonly int _a, _b, _c, _d, _e;
        private readonly int _hash;

        internal AddManyKey(ReadOnlySpan<int> sortedUniqueTypeIds)
        {
            _count = sortedUniqueTypeIds.Length;
            _a = _count > 0 ? sortedUniqueTypeIds[0] : 0;
            _b = _count > 1 ? sortedUniqueTypeIds[1] : 0;
            _c = _count > 2 ? sortedUniqueTypeIds[2] : 0;
            _d = _count > 3 ? sortedUniqueTypeIds[3] : 0;
            _e = _count > 4 ? sortedUniqueTypeIds[4] : 0;
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _count;
                hash = hash * 31 + _a;
                hash = hash * 31 + _b;
                hash = hash * 31 + _c;
                hash = hash * 31 + _d;
                hash = hash * 31 + _e;
                _hash = hash;
            }
        }

        public bool Equals(AddManyKey other) =>
            _count == other._count && _a == other._a && _b == other._b && _c == other._c && _d == other._d &&
            _e == other._e;

        public override bool Equals(object? obj) => obj is AddManyKey other && Equals(other);
        public override int GetHashCode() => _hash;
    }

    internal sealed class ArchetypeCatalog
    {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        private readonly AllocationDiagnostics _allocationDiagnostics;
#endif
        internal readonly ComponentPageManager Pages;
        private readonly Dictionary<ArchetypeKey, Archetype> _byKey =
            new Dictionary<ArchetypeKey, Archetype>();
        private List<Archetype>? _reservedDestinations;
        internal Action<Archetype>? ArchetypeCreated;
        internal Action<Archetype, Archetype>? TransitionCreated;
        internal readonly List<Archetype> All = new List<Archetype>();
        internal int Version { get; private set; }

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        internal ArchetypeCatalog(AllocationDiagnostics allocationDiagnostics)
#else
        internal ArchetypeCatalog()
#endif
        {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            _allocationDiagnostics = allocationDiagnostics;
            Pages = new ComponentPageManager(allocationDiagnostics);
#else
            Pages = new ComponentPageManager();
#endif
            GetOrCreate(Array.Empty<int>());
        }

        internal Archetype Empty => All[0];

        internal Archetype GetOrCreate(int[] sortedTypeIds)
        {
            // The lookup key is used only for this dictionary probe. Cloning here made
            // every uncached transition allocate a second type-id array, even when the
            // destination archetype already existed. The dictionary stores a separate
            // owned key below when a new archetype is actually created.
            var lookup = new ArchetypeKey(sortedTypeIds);
            if (_byKey.TryGetValue(lookup, out var existing)) return existing;

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled) _allocationDiagnostics.ArchetypeCreations++;
#endif
            var ownedTypeIds = (int[])sortedTypeIds.Clone();
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            var archetype = new Archetype(All.Count, ownedTypeIds, Pages, _allocationDiagnostics);
#else
            var archetype = new Archetype(All.Count, ownedTypeIds, Pages);
#endif
            All.Add(archetype);
            _byKey.Add(new ArchetypeKey(ownedTypeIds), archetype);
            Version++;
            WarmReservedRemoveTransitionsFrom(archetype);
            ArchetypeCreated?.Invoke(archetype);
            return archetype;
        }

        internal Archetype With(Archetype source, int typeId)
        {
            if (source.Has(typeId)) return source;
            if (source.AddTransitions.TryGetValue(typeId, out var cached)) return cached;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled) _allocationDiagnostics.TransitionCreations++;
#endif
            var sourceIds = source.TypeIds;
            var result = new int[sourceIds.Length + 1];
            var sourceIndex = 0;
            var targetIndex = 0;
            while (sourceIndex < sourceIds.Length && sourceIds[sourceIndex] < typeId)
                result[targetIndex++] = sourceIds[sourceIndex++];
            result[targetIndex++] = typeId;
            while (sourceIndex < sourceIds.Length) result[targetIndex++] = sourceIds[sourceIndex++];
            var destination = GetOrCreate(result);
            // GetOrCreate can warm the reverse removal transition and populate this key.
            source.AddTransitions[typeId] = destination;
            destination.RemoveTransitions[typeId] = source;
            TransitionCreated?.Invoke(source, destination);
            return destination;
        }

        /// <summary>
        /// Adds multiple component types in one structural transition (Friflo GetArchetypeAdd style).
        /// <paramref name="typeIds"/> need not be sorted; duplicates are ignored.
        /// </summary>
        internal Archetype WithMany(Archetype source, ReadOnlySpan<int> typeIds)
        {
            if (typeIds.Length == 0) return source;
            if (typeIds.Length == 1) return With(source, typeIds[0]);

            Span<int> unique = stackalloc int[typeIds.Length];
            var uniqueCount = 0;
            for (var i = 0; i < typeIds.Length; i++)
            {
                var typeId = typeIds[i];
                if (source.Has(typeId)) continue;
                var insert = uniqueCount;
                while (insert > 0 && unique[insert - 1] > typeId) insert--;
                if ((insert > 0 && unique[insert - 1] == typeId)
                    || (insert < uniqueCount && unique[insert] == typeId)) continue;
                for (var shift = uniqueCount; shift > insert; shift--) unique[shift] = unique[shift - 1];
                unique[insert] = typeId;
                uniqueCount++;
            }

            if (uniqueCount == 0) return source;
            if (uniqueCount == 1) return With(source, unique[0]);
            // AddManyKey stores up to five IDs. Larger transitions still merge directly so a
            // structural batch never creates intermediate Archetypes merely to populate a cache.
            var cacheable = uniqueCount <= 5;
            var key = cacheable ? new AddManyKey(unique.Slice(0, uniqueCount)) : default;
            var transitions = source.AddManyTransitions;
            if (cacheable && transitions != null && transitions.TryGetValue(key, out var cached)) return cached;
            var normalized = unique.Slice(0, uniqueCount);
            if (!cacheable && source.LargeAddManyTransitions != null)
                for (var i = 0; i < source.LargeAddManyTransitions.Count; i++)
                    if (source.LargeAddManyTransitions[i].Matches(normalized))
                        return source.LargeAddManyTransitions[i].Destination;

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled) _allocationDiagnostics.TransitionCreations++;
#endif

            var sourceIds = source.TypeIds;
            var merged = new int[sourceIds.Length + uniqueCount];
            var sourceIndex = 0;
            var uniqueIndex = 0;
            var mergedIndex = 0;
            while (sourceIndex < sourceIds.Length && uniqueIndex < uniqueCount)
            {
                if (sourceIds[sourceIndex] < unique[uniqueIndex])
                    merged[mergedIndex++] = sourceIds[sourceIndex++];
                else
                    merged[mergedIndex++] = unique[uniqueIndex++];
            }
            while (sourceIndex < sourceIds.Length) merged[mergedIndex++] = sourceIds[sourceIndex++];
            while (uniqueIndex < uniqueCount) merged[mergedIndex++] = unique[uniqueIndex++];

            var result = GetOrCreate(merged);
            if (cacheable)
                (source.AddManyTransitions ??= new Dictionary<AddManyKey, Archetype>()).Add(key, result);
            else
                (source.LargeAddManyTransitions ??= new List<AddManyTransition>())
                    .Add(new AddManyTransition(normalized.ToArray(), result));
            TransitionCreated?.Invoke(source, result);
            return result;
        }

        internal Archetype Without(Archetype source, int typeId)
        {
            if (!source.Has(typeId)) return source;
            if (source.RemoveTransitions.TryGetValue(typeId, out var cached)) return cached;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled) _allocationDiagnostics.TransitionCreations++;
#endif
            var result = new int[source.TypeIds.Length - 1];
            var targetIndex = 0;
            for (var i = 0; i < source.TypeIds.Length; i++)
                if (source.TypeIds[i] != typeId) result[targetIndex++] = source.TypeIds[i];
            var destination = GetOrCreate(result);
            // GetOrCreate can warm a reserved destination while this transition is being built.
            source.RemoveTransitions[typeId] = destination;
            destination.AddTransitions[typeId] = source;
            TransitionCreated?.Invoke(source, destination);
            return destination;
        }

        /// <summary>
        /// Removes multiple component types in one structural transition. Input need not be sorted;
        /// types absent from <paramref name="source"/> and duplicates are ignored.
        /// </summary>
        internal Archetype WithoutMany(Archetype source, ReadOnlySpan<int> typeIds)
        {
            if (typeIds.Length == 0) return source;
            if (typeIds.Length == 1) return Without(source, typeIds[0]);

            Span<int> unique = typeIds.Length <= 64
                ? stackalloc int[typeIds.Length]
                : new int[typeIds.Length];
            var uniqueCount = 0;
            for (var i = 0; i < typeIds.Length; i++)
            {
                var typeId = typeIds[i];
                if (!source.Has(typeId)) continue;
                var insert = uniqueCount;
                while (insert > 0 && unique[insert - 1] > typeId) insert--;
                if ((insert > 0 && unique[insert - 1] == typeId)
                    || (insert < uniqueCount && unique[insert] == typeId)) continue;
                for (var shift = uniqueCount; shift > insert; shift--) unique[shift] = unique[shift - 1];
                unique[insert] = typeId;
                uniqueCount++;
            }

            if (uniqueCount == 0) return source;
            if (uniqueCount == 1) return Without(source, unique[0]);

            var normalized = unique.Slice(0, uniqueCount);
            var transitions = source.RemoveManyTransitions;
            if (transitions != null)
                for (var i = 0; i < transitions.Count; i++)
                    if (transitions[i].Matches(normalized)) return transitions[i].Destination;

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            if (_allocationDiagnostics.Enabled) _allocationDiagnostics.TransitionCreations++;
#endif

            var sourceIds = source.TypeIds;
            var remaining = new int[sourceIds.Length - uniqueCount];
            var sourceIndex = 0;
            var removeIndex = 0;
            var remainingIndex = 0;
            while (sourceIndex < sourceIds.Length)
            {
                if (removeIndex < uniqueCount && sourceIds[sourceIndex] == normalized[removeIndex])
                {
                    sourceIndex++;
                    removeIndex++;
                }
                else
                {
                    remaining[remainingIndex++] = sourceIds[sourceIndex++];
                }
            }

            var destination = GetOrCreate(remaining);
            source.EnsureCopyPlan(destination);
            var ownedTypeIds = normalized.ToArray();
            (source.RemoveManyTransitions ??= new List<RemoveManyTransition>())
                .Add(new RemoveManyTransition(ownedTypeIds, destination));
            TransitionCreated?.Invoke(source, destination);
            return destination;
        }

        internal void WarmAddTransitionsTo(Archetype destination)
        {
            var sourceCount = All.Count;
            // Empty is a real transition source for newly spawned, component-less entities.
            // Reserving a layout must warm that path as well as transitions from populated layouts.
            for (var sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
            {
                var source = All[sourceIndex];
                if (ReferenceEquals(source, destination) || source.TypeIds.Length >= destination.TypeIds.Length)
                    continue;
                if (!IsSubset(source.TypeIds, destination.TypeIds)) continue;

                var added = new int[destination.TypeIds.Length - source.TypeIds.Length];
                var addedCount = 0;
                for (var i = 0; i < destination.TypeIds.Length; i++)
                    if (!source.Has(destination.TypeIds[i])) added[addedCount++] = destination.TypeIds[i];
                if (!ReferenceEquals(WithMany(source, added.AsSpan(0, addedCount)), destination)) continue;
                source.EnsureCopyPlan(destination);
            }
        }

        /// <summary>
        /// Marks an explicitly reserved Archetype as a warm destination. Incoming multi-component removal
        /// transitions are prepared both from Archetypes that already exist and from ones created later.
        /// </summary>
        internal void WarmRemoveTransitionsTo(Archetype destination)
        {
            var reservedDestinations = _reservedDestinations ??= new List<Archetype>();
            if (!reservedDestinations.Contains(destination)) reservedDestinations.Add(destination);
            var sourceCount = All.Count;
            for (var sourceIndex = 1; sourceIndex < sourceCount; sourceIndex++)
                WarmRemoveTransition(All[sourceIndex], destination);
        }

        private void WarmReservedRemoveTransitionsFrom(Archetype source)
        {
            var reservedDestinations = _reservedDestinations;
            if (reservedDestinations == null) return;
            for (var i = 0; i < reservedDestinations.Count; i++)
                WarmRemoveTransition(source, reservedDestinations[i]);
        }

        private static void WarmRemoveTransition(Archetype source, Archetype destination)
        {
            if (ReferenceEquals(source, destination)
                || source.TypeIds.Length <= destination.TypeIds.Length
                || !IsSubset(destination.TypeIds, source.TypeIds)) return;

            var removeCount = source.TypeIds.Length - destination.TypeIds.Length;
            if (removeCount == 1)
            {
                var removedTypeId = -1;
                for (var i = 0; i < source.TypeIds.Length; i++)
                {
                    var typeId = source.TypeIds[i];
                    if (!destination.Has(typeId))
                    {
                        removedTypeId = typeId;
                        break;
                    }
                }

                source.RemoveTransitions[removedTypeId] = destination;
                destination.AddTransitions[removedTypeId] = source;
                source.EnsureCopyPlan(destination);
                return;
            }

            var removed = new int[removeCount];
            var removedIndex = 0;
            for (var i = 0; i < source.TypeIds.Length; i++)
                if (!destination.Has(source.TypeIds[i])) removed[removedIndex++] = source.TypeIds[i];

            var transitions = source.RemoveManyTransitions;
            if (transitions != null)
                for (var i = 0; i < transitions.Count; i++)
                    if (transitions[i].Matches(removed)) return;

            source.EnsureCopyPlan(destination);
            (source.RemoveManyTransitions ??= new List<RemoveManyTransition>())
                .Add(new RemoveManyTransition(removed, destination));
        }

        private static bool IsSubset(int[] subset, int[] superset)
        {
            var left = 0;
            var right = 0;
            while (left < subset.Length && right < superset.Length)
            {
                if (subset[left] == superset[right]) { left++; right++; }
                else if (subset[left] > superset[right]) right++;
                else return false;
            }
            return left == subset.Length;
        }
    }

    internal sealed class ArchetypeQueryPlan
    {
        private readonly ArchetypeCatalog _catalog;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        private readonly AllocationDiagnostics _allocationDiagnostics;
#endif
        private readonly int[] _required;
        private readonly int[] _excluded;
        private readonly int[] _any;
        private int _catalogVersion;
        private int _scannedArchetypeCount;
        internal readonly List<Archetype> Matches = new List<Archetype>();
        internal ParallelQueryJob? ParallelRangeJob;
        internal int[] Required => _required;
        internal int[] Excluded => _excluded;
        internal int[] Any => _any;

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        internal ArchetypeQueryPlan(ArchetypeCatalog catalog, AllocationDiagnostics allocationDiagnostics,
            int[] required,
#else
        internal ArchetypeQueryPlan(ArchetypeCatalog catalog, int[] required,
#endif
            int[]? excluded = null, int[]? any = null)
        {
            _catalog = catalog;
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
            _allocationDiagnostics = allocationDiagnostics;
#endif
            _required = required;
            _excluded = excluded ?? Array.Empty<int>();
            _any = any ?? Array.Empty<int>();
        }

        internal void Ensure()
        {
            if (_catalogVersion == _catalog.Version) return;
            var archetypes = _catalog.All;
            for (var i = _scannedArchetypeCount; i < archetypes.Count; i++)
            {
                var archetype = archetypes[i];
                if (archetype.ContainsAll(_required)
                    && archetype.ContainsNone(_excluded)
                    && (_any.Length == 0 || archetype.Intersects(_any)))
                {
#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
                    if (_allocationDiagnostics.Enabled && Matches.Count == Matches.Capacity)
                        _allocationDiagnostics.QueryMatchListGrowths++;
#endif
                    Matches.Add(archetype);
                }
            }

            _scannedArchetypeCount = archetypes.Count;
            _catalogVersion = _catalog.Version;
        }

        internal bool IsMatch(Archetype archetype) =>
            archetype.ContainsAll(_required)
            && archetype.ContainsNone(_excluded)
            && (_any.Length == 0 || archetype.Intersects(_any));
    }

    internal static class ComponentTypeIdList
    {
        internal static int[] Add(int[] source, int typeId)
        {
            var index = Array.BinarySearch(source, typeId);
            if (index >= 0) return source;
            index = ~index;
            var result = new int[source.Length + 1];
            if (index > 0) Array.Copy(source, 0, result, 0, index);
            result[index] = typeId;
            if (index < source.Length) Array.Copy(source, index, result, index + 1, source.Length - index);
            return result;
        }
    }
}
