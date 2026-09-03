using BenchmarkDotNet.Attributes;
using Friflo.Engine.ECS;
using LitheEcs;

namespace LitheEcsBenchmark
{
    public class FrifloComparisonBenchmark : EcsBenchmarkBase
    {
        private EntityStore _frifloWorld = null!;
        private ArchetypeQuery<Position> _frifloSingleQuery = null!;
        private ArchetypeQuery<Position, Velocity> _frifloTwoQuery = null!;
        private ArchetypeQuery<Position, Velocity, Acceleration> _frifloThreeQuery = null!;
        private ArchetypeQuery<Position, Velocity, Acceleration, Health> _frifloFourQuery = null!;
        private ArchetypeQuery<Position, Velocity, Acceleration, Health, Mana> _frifloFiveQuery = null!;
        private ArchetypeQuery<Position, Velocity> _frifloFiltered100Query = null!;
        private ArchetypeQuery<Position, Velocity> _frifloFiltered50Query = null!;
        private ArchetypeQuery<Position, Velocity> _frifloFiltered10Query = null!;
        private ArchetypeQuery<Position, Velocity> _frifloFiltered0Query = null!;
        private Friflo.Engine.ECS.Entity _frifloFilterInvalidationEntity;
        private Friflo.Engine.ECS.Entity[] _frifloCommandEntities = null!;
        private Friflo.Engine.ECS.CommandBuffer _frifloCommandBuffer = null!;
        private EntityStore _frifloSteadyWorld = null!;
        private Friflo.Engine.ECS.Entity[] _frifloSteadyEntities = null!;
        private EntityStore _splitFrifloWorld = null!;
        private Friflo.Engine.ECS.Entity[] _splitFrifloEntities = null!;

        protected override void SetupDerived()
        {
            _frifloWorld = new EntityStore();
            _frifloCommandEntities = new Friflo.Engine.ECS.Entity[EntityCount];
            for (var i = 0; i < EntityCount; i++)
            {
                var tags = (i & 1) == 0
                    ? Tags.Get<Player, Grounded>()
                    : Tags.Get<Player, Flying>();
                _frifloCommandEntities[i] = _frifloWorld.CreateEntity(
                    new Position { X = i, Y = i, Z = i },
                    new Velocity { X = 1, Y = 1, Z = 1 },
                    new Acceleration { X = 1, Y = 1, Z = 1 },
                    new Health { Value = 100 },
                    new Mana { Value = 10 },
                    tags);
                if (i % 10 != 0) _frifloCommandEntities[i].AddTag<Excluded90>();
            }

            _frifloFilterInvalidationEntity = _frifloCommandEntities[0];
            _frifloSingleQuery = _frifloWorld.Query<Position>();
            _frifloTwoQuery = _frifloWorld.Query<Position, Velocity>();
            _frifloThreeQuery = _frifloWorld.Query<Position, Velocity, Acceleration>();
            _frifloFourQuery = _frifloWorld.Query<Position, Velocity, Acceleration, Health>();
            _frifloFiveQuery = _frifloWorld.Query<Position, Velocity, Acceleration, Health, Mana>();

            _frifloFiltered100Query = _frifloWorld.Query<Position, Velocity>();
            _frifloFiltered100Query.AllTags(Tags.Get<Player>());
            _frifloFiltered100Query.WithoutAnyTags(Tags.Get<Disabled>());
            _frifloFiltered100Query.AnyTags(Tags.Get<Grounded, Flying>());
            _frifloFiltered100Query.FreezeFilter();
            _frifloFiltered50Query = _frifloWorld.Query<Position, Velocity>();
            _frifloFiltered50Query.WithoutAnyTags(Tags.Get<Grounded>());
            _frifloFiltered50Query.FreezeFilter();
            _frifloFiltered10Query = _frifloWorld.Query<Position, Velocity>();
            _frifloFiltered10Query.WithoutAnyTags(Tags.Get<Excluded90>());
            _frifloFiltered10Query.FreezeFilter();
            _frifloFiltered0Query = _frifloWorld.Query<Position, Velocity>();
            _frifloFiltered0Query.WithoutAnyTags(Tags.Get<Player>());
            _frifloFiltered0Query.FreezeFilter();
            ValidateFilteredCount(LitheFilteredQuery, EntityCount, _frifloFiltered100Query);
            ValidateFilteredCount(LitheFiltered50Query, EntityCount / 2, _frifloFiltered50Query);
            ValidateFilteredCount(LitheFiltered10Query, EntityCount / 10, _frifloFiltered10Query);
            ValidateFilteredCount(LitheFiltered0Query, 0, _frifloFiltered0Query);

            _frifloCommandBuffer = _frifloWorld.GetCommandBuffer();
            _frifloCommandBuffer.ReuseBuffer = true;
            _frifloSteadyWorld = new EntityStore();
            _frifloSteadyEntities = new Friflo.Engine.ECS.Entity[EntityCount];
            for (var i = 0; i < EntityCount; i++)
            {
                var entity = _frifloSteadyWorld.CreateEntity(
                    new Position { X = 1, Y = 2, Z = 3 },
                    new Velocity { X = 1, Y = 1, Z = 1 },
                    new Acceleration { X = 1, Y = 1, Z = 1 },
                    new Health { Value = 100 });
                entity.DeleteEntity();
            }

            var splitCount = EntityCount * SplitRepetitions;
            _splitFrifloWorld = new EntityStore();
            _splitFrifloEntities = new Friflo.Engine.ECS.Entity[splitCount];
            SpawnFrifloSplitEntities();
            DeleteFrifloSplitEntities();
        }

