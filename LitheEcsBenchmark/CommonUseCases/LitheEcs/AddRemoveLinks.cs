using BenchmarkDotNet.Attributes;

namespace LitheEcs;

public class AddRemoveLinks_LitheEcs : AddRemoveLinks
{
    private World world;
    private Entity[] sources;
    private Entity[] targets;

    [GlobalSetup]
    public void Setup()
    {
        world = new World(Entities + Relations);
        sources = world.CreateEntities(Entities).AddComponents();
        targets = world.CreateEntities(Relations).AddComponents();
    }

    [GlobalCleanup]
    public void Shutdown() => world.Dispose();

    [Benchmark]
    public override void Run()
    {
        foreach (var source in sources) {
            for (var n = 0; n < Relations; n++) source.AddRelation<LinkRelation>(targets[n]);
            for (var n = 0; n < Relations; n++) source.RemoveRelation<LinkRelation>(targets[n]);
        }
    }
}
