using BenchmarkDotNet.Attributes;
using LitheEcs;

namespace LitheEcsBenchmark;

[MemoryDiagnoser]
[BenchmarkCategory("ParallelQueryActiveWorkerTuning")]
public class ParallelQueryActiveWorkerTuningBenchmark
{
    private World _world = null!;
    private ParallelQuery<Position, Velocity> _query;
    private ParallelRangeAction<Position, Velocity> _action = null!;

    [Params(4_096, 8_192, 16_384, 32_768, 65_536, 100_000)]
    public int EntityCount { get; set; }

    [Params(8_192, 16_384, 24_576, 32_768)]
    public int EntitiesPerThread { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World(EntityCount);
        _world.ConfigureParallelQueryScheduling(32_768, EntitiesPerThread);
        _world.CreateTemplate()
            .Add(new Position())
            .Add(new Velocity { X = 1, Y = 1, Z = 1 })
            .SpawnBatch(EntityCount);
        _query = _world.Query<Position, Velocity>().AsParallelQuery(1, 2_048);
        _action = Update;
        _query.Run(_action);
    }

    [Benchmark]
    public void Run() => _query.Run(_action);

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    private static void Update(
        Span<Position> positions, Span<Velocity> velocities, EntityRange entities)
    {
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i].X += velocities[i].X;
            positions[i].Y += velocities[i].Y;
            positions[i].Z += velocities[i].Z;
        }
    }
}
