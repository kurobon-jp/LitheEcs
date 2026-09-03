using BenchmarkDotNet.Attributes;

namespace LitheEcs;

public class DeleteEntity_LitheEcs : DeleteEntity
{
    private World world;
    private Entity[] entities;

    [IterationSetup]
    public void Setup()
    {
        world = new World(Entities);
        entities = world.CreateEntities(Entities).AddComponents();
    }

    [IterationCleanup]
    public void Shutdown() => world.Dispose();

    [Benchmark]
    public override void Run() => world.DespawnBatch(entities);
}
