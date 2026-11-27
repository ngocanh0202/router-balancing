using System.Text;
using System.Globalization;
using RouterBalancing.Core.Domain;
using RouterBalancing.Core.Providers;

namespace RouterBalancing.Cli.Commands;

/// <summary><c>rbctl provider …</c> — CRUD provider, bật/tắt, test kết nối.</summary>
internal static class ProviderCommands
{
    public const string Help = """
        rbctl provider <lệnh>

          list                              Danh sách provider
          show <provider>                   Chi tiết provider (header, account, số model)
          add --name <tên> --base-url <url> (--api-key <key> | --no-key)
              [--identifier <slug>] [--type openai|anthropic]
              [--max-concurrent <n>] [--header Key=Value]...
          update <provider> [--name] [--identifier] [--base-url] [--type]
              [--max-concurrent] [--header Key=Value]... [--clear-headers]
              (--header Key= để xoá một header)
          enable <provider> | disable <provider>
          delete <provider> [--yes]         Provider preset không xoá được
          test <provider>                   Gọi thử endpoint /models bằng account bật đầu tiên

        <provider> = id, identifier (vd openrouter) hoặc tên.
        """;

    public static async Task<int> RunAsync(CliContext ctx)
    {
        var sub = ctx.Args.Next();
        switch (sub)
        {
            case "list" or "ls":
                return await ListAsync(ctx);
            case "show" or "get":
                return await ShowAsync(ctx);
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
                throw new CliUsageException($"Lệnh provider không hợp lệ: {sub}\n\n{Help}");
        }
    }

