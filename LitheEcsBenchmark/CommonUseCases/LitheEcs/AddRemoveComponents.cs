using BenchmarkDotNet.Attributes;

namespace LitheEcs;

public class AddRemoveComponents_LitheEcs : AddRemoveComponents
{
    private World world;
    private Entity[] entities;

    [GlobalSetup]
    public void Setup()
    {
        world = new World(Entities);
        entities = world.CreateEntities(Entities);
    }

    [GlobalCleanup]
    public void Shutdown() => world.Dispose();

    protected override void Run1Component()
    {
        foreach (var entity in entities) entity.Add<Component1>();
        foreach (var entity in entities) entity.Remove<Component1>();
    }

    protected override void Run5Components()
    {
        foreach (var entity in entities) {
            entity.Add(new Component1(), new Component2(), new Component3(), new Component4(), new Component5());
        }
        foreach (var entity in entities) {
            entity.Remove<Component1, Component2, Component3, Component4, Component5>();
        }
    }
}
