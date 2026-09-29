using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using LitheEcs;

namespace LitheEcsBenchmark;

/// <summary>Baseline for parallel structural-command recording. Keep this separate from the hot query benchmark.</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 5, iterationCount: 10)]
[BenchmarkCategory("ParallelCommandBuffer")]
public class ParallelCommandBufferBenchmark
{
    private World _world = null!;
    private LitheEcs.ParallelQuery<Position> _query;
    private EntityCommandBuffer _commandBuffer = null!;
    private bool _addMarker;

    [Params(1_024, 16_384, 100_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup() => SetupWorld();

    [Benchmark]
    public void RecordAndPlaybackStructuralChange()
    {
        if (_addMarker)
        {
            _query.Run((positions, entities) =>
            {
                for (var i = 0; i < positions.Length; i++)
                    entities.CommandBuffer.AddComponent(entities[i], new ParallelCommandMarker());
            });
        }
        else
        {
            _query.Run((positions, entities) =>
            {
                for (var i = 0; i < positions.Length; i++)
                    entities.CommandBuffer.RemoveComponent<ParallelCommandMarker>(entities[i]);
            });
        }
        _commandBuffer.Playback();
        _addMarker = !_addMarker;
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    private void SetupWorld()
    {
        _world = new World(EntityCount);
        var template = _world.CreateTemplate().Add(new Position());
        template.SpawnBatch(EntityCount);
        _commandBuffer = _world.CommandBuffer;
        _query = _world.Query<Position>().AsParallelQuery(1, 1_024);
        _query.Run(static (_, _) => { });
        _addMarker = true;
    }
}

public struct ParallelCommandMarker { }

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 5, iterationCount: 10)]
[BenchmarkCategory("ParallelCommandBuffer", "SerialBaseline")]
public class SerialCommandBufferBenchmark
{
    private World _world = null!;
    private EntityCommandBuffer _commandBuffer = null!;
    private Entity[] _entities = null!;
    private bool _addMarker;

    [Params(1_024, 16_384, 100_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _world = new World(EntityCount);
        var template = _world.CreateTemplate().Add(new Position());
        _entities = new Entity[EntityCount];
        template.SpawnBatch(_entities);
        _commandBuffer = _world.CommandBuffer;

        for (var i = 0; i < EntityCount; i++)
            _commandBuffer.AddComponent(_entities[i], new ParallelCommandMarker());
        _commandBuffer.Playback();
        _addMarker = false;
    }

    [Benchmark]
    public void RecordAndPlaybackStructuralChange()
    {
        if (_addMarker)
        {
            for (var i = 0; i < _entities.Length; i++)
                _commandBuffer.AddComponent(_entities[i], new ParallelCommandMarker());
        }
        else
        {
            for (var i = 0; i < _entities.Length; i++)
                _commandBuffer.RemoveComponent<ParallelCommandMarker>(_entities[i]);
        }

        _commandBuffer.Playback();
        _addMarker = !_addMarker;
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();
}

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 5, iterationCount: 10)]
[BenchmarkCategory("ParallelCommandBuffer", "DeferredEntity")]
public class ParallelDeferredCommandBenchmark
{
    private World _world = null!;
    private LitheEcs.ParallelQuery<Position> _query;
    private EntityCommandBuffer _commandBuffer = null!;
    private Entity _relationTarget;

    [Params(1_024, 16_384)]
    public int EntityCount { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
        _world = new World(EntityCount + 1);
        _world.CreateTemplate().Add(new Position()).SpawnBatch(EntityCount);
        _relationTarget = _world.Spawn();
        _commandBuffer = _world.CommandBuffer;
        _query = _world.Query<Position>().AsParallelQuery(1, 1_024);
        _query.Run(static (_, _) => { });
    }

    [Benchmark(Baseline = true)]
    public void SpawnAndPlaybackDeferredCommands()
    {
        _query.Run((positions, entities) =>
        {
            for (var i = 0; i < positions.Length; i++)
                entities.CommandBuffer.Spawn();
        });
        _commandBuffer.Playback();
    }