    private static async Task<int> ListAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        ctx.Args.EnsureNoMorePositionals();
        var providers = await ctx.Get<IProviderService>().ListAsync(ctx.Cancellation);
        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(providers.Select(ToSummary));
            return CliExitCodes.Ok;
        }

        ctx.Output.Table(
            ["ID", "IDENTIFIER", "NAME", "TYPE", "ON", "PRESET", "MODELS", "ACCOUNTS", "TEST", "BASE URL"],
            providers.Select(p => (IReadOnlyList<string?>)
            [
                Fmt.Id(p.Id), p.Identifier, p.Name, p.Type.ToString(), Fmt.YesNo(p.Enabled), Fmt.YesNo(p.IsPreset),
                $"{p.Models.Count(m => m.Enabled)}/{p.Models.Count}",
                $"{p.Accounts.Count(a => a.Enabled)}/{p.Accounts.Count}",
                Fmt.TestStatus(p.LastTestSuccess), p.BaseUrl,
            ]));
        return CliExitCodes.Ok;
    }

    private static async Task<int> ShowAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();
        // Qua account service để có usage hôm nay (provider.Accounts không nạp ledger)
        var accounts = await ctx.Get<IProviderAccountService>().ListAsync(provider.Id, ctx.Cancellation);

        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(new
            {
                summary = ToSummary(provider),
                headers = provider.Headers.Select(h => new { h.Name, h.Value }),
                accounts = accounts.Select(AccountCommands.ToSummary),
                lastTest = new { provider.LastTestSuccess, provider.LastTestAt, provider.LastTestMessage },
                provider.LastModelSyncAt,
            });
            return CliExitCodes.Ok;
        }

        ctx.Output.Pairs(
        [
            ("Id", Fmt.Id(provider.Id)),
            ("Identifier", provider.Identifier),
            ("Name", provider.Name),
            ("Type", provider.Type.ToString()),
            ("Base URL", provider.BaseUrl),
            ("Enabled", Fmt.YesNo(provider.Enabled)),
            ("Preset", Fmt.YesNo(provider.IsPreset)),
            ("Max concurrent", Fmt.Id(provider.MaxConcurrent)),
            ("Models", $"{provider.Models.Count(m => m.Enabled)} bật / {provider.Models.Count}"),
            ("Last test", provider.LastTestSuccess is null
                ? null
                : $"{Fmt.TestStatus(provider.LastTestSuccess)} @ {Fmt.Time(provider.LastTestAt)} {provider.LastTestMessage}"),
            ("Last model sync", Fmt.Time(provider.LastModelSyncAt)),
        ]);

        if (provider.Headers.Count > 0)
        {
            ctx.Output.Line();
            ctx.Output.Line("Headers:");
            ctx.Output.Table(["NAME", "VALUE"],
                provider.Headers.Select(h => (IReadOnlyList<string?>)[h.Name, h.Value]));
        }

        ctx.Output.Line();
        ctx.Output.Line("Accounts:");
        AccountCommands.PrintTable(ctx, accounts);
        return CliExitCodes.Ok;
    }

    private static async Task<int> AddAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions("name", "base-url", "api-key", "no-key", "identifier", "type",
            "max-concurrent", "header");
        ctx.Args.EnsureNoMorePositionals();

        var name = ctx.Args.Option("name") ?? throw new CliUsageException("Thiếu --name.");
        var noKey = ctx.Args.Flag("no-key");
        var apiKey = ctx.Args.Option("api-key") ?? string.Empty;
        if (!noKey && string.IsNullOrWhiteSpace(apiKey))
            throw new CliUsageException("Cần --api-key <key> hoặc --no-key (endpoint miễn phí).");
        if (noKey && !string.IsNullOrWhiteSpace(apiKey))
            throw new CliUsageException("--api-key và --no-key loại trừ nhau.");

        var draft = new ProviderDraft
        {
            Name = name.Trim(),
            Identifier = (ctx.Args.Option("identifier") ?? Slugify(name)).Trim(),
            Type = ParseType(ctx.Args.Option("type")) ?? ProviderType.OpenAI,
            BaseUrl = ctx.Args.Option("base-url") ?? throw new CliUsageException("Thiếu --base-url."),
            ApiKey = apiKey,
            NoKey = noKey,
            MaxConcurrent = ctx.Args.IntOption("max-concurrent") ?? 4,
            ExtraHeaders = ParseHeaders(ctx.Args.OptionValues("header")),
        };

        ThrowIfInvalid(draft);
        var created = await ctx.Get<IProviderService>().CreateAsync(draft, ctx.Cancellation);
        ctx.Output.Done(
            $"Đã tạo provider #{created.Id} '{created.Name}' (identifier: {created.Identifier}) kèm account 'Default'. " +
            "Chạy `rbctl model fetch " + created.Identifier + "` để nạp danh sách model.",
            ToSummary(created));
        return CliExitCodes.Ok;
    }

    private static async Task<int> UpdateAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions("name", "base-url", "identifier", "type", "max-concurrent", "header", "clear-headers");
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();

        // Draft khởi từ trạng thái hiện tại: UpdateAsync ghi đè MỌI field (kể cả header),
        // nên field không truyền phải mang giá trị cũ, không được để mặc định.
        var headers = provider.Headers
            .Select(h => new ExtraHeaderEntry { Key = h.Name, Value = h.Value })
            .ToList();
        if (ctx.Args.Flag("clear-headers")) headers.Clear();
        foreach (var entry in ParseHeaders(ctx.Args.OptionValues("header")))
        {
            headers.RemoveAll(h => string.Equals(h.Key, entry.Key, StringComparison.OrdinalIgnoreCase));
            // "Key=" (value rỗng) = xoá header đó
            if (entry.Value.Length > 0) headers.Add(entry);
        }

        var draft = new ProviderDraft
        {
            Name = ctx.Args.Option("name") ?? provider.Name,
            Identifier = ctx.Args.Option("identifier") ?? provider.Identifier ?? string.Empty,
            Type = ParseType(ctx.Args.Option("type")) ?? provider.Type,
            BaseUrl = ctx.Args.Option("base-url") ?? provider.BaseUrl,
            MaxConcurrent = ctx.Args.IntOption("max-concurrent") ?? provider.MaxConcurrent,
            ExtraHeaders = headers,
        };

        ThrowIfInvalid(draft);
        await ctx.Get<IProviderService>().UpdateAsync(provider.Id, draft, ctx.Cancellation);
        ctx.Output.Done($"Đã cập nhật provider #{provider.Id} '{draft.Name}'.");
        return CliExitCodes.Ok;
    }

    private static async Task<int> SetEnabledAsync(CliContext ctx, bool enabled)
    {
        ctx.Args.EnsureOnlyOptions();
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();
        await ctx.Get<IProviderService>().SetEnabledAsync(provider.Id, enabled, ctx.Cancellation);
        ctx.Output.Done($"Provider #{provider.Id} '{provider.Name}' đã {(enabled ? "bật" : "tắt")}.");
        return CliExitCodes.Ok;
    }

    private static async Task<int> DeleteAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();
        // Chặn trước khi hỏi xác nhận — service cũng chặn, nhưng hỏi rồi mới báo lỗi thì vô lý
        if (provider.IsPreset)
            throw new InvalidOperationException($"Provider preset '{provider.Name}' không xoá được — dùng `rbctl provider disable {provider.Identifier}`.");
        if (!ctx.Confirm($"Xoá provider #{provider.Id} '{provider.Name}' cùng {provider.Models.Count} model và {provider.Accounts.Count} account?", Console.In))
        {
            ctx.Output.Line("Đã huỷ.");
            return CliExitCodes.Cancelled;
        }
        await ctx.Get<IProviderService>().DeleteAsync(provider.Id, ctx.Cancellation);
        ctx.Output.Done($"Đã xoá provider #{provider.Id} '{provider.Name}'.");
        return CliExitCodes.Ok;
    }

    private static async Task<int> TestAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();
        var result = await ctx.Get<IProviderService>().TestConnectionAsync(provider, null, ctx.Cancellation);
        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(new { providerId = provider.Id, result.Success, result.Message, result.At });
        }
        else
        {
            ctx.Output.Out.WriteLine(result.Success
                ? $"OK — {provider.Name} phản hồi bình thường."
                : $"FAIL — {provider.Name}: {result.Message}");
        }
        return result.Success ? CliExitCodes.Ok : CliExitCodes.Error;
    }

    internal static object ToSummary(Provider p) => new
    {
        p.Id,
        p.Identifier,
        p.Name,
        p.Type,
        p.BaseUrl,
        p.Enabled,
        p.IsPreset,
        p.MaxConcurrent,
        models = p.Models.Count,
        enabledModels = p.Models.Count(m => m.Enabled),
        accounts = p.Accounts.Count,
        p.LastTestSuccess,
        p.LastTestAt,
    };

    internal static ProviderType? ParseType(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "openai" => ProviderType.OpenAI,
        "anthropic" => ProviderType.Anthropic,
        _ => throw new CliUsageException($"--type phải là openai hoặc anthropic (nhận '{raw}')."),
    };

    /// <summary>Header dạng <c>Key=Value</c>; tách ở '=' đầu tiên vì value có thể chứa '='.</summary>
    internal static List<ExtraHeaderEntry> ParseHeaders(IReadOnlyList<string> raw)
    {
        var result = new List<ExtraHeaderEntry>(raw.Count);
        foreach (var item in raw)
        {
            var eq = item.IndexOf('=');
            if (eq <= 0)
                throw new CliUsageException($"--header phải có dạng Key=Value (nhận '{item}').");
            result.Add(new ExtraHeaderEntry { Key = item[..eq].Trim(), Value = item[(eq + 1)..] });
        }
        return result;
    }

    /// <summary>
    /// Slug identifier từ tên — cùng quy tắc <c>DbInitializer.Slugify</c> (bỏ dấu, a-z0-9 và '-').
    /// Viết lại vì bản gốc là internal của Core.
    /// </summary>
    internal static string Slugify(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            // 'đ' không tách dấu được qua FormD — map tay để tên tiếng Việt ra slug hợp lý
            var lower = ch is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(ch);
            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                sb.Append(lower);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length <= 50 ? slug : slug[..50].TrimEnd('-');
    }

    private static void ThrowIfInvalid(ProviderDraft draft)
    {
        var errors = ProviderValidator.Validate(draft);
        if (errors.Count > 0) throw new ProviderValidationException(errors);
    }
}
