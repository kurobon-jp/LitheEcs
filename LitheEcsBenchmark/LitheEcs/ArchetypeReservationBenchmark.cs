using BenchmarkDotNet.Attributes;

namespace LitheEcsBenchmark;

[MemoryDiagnoser]
[BenchmarkCategory(nameof(ArchetypeReservationBenchmark))]
public class ArchetypeReservationBenchmark
{
    [Params(10_000)] public int Capacity { get; set; }

    [Benchmark(Baseline = true)]
    public LitheEcs.World DedicatedArchetypes()
    {
        var world = new LitheEcs.World(Capacity);
        world.ReserveArchetype(Capacity, static a => Base(a));
        world.ReserveArchetype(Capacity, static a => Base(a).Add<Damage>());
        world.ReserveArchetype(Capacity, static a => Base(a).Add<Dead>());
        world.ReserveArchetype(Capacity, static a => Base(a).Add<Damage>().Add<Dead>());
        world.ReserveArchetype(Capacity, static a => InactiveBase(a).Add<Dead>());
        world.ReserveArchetype(Capacity, static a => InactiveBase(a).Add<Damage>().Add<Dead>());
        return world;
    }

    [Benchmark]
    public LitheEcs.World SharedComponentPages()
    {
        var world = new LitheEcs.World(Capacity);
        world.ReserveArchetypeGroup(Capacity, static group => group
            .Add(static a => Base(a))
            .Add(static a => Base(a).Add<Damage>())
            .Add(static a => Base(a).Add<Dead>())
            .Add(static a => Base(a).Add<Damage>().Add<Dead>())
            .Add(static a => InactiveBase(a).Add<Dead>())
            .Add(static a => InactiveBase(a).Add<Damage>().Add<Dead>()));
        return world;
    }

    private static LitheEcs.ArchetypeBuilder Base(LitheEcs.ArchetypeBuilder a) => a
        .Add<C1>().Add<C2>().Add<C3>().Add<C4>().Add<C5>().Add<C6>().Add<Spatial>()
        .Add<C7>().Add<Crowd>().Add<C8>().Add<C9>().Add<C10>().Add<C11>();

    private static LitheEcs.ArchetypeBuilder InactiveBase(LitheEcs.ArchetypeBuilder a) => a
        .Add<C1>().Add<C2>().Add<C3>().Add<C4>().Add<C5>().Add<C6>()
        .Add<C7>().Add<C8>().Add<C9>().Add<C10>().Add<C11>();

    private struct C1; private struct C2; private struct C3; private struct C4;
    private struct C5; private struct C6; private struct C7; private struct C8;
    private struct C9; private struct C10; private struct C11;
    private struct Spatial; private struct Crowd; private struct Damage; private struct Dead;
}
