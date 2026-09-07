# Latest comparison snapshot

This file is a compact human-readable snapshot. The raw BenchmarkDotNet reports remain the source
of truth and are written to `BenchmarkDotNet.Artifacts/results/`.

## Environment

- Date: 2026-09-06
- Configuration: Release
- BenchmarkDotNet: 0.15.8
- OS: Windows 11 25H2 (10.0.26200.9278)
- .NET SDK: 10.0.302
- Runtime: .NET 10.0.10, x64 RyuJIT x86-64-v3
- Job: ShortRun, in-process, 3 warmup and 3 measurement iterations
- Query entity count: 100

The machine's processor name was unavailable to the benchmark process. ShortRun confidence
intervals are wide, so differences below 10% require a longer confirmation run. The parallel
results are especially noisy and should be treated as a snapshot rather than a stable ranking.

## QueryComponents

Friflo is the baseline (`Ratio = 1.00`). Lower is faster.

| Components | Implementation | Mean | Ratio | Allocated | Result |
|---:|---|---:|---:|---:|---|
| 1 | Friflo `Run` | 45.03 ns | 1.00 | 0 B | baseline |
| 1 | LitheEcs `Run` | 57.98 ns | 1.29 | 0 B | 29% slower |
| 5 | Friflo `Run` | 206.80 ns | 1.00 | 0 B | baseline |
| 5 | LitheEcs `Run` | 57.26 ns | 0.28 | 0 B | 3.6x faster |

Interpretation: the single-component path remains behind Friflo in this run. The aligned
five-component LitheEcs path is substantially faster. No compared query allocated managed memory.

Raw report:
`BenchmarkRun-joined-2026-09-06-14-08-47-report-github.md`.

## ParallelQuery

The workload updates all three position fields with `Position += Velocity`. Both implementations
reuse their query, callback, and parallel execution objects after a setup warmup. The current suite
compares the LitheEcs range callback with Friflo's range callback.

| Entities | LitheEcs Range callback | Friflo Range callback | LitheEcs / Friflo | Result |
|---:|---:|---:|---:|---|
| 1,000 | 1.155 us | 0.932 us | 1.24 | latest LitheEcs ShortRun was noisy |
| 100,000 | 42.136 us | 33.383 us | 1.26 | Friflo faster; result remains noisy |
| 1,000,000 | 639.883 us | 729.556 us | 0.88 | LitheEcs faster across the latest separate runs |

MemoryDiagnoser reported effectively zero allocation in all measured cases. The benchmark explicitly
uses `minimumEntityCount: 2048` and `batchSize: 2048`, so these values do not measure the API defaults.
LitheEcs now caches its prepared physical work ranges while matching archetype contents remain stable;
spawn, despawn, and component-driven archetype moves invalidate and rebuild the cache. Friflo's
100,000-entity result remained variable (`StdDev` 3.775 us), so the cross-library ratio requires
confirmation with a longer out-of-process run.

Raw report:
`LitheEcsBenchmark.ParallelQueryComparisonBenchmark-report-github.md`.

## ParallelQuery worker-count sweep

The LitheEcs range workload was measured with the calling thread included in the total thread count.

| Entities | 1 thread | 2 threads | 4 threads | 6 threads | 12 threads | Best practical default |
|---:|---:|---:|---:|---:|---:|---|
| 100,000 | 108.04 us | 62.36 us | 48.30 us | 47.95 us | 45.83 us | 4 active threads |
| 1,000,000 | 2.143 ms | 2.040 ms | 1.672 ms | 1.299 ms | 0.792 ms | 12 active threads |

The finer 4,096-to-100,000 sweep selected a 32,768-entity activation threshold and a target of 8,192
entities per active thread. Static contiguous partitioning removes atomic claiming through 100,000
entities. Larger runs retain dynamic claiming because static scheduling regressed the 1,000,000
case from about 0.64 ms to about 1.01 ms in confirmation runs.

Raw report:
`LitheEcsBenchmark.ParallelQueryWorkerCountBenchmark-report-github.md`.
