using System.Globalization;
using RouterBalancing.Core.Localization;
using RouterBalancing.Core.Settings;

namespace RouterBalancing.Cli.Commands;

/// <summary><c>rbctl settings …</c> — đọc/ghi cấu hình app (bảng AppSettings), validate như trang Settings.</summary>
internal static class SettingsCommands
{
    public const string Help = """
        rbctl settings <lệnh>

          list                              Mọi setting + giá trị hiện tại
          get <key>
          set <key> <value>                 Validate cùng luật với trang Settings
          reset <key>                       Về giá trị mặc định

        Khi app đang chạy, setting ghi qua CLI có hiệu lực sau khi khởi động lại app.
        """;

    private enum Kind { Int, Bool, Text }

    /// <summary>
    /// Mô tả một setting: key DB, kiểu, đọc giá trị hiện tại, gán vào draft để validate.
    /// <c>startWithWindows</c> không có ở đây: bật/tắt cần ghi registry qua IStartupRegistration
    /// của app MAUI — chỉ đổi cờ DB sẽ lệch với trạng thái thật.
    /// </summary>
    private sealed record Descriptor(
        string Key,
        Kind Kind,
        string Description,
        Func<IAppSettingsService, object> Read,
        Action<SettingsDraft, object> Apply,
        Func<SettingsDraft, object> Default);

    private static readonly IReadOnlyList<Descriptor> Descriptors =
    [
        new(SettingsKeys.Port, Kind.Int, "Cổng proxy local (1024–65535)",
            s => s.Port, (d, v) => d.Port = (int)v, d => d.Port),
        new(SettingsKeys.LanAccess, Kind.Bool, "Cho phép máy khác trong LAN gọi proxy",
            s => s.LanAccess, (d, v) => d.LanAccess = (bool)v, d => d.LanAccess),
        new(SettingsKeys.Language, Kind.Text, "Ngôn ngữ UI (auto|en|vi|mã khác)",
            s => s.Language, (d, v) => d.Language = (string)v, d => d.Language),
        new(SettingsKeys.Theme, Kind.Text, "Giao diện (light|dark|system)",
            s => s.Theme, (d, v) => d.Theme = (string)v, d => d.Theme),
        new(SettingsKeys.CloseToTray, Kind.Bool, "Đóng cửa sổ = thu xuống khay",
            s => s.CloseToTray, (d, v) => d.CloseToTray = (bool)v, d => d.CloseToTray),
        new(SettingsKeys.NotifyErrors, Kind.Bool, "Thông báo khi request lỗi",
            s => s.NotifyErrors, (d, v) => d.NotifyErrors = (bool)v, d => d.NotifyErrors),
        new(SettingsKeys.ProviderProbeTimeoutSec, Kind.Int, "Timeout test/fetch provider, giây (1–600)",
            s => s.ProviderProbeTimeoutSec, (d, v) => d.ProviderProbeTimeoutSec = (int)v, d => d.ProviderProbeTimeoutSec),
        new(SettingsKeys.TransientMaxRetries, Kind.Int, "Số lần retry lỗi tạm thời (0–10)",
            s => s.TransientMaxRetries, (d, v) => d.TransientMaxRetries = (int)v, d => d.TransientMaxRetries),
        new(SettingsKeys.TransientBackoffBaseMs, Kind.Int, "Backoff cơ sở, ms (250–4000)",
            s => s.TransientBackoffBaseMs, (d, v) => d.TransientBackoffBaseMs = (int)v, d => d.TransientBackoffBaseMs),
        new(SettingsKeys.UpstreamIdleTimeoutSec, Kind.Int, "Watchdog stream upstream im lặng, giây (0 = tắt)",
            s => s.UpstreamIdleTimeoutSec, (d, v) => d.UpstreamIdleTimeoutSec = (int)v, d => d.UpstreamIdleTimeoutSec),
        new(SettingsKeys.ProviderQuotaBlockSec, Kind.Int, "Cooldown provider khi hết quota, giây (0 = tắt)",
            s => s.ProviderQuotaBlockSec, (d, v) => d.ProviderQuotaBlockSec = (int)v, d => d.ProviderQuotaBlockSec),
        new(SettingsKeys.LogRetentionDays, Kind.Int, "Giữ log bao nhiêu ngày (1–3650)",
            s => s.LogRetentionDays, (d, v) => d.LogRetentionDays = (int)v, d => d.LogRetentionDays),
        new(SettingsKeys.StatsErrorRateThreshold, Kind.Int, "Ngưỡng % lỗi để cảnh báo trên Dashboard (1–100)",
            s => s.StatsErrorRateThreshold, (d, v) => d.StatsErrorRateThreshold = (int)v, d => d.StatsErrorRateThreshold),
        new(SettingsKeys.OpenCodeConfigPath, Kind.Text, "Đường dẫn opencode.json",
            s => s.OpenCodeConfigPath, (d, v) => d.OpenCodeConfigPath = (string)v, _ => ""),
        new(SettingsKeys.DatasetExportEnabled, Kind.Bool, "Ghi dataset JSONL từ request thành công",
            s => s.DatasetExportEnabled, (d, v) => d.DatasetExportEnabled = (bool)v, d => d.DatasetExportEnabled),
        new(SettingsKeys.DatasetExportPath, Kind.Text, "Thư mục dataset (rỗng = mặc định AppData)",
            s => s.DatasetExportPath, (d, v) => d.DatasetExportPath = (string)v, d => d.DatasetExportPath),
        new(SettingsKeys.DatasetRetentionDays, Kind.Int, "Giữ dataset bao nhiêu ngày (0 = mãi mãi)",
            s => s.DatasetRetentionDays, (d, v) => d.DatasetRetentionDays = (int)v, d => d.DatasetRetentionDays),
    ];

