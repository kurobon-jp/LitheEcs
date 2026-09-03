namespace LitheEcs;

public static class BenchUtils
{
    public static Entity[] CreateEntities(this World world, int count)
    {
        var entities = new Entity[count];
        world.SpawnBatch(count, entities);
        return entities;
    }

    public static Entity[] AddComponents(this Entity[] entities)
    {
        foreach (var entity in entities) {
            entity.Add<Component1>();
            entity.Add<Component2>();
            entity.Add<Component3>();
            entity.Add<Component4>();
            entity.Add<Component5>();
        }
        return entities;
    }
}