        private static void ValidateFilteredCount(Query<Position, Velocity> litheQuery, int expected,
            ArchetypeQuery<Position, Velocity> frifloQuery)
        {
            var litheCount = 0;
            litheQuery.ForEach((in LitheEcs.Entity entity, ref Position position, ref Velocity velocity) =>
                litheCount++);
            if (litheCount != expected || frifloQuery.Count != expected)
                throw new System.InvalidOperationException(
                    $"Filtered benchmark setup mismatch. Expected {expected}, " +
                    $"LitheEcs {litheCount}, Friflo {frifloQuery.Count}.");
        }

        [Benchmark(Baseline = true), BenchmarkCategory("Lifecycle.SteadyIndividual")]
        public void Friflo_Steady_SpawnDespawn_Individual()
        {
            var position = new Position { X = 1, Y = 2, Z = 3 };
            var velocity = new Velocity { X = 1, Y = 1, Z = 1 };
            var acceleration = new Acceleration { X = 1, Y = 1, Z = 1 };
            var health = new Health { Value = 100 };
            for (var i = 0; i < EntityCount; i++)
            {
                var entity = _frifloSteadyWorld.CreateEntity(position, velocity, acceleration, health);
                entity.DeleteEntity();
            }
        }

        [Benchmark(OperationsPerInvoke = SplitRepetitions, Baseline = true),
         BenchmarkCategory("Lifecycle.SteadySpawn")]
        public void Friflo_Steady_Spawn_4Components() => SpawnFrifloSplitEntities();

        [IterationCleanup(Target = nameof(Friflo_Steady_Spawn_4Components))]
        public void CleanupFrifloSteadySpawn() => DeleteFrifloSplitEntities();

        [IterationSetup(Target = nameof(Friflo_Steady_Despawn_4Components))]
        public void SetupFrifloSteadyDespawn() => SpawnFrifloSplitEntities();

        [Benchmark(OperationsPerInvoke = SplitRepetitions, Baseline = true),
         BenchmarkCategory("Lifecycle.SteadyDespawn")]
        public void Friflo_Steady_Despawn_4Components() => DeleteFrifloSplitEntities();

        private void SpawnFrifloSplitEntities()
        {
            var position = new Position { X = 1, Y = 2, Z = 3 };
            var velocity = new Velocity { X = 1, Y = 1, Z = 1 };
            var acceleration = new Acceleration { X = 1, Y = 1, Z = 1 };
            var health = new Health { Value = 100 };
            for (var i = 0; i < _splitFrifloEntities.Length; i++)
                _splitFrifloEntities[i] = _splitFrifloWorld.CreateEntity(
                    position, velocity, acceleration, health);
        }

        private void DeleteFrifloSplitEntities()
        {
            for (var i = 0; i < _splitFrifloEntities.Length; i++) _splitFrifloEntities[i].DeleteEntity();
        }

        [Benchmark(Baseline = true), BenchmarkCategory("Spawn.Individual")]
        public void Friflo_Spawn_Individual()
        {
            var store = new EntityStore();
            var position = new Position { X = 1, Y = 2, Z = 3 };
            var velocity = new Velocity { X = 1, Y = 1, Z = 1 };
            for (var i = 0; i < EntityCount; i++) store.CreateEntity(position, velocity);
        }

