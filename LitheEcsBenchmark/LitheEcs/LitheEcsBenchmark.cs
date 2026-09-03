using BenchmarkDotNet.Attributes;
using LitheEcs;

namespace LitheEcsBenchmark
{
    [MemoryDiagnoser]
    public abstract class EcsBenchmarkBase
    {
        protected const int SplitRepetitions = 512;
        private World _litheWorld = null!;
        private EntityTemplate _template = null!;
        private EntityTemplate _steadyTemplate = null!;
        private Query<Position> _litheSingleQuery;
        private Query<Position, Velocity> _litheTwoQuery;
        private Query<Position, Velocity, Acceleration> _litheThreeQuery;
        private Query<Position, Velocity, Acceleration, Health> _litheFourQuery;
        private Query<Position, Velocity, Acceleration, Health, Mana> _litheFiveQuery;
        protected Query<Position, Velocity> LitheFilteredQuery;
        protected Query<Position, Velocity> LitheFiltered50Query;
        protected Query<Position, Velocity> LitheFiltered10Query;
        protected Query<Position, Velocity> LitheFiltered0Query;
        private EntityQuery<Position> _litheEntityPositionQuery;
        private EntityQuery _litheEntityFilteredQuery;
        private EntityQueryResult _litheEntityFilteredResult;
        private LitheEcs.Entity _litheFilterInvalidationEntity;
        private World _commandWorld = null!;
        private LitheEcs.Entity[] _commandEntities = null!;
        private EntityCommandBuffer _commandBuffer = null!;
        private World _steadyWorld = null!;
        private LitheEcs.Entity[] _steadyEntities = null!;
        private World _splitLitheWorld = null!;
        private EntityTemplate _splitLitheTemplate = null!;
        private LitheEcs.Entity[] _splitLitheEntities = null!;
        private World _lookupWorld = null!;
        private LitheEcs.Entity _lookupEntity;
        private object _boundObject = null!;
        private BindingKey _boundStruct;
        private SingleAction _singleAction;
        private MoveAction _moveAction;
        private FiveAction _fiveAction;

        private struct SingleAction : IQueryAction<Position>
        {
            public void Execute(in LitheEcs.Entity entity, ref Position position) => position.X += 1;
        }

        private struct MoveAction : IQueryAction<Position, Velocity>
        {
            public void Execute(in LitheEcs.Entity entity, ref Position position, ref Velocity velocity) => position.X += 1;
        }

        private struct FiveAction : IQueryAction<Position, Velocity, Acceleration, Health, Mana>
        {
            public void Execute(in LitheEcs.Entity entity, ref Position position, ref Velocity velocity,
                ref Acceleration acceleration, ref Health health, ref Mana mana) =>
                position.X = velocity.X + acceleration.X + health.Value + mana.Value;
        }

        [Params(1000)] public int EntityCount;

        [GlobalSetup]
        public void Setup()
        {
            _litheWorld = new World(EntityCount);
            _template = CreateTemplate(_litheWorld);
            var customEntities = new LitheEcs.Entity[EntityCount];
            _template.SpawnBatch(EntityCount, customEntities);
            for (int i = 0; i < customEntities.Length; i++)
            {
                customEntities[i].Add(new Player());
                if ((i & 1) == 0)
                    customEntities[i].Add(new Grounded());
                else
                    customEntities[i].Add(new Flying());
                if (i % 10 != 0)
                    customEntities[i].Add(new Excluded90());
            }
            _litheFilterInvalidationEntity = customEntities[0];

            _litheSingleQuery = _litheWorld.Query<Position>();
            _litheTwoQuery = _litheWorld.Query<Position, Velocity>();
            _litheThreeQuery = _litheWorld.Query<Position, Velocity, Acceleration>();
            _litheFourQuery = _litheWorld.Query<Position, Velocity, Acceleration, Health>();
            _litheFiveQuery = _litheWorld.Query<Position, Velocity, Acceleration, Health, Mana>();
            LitheFilteredQuery = _litheTwoQuery
                .With<Player>()
                .Without<Disabled>()
                .Any<Grounded, Flying>();
            LitheFiltered50Query = _litheTwoQuery.Without<Grounded>();
            LitheFiltered10Query = _litheTwoQuery.Without<Excluded90>();
            LitheFiltered0Query = _litheTwoQuery.Without<Player>();
            _litheEntityPositionQuery = _litheWorld.Query().With<Position>();
            _litheEntityFilteredQuery = _litheWorld.Query()
                .With<Position>()
                .With<Velocity>()
                .With<Player>()
                .Without<Disabled>()
                .Any<Grounded, Flying>();
            ValidateEntityQueryCount(_litheEntityPositionQuery, EntityCount);
            ValidateEntityQueryCount(_litheEntityFilteredQuery, EntityCount);
            _litheEntityFilteredResult = _litheEntityFilteredQuery.Result();
            _commandWorld = new World(EntityCount);
            _commandEntities = new LitheEcs.Entity[EntityCount];
            _commandWorld.SpawnBatch(EntityCount, _commandEntities);
            _commandBuffer = _commandWorld.CommandBuffer;

            _steadyWorld = new World(EntityCount);
            _steadyTemplate = CreateTemplate(_steadyWorld);
            _steadyEntities = new LitheEcs.Entity[EntityCount];
            _steadyTemplate.SpawnBatch(EntityCount, _steadyEntities);
            for (int i = 0; i < _steadyEntities.Length; i++) _steadyWorld.Despawn(_steadyEntities[i]);

            var splitCount = EntityCount * SplitRepetitions;
            _splitLitheWorld = new World(splitCount);
            _splitLitheTemplate = CreateTemplate(_splitLitheWorld);
            _splitLitheEntities = new LitheEcs.Entity[splitCount];
            _splitLitheTemplate.SpawnBatch(splitCount, _splitLitheEntities);
            _splitLitheWorld.DespawnBatch(_splitLitheEntities);

            _lookupWorld = new World();
            var singleton = _lookupWorld.Spawn();
            singleton.Add<LocalPlayer>();
            singleton.Add(new Position { X = 1, Y = 2, Z = 3 });
            _lookupEntity = singleton;
            _boundObject = new object();
            singleton.Bind(_boundObject);
            _boundStruct = new BindingKey(42);
            singleton.Bind(_boundStruct);
            SetupDerived();
        }