    public static Task<int> RunAsync(CliContext ctx)
    {
        var sub = ctx.Args.Next();
        ctx.Args.EnsureOnlyOptions();
        return Task.FromResult(sub switch
        {
            "list" or "ls" => List(ctx),
            "get" => Get(ctx),
            "set" => Set(ctx),
            "reset" => Reset(ctx),
            "help" => PrintHelp(ctx, CliExitCodes.Ok),
            null => PrintHelp(ctx, CliExitCodes.Usage),
            _ => throw new CliUsageException($"Lệnh settings không hợp lệ: {sub}\n\n{Help}"),
        });
    }

    private static int PrintHelp(CliContext ctx, int code)
    {
        ctx.Output.Out.WriteLine(Help);
        return code;
    }

    private static int List(CliContext ctx)
    {
        ctx.Args.EnsureNoMorePositionals();
        var settings = ctx.Get<IAppSettingsService>();
        if (ctx.Output.Json)
        {
            ctx.Output.WriteJson(Descriptors.ToDictionary(d => d.Key, d => d.Read(settings)));
            return CliExitCodes.Ok;
        }
        ctx.Output.Table(["KEY", "VALUE", "MÔ TẢ"],
            Descriptors.Select(d => (IReadOnlyList<string?>)[d.Key, FormatValue(d.Read(settings)), d.Description]));
        ctx.Output.Line();
        ctx.Output.Line($"startWithWindows = {FormatValue(settings.StartWithWindows)} (chỉ đổi được trong app).");
        return CliExitCodes.Ok;
    }

    private static int Get(CliContext ctx)
    {
        var descriptor = Find(ctx.Args.Require("key"));
        ctx.Args.EnsureNoMorePositionals();
        var value = descriptor.Read(ctx.Get<IAppSettingsService>());
        if (ctx.Output.Json)
            ctx.Output.WriteJson(new Dictionary<string, object> { [descriptor.Key] = value });
        else
            ctx.Output.Out.WriteLine(FormatValue(value));
        return CliExitCodes.Ok;
    }

    private static int Set(CliContext ctx)
    {
        var descriptor = Find(ctx.Args.Require("key"));
        var raw = ctx.Args.Require("value");
        ctx.Args.EnsureNoMorePositionals();
        return Write(ctx, descriptor, Parse(descriptor, raw));
    }

