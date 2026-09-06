using BenchmarkDotNet.Attributes;
using LitheEcs;

namespace LitheEcsBenchmark;

[MemoryDiagnoser]
[BenchmarkCategory("ParallelQueryWorkerCount")]
public class ParallelQueryWorkerCountBenchmark
{
    private World _world = null!;
    private ParallelQuery<Position, Velocity> _query;
    private ParallelRangeAction<Position, Velocity> _action = null!;

    [Params(100_000, 1_000_000)]
    public int EntityCount { get; set; }

    // Includes the calling thread. LitheEcs owns TotalThreads - 1 background workers.
    [Params(1, 2, 4, 6, 12)]
    public int TotalThreads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World(EntityCount);
        ParallelQueryBenchmarkConfiguration.SetWorkerCount(_world, TotalThreads - 1);
        _world.CreateTemplate()
            .Add(new Position())
            .Add(new Velocity { X = 1, Y = 1, Z = 1 })
            .SpawnBatch(EntityCount);
        _query = _world.Query<Position, Velocity>().AsParallelQuery(1, 4096);
        _action = Update;
        _query.Run(_action);
    }

    [Benchmark]
    public void Run() => _query.Run(_action);

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    private static void Update(Span<Position> positions, Span<Velocity> velocities, EntityRange _)
    {
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i].X += velocities[i].X;
            positions[i].Y += velocities[i].Y;
            positions[i].Z += velocities[i].Z;
        }
    }
}
