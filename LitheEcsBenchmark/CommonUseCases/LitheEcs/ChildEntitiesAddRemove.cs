using BenchmarkDotNet.Attributes;

namespace LitheEcs;

public class ChildEntitiesAddRemove_LitheEcs : ChildEntitiesAddRemove
{
    private World world;
    private Entity[] parents;
    private Entity[][] children;

    [GlobalSetup]
    public void Setup()
    {
        world = new World(Entities * (Constants.ChildCount + 1));
        parents = world.CreateEntities(Entities).AddComponents();
        children = new Entity[Entities][];
        for (var n = 0; n < Entities; n++)
            children[n] = world.CreateEntities(Constants.ChildCount).AddComponents();
    }

    [GlobalCleanup]
    public void Shutdown() => world.Dispose();

    [Benchmark]
    public override void Run()
    {
        for (var n = 0; n < parents.Length; n++)
            for (var child = 0; child < children[n].Length; child++)
                children[n][child].AddRelation<ChildOf>(parents[n]);

        for (var n = 0; n < parents.Length; n++)
            for (var child = children[n].Length - 1; child >= 0; child--)
                children[n][child].RemoveRelation<ChildOf>(parents[n]);
    }
}
