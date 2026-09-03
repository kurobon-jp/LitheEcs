#if !RELEASE && !DISABLE_LITHEECS_DIAGNOSTICS
#define _INTERNAL_DERIVED_USE_DIAGNOSTICS
#endif

using System;
using NUnit.Framework;

namespace LitheEcs.Tests
{
    public class StructuralBatchScopeTests
    {
        private struct Tag1 { }
        private struct Tag2 { }
        private struct Tag3 { }
        private struct SingletonTag : ISingleton { }

        [Test]
        public void Dispose_CreatesOnlyTheFinalArchetype_EvenAboveFiveTypes()
        {
            using var world = new World();
            var entity = world.Spawn();
            var created = 0;
            world.ArchetypeCreatedLogger = _ => created++;

            using (world.BeginStructuralBatch())
            {
                entity.Add(new Position(1, 2, 3));
                entity.Add(new Velocity(4, 5, 6));
                entity.Add(new Acceleration(7, 8, 9));
                entity.Add<Tag1>();
                entity.Add<Tag2>();
                entity.Add<Tag3>();
            }

            Assert.That(created, Is.EqualTo(1));
            Assert.That(entity.Has<Tag3>(), Is.True);
            Assert.That(entity.Get<Position>().Value.X, Is.EqualTo(1));
        }

        [Test]
        public void ExistingQuery_FlushesPendingAdditions()
        {
            using var world = new World();
            var query = world.Query<Position, Velocity>();
            var entity = world.Spawn();

            using (world.BeginStructuralBatch())
            {
                entity.Add(new Position(1, 0, 0));
                entity.Add(new Velocity(2, 0, 0));
                var count = 0;
                foreach (var _ in query) count++;
                Assert.That(count, Is.EqualTo(1));
            }
        }

        [Test]
        public void SingletonAddedLast_RemainsPartOfTheSingleFinalArchetype()
        {
            using var world = new World();
            var entity = world.Spawn();
            var created = 0;
            world.ArchetypeCreatedLogger = _ => created++;

            using (world.BeginStructuralBatch())
            {
                entity.Add<Position>();
                entity.Add<Velocity>();
                entity.Add<SingletonTag>();
            }

            Assert.That(created, Is.EqualTo(1));
            Assert.That(world.Singleton<SingletonTag>(), Is.EqualTo(entity));
        }

        [Test]
        public void PendingSingleton_RejectsAnotherEntityImmediately()
        {
            using var world = new World();
            var first = world.Spawn();
            var second = world.Spawn();

            using (world.BeginStructuralBatch())
            {
                first.Add<SingletonTag>();
                Assert.Throws<InvalidOperationException>(() => second.Add<SingletonTag>());
            }
        }

        [Test]
        public void NestedScopeAndDespawn_PreserveImmediateSemantics()
        {
            using var world = new World();
            var entity = world.Spawn();

            using (world.BeginStructuralBatch())
            {
                entity.Add<Position>();
                using (world.BeginStructuralBatch()) entity.Add<Velocity>();
                entity.Despawn();
            }

            Assert.That(entity.IsAlive, Is.False);
        }

        [Test]
        public void ReservedBatch_ReusesCommandAndPayloadStorageWithoutAllocation()
        {
            using var world = new World(1);
            world.ReserveArchetype(1, static archetype => archetype
                .Add<Position>().Add<Velocity>().Add<Acceleration>());
            world.CommandBuffer.Reserve(3);
            world.CommandBuffer.ReservePayload<Position>(1);
            world.CommandBuffer.ReservePayload<Velocity>(1);
            world.CommandBuffer.ReservePayload<Acceleration>(1);

            RunOnce(world); // Warm transitions and reusable lists.

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) RunOnce(world);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);

