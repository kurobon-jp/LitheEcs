# LitheEcs benchmark guide

This project contains two benchmark suites with different purposes.

## Comparison suite

`CommonUseCases` is the authoritative LitheEcs versus Friflo comparison. Each category uses the
same scenario, entity count, and component count for both libraries. Friflo is the baseline, so
the `Ratio` column reads as follows:

| Ratio | Meaning |
|---:|---|
| `< 1.00` | LitheEcs is faster |
| `1.00` | Same speed as the Friflo baseline |
| `> 1.00` | LitheEcs is slower |

`Allocated` should be compared independently from execution time. A faster result that allocates
on every invocation may still be unsuitable for a game loop.

Run all comparison categories:

```powershell
dotnet build LitheEcsBenchmark/LitheEcsBenchmark.csproj -c Release --no-restore
dotnet run --project LitheEcsBenchmark -c Release --no-build -- --filter "*_LitheEcs*" "*_Friflo*"
```

BenchmarkDotNet filters use generated type names, so selecting a category directly is usually
more convenient:

```powershell
dotnet run --project LitheEcsBenchmark -c Release --no-build -- --filter "*QueryComponents*"
dotnet run --project LitheEcsBenchmark -c Release --no-build -- --filter "*CreateEntity*"
dotnet run --project LitheEcsBenchmark -c Release --no-build -- --filter "*AddRemoveComponents*"
```

## LitheEcs release suite

`LitheEcsReleaseBenchmark` contains only LitheEcs hot paths and API variants. Its setup does not
create a Friflo world. Use this suite for release regression checks:

```powershell
dotnet run --project LitheEcsBenchmark -c Release --no-build -- --filter "*LitheEcsReleaseBenchmark*"
```

`FrifloComparisonBenchmark` derives from the same LitheEcs fixture and adds Friflo baselines.
Use it for focused implementation comparisons; `CommonUseCases` remains the authoritative fair
library comparison:

```powershell
dotnet run --project LitheEcsBenchmark -c Release --no-build -- --filter "*FrifloComparisonBenchmark*"
```

Friflo is referenced only by the benchmark project. It is not a dependency of the LitheEcs
library or its release package.

## Diagnostic suites

### Parallel query

`ParallelQueryComparisonBenchmark` compares LitheEcs `AsParallelQuery().Run()` and Friflo
`QueryJob.RunParallel()` using the same `Position += Velocity` workload. Queries,
delegates, jobs, and Friflo's shared `ParallelJobRunner` are created once in setup;
worker threads are warmed up before measurements.

```powershell
dotnet run -c Release --project LitheEcsBenchmark -- --filter *ParallelQueryComparisonBenchmark*
```

### Component type overflow

`ComponentTypeOverflowBenchmark` is a LitheEcs-only regression benchmark for direct component
type IDs and IDs beyond the 256-entry inline lookup range.

```powershell
dotnet run -c Release --project LitheEcsBenchmark -- --filter *ComponentTypeOverflowBenchmark*
```

Use a class and method filter to investigate a named hot path, for example:

```powershell
dotnet run --project LitheEcsBenchmark -c Release --no-build -- --filter "*FrifloComparisonBenchmark*Two*"
```

## Reading results

Results are written to `BenchmarkDotNet.Artifacts/results/` in GitHub Markdown and CSV formats.
When recording conclusions, include:

- runtime, CPU, .NET version, and build configuration;
- entity and component counts;
- Mean, Ratio, and Allocated;
- whether the result came from `ShortRun`;
- at least one repeated run when the difference is below 10%.

`ShortRun` is intended for fast iteration and can be noisy. Treat ratios between 0.90 and 1.10 as
inconclusive until confirmed with a longer BenchmarkDotNet job.

## Result summary template

| Category | Parameters | LitheEcs | Friflo | Ratio | Allocated delta | Conclusion |
|---|---|---:|---:|---:|---:|---|
| QueryComponents | 100 entities, 1 component | — | — | — | — | — |

Keep comparisons within one category and identical parameter set. Never compare raw means from
different entity counts, component counts, or lifecycle setup.
