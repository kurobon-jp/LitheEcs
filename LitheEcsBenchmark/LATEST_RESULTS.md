# Latest comparison snapshot

This file is a compact human-readable snapshot. The raw BenchmarkDotNet reports remain the source
of truth and are written to `BenchmarkDotNet.Artifacts/results/`.

## Environment

- Date: 2026-08-08
- Configuration: Release
- BenchmarkDotNet: 0.15.8
- Runtime: .NET 10.0.10, x64 RyuJIT
- Job: ShortRun, in-process, 3 measurement iterations
- Entity count: 100

The machine's processor name was unavailable to the benchmark process. ShortRun confidence
intervals are wide, so differences below 10% require a longer confirmation run.

## QueryComponents

Friflo is the baseline (`Ratio = 1.00`). Lower is faster.

| Components | Implementation | Mean | Ratio | Allocated | Result |
|---:|---|---:|---:|---:|---|
| 1 | Friflo `Run` | 44.99 ns | 1.00 | 0 B | baseline |
| 1 | LitheEcs `Run` | 54.15 ns | 1.20 | 0 B | 20% slower |
| 5 | Friflo `Run` | 258.25 ns | 1.00 | 0 B | baseline |
| 5 | LitheEcs `Run` | 61.43 ns | 0.24 | 0 B | 4.2x faster |

Interpretation: the single-component path remains slightly behind Friflo in this run. The aligned
five-component LitheEcs path is substantially faster. No compared query allocated managed memory.

## ParallelQuery

The workload is `Position += Velocity`. Both implementations reuse their query, callback, and
parallel execution objects after a setup warmup.

| Entities | LitheEcs Entity callback | LitheEcs Range callback | Friflo Range callback | Best LitheEcs vs Friflo |
|---:|---:|---:|---:|---:|
| 1,000 | 2.825 us | 0.948 us | 0.956 us | LitheEcs 1% faster |
| 100,000 | 119.723 us | 110.536 us | 97.805 us | Friflo 12% faster |
| 1,000,000 | 1.028 ms | 0.619 ms | 0.551 ms | Friflo 12% faster |

MemoryDiagnoser reported zero allocation for 1,000 entities and 1–8 normalized bytes for some
large cases; the latter is measurement noise with no observed GC collections. `ParallelForRanges`
removes the per-entity delegate and Entity construction cost and is effectively tied with Friflo
at 1,000 entities, while Friflo is about 12% faster in the larger cases. ShortRun timing for Friflo
remained noisy, so close results should be confirmed with a longer out-of-process run.