            static void RunOnce(World targetWorld)
            {
                var entity = targetWorld.Spawn();
                using (targetWorld.BeginStructuralBatch())
                {
                    entity.Add(new Position(1, 0, 0));
                    entity.Add(new Velocity(2, 0, 0));
                    entity.Add(new Acceleration(3, 0, 0));
                }
                entity.Despawn();
            }
        }

#if _INTERNAL_DERIVED_USE_DIAGNOSTICS
        [Test]
        public void AllocationDiagnostics_ReportsColdPlaybackPaths_AndCanBeReset()
        {
            using var world = new World(1) { AllocationDiagnosticsEnabled = true };
            world.ResetAllocationDiagnostics();

            var entity = world.Spawn();
            world.CommandBuffer.AddComponent(entity, new Position(1, 2, 3));
            world.CommandBuffer.Playback();

            var snapshot = world.GetAllocationDiagnostics();
            Assert.That(snapshot.HasEvents, Is.True);
            Assert.That(snapshot.TotalEvents, Is.GreaterThan(0));
            Assert.That(snapshot.ComponentBufferCreations, Is.EqualTo(1));
            Assert.That(snapshot.ArchetypeCreations, Is.EqualTo(1));
            Assert.That(snapshot.ChunkCreations, Is.EqualTo(1));
            Assert.That(snapshot.LastChunkArchetypeIndex, Is.GreaterThan(0));
            Assert.That(snapshot.LastComponentPageTypeId, Is.EqualTo(ComponentType<Position>.Id));
            Assert.That(snapshot.LastCommandPayloadTypeId, Is.EqualTo(ComponentType<Position>.Id));
            Assert.That(snapshot.ToString(), Does.Contain($"CommandPayloadLastType={typeof(Position).FullName}"));
            Assert.That(world.FormatAllocationDiagnostics(snapshot), Does.Contain(typeof(Position).FullName));

            world.ResetAllocationDiagnostics();
            Assert.That(world.GetAllocationDiagnostics().HasEvents, Is.False);
            Assert.That(world.GetAllocationDiagnostics().TotalEvents, Is.Zero);
            Assert.That(world.GetAllocationDiagnostics().LastCommandPayloadTypeId, Is.EqualTo(-1));
        }

        [Test]
        public void GetAllocationDiagnostics_DoesNotAllocate()
        {
            using var world = new World { AllocationDiagnosticsEnabled = true };
            world.ResetAllocationDiagnostics();
            _ = world.GetAllocationDiagnostics();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) _ = world.GetAllocationDiagnostics();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void AllocationDiagnostics_ChunkActivationAlone_IsNotReportedAsAllocationEvent()
        {
            using var world = new World(1);
            world.ReserveArchetypeGroup(1, static group =>
                group.Add(static archetype => archetype.Add<Position>()));
            var warmEntity = world.Spawn();
            warmEntity.Add<Position>();
            warmEntity.Despawn();
            world.AllocationDiagnosticsEnabled = true;
            world.ResetAllocationDiagnostics();

            world.Spawn().Add<Position>();

            var snapshot = world.GetAllocationDiagnostics();
            Assert.That(snapshot.ChunkActivations, Is.EqualTo(1));
            Assert.That(snapshot.EntityPageAllocations, Is.Zero);
            Assert.That(snapshot.ComponentPageAllocations, Is.Zero);
            Assert.That(snapshot.HasEvents, Is.False);
        }

        [Test]
        public void QueryWarmup_ShouldBuildMatchCacheOnlyOnce()
        {
            using var world = new World();
            world.ReserveArchetype(1, static archetype => archetype.Add<Position>());
            var query = world.Query<Position>();
            world.AllocationDiagnosticsEnabled = true;
            world.ResetAllocationDiagnostics();

            query.Warmup();

            Assert.That(world.GetAllocationDiagnostics().QueryMatchListGrowths, Is.EqualTo(1));
            world.ResetAllocationDiagnostics();
            query.Warmup();
            Assert.That(world.GetAllocationDiagnostics().HasEvents, Is.False);
        }
#endif
    }
}