        [Benchmark(Baseline = true), BenchmarkCategory("Spawn.Batch")]
        public void Friflo_Spawn_Batch()
        {
            var store = new EntityStore();
            var batch = store.Batch(autoReturn: false);
            batch.Add(new Position { X = 1, Y = 2, Z = 3 });
            batch.Add(new Velocity { X = 1, Y = 1, Z = 1 });
            for (var i = 0; i < EntityCount; i++) batch.CreateEntity();
            batch.Return();
        }

        [Benchmark(Baseline = true), BenchmarkCategory("CommandBuffer.Direct")]
        public void Friflo_CommandBuffer_Direct()
        {
            var component = new Position { X = 1, Y = 2, Z = 3 };
            for (var i = 0; i < _frifloCommandEntities.Length; i++)
                _frifloCommandEntities[i].AddComponent(component);
        }

        [Benchmark(Baseline = true), BenchmarkCategory("CommandBuffer.RecordPlayback")]
        public void Friflo_CommandBuffer_RecordAndPlayback()
        {
            var component = new Position { X = 1, Y = 2, Z = 3 };
            for (var i = 0; i < _frifloCommandEntities.Length; i++)
                _frifloCommandBuffer.AddComponent(_frifloCommandEntities[i].Id, component);
            _frifloCommandBuffer.Playback();
        }

        [Benchmark(Baseline = true), BenchmarkCategory("Query.1Component")]
        public void Friflo_Single_ForEach() =>
            _frifloSingleQuery.ForEachEntity(
                (ref Position position, Friflo.Engine.ECS.Entity entity) => position.X += 1);

        [Benchmark(Baseline = true), BenchmarkCategory("Query.2Components")]
        public void Friflo_Two_ForEach() =>
            _frifloTwoQuery.ForEachEntity((ref Position position, ref Velocity velocity,
                Friflo.Engine.ECS.Entity entity) => position.X += 1);

        [Benchmark(Baseline = true), BenchmarkCategory("Query.3Components")]
        public void Friflo_Three_ForEach() =>
            _frifloThreeQuery.ForEachEntity((ref Position position, ref Velocity velocity,
                ref Acceleration acceleration, Friflo.Engine.ECS.Entity entity) => position.X += 1);

        [Benchmark(Baseline = true), BenchmarkCategory("Query.4Components")]
        public void Friflo_Four_ForEach() =>
            _frifloFourQuery.ForEachEntity((ref Position position, ref Velocity velocity,
                ref Acceleration acceleration, ref Health health,
                Friflo.Engine.ECS.Entity entity) => position.X += 1);

        [Benchmark(Baseline = true), BenchmarkCategory("Query.5Components")]
        public void Friflo_Five_ForEach() =>
            _frifloFiveQuery.ForEachEntity((ref Position position, ref Velocity velocity,
                ref Acceleration acceleration, ref Health health, ref Mana mana,
                Friflo.Engine.ECS.Entity entity) =>
                position.X = velocity.X + acceleration.X + health.Value + mana.Value);

        [Benchmark(Baseline = true), BenchmarkCategory("Query.Filtered100")]
        public void Friflo_Two_ForEach_Filtered_Match100() => RunFiltered(_frifloFiltered100Query);

        [Benchmark(Baseline = true), BenchmarkCategory("Query.Filtered50")]
        public void Friflo_Two_ForEach_Filtered_Match50() => RunFiltered(_frifloFiltered50Query);

        [Benchmark(Baseline = true), BenchmarkCategory("Query.Filtered10")]
        public void Friflo_Two_ForEach_Filtered_Match10() => RunFiltered(_frifloFiltered10Query);

        [Benchmark(Baseline = true), BenchmarkCategory("Query.Filtered0")]
        public void Friflo_Two_ForEach_Filtered_Match0() => RunFiltered(_frifloFiltered0Query);

        [Benchmark(Baseline = true), BenchmarkCategory("Query.Filtered100Invalidated")]
        public void Friflo_Two_ForEach_Filtered_Match100_AfterInvalidation()
        {
            _frifloFilterInvalidationEntity.AddTag<Disabled>();
            _frifloFilterInvalidationEntity.RemoveTag<Disabled>();
            RunFiltered(_frifloFiltered100Query);
        }

        private static void RunFiltered(ArchetypeQuery<Position, Velocity> query) =>
            query.ForEachEntity((ref Position position, ref Velocity velocity,
                Friflo.Engine.ECS.Entity entity) => position.X += 1);
    }
}