        protected virtual void SetupDerived() { }

        private static EntityTemplate CreateTemplate(World world) => world.CreateTemplate()
            .Add(new Position { X = 1, Y = 2, Z = 3 })
            .Add(new Velocity { X = 1, Y = 1, Z = 1 })
            .Add(new Acceleration { X = 1, Y = 1, Z = 1 })
            .Add(new Health { Value = 100 })
            .Add(new Mana { Value = 10 });

        private static void ValidateEntityQueryCount(EntityQuery query, int expected)
        {
            var count = 0;
            foreach (var _ in query) count++;
            if (count != expected)
                throw new InvalidOperationException(
                    $"EntityQuery benchmark setup mismatch. Expected {expected}, LitheEcs {count}.");
        }

        private static void ValidateEntityQueryCount<T>(EntityQuery<T> query, int expected) where T : struct
        {
            var count = 0;
            foreach (var _ in query) count++;
            if (count != expected)
                throw new InvalidOperationException(
                    $"EntityQuery benchmark setup mismatch. Expected {expected}, LitheEcs {count}.");
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _litheWorld.Dispose();
            _commandWorld.Dispose();
            _steadyWorld.Dispose();
            _splitLitheWorld.Dispose();
            _lookupWorld.Dispose();
            CleanupDerived();
        }

        protected virtual void CleanupDerived() { }

        [Benchmark, BenchmarkCategory("Spawn.Individual")]
        public void LitheECS_Spawn_Individual()
        {
            using var world = new World(EntityCount);
            var template = CreateTemplate(world);
            for (int i = 0; i < EntityCount; i++)
                template.Spawn();
        }

        [Benchmark, BenchmarkCategory("Spawn.Batch")]
        public void LitheECS_Spawn_Batch()
        {
            using var world = new World(EntityCount);
            CreateTemplate(world).SpawnBatch(EntityCount);
        }

        [Benchmark, BenchmarkCategory("Lifecycle.SteadyIndividual")]
        public void LitheECS_Steady_SpawnDespawn_Individual()
        {
            for (int i = 0; i < EntityCount; i++)
            {
                var entity = _steadyTemplate.Spawn();
                _steadyWorld.Despawn(entity);
            }
        }

        [Benchmark(OperationsPerInvoke = SplitRepetitions), BenchmarkCategory("Lifecycle.SteadySpawn")]
        public void LitheECS_Steady_Spawn_4Components()
        {
            for (var i = 0; i < _splitLitheEntities.Length; i++)
                _splitLitheEntities[i] = _splitLitheTemplate.Spawn();
        }

        [IterationCleanup(Target = nameof(LitheECS_Steady_Spawn_4Components))]
        public void CleanupLitheSteadySpawn() => _splitLitheWorld.DespawnBatch(_splitLitheEntities);

