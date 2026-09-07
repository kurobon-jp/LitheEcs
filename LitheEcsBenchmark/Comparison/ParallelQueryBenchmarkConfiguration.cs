using System.Reflection;
using LitheEcs;

namespace LitheEcsBenchmark;

internal static class ParallelQueryBenchmarkConfiguration
{
    private static readonly MethodInfo ConfigureWorkerCount = GetMethod(
        "ConfigureParallelQueryWorkerCount", typeof(int));
    private static readonly MethodInfo ConfigureScheduling = GetMethod(
        "ConfigureParallelQueryScheduling", typeof(int), typeof(int));

    internal static void SetWorkerCount(World world, int workerCount) =>
        ConfigureWorkerCount.Invoke(world, new object[] { workerCount });

    internal static void SetScheduling(World world, int minimumWorkerEntityCount, int entitiesPerThread) =>
        ConfigureScheduling.Invoke(world, new object[] { minimumWorkerEntityCount, entitiesPerThread });

    private static MethodInfo GetMethod(string name, params Type[] parameterTypes) =>
        typeof(World).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: parameterTypes, modifiers: null)
        ?? throw new MissingMethodException(typeof(World).FullName, name);
}
