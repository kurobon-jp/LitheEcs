using System;
using System.Numerics;
using NUnit.Framework;
using LitheEcs;

namespace LitheEcs.Tests
{
    // --- Test component definitions ---
    public struct Position
    {
        public Vector3 Value;
        public Position(float x, float y, float z) => Value = new Vector3(x, y, z);
    }

    public struct Velocity
    {
        public Vector3 Value;
        public Velocity(float x, float y, float z) => Value = new Vector3(x, y, z);
    }

    public struct Acceleration
    {
        public Vector3 Value;
        public Acceleration(float x, float y, float z) => Value = new Vector3(x, y, z);
    }

    public struct Health
    {
        public int Value;
        public Health(int value) => Value = value;
    }

    public class TestData
    {
        public string Name = string.Empty;
        public int Score;
    }

    public readonly struct BindingKey : IEquatable<BindingKey>
    {
        public readonly int Value;
        public BindingKey(int value) => Value = value;
        public bool Equals(BindingKey other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is BindingKey other && Equals(other);
        public override int GetHashCode() => Value;
    }

    public struct FriendsWith
    {
    } // Tag used by Relation tests.

    public struct LocalPlayer : ISingleton
    {
    }

    public struct GameSession : ISingleton
    {
    }

    public struct Player
    {
    }

    public struct Disabled
    {
    }

    public struct Grounded
    {
    }

    public struct Flying
    {
    }

    public struct FilterRequired { }
    public struct FilterExcluded { }
    public struct FilterAnyA { }
    public struct FilterAnyB { }
    public struct OverflowSeed { }
    public struct OverflowComponent<T> { }

    public struct Move1Action : IQueryAction<Position>
    {
        public int Count;
        public Entity LastEntity;

        public void Execute(in Entity entity, ref Position position)
        {
            position.Value += Vector3.One;
            LastEntity = entity;
            Count++;
        }
    }

    public struct Move2Action : IQueryAction<Position, Velocity>
    {
        public int Count;

        public void Execute(in Entity entity, ref Position position, ref Velocity velocity)
        {
            position.Value += velocity.Value;
            Count++;
        }
    }

    public struct Move3Action : IQueryAction<Position, Velocity, Acceleration>
    {
        public int Count;

        public void Execute(in Entity entity, ref Position position, ref Velocity velocity,
            ref Acceleration acceleration)
        {
            position.Value += velocity.Value + acceleration.Value;
            Count++;
        }
    }

    public struct Move5Action : IQueryAction<Position, Velocity, Acceleration, Health, Player>
    {
        public int Count;

        public void Execute(in Entity entity, ref Position position, ref Velocity velocity,
            ref Acceleration acceleration, ref Health health, ref Player player)
        {
            health.Value++;
            Count++;
        }
    }

    [TestFixture]
    public class LitheEcsTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            _world = new World();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        #region --- 1. Entity Lifecycle Tests ---

        [Test]
        public void Spawn_ShouldCreateAliveEntity()
        {
            var entity = _world.Spawn();

            Assert.That(entity.IsAlive, Is.True);
            Assert.That(_world.IsAlive(entity), Is.True);
            Assert.That(entity.Version, Is.EqualTo(1u));
        }

        [Test]
        public void EntityId_ShouldBeUnmanagedAndResolveWithinWorld()
        {
            AssertUnmanaged<EntityId>();
            var entity = _world.Spawn();

            Assert.That(entity.Id.Index, Is.EqualTo(entity.Index));
            Assert.That(entity.Id.Version, Is.EqualTo(entity.Version));
            Assert.That(_world.IsAlive(entity.Id), Is.True);
            Assert.That(_world.TryGetEntity(entity.Id, out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(entity));
        }

        [Test]
        public void EntityId_DefaultAndStaleValues_ShouldNotResolve()
        {
            Assert.That(_world.IsAlive(default(EntityId)), Is.False);
            Assert.That(_world.TryGetEntity(default(EntityId), out var missing), Is.False);
            Assert.That(missing, Is.EqualTo(default(Entity)));

            var entity = _world.Spawn();
            var staleId = entity.Id;
            _world.Despawn(entity);

            Assert.That(_world.IsAlive(staleId), Is.False);
            Assert.That(_world.TryGetEntity(staleId, out _), Is.False);
            Assert.That(_world.Spawn().Version, Is.EqualTo(2u));
        }

        private static void AssertUnmanaged<T>() where T : unmanaged
        {
        }

        [Test]
        public void Despawn_ShouldKillEntityAndInvalidateHandle()
        {
            var entity = _world.Spawn();
            _world.Despawn(entity);

            Assert.That(entity.IsAlive, Is.False);
            Assert.That(_world.IsAlive(entity), Is.False);
        }

        [Test]
        public void ReuseEntityIndex_ShouldIncrementVersion()
        {
            var e1 = _world.Spawn();
            int index = e1.Index;
            uint version1 = e1.Version;

            _world.Despawn(e1);

            var e2 = _world.Spawn();

            Assert.That(e2.Index, Is.EqualTo(index));
            Assert.That(e2.Version, Is.Not.EqualTo(version1)); // The generation was incremented.
            Assert.That(e1.IsAlive, Is.False); // The stale handle is invalid.
            Assert.That(e2.IsAlive, Is.True); // The new handle is valid.
        }

        [Test]
        public void EntityFromAnotherWorld_ShouldNotBeAccepted()
        {
            using var anotherWorld = new World();
            var foreignEntity = anotherWorld.Spawn();

            Assert.That(_world.IsAlive(foreignEntity), Is.False);
            Assert.Throws<InvalidOperationException>(() => _world.AddComponent(foreignEntity, new Position()));
        }

        [Test]
        public void Entity_ToString_ShouldIncludeIdentityAndHandleDefaultEntity()
        {
            var entity = _world.Spawn();

            Assert.That(entity.ToString(), Does.StartWith(
                $"Entity(Index: {entity.Index}, Version: {entity.Version}, World: "));
            Assert.That(entity.ToString(), Does.EndWith(")"));
            Assert.That(default(Entity).ToString(), Is.EqualTo("Entity(None)"));
        }

        [Test]
        public void Entity_TryGet_ShouldReturnComponentCopyOrFalse()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(1, 2, 3));

            Assert.That(entity.TryGet<Position>(out var position), Is.True);
            Assert.That(position.Value, Is.EqualTo(new Vector3(1, 2, 3)));

            position.Value = Vector3.Zero;
            Assert.That(entity.Get<Position>().Value, Is.EqualTo(new Vector3(1, 2, 3)));

            Assert.That(entity.TryGet<Velocity>(out var velocity), Is.False);
            Assert.That(velocity, Is.EqualTo(default(Velocity)));
        }

        [Test]
        public void Entity_TryGet_ShouldReturnFalseForInvalidEntity()
        {
            Assert.That(default(Entity).TryGet<Position>(out var missing), Is.False);
            Assert.That(missing, Is.EqualTo(default(Position)));

            var entity = _world.Spawn();
            entity.Add(new Position(1, 2, 3));
            _world.Despawn(entity);

            Assert.That(entity.TryGet<Position>(out var despawned), Is.False);
            Assert.That(despawned, Is.EqualTo(default(Position)));
        }

