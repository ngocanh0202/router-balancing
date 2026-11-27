using Microsoft.Extensions.DependencyInjection;
using RouterBalancing.Core.Capabilities;
using RouterBalancing.Core.Combos;
using RouterBalancing.Core.Dataset;
using RouterBalancing.Core.Engine;
using RouterBalancing.Core.Logging;
using RouterBalancing.Core.Providers;
using RouterBalancing.Core.Proxies;
using RouterBalancing.Core.Security;
using RouterBalancing.Core.Server;
using RouterBalancing.Core.Settings;
using RouterBalancing.Core.Stats;
using RouterBalancing.Core.Tools;
using RouterBalancing.Core.Usage;

namespace RouterBalancing.Server;

/// <summary>
/// Container DI cho rb-server — bản rút gọn của <c>MauiProgram.CreateMauiApp</c>: giữ service
/// nghiệp vụ (giống <c>CliServices</c> của rbctl) + các singleton mà <see cref="ProxyHost"/> chia sẻ
/// với container proxy (trace feed, API monitor, dataset, drain gate, journal), bỏ UI/tray/auto-update.
/// </summary>
internal static class ServerServices
{
    public static ServiceProvider Build(ServerEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton(env.Factory);
        services.AddSingleton(env.Protector);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(TimeZoneInfo.Local);

        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IProviderQuotaGate, ProviderQuotaGate>();
        services.AddSingleton<IStatsService, StatsService>();
        services.AddSingleton<IUsageDailyService, UsageDailyService>();

        // Singleton ProxyHost đăng ký lại ĐÚNG instance vào container proxy (giống app desktop)
        services.AddSingleton<ITraceFeed, TraceFeed>();
        services.AddSingleton<IApiMonitorStore, ApiMonitorStore>();
        services.AddSingleton<IDatasetSink, DatasetSink>();
        services.AddSingleton<DrainGate>();
        services.AddSingleton<IStatsInvalidationSource, StatsInvalidationSource>();
        services.AddSingleton<IRequestJournalWriter, RequestJournalWriter>();
        services.AddSingleton<IProxyHost, ProxyHost>();
        services.AddSingleton<LogRetentionWorker>();

        // Outbound proxy (spec proxy-pool §6) — cùng pipeline HttpClient với app/rbctl
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
}
