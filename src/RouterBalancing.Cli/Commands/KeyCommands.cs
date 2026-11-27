using RouterBalancing.Core.Server;

namespace RouterBalancing.Cli.Commands;

/// <summary><c>rbctl key …</c> — client key mà ứng dụng (opencode, IDE…) dùng để gọi proxy.</summary>
internal static class KeyCommands
{
    public const string Help = """
        rbctl key <lệnh>

          list                              Danh sách client key (chỉ hiện mask)
          create <tên> [--rpm <n>] [--tpm <n>]
                                            Tạo key — plaintext CHỈ in ra một lần
          update <key> [--name <tên>] [--rpm <n|none>] [--tpm <n|none>]
          enable <key> | disable <key>
          delete <key> [--yes]

        <key> = id hoặc tên. --rpm: request/phút, --tpm: token/phút (none = không giới hạn).
        Khi app đang chạy, đổi key qua CLI cần khởi động lại app để proxy nạp lại cache xác thực.
        """;

    public static async Task<int> RunAsync(CliContext ctx)
    {
        var sub = ctx.Args.Next();
        switch (sub)
        {
            case "list" or "ls":
                return await ListAsync(ctx);
            case "create" or "add":
                return await CreateAsync(ctx);
            case "update" or "edit":
                return await UpdateAsync(ctx);
            case "enable":
                return await SetEnabledAsync(ctx, true);
            case "disable":
                return await SetEnabledAsync(ctx, false);
            case "delete" or "rm" or "remove" or "revoke":
                return await DeleteAsync(ctx);
            case null or "help":
                ctx.Output.Out.WriteLine(Help);
                return sub is null ? CliExitCodes.Usage : CliExitCodes.Ok;
            default:
                throw new CliUsageException($"Lệnh key không hợp lệ: {sub}\n\n{Help}");
        }
    }

    private static async Task<int> ListAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        ctx.Args.EnsureNoMorePositionals();
        var keys = await ctx.Get<IClientKeyService>().ListAsync(ctx.Cancellation);
        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(keys.Select(k => new
            {
                k.Id,
                k.Name,
                k.KeyMask,
                k.Enabled,
                k.RatePerMinute,
                k.TokensPerMinute,
                requestsToday = k.RequestsUsed,
                tokensToday = k.TokensUsed,
                k.LastUsedAt,
                k.CreatedAt,
            }));
            return CliExitCodes.Ok;
        }
        ctx.Output.Table(["ID", "NAME", "KEY", "ON", "RPM", "TPM", "REQ TODAY", "TOK TODAY", "LAST USED"],
            keys.Select(k => (IReadOnlyList<string?>)
            [
                Fmt.Id(k.Id), k.Name, k.KeyMask, Fmt.YesNo(k.Enabled),
                Fmt.Number(k.RatePerMinute) ?? "∞", Fmt.Number(k.TokensPerMinute) ?? "∞",
                Fmt.Number(k.RequestsUsed), Fmt.Number(k.TokensUsed), Fmt.Time(k.LastUsedAt),
            ]));
        return CliExitCodes.Ok;
    }

    private static async Task<int> CreateAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions("rpm", "tpm");
        var name = ctx.Args.Require("tên");
        ctx.Args.EnsureNoMorePositionals();
        var draft = new ClientKeyDraft(name, ctx.Args.NullableIntOption("rpm").Value, ctx.Args.NullableIntOption("tpm").Value);
        var (key, plaintext) = await ctx.Get<IClientKeyService>().CreateAsync(draft, ctx.Cancellation);

        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(new { key.Id, key.Name, key.KeyMask, apiKey = plaintext });
        }
        else
        {
            ctx.Output.Out.WriteLine($"Đã tạo client key #{key.Id} '{key.Name}'.");
            ctx.Output.Out.WriteLine();
            ctx.Output.Out.WriteLine($"    {plaintext}");
            ctx.Output.Out.WriteLine();
            ctx.Output.Out.WriteLine("Lưu key này ngay — DB chỉ giữ hash, không xem lại được.");
        }
        ctx.WarnRestartIfRunning("key mới");
        return CliExitCodes.Ok;
    }

    private static async Task<int> UpdateAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions("name", "rpm", "tpm");
        var key = await ctx.ResolveClientKeyAsync(ctx.Args.Require("key"));
        ctx.Args.EnsureNoMorePositionals();
        var rpm = ctx.Args.NullableIntOption("rpm");
        var tpm = ctx.Args.NullableIntOption("tpm");
        var draft = new ClientKeyDraft(
            ctx.Args.Option("name") ?? key.Name,
            rpm.Present ? rpm.Value : key.RatePerMinute,
            tpm.Present ? tpm.Value : key.TokensPerMinute);
        await ctx.Get<IClientKeyService>().UpdateAsync(key.Id, draft, ctx.Cancellation);
        ctx.Output.Done($"Đã cập nhật client key #{key.Id} '{draft.Name}'.");
        ctx.WarnRestartIfRunning("giới hạn/tên key");
        return CliExitCodes.Ok;
    }

    private static async Task<int> SetEnabledAsync(CliContext ctx, bool enabled)
    {
        ctx.Args.EnsureOnlyOptions();
        var key = await ctx.ResolveClientKeyAsync(ctx.Args.Require("key"));
        ctx.Args.EnsureNoMorePositionals();
        await ctx.Get<IClientKeyService>().SetEnabledAsync(key.Id, enabled, ctx.Cancellation);
        ctx.Output.Done($"Client key #{key.Id} '{key.Name}' đã {(enabled ? "bật" : "tắt")}.");
        ctx.WarnRestartIfRunning(enabled ? "bật key" : "tắt key");
        return CliExitCodes.Ok;
    }

    private static async Task<int> DeleteAsync(CliContext ctx)
    {
        ctx.Args.EnsureOnlyOptions();
        var key = await ctx.ResolveClientKeyAsync(ctx.Args.Require("key"));
        ctx.Args.EnsureNoMorePositionals();
        if (!ctx.Confirm($"Xoá client key #{key.Id} '{key.Name}' ({key.KeyMask})? Client đang dùng key sẽ bị 401.", Console.In))
        {
            ctx.Output.Line("Đã huỷ.");
            return CliExitCodes.Cancelled;
        }
        await ctx.Get<IClientKeyService>().DeleteAsync(key.Id, ctx.Cancellation);
        ctx.Output.Done($"Đã xoá client key #{key.Id} '{key.Name}'.");
        ctx.WarnRestartIfRunning("thu hồi key");
        return CliExitCodes.Ok;
    }
}