        [Test]
        public void Entity_TryGetRef_ShouldReturnMutableComponentReferenceOrFalse()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(1, 2, 3));

            Assert.That(entity.TryGetRef<Position>(out var position), Is.True);
            position.Value.Value = new Vector3(4, 5, 6);
            Assert.That(entity.Get<Position>().Value, Is.EqualTo(new Vector3(4, 5, 6)));

            Assert.That(entity.TryGetRef<Velocity>(out _), Is.False);

            _world.Despawn(entity);
            Assert.That(entity.TryGetRef<Position>(out _), Is.False);
            Assert.That(default(Entity).TryGetRef<Position>(out _), Is.False);
        }

        [Test]
        public void World_ShouldRejectNegativeCapacityAndNegativeBatchCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new World(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _world.SpawnBatch(-1, Span<Entity>.Empty));
        }

        [Test]
        public void World_Dispose_ShouldInvalidateEntitiesQueriesAndReleaseOwnedState()
        {
            var entity = SpawnMovingEntity();
            var query = _world.Query<Position, Velocity>();
            Assert.That(CountQuery(query), Is.EqualTo(1));

            _world.Dispose();
            _world.Dispose();

            Assert.That(entity.IsAlive, Is.False);
            Assert.Throws<ObjectDisposedException>(() => _world.Spawn());
            Assert.Throws<ObjectDisposedException>(() => entity.Add(new Health(1)));
            Assert.Throws<ObjectDisposedException>(() => CountQuery(query));

            using var freshWorld = new World();
            Assert.That(CountQuery(freshWorld.Query<Position, Velocity>()), Is.Zero);

            static int CountQuery(Query<Position, Velocity> value)
            {
                int count = 0;
                foreach (var _ in value) count++;
                return count;
            }
        }

        [Test]
        public void DisposingOneWorld_ShouldNotAffectAnotherWorld()
        {
            using var worldA = new World();
            using var worldB = new World();

            var entityA = worldA.Spawn();
            entityA.Add(new Position(9, 9, 9));

            var entityB = worldB.Spawn();
            entityB.Add(new Position(1, 2, 3));
            entityB.Add(new Velocity(4, 5, 6));
            var queryB = worldB.Query<Position, Velocity>();

            worldA.Dispose();

            Assert.That(entityA.IsAlive, Is.False);
            Assert.That(entityB.IsAlive, Is.True);
            Assert.That(entityB.Get<Position>().Value, Is.EqualTo(new Vector3(1, 2, 3)));

            int count = 0;
            foreach (var (position, velocity) in queryB)
            {
                position.Value.Value += velocity.Value.Value;
                count++;
            }

            Assert.That(count, Is.EqualTo(1));
            Assert.That(entityB.Get<Position>().Value, Is.EqualTo(new Vector3(5, 7, 9)));
        }

        [Test]
        public void Bind_ShouldResolveExternalObjectToEntityByReference()
        {
            var entity = _world.Spawn();
            var externalObject = new TestData { Name = "collider" };

            entity.Bind(externalObject);

            Assert.That(_world.TryGetEntity(externalObject, out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(entity));
            Assert.That(_world.GetEntity(externalObject), Is.EqualTo(entity));
            Assert.That(_world.TryGetEntity(new TestData { Name = "collider" }, out _), Is.False);
        }

        [Test]
        public void Bind_ShouldResolveStructKeyByValue()
        {
            var entity = _world.Spawn();
            entity.Bind(new BindingKey(42));

            Assert.That(_world.TryGetEntity(new BindingKey(42), out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(entity));
            Assert.That(_world.GetEntity(new BindingKey(42)), Is.EqualTo(entity));
            Assert.That(_world.TryGetEntity(new BindingKey(43), out _), Is.False);
        }

        [Test]
        public void StructBinding_LookupShouldNotAllocateAfterWarmup()
        {
            var entity = _world.Spawn();
            var key = new BindingKey(42);
            entity.Bind(key);
            _world.TryGetEntity(key, out _);
            _world.GetEntity(key);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1_000; i++)
            {
                _world.TryGetEntity(key, out _);
                _world.GetEntity(key);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void TryGetRef_ShouldNotAllocateForRegisteredComponent()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(1, 2, 3));

            var before = GC.GetAllocatedBytesForCurrentThread();
            var found = entity.TryGetRef<Position>(out var component);
            component.Value.Value.X = 4;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(found, Is.True);
            Assert.That(allocated, Is.Zero);
            Assert.That(entity.Get<Position>().Value.X, Is.EqualTo(4));
        }

        [Test]
        public void Bind_ShouldRejectEqualStructValueForDifferentEntity()
        {
            var first = _world.Spawn();
            var second = _world.Spawn();
            first.Bind(new BindingKey(42));

            Assert.Throws<InvalidOperationException>(() => second.Bind(new BindingKey(42)));
            Assert.That(_world.GetEntity(new BindingKey(42)), Is.EqualTo(first));
        }

        [Test]
        public void StructBinding_ShouldUnbindAndCleanUpOnDespawn()
        {
            var first = _world.Spawn();
            first.Bind(new BindingKey(1));
            first.Bind(new BindingKey(2));

            Assert.That(first.Unbind(new BindingKey(1)), Is.True);
            Assert.That(_world.TryGetEntity(new BindingKey(1), out _), Is.False);

            _world.Despawn(first);
            Assert.That(_world.TryGetEntity(new BindingKey(2), out _), Is.False);
        }

        [Test]
        public void Bind_ShouldKeepReferenceIdentityForClassesWhenStructsAreSupported()
        {
            var first = _world.Spawn();
            var second = _world.Spawn();
            var firstObject = new TestData { Name = "same" };
            var secondObject = new TestData { Name = "same" };

            first.Bind(firstObject);
            second.Bind(secondObject);

            Assert.That(_world.GetEntity(firstObject), Is.EqualTo(first));
            Assert.That(_world.GetEntity(secondObject), Is.EqualTo(second));
        }

        [Test]
        public void ClassBinding_LookupShouldRemainAllocationFreeAfterWarmup()
        {
            var entity = _world.Spawn();
            var key = new TestData();
            entity.Bind(key);
            _world.TryGetEntity(key, out _);
            _world.GetEntity(key);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1_000; i++)
            {
                _world.TryGetEntity(key, out _);
                _world.GetEntity(key);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Bind_ShouldAllowMultipleObjectsPerEntityAndUnbindIndividually()
        {
            var entity = _world.Spawn();
            var colliderA = new TestData { Name = "A" };
            var colliderB = new TestData { Name = "B" };

            _world.Bind(colliderA, entity);
            _world.Bind(colliderB, entity);

            Assert.That(entity.Unbind(colliderA), Is.True);
            Assert.That(_world.TryGetEntity(colliderA, out _), Is.False);
            Assert.That(_world.TryGetEntity(colliderB, out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(entity));
        }

        [Test]
        public void Bind_ShouldRejectBindingSameObjectToDifferentEntity()
        {
            var first = _world.Spawn();
            var second = _world.Spawn();
            var externalObject = new TestData();

            _world.Bind(externalObject, first);
            _world.Bind(externalObject, first);

            Assert.Throws<InvalidOperationException>(() => _world.Bind(externalObject, second));
            Assert.That(_world.GetEntity(externalObject), Is.EqualTo(first));
        }

        [Test]
        public void Despawn_ShouldAutomaticallyRemoveAllBindingsBeforeIndexReuse()
        {
            var entity = _world.Spawn();
            var colliderA = new TestData();
            var colliderB = new object();
            entity.Bind(colliderA);
            entity.Bind(colliderB);

            _world.Despawn(entity);
            var reused = _world.Spawn();

            Assert.That(reused.Index, Is.EqualTo(entity.Index));
            Assert.That(_world.TryGetEntity(colliderA, out _), Is.False);
            Assert.That(_world.TryGetEntity(colliderB, out _), Is.False);
        }

        [Test]
        public void Bind_ShouldRejectNullDeadAndForeignEntities()
        {
            var entity = _world.Spawn();
            _world.Despawn(entity);

            Assert.Throws<ArgumentNullException>(() => _world.Bind<TestData>(null!, _world.Spawn()));
            Assert.Throws<InvalidOperationException>(() => _world.Bind(new TestData(), entity));

            using var anotherWorld = new World();
            Assert.Throws<InvalidOperationException>(() => _world.Bind(new TestData(), anotherWorld.Spawn()));
        }

        #endregion

        #region --- Singleton Entity Tests ---

        [Test]
        public void Singleton_ShouldReturnEntityOwningMarkerAndRegularComponents()
        {
            var player = _world.Spawn();
            player.Add<LocalPlayer>();
            player.Add(new Health(100));

            Assert.That(_world.Singleton<LocalPlayer>(), Is.EqualTo(player));
            Assert.That(_world.HasSingleton<LocalPlayer>(), Is.True);
            Assert.That(_world.TryGetSingleton<LocalPlayer>(out var resolved), Is.True);
            Assert.That(resolved.Get<Health>().Value, Is.EqualTo(100));
        }

        [Test]
        public void Singleton_LookupShouldNotAllocateAfterRegistration()
        {
            var player = _world.Spawn();
            player.Add<LocalPlayer>();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1_000; i++)
                _world.Singleton<LocalPlayer>();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Singleton_ShouldRejectSameMarkerOnDifferentEntity()
        {
            var first = _world.Spawn();
            var second = _world.Spawn();
            first.Add<LocalPlayer>();

            Assert.Throws<InvalidOperationException>(() => second.Add<LocalPlayer>());
            Assert.That(second.Has<LocalPlayer>(), Is.False);
            Assert.That(_world.Singleton<LocalPlayer>(), Is.EqualTo(first));
        }

        [Test]
        public void Singleton_RemoveMarker_ShouldKeepEntityAndAllowNewOwner()
        {
            var first = _world.Spawn();
            first.Add<LocalPlayer>();
            first.Add(new Health(100));

            Assert.That(first.Remove<LocalPlayer>(), Is.True);
            Assert.That(first.IsAlive, Is.True);
            Assert.That(first.Get<Health>().Value, Is.EqualTo(100));
            Assert.That(_world.TryGetSingleton<LocalPlayer>(out _), Is.False);

            var second = _world.Spawn();
            second.Add<LocalPlayer>();
            Assert.That(_world.Singleton<LocalPlayer>(), Is.EqualTo(second));
        }

        [Test]
        public void Singleton_Despawn_ShouldRemoveRegistrationAndAllowReplacement()
        {
            var player = _world.Spawn();
            player.Add<LocalPlayer>();
            player.Add(new Health(100));

            _world.Despawn(player);

            Assert.That(_world.HasSingleton<LocalPlayer>(), Is.False);
            Assert.Throws<InvalidOperationException>(() => _world.Singleton<LocalPlayer>());

            var replacement = _world.Spawn();
            replacement.Add<LocalPlayer>();
            Assert.That(_world.Singleton<LocalPlayer>(), Is.EqualTo(replacement));
        }

        [Test]
        public void Singleton_ShouldAllowDifferentMarkersOnSameEntity()
        {
            var globals = _world.Spawn();
            globals.Add<LocalPlayer>();
            globals.Add<GameSession>();

            Assert.That(_world.Singleton<LocalPlayer>(), Is.EqualTo(globals));
            Assert.That(_world.Singleton<GameSession>(), Is.EqualTo(globals));
        }

        [Test]
        public void Singleton_BatchAdd_ShouldRejectMultipleOwnersWithoutPartialMutation()
        {
            var first = _world.Spawn();
            var second = _world.Spawn();
            var entities = new[] { first, second };

            Assert.Throws<InvalidOperationException>(() => _world.AddComponentBatch<LocalPlayer>(entities, default));
            Assert.That(first.Has<LocalPlayer>(), Is.False);
            Assert.That(second.Has<LocalPlayer>(), Is.False);
            Assert.That(_world.HasSingleton<LocalPlayer>(), Is.False);
        }

        #endregion

        #region --- 2. Struct Component Tests ---

        [Test]
        public void AddAndGetComponent_ShouldStoreAndReturnCorrectData()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(10, 20, 30));

            Assert.That(entity.Has<Position>(), Is.True);

            ref var pos = ref entity.Get<Position>();
            Assert.That(pos.Value.X, Is.EqualTo(10f));

            // Verify direct value mutation through a ref return.
            pos.Value.X = 99f;
            Assert.That(entity.Get<Position>().Value.X, Is.EqualTo(99f));
        }

        [Test]
        public void RemoveComponent_ShouldRemoveComponentSuccessfully()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(1, 2, 3));

            bool removed = entity.Remove<Position>();

            Assert.That(removed, Is.True);
            Assert.That(entity.Has<Position>(), Is.False);
        }

        #endregion

        #region --- 3. Managed Component Tests ---

        [Test]
        public void AddAndGetManagedComponent_ShouldHandleClassObjects()
        {
            var entity = _world.Spawn();
            var data = new TestData { Name = "Hero", Score = 100 };

            _world.AddComponent(entity, Link.With(data));

            Assert.That(entity.Has<Link<TestData>>(), Is.True);

            var retrievedData = entity.GetLink<TestData>();
            Assert.That(retrievedData, Is.Not.Null);
            Assert.That(retrievedData.Name, Is.EqualTo("Hero"));
            Assert.That(retrievedData.Score, Is.EqualTo(100));
        }

        [Test]
        public void LinkToString_ShouldIncludeTypeAndValue()
        {
            Assert.That(Link.With("Player").ToString(), Is.EqualTo("Link<String>(Player)"));
            Assert.That(new Link<string>(null!).ToString(), Is.EqualTo("Link<String>(null)"));
        }

        #endregion

        #region --- 4. Sparse Relation Tests ---

        [Test]
        public void Relation_ShouldAddAndGetRelationsCorrectly()
        {
            var source = _world.Spawn();
            var target1 = _world.Spawn();
            var target2 = _world.Spawn();

            source.AddRelation<FriendsWith>(target1);
            source.AddRelation<FriendsWith>(target2);

            ReadOnlySpan<Entity> relations = source.GetRelations<FriendsWith>();

            Assert.That(relations.Length, Is.EqualTo(2));
            Assert.That(relations[0], Is.EqualTo(target1));
            Assert.That(relations[1], Is.EqualTo(target2));
            Assert.That(source.HasRelation<FriendsWith>(target1), Is.True);
            Assert.That(source.RemoveRelation<FriendsWith>(target1), Is.True);
            Assert.That(source.HasRelation<FriendsWith>(target1), Is.False);
            Assert.That(source.GetRelations<FriendsWith>().ToArray(), Is.EqualTo(new[] { target2 }));
        }

        [Test]
        public void ReserveRelation_ShouldAvoidAddRelationAllocations()
        {
            const int count = 128;
            using var world = new World(count + 1);
            world.ReserveRelation<FriendsWith>(count);
            var source = world.Spawn();
            var targets = new Entity[count];
            world.SpawnBatch(count, targets);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < count; i++) source.AddRelation<FriendsWith>(targets[i]);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ReserveRelation_ShouldRejectNegativeCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _world.ReserveRelation<FriendsWith>(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _world.ReserveRelation<FriendsWith>(0, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _world.ReserveRelation<FriendsWith>(0, 0, -1));
        }

        [Test]
        public void ReserveRelation_WithSearchCapacities_ShouldAvoidSearchAllocations()
        {
            const int count = 128;
            using var world = new World(count + 1);
            world.ReserveRelation<FriendsWith>(count * 2, count, count);
            var source = world.Spawn();
            var target = world.Spawn();
            var forwardTargets = new Entity[count];
            var backwardSources = new Entity[count];
            world.SpawnBatch(count, forwardTargets);
            world.SpawnBatch(count, backwardSources);

            for (var i = 0; i < count; i++)
            {
                source.AddRelation<FriendsWith>(forwardTargets[i]);
                backwardSources[i].AddRelation<FriendsWith>(target);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            var forwardCount = source.GetRelations<FriendsWith>().Length;
            var backwardCount = world.GetEntitiesWithTarget<FriendsWith>(target).Length;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(forwardCount, Is.EqualTo(count));
            Assert.That(backwardCount, Is.EqualTo(count));
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Relation_RemoveRelationWithoutTarget_ShouldRemoveAllRelationsOfType()
        {
            var source = _world.Spawn();
            var target1 = _world.Spawn();
            var target2 = _world.Spawn();
            source.AddRelation<FriendsWith>(target1);
            source.AddRelation<FriendsWith>(target2);

            Assert.That(source.RemoveRelation<FriendsWith>(), Is.True);
            Assert.That(source.GetRelations<FriendsWith>().Length, Is.EqualTo(0));
            Assert.That(_world.GetEntitiesWithTarget<FriendsWith>(target1).Length, Is.EqualTo(0));
            Assert.That(_world.GetEntitiesWithTarget<FriendsWith>(target2).Length, Is.EqualTo(0));
            Assert.That(source.RemoveRelation<FriendsWith>(), Is.False);
        }

        [Test]
        public void Relation_SingleAccess_ShouldReturnTheOnlyTarget()
        {
            var source = _world.Spawn();
            var target = _world.Spawn();
            source.AddRelation<FriendsWith>(target);

            Assert.That(source.GetRelation<FriendsWith>(), Is.EqualTo(target));
            Assert.That(source.TryGetRelation<FriendsWith>(out var actual), Is.True);
            Assert.That(actual, Is.EqualTo(target));
        }

        [Test]
        public void Relation_SetRelation_ShouldReplaceOutgoingRelations()
        {
            var source = _world.Spawn();
            var first = _world.Spawn();
            var second = _world.Spawn();
            source.AddRelation<FriendsWith>(first);
            source.AddRelation<FriendsWith>(second);

            source.SetRelation<FriendsWith>(first);

            Assert.That(source.GetRelations<FriendsWith>().ToArray(), Is.EqualTo(new[] { first }));
            Assert.That(_world.GetEntitiesWithTarget<FriendsWith>(second).Length, Is.Zero);
        }

        [Test]
        public void Relation_SetRelation_DefaultEntity_ShouldClearOutgoingRelations()
        {
            var source = _world.Spawn();
            var target = _world.Spawn();
            source.AddRelation<FriendsWith>(target);

            source.SetRelation<FriendsWith>(default);

            Assert.That(source.GetRelations<FriendsWith>().Length, Is.Zero);
            Assert.That(_world.GetEntitiesWithTarget<FriendsWith>(target).Length, Is.Zero);
        }

        [Test]
        public void Relation_SingleAccess_ShouldRejectMissingOrMultipleTargets()
        {
            var source = _world.Spawn();

            Assert.That(source.TryGetRelation<FriendsWith>(out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => source.GetRelation<FriendsWith>());

            source.AddRelation<FriendsWith>(_world.Spawn());
            source.AddRelation<FriendsWith>(_world.Spawn());

            Assert.That(source.TryGetRelation<FriendsWith>(out _), Is.False);
            var exception = Assert.Throws<InvalidOperationException>(() => source.GetRelation<FriendsWith>());
            Assert.That(exception!.Message, Does.Contain("GetRelations<FriendsWith>()"));
        }

        [Test]
        public void Relation_ShouldSupportReverseLookup()
        {
            var user1 = _world.Spawn();
            var user2 = _world.Spawn();
            var targetGroup = _world.Spawn();

            user1.AddRelation<FriendsWith>(targetGroup);
            user2.AddRelation<FriendsWith>(targetGroup);

            // Reverse lookup: Entities that reference the target.
            ReadOnlySpan<Entity> sources = _world.GetEntitiesWithTarget<FriendsWith>(targetGroup);

            Assert.That(sources.Length, Is.EqualTo(2));
            Assert.That(sources[0], Is.EqualTo(user1));
            Assert.That(sources[1], Is.EqualTo(user2));
        }

        [Test]
        public void Despawn_ShouldCleanupRelations()
        {
            var source = _world.Spawn();
            var target = _world.Spawn();

            source.AddRelation<FriendsWith>(target);
            _world.Despawn(source);

            // Verify that removing the source also removes it from the target's reverse lookup.
            ReadOnlySpan<Entity> sources = _world.GetEntitiesWithTarget<FriendsWith>(target);
            Assert.That(sources.Length, Is.EqualTo(0));
        }

        [Test]
        public void DespawnTarget_ShouldCleanupForwardRelations()
        {
            var source = _world.Spawn();
            var target = _world.Spawn();
            source.AddRelation<FriendsWith>(target);

            _world.Despawn(target);

            Assert.That(source.GetRelations<FriendsWith>().Length, Is.EqualTo(0));
        }

        [Test]
        public void Relation_ShouldNotAlsoRegisterAComponent()
        {
            var source = _world.Spawn();
            var target = _world.Spawn();

            source.AddRelation<FriendsWith>(target);

            Assert.That(source.Has<FriendsWith>(), Is.False);
            Assert.That(source.GetRelations<FriendsWith>().Length, Is.EqualTo(1));
        }

        #endregion

        #region --- 5. Query Tests ---

        [Test]
        public void Query_SingleComponent_ShouldMatchCorrectEntities()
        {
            var e1 = _world.Spawn();
            e1.Add(new Position(1, 0, 0));

            var e2 = _world.Spawn(); // No Position component.

            var e3 = _world.Spawn();
            e3.Add(new Position(3, 0, 0));

            int count = 0;
            foreach (ref var pos in _world.Query<Position>())
            {
                count++;
                pos.Value.Y += 10f; // Mutate the value.
            }

            Assert.That(count, Is.EqualTo(2));
            Assert.That(e1.Get<Position>().Value.Y, Is.EqualTo(10f));
            Assert.That(e3.Get<Position>().Value.Y, Is.EqualTo(10f));
        }

        [Test]
        public void Query_MultipleComponents_ShouldMatchTupleAndSupportDeconstruction()
        {
            var e1 = _world.Spawn();
            e1.Add(new Position(1, 2, 3));
            e1.Add(new Velocity(1, 1, 1));
            e1.Add(Link.With(new TestData()));
            var e2 = _world.Spawn();
            e2.Add(new Position(10, 20, 30)); // No Velocity component.

            int count = 0;
            foreach (var (pos, vel) in _world.Query<Position, Velocity>())
            {
                count++;
                pos.Value.Value += vel.Value.Value;
            }

            Assert.That(count, Is.EqualTo(1));
            Assert.That(e1.Get<Position>().Value, Is.EqualTo(new Vector3(2, 3, 4)));
        }

        [Test]
        public void Query_Callback_ShouldReceiveEntityAndMutableComponents()
        {
            var matching = _world.Spawn();
            matching.Add(new Position(1, 2, 3));
            matching.Add(new Velocity(4, 5, 6));
            var positionOnly = _world.Spawn();
            positionOnly.Add(new Position(10, 20, 30));

            Entity visited = default;
            int count = 0;
            _world.Query<Position, Velocity>()
                .ForEach((in Entity entity, ref Position position, ref Velocity velocity) =>
                {
                    visited = entity;
                    position.Value += velocity.Value;
                    count++;
                });

            Assert.That(count, Is.EqualTo(1));
            Assert.That(visited, Is.EqualTo(matching));
            Assert.That(matching.Get<Position>().Value, Is.EqualTo(new Vector3(5, 7, 9)));
            Assert.That(positionOnly.Get<Position>().Value, Is.EqualTo(new Vector3(10, 20, 30)));
        }

        [Test]
        public void Query_SingleComponentForEach_ShouldSupportDelegateAndStructAction()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(1, 2, 3));
            Entity visited = default;

            _world.Query<Position>().ForEach((in Entity current, ref Position position) =>
            {
                visited = current;
                position.Value += Vector3.One;
            });

            var action = new Move1Action();
            _world.Query<Position>().ForEach(ref action);

            Assert.That(visited, Is.EqualTo(entity));
            Assert.That(action.LastEntity, Is.EqualTo(entity));
            Assert.That(action.Count, Is.EqualTo(1));
            Assert.That(entity.Get<Position>().Value, Is.EqualTo(new Vector3(3, 4, 5)));
            Assert.DoesNotThrow(() => _world.Query<Health>().ForEach(
                (in Entity _, ref Health _) => Assert.Fail("Empty query invoked the callback.")));
        }

#if !RELEASE && !DISABLE_LITHEECS_VALIDATION
        [Test]
        public void Entity_TryGetRef_DebugBuild_ShouldRejectStaleReference()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(1, 2, 3));
            Assert.That(entity.TryGetRef<Position>(out var position), Is.True);

            entity.Remove<Position>();

            InvalidOperationException? exception = null;
            try
            {
                _ = position.Value;
            }
            catch (InvalidOperationException caught)
            {
                exception = caught;
            }

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Message, Does.Contain("TryGetRef<T>()"));
        }

        [Test]
        public void Query_ShouldFailFastWhenWorldStructureChangesDuringExecution()
        {
            var first = SpawnMovingEntity();
            var second = SpawnMovingEntity();

            var componentError = Assert.Throws<InvalidOperationException>(() =>
                _world.Query<Position>().ForEach((in Entity entity, ref Position position) =>
                    entity.Remove<Velocity>()));
            Assert.That(componentError!.Message, Does.Contain("EntityCommandBuffer"));

            first.Add(new Velocity());
            Assert.Throws<InvalidOperationException>(() =>
            {
                foreach (var _ in _world.Query<Position, Velocity>())
                    _world.Despawn(second);
            });
        }

        [Test]
        public void Query_ShouldTreatRelationChangesAsStructuralChanges()
        {
            var source = SpawnMovingEntity();
            var target = _world.Spawn();

            Assert.Throws<InvalidOperationException>(() =>
                _world.Query<Position>().ForEach((in Entity _, ref Position _) =>
                    source.AddRelation<FriendsWith>(target)));
        }
