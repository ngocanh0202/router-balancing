using System.Globalization;
using RouterBalancing.Core.Domain;
using RouterBalancing.Core.Logging;

namespace RouterBalancing.Cli.Commands;

/// <summary><c>rbctl logs</c> — đọc log app/request từ DB (mới nhất trước), có chế độ follow.</summary>
internal static class LogsCommand
{
    public const string Help = """
        rbctl logs [tuỳ chọn]

          --level debug|info|warn|error     Mức tối thiểu (mặc định: tất cả)
          --category app|request
          --search <text>                   Lọc theo nội dung message
          --since <mốc>                     30m, 2h, 7d hoặc thời điểm ISO (2026-11-27T08:00)
          --until <mốc>
          --limit <n>                       Số dòng (mặc định 50, tối đa 1000)
          --page <n>                        Trang (theo --limit)
          --follow                          Theo dõi log mới (Ctrl+C để dừng)
        """;

    private static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(2);

    public static async Task<int> RunAsync(CliContext ctx)
    {
        if (ctx.Args.Next() is { } extra)
        {
            if (extra == "help")
            {
                ctx.Output.Out.WriteLine(Help);
                return CliExitCodes.Ok;
            }
            throw new CliUsageException($"Tham số thừa: {extra}\n\n{Help}");
        }
        ctx.Args.EnsureOnlyOptions("level", "category", "search", "since", "until", "limit", "page", "follow");

        var now = DateTimeOffset.Now;
        var limit = ctx.Args.IntOption("limit") ?? 50;
        if (limit is < 1 or > 1000) throw new CliUsageException("--limit phải trong khoảng 1–1000.");
        var page = ctx.Args.IntOption("page") ?? 1;
        if (page < 1) throw new CliUsageException("--page phải ≥ 1.");

        var query = new LogQuery(
            MinSeverity: ParseLevel(ctx.Args.Option("level")),
            Category: ParseCategory(ctx.Args.Option("category")),
            Search: ctx.Args.Option("search"),
            From: ParseMoment(ctx.Args.Option("since"), now, "since"),
            To: ParseMoment(ctx.Args.Option("until"), now, "until"),
            Page: page,
            PageSize: limit);

        var log = ctx.Get<ILogService>();
        // Query trả mới nhất trước; in theo thứ tự thời gian tăng dần như tail
        var entries = log.Query(query).Reverse().ToList();

        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(entries.Select(ToJson));
        }
        else
        {
            foreach (var entry in entries) ctx.Output.Out.WriteLine(FormatLine(entry));
            if (entries.Count == 0) ctx.Output.Line("(không có log khớp bộ lọc)");
        }

        if (!ctx.Args.Flag("follow")) return CliExitCodes.Ok;

        var lastId = entries.Count == 0 ? 0 : entries.Max(e => e.Id);
        var since = entries.Count == 0 ? now : entries.Max(e => e.Timestamp);
        while (!ctx.Cancellation.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FollowInterval, ctx.Cancellation);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Lọc theo Id > lastId: nhiều dòng cùng timestamp không bị in lặp/bị sót
            var fresh = log.Query(query with { From = since, To = null, Page = 1, PageSize = 1000 })
                .Where(e => e.Id > lastId)
                .OrderBy(e => e.Id)
                .ToList();
            foreach (var entry in fresh)
            {
                if (ctx.Output.Json)
                    ctx.Output.WriteJson(ToJson(entry));
                else
                    ctx.Output.Out.WriteLine(FormatLine(entry));
                lastId = entry.Id;
                if (entry.Timestamp > since) since = entry.Timestamp;
            }
        }
        return CliExitCodes.Ok;
    }

    internal static string FormatLine(LogEntry e)
    {
        var level = e.Severity switch
        {
            LogSeverity.Debug => "DBG",
            LogSeverity.Info => "INF",
            LogSeverity.Warning => "WRN",
            LogSeverity.Error => "ERR",
            _ => e.Severity.ToString(),
        };
        var parts = new List<string> { $"{Fmt.Time(e.Timestamp)} {level} [{e.Category.ToString().ToLowerInvariant()}]" };
        if (e.RequestId is not null) parts.Add($"req={e.RequestId}");
        if (e.ModelKey is not null) parts.Add($"model={e.ModelKey}");
        if (e.HttpStatus is not null) parts.Add($"http={e.HttpStatus}");
        if (e.DurationMs is not null) parts.Add($"{e.DurationMs}ms");
        parts.Add(e.Message.Replace("\r", " ").Replace("\n", " "));
        return string.Join(' ', parts);
    }

    private static object ToJson(LogEntry e) => new
    {
        e.Id,
        e.Timestamp,
        e.Severity,
        e.Category,
        e.Message,
        e.RequestId,
        e.ProviderKey,
        e.ModelKey,
        e.HttpStatus,
        e.DurationMs,
        e.PromptTokens,
        e.CompletionTokens,
        e.Outcome,
        e.ErrorCode,
        e.Details,
    };

    internal static LogSeverity? ParseLevel(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "debug" or "dbg" => LogSeverity.Debug,
        "info" or "inf" => LogSeverity.Info,
        "warn" or "warning" or "wrn" => LogSeverity.Warning,
        "error" or "err" => LogSeverity.Error,
        _ => throw new CliUsageException($"--level không hợp lệ: '{raw}' (debug|info|warn|error)."),
    };

    internal static LogCategory? ParseCategory(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "app" => LogCategory.App,
        "request" or "req" => LogCategory.Request,
        _ => throw new CliUsageException($"--category không hợp lệ: '{raw}' (app|request)."),
    };

    /// <summary>
    /// Mốc thời gian: khoảng tương đối (<c>30m</c>, <c>2h</c>, <c>7d</c>, <c>45s</c>) tính lùi từ
    /// <paramref name="now"/>, hoặc thời điểm tuyệt đối (giờ máy nếu không ghi offset).
    /// </summary>
    internal static DateTimeOffset? ParseMoment(string? raw, DateTimeOffset now, string optionName)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();
        var unit = value[^1];
        if (value.Length > 1 && char.IsAsciiLetter(unit)
            && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            && amount >= 0)
        {
            TimeSpan? span = char.ToLowerInvariant(unit) switch
            {
                's' => TimeSpan.FromSeconds(amount),
                'm' => TimeSpan.FromMinutes(amount),
                'h' => TimeSpan.FromHours(amount),
                'd' => TimeSpan.FromDays(amount),
                'w' => TimeSpan.FromDays(amount * 7),
                _ => null,
            };
            if (span is { } s) return now - s;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at))
            return at;

        throw new CliUsageException($"--{optionName} không hợp lệ: '{raw}' (vd 30m, 2h, 7d, 2026-11-27T08:00).");
    }
}
