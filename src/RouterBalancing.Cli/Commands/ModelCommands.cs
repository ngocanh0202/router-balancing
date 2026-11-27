using RouterBalancing.Core.Domain;
using RouterBalancing.Core.Providers;

namespace RouterBalancing.Cli.Commands;

/// <summary><c>rbctl model …</c> — model của provider.</summary>
internal static class ModelCommands
{
    public const string Help = """
        rbctl model <lệnh>

          list [<provider>] [--search <text>] [--enabled true|false]
                                            Không truyền provider = mọi provider
          fetch <provider>                  Gọi /models của provider, thêm model mới + điền metadata
          sync <provider> | sync --all      Đồng bộ danh sách model free (provider preset)
          add <provider> <modelId>...       Thêm model thủ công
          enable <model>... | disable <model>...
          enable-all <provider> | disable-all <provider>
          remove <model> [--yes]
          remove-all <provider> [--yes]

        <model> = id số hoặc <provider>/<modelId> (vd openrouter/meta-llama/llama-3.1-8b:free).
        """;

    public static async Task<int> RunAsync(CliContext ctx)
    {
        var sub = ctx.Args.Next();
        switch (sub)
        {
            case "list" or "ls":
                return await ListAsync(ctx);
            case "fetch":
                return await FetchAsync(ctx);
            case "sync":
                return await SyncAsync(ctx);
            case "add":
                return await AddAsync(ctx);
            case "enable":
                return await SetEnabledAsync(ctx, true);
            case "disable":
                return await SetEnabledAsync(ctx, false);
            case "enable-all":
                return await SetAllEnabledAsync(ctx, true);
            case "disable-all":
                return await SetAllEnabledAsync(ctx, false);
            case "remove" or "rm" or "delete":
                return await RemoveAsync(ctx);
            case "remove-all":
                return await RemoveAllAsync(ctx);
            case null or "help":
                ctx.Output.Out.WriteLine(Help);
                return sub is null ? CliExitCodes.Usage : CliExitCodes.Ok;
            default:
                throw new CliUsageException($"Lệnh model không hợp lệ: {sub}\n\n{Help}");
        }
    }

    private static async Task<int> ListAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions("search", "enabled");
        var reference = ctx.Args.Next();
        ctx.Args.EnsureNoMorePositionals();

        var providers = await ctx.Get<IProviderService>().ListAsync(ctx.Cancellation);
        if (reference is not null)
            providers = [RefResolver.Provider(providers, reference)];

        var search = ctx.Args.Option("search");
        var enabledFilter = ctx.Args.BoolOption("enabled");
        var rows = providers
            .SelectMany(p => p.Models.Select(m => (Provider: p, Model: m)))
            .Where(x => enabledFilter is null || x.Model.Enabled == enabledFilter)
            .Where(x => string.IsNullOrWhiteSpace(search)
                || x.Model.ModelId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (x.Model.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(x => x.Provider.Id)
            .ThenBy(x => x.Model.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(rows.Select(x => new
            {
                x.Model.Id,
                x.Model.ProviderId,
                provider = x.Provider.Name,
                key = ModelKey(x.Provider, x.Model),
                x.Model.ModelId,
                x.Model.DisplayName,
                x.Model.Enabled,
                x.Model.IsManual,
                x.Model.ContextWindow,
                x.Model.MaxOutputTokens,
            }));
            return CliExitCodes.Ok;
        }

        ctx.Output.Table(["ID", "KEY", "NAME", "ON", "MANUAL", "CONTEXT"],
            rows.Select(x => (IReadOnlyList<string?>)
            [
                Fmt.Id(x.Model.Id), ModelKey(x.Provider, x.Model), Fmt.Truncate(x.Model.DisplayName, 40),
                Fmt.YesNo(x.Model.Enabled), Fmt.YesNo(x.Model.IsManual), Fmt.Number(x.Model.ContextWindow),
            ]));
        ctx.Output.Line($"{rows.Count} model.");
        return CliExitCodes.Ok;
    }

    private static async Task<int> FetchAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();
        ctx.Output.Line($"Đang lấy danh sách model từ {provider.BaseUrl} …");
        var (added, skipped, filled) = await ctx.Get<IModelService>().FetchFromProviderAsync(provider.Id, ctx.Cancellation);
        ctx.Output.Done($"{provider.Name}: thêm {added}, bỏ qua {skipped} (đã có), điền metadata {filled}.",
            new { added, skipped, filled });
        return CliExitCodes.Ok;
    }