    [Benchmark]
    public void SpawnAddComponentAndPlaybackDeferredCommands()
    {
        _query.Run((positions, entities) =>
        {
            for (var i = 0; i < positions.Length; i++)
            {
                var deferred = entities.CommandBuffer.Spawn();
                entities.CommandBuffer.AddComponent(deferred, new ParallelDeferredMarker());
            }
        });
        _commandBuffer.Playback();
    }

    [Benchmark]
    public void SpawnAddComponentAndRelationAndPlaybackDeferredCommands()
    {
        _query.Run((positions, entities) =>
        {
            for (var i = 0; i < positions.Length; i++)
            {
                var deferred = entities.CommandBuffer.Spawn();
                entities.CommandBuffer.AddComponent(deferred, new ParallelDeferredMarker());
                entities.CommandBuffer.AddRelation<ParallelDeferredRelation>(deferred, _relationTarget);
            }
        });
        _commandBuffer.Playback();
    }

    [Benchmark]
    public void SpawnAddRelationAndPlaybackDeferredCommands()
    {
        _query.Run((positions, entities) =>
        {
            for (var i = 0; i < positions.Length; i++)
            {
                var deferred = entities.CommandBuffer.Spawn();
                entities.CommandBuffer.AddRelation<ParallelDeferredRelation>(deferred, _relationTarget);
            }
        });
        _commandBuffer.Playback();
    }

    [Benchmark]
    public void AddRelationToExistingEntitiesAndPlayback()
    {
        _query.Run((positions, entities) =>
        {
            for (var i = 0; i < positions.Length; i++)
                entities.CommandBuffer.AddRelation<ParallelDeferredRelation>(entities[i], _relationTarget);
        });
        _commandBuffer.Playback();
    }

    [Benchmark]
    public void AddDuplicateRelationAndPlayback()
    {
        _query.Run((positions, entities) =>
        {
            for (var i = 0; i < positions.Length; i++)
            {
                entities.CommandBuffer.AddRelation<ParallelDeferredRelation>(entities[i], _relationTarget);
                entities.CommandBuffer.AddRelation<ParallelDeferredRelation>(entities[i], _relationTarget);
            }
        });
        _commandBuffer.Playback();
    }

    [IterationCleanup]
    public void IterationCleanup() => _world.Dispose();
}

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 5, iterationCount: 10)]
[BenchmarkCategory("ParallelCommandBuffer", "Relation")]
public class ParallelRelationRemovalBenchmark
{
    private World _world = null!;
    private LitheEcs.ParallelQuery<Position> _query;
    private EntityCommandBuffer _commandBuffer = null!;
    private Entity _relationTarget;

    [Params(1_024, 16_384)]
    public int EntityCount { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
        _world = new World(EntityCount + 1);
        var sources = new Entity[EntityCount];
        _world.CreateTemplate().Add(new Position()).SpawnBatch(EntityCount, sources);
        _relationTarget = _world.Spawn();
        for (var i = 0; i < sources.Length; i++)
            sources[i].AddRelation<ParallelDeferredRelation>(_relationTarget);
        _commandBuffer = _world.CommandBuffer;
        _query = _world.Query<Position>().AsParallelQuery(1, 1_024);
        _query.Run(static (_, _) => { });
    }

    [Benchmark]
    public void RemoveRelationFromExistingEntitiesAndPlayback()
    {
        _query.Run((positions, entities) =>
        {
            for (var i = 0; i < positions.Length; i++)
                entities.CommandBuffer.RemoveRelation<ParallelDeferredRelation>(entities[i], _relationTarget);
        });
        _commandBuffer.Playback();
    }

    [Benchmark]
    public void RemoveAllRelationsFromExistingEntitiesAndPlayback()
    {
        _query.Run((positions, entities) =>
        {
            for (var i = 0; i < positions.Length; i++)
                entities.CommandBuffer.RemoveRelation<ParallelDeferredRelation>(entities[i]);
        });
        _commandBuffer.Playback();
    }

    [IterationCleanup]
    public void IterationCleanup() => _world.Dispose();
}

public struct ParallelDeferredMarker { }
public struct ParallelDeferredRelation { }
