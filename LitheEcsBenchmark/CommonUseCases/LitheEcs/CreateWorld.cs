using BenchmarkDotNet.Attributes;

namespace LitheEcs;

public class CreateWorld_LitheEcs : CreateWorld
{
    [Benchmark]
    public override void Run()
    {
        using var world = new World();
    }
}
