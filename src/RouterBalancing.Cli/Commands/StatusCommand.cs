using RouterBalancing.Core.Combos;
using RouterBalancing.Core.Providers;
using RouterBalancing.Core.Server;
using RouterBalancing.Core.Settings;

namespace RouterBalancing.Cli.Commands;

/// <summary><c>rbctl status</c> — app có chạy không, proxy có phản hồi không, tóm tắt cấu hình.</summary>
internal static class StatusCommand
{
    public const string Help = """
        rbctl status

          Trạng thái app desktop, /health của proxy, đường dẫn DB và số lượng
          provider/model/combo/client key. Exit code 0 khi proxy phản hồi OK, 1 nếu không.
        """;

    public static async Task<int> RunAsync(CliContext ctx)
    {
        if (ctx.Args.Next() == "help")
        {
            ctx.Output.Out.WriteLine(Help);
            return CliExitCodes.Ok;
        }
        ctx.Args.EnsureNoMorePositionals();
        ctx.Args.EnsureOnlyOptions();

        var settings = ctx.Get<IAppSettingsService>();
        var providers = await ctx.Get<IProviderService>().ListAsync(ctx.Cancellation);
        var combos = await ctx.Get<IComboService>().ListAsync(ctx.Cancellation);
        var keys = await ctx.Get<IClientKeyService>().ListAsync(ctx.Cancellation);
        var (healthy, healthMessage) = await AppProbe.CheckHealthAsync(settings.Port, ctx.Cancellation);

        var enabledProviders = providers.Where(p => p.Enabled).ToList();
        // Model "phục vụ được" = model bật của provider bật — con số client thực sự gọi được
        var servableModels = enabledProviders.Sum(p => p.Models.Count(m => m.Enabled));
        var dbFile = new FileInfo(ctx.Environment.DatabasePath);
        var baseUrl = $"http://127.0.0.1:{settings.Port}/v1";
        var keyStorage = ctx.Environment.Protector is FileKeySecretProtector fileKey
            ? $"AES-GCM, khoá {fileKey.KeyPath}"
            : "DPAPI (user Windows hiện tại)";

        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(new
            {
                appRunning = ctx.AppRunning,
                proxy = new { port = settings.Port, healthy, message = healthMessage, baseUrl, settings.LanAccess },
                database = new { path = dbFile.FullName, sizeBytes = dbFile.Exists ? dbFile.Length : 0 },
                keyStorage,
                providers = new { total = providers.Count, enabled = enabledProviders.Count },
                models = new { total = providers.Sum(p => p.Models.Count), servable = servableModels },
                combos = combos.Count,
                clientKeys = new { total = keys.Count, enabled = keys.Count(k => k.Enabled) },
            });
        }
        else
        {
            ctx.Output.Pairs(
            [
                ("App desktop", ctx.AppRunning ? "đang chạy" : "không chạy"),
                ("Proxy", healthy ? $"OK — {baseUrl}" : $"không phản hồi ở cổng {settings.Port} ({healthMessage})"),
                ("Truy cập LAN", settings.LanAccess ? "bật" : "tắt"),
                ("Database", $"{dbFile.FullName} ({FormatSize(dbFile)})"),
                ("Mã hoá API key", keyStorage),
                ("Provider", $"{enabledProviders.Count} bật / {providers.Count}"),
                ("Model phục vụ được", $"{servableModels} (tổng {providers.Sum(p => p.Models.Count)})"),
                ("Combo", combos.Count.ToString()),
                ("Client key", keys.Count == 0
                    ? "chưa có (proxy không yêu cầu key)"
                    : $"{keys.Count(k => k.Enabled)} bật / {keys.Count}"),
            ]);
            if (ctx.AppRunning && !healthy)
                ctx.Output.Warn("App đang chạy nhưng proxy không phản hồi ở cổng cấu hình — có thể app đã chọn cổng khác vì cổng bận, hoặc proxy đang tắt.");
        }

        return healthy ? CliExitCodes.Ok : CliExitCodes.Error;
    }

    private static string FormatSize(FileInfo file)
    {
        if (!file.Exists) return "chưa tạo";
        var mb = file.Length / 1024d / 1024d;
        return mb >= 1 ? $"{mb:0.0} MB" : $"{file.Length / 1024d:0} KB";
    }
}