        [IterationSetup(Target = nameof(LitheECS_Steady_Despawn_4Components))]
        public void SetupLitheSteadyDespawn() =>
            _splitLitheTemplate.SpawnBatch(_splitLitheEntities.Length, _splitLitheEntities);

        [Benchmark(OperationsPerInvoke = SplitRepetitions), BenchmarkCategory("Lifecycle.SteadyDespawn")]
        public void LitheECS_Steady_Despawn_4Components()
        {
            for (var i = 0; i < _splitLitheEntities.Length; i++)
                _splitLitheWorld.Despawn(_splitLitheEntities[i]);
        }

        [Benchmark, BenchmarkCategory("LitheOnly.SteadyBatchLifecycle")]
        public void LitheECS_Steady_SpawnDespawn_Batch()
        {
            _steadyTemplate.SpawnBatch(EntityCount, _steadyEntities);
            _steadyWorld.DespawnBatch(_steadyEntities);
        }

        [Benchmark, BenchmarkCategory("CommandBuffer.Direct")]
        public void LitheECS_CommandBuffer_Direct()
        {
            var component = new Position { X = 1, Y = 2, Z = 3 };
            for (int i = 0; i < _commandEntities.Length; i++)
                _commandWorld.AddComponent(_commandEntities[i], component);
        }

        [Benchmark, BenchmarkCategory("CommandBuffer.RecordPlayback")]
        public void LitheECS_CommandBuffer_RecordAndPlayback()
        {
            var component = new Position { X = 1, Y = 2, Z = 3 };
            _commandBuffer.AddComponentBatch(_commandEntities, component);
            _commandBuffer.Playback();
        }

        [Benchmark, BenchmarkCategory("Query.1Component")]
        public void LitheECS_Single()
        {
            foreach (ref var position in _litheWorld.Query<Position>())
                position.X += 1;
        }

        [Benchmark, BenchmarkCategory("Query.1Component")]
        public void LitheECS_Single_Callback()
        {
            _litheSingleQuery.ForEach((in LitheEcs.Entity entity, ref Position position) => position.X += 1);
        }

        [Benchmark, BenchmarkCategory("Query.1Component")]
        public void LitheECS_Single_StructAction() => _litheSingleQuery.ForEach(ref _singleAction);

        [Benchmark, BenchmarkCategory("LitheOnly.EntityQuery.ComponentAccess")]
        public void LitheECS_EntityQuery_Single_CreateEachTime()
        {
            foreach (var entity in _litheWorld.Query().With<Position>())
                entity.Get<Position>().X += 1;
        }

        [Benchmark, BenchmarkCategory("LitheOnly.EntityQuery.ComponentAccess")]
        public void LitheECS_EntityQuery_Single_Cached()
        {
            foreach (var entity in _litheEntityPositionQuery)
                entity.Get<Position>().X += 1;
        }

        [Benchmark, BenchmarkCategory("LitheOnly.EntityQuery.Filtered")]
        public void LitheECS_EntityQuery_Filtered_Match100()
        {
            foreach (var entity in _litheEntityFilteredQuery)
                entity.Get<Position>().X += 1;
        }

        [Benchmark, BenchmarkCategory("LitheOnly.EntityQuery.Result")]
        public LitheEcs.Entity LitheECS_EntityQuery_Result_CreateViewEachTime()
        {
            var result = _litheEntityFilteredQuery.Result();
            return result[result.Count / 2];
        }

        [Benchmark, BenchmarkCategory("LitheOnly.EntityQuery.Result")]
        public LitheEcs.Entity LitheECS_EntityQuery_Result_CachedView() =>
            _litheEntityFilteredResult[_litheEntityFilteredResult.Count / 2];

        [Benchmark, BenchmarkCategory("LitheOnly.EntityQuery.Result")]
        public void LitheECS_EntityQuery_Result_IndexedAll()
        {
            var result = _litheEntityFilteredQuery.Result();
            var count = result.Count;
            for (var i = 0; i < count; i++)
                result[i].Get<Position>().X += 1;
        }

        [Benchmark, BenchmarkCategory("Query.2Components")]
        public void LitheECS_Two()
        {
            foreach (var (position, velocity) in _litheWorld.Query<Position, Velocity>())
                position.Value.X += 1;
        }

        [Benchmark, BenchmarkCategory("Query.2Components")]
        public void LitheECS_Two_Callback()
        {
            _litheTwoQuery.ForEach((in LitheEcs.Entity entity, ref Position position, ref Velocity velocity) =>
                position.X += 1);
        }

        [Benchmark, BenchmarkCategory("Query.2Components")]
        public void LitheECS_Two_StructAction() => _litheTwoQuery.ForEach(ref _moveAction);

