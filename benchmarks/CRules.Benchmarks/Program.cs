using BenchmarkDotNet.Running;
using CRules.Benchmarks;
if (args.Length > 0 && args[0] == "--business-worker") BusinessWorkloads.RunWorker(args);
else if (args.Length > 0 && args[0] == "--compile-worker") BusinessWorkloads.RunCompileWorker(args);
else if (args.Length > 0 && args[0] == "--business-suite") await BusinessWorkloads.RunSuiteAsync(args);
else if (args.Length > 0 && args[0] == "--business-singleton") await BusinessWorkloads.RunSingletonSupplementAsync(args);
else if (args.Length > 0 && args[0] == "--business-prepare") await BusinessWorkloads.RunPreparationAsync(args);
else if (args.Length >= 2 && args[0] == "--cold-once") LifecycleMeasurements.ColdOnce(args[1]);
else if (args.Length >= 2 && args[0] == "--lifecycle") await LifecycleMeasurements.RunAsync(args[1]);
else if (args.Contains("--verify"))
{
    using var route = new RoutingBenchmarks(); route.Setup();
    using var accounting = new AccountingBenchmarks(); accounting.Setup();
    foreach (int count in new[] { 10, 100, 1000 })
    {
        using var collection = new CollectionBenchmarks { Count = count }; collection.Setup();
    }
    using var host = new HostLifecycleBenchmarks(); host.Setup();
    Console.WriteLine("全部基准 Setup 的跨实现结果校验通过。");
}
else BenchmarkSwitcher.FromAssembly(typeof(RoutingBenchmarks).Assembly).Run(args);