#endif

        [Test]
        public void Query_Filters_ShouldApplyWithWithoutAndAny()
        {
            var groundedPlayer = SpawnMovingEntity();
            groundedPlayer.Add(new Player());
            groundedPlayer.Add(new Grounded());

            var flyingPlayer = SpawnMovingEntity();
            flyingPlayer.Add(new Player());
            flyingPlayer.Add(new Flying());

            var disabledPlayer = SpawnMovingEntity();
            disabledPlayer.Add(new Player());
            disabledPlayer.Add(new Grounded());
            disabledPlayer.Add(new Disabled());

            var nonPlayer = SpawnMovingEntity();
            nonPlayer.Add(new Grounded());

            var visited = new HashSet<Entity>();
            _world.Query<Position, Velocity>()
                .With<Player>()
                .Without<Disabled>()
                .Any<Grounded, Flying>()
                .ForEach((in Entity entity, ref Position position, ref Velocity velocity) =>
                {
                    visited.Add(entity);
                    position.Value += velocity.Value;
                });

            Assert.That(visited, Is.EquivalentTo(new[] { groundedPlayer, flyingPlayer }));
            Assert.That(groundedPlayer.Get<Position>().Value, Is.EqualTo(Vector3.One));
            Assert.That(flyingPlayer.Get<Position>().Value, Is.EqualTo(Vector3.One));
            Assert.That(disabledPlayer.Get<Position>().Value, Is.EqualTo(Vector3.Zero));
            Assert.That(nonPlayer.Get<Position>().Value, Is.EqualTo(Vector3.Zero));
        }

        [Test]
        public void Query_Filters_ShouldAlsoApplyToForeach()
        {
            var included = SpawnMovingEntity();
            included.Add(new Player());
            var excluded = SpawnMovingEntity();
            excluded.Add(new Player());
            excluded.Add(new Disabled());

            int count = 0;
            foreach (var _ in _world.Query<Position, Velocity>().With<Player>().Without<Disabled>())
                count++;

            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void EntityQuery_With_ShouldReturnMatchingEntities()
        {
            var matching = SpawnMovingEntity();
            var positionOnly = _world.Spawn();
            positionOnly.Add(new Position());
            _world.Spawn().Add(new Velocity());

            var entities = new System.Collections.Generic.List<Entity>();
            foreach (var entity in _world.Query().With<Position>().With<Velocity>())
                entities.Add(entity);

            Assert.That(entities, Is.EquivalentTo(new[] { matching }));
        }

        [Test]
        public void EntityQuery_SingleWithCached_ShouldFollowStorageChanges()
        {
            var first = _world.Spawn();
            first.Add(new Position());
            var second = _world.Spawn();
            var query = _world.Query().With<Position>();

            Assert.That(Collect(query), Is.EquivalentTo(new[] { first }));
            second.Add(new Position());
            Assert.That(Collect(query), Is.EquivalentTo(new[] { first, second }));
            first.Remove<Position>();
            Assert.That(Collect(query), Is.EquivalentTo(new[] { second }));

            static System.Collections.Generic.List<Entity> Collect(EntityQuery<Position> value)
            {
                var result = new System.Collections.Generic.List<Entity>();
                foreach (var entity in value) result.Add(entity);
                return result;
            }
        }

        [Test]
        public void EntityQuery_Warmup_ShouldSupportSingleAndFilteredQueries()
        {
            var first = _world.Spawn();
            first.Add(new Position());
            first.Add(new Player());
            var second = _world.Spawn();
            second.Add(new Position());

            var single = _world.Query().With<Position>().Warmup();
            var filtered = _world.Query().With<Position>().With<Player>().Without<Disabled>().Warmup();

            Assert.That(single.Count, Is.EqualTo(2));
            Assert.That(filtered.Count, Is.EqualTo(1));

            second.Add(new Player());

            Assert.That(filtered.Warmup().Count, Is.EqualTo(2));
        }

        [Test]
        public void EntityQuery_SingleWith_ShouldExposeCountAndIndexedEntities()
        {
            var first = _world.Spawn();
            first.Add(new Position());
            var second = _world.Spawn();
            second.Add(new Position());
            var query = _world.Query().With<Position>();

            Assert.That(query.Count, Is.EqualTo(2));
            Assert.That(new[] { query[0], query[1] }, Is.EquivalentTo(new[] { first, second }));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = query[-1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = query[query.Count]);

            first.Remove<Position>();

            Assert.That(query.Count, Is.EqualTo(1));
            Assert.That(query[0], Is.EqualTo(second));
        }

        [Test]
        public void EntityQuery_SingleWith_MissingStorageShouldBeEmpty()
        {
            var query = _world.Query().With<FilterRequired>();

            Assert.That(query.Count, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = query[0]);
        }

        [Test]
        public void EntityQuery_Result_ShouldExposeFilteredEntitiesByIndex()
        {
            var first = _world.Spawn();
            first.Add(new Position());
            first.Add(new Player());
            var second = _world.Spawn();
            second.Add(new Position());
            second.Add(new Player());
            second.Add(new Disabled());

            var result = _world.Query().With<Position>().With<Player>().Without<Disabled>().Result();
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(first));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = result[1]);
        }

        [Test]
        public void EntityQuery_Result_ShouldReusePlanAndRejectStaleView()
        {
            var first = _world.Spawn();
            first.Add(new Position());
            first.Add(new Player());
            var query = _world.Query().With<Position>().With<Player>();
            var stale = query.Result();

            var unrelated = _world.Spawn();
            unrelated.Add(new Health());
            Assert.That(stale.Count, Is.EqualTo(1));

            var second = _world.Spawn();
            second.Add(new Position());
            second.Add(new Player());

            Assert.Throws<InvalidOperationException>(() => _ = stale.Count);

            var refreshed = query.Result();
            Assert.That(refreshed.Count, Is.EqualTo(2));
            Assert.That(new[] { refreshed[0], refreshed[1] }, Is.EquivalentTo(new[] { first, second }));

            var third = _world.Spawn();
            _world.AddComponentBatch(new[] { third }, new Position());
            Assert.Throws<InvalidOperationException>(() => _ = refreshed.Count);

            var beforeDespawn = query.Result();
            _world.Despawn(first);
            Assert.Throws<InvalidOperationException>(() => _ = beforeDespawn.Count);
        }

        [Test]
        public void EntityQuery_Filters_ShouldCompose()
        {
            var groundedPlayer = SpawnMovingEntity();
            groundedPlayer.Add(new Player());
            groundedPlayer.Add(new Grounded());

            var flyingPlayer = SpawnMovingEntity();
            flyingPlayer.Add(new Player());
            flyingPlayer.Add(new Flying());

            var disabledPlayer = SpawnMovingEntity();
            disabledPlayer.Add(new Player());
            disabledPlayer.Add(new Grounded());
            disabledPlayer.Add(new Disabled());

            SpawnMovingEntity().Add(new Grounded());

            var entities = new System.Collections.Generic.List<Entity>();
            var query = _world.Query().With<Position>().With<Player>()
                .Without<Disabled>().Any<Grounded, Flying>();
            foreach (var entity in query) entities.Add(entity);

            Assert.That(entities, Is.EquivalentTo(new[] { groundedPlayer, flyingPlayer }));
            Assert.That(query.Matches(groundedPlayer), Is.True);
            Assert.That(query.Matches(disabledPlayer), Is.False);
        }

        [Test]
        public void EntityQuery_ShouldReflectStructuralChangesAndMissingComponents()
        {
            var entity = _world.Spawn();
            var query = _world.Query().With<Position>().With<Velocity>();

            Assert.That(Count(query), Is.Zero);
            entity.Add(new Position());
            Assert.That(Count(query), Is.Zero);
            entity.Add(new Velocity());
            Assert.That(Count(query), Is.EqualTo(1));
            entity.Remove<Position>();
            Assert.That(Count(query), Is.Zero);

            static int Count(EntityQuery value)
            {
                var count = 0;
                foreach (var _ in value) count++;
                return count;
            }
        }

        [Test]
        public void Query_FilteredCallback_ShouldRefreshAndExecuteInSinglePassAfterInvalidation()
        {
            var entity = SpawnMovingEntity();
            entity.Add(new Player());
            var query = _world.Query<Position, Velocity>().With<Player>().Without<Disabled>();
            var count = 0;

            query.ForEach((in Entity _, ref Position _, ref Velocity _) => count++);
            entity.Add(new Disabled());
            query.ForEach((in Entity _, ref Position _, ref Velocity _) => count++);
            Assert.That(count, Is.EqualTo(1));
            entity.Remove<Disabled>();
            query.ForEach((in Entity _, ref Position _, ref Velocity _) => count++);

            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void Query_FilteredPlan_ShouldIncrementallyApplyWithAndAnyChanges()
        {
            var entity = SpawnMovingEntity();
            var query = _world.Query<Position, Velocity>().With<Player>().Any<Grounded, Flying>();

            Assert.That(Count(query), Is.Zero);
            entity.Add(new Player());
            Assert.That(Count(query), Is.Zero);
            entity.Add(new Grounded());
            Assert.That(Count(query), Is.EqualTo(1));
            entity.Remove<Player>();
            Assert.That(Count(query), Is.Zero);

            static int Count(Query<Position, Velocity> value)
            {
                var result = 0;
                foreach (var _ in value) result++;
                return result;
            }
        }

        [Test]
        public void Query_FilteredPlan_ShouldRebuildForBatchAndDriverStorageChanges()
        {
            var first = SpawnMovingEntity();
            var second = SpawnMovingEntity();
            var query = _world.Query<Position, Velocity>().Without<Disabled>();

            Assert.That(Count(query), Is.EqualTo(2));
            _world.AddComponentBatch(new[] { first, second }, new Disabled());
            Assert.That(Count(query), Is.Zero);
            first.Remove<Disabled>();
            second.Remove<Disabled>();
            Assert.That(Count(query), Is.EqualTo(2));
            first.Remove<Velocity>();
            Assert.That(Count(query), Is.EqualTo(1));
            first.Add(new Velocity());
            Assert.That(Count(query), Is.EqualTo(2));

            static int Count(Query<Position, Velocity> value)
            {
                var result = 0;
                foreach (var _ in value) result++;
                return result;
            }
        }

        [Test]
        public void Query_Filter_ShouldSupportManagedLinkAsRequiredComponent()
        {
            var linked = SpawnMovingEntity();
            linked.Add(Link.With(new TestData { Name = "linked" }));
            SpawnMovingEntity();

            Entity visited = default;
            _world.Query<Position, Velocity>()
                .With<Link<TestData>>()
                .ForEach((in Entity entity, ref Position position, ref Velocity velocity) => visited = entity);

            Assert.That(visited, Is.EqualTo(linked));
            Assert.That(visited.GetLink<TestData>().Name, Is.EqualTo("linked"));
        }

        [Test]
        public void Query_Filter_ShouldReturnEmptyForMissingOrContradictoryRequirements()
        {
            var entity = SpawnMovingEntity();
            entity.Add(new Player());

            int missingCount = 0;
            foreach (var _ in _world.Query<Position, Velocity>().With<Health>()) missingCount++;

            int contradictoryCount = 0;
            foreach (var _ in _world.Query<Position, Velocity>().With<Player>().Without<Player>()) contradictoryCount++;

            int anyCount = 0;
            foreach (var _ in _world.Query<Position, Velocity>().Any<Grounded, Flying>()) anyCount++;

            Assert.That(missingCount, Is.Zero);
            Assert.That(contradictoryCount, Is.Zero);
            Assert.That(anyCount, Is.Zero);
        }

        [Test]
        public void QueryPlan_ShouldReuseResultsAndRefreshAfterStructuralChanges()
        {
            var first = SpawnMovingEntity();
            var second = _world.Spawn();
            second.Add(new Position());
            var query = _world.Query<Position, Velocity>();

            Assert.That(CountQuery(query), Is.EqualTo(1));
            Assert.That(CountQuery(query), Is.EqualTo(1));

            second.Add(new Velocity());
            Assert.That(CountQuery(query), Is.EqualTo(2));

            first.Remove<Velocity>();
            Assert.That(CountQuery(query), Is.EqualTo(1));

            static int CountQuery(Query<Position, Velocity> value)
            {
                int count = 0;
                foreach (var _ in value) count++;
                return count;
            }
        }

        [Test]
        public void QueryPlan_ThreeAndFour_ShouldRefreshAfterStructuralChanges()
        {
            var first = SpawnMovingEntity();
            first.Add(new Acceleration());
            first.Add(new Health());
            var second = SpawnMovingEntity();
            second.Add(new Acceleration());
            second.Add(new Health());
            var three = _world.Query<Position, Velocity, Acceleration>();
            var four = _world.Query<Position, Velocity, Acceleration, Health>();

            Assert.That(Count3(), Is.EqualTo(2));
            Assert.That(Count4(), Is.EqualTo(2));
            first.Remove<Acceleration>();
            Assert.That(Count3(), Is.EqualTo(1));
            Assert.That(Count4(), Is.EqualTo(1));
            first.Add(new Acceleration());
            second.Remove<Health>();
            Assert.That(Count3(), Is.EqualTo(2));
            Assert.That(Count4(), Is.EqualTo(1));

            int Count3()
            {
                int count = 0;
                foreach (var _ in three) count++;
                return count;
            }

            int Count4()
            {
                int count = 0;
                foreach (var _ in four) count++;
                return count;
            }
        }

        [Test]
        public void DespawnBatch_ShouldRemoveComponentsBindingsRelationsAndSingletons()
        {
            var first = SpawnMovingEntity();
            first.Add<LocalPlayer>();
            var second = SpawnMovingEntity();
            var external = new TestData();
            first.Bind(external);
            first.AddRelation<FriendsWith>(second);

            _world.DespawnBatch(new[] { first, second });

            Assert.That(_world.IsAlive(first), Is.False);
            Assert.That(_world.IsAlive(second), Is.False);
            Assert.That(_world.TryGetEntity(external, out _), Is.False);
            Assert.That(_world.HasSingleton<LocalPlayer>(), Is.False);
            var replacement = SpawnMovingEntity();
            Assert.That(replacement.Index, Is.EqualTo(second.Index));
        }

        [Test]
        public void DespawnBatchFast_ShouldHandleDuplicatesAndKeepQueryPlansCorrect()
        {
            var first = SpawnMovingEntity();
            var second = SpawnMovingEntity();
            var query = _world.Query<Position, Velocity>();
            Assert.That(Count(), Is.EqualTo(2));

            _world.DespawnBatch(new[] { first, second, first, default(Entity) });

            Assert.That(_world.IsAlive(first), Is.False);
            Assert.That(_world.IsAlive(second), Is.False);
            Assert.That(Count(), Is.Zero);
            var replacement1 = _world.Spawn();
            var replacement2 = _world.Spawn();
            Assert.That(replacement1.Index, Is.EqualTo(second.Index));
            Assert.That(replacement2.Index, Is.EqualTo(first.Index));

            int Count()
            {
                var count = 0;
                foreach (var _ in query) count++;
                return count;
            }
        }

        [Test]
        public void QueryPlan_ShouldRemainCorrectAcrossUnrelatedStructuralChanges()
        {
            SpawnMovingEntity();
            var query = _world.Query<Position, Velocity>();

            int Count()
            {
                int count = 0;
                foreach (var _ in query) count++;
                return count;
            }

            Assert.That(Count(), Is.EqualTo(1));
            var unrelated = _world.Spawn();
            unrelated.Add(new Health(10));
            Assert.That(Count(), Is.EqualTo(1));
            unrelated.Remove<Health>();
            Assert.That(Count(), Is.EqualTo(1));
        }

        [Test]
        public void Query_StructAction_ShouldMutateWithoutDelegateCallback()
        {
            var entity = SpawnMovingEntity();
            entity.Add(new Acceleration(2, 2, 2));
            entity.Add(new Health(10));
            entity.Add(new Player());
            var action2 = new Move2Action();
            _world.Query<Position, Velocity>().ForEach(ref action2);
            var action3 = new Move3Action();
            _world.Query<Position, Velocity, Acceleration>().ForEach(ref action3);
            var action5 = new Move5Action();
            _world.Query<Position, Velocity, Acceleration, Health, Player>().ForEach(ref action5);

            Assert.That(action2.Count, Is.EqualTo(1));
            Assert.That(action3.Count, Is.EqualTo(1));
            Assert.That(action5.Count, Is.EqualTo(1));
            Assert.That(entity.Get<Position>().Value, Is.EqualTo(new Vector3(4, 4, 4)));
            Assert.That(entity.Get<Health>().Value, Is.EqualTo(11));
        }

        [Test]
        public void Query_ThreeAndFourComponents_ShouldMatchAndMutateCorrectEntity()
        {
            var complete = SpawnMovingEntity();
            complete.Add(new Acceleration(2, 2, 2));
            complete.Add(new Health(10));
            var threeOnly = SpawnMovingEntity();
            threeOnly.Add(new Acceleration(3, 3, 3));

            int threeCount = 0;
            foreach (var (position, velocity, acceleration) in _world.Query<Position, Velocity, Acceleration>())
            {
                position.Value.Value += velocity.Value.Value + acceleration.Value.Value;
                threeCount++;
            }

            int fourCount = 0;
            foreach (var (position, velocity, acceleration, health) in _world
                         .Query<Position, Velocity, Acceleration, Health>())
            {
                health.Value.Value += 5;
                fourCount++;
            }

            Assert.That(threeCount, Is.EqualTo(2));
            Assert.That(fourCount, Is.EqualTo(1));
            Assert.That(complete.Get<Position>().Value, Is.EqualTo(new Vector3(3, 3, 3)));
            Assert.That(threeOnly.Get<Position>().Value, Is.EqualTo(new Vector3(4, 4, 4)));
            Assert.That(complete.Get<Health>().Value, Is.EqualTo(15));
        }

        [Test]
        public void Query_ThreeAndFourComponents_ShouldSupportDelegateForEach()
        {
            var complete = SpawnMovingEntity();
            complete.Add(new Acceleration(2, 2, 2));
            complete.Add(new Health(10));
            var threeOnly = SpawnMovingEntity();
            threeOnly.Add(new Acceleration(3, 3, 3));

            var threeCount = 0;
            _world.Query<Position, Velocity, Acceleration>().ForEach((in Entity entity,
                ref Position position, ref Velocity velocity, ref Acceleration acceleration) =>
            {
                position.Value += velocity.Value + acceleration.Value;
                threeCount++;
            });

            var fourCount = 0;
            _world.Query<Position, Velocity, Acceleration, Health>().ForEach((in Entity entity,
                ref Position position, ref Velocity velocity, ref Acceleration acceleration, ref Health health) =>
            {
                health.Value += 5;
                fourCount++;
            });

            Assert.That(threeCount, Is.EqualTo(2));
            Assert.That(fourCount, Is.EqualTo(1));
            Assert.That(complete.Get<Position>().Value, Is.EqualTo(new Vector3(3, 3, 3)));
            Assert.That(threeOnly.Get<Position>().Value, Is.EqualTo(new Vector3(4, 4, 4)));
            Assert.That(complete.Get<Health>().Value, Is.EqualTo(15));
        }

        [Test]
        public void Query_Filters_ShouldBeConsistentAcrossAllArities()
        {
            Entity SpawnAll(bool required, bool excluded, bool any)
            {
                var entity = _world.Spawn();
                entity.Add(new Position());
                entity.Add(new Velocity());
                entity.Add(new Acceleration());
                entity.Add(new Health());
                entity.Add(new Player());
                entity.Add(new Disabled());
                entity.Add(new Grounded());
                entity.Add(new Flying());
                if (required) entity.Add(new FilterRequired());
                if (excluded) entity.Add(new FilterExcluded());
                if (any) entity.Add(new FilterAnyA());
                return entity;
            }

            var match = SpawnAll(true, false, true);
            SpawnAll(false, false, true);
            SpawnAll(true, true, true);
            SpawnAll(true, false, false);

            var counts = new int[8];
            foreach (var _ in _world.Query<Position>().With<FilterRequired>().Without<FilterExcluded>().Any<FilterAnyA, FilterAnyB>()) counts[0]++;
            foreach (var _ in _world.Query<Position, Velocity>().With<FilterRequired>().Without<FilterExcluded>().Any<FilterAnyA, FilterAnyB>()) counts[1]++;
            foreach (var _ in _world.Query<Position, Velocity, Acceleration>().With<FilterRequired>().Without<FilterExcluded>().Any<FilterAnyA, FilterAnyB>()) counts[2]++;
            foreach (var _ in _world.Query<Position, Velocity, Acceleration, Health>().With<FilterRequired>().Without<FilterExcluded>().Any<FilterAnyA, FilterAnyB>()) counts[3]++;
            foreach (var _ in _world.Query<Position, Velocity, Acceleration, Health, Player>().With<FilterRequired>().Without<FilterExcluded>().Any<FilterAnyA, FilterAnyB>()) counts[4]++;
            foreach (var _ in _world.Query<Position, Velocity, Acceleration, Health, Player, Disabled>().With<FilterRequired>().Without<FilterExcluded>().Any<FilterAnyA, FilterAnyB>()) counts[5]++;
            foreach (var _ in _world.Query<Position, Velocity, Acceleration, Health, Player, Disabled, Grounded>().With<FilterRequired>().Without<FilterExcluded>().Any<FilterAnyA, FilterAnyB>()) counts[6]++;
            foreach (var _ in _world.Query<Position, Velocity, Acceleration, Health, Player, Disabled, Grounded, Flying>().With<FilterRequired>().Without<FilterExcluded>().Any<FilterAnyA, FilterAnyB>()) counts[7]++;

            Assert.That(counts, Is.All.EqualTo(1));
            Assert.That(_world.Query<Position>().With<FilterRequired>().Without<FilterExcluded>()
                .Any<FilterAnyA, FilterAnyB>().Matches(match), Is.True);
            Assert.That(_world.Query<Position, Velocity, Acceleration, Health, Player, Disabled, Grounded, Flying>()
                .With<FilterRequired>().Without<FilterExcluded>().Any<FilterAnyA, FilterAnyB>().Matches(match), Is.True);
        }

        [Test]
        public void GeneratedQuery_FiveAndEightComponents_ShouldMatchAndMutateCorrectEntity()
        {
            var complete = SpawnMovingEntity();
            complete.Add(new Acceleration(2, 2, 2));
            complete.Add(new Health(10));
            complete.Add(new Player());
            complete.Add(new Disabled());
            complete.Add(new Grounded());
            complete.Add(new Flying());

            var incomplete = SpawnMovingEntity();
            incomplete.Add(new Acceleration());
            incomplete.Add(new Health());
            incomplete.Add(new Player());
            incomplete.Add(new Disabled());
            incomplete.Add(new Grounded());

            int fiveCount = 0;
            _world.Query<Position, Velocity, Acceleration, Health, Player>().ForEach((in Entity entity,
                ref Position position, ref Velocity velocity,
                ref Acceleration acceleration, ref Health health, ref Player player) =>
            {
                health.Value++;
                fiveCount++;
            });

            int eightCount = 0;
            foreach (var (position, velocity, acceleration, health, player, disabled, grounded, flying)
                     in _world.Query<Position, Velocity, Acceleration, Health, Player, Disabled, Grounded, Flying>())
            {
                position.Value.Value += velocity.Value.Value + acceleration.Value.Value;
                eightCount++;
            }

            Assert.That(fiveCount, Is.EqualTo(2));
            Assert.That(eightCount, Is.EqualTo(1));
            Assert.That(complete.Get<Health>().Value, Is.EqualTo(11));
            Assert.That(complete.Get<Position>().Value, Is.EqualTo(new Vector3(3, 3, 3)));
        }

        [Test]
        public void QueryWarmup_ShouldSupportMinimumAndMaximumAritiesWithoutSteadyStateAllocation()
        {
            using var world = new World();
            world.ReserveArchetype(1, static archetype => archetype
                .Add<Position>().Add<Velocity>().Add<Acceleration>().Add<Health>()
                .Add<Player>().Add<Disabled>().Add<Grounded>().Add<Flying>());

            var one = world.Query<Position>().Warmup();
            var eight = world.Query<Position, Velocity, Acceleration, Health,
                Player, Disabled, Grounded, Flying>().Warmup();
            var parallel = world.Query<Position>().Warmup().AsParallelQuery();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                one.Warmup();
                eight.Warmup();
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            Assert.That(parallel.MinimumEntityCount, Is.GreaterThan(0));
        }

        [Test]
        public void GeneratedQueryPlan_ShouldRebuildAfterProjectedComponentChanges()
        {
            var first = SpawnMovingEntity();
            first.Add(new Acceleration()); first.Add(new Health()); first.Add(new Player());
            var second = SpawnMovingEntity();
            second.Add(new Acceleration()); second.Add(new Health()); second.Add(new Player());
            var query = _world.Query<Position, Velocity, Acceleration, Health, Player>();

            Assert.That(Count(), Is.EqualTo(2));
            first.Remove<Player>();
            Assert.That(Count(), Is.EqualTo(1));
            first.Add(new Player());
            Assert.That(Count(), Is.EqualTo(2));
            _world.Despawn(second);
            Assert.That(Count(), Is.EqualTo(1));

            int Count()
            {
                var count = 0;
                foreach (var _ in query) count++;
                return count;
            }
        }

        [Test]
        public void GeneratedQuery_ShouldExposeAlignedChunkAndAllowSpanMutation()
        {
            var first = SpawnMovingEntity();
            first.Add(new Acceleration()); first.Add(new Health()); first.Add(new Player());
            var second = SpawnMovingEntity();
            second.Add(new Acceleration()); second.Add(new Health()); second.Add(new Player());
            var query = _world.Query<Position, Velocity, Acceleration, Health, Player>();

            Assert.That(query.TryGetAlignedChunk(out var chunk), Is.True);
            Assert.That(chunk.Length, Is.EqualTo(2));
            for (var n = 0; n < chunk.Length; n++)
                chunk.Component4[n].Value = 20 + n;

            Assert.That(first.Get<Health>().Value, Is.EqualTo(20));
            Assert.That(second.Get<Health>().Value, Is.EqualTo(21));
        }

        [Test]
        public void GeneratedQuery_MissingStorages_ShouldBehaveAsEmptyAcrossExecutionPaths()
        {
            var query = _world.Query<Position, Velocity>();
            var delegateCount = 0;

            Assert.DoesNotThrow(() => query.ForEach(
                (in Entity _, ref Position _, ref Velocity _) => delegateCount++));

            var action = new Move2Action();
            Assert.DoesNotThrow(() => query.ForEach(ref action));

            var enumeratedCount = 0;
            foreach (var _ in query) enumeratedCount++;

            Assert.That(query.TryGetAlignedChunk(out var chunk), Is.True);

            var componentAction = new HealthComponentAction();
            Assert.DoesNotThrow(() => _world.Query<Position, Velocity, Acceleration, Health, Player>()
                .ForEach(ref componentAction));

            Assert.That(delegateCount, Is.Zero);
            Assert.That(action.Count, Is.Zero);
            Assert.That(enumeratedCount, Is.Zero);
            Assert.That(chunk.Length, Is.Zero);
        }

        [Test]
        public void GeneratedQuery_ArchetypeColumnsStayAlignedButFilteredQueryIsRejected()
        {
            var first = SpawnMovingEntity();
            first.Add(new Acceleration()); first.Add(new Health()); first.Add(new Player());
            var second = SpawnMovingEntity();
            second.Add(new Acceleration()); second.Add(new Health()); second.Add(new Player());
            first.Remove<Player>();
            first.Add(new Player());

            Assert.That(_world.Query<Position, Velocity, Acceleration, Health, Player>()
                .TryGetAlignedChunk(out _), Is.True);
            Assert.That(_world.Query<Position, Velocity, Acceleration, Health, Player>()
                .With<Position>().TryGetAlignedChunk(out _), Is.False);
        }

        [Test]
        public void GeneratedQuery_AlignedChunkAndQueryAction_ShouldMutateComponents()
        {
            var entity = SpawnMovingEntity();
            entity.Add(new Acceleration()); entity.Add(new Health()); entity.Add(new Player());

            Assert.That(_world.Query<Position, Velocity, Acceleration>().TryGetAlignedChunk(out var chunk), Is.True);
            for (var n = 0; n < chunk.Length; n++)
                chunk.Component1[n].Value += chunk.Component2[n].Value + chunk.Component3[n].Value;

            var action = new HealthComponentAction();
            _world.Query<Position, Velocity, Acceleration, Health, Player>().ForEach(ref action);

            Assert.That(entity.Get<Position>().Value, Is.EqualTo(new Vector3(1, 1, 1)));
            Assert.That(entity.Get<Health>().Value, Is.EqualTo(1));
        }

        [Test]
        public void GeneratedQuery_AlignedChunk_ShouldRejectMultipleChunks()
        {
            _world.CreateTemplate().Add(new Position()).Add(new Velocity()).SpawnBatch(300);

            // A Span cannot cross fixed-size page boundaries.
            Assert.That(_world.Query<Position, Velocity>().TryGetAlignedChunk(out _), Is.False);
        }

        private struct HealthComponentAction : IQueryAction<Position, Velocity, Acceleration, Health, Player>
        {
            public void Execute(in Entity entity, ref Position position, ref Velocity velocity, ref Acceleration acceleration,
                ref Health health, ref Player player) => health.Value++;
        }

        private Entity SpawnMovingEntity()
        {
            var entity = _world.Spawn();
            entity.Add(new Position());
            entity.Add(new Velocity(1, 1, 1));
            return entity;
        }

        #endregion

        #region --- 6. EntityCollector Tests ---

        [Test]
        public void EntityCollector_ShouldCollectMultipleTriggersAndDeduplicateEntities()
        {
            using var collector = _world
                .Observe<Position>(ComponentEvent.KeyAdded | ComponentEvent.KeyChanged)
                .Or<Velocity>(ComponentEvent.KeyRemoved);
            var entity = _world.Spawn();

            entity.Add(new Position(1, 2, 3));
            entity.Add(new Position(4, 5, 6));
            entity.Add(new Velocity(1, 1, 1));
            entity.Remove<Velocity>();

            Assert.That(collector.Count, Is.EqualTo(1));
            Assert.That(collector[0], Is.EqualTo(entity));

            collector.Clear();
            entity.Add(new Position(7, 8, 9));
            Assert.That(collector.Count, Is.EqualTo(1));
        }

        [Test]
        public void EntityCollector_ShouldCollectRemovedComponentsDuringDespawn()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(1, 2, 3));
            using var collector = _world.Observe<Position>(ComponentEvent.KeyRemoved);

            _world.Despawn(entity);

            Assert.That(collector.Count, Is.EqualTo(1));
            Assert.That(collector[0], Is.EqualTo(entity));
            Assert.That(collector[0].IsAlive, Is.False);
        }

        [Test]
        public void EntityCollector_EnsureCapacity_ShouldBeChainableAndValidateState()
        {
            var collector = _world.Observe<Position>(ComponentEvent.KeyRemoved);

            Assert.That(collector.EnsureCapacity(16), Is.SameAs(collector));
            Assert.Throws<ArgumentOutOfRangeException>(() => collector.EnsureCapacity(-1));

            var entity = _world.Spawn();
            entity.Add(new Position());
            entity.Despawn();
            Assert.That(collector.Count, Is.EqualTo(1));

            collector.Dispose();
            Assert.Throws<ObjectDisposedException>(() => collector.EnsureCapacity(16));
        }

        [Test]
        public void EntityCollector_ShouldRejectInvalidEventsAndBecomeInvalidWithWorld()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _world.Observe<Position>(0));
            var collector = _world.Observe<Position>(ComponentEvent.KeyAdded);

            _world.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = collector.Count);
            Assert.DoesNotThrow(() => collector.Dispose());
        }

        [Test]
        public void Query_Matches_ShouldApplyRequiredAndFilteredMasks()
        {
            using var anotherWorld = new World();
            var matching = _world.Spawn();
            matching.Add(new Position());
            matching.Add(new Velocity());
            matching.Add(new Player());
            var excluded = _world.Spawn();
            excluded.Add(new Position());
            excluded.Add(new Velocity());
            excluded.Add(new Player());
            excluded.Add(new Health());
            var foreign = anotherWorld.Spawn();
            foreign.Add(new Position());
            foreign.Add(new Velocity());
            foreign.Add(new Player());
            var filter = _world.Query<Position, Velocity>().With<Player>().Without<Health>();

            Assert.That(filter.Matches(matching), Is.True);
            Assert.That(filter.Matches(excluded), Is.False);
            Assert.That(filter.Matches(foreign), Is.False);
        }

        #endregion

        #region --- 7. EntityCommandBuffer & Template Tests ---

        [Test]
        public void World_CommandBuffer_ShouldReturnSingleInstance()
        {
            var first = _world.CommandBuffer;
            var second = _world.CommandBuffer;
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void EntityCommandBuffer_ShouldRejectEntitiesFromAnotherWorldWhenRecording()
        {
            using var anotherWorld = new World();
            var localEntity = _world.Spawn();
            var foreignEntity = anotherWorld.Spawn();
            var ecb = _world.CommandBuffer;

            Assert.Throws<InvalidOperationException>(() => ecb.Despawn(foreignEntity));
            Assert.Throws<InvalidOperationException>(() =>
                ecb.AddComponent(foreignEntity, new Position()));
            Assert.Throws<InvalidOperationException>(() =>
                ecb.AddComponentBatch(new[] { localEntity, foreignEntity }, new Position()));
            Assert.Throws<InvalidOperationException>(() => ecb.RemoveComponent<Position>(foreignEntity));
            Assert.Throws<InvalidOperationException>(() =>
                ecb.AddRelation<FriendsWith>(localEntity, foreignEntity));
            Assert.Throws<InvalidOperationException>(() =>
                ecb.AddRelation<FriendsWith>(foreignEntity, localEntity));
        }

        [Test]
        public void EntityCommandBuffer_Playback_ShouldApplyCommands()
        {
            var ecb = _world.CommandBuffer;
            var deferred = ecb.Spawn();
            ecb.AddComponent(deferred, new Position(5, 5, 5));

            ecb.Playback();

            var createdEntity = _world.Query().With<Position>()[0];
            Assert.That(createdEntity.Has<Position>(), Is.True);
            Assert.That(createdEntity.Get<Position>().Value, Is.EqualTo(new Vector3(5, 5, 5)));

            ecb.Despawn(createdEntity);
            ecb.Playback();

            Assert.That(createdEntity.IsAlive, Is.False);
        }

        [Test]
        public void EntityCommandBuffer_MultiComponentSpawnAndAdd_ShouldApplyAllComponents()
        {
            var ecb = _world.CommandBuffer;
            ecb.Spawn(new Position(1, 0, 0), new Velocity(2, 0, 0));
            ecb.Spawn(new Position(3, 0, 0), new Velocity(), new Acceleration());
            ecb.Spawn(new Position(4, 0, 0), new Velocity(), new Acceleration(), new Health(40));
            var deferred = ecb.Spawn();
            ecb.AddComponent(deferred, new Position(5, 0, 0), new Velocity());
            ecb.AddComponent(deferred, new Acceleration(), new Health(50), new Grounded());
            var existing = _world.Spawn();
            ecb.AddComponent(existing, new Position(6, 0, 0), new Velocity(), new Acceleration(), new Health(60));

            ecb.Playback();

            Assert.That(_world.Query().With<Position>().Count, Is.EqualTo(5));
            Assert.That(_world.Query().With<Position>().With<Velocity>().Count, Is.EqualTo(5));
            Assert.That(_world.Query().With<Position>().With<Velocity>()[0].Has<Position>(), Is.True);
            Assert.That(_world.Query().With<Position>().With<Velocity>().Result().Count, Is.EqualTo(5));
            Assert.That(_world.Query().With<Position>().With<Acceleration>().Result().Count, Is.EqualTo(4));
            Assert.That(_world.Query().With<Position>().With<Health>().Result().Count, Is.EqualTo(3));
            Assert.That(_world.Query().With<Grounded>().Count, Is.EqualTo(1));
        }

        [Test]
        public void EntityCommandBuffer_DeferredEntity_ShouldRejectForeignAndExpiredHandles()
        {
            using var anotherWorld = new World();
            var first = _world.CommandBuffer;
            var second = anotherWorld.CommandBuffer;
            var deferred = first.Spawn();

            Assert.Throws<InvalidOperationException>(() =>
                second.AddComponent(deferred, new Position()));

            first.Playback();

            Assert.Throws<InvalidOperationException>(() =>
                first.AddComponent(deferred, new Position()));
        }

        [Test]
        public void EntityCommandBuffer_ShouldRejectUseFromAnotherThread()
        {
            var ecb = _world.CommandBuffer;

            var exception = Assert.Throws<InvalidOperationException>(() =>
                System.Threading.Tasks.Task.Run(() => ecb.Spawn()).GetAwaiter().GetResult());

            Assert.That(exception!.Message, Does.Contain("thread that created it"));
        }

        [Test]
        public void EntityCommandBuffer_ShouldPreserveCommandOrderWithoutBoxingCommands()
        {
            var entity = _world.Spawn();
            var ecb = _world.CommandBuffer;

            ecb.AddComponent(entity, new Position(1, 2, 3));
            ecb.RemoveComponent<Position>(entity);
            ecb.Playback();
            Assert.That(entity.Has<Position>(), Is.False);

            ecb.RemoveComponent<Position>(entity);
            ecb.AddComponent(entity, new Position(4, 5, 6));
            ecb.Playback();
            Assert.That(entity.Get<Position>().Value, Is.EqualTo(new Vector3(4, 5, 6)));
        }

        [Test]
        public void RemoveComponents_DuplicateTypes_ShouldNotDetachUnrelatedComponents()
        {
            var ecb = _world.CommandBuffer;
            ecb.Spawn(new Position(1, 2, 3), new Health(42));
            ecb.Playback();
            var entity = _world.Query().With<Position>().With<Health>()[0];

            Assert.That(entity.Remove<Position, Position>(), Is.True);

            Assert.That(entity.IsAlive, Is.True);
            Assert.That(entity.Has<Position>(), Is.False);
            Assert.That(entity.Get<Health>().Value, Is.EqualTo(42));
        }

#if !RELEASE
        [Test]
        public void EntityCommandBuffer_MultiRemove_ShouldCreateOnlyFinalArchetype()
        {
            var ecb = _world.CommandBuffer;
            ecb.Spawn(new Position(1, 2, 3), new Velocity(4, 5, 6), new Health(42));
            ecb.Playback();
            var entity = _world.Query().With<Position>().With<Velocity>().With<Health>()[0];
            var before = _world.CreateDiagnosticsSnapshot().ArchetypeCount;

            ecb.RemoveComponent<Velocity>(entity);
            ecb.RemoveComponent<Health>(entity);
            ecb.Playback();

            Assert.That(_world.CreateDiagnosticsSnapshot().ArchetypeCount, Is.EqualTo(before + 1));
            Assert.That(entity.Get<Position>().Value, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(entity.Has<Velocity>(), Is.False);
            Assert.That(entity.Has<Health>(), Is.False);
        }
#endif

        [Test]
        public void EntityCommandBuffer_MultiRemove_ShouldReuseOrderIndependentTransitionWithoutAllocation()
        {
            const int count = 128;
            var entities = new Entity[count];
            var ecb = _world.CommandBuffer;
            for (var i = 0; i < count; i++)
                ecb.Spawn(new Position(i, 0, 0), new Velocity(), new Health(i));
            ecb.Playback();
            var query = _world.Query<Position, Velocity, Health>();
            var entityIndex = 0;
            query.ForEach((in Entity entity, ref Position _, ref Velocity _, ref Health _) =>
                entities[entityIndex++] = entity);

            ecb.RemoveComponent<Velocity>(entities[0]);
            ecb.RemoveComponent<Health>(entities[0]);
            ecb.Playback();

            for (var i = 1; i < count; i++)
            {
                ecb.RemoveComponent<Health>(entities[i]);
                ecb.RemoveComponent<Velocity>(entities[i]);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            ecb.Playback();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            for (var i = 0; i < count; i++)
            {
                Assert.That(entities[i].Has<Velocity>(), Is.False);
                Assert.That(entities[i].Has<Health>(), Is.False);
                Assert.That(entities[i].Get<Position>().Value.X, Is.EqualTo(i));
            }
        }

        [Test]
        public void CommandBufferReservePayload_ShouldAvoidTypedPayloadAllocations()
        {
            const int count = 256;
            var entities = new Entity[count];
            _world.SpawnBatch(count, entities);
            _world.ReserveArchetype(count, static archetype => archetype.Add<Position>());
            var ecb = _world.CommandBuffer;
            ecb.Reserve(count);
            ecb.ReservePayload<Position>(count);

            // Warm unrelated command bookkeeping; Playback retains its capacity.
            for (var i = 0; i < count; i++) ecb.AddComponent(entities[i], new Position());
            ecb.Playback();
            for (var i = 0; i < count; i++) entities[i].Remove<Position>();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < count; i++) ecb.AddComponent(entities[i], new Position(i, 0, 0));
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            ecb.Playback();
        }

        [Test]
        public void CommandBufferReserve_ShouldAvoidDeferredEntityRecordingAllocations()
        {
            const int count = 256;
            using var world = new World(count);
            world.ReserveArchetype(count, static archetype => archetype.Add<Position>());
            var ecb = world.CommandBuffer;
            ecb.Reserve(count * 2, deferredEntityCapacity: count);
            ecb.ReservePayload<Position>(count);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < count; i++)
            {
                var deferred = ecb.Spawn();
                ecb.AddComponent(deferred, new Position(i, 0, 0));
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            ecb.Playback();
        }

        [Test]
        public void CommandBufferReserve_WithArchetypeGroup_ShouldAvoidDeferredEntityRecordingAllocations()
        {
            const int count = 256;
            using var world = new World(count);
            world.ReserveArchetypeGroup(count, static group => group
                .Add(static archetype => archetype.Add<Position>())
                .Add(static archetype => archetype.Add<Position>().Add<Velocity>()));
            var ecb = world.CommandBuffer;
            ecb.Reserve(count * 2, deferredEntityCapacity: count);
            ecb.ReservePayload<Position>(count);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < count; i++)
            {
                var deferred = ecb.Spawn();
                ecb.AddComponent(deferred, new Position(i, 0, 0));
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            ecb.Playback();
        }

        [Test]
        public void ReserveArchetypeGroup_ShouldWarmEmptySourceCommandBufferPlaybackTransition()
        {
            using var world = new World(1);
            world.ReserveArchetypeGroup(1, static group => group
                .Add(static archetype => archetype.Add<Position>()));
            var ecb = world.CommandBuffer;
            ecb.Reserve(1);
            ecb.ReservePayload<Position>(1);
            var entity = world.Spawn();
            ecb.AddComponent(entity, new Position());

            var before = GC.GetAllocatedBytesForCurrentThread();
            ecb.Playback();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            Assert.That(entity.Has<Position>(), Is.True);
        }

        [Test]
        public void ReserveArchetypeGroup_ShouldCoverPartialPagesAcrossLayouts()
        {
            const int countPerLayout = 100;
            const int totalCapacity = countPerLayout * 5;
            using var world = new World(totalCapacity);
            world.ReserveArchetypeGroup(totalCapacity, static group => group
                .Add(static archetype => archetype.Add<Position>())
                .Add(static archetype => archetype.Add<Velocity>())
                .Add(static archetype => archetype.Add<Acceleration>())
                .Add(static archetype => archetype.Add<Health>())
                .Add(static archetype => archetype.Add<Player>()));
            var ecb = world.CommandBuffer;

            // Warm the empty-source copy plans; this test measures reserved pages.
            var warmPosition = world.Spawn(); warmPosition.Add(new Position()); warmPosition.Despawn();
            var warmVelocity = world.Spawn(); warmVelocity.Add(new Velocity()); warmVelocity.Despawn();
            var warmAcceleration = world.Spawn(); warmAcceleration.Add(new Acceleration()); warmAcceleration.Despawn();
            var warmHealth = world.Spawn(); warmHealth.Add(new Health()); warmHealth.Despawn();
            var warmPlayer = world.Spawn(); warmPlayer.Add(new Player()); warmPlayer.Despawn();

            for (var i = 0; i < countPerLayout; i++)
            {
                var position = ecb.Spawn();
                ecb.AddComponent(position, new Position());
                var velocity = ecb.Spawn();
                ecb.AddComponent(velocity, new Velocity());
                var acceleration = ecb.Spawn();
                ecb.AddComponent(acceleration, new Acceleration());
                var health = ecb.Spawn();
                ecb.AddComponent(health, new Health());
                var player = ecb.Spawn();
                ecb.AddComponent(player, new Player());
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            ecb.Playback();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ReserveArchetypeGroup_ShouldWarmSingleComponentRemovalCopyPlan()
        {
            const int count = 128;
            using var world = new World(count);
            var entities = new Entity[count];
            world.SpawnBatch(count, entities);
            for (var i = 0; i < count; i++)
                entities[i].Add(new Position(i, 0, 0), new Velocity());

            world.ReserveArchetypeGroup(count, static group => group
                .Add(static archetype => archetype.Add<Position>().Add<Velocity>())
                .Add(static archetype => archetype.Add<Position>()));
            var ecb = world.CommandBuffer;
            for (var i = 0; i < count; i++) ecb.RemoveComponent<Velocity>(entities[i]);

            var before = GC.GetAllocatedBytesForCurrentThread();
            ecb.Playback();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void CommandBufferReserve_ShouldValidateCapacities()
        {
            var ecb = _world.CommandBuffer;
            Assert.Throws<ArgumentOutOfRangeException>(() => ecb.Reserve(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ecb.Reserve(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ecb.Reserve(0, 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ecb.ReservePayload<Position>(-1));
        }

        [Test]
        public void ReserveEntities_ShouldValidateCapacityAndAllowReservedBatchSpawn()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _world.ReserveEntities(-1));
            _world.ReserveEntities(512);
            var entities = new Entity[512];
            _world.SpawnBatch(entities.Length, entities);
            Assert.That(entities[511].IsAlive, Is.True);
        }

        [Test]
        public void CommandBufferReserve_ShouldAvoidDespawnRecordingAllocations()
        {
            const int count = 256;
            var entities = new Entity[count];
            _world.SpawnBatch(count, entities);
            _world.ReserveArchetype(count, static archetype => archetype.Add<Position>());
            var ecb = _world.CommandBuffer;
            ecb.Reserve(count);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < count; i++) ecb.Despawn(entities[i]);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            ecb.Playback();
        }

        [Test]
        public void ReserveArchetype_ShouldWarmCommandBufferPlaybackTransition()
        {
            const int count = 128;
            using var world = new World(count);
            world.ReserveArchetype(count, static archetype => archetype.Add<Position>());
            var ecb = world.CommandBuffer;
            for (var i = 0; i < count; i++)
            {
                var deferred = ecb.Spawn();
                ecb.AddComponent(deferred, new Position(i, 0, 0));
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            ecb.Playback();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ReserveArchetype_ShouldCoverSpawnAddAndSourceDespawnCommands()
        {
            const int count = 128;
            using var world = new World(count * 2);
            world.ReserveArchetype(count, static archetype => archetype.Add<Position>());
            var sources = new Entity[count];
            world.SpawnBatch(count, sources);
            var ecb = world.CommandBuffer;
            for (var i = 0; i < count; i++)
            {
                var deferred = ecb.Spawn();
                ecb.AddComponent(deferred, new Position(i, 0, 0));
                ecb.Despawn(sources[i]);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            ecb.Playback();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ReserveArchetype_ShouldWarmMultiComponentPlaybackRegardlessOfRecordOrder()
        {
            const int count = 128;
            using (var runtimeWarmupWorld = new World())
            {
                var runtimeWarmup = runtimeWarmupWorld.CommandBuffer;
                runtimeWarmup.Spawn(new Position(), new Velocity());
                runtimeWarmup.Playback();
            }

            using var world = new World(count);
            world.ReserveArchetype(count, static archetype =>
                archetype.Add<Velocity>().Add<Position>());
            var ecb = world.CommandBuffer;

            var warmup = ecb.Spawn();
            ecb.AddComponent(warmup, new Position());
            ecb.AddComponent(warmup, new Velocity());
            ecb.Playback();
            world.Query().With<Position>().With<Velocity>()[0].Despawn();

            for (var i = 0; i < count; i++)
            {
                var deferred = ecb.Spawn();
                ecb.AddComponent(deferred, new Position(i, 0, 0));
                ecb.AddComponent(deferred, new Velocity(i, 0, 0));
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            ecb.Playback();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ReserveArchetype_ShouldWarmTransitionFromExistingSubsetArchetype()
        {
            const int count = 128;
            using var world = new World(count);
            var entities = new Entity[count];
            world.SpawnBatch(count, entities);
            for (var i = 0; i < count; i++) entities[i].Add(new Position(i, 0, 0));

            world.ReserveArchetype(count, static archetype =>
                archetype.Add<Position>().Add<Velocity>());
            var ecb = world.CommandBuffer;
            for (var i = 0; i < count; i++)
                ecb.AddComponent(entities[i], new Velocity(i, 0, 0));

            var before = GC.GetAllocatedBytesForCurrentThread();
            ecb.Playback();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ReserveArchetype_ShouldWarmIncomingMultiRemoveFromExistingSource()
        {
            const int count = 128;
            using var world = new World(count);
            var entities = new Entity[count];
            var ecb = world.CommandBuffer;
            for (var i = 0; i < count; i++)
                ecb.Spawn(new Position(i, 0, 0), new Velocity(), new Health(i));
            ecb.Playback();
            var entityIndex = 0;
            world.Query<Position, Velocity, Health>().ForEach(
                (in Entity entity, ref Position _, ref Velocity _, ref Health _) => entities[entityIndex++] = entity);

            world.ReserveArchetype(count, static archetype => archetype.Add<Position>());
            for (var i = 0; i < count; i++)
            {
                ecb.RemoveComponent<Health>(entities[i]);
                ecb.RemoveComponent<Velocity>(entities[i]);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            ecb.Playback();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ReserveArchetype_ShouldWarmIncomingMultiRemoveFromSourceCreatedLater()
        {
            const int count = 128;
            using var world = new World(count);
            world.ReserveArchetype(count, static archetype => archetype.Add<Position>());
            var entities = new Entity[count];
            var ecb = world.CommandBuffer;
            for (var i = 0; i < count; i++)
                ecb.Spawn(new Position(i, 0, 0), new Velocity(), new Health(i));
            ecb.Playback();
            var entityIndex = 0;
            world.Query<Position, Velocity, Health>().ForEach(
                (in Entity entity, ref Position _, ref Velocity _, ref Health _) => entities[entityIndex++] = entity);
            for (var i = 0; i < count; i++)
            {
                ecb.RemoveComponent<Velocity>(entities[i]);
                ecb.RemoveComponent<Health>(entities[i]);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            ecb.Playback();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ArchetypeCreatedLogger_ShouldLogOnlyNewArchetypes()
        {
            var messages = new System.Collections.Generic.List<string>();
            _world.ArchetypeCreatedLogger = messages.Add;

            var first = _world.Spawn();
            first.Add(new Position());
            var second = _world.Spawn();
            second.Add(new Position());
            second.Add(new Velocity());

            Assert.That(messages, Has.Count.EqualTo(2));
            Assert.That(messages[0], Does.Contain(typeof(Position).FullName));
            Assert.That(messages[1], Does.Contain(typeof(Position).FullName));
            Assert.That(messages[1], Does.Contain(typeof(Velocity).FullName));

            _world.ArchetypeCreatedLogger = null;
            first.Add(new Health());
            Assert.That(messages, Has.Count.EqualTo(2));
        }

        [Test]
        public void TransitionCreatedLogger_ShouldDescribeOnlyNewTransitions()
        {
            using var world = new World();
            var first = world.Spawn();
            first.Add(new Position());

            var messages = new System.Collections.Generic.List<string>();
            world.TransitionCreatedLogger = messages.Add;
            first.Add(new Velocity());

            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0], Does.StartWith("LitheEcs transition created:"));
            Assert.That(messages[0], Does.Contain($"Added={{{typeof(Velocity).FullName}}}"));
            Assert.That(messages[0], Does.Contain("Removed={}"));
            Assert.That(messages[0], Does.Contain(typeof(Position).FullName));

            var second = world.Spawn();
            second.Add(new Position());
            second.Add(new Velocity());
            first.Remove<Velocity>();
            Assert.That(messages, Has.Count.EqualTo(1));

            world.TransitionCreatedLogger = null;
            first.Add(new Health());
            Assert.That(messages, Has.Count.EqualTo(1));
        }

        [Test]
        public void EntityCommandBuffer_AddComponentBatch_ShouldApplyInCommandOrder()
        {
            var first = _world.Spawn();
            var second = _world.Spawn();
            var entities = new[] { first, second };
            var ecb = _world.CommandBuffer;

            ecb.AddComponentBatch(entities, new Position(7, 8, 9));
            ecb.RemoveComponent<Position>(second);
            ecb.Playback();

            Assert.That(first.Get<Position>().Value, Is.EqualTo(new Vector3(7, 8, 9)));
            Assert.That(second.Has<Position>(), Is.False);
        }

        [Test]
        public void EntityCommandBuffer_AddRelation_ShouldApplyOnPlayback()
        {
            var source = _world.Spawn();
            var target = _world.Spawn();
            var ecb = _world.CommandBuffer;

            ecb.AddRelation<FriendsWith>(source, target);

            Assert.That(source.GetRelations<FriendsWith>().Length, Is.EqualTo(0));
            Assert.That(_world.GetEntitiesWithTarget<FriendsWith>(target).Length, Is.EqualTo(0));

            ecb.Playback();

            Assert.That(source.GetRelations<FriendsWith>().ToArray(), Is.EqualTo(new[] { target }));
            Assert.That(_world.GetEntitiesWithTarget<FriendsWith>(target).ToArray(), Is.EqualTo(new[] { source }));
            Assert.That(source.Has<FriendsWith>(), Is.False);
        }

        [Test]
        public void EntityCommandBuffer_AddRelation_ShouldResolveDeferredSourceOnPlayback()
        {
            var target = _world.Spawn();
            var ecb = _world.CommandBuffer;
            var source = ecb.Spawn();

            ecb.AddRelation<FriendsWith>(source, target);
            ecb.Playback();

            var sources = _world.GetEntitiesWithTarget<FriendsWith>(target);
            Assert.That(sources.Length, Is.EqualTo(1));
            Assert.That(sources[0].GetRelation<FriendsWith>(), Is.EqualTo(target));
        }

        [Test]
        public void EntityCommandBuffer_AddRelation_ShouldRespectCommandOrderAndCleanup()
        {
            var source = _world.Spawn();
            var target = _world.Spawn();
            var ecb = _world.CommandBuffer;

            ecb.AddRelation<FriendsWith>(source, target);
            ecb.Despawn(target);
            ecb.Playback();

            Assert.That(source.GetRelations<FriendsWith>().Length, Is.EqualTo(0));
            Assert.That(target.IsAlive, Is.False);
        }

        [Test]
        public void EntityCommandBuffer_RemoveRelation_ShouldApplyOnPlayback()
        {
            var source = _world.Spawn();
            var target = _world.Spawn();
            source.AddRelation<FriendsWith>(target);
            var ecb = _world.CommandBuffer;

            ecb.RemoveRelation<FriendsWith>(source, target);

            Assert.That(source.HasRelation<FriendsWith>(target), Is.True);
            ecb.Playback();
            Assert.That(source.HasRelation<FriendsWith>(target), Is.False);
        }

        [Test]
        public void EntityCommandBuffer_RemoveRelationWithoutTarget_ShouldRemoveAllOnPlayback()
        {
            var source = _world.Spawn();
            var target1 = _world.Spawn();
            var target2 = _world.Spawn();
            source.AddRelation<FriendsWith>(target1);
            source.AddRelation<FriendsWith>(target2);
            var ecb = _world.CommandBuffer;

            ecb.RemoveRelation<FriendsWith>(source);

            Assert.That(source.GetRelations<FriendsWith>().Length, Is.EqualTo(2));
            ecb.Playback();
            Assert.That(source.GetRelations<FriendsWith>().Length, Is.EqualTo(0));
            Assert.That(_world.GetEntitiesWithTarget<FriendsWith>(target1).Length, Is.EqualTo(0));
            Assert.That(_world.GetEntitiesWithTarget<FriendsWith>(target2).Length, Is.EqualTo(0));
        }

        [Test]
        public void EntityCommandBuffer_ShouldClearCommandsAndPayloadsAfterPlaybackFailure()
        {
            var stale = _world.Spawn();
            _world.Despawn(stale);
            var valid = _world.Spawn();
            var ecb = _world.CommandBuffer;

            ecb.AddComponent(stale, new Position(1, 2, 3));
            Assert.Throws<InvalidOperationException>(() => ecb.Playback());

            ecb.AddComponent(valid, new Position(4, 5, 6));
            Assert.DoesNotThrow(() => ecb.Playback());
            Assert.That(valid.Get<Position>().Value, Is.EqualTo(new Vector3(4, 5, 6)));
        }

        [Test]
        public void EntityTemplate_Spawn_ShouldCreateEntityWithDefaultComponents()
        {
            var template = _world.CreateTemplate()
                .Add(new Position(10, 20, 30))
                .Add(new Velocity(1, 1, 1));

            var entity = template.Spawn();

            Assert.That(entity.IsAlive, Is.True);
            Assert.That(entity.Has<Position>(), Is.True);
            Assert.That(entity.Has<Velocity>(), Is.True);
            Assert.That(entity.Get<Position>().Value, Is.EqualTo(new Vector3(10, 20, 30)));
        }

        [Test]
        public void EntityTemplate_SpawnBatch_ShouldCreateMultipleEntitiesWithDefaultComponents()
        {
            var template = _world.CreateTemplate()
                .Add(new Position(100, 200, 300))
                .Add(new Velocity(5, 5, 5));

            Span<Entity> entities = new Entity[5];
            // The count is inferred from the Span length, keeping the call concise.
            template.SpawnBatch(entities);

            Assert.That(entities.Length, Is.EqualTo(5));
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                Assert.That(entity.IsAlive, Is.True);
                Assert.That(entity.Has<Position>(), Is.True);
                Assert.That(entity.Has<Velocity>(), Is.True);
                Assert.That(entity.Get<Position>().Value, Is.EqualTo(new Vector3(100, 200, 300)));
            }
        }

        [Test]
        public void EntityTemplate_SpawnBatch_WithoutResultSpan_ShouldCreateQueryableEntities()
        {
            var template = _world.CreateTemplate().Add(new Position(7, 8, 9));

            template.SpawnBatch(4);

            int count = 0;
            foreach (ref var position in _world.Query<Position>())
            {
                Assert.That(position.Value, Is.EqualTo(new Vector3(7, 8, 9)));
                count++;
            }

            Assert.That(count, Is.EqualTo(4));
        }

        [Test]
        public void EntityTemplate_SpawnBatch_ShouldSupportReusedEntityIndices()
        {
            var oldEntity = _world.Spawn();
            oldEntity.Add(new Position(1, 1, 1));
            _world.Despawn(oldEntity);

            var template = _world.CreateTemplate().Add(new Position(2, 3, 4));
            Span<Entity> entities = new Entity[2];
            template.SpawnBatch(entities);

            Assert.That(entities[0].Index, Is.EqualTo(oldEntity.Index));
            Assert.That(entities[0].Version, Is.Not.EqualTo(oldEntity.Version));
            Assert.That(entities[0].Get<Position>().Value, Is.EqualTo(new Vector3(2, 3, 4)));
            Assert.That(entities[1].Get<Position>().Value, Is.EqualTo(new Vector3(2, 3, 4)));
        }

        [Test]
        public void EntityTemplate_SpawnBatch_ShouldRejectInvalidCountOrSmallResultSpan()
        {
            var template = _world.CreateTemplate().Add(new Position());

            Assert.Throws<ArgumentOutOfRangeException>(() => template.SpawnBatch(-1));
            Assert.Throws<ArgumentException>(() => template.SpawnBatch(2, new Entity[1]));
        }

        [Test]
        public void EntityTemplate_DuplicateComponentType_ShouldUseLastDefaultOnce()
        {
            var template = _world.CreateTemplate()
                .Add(new Position(1, 2, 3))
                .Add(new Position(4, 5, 6));
            Span<Entity> entities = new Entity[3];

            template.SpawnBatch(entities);

            int queryCount = 0;
            foreach (ref var position in _world.Query<Position>())
            {
                Assert.That(position.Value, Is.EqualTo(new Vector3(4, 5, 6)));
                queryCount++;
            }

            Assert.That(queryCount, Is.EqualTo(3));
        }

        [Test]
        public void EntityTemplate_ShouldUpdateFilteredResultAndCollectorAfterFinalization()
        {
            var query = _world.Query().With<Position>().With<Velocity>();
            var result = query.Result();
            using var collector = _world.Observe<Position>(ComponentEvent.KeyAdded);
            var template = _world.CreateTemplate()
                .Add(new Position(1, 2, 3))
                .Add(new Velocity(4, 5, 6));

            Assert.That(result.Count, Is.Zero);
            var entity = template.Spawn();

            Assert.Throws<InvalidOperationException>(() => _ = result.Count);
            var updatedResult = query.Result();
            Assert.That(updatedResult.Count, Is.EqualTo(1));
            Assert.That(updatedResult[0], Is.EqualTo(entity));
            Assert.That(collector.Count, Is.EqualTo(1));
            Assert.That(collector[0], Is.EqualTo(entity));
        }

        [Test]
        public void EntityTemplate_ShouldEnforceSingletonAndWorldLifetime()
        {
            var template = _world.CreateTemplate().Add(new LocalPlayer());
            var entity = template.Spawn();

            Assert.That(_world.Singleton<LocalPlayer>(), Is.EqualTo(entity));
            Assert.Throws<InvalidOperationException>(() => template.Spawn());
            Assert.Throws<InvalidOperationException>(() => template.SpawnBatch(2));

            _world.Dispose();
            Assert.Throws<ObjectDisposedException>(() => template.Spawn());
            Assert.Throws<ObjectDisposedException>(() => _world.CreateTemplate());
        }

        [Test]
        public void World_WithZeroInitialCapacity_ShouldStillSpawn()
        {
            using var world = new World(0);

            var entity = world.Spawn();

            Assert.That(entity.IsAlive, Is.True);
        }

        [Test]
        public void World_WithZeroInitialCapacity_ShouldSpawnTemplateComponents()
        {
            using var world = new World(0);
            var template = world.CreateTemplate().Add(new Position(1, 2, 3));

            var entity = template.Spawn();

            Assert.That(entity.Get<Position>().Value, Is.EqualTo(new Vector3(1, 2, 3)));
        }

        [Test]
        public void ReserveArchetype_ShouldPreallocateMatchingComponentLayout()
        {
            const int capacity = 600;
            using var world = new World(capacity + 1);
            world.ReserveArchetype(capacity, static archetype =>
            {
                archetype.Add<Position>();
                archetype.Add<Velocity>();
            });

            // Warm the spawn and transition paths before measuring reserved storage.
            var first = world.Spawn();
            first.Add(new Position(), new Velocity());
            first.Despawn();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < capacity; i++)
            {
                var entity = world.Spawn();
                entity.Add(new Position(), new Velocity());
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ReserveArchetype_ShouldValidateArgumentsAndLayout()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _world.ReserveArchetype(-1, static archetype => archetype.Add<Position>()));
            Assert.Throws<ArgumentNullException>(() => _world.ReserveArchetype(1, null!));
            Assert.Throws<InvalidOperationException>(() =>
                _world.ReserveArchetype(1, static _ => { }));
        }

        [Test]
        public void ArchetypeReservations_ShouldNotCreateOrReserveCommandBuffer()
        {
            using var world = new World();

            world.ReserveArchetype(128, static archetype => archetype.Add<Position>());
            world.ReserveArchetypeGroup(128, static group => group
                .Add(static archetype => archetype.Add<Position>())
                .Add(static archetype => archetype.Add<Position>().Add<Velocity>()));

            var commandBuffer = typeof(World).GetField("_commandBuffer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(world);
            Assert.That(commandBuffer, Is.Null);
        }

        [Test]
        public void ReserveArchetypeGroup_ShouldRegisterEveryLayout()
        {
            var created = new List<string>();
            _world.ArchetypeCreatedLogger = created.Add;

            _world.ReserveArchetypeGroup(100, static group => group
                .Add(static archetype => archetype.Add<Position>())
                .Add(static archetype => archetype.Add<Position>().Add<Velocity>()));

            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(created[0], Does.Contain(typeof(Position).FullName));
            Assert.That(created[0], Does.Not.Contain(typeof(Velocity).FullName));
            Assert.That(created[1], Does.Contain(typeof(Velocity).FullName));
        }

        [Test]
        public void ReserveArchetypeGroup_Common_ShouldRegisterOnlyExplicitCompletedLayouts()
        {
            using var world = new World();
            var created = new List<string>();
            world.ArchetypeCreatedLogger = created.Add;

            world.ReserveArchetypeGroup(100, static group => group
                .Common(static archetype => archetype.Add<Position>().Add<Velocity>())
                .Add()
                    .Add(static archetype => archetype.Add<Disabled>())
                    .Add(static archetype => archetype.Add<Grounded>().Add<Flying>()));

            Assert.That(created, Has.Count.EqualTo(3));
            Assert.That(created[0], Does.Contain(typeof(Position).FullName));
            Assert.That(created[0], Does.Contain(typeof(Velocity).FullName));
            Assert.That(created[0], Does.Not.Contain(typeof(Disabled).FullName));
            Assert.That(created[1], Does.Contain(typeof(Disabled).FullName));
            Assert.That(created[1], Does.Not.Contain(typeof(Grounded).FullName));
            Assert.That(created[2], Does.Contain(typeof(Grounded).FullName));
            Assert.That(created[2], Does.Contain(typeof(Flying).FullName));
            Assert.That(created[2], Does.Not.Contain(typeof(Disabled).FullName));

            created.Clear();
            var entity = world.Spawn();
            entity.Add(new Position(), new Velocity());
            entity.Add(new Disabled());
            Assert.That(created, Is.Empty);
        }

        [Test]
        public void ReserveArchetypeGroup_Common_ShouldValidateOrderingAndDuplicates()
        {
            Assert.Throws<ArgumentNullException>(() => _world.ReserveArchetypeGroup(1,
                static group => group.Common(null!)));
            Assert.Throws<InvalidOperationException>(() => _world.ReserveArchetypeGroup(1,
                static group => group.Common(static _ => { })));
            Assert.Throws<InvalidOperationException>(() => _world.ReserveArchetypeGroup(1,
                static group => group.Add()));
            Assert.Throws<InvalidOperationException>(() => _world.ReserveArchetypeGroup(1,
                static group => group
                    .Add(static archetype => archetype.Add<Position>())
                    .Common(static archetype => archetype.Add<Velocity>())));
            Assert.Throws<InvalidOperationException>(() => _world.ReserveArchetypeGroup(1,
                static group => group
                    .Common(static archetype => archetype.Add<Position>())
                    .Common(static archetype => archetype.Add<Velocity>())));
            Assert.Throws<InvalidOperationException>(() => _world.ReserveArchetypeGroup(1,
                static group => group
                    .Common(static archetype => archetype.Add<Position>())
                    .Add()
                    .Add(static archetype => archetype.Add<Position>())));
        }

        [Test]
        public void ReserveArchetypeGroup_ShouldValidateArgumentsAndLayouts()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _world.ReserveArchetypeGroup(-1, static group =>
                    group.Add(static archetype => archetype.Add<Position>())));
            Assert.Throws<ArgumentNullException>(() => _world.ReserveArchetypeGroup(1, null!));
            Assert.Throws<InvalidOperationException>(() =>
                _world.ReserveArchetypeGroup(1, static _ => { }));
            Assert.Throws<ArgumentNullException>(() =>
                _world.ReserveArchetypeGroup(1, static group => group.Add(null!)));
            Assert.Throws<InvalidOperationException>(() =>
                _world.ReserveArchetypeGroup(1, static group => group.Add(static _ => { })));
        }

        [Test]
        public void ReserveArchetypeGroup_WithMoreThanFiveComponents_ShouldCreateOnlyCompletedLayout()
        {
            var created = new List<string>();
            _world.ArchetypeCreatedLogger = created.Add;

            _world.ReserveArchetypeGroup(0, static group => group.Add(static archetype => archetype
                .Add<Position>()
                .Add<Velocity>()
                .Add<Acceleration>()
                .Add<Health>()
                .Add<Player>()
                .Add<Disabled>()));

            Assert.That(created, Has.Count.EqualTo(1));
            Assert.That(created[0], Does.Contain(typeof(Position).FullName));
            Assert.That(created[0], Does.Contain(typeof(Disabled).FullName));
        }

        [Test]
        public void DedicatedReservations_ShouldNotConsumePreviouslyReservedSharedEntityPages()
        {
            const int sharedCapacity = 512;
            var entities = new Entity[sharedCapacity];
            var template = _world.CreateTemplate().Add(new Position());

            _world.ReserveArchetypeGroup(sharedCapacity, static group => group
                .Add(static archetype => archetype.Add<Position>()));

            // Materialize and retain the two chunk shells, then return their pages.
            template.SpawnBatch(entities);
            _world.DespawnBatch(entities);

            // These dedicated layouts use the same World-level Entity ID page pool.
            _world.ReserveArchetype(256, static archetype => archetype.Add<Velocity>());
            _world.ReserveArchetype(256, static archetype => archetype.Add<Acceleration>());
            _world.ReserveArchetype(256, static archetype => archetype.Add<Health>());

            var available = (int)typeof(World)
                .GetProperty("AvailableEntityPageCount",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(_world)!;
            Assert.That(available, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void ComponentTypes_Beyond256_ShouldSupportAddHasAndQueryMatch()
        {
            var componentDefinition = typeof(OverflowComponent<>);
            var componentType = typeof(OverflowSeed);
            for (var i = 0; i < 300; i++)
            {
                componentType = componentDefinition.MakeGenericType(componentType);
                _ = typeof(ComponentType<>).MakeGenericType(componentType)
                    .GetField(nameof(ComponentType<int>.Id))!.GetValue(null);
            }

            System.Reflection.MethodInfo? addDefinition = null;
            System.Reflection.MethodInfo? hasDefinition = null;
            System.Reflection.MethodInfo? removeDefinition = null;
            System.Reflection.MethodInfo? queryDefinition = null;
            foreach (var method in typeof(World).GetMethods())
            {
                if (!method.IsGenericMethodDefinition) continue;
                var genericCount = method.GetGenericArguments().Length;
                var parameters = method.GetParameters();
                if (method.Name == nameof(World.AddComponent) && genericCount == 1 && parameters.Length == 2
                    && parameters[0].ParameterType == typeof(Entity)) addDefinition = method;
                else if (method.Name == nameof(World.HasComponent) && genericCount == 1)
                    hasDefinition = method;
                else if (method.Name == nameof(World.RemoveComponent) && genericCount == 1)
                    removeDefinition = method;
                else if (method.Name == nameof(World.Query) && genericCount == 1)
                    queryDefinition = method;
            }

            var entity = _world.Spawn();
            var component = Activator.CreateInstance(componentType)!;
            addDefinition!.MakeGenericMethod(componentType)
                .Invoke(_world, new[] { (object)entity, component });

            Assert.That(hasDefinition!.MakeGenericMethod(componentType)
                .Invoke(_world, new object[] { entity }), Is.True);

            var query = queryDefinition!.MakeGenericMethod(componentType).Invoke(_world, null)!;
            Assert.That(query.GetType().GetMethod(nameof(Query<int>.Matches))!
                .Invoke(query, new object[] { entity }), Is.True);

            Assert.That(removeDefinition!.MakeGenericMethod(componentType)
                .Invoke(_world, new object[] { entity }), Is.True);
            Assert.That(hasDefinition.MakeGenericMethod(componentType)
                .Invoke(_world, new object[] { entity }), Is.False);

            addDefinition.MakeGenericMethod(componentType)
                .Invoke(_world, new[] { (object)entity, component });
            _world.Despawn(entity);
            Assert.That(entity.IsAlive, Is.False);
        }

        [Test]
        public void AsJobQuery_ShouldAcceptUnmanagedProjectionWithManagedLinkFilter()
        {
            var jobQuery = _world.Query<Position, Velocity>()
                .With<Link<TestData>>()
                .AsJobQuery();

            Assert.That(jobQuery, Is.TypeOf<JobQuery<Position, Velocity>>());
        }

        [Test]
        public void JobQuery_AcquireRanges_ShouldExposeMemoryAndHoldStructuralLockUntilDisposed()
        {
            var first = _world.Spawn();
            first.Add(new Position(1, 0, 0), new Velocity(10, 0, 0));
            var second = _world.Spawn();
            second.Add(new Position(2, 0, 0), new Velocity(20, 0, 0));
            second.Add<Player>();

            var lease = _world.Query<Position, Velocity>().AsJobQuery().AcquireRanges();
            Assert.That(lease.RangeCount, Is.EqualTo(2));
            var visited = 0;
            for (var i = 0; i < lease.RangeCount; i++)
            {
                var range = lease.GetRange(i);
                Assert.That(range.Components1.Length, Is.EqualTo(range.Components2.Length));
                var positions = range.Components1.Span;
                var velocities = range.Components2.Span;
                for (var n = 0; n < range.Length; n++)
                {
                    positions[n].Value += velocities[n].Value;
                    visited++;
                }
            }
            Assert.Throws<InvalidOperationException>(() => _world.Spawn());
            lease.Dispose();

            Assert.That(visited, Is.EqualTo(2));
            Assert.That(first.Get<Position>().Value.X, Is.EqualTo(11));
            Assert.That(second.Get<Position>().Value.X, Is.EqualTo(22));
            Assert.That(_world.Spawn().IsAlive, Is.True);
            var disposedRejected = false;
            try { lease.GetRange(0); }
            catch (ObjectDisposedException) { disposedRejected = true; }
            Assert.That(disposedRejected, Is.True);
        }

        [Test]
        public void JobQuery_AcquireRanges_ShouldNotAllocateAfterWarmup()
        {
            _world.Spawn().Add(new Position());
            var query = _world.Query<Position>().AsJobQuery();
            var warmup = query.AcquireRanges();
            _ = warmup.GetRange(0);
            warmup.Dispose();

            var before = GC.GetAllocatedBytesForCurrentThread();
            var lease = query.AcquireRanges();
            _ = lease.GetRange(0);
            lease.Dispose();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void ParallelForRanges_ShouldUpdateEveryEntityAndExposeGeneratedMaximumArity()
        {
            for (var i = 0; i < 128; i++)
            {
                var entity = _world.Spawn();
                entity.Add(new Position(i, 0, 0));
                entity.Add(new Velocity(1, 2, 3));
                entity.Add(new Acceleration());
                entity.Add(new Health());
                entity.Add<Player>();
                entity.Add<Disabled>();
                entity.Add<Grounded>();
                entity.Add<Flying>();
            }

            var entitiesByQueryOffset = new int[128];
            Array.Fill(entitiesByQueryOffset, -1);
            _world.Query<Position, Velocity>().AsParallelQuery(1, 8).Run(
                (positions, velocities, entities) =>
                {
                    if (positions.Length != velocities.Length || positions.Length != entities.Length)
                        throw new InvalidOperationException("Parallel range lengths must match.");
                    if (entities.Offset < 0 || entities.Offset + entities.Length > entitiesByQueryOffset.Length)
                        throw new InvalidOperationException("Parallel range offset is outside the Query output.");
                    for (var i = 0; i < positions.Length; i++)
                    {
                        positions[i].Value += velocities[i].Value;
                        entitiesByQueryOffset[entities.Offset + i] = entities[i].Index;
                    }
                });

            _world.Query<Position, Velocity, Acceleration, Health, Player, Disabled, Grounded, Flying>()
                .AsParallelQuery(1, 8).Run((positions, velocities, accelerations, health, players, disabled, grounded, flying,
                    entities) =>
                {
                    for (var i = 0; i < health.Length; i++) health[i].Value = entities[i].Index + 1;
                });

            var count = 0;
            _world.Query<Position, Velocity, Health>().ForEach(
                (in Entity entity, ref Position position, ref Velocity _, ref Health health) =>
                {
                    Assert.That(position.Value.Y, Is.EqualTo(2));
                    Assert.That(health.Value, Is.EqualTo(entity.Index + 1));
                    count++;
                });
            Assert.That(count, Is.EqualTo(128));
            Assert.That(entitiesByQueryOffset, Has.None.EqualTo(-1));
            Assert.That(entitiesByQueryOffset.Distinct().Count(), Is.EqualTo(128));
        }

        [Test]
        public void ParallelForRanges_Offset_ShouldCoverMultipleArchetypesAndChunksInSequentialFallback()
        {
            _world.CreateTemplate().Add(new Position()).SpawnBatch(300);
            _world.CreateTemplate().Add(new Position()).Add(new Velocity()).SpawnBatch(25);
            var output = new int[325];
            Array.Fill(output, -1);

            _world.Query<Position>().AsParallelQuery(int.MaxValue, 4096).Run((positions, entities) =>
            {
                for (var i = 0; i < positions.Length; i++)
                    output[entities.Offset + i] = entities[i].Index;
            });

            Assert.That(output, Has.None.EqualTo(-1));
            Assert.That(output.Distinct().Count(), Is.EqualTo(325));
        }

        [Test]
        public void WarmParallelQueryWorkers_ShouldBeIdempotentAndReusableByParallelQuery()
        {
            Assert.DoesNotThrow(() => _world.WarmParallelQueryWorkers());
            Assert.DoesNotThrow(() => _world.WarmParallelQueryWorkers());

            var entity = _world.Spawn();
            entity.Add(new Position());
            _world.Query<Position>().AsParallelQuery(1, 4096).Run(static (positions, _) =>
            {
                positions[0].Value.X++;
            });

            Assert.That(entity.Get<Position>().Value.X, Is.EqualTo(1));
        }

        [Test]
        public void ParallelQuery_Reserve_ShouldPrepareMinimumAndMaximumArities()
        {
            var one = _world.Query<Position>().AsParallelQuery(1, 8);
            var eight = _world.Query<Position, Velocity, Acceleration, Health, Player, Disabled, Grounded, Flying>()
                .AsParallelQuery(1, 8);
            Assert.DoesNotThrow(() => one.Reserve(512));
            Assert.DoesNotThrow(() => eight.Reserve(512));
            Assert.Throws<ArgumentOutOfRangeException>(() => one.Reserve(-1));
        }

        [Test]
        public void ParallelQuery_ShouldKeepExecutionSettingsAndSupportMinimumAndMaximumArities()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(), new Velocity(), new Acceleration(), new Health());
            entity.Add<Player>();
            entity.Add<Disabled>();
            entity.Add<Grounded>();
            entity.Add<Flying>();

            var one = _world.Query<Position>().AsParallelQuery(minimumEntityCount: 1, batchSize: 8);
            var eight = _world.Query<Position, Velocity, Acceleration, Health, Player, Disabled, Grounded, Flying>()
                .AsParallelQuery(minimumEntityCount: 1, batchSize: 8);

            Assert.That(one.MinimumEntityCount, Is.EqualTo(1));
            Assert.That(one.BatchSize, Is.EqualTo(8));
            Assert.DoesNotThrow(() => one.Reserve(512));
            Assert.DoesNotThrow(() => eight.Reserve(512));

            one.Run((positions, _) => positions[0].Value.X++);
            eight.Run((positions, _, _, health, _, _, _, _, _) => health[0].Value++);

            Assert.That(entity.Get<Position>().Value.X, Is.EqualTo(1));
            Assert.That(entity.Get<Health>().Value, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _world.Query<Position>().AsParallelQuery(minimumEntityCount: 0, batchSize: 8));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _world.Query<Position>().AsParallelQuery(minimumEntityCount: 1, batchSize: 0));
        }

        [Test]
        public void ParallelQuery_ShouldRejectStructuralChangesAndReleaseWorldAfterFailure()
        {
            var entity = _world.Spawn();
            entity.Add(new Position());

            var exception = Assert.Throws<AggregateException>(() =>
                _world.Query<Position>().AsParallelQuery(1, 8).Run((_, _) => _world.Spawn()));
            Assert.That(exception!.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());

            Assert.DoesNotThrow(() =>
                _world.Query<Position>().AsParallelQuery(1, 8).Run((positions, _) => positions[0].Value.X++));
            Assert.That(entity.Get<Position>().Value.X, Is.EqualTo(1));
        }

        [Test]
        public void MultiComponentAddRemove_ShouldApplyAllComponentsInOneOperation()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(1, 2, 3), new Velocity(4, 5, 6), new Acceleration(7, 8, 9),
                new Health(10), new Grounded());

            Assert.That(entity.Get<Position>().Value, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(entity.Get<Velocity>().Value, Is.EqualTo(new Vector3(4, 5, 6)));
            Assert.That(entity.Get<Acceleration>().Value, Is.EqualTo(new Vector3(7, 8, 9)));
            Assert.That(entity.Get<Health>().Value, Is.EqualTo(10));
            Assert.That(entity.Has<Grounded>(), Is.True);

            Assert.That(entity.Remove<Position, Velocity, Acceleration, Health, Grounded>(), Is.True);
            Assert.That(entity.Has<Position>(), Is.False);
            Assert.That(entity.Has<Velocity>(), Is.False);
            Assert.That(entity.Has<Acceleration>(), Is.False);
            Assert.That(entity.Has<Health>(), Is.False);
            Assert.That(entity.Has<Grounded>(), Is.False);
        }

        [Test]
        public void EntityData_ShouldReadAndWriteComponentsWithoutRepeatedEntityLookup()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(1, 2, 3), new Health(10));

            var data = entity.Data;
            data.Get<Position>().Value.X = 42;
            data.Get<Health>().Value = 99;

            Assert.That(entity.Get<Position>().Value.X, Is.EqualTo(42));
            Assert.That(entity.Get<Health>().Value, Is.EqualTo(99));
            Assert.That(data.Has<Position>(), Is.True);
            Assert.That(data.Has<Velocity>(), Is.False);
        }

        [Test]
        public void CommandBuffer_ShouldMergeAdjacentComponentMutationsWithoutChangingResults()
        {
            var entities = new Entity[4];
            _world.SpawnBatch(entities.Length, entities);
            var commandBuffer = _world.CommandBuffer;
            commandBuffer.AddComponentBatch(entities, new Position(1, 2, 3));
            commandBuffer.AddComponentBatch(entities, new Health(10));
            commandBuffer.Playback();

            foreach (var entity in entities)
            {
                Assert.That(entity.Has<Position>(), Is.True);
                Assert.That(entity.Has<Health>(), Is.True);
                commandBuffer.RemoveComponent<Position>(entity);
                commandBuffer.RemoveComponent<Health>(entity);
            }
            commandBuffer.Playback();

            foreach (var entity in entities)
            {
                Assert.That(entity.Has<Position>(), Is.False);
                Assert.That(entity.Has<Health>(), Is.False);
            }
        }

#if !RELEASE && !DISABLE_LITHEECS_DIAGNOSTICS
        [Test]
        public void CreateDiagnosticsSnapshot_ShouldCaptureCurrentWorldState()
        {
            var first = _world.Spawn();
            first.Add(new Position(1, 2, 3));
            first.Add<LocalPlayer>();
            first.Bind(new TestData());

            var second = _world.Spawn();
            second.Add(new Position(4, 5, 6));
            first.AddRelation<FriendsWith>(second);
            _world.Query().With<Position>().With<LocalPlayer>().Result();
            _world.Despawn(second);

            var snapshot = _world.CreateDiagnosticsSnapshot();

            Assert.That(snapshot.AliveEntityCount, Is.EqualTo(1));
            Assert.That(snapshot.AllocatedEntitySlotCount, Is.EqualTo(2));
            Assert.That(snapshot.RecycledEntitySlotCount, Is.EqualTo(1));
            Assert.That(snapshot.EntityCapacity, Is.GreaterThanOrEqualTo(2));
            Assert.That(snapshot.StructuralVersion, Is.GreaterThan(0));
            Assert.That(snapshot.ComponentStorageCount, Is.EqualTo(2));
            Assert.That(snapshot.CachedQueryPlanCount, Is.EqualTo(1));
            Assert.That(snapshot.RelationStorageCount, Is.EqualTo(1));
            Assert.That(snapshot.BindingCount, Is.EqualTo(1));

            var positionFound = false;
            var singletonFound = false;
            foreach (ref readonly var storage in snapshot.ComponentStorages)
            {
                if (storage.ComponentType == typeof(Position))
                {
                    positionFound = true;
                    Assert.That(storage.TypeId, Is.EqualTo(ComponentType<Position>.Id));
                    Assert.That(storage.Count, Is.EqualTo(1));
                    Assert.That(storage.Capacity, Is.GreaterThanOrEqualTo(storage.Count));
                    Assert.That(storage.IsSingleton, Is.False);
                }
                else if (storage.ComponentType == typeof(LocalPlayer))
                {
                    singletonFound = true;
                    Assert.That(storage.Count, Is.EqualTo(1));
                    Assert.That(storage.IsSingleton, Is.True);
                }
            }

            Assert.That(positionFound, Is.True);
            Assert.That(singletonFound, Is.True);

            var text = snapshot.ToString();
            Assert.That(text, Does.Contain("WorldDiagnosticsSnapshot"));
            Assert.That(text, Does.Contain("AliveEntities: 1"));
            Assert.That(text, Does.Contain(typeof(Position).FullName));
            Assert.That(text, Does.Contain("Count: 1"));
            Assert.That(text, Does.Contain("Singleton: True"));
        }

        [Test]
        public void CreateDiagnosticsSnapshot_ShouldBeIndependentFromLaterWorldChanges()
        {
            var entity = _world.Spawn();
            entity.Add(new Position());
            var before = _world.CreateDiagnosticsSnapshot();

            _world.Spawn().Add(new Position());
            var after = _world.CreateDiagnosticsSnapshot();

            Assert.That(before.AliveEntityCount, Is.EqualTo(1));
            Assert.That(before.ComponentStorages[0].Count, Is.EqualTo(1));
            Assert.That(after.AliveEntityCount, Is.EqualTo(2));
            Assert.That(after.ComponentStorages[0].Count, Is.EqualTo(2));
        }

        [Test]
        public void CreateEntityDiagnosticsSnapshot_ShouldCaptureComponentsForEveryLiveEntity()
        {
            var first = _world.Spawn();
            first.Add(new Position());
            first.Add(new Velocity());

            var despawned = _world.Spawn();
            despawned.Add(new Health(10));
            _world.Despawn(despawned);

            var empty = _world.Spawn();
            var snapshot = _world.CreateEntityDiagnosticsSnapshot();

            Assert.That(snapshot.EntityCount, Is.EqualTo(2));
            Assert.That(snapshot.Entities[0].Entity, Is.EqualTo(first));
            Assert.That(snapshot.Entities[0].ComponentCount, Is.EqualTo(2));
            var firstTypes = snapshot.GetComponentTypeIds(snapshot.Entities[0]);
            Assert.That(firstTypes[0], Is.EqualTo(ComponentType<Position>.Id));
            Assert.That(firstTypes[1], Is.EqualTo(ComponentType<Velocity>.Id));
            Assert.That(snapshot.GetComponentType(firstTypes[0]), Is.EqualTo(typeof(Position)));
            Assert.That(snapshot.GetComponentType(firstTypes[1]), Is.EqualTo(typeof(Velocity)));

            Assert.That(snapshot.Entities[1].Entity, Is.EqualTo(empty));
            Assert.That(snapshot.Entities[1].ComponentCount, Is.EqualTo(0));
            Assert.That(snapshot.GetComponentTypeIds(snapshot.Entities[1]).Length, Is.EqualTo(0));

            var entityText = snapshot.Entities[0].ToString();
            Assert.That(entityText, Does.Contain($"Entity {first.Index}:{first.Version}"));
            Assert.That(entityText, Does.Contain("Components: 2"));

            var listText = snapshot.ToString();
            Assert.That(listText, Does.Contain("EntityListDiagnosticsSnapshot { Entities: 2 }"));
            Assert.That(listText, Does.Contain(snapshot.FormatEntity(snapshot.Entities[0])));
            Assert.That(listText, Does.Contain(snapshot.FormatEntity(snapshot.Entities[1])));
        }

        [Test]
        public void CreateEntityDiagnosticsSnapshot_ShouldRemainIndependentFromComponentChanges()
        {
            var entity = _world.Spawn();
            entity.Add(new Position());
            var before = _world.CreateEntityDiagnosticsSnapshot();

            entity.Add(new Health(20));
            var after = _world.CreateEntityDiagnosticsSnapshot();

            Assert.That(before.Entities[0].ComponentCount, Is.EqualTo(1));
            Assert.That(after.Entities[0].ComponentCount, Is.EqualTo(2));
            Assert.That(before.Entities[0].ComponentMaskHash,
                Is.Not.EqualTo(after.Entities[0].ComponentMaskHash));
        }

        [Test]
        public void CreateEntityDiagnosticsSnapshotForEntity_ShouldCaptureBoxedComponentValues()
        {
            var entity = _world.Spawn();
            entity.Add(new Position(1, 2, 3));
            entity.Add(Link.With("Player"));

            var snapshot = _world.CreateEntityDiagnosticsSnapshot(entity);

            Assert.That(snapshot.Entity, Is.EqualTo(entity));
            Assert.That(snapshot.IsAlive, Is.True);
            Assert.That(snapshot.ComponentCount, Is.EqualTo(2));

            var positionFound = false;
            var linkFound = false;
            var capturedPosition = default(Position);
            foreach (ref readonly var component in snapshot.Components)
            {
                if (component.ComponentType == typeof(Position))
                {
                    positionFound = true;
                    capturedPosition = (Position)component.Value;
                    Assert.That(component.TypeId, Is.EqualTo(ComponentType<Position>.Id));
                    Assert.That(capturedPosition.Value, Is.EqualTo(new Vector3(1, 2, 3)));
                }
                else if (component.ComponentType == typeof(Link<string>))
                {
                    linkFound = true;
                    Assert.That(component.Value.ToString(), Is.EqualTo("Link<String>(Player)"));
                }
            }

            entity.Get<Position>().Value = new Vector3(9, 9, 9);
            Assert.That(capturedPosition.Value, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(positionFound, Is.True);
            Assert.That(linkFound, Is.True);

            _world.Despawn(entity);
            var stale = _world.CreateEntityDiagnosticsSnapshot(entity);
            Assert.That(stale.IsAlive, Is.False);
            Assert.That(stale.ComponentCount, Is.EqualTo(0));
        }
#endif

        #endregion
    }
}
