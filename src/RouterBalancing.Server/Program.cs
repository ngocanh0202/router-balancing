using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using RouterBalancing.Core.Logging;
using RouterBalancing.Core.Server;
using RouterBalancing.Core.Settings;
using RouterBalancing.Core.Storage;
using RouterBalancing.Server;

// rb-server — proxy Router Balancing chạy không giao diện (Linux/macOS/server).
// Đọc cấu hình (provider, account, combo, client key, settings) từ cùng DB với rbctl;
// đổi provider/model/combo bằng rbctl có hiệu lực ngay (proxy đọc DB mỗi request),
// riêng settings và client key cần khởi động lại rb-server (cache RAM — như app desktop).

string? dbOverride = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--db" when i + 1 < args.Length:
            dbOverride = args[++i];
            break;
        case "-h" or "--help":
            PrintHelp();
            return 0;
        case "-v" or "--version":
            Console.WriteLine(typeof(ServerServices).Assembly.GetName().Version?.ToString(3));
            return 0;
        default:
            Console.Error.WriteLine($"Tham số không hợp lệ: {args[i]}");
            PrintHelp();
            return 2;
    }
}

var env = ServerEnvironment.CreateDefault(dbOverride);
try
{
    // Migrate + seed như app/rbctl khi khởi động
    DbInitializer.Initialize(env.Factory, env.Protector);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"rb-server: khởi tạo DB lỗi ({env.DatabasePath}): {ex.Message}");
    return 1;
}

await using var services = ServerServices.Build(env);
var host = services.GetRequiredService<IProxyHost>();
var retention = services.GetRequiredService<LogRetentionWorker>();

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.Cancel(); });

retention.Start();
try
{
    await host.StartAsync(stop.Token);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"rb-server: không khởi động được proxy: {ex.Message}");
    return 1;
}

var configuredPort = services.GetRequiredService<IAppSettingsService>().Port;
if (host.Port != configuredPort)
{
    Console.WriteLine($"rb-server: cổng {configuredPort} (settings) đang bận/bị giữ — dùng cổng {host.Port}. " +
                      "Đổi bằng: rbctl settings set port <cổng> rồi chạy lại rb-server.");
}
Console.WriteLine($"rb-server: proxy đang chạy tại http://127.0.0.1:{host.Port}/v1  (DB: {env.DatabasePath})");
Console.WriteLine($"LISTENING {host.Port}");
Console.WriteLine("Quản lý bằng rbctl (provider/account/combo/key …). Ctrl+C để dừng.");

try
{
    await Task.Delay(Timeout.Infinite, stop.Token);
}
catch (OperationCanceledException)
{
}

Console.WriteLine("rb-server: đang dừng…");
await host.StopAsync();
return 0;

static void PrintHelp() => Console.WriteLine("""
    rb-server — chạy proxy Router Balancing không giao diện (OpenAI-compatible /v1)

    Cách dùng: rb-server [--db <đường dẫn DB>]

      --db <path>     File SQLite (mặc định giống rbctl: ~/.config/router-balancing/router-balancing.db
                      trên macOS/Linux, %AppData%\router-balancing trên Windows)
      -v, --version   In phiên bản
      -h, --help      Trợ giúp

    Cổng/LAN lấy từ settings: rbctl settings set port 8317 (khởi động lại rb-server để áp dụng).
    """);
