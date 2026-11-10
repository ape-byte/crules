using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Jint;
using Jint.Native;

namespace CRules.Benchmarks;

public sealed record BusinessMeasurement(string Scenario, string Mode, int Round, int Count, int Seed,
    double PrepareMs, double ElapsedMs, double CpuMs, long AllocatedBytes, int Gen0, int Gen1, int Gen2,
    long WorkingSetBefore, long WorkingSetAfter, long PeakWorkingSet, long ManagedHeapBefore, long ManagedHeapAfter,
    long ManagedHeapAfterCleanup, long CommittedBefore, long CommittedAfter, ulong ResultChecksum, ulong ContextChecksum,
    string Runtime, int ProcessId, int WarmupCount, double WarmupMs);

public static class BusinessWorkloads
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void RunCompileWorker(string[] args)
    {
        var scenario = BusinessRules.Scenarios.Single(s => s.Id == args[1]);
        int count = int.Parse(args[2]);
        var compiler = BenchmarkCompiler.Create();
        string code = BusinessRules.Clr(scenario);
        for (int i = 0; i < 5; i++) BenchmarkCompiler.Compile<BusinessContext, long>(compiler, code);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetTotalAllocatedBytes(true);
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var timer = Stopwatch.StartNew();
        Func<BusinessContext, long>? compiled = null;
        for (int i = 0; i < count; i++) compiled = BenchmarkCompiler.Compile<BusinessContext, long>(compiler, code);
        timer.Stop();
        long allocated = GC.GetTotalAllocatedBytes(true) - before;
        var context = BusinessContexts.Create(new Random(20261109), 0, scenario.Large);
        if (compiled!(context) != BusinessRules.Reference(scenario, context)) throw new InvalidOperationException("Compile probe result mismatch.");
        File.WriteAllText(args[3], JsonSerializer.Serialize(new
        {
            Scenario = scenario.Id, Round = int.Parse(args[4]), Count = count,
            ElapsedMs = timer.Elapsed.TotalMilliseconds, AllocatedBytes = allocated,
            Gen0 = GC.CollectionCount(0) - g0, Gen1 = GC.CollectionCount(1) - g1, Gen2 = GC.CollectionCount(2) - g2,
            SourceSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)))
        }, Json));
    }

    public static void RunWorker(string[] args)
    {
        var scenario = BusinessRules.Scenarios.Single(s => s.Id == args[1]);
        string mode = args[2], expectedFile = args[3], output = args[4];
        int count = int.Parse(args[5]), seed = int.Parse(args[6]), round = int.Parse(args[7]);
        long[] expected = MemoryMarshal.Cast<byte, long>(File.ReadAllBytes(expectedFile)).ToArray();
        if (expected.Length != count) throw new InvalidOperationException("期望值数量不匹配。");
        var prepare = Stopwatch.StartNew();
        var executor = new BusinessExecutor(scenario, mode);
        prepare.Stop();
        var warmRandom = new Random(seed + 1);
        var warmTimer = Stopwatch.StartNew();
        int warmCount = 0;
        for (int i = 0; i < 2000 || warmTimer.ElapsedMilliseconds < 750; i++)
        {
            var ctx = BusinessContexts.Create(warmRandom, i, scenario.Large);
            long result = executor.Execute(ctx);
            if (mode != "BuildOnly" && result != BusinessRules.Reference(scenario, ctx)) throw new InvalidOperationException("预热结果不一致。");
            warmCount++;
        }
        warmTimer.Stop();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess(); process.Refresh();
        long wsBefore = process.WorkingSet64, heapBefore = GC.GetTotalMemory(false);
        long committedBefore = GC.GetGCMemoryInfo().TotalCommittedBytes;
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        var random = new Random(seed);
        var cpuBefore = process.TotalProcessorTime;
        long allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        ulong resultHash = 1469598103934665603UL, contextHash = 1469598103934665603UL;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            // context 的全部分配和随机数据生成在计时内，没有复用预生成 context。
            var context = BusinessContexts.Create(random, i, scenario.Large);
            long value = executor.Execute(context);
            if (mode != "BuildOnly" && value != expected[i]) throw new InvalidOperationException($"第 {i} 项不一致：{value} != {expected[i]}");
            resultHash = Mix(resultHash, value);
            contextHash = Mix(contextHash, context.Request.InputTokens + context.Request.Flags + context.Metrics[0]);
        }
        watch.Stop();
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocationBefore;
        int g0 = GC.CollectionCount(0) - gen0, g1 = GC.CollectionCount(1) - gen1, g2 = GC.CollectionCount(2) - gen2;
        process.Refresh();
        var cpu = process.TotalProcessorTime - cpuBefore;
        long wsAfter = process.WorkingSet64, peak = process.PeakWorkingSet64;
        long heapAfter = GC.GetTotalMemory(false), committedAfter = GC.GetGCMemoryInfo().TotalCommittedBytes;
        executor.Dispose();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long heapCleanup = GC.GetTotalMemory(false);
        GC.KeepAlive(executor); GC.KeepAlive(expected);
        var measurement = new BusinessMeasurement(scenario.Id, mode, round, count, seed, prepare.Elapsed.TotalMilliseconds,
            watch.Elapsed.TotalMilliseconds, cpu.TotalMilliseconds, allocated, g0, g1, g2,
            wsBefore, wsAfter, peak, heapBefore, heapAfter, heapCleanup, committedBefore, committedAfter,
            resultHash, contextHash, RuntimeInformation.FrameworkDescription, Environment.ProcessId, warmCount, warmTimer.Elapsed.TotalMilliseconds);
        File.WriteAllText(output, JsonSerializer.Serialize(measurement, Json));
    }

    private static ulong Mix(ulong hash, long value) => unchecked((hash ^ (ulong)value) * 1099511628211UL);

    public static async Task RunSuiteAsync(string[] args)
    {
        string output = Path.GetFullPath(args[1]);
        int count = args.Length > 2 ? int.Parse(args[2]) : 100000;
        int rounds = args.Length > 3 ? int.Parse(args[3]) : 3;
        const int seed = 20261109;
        Directory.CreateDirectory(output); Directory.CreateDirectory(Path.Combine(output, "rules"));
        Directory.CreateDirectory(Path.Combine(output, "workers"));
        List<object> metadata = [];
        foreach (var scenario in BusinessRules.Scenarios)
        {
            string clr = BusinessRules.Clr(scenario), js = BusinessRules.Javascript(scenario);
            File.WriteAllText(Path.Combine(output, "rules", scenario.Id + ".clr.txt"), clr);
            File.WriteAllText(Path.Combine(output, "rules", scenario.Id + ".js.txt"), js);
            var random = new Random(seed);
            long[] expected = new long[count];
            long min = long.MaxValue, max = long.MinValue, jsonBytes = 0;
            int[] flags = new int[4]; int vip = 0;
            for (int i = 0; i < count; i++)
            {
                var context = BusinessContexts.Create(random, i, scenario.Large);
                expected[i] = BusinessRules.Reference(scenario, context);
                min = Math.Min(min, expected[i]); max = Math.Max(max, expected[i]);
                flags[context.Request.Flags % 4]++; if (context.Request.Vip) vip++;
                if (i < 16) jsonBytes += JsonSerializer.SerializeToUtf8Bytes(context).Length;
                if (i == 0) File.WriteAllText(Path.Combine(output, "rules", scenario.Id + ".context.json"), JsonSerializer.Serialize(context, Json));
            }
            File.WriteAllBytes(Path.Combine(output, scenario.Id + ".expected.bin"), MemoryMarshal.AsBytes(expected.AsSpan()).ToArray());
            metadata.Add(new
            {
                scenario.Id,
                scenario.Name,
                scenario.Large,
                scenario.Locals,
                scenario.Conditions,
                AverageContextJsonBytes = jsonBytes / 16d,
                ExpectedMin = min,
                ExpectedMax = max,
                FlagBuckets = flags,
                VipCount = vip,
                SourceSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(clr)))
            });
        }
        var sourceHashes = Directory.GetFiles(AppContext.BaseDirectory, "CRules*.dll")
            .Select(p => new { File = Path.GetFileName(p), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) }).ToArray();
        List<BusinessMeasurement> results = [];
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
        {
            Date = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd"),
            Count = count,
            Rounds = rounds,
            Seed = seed,
            WarmupPerWorker = 2000,
            Timer = "Stopwatch wall time, single-thread workload, no overhead subtraction",
            Memory = "Fresh process per row; total allocated includes workload; peak working set includes process startup/preparation/warmup",
            NumericSemantics = "All computations are safe integers or fixed-point units; exact item-by-item comparison; no decimal string-helper penalty",
            Machine = new
            {
                OS = RuntimeInformation.OSDescription,
                Runtime = RuntimeInformation.FrameworkDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                LogicalCores = Environment.ProcessorCount
            },
            Scenarios = metadata,
            AssemblyHashes = sourceHashes
        }, Json));

        if (rounds == 0) { Console.WriteLine("INPUTS_EXPORTED"); return; }
        // 各轮旋转实现顺序，避免总把某个实现放在热机或低温阶段。
        for (int round = 1; round <= rounds; round++)
            foreach (var scenario in BusinessRules.Scenarios)
            {
                var modes = BusinessExecutor.Modes.ToList();
                if (scenario.Id is not ("S1" or "S4")) modes.Remove("BuildOnly");
                int rotate = (round - 1) % modes.Count;
                foreach (string mode in modes.Skip(rotate).Concat(modes.Take(rotate)))
                {
                    string file = Path.Combine(output, "workers", $"{scenario.Id}-{mode}-{round}.json");
                    Console.WriteLine($"RUN round={round}/{rounds} scenario={scenario.Id} mode={mode} count={count}");
                    var psi = ChildStart();
                    foreach (string arg in new[] { "--business-worker", scenario.Id, mode, Path.Combine(output, scenario.Id + ".expected.bin"),
                    file, count.ToString(), seed.ToString(), round.ToString() }) psi.ArgumentList.Add(arg);
                    using var child = Process.Start(psi)!;
                    var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
                    await child.WaitForExitAsync();
                    string outText = await stdout, errText = await stderr;
                    if (child.ExitCode != 0) throw new InvalidOperationException($"{scenario.Id}/{mode} failed: {outText}\n{errText}");
                    var result = JsonSerializer.Deserialize<BusinessMeasurement>(File.ReadAllText(file))!;
                    results.Add(result);
                    File.WriteAllText(Path.Combine(output, "results.partial.json"), JsonSerializer.Serialize(results, Json));
                    Console.WriteLine($"DONE {scenario.Id}/{mode}: {result.ElapsedMs:F2} ms; {result.AllocatedBytes / (double)count:F1} B/op; GC={result.Gen0}/{result.Gen1}/{result.Gen2}");
                }
            }
        foreach (var scenario in BusinessRules.Scenarios)
        {
            var valid = results.Where(r => r.Scenario == scenario.Id && r.Mode != "BuildOnly").ToArray();
            if (valid.Select(r => r.ResultChecksum).Distinct().Count() != 1 || valid.Select(r => r.ContextChecksum).Distinct().Count() != 1)
                throw new InvalidOperationException($"{scenario.Id} 校验和不一致。");
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, Json));
        Console.WriteLine($"SUITE_PASS rows={results.Count}; count_per_row={count}; total_checked={results.Where(r => r.Mode != "BuildOnly").Sum(r => (long)r.Count)}");
    }

    private static ProcessStartInfo ChildStart()
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") psi.ArgumentList.Add(typeof(BusinessWorkloads).Assembly.Location);
        return psi;
    }

    public static async Task RunSingletonSupplementAsync(string[] args)
    {
        string output = Path.GetFullPath(args[1]);
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
        int count = manifest.RootElement.GetProperty("Count").GetInt32();
        int rounds = manifest.RootElement.GetProperty("Rounds").GetInt32();
        int seed = manifest.RootElement.GetProperty("Seed").GetInt32();
        var results = JsonSerializer.Deserialize<List<BusinessMeasurement>>(File.ReadAllText(Path.Combine(output, "results.json")))!;
        results.RemoveAll(r => r.Mode == "JintSingleton");
        for (int round = 1; round <= rounds; round++) foreach (var scenario in BusinessRules.Scenarios)
        {
            string file = Path.Combine(output, "workers", $"{scenario.Id}-JintSingleton-{round}.json");
            var psi = ChildStart();
            foreach (string arg in new[]{"--business-worker",scenario.Id,"JintSingleton",Path.Combine(output,scenario.Id+".expected.bin"),
                file,count.ToString(),seed.ToString(),round.ToString()}) psi.ArgumentList.Add(arg);
            Console.WriteLine($"SINGLETON RUN {scenario.Id} round={round}/{rounds} count={count}");
            using var child = Process.Start(psi)!;
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync();
            string outText = await stdout, errText = await stderr;
            if (child.ExitCode != 0) throw new InvalidOperationException(outText + errText);
            var result = JsonSerializer.Deserialize<BusinessMeasurement>(File.ReadAllText(file))!;
            var reference = results.First(r => r.Scenario == scenario.Id && r.Mode == "CRules");
            if (result.ResultChecksum != reference.ResultChecksum || result.ContextChecksum != reference.ContextChecksum)
                throw new InvalidOperationException("单例入口与原测试结果不一致。");
            results.Add(result);
            File.WriteAllText(Path.Combine(output, "results.partial.json"), JsonSerializer.Serialize(results, Json));
            Console.WriteLine($"SINGLETON DONE {scenario.Id}: {result.ElapsedMs:F2} ms; {result.AllocatedBytes / (double)count:F1} B/op");
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, Json));
        File.WriteAllText(Path.Combine(output, "singleton-supplement.json"), JsonSerializer.Serialize(new
        {
            Mode = "JintSingleton",
            EngineLifetime = "One Engine per worker process, reused for warmup plus 100000 measured calls",
            Entry = "SetValue(ctx, fresh CLR object) + Evaluate(prepared decide(ctx)); function declared once",
            Threading = "Sequential; no concurrent access to a Jint Engine",
            Rounds = rounds,
            Count = count,
            Note = "Additional entry requested after baseline run; production CRules/Jint versions and workload algorithms unchanged",
            AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(BusinessWorkloads).Assembly.Location)))
        }, Json));
        Console.WriteLine($"SINGLETON_SUITE_PASS total_rows={results.Count}");
    }

    public static Task RunPreparationAsync(string[] args)
    {
        string output = Path.GetFullPath(args[1]);
        var rows = new List<object>();
        foreach (var scenario in BusinessRules.Scenarios)
        {
            int count = scenario.Locals == 1 ? 1000 : 200;
            long[] expected = MemoryMarshal.Cast<byte, long>(File.ReadAllBytes(Path.Combine(output, scenario.Id + ".expected.bin"))).ToArray();
            foreach (string mode in new[] { "CRules", "JintClr", "JintIsolatedJson", "JintSingletonParse" })
                for (int round = 1; round <= 3; round++)
                {
                    using var singleton = mode == "JintSingletonParse" ? new Jint.Engine() : null;
                    string singletonSource = BusinessRules.Javascript(scenario) + "\ndecide(ctx);";
                    // 每次重新构造 executor，包含规则解析/编译。这里只测实际 200/1000 次，不外推成十万次。
                    for (int i = 0; i < 5; i++)
                    {
                        if (singleton is not null)
                        {
                            var warmContext = BusinessContexts.Create(new Random(i), i, scenario.Large);
                            singleton.SetValue("ctx", warmContext);
                            if (singleton.Evaluate(singletonSource).AsNumber() != BusinessRules.Reference(scenario, warmContext))
                                throw new InvalidOperationException("单例解析预热失败。");
                            continue;
                        }
                        using var warm = new BusinessExecutor(scenario, mode);
                        var ctx = BusinessContexts.Create(new Random(i), i, scenario.Large);
                        if (warm.Execute(ctx) != BusinessRules.Reference(scenario, ctx)) throw new InvalidOperationException("准备探针预热失败。");
                    }
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    var random = new Random(20261109);
                    int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                    long before = GC.GetTotalAllocatedBytes(true);
                    var watch = Stopwatch.StartNew();
                    for (int i = 0; i < count; i++)
                    {
                        var ctx = BusinessContexts.Create(random, i, scenario.Large);
                        if (singleton is not null)
                        {
                            singleton.SetValue("ctx", ctx);
                            double result = singleton.Evaluate(singletonSource).AsNumber();
                            if (result != expected[i]) throw new InvalidOperationException("单例重新解析结果不一致。");
                        }
                        else
                        {
                            using var executor = new BusinessExecutor(scenario, mode);
                            if (executor.Execute(ctx) != expected[i]) throw new InvalidOperationException("重新解析结果不一致。");
                        }
                    }
                    watch.Stop();
                    var row = new
                    {
                        scenario.Id,
                        Mode = mode,
                        Round = round,
                        Count = count,
                        ElapsedMs = watch.Elapsed.TotalMilliseconds,
                        AllocatedBytes = GC.GetTotalAllocatedBytes(true) - before,
                        Gen0 = GC.CollectionCount(0) - g0,
                        Gen1 = GC.CollectionCount(1) - g1,
                        Gen2 = GC.CollectionCount(2) - g2
                    };
                    rows.Add(row);
                    Console.WriteLine($"PREPARE {scenario.Id}/{mode} round={round}: {count} iterations {row.ElapsedMs:F2} ms");
                }
        }
        File.WriteAllText(Path.Combine(output, "preparation.json"), JsonSerializer.Serialize(rows, Json));
        return Task.CompletedTask;
    }
}
