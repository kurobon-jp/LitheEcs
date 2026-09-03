using BenchmarkDotNet.Attributes;

namespace LitheEcs;

public class CreateEntity_LitheEcs : CreateEntity
{
    private World world;

    [IterationSetup]
    public void Setup() => world = new World(Entities);

    [IterationCleanup]
    public void Shutdown() => world.Dispose();

    protected override void CreateEntity1Component()
    {
        for (var n = 0; n < Entities; n++)
            world.Spawn().Add(new Component1 { Value = n });
    }

    protected override void CreateEntity3Components()
    {
        for (var n = 0; n < Entities; n++) {
            var entity = world.Spawn();
            entity.Add(new Component1 { Value = n }, new Component2 { Value = n }, new Component3 { Value = n });
        }
    }
}

public class CreateBulk_LitheEcs : CreateBulk
{
    private World world;

    [IterationSetup]
    public void Setup() => world = new World(Entities);

    [IterationCleanup]
    public void Shutdown() => world.Dispose();

    protected override void CreateEntity1Component()
    {
        world.CreateTemplate().Add(new Component1()).SpawnBatch(Entities);
        var n = 0;
        foreach (ref var component in world.Query<Component1>()) component.Value = n++;
    }

    protected override void CreateEntity3Components()
    {
        world.CreateTemplate().Add(new Component1()).Add(new Component2()).Add(new Component3()).SpawnBatch(Entities);
        var n = 0;
        foreach (var (c1, c2, c3) in world.Query<Component1, Component2, Component3>()) {
            c1.Value.Value = n;
            c2.Value.Value = n;
            c3.Value.Value = n++;
        }
    }
}
