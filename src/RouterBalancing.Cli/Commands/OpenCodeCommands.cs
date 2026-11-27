using RouterBalancing.Core.Combos;
using RouterBalancing.Core.Providers;
using RouterBalancing.Core.Settings;
using RouterBalancing.Core.Tools;

namespace RouterBalancing.Cli.Commands;

/// <summary><c>rbctl opencode …</c> — sinh/ghi block provider cho opencode.json (giống trang Tools).</summary>
internal static class OpenCodeCommands
{
    public const string Help = """
        rbctl opencode <lệnh>

          list                              Model/combo có thể đưa vào config, đánh dấu [x] đang chọn
          generate [chọn lọc]               In block config JSON ra stdout
          write [--path <file>] [chọn lọc]  Ghi/ghép vào opencode.json (tự backup .bak)

        Chọn lọc (mặc định = lựa chọn đã lưu ở trang Tools, chưa lưu thì bộ mặc định):
          --models <model,...>              id hoặc <provider>/<modelId>; "default" = bộ mặc định
          --combos <combo,...>              id hoặc tên combo; "none" = không combo nào
          --api-key <client key>            Key nhúng vào config (bỏ trống nếu proxy không bật key)
          --base-url <url>                  Mặc định http://127.0.0.1:<port>/v1
          --save-selection                  Lưu lựa chọn này làm mặc định cho trang Tools
        """;

    public static async Task<int> RunAsync(CliContext ctx)
    {
        var sub = ctx.Args.Next();
        switch (sub)
        {
            case "list" or "ls":
                return await ListAsync(ctx);
            case "generate" or "gen":
                return await GenerateAsync(ctx, write: false);
            case "write":
                return await GenerateAsync(ctx, write: true);
            case null or "help":
                ctx.Output.Out.WriteLine(Help);
                return sub is null ? CliExitCodes.Usage : CliExitCodes.Ok;
            default:
                throw new CliUsageException($"Lệnh opencode không hợp lệ: {sub}\n\n{Help}");
        }
    }

