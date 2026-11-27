using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RouterBalancing.Core.Capabilities;
using RouterBalancing.Core.Combos;
using RouterBalancing.Core.Engine;
using RouterBalancing.Core.Logging;
using RouterBalancing.Core.Providers;
using RouterBalancing.Core.Proxies;
using RouterBalancing.Core.Security;
using RouterBalancing.Core.Server;
using RouterBalancing.Core.Settings;
using RouterBalancing.Core.Stats;
using RouterBalancing.Core.Storage;
using RouterBalancing.Core.Tools;
using RouterBalancing.Core.Usage;

namespace RouterBalancing.Cli;

/// <summary>
/// Đầu vào hạ tầng cho CLI: DB factory + secret protector. Tách ra để test thay bằng DB tạm
/// và protector giả mà không đụng %AppData% hay DPAPI.
/// </summary>
/// <param name="DatabasePath">Đường dẫn file SQLite (hiển thị ở <c>status</c>).</param>
/// <param name="Factory">Factory DbContext trỏ vào <paramref name="DatabasePath"/>.</param>
/// <param name="Protector">Mã hoá/giải mã API key provider (DPAPI ở bản thật).</param>
internal sealed record CliEnvironment(
    string DatabasePath,
    IDbContextFactory<RouterBalancingDbContext> Factory,
    ISecretProtector Protector)
{
    /// <summary>Tên file khoá AES (macOS/Linux), nằm cạnh file DB.</summary>
    public const string SecretKeyFileName = "secret.key";

    /// <summary>
    /// Môi trường thật: cùng file DB với app hoặc file chỉ định qua <c>--db</c>.
    /// Pooling=False để process CLI thoát là nhả file ngay cho app.
    /// </summary>
    /// <remarks>
    /// Windows: %AppData%\router-balancing + DPAPI — trùng app desktop nên dùng chung DB được.
    /// macOS/Linux: ApplicationData của .NET là <c>~/.config</c> (XDG) → DB riêng của CLI tại
    /// <c>~/.config/router-balancing</c>, key mã hoá AES-GCM bằng khoá <see cref="SecretKeyFileName"/>
    /// đặt cạnh DB để cặp DB/khoá luôn đi cùng nhau (kể cả khi dùng <c>--db</c>).
    /// </remarks>
    public static CliEnvironment CreateDefault(string? databasePathOverride)
    {
        var path = string.IsNullOrWhiteSpace(databasePathOverride)
            ? StoragePathProvider.GetDatabasePath()
            : Path.GetFullPath(databasePathOverride);
        var options = new DbContextOptionsBuilder<RouterBalancingDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;
        ISecretProtector protector = OperatingSystem.IsWindows()
            ? new DpapiSecretProtector()
            : new FileKeySecretProtector(Path.Combine(Path.GetDirectoryName(path)!, SecretKeyFileName));
        return new CliEnvironment(path, new OptionsFactory(options), protector);
    }

    private sealed class OptionsFactory(DbContextOptions<RouterBalancingDbContext> options)
        : IDbContextFactory<RouterBalancingDbContext>
    {
        public RouterBalancingDbContext CreateDbContext() => new(options);
    }
}

/// <summary>
/// Dựng container DI cho CLI — bản rút gọn của <c>MauiProgram.CreateMauiApp</c>: giữ nguyên
/// các service nghiệp vụ (Core) và pipeline HttpClient có outbound proxy, bỏ UI/tray/proxy host.
/// </summary>
internal static class CliServices
{
    public static ServiceProvider Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        var services = new ServiceCollection();

        services.AddSingleton(env.Factory);
        services.AddSingleton(env.Protector);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(TimeZoneInfo.Local);

        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<ILogService, LogService>();
        // Quota gate trong process CLI chỉ để thoả ctor service: window cooldown thật nằm
        // trong RAM của app đang chạy, CLI không xoá được (app tự hết hạn theo TTL).
        services.AddSingleton<IProviderQuotaGate, ProviderQuotaGate>();
        services.AddSingleton<IStatsService, StatsService>();
        services.AddSingleton<IUsageDailyService, UsageDailyService>();

        // Outbound proxy: test connection / fetch models phải đi cùng đường proxy như app
        // (spec proxy-pool §6), nếu không kết quả test CLI sẽ khác kết quả trong UI.
        services.AddSingleton<IProxyPool, ProxyPool>();
        services.AddSingleton<IProxyEchoClient, ProxyEchoClient>();
        services.AddSingleton<IProxySelectionResolver, ProxySelectionResolver>();
        services.AddSingleton<IProxyService, ProxyService>();
        services.AddTransient<ProxyHealthHandler>();
        services.AddTransient<ProviderProbeTimeoutHandler>();

        services.AddHttpClient(ProviderRequestFactory.HttpClientName,
                client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                Proxy = new RoundRobinWebProxy(),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(60),
            })
            .AddHttpMessageHandler<ProxyHealthHandler>()
            .AddHttpMessageHandler<ProviderProbeTimeoutHandler>();
        services.AddHttpClient(FreeModelSyncService.HttpClientName,
                client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                Proxy = new RoundRobinWebProxy(),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(60),
            })
            .AddHttpMessageHandler<ProxyHealthHandler>();

        services.AddSingleton<IProviderService, ProviderService>();
        services.AddSingleton<IProviderAccountService, ProviderAccountService>();
        services.AddSingleton<IModelService, ModelService>();
        services.AddSingleton<IFreeModelSyncService, FreeModelSyncService>();
        services.AddSingleton<IComboService, ComboService>();
        services.AddSingleton<IComboCapabilityCalculator, ComboCapabilityCalculator>();
        services.AddSingleton<IClientKeyService, ClientKeyService>();
        services.AddSingleton<IOpenCodeConfigService, OpenCodeConfigService>();
        services.AddSingleton<IOpenCodeConfigFileWriter, OpenCodeConfigFileWriter>();

        // Cùng thứ tự chain metadata với MauiProgram: nguồn giàu nhất trước.
        services.AddSingleton<IModelMetadataService, ModelMetadataService>();
        services.AddSingleton<IModelMetadataProvider, OllamaTagsMetadataProvider>();
        services.AddSingleton<IModelMetadataProvider, NvidiaCatalogMetadataProvider>();
        services.AddSingleton<IModelMetadataProvider, ProviderEndpointMetadataProvider>();
        services.AddSingleton<IModelCapabilityWriter, ModelCapabilityWriter>();
        services.AddSingleton<IModelCapabilityReader, ModelCapabilityReader>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    /// <summary>
    /// Migrate + seed như app khi khởi động. Chặn khi app đang chạy mà DB còn migration chờ:
    /// CLI mới hơn app sẽ đổi schema dưới chân process đang mở DB.
    /// </summary>
    /// <exception cref="InvalidOperationException">App đang chạy và DB cần migrate.</exception>
    public static void InitializeDatabase(CliEnvironment env, bool appRunning)
    {
        using (var db = env.Factory.CreateDbContext())
        {
            var pending = db.Database.GetPendingMigrations().ToList();
            if (pending.Count > 0 && appRunning)
            {
                throw new InvalidOperationException(
                    $"DB cần {pending.Count} migration nhưng app router-balancing đang chạy. " +
                    "Tắt app rồi chạy lại lệnh (hoặc cập nhật app cùng phiên bản với rbctl).");
            }
        }

        DbInitializer.Initialize(env.Factory, env.Protector);
    }
}
