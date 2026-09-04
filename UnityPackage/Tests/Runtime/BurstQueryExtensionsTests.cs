using System;
using NUnit.Framework;
using LitheEcs;
using LitheEcs.Unity.Jobs;

namespace LitheEcs.Unity.Jobs.Tests
{
    public sealed class BurstQueryExtensionsTests
    {
        private struct C1 { public int Value; }
        private struct C2 { public int Value; }
        private struct C3 { public int Value; }
        private struct Extra { }

        private struct Add1 : IBurstQueryAction<C1>
        {
            public void Execute(int index, ref C1 c1) => c1.Value += 1;
        }

        private struct Add2 : IBurstQueryAction<C1, C2>
        {
            public void Execute(int index, ref C1 c1, ref C2 c2)
            {
                c1.Value += c2.Value;
                c2.Value += 1;
            }
        }

        private struct Add3 : IBurstQueryAction<C1, C2, C3>
        {
            public void Execute(int index, ref C1 c1, ref C2 c2, ref C3 c3)
            {
                c1.Value += c2.Value + c3.Value;
                c2.Value += 1;
                c3.Value += 2;
            }
        }

        [Test]
        public void AcquireRanges_ShouldExposeAlignedLengthsForMinimumAndMaximumArities()
        {
            using var world = new World();
            var entity = world.Spawn();
            entity.Add(new C1(), new C2(), new C3());

            using (var ranges = world.Query<C1>().AsJobQuery().AcquireRanges())
            {
                Assert.That(ranges.RangeCount, Is.EqualTo(1));
                Assert.That(ranges.GetRange(0).Length, Is.EqualTo(1));
            }

            using (var ranges = world.Query<C1, C2, C3>().AsJobQuery().AcquireRanges())
            {
                Assert.That(ranges.RangeCount, Is.EqualTo(1));
                var range = ranges.GetRange(0);
                Assert.That(range.Components2.Length, Is.EqualTo(range.Length));
                Assert.That(range.Components3.Length, Is.EqualTo(range.Length));
            }
        }

        [Test]
        public void ReserveBurstUnsafe_ShouldPrepareAllSupportedAritiesWithoutExecutingActions()
        {
            using var world = new World();
            world.ReserveArchetypeGroup(0, static group => group
                .Add(static a => a.Add<C1>().Add<C2>().Add<C3>()));

            var query1 = world.Query<C1>().AsJobQuery();
            var query2 = world.Query<C1, C2>().AsJobQuery();
            var query3 = world.Query<C1, C2, C3>().AsJobQuery();

            Assert.DoesNotThrow(() => query1.ReserveBurstUnsafe(512, 1));
            Assert.DoesNotThrow(() => query2.ReserveBurstUnsafe(512, 1));
            Assert.DoesNotThrow(() => query3.ReserveBurstUnsafe(512, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => query1.ReserveBurstUnsafe(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => query1.ReserveBurstUnsafe(512, 0));

            var entity = world.Spawn();
            entity.Add(new C1 { Value = 1 }, new C2 { Value = 2 }, new C3 { Value = 3 });
            Assert.That(entity.Get<C1>().Value, Is.EqualTo(1));
            Assert.That(entity.Get<C2>().Value, Is.EqualTo(2));
            Assert.That(entity.Get<C3>().Value, Is.EqualTo(3));
        }

        [Test]
        public void BurstQuery_ShouldKeepBatchSizeAndRunWithoutRepeatingIt()
        {
            using var world = new World();
            var entity = world.Spawn();
            entity.Add(new C1 { Value = 1 }, new C2 { Value = 2 }, new C3 { Value = 3 });
            var query = world.Query<C1, C2, C3>().AsBurstQuery(batchSize: 1);
            var action = new Add3();

            Assert.That(query.BatchSize, Is.EqualTo(1));
            Assert.DoesNotThrow(() => query.Reserve(512));
            query.RunUnsafe(ref action);

            Assert.That(entity.Get<C1>().Value, Is.EqualTo(6));
            Assert.That(entity.Get<C2>().Value, Is.EqualTo(3));
            Assert.That(entity.Get<C3>().Value, Is.EqualTo(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.Query<C1>().AsBurstQuery(batchSize: 0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RunBurst_ShouldHandleZeroAndOneComponent(bool unsafeMode)
        {
            using var world = new World();
            var action = new Add1();
            var query = world.Query<C1>().AsJobQuery();
            if (unsafeMode) query.RunBurstUnsafe(ref action);
            else query.RunBurst(ref action);

            var entity = world.Spawn();
            entity.Add(new C1 { Value = 4 });
            if (unsafeMode) query.RunBurstUnsafe(ref action);
            else query.RunBurst(ref action);

            Assert.That(entity.Get<C1>().Value, Is.EqualTo(5));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RunBurst_ShouldUpdateTwoComponentsAcrossMultipleArchetypes(bool unsafeMode)
        {
            using var world = new World();
            var first = world.Spawn();
            first.Add(new C1 { Value = 1 }, new C2 { Value = 10 });
            var second = world.Spawn();
            second.Add(new C1 { Value = 2 }, new C2 { Value = 20 });
            second.Add<Extra>();
            var action = new Add2();
            var query = world.Query<C1, C2>().AsJobQuery();

            if (unsafeMode) query.RunBurstUnsafe(ref action, 1);
            else query.RunBurst(ref action, 1);

            Assert.That(first.Get<C1>().Value, Is.EqualTo(11));
            Assert.That(first.Get<C2>().Value, Is.EqualTo(11));
            Assert.That(second.Get<C1>().Value, Is.EqualTo(22));
            Assert.That(second.Get<C2>().Value, Is.EqualTo(21));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RunBurst_ShouldUpdateThreeComponentsAndKeepFilter(bool unsafeMode)
        {
            using var world = new World();
            var included = world.Spawn();
            included.Add(new C1 { Value = 1 }, new C2 { Value = 2 }, new C3 { Value = 3 });
            var excluded = world.Spawn();
            excluded.Add(new C1 { Value = 10 }, new C2 { Value = 20 }, new C3 { Value = 30 });
            excluded.Add<Extra>();
            var action = new Add3();
            var query = world.Query<C1, C2, C3>().Without<Extra>().AsJobQuery();

            if (unsafeMode) query.RunBurstUnsafe(ref action, 1);
            else query.RunBurst(ref action, 1);

            Assert.That(included.Get<C1>().Value, Is.EqualTo(6));
            Assert.That(included.Get<C2>().Value, Is.EqualTo(3));
            Assert.That(included.Get<C3>().Value, Is.EqualTo(5));
            Assert.That(excluded.Get<C1>().Value, Is.EqualTo(10));
        }
    }
}
