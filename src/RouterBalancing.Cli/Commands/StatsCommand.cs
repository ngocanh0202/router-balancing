using System.Globalization;
using RouterBalancing.Core.Stats;

namespace RouterBalancing.Cli.Commands;

/// <summary><c>rbctl stats</c> — số liệu request/token giống panel Dashboard.</summary>
internal static class StatsCommand
{
    public const string Help = """
        rbctl stats [today|7d|30d] [--top <n>]

          Tổng request, lỗi, latency, token và top provider/model/client key trong khoảng.
          Mặc định: today (theo giờ máy). --top: số dòng mỗi bảng breakdown (mặc định 10).
        """;

    public static async Task<int> RunAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions("top");
        var rangeArg = ctx.Args.Next();
        if (rangeArg == "help")
        {
            ctx.Output.Out.WriteLine(Help);
            return CliExitCodes.Ok;
        }
        ctx.Args.EnsureNoMorePositionals();
        var range = ParseRange(rangeArg);
        var top = ctx.Args.IntOption("top") ?? 10;
        if (top < 1) throw new CliUsageException("--top phải ≥ 1.");

        var snapshot = await ctx.Get<IStatsService>().GetRangeAsync(range, ctx.Cancellation);
        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(snapshot);
            return CliExitCodes.Ok;
        }

        ctx.Output.Pairs(
        [
            ("Khoảng", $"{Fmt.Time(snapshot.From)} → {Fmt.Time(snapshot.To)}"),
            ("Requests", Fmt.Number(snapshot.Requests)),
            ("OK / Lỗi / Huỷ", $"{Fmt.Number(snapshot.Ok)} / {Fmt.Number(snapshot.Errors)} / {Fmt.Number(snapshot.Cancelled)}"),
            ("Tỉ lệ lỗi", snapshot.ErrorRatePercent.ToString("0.#", CultureInfo.InvariantCulture) + "%"),
            ("Latency TB / p95", $"{FormatMs(snapshot.AvgLatencyMs)} / {FormatMs(snapshot.P95LatencyMs)}"),
            ("Token vào / ra", $"{Fmt.Number(snapshot.PromptTokens)} / {Fmt.Number(snapshot.CompletionTokens)}"),
        ]);

        PrintBreakdown(ctx, "Theo provider", snapshot.ByProvider, top);
        PrintBreakdown(ctx, "Theo model", snapshot.ByModel, top);
        PrintBreakdown(ctx, "Theo client key", snapshot.ByClientKey, top);
        return CliExitCodes.Ok;
    }

    internal static StatsRange ParseRange(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        null or "today" or "1d" => StatsRange.Today,
        "7d" or "week" or "last7days" => StatsRange.Last7Days,
        "30d" or "month" or "last30days" => StatsRange.Last30Days,
        _ => throw new CliUsageException($"Khoảng thời gian không hợp lệ: '{raw}' (today|7d|30d)."),
    };

    private static void PrintBreakdown(CliContext ctx, string title, IReadOnlyList<StatsBreakdownRow> rows, int top)
    {
        if (rows.Count == 0) return;
        ctx.Output.Out.WriteLine();
        ctx.Output.Out.WriteLine($"{title}:");
        ctx.Output.Table(["KEY", "REQUESTS", "ERRORS", "TOKENS"],
            rows.OrderByDescending(r => r.Requests).Take(top).Select(r => (IReadOnlyList<string?>)
                [r.Key, Fmt.Number(r.Requests), Fmt.Number(r.Errors), Fmt.Number(r.Tokens)]));
    }

    private static string FormatMs(int? ms) => ms is null ? "-" : $"{Fmt.Number(ms)} ms";
}
