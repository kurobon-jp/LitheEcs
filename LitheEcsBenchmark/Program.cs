using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using LitheEcsBenchmark;

var config = ManualConfig.Create(DefaultConfig.Instance)
    .WithOption(ConfigOptions.JoinSummary, true)
    .AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory)
    .AddColumn(CategoriesColumn.Default, RankColumn.Arabic)
    .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.Method, MethodOrderPolicy.Declared))
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddJob(Job.ShortRun.WithToolchain(InProcessNoEmitToolchain.Instance)
    );

BenchmarkSwitcher.FromAssembly(typeof(LitheEcsReleaseBenchmark).Assembly).Run(args, config);
