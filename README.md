# LitheEcs

[日本語](README.ja.md) | English | [API Reference](LitheEcs/API.md)

LitheEcs is a small, fast, managed C# Entity Component System designed to feel natural in Unity.
It focuses on predictable APIs, contiguous archetype storage, allocation-free iteration, and
explicit integration with managed objects.

> [!IMPORTANT]
> LitheEcs is under active development. The API may change before the first stable release.

## Highlights

- Managed archetype ECS targeting .NET Standard 2.1 and C# 9
- Plain `struct` components with no marker interface required
- Contiguous component storage and allocation-free typed query iteration
- Typed queries for one to eight components
- Struct query actions that avoid delegate dispatch in hot loops
- Managed parallel range queries with `AsParallelQuery()`
- Batched spawn, despawn, add, and remove operations
- Reusable `EntityTemplate` definitions for common entity layouts
- Generational entity handles with index reuse protection
- Singleton entities identified by `ISingleton` marker components
- Forward and reverse entity relations
- External object and value binding with reverse lookup
- Entity command buffers for deferred structural changes
- Component change collectors and optional debug diagnostics
- Component type IDs beyond 255 through lazily allocated overflow storage

## Requirements

- Unity 2022.3 LTS or later, or another .NET Standard 2.1 compatible runtime
- C# 9 or later
- .NET SDK 10 for building the repository and running its current test project

`AsParallelQuery()` uses managed worker threads. The Unity package separately provides synchronous
Burst and Unity Job System integration for queries with one to three components.

## Getting started

For Unity, open **Window > Package Manager**, select **Add package from git URL**, and enter:

```text
https://github.com/kurobon-jp/LitheEcs.git?path=/UnityPackage
```

For a .NET project, clone this repository and build the solution:

```powershell
cd LitheEcs
dotnet build LitheEcs.sln --configuration Release
```

Then reference `LitheEcs/LitheEcs.csproj` from the .NET project.

## Quick example

```csharp
using LitheEcs;

public struct Position
{
    public float X;
    public float Y;
}

public struct Velocity
{
    public float X;
    public float Y;
}

using var world = new World(defaultCapacity: 1_000);

var entity = world.Spawn();
entity.Add(new Position { X = 10, Y = 20 });
entity.Add(new Velocity { X = 1, Y = -2 });

foreach (var (position, velocity) in world.Query<Position, Velocity>())
{
    position.Value.X += velocity.Value.X;
    position.Value.Y += velocity.Value.Y;
}
```

Component references returned by a query are valid only while the matching storage remains
structurally unchanged. Do not modify the World structure while a query is executing. This includes
spawning or despawning entities and adding or removing components. Updating existing component
values through the query's `ref` parameters is allowed. Record structural changes in an
`EntityCommandBuffer` and call `Playback()` after the query completes.

## Query styles

Use direct iteration when only component data is needed:

```csharp
foreach (ref var position in world.Query<Position>())
    position.X += 1;
```

Use a struct action when both the entity and its components are needed on a hot path:

```csharp
public struct Integrate : IQueryAction<Position, Velocity>
{
    public void Execute(in Entity entity, ref Position position, ref Velocity velocity)
    {
        position.X += velocity.X;
        position.Y += velocity.Y;
    }
}

var integrate = new Integrate();
world.Query<Position, Velocity>().ForEach(ref integrate);
```

Use `EntityQuery` when the primary result is a set of entities rather than component columns:

```csharp
foreach (var entity in world.Query().With<Position>().Without<Disabled>())
    Process(entity);
```

Typed component queries are the preferred path for bulk component processing. Calling
`entity.Get<T>()` for every entity performs additional entity and component-location lookup work.

## Parallel queries

Use `AsParallelQuery()` for sufficiently large, independent component workloads. It divides a typed
query into ranges and runs the range callback on World-owned managed worker threads:

```csharp
world.Query<Position, Velocity>()
    .Without<Disabled>()
    .AsParallelQuery(minimumEntityCount: 4_096, batchSize: 4_096)
    .Run((positions, velocities, entities) =>
    {
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i].X += velocities[i].X;
            positions[i].Y += velocities[i].Y;
        }
    });
```

The callback receives matching component `Span<T>` values and the corresponding `EntityRange`.
Below `minimumEntityCount`, the same callback runs sequentially on the calling thread. Both
`minimumEntityCount` and `batchSize` default to 4,096.

Each callback may update components in its own range, but it must synchronize access to captured
shared state. Do not run parallel queries concurrently or perform structural changes while one is
running. The current `EntityCommandBuffer` is owner-thread-only, so collect structural-change
requests safely and record them after `Run()` returns. This is a managed-thread API, not a Unity
Job System or Burst API.

## Entity lifecycle

```csharp
var entities = new Entity[1_000];
world.SpawnBatch(entities.Length, entities);

world.AddComponentBatch(entities, new Position());
world.DespawnBatch(entities);
```

For repeated layouts, use a template:

```csharp
var projectile = world.CreateTemplate()
    .Add(new Position())
    .Add(new Velocity { X = 20 });

Entity one = projectile.Spawn();
projectile.SpawnBatch(100);
```

## Singleton entities

```csharp
public struct GameSession : ISingleton { }

var session = world.Spawn();
session.Add<GameSession>();
session.Add(new Score());

Entity resolved = world.Singleton<GameSession>();
```

The entity is singleton, not every regular component attached to it.

## Managed object and value binding

```csharp
entity.Bind(gameObject);

if (world.TryGetEntity(gameObject, out var boundEntity))
    Use(boundEntity);
```

Class keys use reference identity. Struct keys use value equality and avoid boxing in steady-state
lookup and binding paths. Bindings are removed automatically when an entity is despawned.

## `Link<T>` managed references

`Link<T>` is a component-based mechanism for storing managed references and is separate from Binding:

```csharp
entity.Add(Link.With(gameObject));
```

## Relations

```csharp
public struct ParentOf { }

parent.AddRelation<ParentOf>(child);

foreach (var target in parent.GetRelations<ParentOf>())
    Use(target);

foreach (var source in world.GetEntitiesWithTarget<ParentOf>(child))
    Use(source);
```

Relation storage tracks both directions and removes affected relations when an entity is despawned.

## Component change collection

Use an `EntityCollector` to react to component events that occurred since the last `Clear()`:

```csharp
using var healthChanges = world
    .Observe<Health>(ComponentEvent.KeyAdded | ComponentEvent.KeyChanged)
    .Or<Dead>(ComponentEvent.KeyRemoved);

foreach (var entity in healthChanges)
{
    if (entity.IsAlive)
        Process(entity);
}

healthChanges.Clear();
```

Each entity appears at most once even if several observed events occur. Removed-component and
despawn events may leave a collected entity no longer alive, so check `IsAlive` when required.
Dispose the collector to unregister its subscriptions; `using` is recommended.

## Deferred structural changes

```csharp
var commands = world.CommandBuffer;

world.Query<Health>().ForEach(
    (in Entity entity, ref Health health) =>
    {
        if (health.Value <= 0)
            commands.Despawn(entity);
    });

commands.Playback();
```

## Acknowledgements

LitheEcs is inspired by [Friflo.Engine.ECS](https://github.com/friflo/Friflo.Engine.ECS) and
[fennecs](https://github.com/outfox/fennecs). LitheEcs draws ideas from both projects while designing
its API around its own goals and Unity use cases.
