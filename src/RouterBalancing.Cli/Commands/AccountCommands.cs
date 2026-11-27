using System.Text.Json;
using RouterBalancing.Core.Domain;
using RouterBalancing.Core.Providers;

namespace RouterBalancing.Cli.Commands;

/// <summary><c>rbctl account …</c> — tài khoản (API key) của provider.</summary>
internal static class AccountCommands
{
    public const string Help = """
        rbctl account <lệnh>

          list <provider>                   Account của provider + usage hôm nay (UTC)
          add <provider> --name <tên> (--api-key <key> | --no-key)
              [--weight <0-10000>] [--patterns a*,b*]
              [--daily-tokens <n>] [--daily-requests <n>] [--enabled false]
          update <account> [--name] [--api-key <key mới>] [--no-key] [--weight]
              [--patterns a,b | --patterns none] [--daily-tokens <n|none>]
              [--daily-requests <n|none>]
          enable <account> | disable <account>
          delete <account> [--yes]          Không xoá được account cuối cùng của provider
          test <provider>                   Test lần lượt mọi account đang bật

        <account> = id hoặc <provider>/<tên account>.
        API key được mã hoá DPAPI (CurrentUser) giống app — chỉ đọc được trên cùng user Windows.
        """;

    public static async Task<int> RunAsync(CliContext ctx)
    {
        var sub = ctx.Args.Next();
        switch (sub)
        {
            case "list" or "ls":
                return await ListAsync(ctx);
            case "add" or "create":
                return await AddAsync(ctx);
            case "update" or "edit":
                return await UpdateAsync(ctx);
            case "enable":
                return await SetEnabledAsync(ctx, true);
            case "disable":
                return await SetEnabledAsync(ctx, false);
            case "delete" or "rm" or "remove":
                return await DeleteAsync(ctx);
            case "test":
                return await TestAsync(ctx);
            case null or "help":
                ctx.Output.Out.WriteLine(Help);
                return sub is null ? CliExitCodes.Usage : CliExitCodes.Ok;
            default:
                throw new CliUsageException($"Lệnh account không hợp lệ: {sub}\n\n{Help}");
        }
    }

