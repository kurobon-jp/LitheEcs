using System;
using System.Buffers;
using LitheEcs;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace LitheEcs.Unity.Jobs
{
    public readonly struct BurstQuery<T1> where T1 : unmanaged
    {
        internal readonly JobQuery<T1> Source;
        public int BatchSize { get; }

        internal BurstQuery(Query<T1> source, int batchSize)
        {
            if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
            Source = source.AsJobQuery();
            BatchSize = batchSize;
        }

        public void Reserve(int maximumEntityCount) => Source.ReserveBurstUnsafe(maximumEntityCount, BatchSize);
        public void Run<TAction>(ref TAction action) where TAction : unmanaged, IBurstQueryAction<T1> =>
            Source.RunBurst(ref action, BatchSize);
        /// <summary>Runs synchronously through direct pointers without NativeContainer safety checks.</summary>
        /// <remarks>Do not access the same Component columns concurrently or retain pointers or JobHandles outside this call. Use <see cref="Run{TAction}"/> when safety checks are required.</remarks>
        public void RunUnsafe<TAction>(ref TAction action) where TAction : unmanaged, IBurstQueryAction<T1> =>
            Source.RunBurstUnsafe(ref action, BatchSize);
    }

    public readonly struct BurstQuery<T1, T2> where T1 : unmanaged where T2 : unmanaged
    {
        internal readonly JobQuery<T1, T2> Source;
        public int BatchSize { get; }

        internal BurstQuery(Query<T1, T2> source, int batchSize)
        {
            if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
            Source = source.AsJobQuery();
            BatchSize = batchSize;
        }

        public void Reserve(int maximumEntityCount) => Source.ReserveBurstUnsafe(maximumEntityCount, BatchSize);
        public void Run<TAction>(ref TAction action) where TAction : unmanaged, IBurstQueryAction<T1, T2> =>
            Source.RunBurst(ref action, BatchSize);
        /// <summary>Runs synchronously through direct pointers without NativeContainer safety checks.</summary>
        /// <remarks>Do not access the same Component columns concurrently or retain pointers or JobHandles outside this call. Use <see cref="Run{TAction}"/> when safety checks are required.</remarks>
        public void RunUnsafe<TAction>(ref TAction action) where TAction : unmanaged, IBurstQueryAction<T1, T2> =>
            Source.RunBurstUnsafe(ref action, BatchSize);
    }

    public readonly struct BurstQuery<T1, T2, T3>
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    {
        internal readonly JobQuery<T1, T2, T3> Source;
        public int BatchSize { get; }

        internal BurstQuery(Query<T1, T2, T3> source, int batchSize)
        {
            if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
            Source = source.AsJobQuery();
            BatchSize = batchSize;
        }

        public void Reserve(int maximumEntityCount) => Source.ReserveBurstUnsafe(maximumEntityCount, BatchSize);
        public void Run<TAction>(ref TAction action) where TAction : unmanaged, IBurstQueryAction<T1, T2, T3> =>
            Source.RunBurst(ref action, BatchSize);
        /// <summary>Runs synchronously through direct pointers without NativeContainer safety checks.</summary>
        /// <remarks>Do not access the same Component columns concurrently or retain pointers or JobHandles outside this call. Use <see cref="Run{TAction}"/> when safety checks are required.</remarks>
        public void RunUnsafe<TAction>(ref TAction action)
            where TAction : unmanaged, IBurstQueryAction<T1, T2, T3> =>
            Source.RunBurstUnsafe(ref action, BatchSize);
    }

    public interface IBurstQueryAction<T1> where T1 : unmanaged
    {
        void Execute(int index, ref T1 component1);
    }

    public interface IBurstQueryAction<T1, T2> where T1 : unmanaged where T2 : unmanaged
    {
        void Execute(int index, ref T1 component1, ref T2 component2);
    }

    public interface IBurstQueryAction<T1, T2, T3>
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    {
        void Execute(int index, ref T1 component1, ref T2 component2, ref T3 component3);
    }

    public static class BurstQueryExtensions
    {
        public static BurstQuery<T1> AsBurstQuery<T1>(this Query<T1> query, int batchSize = 128)
            where T1 : unmanaged => new BurstQuery<T1>(query, batchSize);

        public static BurstQuery<T1, T2> AsBurstQuery<T1, T2>(this Query<T1, T2> query, int batchSize = 128)
            where T1 : unmanaged where T2 : unmanaged => new BurstQuery<T1, T2>(query, batchSize);

        public static BurstQuery<T1, T2, T3> AsBurstQuery<T1, T2, T3>(this Query<T1, T2, T3> query,
            int batchSize = 128)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged =>
            new BurstQuery<T1, T2, T3>(query, batchSize);

        public static unsafe void RunBurst<TAction, T1>(this JobQuery<T1> query, ref TAction action,
            int innerLoopBatchCount = 128)
            where TAction : unmanaged, IBurstQueryAction<T1>
            where T1 : unmanaged
        {
            ValidateBatchCount(innerLoopBatchCount);
            using var ranges = query.AcquireRanges();
            SafeBuffers<T1>.EnsureCapacity(ranges.RangeCount);
            var scheduled = 0;
            var hasDependency = false;
            var dependency = default(JobHandle);
            try
            {
                for (var i = 0; i < ranges.RangeCount; i++)
                {
                    var range = ranges.GetRange(i);
                    if (range.Length == 0) continue;
                    SafeBuffers<T1>.Pins1[i] = range.Components1.Pin();
                    SafeBuffers<T1>.Pinned1[i] = true;
                    SafeBuffers<T1>.Views1[i] = PinnedNativeArrayView.Create<T1>(
                        SafeBuffers<T1>.Pins1[i].Pointer, range.Length);
                    var handle = new BurstRangeJob<TAction, T1>
                        { Action = action, Components1 = SafeBuffers<T1>.Views1[i] }
                        .Schedule(range.Length, innerLoopBatchCount);
                    SafeBuffers<T1>.Handles[scheduled++] = handle;
                    dependency = hasDependency ? JobHandle.CombineDependencies(dependency, handle) : handle;
                    hasDependency = true;
                }
                if (hasDependency) dependency.Complete();
            }
            catch
            {
                CompleteScheduledJobs(SafeBuffers<T1>.Handles, scheduled);
                throw;
            }
            finally
            {
                SafeBuffers<T1>.Release(ranges.RangeCount);
            }
        }

        public static void RunBurst<TAction, T1, T2>(this JobQuery<T1, T2> query, ref TAction action,
            int innerLoopBatchCount = 128)
            where TAction : unmanaged, IBurstQueryAction<T1, T2>
            where T1 : unmanaged where T2 : unmanaged
        {
            ValidateBatchCount(innerLoopBatchCount);
            using var ranges = query.AcquireRanges();
            SafeBuffers<T1, T2>.EnsureCapacity(ranges.RangeCount);
            var scheduled = 0;
            var hasDependency = false;
            var dependency = default(JobHandle);
            try
            {
                for (var i = 0; i < ranges.RangeCount; i++)
                {
                    var range = ranges.GetRange(i);
                    if (range.Length == 0) continue;
                    SafeBuffers<T1, T2>.Pin(i, range);
                    var handle = new BurstRangeJob<TAction, T1, T2>
                    {
                        Action = action,
                        Components1 = SafeBuffers<T1, T2>.Views1[i],
                        Components2 = SafeBuffers<T1, T2>.Views2[i]
                    }.Schedule(range.Length, innerLoopBatchCount);
                    SafeBuffers<T1, T2>.Handles[scheduled++] = handle;
                    dependency = hasDependency ? JobHandle.CombineDependencies(dependency, handle) : handle;
                    hasDependency = true;
                }
                if (hasDependency) dependency.Complete();
            }
            catch
            {
                CompleteScheduledJobs(SafeBuffers<T1, T2>.Handles, scheduled);
                throw;
            }
            finally
            {
                SafeBuffers<T1, T2>.Release(ranges.RangeCount);
            }
        }

        public static void RunBurst<TAction, T1, T2, T3>(this JobQuery<T1, T2, T3> query,
            ref TAction action, int innerLoopBatchCount = 128)
            where TAction : unmanaged, IBurstQueryAction<T1, T2, T3>
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            ValidateBatchCount(innerLoopBatchCount);
            using var ranges = query.AcquireRanges();
            SafeBuffers<T1, T2, T3>.EnsureCapacity(ranges.RangeCount);
            var scheduled = 0;
            var hasDependency = false;
            var dependency = default(JobHandle);
            try
            {
                for (var i = 0; i < ranges.RangeCount; i++)
                {
                    var range = ranges.GetRange(i);
                    if (range.Length == 0) continue;
                    SafeBuffers<T1, T2, T3>.Pin(i, range);
                    var handle = new BurstRangeJob<TAction, T1, T2, T3>
                    {
                        Action = action,
                        Components1 = SafeBuffers<T1, T2, T3>.Views1[i],
                        Components2 = SafeBuffers<T1, T2, T3>.Views2[i],
                        Components3 = SafeBuffers<T1, T2, T3>.Views3[i]
                    }.Schedule(range.Length, innerLoopBatchCount);
                    SafeBuffers<T1, T2, T3>.Handles[scheduled++] = handle;
                    dependency = hasDependency ? JobHandle.CombineDependencies(dependency, handle) : handle;
                    hasDependency = true;
                }
                if (hasDependency) dependency.Complete();
            }
            catch
            {
                CompleteScheduledJobs(SafeBuffers<T1, T2, T3>.Handles, scheduled);
                throw;
            }
            finally
            {
                SafeBuffers<T1, T2, T3>.Release(ranges.RangeCount);
            }
        }

        /// <summary>
        /// Preallocates the thread-local pin and work-item buffers used by RunBurstUnsafe for the
        /// maximum matching entity count. Register matching Archetypes before calling this method,
        /// and call it on the same thread that executes RunBurstUnsafe.
        /// </summary>
        public static void ReserveBurstUnsafe<T1>(this JobQuery<T1> query, int maximumEntityCount,
            int innerLoopBatchCount = 128)
            where T1 : unmanaged
        {
            ValidateBatchCount(innerLoopBatchCount);
            query.Source.GetJobRangeReservationCounts(maximumEntityCount, innerLoopBatchCount,
                out var rangeCount, out var workCount);
            UnsafeBuffers<T1>.EnsureCapacity(rangeCount, workCount);
        }

        public static void ReserveBurstUnsafe<T1, T2>(this JobQuery<T1, T2> query,
            int maximumEntityCount, int innerLoopBatchCount = 128)
            where T1 : unmanaged where T2 : unmanaged
        {
            ValidateBatchCount(innerLoopBatchCount);
            query.Source.GetJobRangeReservationCounts(maximumEntityCount, innerLoopBatchCount,
                out var rangeCount, out var workCount);
            UnsafeBuffers<T1, T2>.EnsureCapacity(rangeCount, workCount);
        }

        public static void ReserveBurstUnsafe<T1, T2, T3>(this JobQuery<T1, T2, T3> query,
            int maximumEntityCount, int innerLoopBatchCount = 128)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            ValidateBatchCount(innerLoopBatchCount);
            query.Source.GetJobRangeReservationCounts(maximumEntityCount, innerLoopBatchCount,
                out var rangeCount, out var workCount);
            UnsafeBuffers<T1, T2, T3>.EnsureCapacity(rangeCount, workCount);
        }

        /// <summary>Runs synchronously through direct pointers without NativeContainer bounds, alias, or dependency checks.</summary>
        /// <remarks>Do not access the same Component columns concurrently or retain pointers or JobHandles outside this call. Use <see cref="RunBurst{TAction,T1}(JobQuery{T1},ref TAction,int)"/> when safety checks are required.</remarks>
        public static unsafe void RunBurstUnsafe<TAction, T1>(this JobQuery<T1> query, ref TAction action,
            int innerLoopBatchCount = 128)
            where TAction : unmanaged, IBurstQueryAction<T1>
            where T1 : unmanaged
        {
            ValidateBatchCount(innerLoopBatchCount);
            using var ranges = query.AcquireRanges();
            var workCount = CountWorkItems(ranges, innerLoopBatchCount);
            if (workCount == 0) return;
            UnsafeBuffers<T1>.EnsureCapacity(ranges.RangeCount, workCount);
            var destination = 0;
            try
            {
                for (var i = 0; i < ranges.RangeCount; i++)
                {
                    var range = ranges.GetRange(i);
                    if (range.Length == 0) continue;
                    UnsafeBuffers<T1>.Pins1[i] = range.Components1.Pin();
                    UnsafeBuffers<T1>.Pinned1[i] = true;
                    var pointer1 = (T1*)UnsafeBuffers<T1>.Pins1[i].Pointer;
                    for (var start = 0; start < range.Length; start += innerLoopBatchCount)
                        UnsafeBuffers<T1>.Work[destination++] = new PointerWork<T1>(pointer1, start,
                            Math.Min(start + innerLoopBatchCount, range.Length));
                }
                fixed (PointerWork<T1>* work = UnsafeBuffers<T1>.Work)
                    new BurstPointerBatchJob<TAction, T1> { Action = action, Work = work }
                        .Schedule(destination, 1).Complete();
            }
            finally
            {
                UnsafeBuffers<T1>.Release(ranges.RangeCount);
            }
        }

        /// <summary>Runs synchronously through direct pointers without NativeContainer bounds, alias, or dependency checks.</summary>
        /// <remarks>Do not access the same Component columns concurrently or retain pointers or JobHandles outside this call. Use <see cref="RunBurst{TAction,T1,T2}(JobQuery{T1,T2},ref TAction,int)"/> when safety checks are required.</remarks>
        public static unsafe void RunBurstUnsafe<TAction, T1, T2>(this JobQuery<T1, T2> query, ref TAction action,
            int innerLoopBatchCount = 128)
            where TAction : unmanaged, IBurstQueryAction<T1, T2>
            where T1 : unmanaged where T2 : unmanaged
        {
            ValidateBatchCount(innerLoopBatchCount);
            using var ranges = query.AcquireRanges();
            var workCount = CountWorkItems(ranges, innerLoopBatchCount);
            if (workCount == 0) return;
            UnsafeBuffers<T1, T2>.EnsureCapacity(ranges.RangeCount, workCount);
            var destination = 0;
            try
            {
                for (var i = 0; i < ranges.RangeCount; i++)
                {
                    var range = ranges.GetRange(i);
                    if (range.Length == 0) continue;
                    UnsafeBuffers<T1, T2>.Pin(i, range);
                    var pointer1 = (T1*)UnsafeBuffers<T1, T2>.Pins1[i].Pointer;
                    var pointer2 = (T2*)UnsafeBuffers<T1, T2>.Pins2[i].Pointer;
                    for (var start = 0; start < range.Length; start += innerLoopBatchCount)
                        UnsafeBuffers<T1, T2>.Work[destination++] = new PointerWork<T1, T2>(pointer1, pointer2,
                            start, Math.Min(start + innerLoopBatchCount, range.Length));
                }
                fixed (PointerWork<T1, T2>* work = UnsafeBuffers<T1, T2>.Work)
                    new BurstPointerBatchJob<TAction, T1, T2> { Action = action, Work = work }
                        .Schedule(destination, 1).Complete();
            }
            finally
            {
                UnsafeBuffers<T1, T2>.Release(ranges.RangeCount);
            }
        }

        /// <summary>Runs synchronously through direct pointers without NativeContainer bounds, alias, or dependency checks.</summary>
        /// <remarks>Do not access the same Component columns concurrently or retain pointers or JobHandles outside this call. Use <see cref="RunBurst{TAction,T1,T2,T3}(JobQuery{T1,T2,T3},ref TAction,int)"/> when safety checks are required.</remarks>
        public static unsafe void RunBurstUnsafe<TAction, T1, T2, T3>(this JobQuery<T1, T2, T3> query,
            ref TAction action, int innerLoopBatchCount = 128)
            where TAction : unmanaged, IBurstQueryAction<T1, T2, T3>
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            ValidateBatchCount(innerLoopBatchCount);
            using var ranges = query.AcquireRanges();
            var workCount = CountWorkItems(ranges, innerLoopBatchCount);
            if (workCount == 0) return;
            UnsafeBuffers<T1, T2, T3>.EnsureCapacity(ranges.RangeCount, workCount);
            var destination = 0;
            try
            {
                for (var i = 0; i < ranges.RangeCount; i++)
                {
                    var range = ranges.GetRange(i);
                    if (range.Length == 0) continue;
                    UnsafeBuffers<T1, T2, T3>.Pin(i, range);
                    var pointer1 = (T1*)UnsafeBuffers<T1, T2, T3>.Pins1[i].Pointer;
                    var pointer2 = (T2*)UnsafeBuffers<T1, T2, T3>.Pins2[i].Pointer;
                    var pointer3 = (T3*)UnsafeBuffers<T1, T2, T3>.Pins3[i].Pointer;
                    for (var start = 0; start < range.Length; start += innerLoopBatchCount)
                        UnsafeBuffers<T1, T2, T3>.Work[destination++] = new PointerWork<T1, T2, T3>(
                            pointer1, pointer2, pointer3, start,
                            Math.Min(start + innerLoopBatchCount, range.Length));
                }
                fixed (PointerWork<T1, T2, T3>* work = UnsafeBuffers<T1, T2, T3>.Work)
                    new BurstPointerBatchJob<TAction, T1, T2, T3> { Action = action, Work = work }
                        .Schedule(destination, 1).Complete();
            }
            finally
            {
                UnsafeBuffers<T1, T2, T3>.Release(ranges.RangeCount);
            }
        }

        private static void ValidateBatchCount(int value)
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
        }

        private static void CompleteScheduledJobs(JobHandle[] handles, int count)
        {
            for (var i = 0; i < count; i++)
                try { handles[i].Complete(); }
                catch { /* Preserve the original schedule/complete exception. */ }
        }

        private static int CountWorkItems<T1>(JobQueryRangeLease<T1> ranges, int batchCount) where T1 : struct
        {
            var count = 0;
            for (var i = 0; i < ranges.RangeCount; i++)
                count += (ranges.GetRange(i).Length + batchCount - 1) / batchCount;
            return count;
        }

        private static int CountWorkItems<T1, T2>(JobQueryRangeLease<T1, T2> ranges, int batchCount)
            where T1 : struct where T2 : struct
        {
            var count = 0;
            for (var i = 0; i < ranges.RangeCount; i++)
                count += (ranges.GetRange(i).Length + batchCount - 1) / batchCount;
            return count;
        }

        private static int CountWorkItems<T1, T2, T3>(JobQueryRangeLease<T1, T2, T3> ranges, int batchCount)
            where T1 : struct where T2 : struct where T3 : struct
        {
            var count = 0;
            for (var i = 0; i < ranges.RangeCount; i++)
                count += (ranges.GetRange(i).Length + batchCount - 1) / batchCount;
            return count;
        }
    }

    internal static unsafe class PinnedNativeArrayView
    {
        internal static NativeArray<T> Create<T>(void* pointer, int length) where T : unmanaged
        {
            var view = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(pointer, length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref view, AtomicSafetyHandle.Create());
#endif
            return view;
        }

        internal static void Release<T>(ref NativeArray<T> view) where T : unmanaged
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (view.IsCreated)
                AtomicSafetyHandle.Release(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(view));
#endif
            view = default;
        }
    }

    internal static class SafeBuffers<T1> where T1 : unmanaged
    {
        [ThreadStatic] internal static MemoryHandle[] Pins1 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static bool[] Pinned1 = Array.Empty<bool>();
        [ThreadStatic] internal static NativeArray<T1>[] Views1 = Array.Empty<NativeArray<T1>>();
        [ThreadStatic] internal static JobHandle[] Handles = Array.Empty<JobHandle>();

        internal static void EnsureCapacity(int count)
        {
            Pins1 ??= Array.Empty<MemoryHandle>(); Pinned1 ??= Array.Empty<bool>();
            Views1 ??= Array.Empty<NativeArray<T1>>(); Handles ??= Array.Empty<JobHandle>();
            if (Pins1.Length >= count) return;
            var capacity = NextCapacity(Pins1.Length, count);
            Array.Resize(ref Pins1, capacity); Array.Resize(ref Pinned1, capacity);
            Array.Resize(ref Views1, capacity); Array.Resize(ref Handles, capacity);
        }

        internal static void Release(int count)
        {
            for (var i = 0; i < count; i++)
            {
                PinnedNativeArrayView.Release(ref Views1[i]);
                if (Pinned1[i]) { Pins1[i].Dispose(); Pinned1[i] = false; }
            }
        }

        internal static int NextCapacity(int current, int required) =>
            Math.Max(required, current == 0 ? 4 : current * 2);
    }

    internal static class SafeBuffers<T1, T2> where T1 : unmanaged where T2 : unmanaged
    {
        [ThreadStatic] internal static MemoryHandle[] Pins1 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static MemoryHandle[] Pins2 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static bool[] Pinned1 = Array.Empty<bool>();
        [ThreadStatic] internal static bool[] Pinned2 = Array.Empty<bool>();
        [ThreadStatic] internal static NativeArray<T1>[] Views1 = Array.Empty<NativeArray<T1>>();
        [ThreadStatic] internal static NativeArray<T2>[] Views2 = Array.Empty<NativeArray<T2>>();
        [ThreadStatic] internal static JobHandle[] Handles = Array.Empty<JobHandle>();

        internal static void EnsureCapacity(int count)
        {
            Pins1 ??= Array.Empty<MemoryHandle>(); Pins2 ??= Array.Empty<MemoryHandle>();
            Pinned1 ??= Array.Empty<bool>(); Pinned2 ??= Array.Empty<bool>();
            Views1 ??= Array.Empty<NativeArray<T1>>(); Views2 ??= Array.Empty<NativeArray<T2>>();
            Handles ??= Array.Empty<JobHandle>();
            if (Pins1.Length >= count) return;
            var capacity = SafeBuffers<T1>.NextCapacity(Pins1.Length, count);
            Array.Resize(ref Pins1, capacity); Array.Resize(ref Pins2, capacity);
            Array.Resize(ref Pinned1, capacity); Array.Resize(ref Pinned2, capacity);
            Array.Resize(ref Views1, capacity); Array.Resize(ref Views2, capacity);
            Array.Resize(ref Handles, capacity);
        }

        internal static unsafe void Pin(int index, JobQueryRange<T1, T2> range)
        {
            Pins1[index] = range.Components1.Pin(); Pinned1[index] = true;
            Views1[index] = PinnedNativeArrayView.Create<T1>(Pins1[index].Pointer, range.Length);
            Pins2[index] = range.Components2.Pin(); Pinned2[index] = true;
            Views2[index] = PinnedNativeArrayView.Create<T2>(Pins2[index].Pointer, range.Length);
        }

        internal static void Release(int count)
        {
            for (var i = 0; i < count; i++)
            {
                PinnedNativeArrayView.Release(ref Views2[i]);
                PinnedNativeArrayView.Release(ref Views1[i]);
                if (Pinned2[i]) { Pins2[i].Dispose(); Pinned2[i] = false; }
                if (Pinned1[i]) { Pins1[i].Dispose(); Pinned1[i] = false; }
            }
        }
    }

    internal static class SafeBuffers<T1, T2, T3>
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    {
        [ThreadStatic] internal static MemoryHandle[] Pins1 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static MemoryHandle[] Pins2 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static MemoryHandle[] Pins3 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static bool[] Pinned1 = Array.Empty<bool>();
        [ThreadStatic] internal static bool[] Pinned2 = Array.Empty<bool>();
        [ThreadStatic] internal static bool[] Pinned3 = Array.Empty<bool>();
        [ThreadStatic] internal static NativeArray<T1>[] Views1 = Array.Empty<NativeArray<T1>>();
        [ThreadStatic] internal static NativeArray<T2>[] Views2 = Array.Empty<NativeArray<T2>>();
        [ThreadStatic] internal static NativeArray<T3>[] Views3 = Array.Empty<NativeArray<T3>>();
        [ThreadStatic] internal static JobHandle[] Handles = Array.Empty<JobHandle>();

        internal static void EnsureCapacity(int count)
        {
            Pins1 ??= Array.Empty<MemoryHandle>(); Pins2 ??= Array.Empty<MemoryHandle>(); Pins3 ??= Array.Empty<MemoryHandle>();
            Pinned1 ??= Array.Empty<bool>(); Pinned2 ??= Array.Empty<bool>(); Pinned3 ??= Array.Empty<bool>();
            Views1 ??= Array.Empty<NativeArray<T1>>(); Views2 ??= Array.Empty<NativeArray<T2>>(); Views3 ??= Array.Empty<NativeArray<T3>>();
            Handles ??= Array.Empty<JobHandle>();
            if (Pins1.Length >= count) return;
            var capacity = SafeBuffers<T1>.NextCapacity(Pins1.Length, count);
            Array.Resize(ref Pins1, capacity); Array.Resize(ref Pins2, capacity); Array.Resize(ref Pins3, capacity);
            Array.Resize(ref Pinned1, capacity); Array.Resize(ref Pinned2, capacity); Array.Resize(ref Pinned3, capacity);
            Array.Resize(ref Views1, capacity); Array.Resize(ref Views2, capacity); Array.Resize(ref Views3, capacity);
            Array.Resize(ref Handles, capacity);
        }

        internal static unsafe void Pin(int index, JobQueryRange<T1, T2, T3> range)
        {
            Pins1[index] = range.Components1.Pin(); Pinned1[index] = true;
            Views1[index] = PinnedNativeArrayView.Create<T1>(Pins1[index].Pointer, range.Length);
            Pins2[index] = range.Components2.Pin(); Pinned2[index] = true;
            Views2[index] = PinnedNativeArrayView.Create<T2>(Pins2[index].Pointer, range.Length);
            Pins3[index] = range.Components3.Pin(); Pinned3[index] = true;
            Views3[index] = PinnedNativeArrayView.Create<T3>(Pins3[index].Pointer, range.Length);
        }

        internal static void Release(int count)
        {
            for (var i = 0; i < count; i++)
            {
                PinnedNativeArrayView.Release(ref Views3[i]); PinnedNativeArrayView.Release(ref Views2[i]);
                PinnedNativeArrayView.Release(ref Views1[i]);
                if (Pinned3[i]) { Pins3[i].Dispose(); Pinned3[i] = false; }
                if (Pinned2[i]) { Pins2[i].Dispose(); Pinned2[i] = false; }
                if (Pinned1[i]) { Pins1[i].Dispose(); Pinned1[i] = false; }
            }
        }
    }

    internal static class UnsafeBuffers<T1> where T1 : unmanaged
    {
        [ThreadStatic] internal static MemoryHandle[] Pins1 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static bool[] Pinned1 = Array.Empty<bool>();
        [ThreadStatic] internal static PointerWork<T1>[] Work = Array.Empty<PointerWork<T1>>();
        internal static void EnsureCapacity(int ranges, int work)
        {
            Pins1 ??= Array.Empty<MemoryHandle>(); Pinned1 ??= Array.Empty<bool>(); Work ??= Array.Empty<PointerWork<T1>>();
            if (Pins1.Length < ranges) { var c = SafeBuffers<T1>.NextCapacity(Pins1.Length, ranges); Array.Resize(ref Pins1, c); Array.Resize(ref Pinned1, c); }
            if (Work.Length < work) Array.Resize(ref Work, SafeBuffers<T1>.NextCapacity(Work.Length, work));
        }
        internal static void Release(int count) { for (var i = 0; i < count; i++) if (Pinned1[i]) { Pins1[i].Dispose(); Pinned1[i] = false; } }
    }

    internal static class UnsafeBuffers<T1, T2> where T1 : unmanaged where T2 : unmanaged
    {
        [ThreadStatic] internal static MemoryHandle[] Pins1 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static MemoryHandle[] Pins2 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static bool[] Pinned1 = Array.Empty<bool>();
        [ThreadStatic] internal static bool[] Pinned2 = Array.Empty<bool>();
        [ThreadStatic] internal static PointerWork<T1, T2>[] Work = Array.Empty<PointerWork<T1, T2>>();
        internal static void EnsureCapacity(int ranges, int work)
        {
            Pins1 ??= Array.Empty<MemoryHandle>(); Pins2 ??= Array.Empty<MemoryHandle>();
            Pinned1 ??= Array.Empty<bool>(); Pinned2 ??= Array.Empty<bool>(); Work ??= Array.Empty<PointerWork<T1, T2>>();
            if (Pins1.Length < ranges) { var c = SafeBuffers<T1>.NextCapacity(Pins1.Length, ranges); Array.Resize(ref Pins1, c); Array.Resize(ref Pins2, c); Array.Resize(ref Pinned1, c); Array.Resize(ref Pinned2, c); }
            if (Work.Length < work) Array.Resize(ref Work, SafeBuffers<T1>.NextCapacity(Work.Length, work));
        }
        internal static void Pin(int i, JobQueryRange<T1, T2> range) { Pins1[i] = range.Components1.Pin(); Pinned1[i] = true; Pins2[i] = range.Components2.Pin(); Pinned2[i] = true; }
        internal static void Release(int count) { for (var i = 0; i < count; i++) { if (Pinned2[i]) { Pins2[i].Dispose(); Pinned2[i] = false; } if (Pinned1[i]) { Pins1[i].Dispose(); Pinned1[i] = false; } } }
    }

    internal static class UnsafeBuffers<T1, T2, T3>
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    {
        [ThreadStatic] internal static MemoryHandle[] Pins1 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static MemoryHandle[] Pins2 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static MemoryHandle[] Pins3 = Array.Empty<MemoryHandle>();
        [ThreadStatic] internal static bool[] Pinned1 = Array.Empty<bool>();
        [ThreadStatic] internal static bool[] Pinned2 = Array.Empty<bool>();
        [ThreadStatic] internal static bool[] Pinned3 = Array.Empty<bool>();
        [ThreadStatic] internal static PointerWork<T1, T2, T3>[] Work = Array.Empty<PointerWork<T1, T2, T3>>();
        internal static void EnsureCapacity(int ranges, int work)
        {
            Pins1 ??= Array.Empty<MemoryHandle>(); Pins2 ??= Array.Empty<MemoryHandle>(); Pins3 ??= Array.Empty<MemoryHandle>();
            Pinned1 ??= Array.Empty<bool>(); Pinned2 ??= Array.Empty<bool>(); Pinned3 ??= Array.Empty<bool>(); Work ??= Array.Empty<PointerWork<T1, T2, T3>>();
            if (Pins1.Length < ranges) { var c = SafeBuffers<T1>.NextCapacity(Pins1.Length, ranges); Array.Resize(ref Pins1, c); Array.Resize(ref Pins2, c); Array.Resize(ref Pins3, c); Array.Resize(ref Pinned1, c); Array.Resize(ref Pinned2, c); Array.Resize(ref Pinned3, c); }
            if (Work.Length < work)
            {
                Array.Resize(ref Work, SafeBuffers<T1>.NextCapacity(Work.Length, work));
            }
        }
        internal static void Pin(int i, JobQueryRange<T1, T2, T3> range) { Pins1[i] = range.Components1.Pin(); Pinned1[i] = true; Pins2[i] = range.Components2.Pin(); Pinned2[i] = true; Pins3[i] = range.Components3.Pin(); Pinned3[i] = true; }
        internal static void Release(int count) { for (var i = 0; i < count; i++) { if (Pinned3[i]) { Pins3[i].Dispose(); Pinned3[i] = false; } if (Pinned2[i]) { Pins2[i].Dispose(); Pinned2[i] = false; } if (Pinned1[i]) { Pins1[i].Dispose(); Pinned1[i] = false; } } }
    }

    internal unsafe struct PointerWork<T1> where T1 : unmanaged
    { public T1* C1; public int Start, End; public PointerWork(T1* c1, int start, int end) { C1 = c1; Start = start; End = end; } }
    internal unsafe struct PointerWork<T1, T2> where T1 : unmanaged where T2 : unmanaged
    { public T1* C1; public T2* C2; public int Start, End; public PointerWork(T1* c1, T2* c2, int start, int end) { C1 = c1; C2 = c2; Start = start; End = end; } }
    internal unsafe struct PointerWork<T1, T2, T3> where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    { public T1* C1; public T2* C2; public T3* C3; public int Start, End; public PointerWork(T1* c1, T2* c2, T3* c3, int start, int end) { C1 = c1; C2 = c2; C3 = c3; Start = start; End = end; } }

    [BurstCompile]
    internal struct BurstRangeJob<TAction, T1> : IJobParallelFor where TAction : unmanaged, IBurstQueryAction<T1> where T1 : unmanaged
    { public TAction Action; public NativeArray<T1> Components1; public void Execute(int i) { var c1 = Components1[i]; Action.Execute(i, ref c1); Components1[i] = c1; } }
    [BurstCompile]
    internal struct BurstRangeJob<TAction, T1, T2> : IJobParallelFor where TAction : unmanaged, IBurstQueryAction<T1, T2> where T1 : unmanaged where T2 : unmanaged
    { public TAction Action; public NativeArray<T1> Components1; public NativeArray<T2> Components2; public void Execute(int i) { var c1 = Components1[i]; var c2 = Components2[i]; Action.Execute(i, ref c1, ref c2); Components1[i] = c1; Components2[i] = c2; } }
    [BurstCompile]
    internal struct BurstRangeJob<TAction, T1, T2, T3> : IJobParallelFor where TAction : unmanaged, IBurstQueryAction<T1, T2, T3> where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    { public TAction Action; public NativeArray<T1> Components1; public NativeArray<T2> Components2; public NativeArray<T3> Components3; public void Execute(int i) { var c1 = Components1[i]; var c2 = Components2[i]; var c3 = Components3[i]; Action.Execute(i, ref c1, ref c2, ref c3); Components1[i] = c1; Components2[i] = c2; Components3[i] = c3; } }

    [BurstCompile]
    public unsafe struct BurstPointerBatchJob<TAction, T1> : IJobParallelFor where TAction : unmanaged, IBurstQueryAction<T1> where T1 : unmanaged
    { public TAction Action; [NativeDisableUnsafePtrRestriction] internal PointerWork<T1>* Work; public void Execute(int w) { var item = Work[w]; for (var i = item.Start; i < item.End; i++) { ref var c1 = ref UnsafeUtility.ArrayElementAsRef<T1>(item.C1, i); Action.Execute(i, ref c1); } } }
    [BurstCompile]
    public unsafe struct BurstPointerBatchJob<TAction, T1, T2> : IJobParallelFor where TAction : unmanaged, IBurstQueryAction<T1, T2> where T1 : unmanaged where T2 : unmanaged
    { public TAction Action; [NativeDisableUnsafePtrRestriction] internal PointerWork<T1, T2>* Work; public void Execute(int w) { var item = Work[w]; for (var i = item.Start; i < item.End; i++) { ref var c1 = ref UnsafeUtility.ArrayElementAsRef<T1>(item.C1, i); ref var c2 = ref UnsafeUtility.ArrayElementAsRef<T2>(item.C2, i); Action.Execute(i, ref c1, ref c2); } } }
    [BurstCompile]
    public unsafe struct BurstPointerBatchJob<TAction, T1, T2, T3> : IJobParallelFor where TAction : unmanaged, IBurstQueryAction<T1, T2, T3> where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    { public TAction Action; [NativeDisableUnsafePtrRestriction] internal PointerWork<T1, T2, T3>* Work; public void Execute(int w) { var item = Work[w]; for (var i = item.Start; i < item.End; i++) { ref var c1 = ref UnsafeUtility.ArrayElementAsRef<T1>(item.C1, i); ref var c2 = ref UnsafeUtility.ArrayElementAsRef<T2>(item.C2, i); ref var c3 = ref UnsafeUtility.ArrayElementAsRef<T3>(item.C3, i); Action.Execute(i, ref c1, ref c2, ref c3); } } }
}