    private static int Reset(CliContext ctx)
    {
        var descriptor = Find(ctx.Args.Require("key"));
        ctx.Args.EnsureNoMorePositionals();
        return Write(ctx, descriptor, descriptor.Default(new SettingsDraft()));
    }

    private static int Write(CliContext ctx, Descriptor descriptor, object value)
    {
        var settings = ctx.Get<IAppSettingsService>();
        // Validate cả draft (không chỉ field đổi) — giống SettingsPanel: luật nằm trên draft hoàn chỉnh
        var draft = CurrentDraft(settings);
        descriptor.Apply(draft, value);

        // openCodeConfigPath rỗng = "dùng mặc định" khi reset; validator đòi non-empty nên
        // validate bằng đường dẫn mặc định đã mở rộng.
        if (descriptor.Key == SettingsKeys.OpenCodeConfigPath && string.IsNullOrWhiteSpace(draft.OpenCodeConfigPath))
            draft.OpenCodeConfigPath = RouterBalancing.Core.Tools.OpenCodeConfigPaths.DefaultPath;

        var errors = SettingsValidator.Validate(draft);
        var field = ToDraftPropertyName(descriptor.Key);
        if (errors.TryGetValue(field, out var errorKey))
            throw new CliUsageException($"{descriptor.Key}: {Translate(errorKey)}");

        settings.Set(descriptor.Key, value);
        ctx.Output.Done($"{descriptor.Key} = {FormatValue(descriptor.Read(settings))}");
        ctx.WarnRestartIfRunning($"setting {descriptor.Key}");
        return CliExitCodes.Ok;
    }

    private static SettingsDraft CurrentDraft(IAppSettingsService s) => new()
    {
        Language = s.Language,
        Theme = s.Theme,
        Port = s.Port,
        LanAccess = s.LanAccess,
        CloseToTray = s.CloseToTray,
        StartWithWindows = s.StartWithWindows,
        NotifyErrors = s.NotifyErrors,
        ProviderProbeTimeoutSec = s.ProviderProbeTimeoutSec,
        LogRetentionDays = s.LogRetentionDays,
        StatsErrorRateThreshold = s.StatsErrorRateThreshold,
        TransientMaxRetries = s.TransientMaxRetries,
        TransientBackoffBaseMs = s.TransientBackoffBaseMs,
        UpstreamIdleTimeoutSec = s.UpstreamIdleTimeoutSec,
        ProviderQuotaBlockSec = s.ProviderQuotaBlockSec,
        OpenCodeConfigPath = s.OpenCodeConfigPath,
        DatasetExportEnabled = s.DatasetExportEnabled,
        DatasetExportPath = s.DatasetExportPath,
        DatasetRetentionDays = s.DatasetRetentionDays,
    };

    private static Descriptor Find(string key) =>
        Descriptors.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? throw new CliUsageException(key.Equals(SettingsKeys.StartWithWindows, StringComparison.OrdinalIgnoreCase)
            ? "startWithWindows cần ghi registry — đổi trong app (Settings)."
            : $"Setting không tồn tại: '{key}'. Xem `rbctl settings list`.");

    private static object Parse(Descriptor descriptor, string raw) => descriptor.Kind switch
    {
        Kind.Int => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : throw new CliUsageException($"{descriptor.Key} cần số nguyên (nhận '{raw}')."),
        Kind.Bool => CliArgs.IsTrue(raw) ? true
            : CliArgs.IsFalse(raw) ? false
            : throw new CliUsageException($"{descriptor.Key} cần true/false (nhận '{raw}')."),
        _ => raw,
    };

    // SettingsKeys là camelCase của tên property SettingsDraft (port → Port)
    private static string ToDraftPropertyName(string key) =>
        char.ToUpperInvariant(key[0]) + key[1..];

    private static string FormatValue(object value) => value switch
    {
        bool b => b ? "true" : "false",
        string s when s.Length == 0 => "\"\"",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Khoá lỗi i18n của validator → câu tiếng Việt (CLI dùng tiếng Việt cố định).</summary>
    internal static string Translate(string key) =>
        Translations.Vietnamese.TryGetValue(key, out var text) ? text : key;
}