    private static async Task<int> SyncAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions("all");
        var sync = ctx.Get<IFreeModelSyncService>();
        if (ctx.Args.Flag("all"))
        {
            ctx.Args.EnsureNoMorePositionals();
            var results = await sync.SyncAllEnabledAsync(ctx.Cancellation);
            if (ctx.Output.Json)
            {
                ctx.Output.WriteJson(results.Select(r => new { r.ProviderId, r.ModelCount }));
                return CliExitCodes.Ok;
            }
            ctx.Output.Table(["PROVIDER ID", "MODELS"],
                results.Select(r => (IReadOnlyList<string?>)[Fmt.Id(r.ProviderId), Fmt.Id(r.ModelCount)]));
            return CliExitCodes.Ok;
        }

        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();
        var count = await sync.SyncProviderAsync(provider.Id, ctx.Cancellation);
        ctx.Output.Done($"{provider.Name}: đồng bộ xong, {count} model free.", new { providerId = provider.Id, count });
        return CliExitCodes.Ok;
    }

    private static async Task<int> AddAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        var ids = ctx.Args.Remaining;
        if (ids.Count == 0) throw new CliUsageException("Thiếu <modelId>.");
        var (added, skipped) = await ctx.Get<IModelService>().AddBulkAsync(provider.Id, ids, ctx.Cancellation);
        ctx.Output.Done($"{provider.Name}: thêm {added} model, bỏ qua {skipped} (đã tồn tại).", new { added, skipped });
        return CliExitCodes.Ok;
    }

    private static async Task<int> SetEnabledAsync(CliContext ctx, bool enabled)
    {
        ctx.Args.EnsureOnlyOptions();
        var refs = ctx.Args.Remaining;
        if (refs.Count == 0) throw new CliUsageException("Thiếu <model>.");

        // Resolve hết trước khi ghi: một tham chiếu sai không để lại trạng thái nửa vời
        var providers = await ctx.Get<IProviderService>().ListAsync(ctx.Cancellation);
        var models = refs.Select(r => RefResolver.Model(providers, r)).ToList();
        var service = ctx.Get<IModelService>();
        foreach (var model in models)
            await service.SetEnabledAsync(model.Id, enabled, ctx.Cancellation);

        ctx.Output.Done($"Đã {(enabled ? "bật" : "tắt")} {models.Count} model.",
            models.Select(m => m.Id).ToArray());
        return CliExitCodes.Ok;
    }

    private static async Task<int> SetAllEnabledAsync(CliContext ctx, bool enabled)
    {
        ctx.Args.EnsureOnlyOptions();
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();
        await ctx.Get<IModelService>().SetAllEnabledAsync(provider.Id, enabled, ctx.Cancellation);
        ctx.Output.Done($"Đã {(enabled ? "bật" : "tắt")} toàn bộ {provider.Models.Count} model của '{provider.Name}'.");
        return CliExitCodes.Ok;
    }

    private static async Task<int> RemoveAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var model = await ctx.ResolveModelAsync(ctx.Args.Require("model"));
        ctx.Args.EnsureNoMorePositionals();
        if (!ctx.Confirm($"Xoá model #{model.Id} '{model.ModelId}'? Combo đang dùng model này sẽ mất thành viên.", Console.In))
        {
            ctx.Output.Line("Đã huỷ.");
            return CliExitCodes.Cancelled;
        }
        await ctx.Get<IModelService>().RemoveAsync(model.Id, ctx.Cancellation);
        ctx.Output.Done($"Đã xoá model #{model.Id} '{model.ModelId}'.");
        return CliExitCodes.Ok;
    }

    private static async Task<int> RemoveAllAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var provider = await ctx.ResolveProviderAsync(ctx.Args.Require("provider"));
        ctx.Args.EnsureNoMorePositionals();
        if (!ctx.Confirm($"Xoá toàn bộ {provider.Models.Count} model của '{provider.Name}'?", Console.In))
        {
            ctx.Output.Line("Đã huỷ.");
            return CliExitCodes.Cancelled;
        }
        var removed = await ctx.Get<IModelService>().RemoveAllAsync(provider.Id, ctx.Cancellation);
        ctx.Output.Done($"Đã xoá {removed} model của '{provider.Name}'.", new { removed });
        return CliExitCodes.Ok;
    }

    /// <summary>
    /// Key client gửi lên proxy: <c>identifier/modelId</c>, bare khi provider chưa có identifier
    /// — cùng quy tắc model key của config opencode (C-A).
    /// </summary>
    internal static string ModelKey(Provider provider, Model model) =>
        string.IsNullOrEmpty(provider.Identifier) ? model.ModelId : $"{provider.Identifier}/{model.ModelId}";
}
