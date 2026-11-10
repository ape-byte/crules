using System.Diagnostics;
using System.Text.Json;
using Jint;

namespace CRules.Benchmarks;

/// <summary>独立进程首条求值及代表性宿主尾延迟探针，和 BDN 微基准分别报告。</summary>
public static class LifecycleMeasurements
{
    public static void ColdOnce(string mode)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        int result;
        if (mode == "clr")
            result = new RuleCompiler().Compile<RoutingInput, int>(RoutingBenchmarks.ClrSource)
                .RequireExecutable().Execute(new(750, true));
        else
        {
            using var engine = new Engine();
            result = (int)engine.Evaluate(Engine.PrepareScript(RoutingBenchmarks.JsSource + "decide({tokens:750,premium:true});")).AsNumber();
        }
        watch.Stop();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Mode = mode,
            FirstEvaluationMs = watch.Elapsed.TotalMilliseconds,
            ManagedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated,
            Result = result
        }));
    }

    public static async Task RunAsync(string output)
    {
        List<object> cold = [];
        for (int i = 0; i < 3; i++) foreach (string mode in new[] { "clr", "jint" })
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
                start.ArgumentList.Add(typeof(LifecycleMeasurements).Assembly.Location);
            start.ArgumentList.Add("--cold-once"); start.ArgumentList.Add(mode);
            var watch = Stopwatch.StartNew();
            using var process = Process.Start(start)!;
            string text = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(); watch.Stop();
            if (process.ExitCode != 0) throw new InvalidOperationException(error);
            using var payload = JsonDocument.Parse(text);
            if (payload.RootElement.GetProperty("Result").GetInt32() != 2) throw new InvalidOperationException("冷启动结果错误。");
            cold.Add(new { Iteration = i + 1, ProcessLaunchToExitMs = watch.Elapsed.TotalMilliseconds, Details = payload.RootElement.Clone() });
        }
        using var host = new HostLifecycleBenchmarks(); host.Setup();
        var tails = new[] { Measure("ClrJsonSnapshot", host.ClrJsonSnapshot), Measure("JintFreshEngineJson", host.JintFreshEngineJson) };
        string fullPath = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(new
        {
            Date = "2026-11-09",
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Note = "Tail samples are serial local Stopwatch probes, include timer overhead, and are not production load SLO measurements.",
            Cold = cold,
            Tail = tails
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(fullPath);
    }

    private static object Measure(string name, Func<string> execute)
    {
        for (int i = 0; i < 1000; i++) if (execute() != "2") throw new InvalidOperationException("结果错误。");
        const int count = 5000;
        var samples = new double[count];
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
        {
            long start = Stopwatch.GetTimestamp(); string result = execute();
            samples[i] = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
            if (result != "2") throw new InvalidOperationException("结果错误。");
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(samples);
        return new
        {
            Name = name,
            Count = count,
            P50Ns = samples[count / 2],
            P95Ns = samples[(int)(count * .95)],
            P99Ns = samples[(int)(count * .99)],
            AllocatedBytesPerOperation = bytes / (double)count
        };
    }
}
