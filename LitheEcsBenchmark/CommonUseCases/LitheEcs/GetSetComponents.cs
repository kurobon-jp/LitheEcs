using BenchmarkDotNet.Attributes;

namespace LitheEcs;

public class GetSetComponents_LitheEcs : GetSetComponents
{
    private World world;
    private Entity[] entities;

    [GlobalSetup]
    public void Setup()
    {
        world = new World(Entities);
        entities = world.CreateEntities(Entities).AddComponents();
    }

    [GlobalCleanup]
    public void Shutdown() => world.Dispose();

    protected override void Run1Component()
    {
        foreach (var entity in entities) {
            var data = entity.Data;
            data.Get<Component1>() = new Component1();
        }
    }

    protected override void Run5Components()
    {
        foreach (var entity in entities) {
            var data = entity.Data;
            data.Get<Component1>() = new Component1(); data.Get<Component2>() = new Component2();
            data.Get<Component3>() = new Component3(); data.Get<Component4>() = new Component4();
            data.Get<Component5>() = new Component5();
        }
    }
}
