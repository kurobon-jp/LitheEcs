using BenchmarkDotNet.Attributes;

namespace LitheEcs;

public class CommandBufferAddRemove_LitheEcs : CommandBufferAddRemove
{
    private World world;
    private Entity[] entities;
    private EntityCommandBuffer commandBuffer;

    [GlobalSetup]
    public void Setup()
    {
        world = new World(Entities);
        entities = world.CreateEntities(Entities);
        commandBuffer = world.CommandBuffer;
    }

    [GlobalCleanup]
    public void Shutdown() => world.Dispose();

    [Benchmark]
    public override void Run()
    {
        commandBuffer.AddComponentBatch(entities, new Component1());
        commandBuffer.AddComponentBatch(entities, new Component2());
        commandBuffer.Playback();

        foreach (var entity in entities) {
            commandBuffer.RemoveComponent<Component1>(entity);
            commandBuffer.RemoveComponent<Component2>(entity);
        }
        commandBuffer.Playback();
    }
}