        [Benchmark, BenchmarkCategory("Query.5Components")]
        public void LitheECS_Five_ForEach()
        {
            foreach (var (position, velocity, acceleration, health, player) in _litheFiveQuery)
                position.Value.X = velocity.Value.X + acceleration.Value.X + health.Value.Value + player.Value.Value;
        }

        [Benchmark, BenchmarkCategory("Query.5Components")]
        public void LitheECS_Five_StructAction() => _litheFiveQuery.ForEach(ref _fiveAction);

        [Benchmark, BenchmarkCategory("Query.Filtered100")]
        public void LitheECS_Two_Callback_Filtered_Match100()
        {
            LitheFilteredQuery.ForEach((in LitheEcs.Entity entity, ref Position position, ref Velocity velocity) =>
                position.X += 1);
        }

        [Benchmark, BenchmarkCategory("Query.Filtered50")]
        public void LitheECS_Two_Callback_Filtered_Match50() =>
            LitheFiltered50Query.ForEach((in LitheEcs.Entity entity, ref Position position, ref Velocity velocity) => position.X += 1);

        [Benchmark, BenchmarkCategory("Query.Filtered10")]
        public void LitheECS_Two_Callback_Filtered_Match10() =>
            LitheFiltered10Query.ForEach((in LitheEcs.Entity entity, ref Position position, ref Velocity velocity) => position.X += 1);

        [Benchmark, BenchmarkCategory("Query.Filtered0")]
        public void LitheECS_Two_Callback_Filtered_Match0() =>
            LitheFiltered0Query.ForEach((in LitheEcs.Entity entity, ref Position position, ref Velocity velocity) => position.X += 1);

        [Benchmark, BenchmarkCategory("Query.Filtered100Invalidated")]
        public void LitheECS_Two_Callback_Filtered_Match100_AfterInvalidation()
        {
            _litheFilterInvalidationEntity.Add(new Disabled());
            _litheFilterInvalidationEntity.Remove<Disabled>();
            LitheFilteredQuery.ForEach(
                (in LitheEcs.Entity entity, ref Position position, ref Velocity velocity) => position.X += 1);
        }

        [Benchmark, BenchmarkCategory("LitheOnly.Lookup.Singleton")]
        public LitheEcs.Entity LitheECS_Singleton_Lookup() => _lookupWorld.Singleton<LocalPlayer>();

        [Benchmark, BenchmarkCategory("LitheOnly.Lookup.Bind")]
        public LitheEcs.Entity LitheECS_Bind_Lookup()
        {
            _lookupWorld.TryGetEntity(_boundObject, out var entity);
            return entity;
        }

        [Benchmark, BenchmarkCategory("LitheOnly.Lookup.Bind")]
        public void LitheECS_Steady_BindStruct_NoAlloc() => _lookupEntity.Bind(_boundStruct);

        [Benchmark, BenchmarkCategory("LitheOnly.Lookup.Bind")]
        public LitheEcs.Entity LitheECS_BindStruct_Lookup()
        {
            _lookupWorld.TryGetEntity(_boundStruct, out var entity);
            return entity;
        }

        [Benchmark, BenchmarkCategory("LitheOnly.EntityAccess")]
        public float LitheECS_Entity_TryGet_Hit() =>
            _lookupEntity.TryGet<Position>(out var position) ? position.X : -1;

        [Benchmark, BenchmarkCategory("LitheOnly.EntityAccess")]
        public bool LitheECS_Entity_TryGet_Miss() =>
            _lookupEntity.TryGet<Velocity>(out _);

        [Benchmark, BenchmarkCategory("LitheOnly.EntityAccess")]
        public float LitheECS_Entity_TryGetRef_Hit() =>
            _lookupEntity.TryGetRef<Position>(out var position) ? position.Value.X : -1;

        [Benchmark, BenchmarkCategory("LitheOnly.EntityAccess")]
        public bool LitheECS_Entity_TryGetRef_Miss() =>
            _lookupEntity.TryGetRef<Velocity>(out _);

        [Benchmark, BenchmarkCategory("Query.3Components")]
        public void LitheECS_Three()
        {
            foreach (var (position, velocity, acceleration) in _litheThreeQuery)
                position.Value.X += 1;
        }

        [Benchmark, BenchmarkCategory("Query.4Components")]
        public void LitheECS_Four()
        {
            foreach (var (position, velocity, acceleration, health) in _litheFourQuery)
                position.Value.X += 1;
        }

    }

    public class LitheEcsReleaseBenchmark : EcsBenchmarkBase
    {
    }
}
