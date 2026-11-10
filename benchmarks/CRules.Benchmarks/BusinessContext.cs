namespace CRules.Benchmarks;

public sealed record BusinessRequest(long InputTokens, long OutputTokens, long CacheTokens, int Flags, bool Vip);
public sealed record BusinessPricing(long InputRate, long OutputRate, long CacheRate);
public sealed record BusinessItem(long Quantity, long UnitPrice, long Discount, long Category, long Stock, long Weight, long Zone, long Tax);

public sealed record BusinessContext(long Sequence, BusinessRequest Request, BusinessPricing Pricing,
    long[] Metrics, BusinessItem[] Items, string[] Tags, Dictionary<string, long> Data, string Payload);

public sealed record BusinessScenario(string Id, string Name, bool Large, int Locals, int Conditions);

/// <summary>所有计时路径均在循环内生成新对象。固定种子使各独立进程收到逐项相同的数据。</summary>
public static class BusinessContexts
{
    private static readonly string[] Keys = Enumerable.Range(0, 32).Select(i => $"K{i}").ToArray();
    public static BusinessContext Create(Random random, int sequence, bool large)
    {
        var request = new BusinessRequest(random.Next(1, 100001), random.Next(1, 20001),
            random.Next(0, 50001), random.Next(0, 1024), random.Next(0, 2) == 0);
        var pricing = new BusinessPricing(random.Next(1, 101), random.Next(1, 501), random.Next(1, 51));
        var metrics = new long[large ? 128 : 8];
        for (int i = 0; i < metrics.Length; i++) metrics[i] = random.Next(0, 10001);
        var items = new BusinessItem[large ? 64 : 0];
        for (int i = 0; i < items.Length; i++) items[i] = new(random.Next(1, 11), random.Next(1, 1001),
            random.Next(0, 100), random.Next(0, 8), random.Next(0, 1000), random.Next(1, 100), random.Next(0, 8), random.Next(0, 20));
        var tags = new string[large ? 16 : 0];
        for (int i = 0; i < tags.Length; i++) tags[i] = random.NextInt64().ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        var metadata = new Dictionary<string, long>(large ? 32 : 0, StringComparer.Ordinal);
        if (large) foreach (string key in Keys) metadata.Add(key, random.Next(0, 100000));
        string payload = large ? new string((char)('a' + random.Next(26)), 4096) : "";
        return new(sequence, request, pricing, metrics, items, tags, metadata, payload);
    }
}

public static class BusinessRules
{
    public static readonly BusinessScenario[] Scenarios =
    [
        new("S1", "新建小 context 与简单规则", false, 1, 1),
        new("S2", "新建小 context 与 100 个依赖变量", false, 100, 0),
        new("S3", "新建小 context 与 100 变量和 32 条件", false, 100, 32),
        new("S4", "新建大 context 与简单规则", true, 1, 1),
        new("S5", "新建大 context 与 100 变量和 32 条件", true, 100, 32)
    ];

    public static string Clr(BusinessScenario scenario)
    {
        var code = new System.Text.StringBuilder();
        code.AppendLine("var v0 = ctx.Request.InputTokens * ctx.Pricing.InputRate + ctx.Request.OutputTokens * ctx.Pricing.OutputRate;");
        if (scenario.Locals == 1)
        {
            code.AppendLine("if (ctx.Request.Vip) { v0 = v0 * 9; } else { v0 = v0 * 10; }");
            code.AppendLine("return v0 + ctx.Request.CacheTokens * ctx.Pricing.CacheRate + ctx.Metrics[0];");
            return code.ToString();
        }
        for (int i = 1; i < 100; i++)
        {
            string item = scenario.Large ? $" + ctx.Items[{i % 64}].Quantity * ctx.Items[{i % 64}].UnitPrice" : " + ctx.Pricing.OutputRate";
            code.AppendLine($"var v{i} = v{i - 1} + ctx.Metrics[{i % (scenario.Large ? 128 : 8)}] * {i % 7 + 1} + ctx.Request.InputTokens % {i + 17}{item};");
        }
        if (scenario.Conditions > 0)
        {
            for (int group = 0; group < 8; group++)
            {
                code.AppendLine($$"""
                    if ((ctx.Request.Flags + {{group}}) % 4 == 0) {
                        v99 = v99 + ctx.Pricing.InputRate * {{group + 1}};
                        if (ctx.Request.Vip) { v99 = v99 + 17; } else { v99 = v99 - 11; }
                    } else if ((ctx.Request.Flags + {{group}}) % 4 == 1) {
                        v99 = v99 + ctx.Pricing.OutputRate * {{group + 2}};
                    } else if ((ctx.Request.Flags + {{group}}) % 4 == 2) {
                        v99 = v99 + ctx.Request.CacheTokens % {{group + 23}};
                    } else { v99 = v99 - ctx.Metrics[{{group}}]; }
                    """);
            }
        }
        code.AppendLine("return v99;");
        return code.ToString();
    }

    public static string Javascript(BusinessScenario scenario) => "function decide(ctx) {\n" + Clr(scenario).Replace("var ", "let ", StringComparison.Ordinal) + "\n}";

    /// <summary>独立循环参考实现用于逐项期望值，不用于声称原生 C# 性能上界。</summary>
    public static long Reference(BusinessScenario scenario, BusinessContext ctx)
    {
        long value = ctx.Request.InputTokens * ctx.Pricing.InputRate + ctx.Request.OutputTokens * ctx.Pricing.OutputRate;
        if (scenario.Locals == 1)
            return value * (ctx.Request.Vip ? 9 : 10) + ctx.Request.CacheTokens * ctx.Pricing.CacheRate + ctx.Metrics[0];
        for (int i = 1; i < 100; i++)
        {
            value += ctx.Metrics[i % (scenario.Large ? 128 : 8)] * (i % 7 + 1) + ctx.Request.InputTokens % (i + 17);
            value += scenario.Large ? ctx.Items[i % 64].Quantity * ctx.Items[i % 64].UnitPrice : ctx.Pricing.OutputRate;
        }
        if (scenario.Conditions > 0)
        {
            for (int group = 0; group < 8; group++)
            {
                switch ((ctx.Request.Flags + group) % 4)
                {
                    case 0: value += ctx.Pricing.InputRate * (group + 1) + (ctx.Request.Vip ? 17 : -11); break;
                    case 1: value += ctx.Pricing.OutputRate * (group + 2); break;
                    case 2: value += ctx.Request.CacheTokens % (group + 23); break;
                    default: value -= ctx.Metrics[group]; break;
                }
            }
        }
        return value;
    }
}
