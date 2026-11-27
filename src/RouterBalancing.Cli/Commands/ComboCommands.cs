using RouterBalancing.Core.Combos;
using RouterBalancing.Core.Domain;
using RouterBalancing.Core.Providers;

namespace RouterBalancing.Cli.Commands;

/// <summary><c>rbctl combo …</c> — nhóm model (round-robin / fallback), có thể lồng combo.</summary>
internal static class ComboCommands
{
    public const string Help = """
        rbctl combo <lệnh>

          list                              Danh sách combo + thành viên
          show <combo>                      Thành viên theo thứ tự
          add <tên> [--mode round-robin|fallback] <item>...
          update <combo> [--name <tên mới>] [--mode …] [<item>...]
                                            Truyền item = thay TOÀN BỘ danh sách thành viên
          delete <combo> [--yes]

        <item>  = model (id số hoặc <provider>/<modelId>) hoặc combo:<tên|id> (combo lồng).
        Thứ tự item = thứ tự thử ở chế độ fallback.
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
            case "delete" or "rm" or "remove":
                return await DeleteAsync(ctx);
            case null or "help":
                ctx.Output.Out.WriteLine(Help);
                return sub is null ? CliExitCodes.Usage : CliExitCodes.Ok;
            default:
                throw new CliUsageException($"Lệnh combo không hợp lệ: {sub}\n\n{Help}");
        }
    }

    private static async Task<int> ListAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        ctx.Args.EnsureNoMorePositionals();
        var (combos, labeler) = await LoadAsync(ctx);
        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(combos.Select(c => ToJson(c, labeler)));
            return CliExitCodes.Ok;
        }
        ctx.Output.Table(["ID", "NAME", "MODE", "MEMBERS"],
            combos.Select(c => (IReadOnlyList<string?>)
            [
                Fmt.Id(c.Id), c.Name, ModeText(c.Mode),
                string.Join(", ", c.Items.OrderBy(i => i.Position).Select(labeler.Label)),
            ]));
        return CliExitCodes.Ok;
    }

    private static async Task<int> ShowAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var reference = ctx.Args.Require("combo");
        ctx.Args.EnsureNoMorePositionals();
        var (combos, labeler) = await LoadAsync(ctx);
        var combo = RefResolver.Combo(combos, reference);
        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(ToJson(combo, labeler));
            return CliExitCodes.Ok;
        }
        ctx.Output.Pairs([("Id", Fmt.Id(combo.Id)), ("Name", combo.Name), ("Mode", ModeText(combo.Mode))]);
        ctx.Output.Line();
        ctx.Output.Table(["#", "MEMBER", "TYPE"],
            combo.Items.OrderBy(i => i.Position).Select((item, index) => (IReadOnlyList<string?>)
            [
                Fmt.Id(index + 1), labeler.Label(item), item.TargetComboId is null ? "model" : "combo",
            ]));
        return CliExitCodes.Ok;
    }

    private static async Task<int> AddAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions("mode");
        var name = ctx.Args.Require("tên");
        var itemRefs = ctx.Args.Remaining;
        if (itemRefs.Count == 0) throw new CliUsageException("Combo cần ít nhất một <item>.");

        var draft = new ComboDraft
        {
            Name = name,
            Mode = ParseMode(ctx.Args.Option("mode")) ?? ComboMode.RoundRobin,
            Items = await ResolveItemsAsync(ctx, itemRefs),
        };
        var created = await ctx.Get<IComboService>().CreateAsync(draft, ctx.Cancellation);
        ctx.Output.Done($"Đã tạo combo #{created.Id} '{created.Name}' ({ModeText(created.Mode)}, {draft.Items.Count} thành viên). " +
            $"Client gọi bằng model = \"{created.Name}\".", new { created.Id, created.Name });
        return CliExitCodes.Ok;
    }

    private static async Task<int> UpdateAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions("mode", "name");
        var combo = await ctx.ResolveComboAsync(ctx.Args.Require("combo"));
        var itemRefs = ctx.Args.Remaining;

        // Không truyền item = giữ nguyên danh sách theo thứ tự hiện tại
        var items = itemRefs.Count > 0
            ? await ResolveItemsAsync(ctx, itemRefs)
            : combo.Items.OrderBy(i => i.Position)
                .Select(i => new ComboItemDraft { TargetModelId = i.TargetModelId, TargetComboId = i.TargetComboId })
                .ToList();

        var draft = new ComboDraft
        {
            Name = ctx.Args.Option("name") ?? combo.Name,
            Mode = ParseMode(ctx.Args.Option("mode")) ?? combo.Mode,
            Items = items,
        };
        await ctx.Get<IComboService>().UpdateAsync(combo.Id, draft, ctx.Cancellation);
        ctx.Output.Done($"Đã cập nhật combo #{combo.Id} '{draft.Name}'.");
        return CliExitCodes.Ok;
    }

    private static async Task<int> DeleteAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var combo = await ctx.ResolveComboAsync(ctx.Args.Require("combo"));
        ctx.Args.EnsureNoMorePositionals();
        var service = ctx.Get<IComboService>();
        var parents = await service.GetReferencingComboNamesAsync(combo.Id, ctx.Cancellation);
        var question = parents.Count == 0
            ? $"Xoá combo #{combo.Id} '{combo.Name}'?"
            : $"Combo '{combo.Name}' đang nằm trong: {string.Join(", ", parents)}. Vẫn xoá?";
        if (!ctx.Confirm(question, Console.In))
        {
            ctx.Output.Line("Đã huỷ.");
            return CliExitCodes.Cancelled;
        }
        await service.DeleteAsync(combo.Id, ctx.Cancellation);
        ctx.Output.Done($"Đã xoá combo #{combo.Id} '{combo.Name}'.");
        return CliExitCodes.Ok;
    }

    /// <summary>
    /// Item <c>combo:&lt;ref&gt;</c> → combo lồng; còn lại là model. Chu trình/trùng lặp do
    /// <see cref="ComboService"/> kiểm tra — CLI không lặp lại luật nghiệp vụ.
    /// </summary>
    private static async Task<List<ComboItemDraft>> ResolveItemsAsync(CliContext ctx, IReadOnlyList<string> refs)
    {
        var providers = await ctx.Get<IProviderService>().ListAsync(ctx.Cancellation);
        var combos = await ctx.Get<IComboService>().ListAsync(ctx.Cancellation);
        var items = new List<ComboItemDraft>(refs.Count);
        foreach (var reference in refs)
        {
            if (reference.StartsWith("combo:", StringComparison.OrdinalIgnoreCase))
            {
                var nested = RefResolver.Combo(combos, reference["combo:".Length..]);
                items.Add(new ComboItemDraft { TargetComboId = nested.Id });
            }
            else
            {
                var model = RefResolver.Model(providers, reference);
                items.Add(new ComboItemDraft { TargetModelId = model.Id });
            }
        }
        return items;
    }

    private static async Task<(IReadOnlyList<Combo> Combos, ItemLabeler Labeler)> LoadAsync(CliContext ctx)
    {
        var combos = await ctx.Get<IComboService>().ListAsync(ctx.Cancellation);
        var providers = await ctx.Get<IProviderService>().ListAsync(ctx.Cancellation);
        return (combos, new ItemLabeler(providers, combos));
    }

    private static object ToJson(Combo combo, ItemLabeler labeler) => new
    {
        combo.Id,
        combo.Name,
        mode = ModeText(combo.Mode),
        items = combo.Items.OrderBy(i => i.Position).Select(i => new
        {
            i.Position,
            i.TargetModelId,
            i.TargetComboId,
            label = labeler.Label(i),
        }),
    };

    internal static ComboMode? ParseMode(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "round-robin" or "roundrobin" or "rr" => ComboMode.RoundRobin,
        "fallback" or "fb" => ComboMode.Fallback,
        _ => throw new CliUsageException($"--mode phải là round-robin hoặc fallback (nhận '{raw}')."),
    };

    internal static string ModeText(ComboMode mode) => mode == ComboMode.Fallback ? "fallback" : "round-robin";

    /// <summary>Đổi item (id model/combo) thành nhãn dễ đọc: <c>identifier/modelId</c> hoặc <c>combo:tên</c>.</summary>
    private sealed class ItemLabeler
    {
        private readonly Dictionary<long, string> _models = new();
        private readonly Dictionary<long, string> _combos;

        public ItemLabeler(IReadOnlyList<Provider> providers, IReadOnlyList<Combo> combos)
        {
            foreach (var provider in providers)
            {
                foreach (var model in provider.Models)
                    _models[model.Id] = ModelCommands.ModelKey(provider, model);
            }
            _combos = combos.ToDictionary(c => c.Id, c => c.Name);
        }

        public string Label(ComboItem item)
        {
            if (item.TargetComboId is { } comboId)
                return _combos.TryGetValue(comboId, out var name) ? $"combo:{name}" : $"combo:#{comboId}";
            if (item.TargetModelId is { } modelId)
                return _models.TryGetValue(modelId, out var key) ? key : $"model#{modelId}";
            return "?";
        }
    }
}
