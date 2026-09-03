using BenchmarkDotNet.Attributes;

namespace LitheEcs;

public class QueryComponents_LitheEcs : QueryComponents
{
    private World world;
    private Query<Component1> query1;
    private Query<Component1, Component2, Component3, Component4, Component5> query5;

    [GlobalSetup]
    public void Setup()
    {
        world = new World(Entities);
        world.CreateTemplate().Add(new Component1()).Add(new Component2()).Add(new Component3())
            .Add(new Component4()).Add(new Component5()).SpawnBatch(Entities);
        query1 = world.Query<Component1>();
        query5 = world.Query<Component1, Component2, Component3, Component4, Component5>();
        var count = 0;
        foreach (ref var unused in query1) count++;
        Check.AreEqual(Entities, count);
    }

    [GlobalCleanup]
    public void Shutdown() => world.Dispose();

    protected override void Run1Component()
    {
        foreach (ref var component in query1) component.Value++;
    }

    protected override void Run5Components()
    {
        if (query5.TryGetAlignedChunk(out var chunk)) {
            for (var n = 0; n < chunk.Length; n++)
                chunk.Component1[n].Value = chunk.Component2[n].Value + chunk.Component3[n].Value
                    + chunk.Component4[n].Value + chunk.Component5[n].Value;
            return;
        }
        foreach (var (c1, c2, c3, c4, c5) in query5)
            c1.Value.Value = c2.Value.Value + c3.Value.Value + c4.Value.Value + c5.Value.Value;
    }

}

public class QueryFragmented_LitheEcs : QueryFragmented
{
    private World world;
    private Query<Component1> query;

    [GlobalSetup]
    public void Setup()
    {
        world = new World(Entities);
        for (var n = 0; n < Entities; n++) {
            var entity = world.Spawn();
            entity.Add<Component1>();
            if ((n & 1) != 0) entity.Add<Component2>();
            if ((n & 2) != 0) entity.Add<Component3>();
            if ((n & 4) != 0) entity.Add<Component4>();
            if ((n & 8) != 0) entity.Add<Component5>();
        }
        query = world.Query<Component1>();
    }

    [GlobalCleanup]
    public void Shutdown() => world.Dispose();

    [Benchmark]
    public override void Run()
    {
        foreach (ref var component in query) component.Value++;
    }
}
