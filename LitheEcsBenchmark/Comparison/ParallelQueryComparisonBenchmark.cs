using BenchmarkDotNet.Attributes;
using Friflo.Engine.ECS;
using LitheEcs;

namespace LitheEcsBenchmark;

[MemoryDiagnoser]
[BenchmarkCategory("ParallelQuery")]
public class ParallelQueryComparisonBenchmark
{
    private World _litheWorld = null!;
    private ParallelQuery<Position, Velocity> _litheQuery;
    private ParallelRangeAction<Position, Velocity> _litheRangeAction = null!;

    private EntityStore _frifloWorld = null!;
    private ParallelJobRunner _frifloRunner = null!;
    private QueryJob<Position, Velocity> _frifloJob = null!;

    [Params(1_000, 100_000, 1_000_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _litheWorld = new World(EntityCount);
        var template = _litheWorld.CreateTemplate()
            .Add(new Position())
            .Add(new Velocity { X = 1, Y = 1, Z = 1 });
        var litheEntities = new LitheEcs.Entity[EntityCount];
        template.SpawnBatch(EntityCount, litheEntities);
        _litheQuery = _litheWorld.Query<Position, Velocity>().AsParallelQuery();
        _litheRangeAction = UpdateLitheRange;

        _frifloRunner = new ParallelJobRunner(Environment.ProcessorCount);
        _frifloWorld = new EntityStore { JobRunner = _frifloRunner };
        for (var i = 0; i < EntityCount; i++)
            _frifloWorld.CreateEntity(new Position(), new Velocity { X = 1, Y = 1, Z = 1 });

        _frifloJob = _frifloWorld.Query<Position, Velocity>().ForEach(UpdateFriflo);

        // Start worker threads before BenchmarkDotNet begins collecting measurements.
        _litheQuery.Run(_litheRangeAction);
        _frifloJob.RunParallel();
    }

    [Benchmark]
    public void LitheEcs_ParallelForRanges() => _litheQuery.Run(_litheRangeAction);

    [Benchmark(Baseline = true)]
    public void Friflo_RunParallel() => _frifloJob.RunParallel();

    [GlobalCleanup]
    public void Cleanup()
    {
        _frifloRunner.Dispose();
        _litheWorld.Dispose();
    }

    private static void UpdateFriflo(
        Chunk<Position> positions,
        Chunk<Velocity> velocities,
        ChunkEntities entities)
    {
        var positionSpan = positions.Span;
        var velocitySpan = velocities.Span;
        for (var i = 0; i < positionSpan.Length; i++)
        {
            positionSpan[i].X += velocitySpan[i].X;
            positionSpan[i].Y += velocitySpan[i].Y;
            positionSpan[i].Z += velocitySpan[i].Z;
        }
    }

    private static void UpdateLitheRange(
        Span<Position> positions,
        Span<Velocity> velocities,
        EntityRange entities)
    {
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i].X += velocities[i].X;
            positions[i].Y += velocities[i].Y;
            positions[i].Z += velocities[i].Z;
        }
    }
}
