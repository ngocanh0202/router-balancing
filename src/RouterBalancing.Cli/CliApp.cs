using System.Reflection;
using Microsoft.Data.Sqlite;
using RouterBalancing.Cli.Commands;
using RouterBalancing.Core.Combos;
using RouterBalancing.Core.Providers;
using RouterBalancing.Core.Tools;

namespace RouterBalancing.Cli;

/// <summary>Exit code của rbctl — script dựa vào đây để biết lệnh thành công hay không.</summary>
internal static class CliExitCodes
{
    public const int Ok = 0;
    /// <summary>Lỗi nghiệp vụ/runtime (không tìm thấy, validate fail, test kết nối fail…).</summary>
    public const int Error = 1;
    /// <summary>Sai cú pháp lệnh.</summary>
    public const int Usage = 2;
    /// <summary>Người dùng từ chối xác nhận.</summary>
    public const int Cancelled = 3;
}

/// <summary>
/// Điểm vào có thể test: parse argv → dựng service → dispatch lệnh → map exception thành exit code.
/// </summary>
internal static class CliApp
{
    public const string Help = """
        rbctl — CLI quản lý Router Balancing (đọc/ghi trực tiếp DB của app)

        Cách dùng: rbctl <nhóm> <lệnh> [tham số] [--json] [--db <file>]

          status                            Trạng thái app, proxy, DB
          provider  list|show|add|update|enable|disable|delete|test
          account   list|add|update|enable|disable|delete|test
          model     list|fetch|sync|add|enable|disable|enable-all|disable-all|remove|remove-all
          combo     list|show|add|update|delete
          key       list|create|update|enable|disable|delete
          logs      [--level] [--category] [--search] [--since] [--limit] [--follow]
          stats     [today|7d|30d]
          settings  list|get|set|reset
          opencode  list|generate|write

        Tuỳ chọn chung:
          --json        Xuất JSON (cho script)
          --db <file>   Dùng file DB khác (mặc định: Windows %AppData%\router-balancing\router-balancing.db,
                        macOS/Linux ~/.config/router-balancing/router-balancing.db)
          --yes         Bỏ qua câu hỏi xác nhận khi xoá
          --help, -h    Trợ giúp; `rbctl <nhóm> help` cho trợ giúp từng nhóm
          --version

        Exit code: 0 OK · 1 lỗi · 2 sai cú pháp · 3 huỷ xác nhận
        """;