    private static async Task<int> ListAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        ctx.Args.EnsureNoMorePositionals();
        var data = await ctx.Get<IOpenCodeConfigService>().GetPageDataAsync(ctx.Cancellation);
        var selection = StoredSelection(ctx, data);
        var selectedModels = selection.ModelIds.ToHashSet();
        var selectedCombos = selection.ComboIds.ToHashSet();

        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(new
            {
                models = data.Groups.SelectMany(g => g.Models.Select(m => new
                {
                    m.Id,
                    g.ProviderId,
                    provider = g.ProviderName,
                    m.ModelId,
                    m.DisplayName,
                    m.ContextWindow,
                    m.Tools,
                    m.Reasoning,
                    m.Vision,
                    selected = selectedModels.Contains(m.Id),
                })),
                combos = data.Combos.Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Mode,
                    c.MemberCount,
                    selected = selectedCombos.Contains(c.Id),
                }),
            });
            return CliExitCodes.Ok;
        }

        foreach (var group in data.Groups)
        {
            ctx.Output.Out.WriteLine($"{group.ProviderName}{(group.ProviderEnabled ? "" : " (đang tắt)")}");
            ctx.Output.Table(["", "ID", "MODEL", "CONTEXT", "CAP"],
                group.Models.Select(m => (IReadOnlyList<string?>)
                [
                    selectedModels.Contains(m.Id) ? "[x]" : "[ ]", Fmt.Id(m.Id), m.ModelId,
                    Fmt.Number(m.ContextWindow), Capabilities(m.Tools, m.Reasoning, m.Vision),
                ]));
            ctx.Output.Out.WriteLine();
        }
        if (data.Combos.Count > 0)
        {
            ctx.Output.Out.WriteLine("Combo");
            ctx.Output.Table(["", "ID", "NAME", "MODE", "MEMBERS"],
                data.Combos.Select(c => (IReadOnlyList<string?>)
                [
                    selectedCombos.Contains(c.Id) ? "[x]" : "[ ]", Fmt.Id(c.Id), c.Name,
                    ComboCommands.ModeText(c.Mode), Fmt.Id(c.MemberCount),
                ]));
        }
        return CliExitCodes.Ok;
    }

    private static async Task<int> GenerateAsync(CliContext ctx, bool write)
    {
        if (write)
            ctx.Args.EnsureOnlyOptions("models", "combos", "api-key", "base-url", "save-selection", "path");
        else
            ctx.Args.EnsureOnlyOptions("models", "combos", "api-key", "base-url", "save-selection");
        ctx.Args.EnsureNoMorePositionals();

        var service = ctx.Get<IOpenCodeConfigService>();
        var settings = ctx.Get<IAppSettingsService>();
        var data = await service.GetPageDataAsync(ctx.Cancellation);
        var selection = await ResolveSelectionAsync(ctx, data);
        if (selection.ModelIds.Length == 0 && selection.ComboIds.Length == 0)
            throw new CliUsageException("Chưa chọn model/combo nào — dùng --models/--combos hoặc chọn trong trang Tools.");

        // CLI không biết port proxy thực tế (PortSelector có thể chọn port khác khi bận) —
        // dùng port cấu hình, cho phép ghi đè bằng --base-url.
        var baseUrl = ctx.Args.Option("base-url") ?? OpenCodeConfigService.BuildBaseUrl(null, settings.Port);
        var apiKey = ctx.Args.Option("api-key");

        if (ctx.Args.Flag("save-selection"))
            settings.Set(SettingsKeys.ToolsSelection, selection);

        if (!write)
        {
            var result = await service.GenerateAsync(selection.ModelIds, selection.ComboIds, baseUrl, apiKey, ctx.Cancellation);
            // stdout chỉ chứa JSON config để pipe thẳng vào file; mọi ghi chú qua stderr
            ctx.Output.Out.WriteLine(result.Json);
            ReportSkipped(ctx, result.SkippedKeys);
            return CliExitCodes.Ok;
        }

        var path = OpenCodeConfigPaths.Expand(ctx.Args.Option("path") ?? settings.OpenCodeConfigPath);
        var written = await service.WriteAsync(path, selection.ModelIds, selection.ComboIds, baseUrl, apiKey, ctx.Cancellation);
        ctx.Output.Done(
            written.BackupPath is null
                ? $"Đã ghi {written.Path}"
                : $"Đã ghi {written.Path} (bản cũ: {written.BackupPath})",
            new { written.Path, written.BackupPath, models = selection.ModelIds.Length, combos = selection.ComboIds.Length });
        return CliExitCodes.Ok;
    }

    /// <summary>
    /// Lựa chọn hiệu lực: tham số dòng lệnh thắng; thiếu phần nào thì lấy phần đó từ lựa chọn
    /// đã lưu (ToolsSelectionResolver lọc id đã xoá — cùng logic trang Tools).
    /// </summary>
    private static async Task<ToolsSelection> ResolveSelectionAsync(CliContext ctx, OpenCodeConfigPageData data)
    {
        var stored = StoredSelection(ctx, data);
        var modelArgs = ctx.Args.OptionList("models");
        var comboArgs = ctx.Args.OptionList("combos");

        long[] modelIds = stored.ModelIds;
        if (modelArgs.Count > 0)
        {
            if (modelArgs.Count == 1 && modelArgs[0].Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                modelIds = data.DefaultSelectedIds.ToArray();
            }
            else if (modelArgs.Count == 1 && modelArgs[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                modelIds = [];
            }
            else
            {
                var providers = await ctx.Get<IProviderService>().ListAsync(ctx.Cancellation);
                modelIds = modelArgs.Select(r => RefResolver.Model(providers, r).Id).Distinct().ToArray();
            }
        }

        long[] comboIds = stored.ComboIds;
        if (comboArgs.Count > 0)
        {
            if (comboArgs.Count == 1 && comboArgs[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                comboIds = [];
            }
            else
            {
                var combos = await ctx.Get<IComboService>().ListAsync(ctx.Cancellation);
                comboIds = comboArgs.Select(r => RefResolver.Combo(combos, r).Id).Distinct().ToArray();
            }
        }

        return new ToolsSelection(modelIds, comboIds);
    }

    private static ToolsSelection StoredSelection(CliContext ctx, OpenCodeConfigPageData data)
    {
        var stored = ctx.Get<IAppSettingsService>().Get<ToolsSelection?>(SettingsKeys.ToolsSelection, null);
        return ToolsSelectionResolver.Resolve(
            stored,
            data.Groups.SelectMany(g => g.Models).Select(m => m.Id).ToHashSet(),
            data.Combos.Select(c => c.Id).ToHashSet(),
            data.DefaultSelectedIds);
    }

    private static void ReportSkipped(CliContext ctx, IReadOnlyList<string> skipped)
    {
        if (skipped.Count > 0)
            ctx.Output.Warn($"Bỏ qua {skipped.Count} key trùng: {string.Join(", ", skipped)}");
    }

    private static string Capabilities(bool? tools, bool? reasoning, bool? vision)
    {
        var parts = new List<string>(3);
        if (tools == true) parts.Add("tools");
        if (reasoning == true) parts.Add("think");
        if (vision == true) parts.Add("vision");
        return string.Join(",", parts);
    }
}
