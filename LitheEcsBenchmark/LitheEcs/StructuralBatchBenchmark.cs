using BenchmarkDotNet.Attributes;

namespace LitheEcsBenchmark
{
    [MemoryDiagnoser]
    [BenchmarkCategory("StructuralBatch")]
    public class StructuralBatchBenchmark
    {
        private struct BatchA { public int Value; }
        private struct BatchB { public int Value; }
        private struct BatchC { public int Value; }
        private struct BatchD { public int Value; }
        private struct BatchE { public int Value; }
        private struct BatchF { public int Value; }

        private LitheEcs.World _sequential = null!;
        private LitheEcs.World _batched = null!;

        [GlobalSetup]
        public void Setup()
        {
            _sequential = new LitheEcs.World(1);
            _batched = new LitheEcs.World(1);
            _batched.ReserveArchetype(1, static archetype => archetype
                .Add<BatchA>().Add<BatchB>().Add<BatchC>()
                .Add<BatchD>().Add<BatchE>().Add<BatchF>());
            _batched.CommandBuffer.Reserve(6);
            _batched.CommandBuffer.ReservePayload<BatchA>(1);
            _batched.CommandBuffer.ReservePayload<BatchB>(1);
            _batched.CommandBuffer.ReservePayload<BatchC>(1);
            _batched.CommandBuffer.ReservePayload<BatchD>(1);
            _batched.CommandBuffer.ReservePayload<BatchE>(1);
            _batched.CommandBuffer.ReservePayload<BatchF>(1);
            RunSequential();
            RunBatched();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _sequential.Dispose();
            _batched.Dispose();
        }

        [Benchmark(Baseline = true)]
        public void SequentialAdd()
        {
            for (var i = 0; i < 100; i++) RunSequential();
        }

        [Benchmark]
        public void StructuralBatch()
        {
            for (var i = 0; i < 100; i++) RunBatched();
        }

        private void RunSequential()
        {
            var entity = _sequential.Spawn();
            entity.Add(new BatchA { Value = 1 });
            entity.Add(new BatchB { Value = 2 });
            entity.Add(new BatchC { Value = 3 });
            entity.Add(new BatchD { Value = 4 });
            entity.Add(new BatchE { Value = 5 });
            entity.Add(new BatchF { Value = 6 });
            entity.Despawn();
        }

        private void RunBatched()
        {
            var entity = _batched.Spawn();
            using (_batched.BeginStructuralBatch())
            {
                entity.Add(new BatchA { Value = 1 });
                entity.Add(new BatchB { Value = 2 });
                entity.Add(new BatchC { Value = 3 });
                entity.Add(new BatchD { Value = 4 });
                entity.Add(new BatchE { Value = 5 });
                entity.Add(new BatchF { Value = 6 });
            }
            entity.Despawn();
        }
    }
}