    private static async Task<int> ListAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();
        // ListAsync của service (không phải provider.Accounts) — nạp kèm usage hôm nay từ ledger
        var accounts = await ctx.Get<IProviderAccountService>().ListAsync(provider.Id, ctx.Cancellation);
        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(accounts.Select(ToSummary));
            return CliExitCodes.Ok;
        }
        PrintTable(ctx, accounts);
        return CliExitCodes.Ok;
    }

    internal static void PrintTable(CliContext ctx, IEnumerable<ProviderAccount> accounts) =>
        ctx.Output.Table(
            ["ID", "NAME", "ON", "KEY", "WEIGHT", "PATTERNS", "REQ TODAY", "TOK TODAY", "LIMIT REQ/TOK", "TEST"],
            accounts.Select(a => (IReadOnlyList<string?>)
            [
                Fmt.Id(a.Id), a.Name, Fmt.YesNo(a.Enabled), HasKey(a) ? "yes" : "no-key", Fmt.Id(a.Weight),
                string.Join(",", ReadPatterns(a.ModelPatterns)),
                Fmt.Number(a.RequestsUsed), Fmt.Number(a.TokensUsed),
                $"{Fmt.Number(a.DailyRequestLimit) ?? "∞"}/{Fmt.Number(a.DailyTokenLimit) ?? "∞"}",
                Fmt.TestStatus(a.LastTestSuccess),
            ]));

    private static async Task<int> AddAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions("name", "api-key", "no-key", "weight", "patterns", "daily-tokens",
            "daily-requests", "enabled");
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();

        var noKey = ctx.Args.Flag("no-key");
        var apiKey = ctx.Args.Option("api-key") ?? string.Empty;
        if (noKey && !string.IsNullOrWhiteSpace(apiKey))
            throw new CliUsageException("--api-key và --no-key loại trừ nhau.");
        if (!noKey && string.IsNullOrWhiteSpace(apiKey))
            throw new CliUsageException("Cần --api-key <key> hoặc --no-key.");

        var draft = new ProviderAccountDraft
        {
            ProviderId = provider.Id,
            Name = ctx.Args.Option("name") ?? throw new CliUsageException("Thiếu --name."),
            ApiKey = apiKey,
            NoKey = noKey,
            Enabled = ctx.Args.BoolOption("enabled") ?? true,
            ModelPatterns = ParsePatternsOption(ctx) ?? [],
            Weight = ctx.Args.IntOption("weight") ?? 100,
            DailyTokenLimit = ctx.Args.NullableIntOption("daily-tokens").Value,
            DailyRequestLimit = ctx.Args.NullableIntOption("daily-requests").Value,
        };

        var created = await ctx.Get<IProviderAccountService>().CreateAsync(draft, ctx.Cancellation);
        ctx.Output.Done($"Đã thêm account #{created.Id} '{created.Name}' cho provider '{provider.Name}'.", ToSummary(created));
        return CliExitCodes.Ok;
    }

    private static async Task<int> UpdateAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions("name", "api-key", "no-key", "weight", "patterns", "daily-tokens",
            "daily-requests", "enabled");
        var (provider, account) = await ctx.ResolveAccountAsync(ctx.Args.Require("account"));
        ctx.Args.EnsureNoMorePositionals();

        var noKey = ctx.Args.Flag("no-key");
        var apiKey = ctx.Args.Option("api-key") ?? string.Empty;
        if (noKey && !string.IsNullOrWhiteSpace(apiKey))
            throw new CliUsageException("--api-key và --no-key loại trừ nhau.");

        var dailyTokens = ctx.Args.NullableIntOption("daily-tokens");
        var dailyRequests = ctx.Args.NullableIntOption("daily-requests");

        // Service ghi đè mọi field → field không truyền phải giữ giá trị hiện tại.
        // ApiKey rỗng = giữ key cũ (quy ước của ProviderAccountService.UpdateAsync).
        var draft = new ProviderAccountDraft
        {
            ProviderId = provider.Id,
            Name = ctx.Args.Option("name") ?? account.Name,
            ApiKey = apiKey,
            NoKey = noKey,
            Enabled = ctx.Args.BoolOption("enabled") ?? account.Enabled,
            ModelPatterns = ParsePatternsOption(ctx) ?? ReadPatterns(account.ModelPatterns),
            Weight = ctx.Args.IntOption("weight") ?? account.Weight,
            DailyTokenLimit = dailyTokens.Present ? dailyTokens.Value : account.DailyTokenLimit,
            DailyRequestLimit = dailyRequests.Present ? dailyRequests.Value : account.DailyRequestLimit,
        };

        await ctx.Get<IProviderAccountService>().UpdateAsync(account.Id, draft, ctx.Cancellation);
        ctx.Output.Done($"Đã cập nhật account #{account.Id} '{draft.Name}' ({provider.Name}).");
        return CliExitCodes.Ok;
    }

    private static async Task<int> SetEnabledAsync(CliContext ctx, bool enabled)
    {
        ctx.Args.EnsureOnlyOptions();
        var (provider, account) = await ctx.ResolveAccountAsync(ctx.Args.Require("account"));
        ctx.Args.EnsureNoMorePositionals();
        await ctx.Get<IProviderAccountService>().SetEnabledAsync(account.Id, enabled, ctx.Cancellation);
        ctx.Output.Done($"Account #{account.Id} '{account.Name}' ({provider.Name}) đã {(enabled ? "bật" : "tắt")}.");
        return CliExitCodes.Ok;
    }

    private static async Task<int> DeleteAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var (provider, account) = await ctx.ResolveAccountAsync(ctx.Args.Require("account"));
        ctx.Args.EnsureNoMorePositionals();
        if (!ctx.Confirm($"Xoá account #{account.Id} '{account.Name}' của '{provider.Name}'?", Console.In))
        {
            ctx.Output.Line("Đã huỷ.");
            return CliExitCodes.Cancelled;
        }
        await ctx.Get<IProviderAccountService>().DeleteAsync(account.Id, ctx.Cancellation);
        ctx.Output.Done($"Đã xoá account #{account.Id} '{account.Name}'.");
        return CliExitCodes.Ok;
    }

    private static async Task<int> TestAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();
        var results = await ctx.Get<IProviderAccountService>().TestAllAsync(provider.Id, ctx.Cancellation);
        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(results);
        }
        else if (results.Count == 0)
        {
            ctx.Output.Out.WriteLine($"Provider '{provider.Name}' không có account nào đang bật.");
        }
        else
        {
            ctx.Output.Table(["ID", "ACCOUNT", "RESULT", "MESSAGE"],
                results.Select(r => (IReadOnlyList<string?>)
                    [Fmt.Id(r.AccountId), r.AccountName, r.Success ? "ok" : "fail", r.Message]));
        }
        return results.Count > 0 && results.All(r => r.Success) ? CliExitCodes.Ok : CliExitCodes.Error;
    }

    internal static object ToSummary(ProviderAccount a) => new
    {
        a.Id,
        a.ProviderId,
        a.Name,
        a.Enabled,
        hasKey = HasKey(a),
        a.Weight,
        modelPatterns = ReadPatterns(a.ModelPatterns),
        a.DailyRequestLimit,
        a.DailyTokenLimit,
        requestsToday = a.RequestsUsed,
        tokensToday = a.TokensUsed,
        a.LastTestSuccess,
        a.LastTestAt,
        a.LastTestMessage,
    };

    // Cột rỗng = account no-key (spec free-account D1) — không bao giờ in/giải mã key ra CLI
    private static bool HasKey(ProviderAccount a) => !string.IsNullOrEmpty(a.ApiKeyEncrypted);

    /// <summary><c>--patterns none</c> = xoá hết; không truyền = <see langword="null"/> (giữ nguyên).</summary>
    private static string[]? ParsePatternsOption(CliContext ctx)
    {
        if (!ctx.Args.Has("patterns")) return null;
        var list = ctx.Args.OptionList("patterns");
        return list.Count == 1 && list[0] == "none" ? [] : list.ToArray();
    }

    /// <summary>Cột ModelPatterns lưu JSON array (xem ProviderAccountService.SerializePatterns).</summary>
    internal static string[] ReadPatterns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            // Dữ liệu cũ không phải JSON — hiển thị thô thay vì làm hỏng cả lệnh list
            return [json];
        }
    }
}