    private static readonly IReadOnlyDictionary<string, (string Help, Func<CliContext, Task<int>> Run)> Groups =
        new Dictionary<string, (string, Func<CliContext, Task<int>>)>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = (StatusCommand.Help, StatusCommand.RunAsync),
            ["provider"] = (ProviderCommands.Help, ProviderCommands.RunAsync),
            ["providers"] = (ProviderCommands.Help, ProviderCommands.RunAsync),
            ["account"] = (AccountCommands.Help, AccountCommands.RunAsync),
            ["accounts"] = (AccountCommands.Help, AccountCommands.RunAsync),
            ["model"] = (ModelCommands.Help, ModelCommands.RunAsync),
            ["models"] = (ModelCommands.Help, ModelCommands.RunAsync),
            ["combo"] = (ComboCommands.Help, ComboCommands.RunAsync),
            ["combos"] = (ComboCommands.Help, ComboCommands.RunAsync),
            ["key"] = (KeyCommands.Help, KeyCommands.RunAsync),
            ["keys"] = (KeyCommands.Help, KeyCommands.RunAsync),
            ["logs"] = (LogsCommand.Help, LogsCommand.RunAsync),
            ["log"] = (LogsCommand.Help, LogsCommand.RunAsync),
            ["stats"] = (StatsCommand.Help, StatsCommand.RunAsync),
            ["settings"] = (SettingsCommands.Help, SettingsCommands.RunAsync),
            ["setting"] = (SettingsCommands.Help, SettingsCommands.RunAsync),
            ["opencode"] = (OpenCodeCommands.Help, OpenCodeCommands.RunAsync),
        };

    /// <summary>Chạy CLI với môi trường thật (%AppData% DB, DPAPI, mutex của app).</summary>
    public static Task<int> RunAsync(string[] argv, TextWriter stdout, TextWriter stderr, CancellationToken ct) =>
        RunAsync(argv, stdout, stderr, CliEnvironment.CreateDefault, AppProbe.IsAppRunning, ct);

    /// <summary>
    /// Overload cho test: inject môi trường (DB tạm, protector giả) và trạng thái "app đang chạy".
    /// </summary>
    /// <param name="environmentFactory">Nhận giá trị <c>--db</c> (có thể null), trả môi trường.</param>
    /// <param name="isAppRunning">Probe app desktop.</param>
    internal static async Task<int> RunAsync(
        string[] argv,
        TextWriter stdout,
        TextWriter stderr,
        Func<string?, CliEnvironment> environmentFactory,
        Func<bool> isAppRunning,
        CancellationToken ct)
    {
        CliArgs args;
        try
        {
            args = CliArgs.Parse(argv);
        }
        catch (CliUsageException ex)
        {
            stderr.WriteLine($"Lỗi: {ex.Message}");
            return CliExitCodes.Usage;
        }

        var output = new CliOutput(stdout, stderr, args.Flag("json"));

        if (args.Flag("version"))
        {
            stdout.WriteLine($"rbctl {Version}");
            return CliExitCodes.Ok;
        }

        var groupName = args.Next();
        if (groupName is null || groupName is "help")
        {
            stdout.WriteLine(Help);
            return groupName is null && !args.Flag("help") ? CliExitCodes.Usage : CliExitCodes.Ok;
        }

        if (!Groups.TryGetValue(groupName, out var group))
        {
            output.Error($"Nhóm lệnh không tồn tại: '{groupName}'.");
            stderr.WriteLine();
            stderr.WriteLine(Help);
            return CliExitCodes.Usage;
        }

        // --help không cần mở DB — trả lời ngay kể cả khi DB lỗi/đang migrate
        if (args.Flag("help"))
        {
            stdout.WriteLine(group.Help);
            return CliExitCodes.Ok;
        }

        try
        {
            var environment = environmentFactory(args.Option("db"));
            var dbDirectory = Path.GetDirectoryName(environment.DatabasePath);
            if (!string.IsNullOrEmpty(dbDirectory)) Directory.CreateDirectory(dbDirectory);

            var appRunning = isAppRunning();
            CliServices.InitializeDatabase(environment, appRunning);
            await using var services = CliServices.Build(environment);
            var ctx = new CliContext(args, output, services, environment, appRunning, ct);
            return await group.Run(ctx);
        }
        catch (CliUsageException ex)
        {
            output.Error(ex.Message);
            return CliExitCodes.Usage;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            output.Error("Đã dừng.");
            return CliExitCodes.Cancelled;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            WriteKnownError(output, ex);
            return CliExitCodes.Error;
        }
    }

    public static string Version =>
        typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? typeof(CliApp).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>
    /// Lỗi "bình thường" của nghiệp vụ/IO in một dòng gọn. Exception khác (bug) để nổi lên
    /// kèm stack trace — nuốt chúng sẽ che lỗi thật.
    /// </summary>
    private static bool IsExpected(Exception ex) => ex is
        CliNotFoundException or KeyNotFoundException or ArgumentException or InvalidOperationException
        or ProviderValidationException or ComboValidationException or OpenCodeConfigFormatException
        or FreeModelSyncException or HttpRequestException or IOException or UnauthorizedAccessException
        or SqliteException or PlatformNotSupportedException
        or System.Security.Cryptography.CryptographicException;

    private static void WriteKnownError(CliOutput output, Exception ex)
    {
        switch (ex)
        {
            case ProviderValidationException validation:
                output.Error("dữ liệu provider không hợp lệ:");
                foreach (var (field, key) in validation.Errors)
                    output.Err.WriteLine($"  - {field}: {SettingsCommands.Translate(key)}");
                break;
            case PlatformNotSupportedException:
                output.Error(ex.Message);
                break;
            case System.Security.Cryptography.CryptographicException when OperatingSystem.IsWindows():
                output.Error($"không giải mã được API key ({ex.Message}). rbctl phải chạy cùng user Windows với app.");
                break;
            case System.Security.Cryptography.CryptographicException:
                // macOS/Linux: khoá AES nằm cạnh DB — sai/mất file khoá thì key cũ không đọc được nữa
                output.Error($"không giải mã được API key: {ex.Message} " +
                    $"(khoá: file {CliEnvironment.SecretKeyFileName} cạnh DB; mất khoá thì nhập lại key bằng `rbctl account update`).");
                break;
            case SqliteException sqlite when sqlite.SqliteErrorCode == 5:
                output.Error("DB đang bị khoá (app đang ghi). Thử lại sau giây lát.");
                break;
            default:
                output.Error(ex.Message);
                break;
        }

        if (output.Json)
            output.WriteJson(new { ok = false, error = ex.Message, type = ex.GetType().Name });
    }
}
