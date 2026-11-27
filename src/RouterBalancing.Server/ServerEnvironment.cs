using Microsoft.EntityFrameworkCore;
using RouterBalancing.Cli;
using RouterBalancing.Core.Security;
using RouterBalancing.Core.Storage;

namespace RouterBalancing.Server;

/// <summary>
/// Hạ tầng cho rb-server: DB factory + secret protector — CÙNG quy ước với rbctl
/// (<c>CliEnvironment</c>) để hai bên dùng chung một DB:
/// Windows: %AppData%\router-balancing + DPAPI; macOS/Linux: ~/.config/router-balancing
/// + AES-GCM với <c>secret.key</c> nằm cạnh file DB.
/// </summary>
internal sealed record ServerEnvironment(
    string DatabasePath,
    IDbContextFactory<RouterBalancingDbContext> Factory,
    ISecretProtector Protector)
{
    public const string SecretKeyFileName = "secret.key";

    public static ServerEnvironment CreateDefault(string? databasePathOverride)
    {
        var path = string.IsNullOrWhiteSpace(databasePathOverride)
            ? StoragePathProvider.GetDatabasePath()
            : Path.GetFullPath(databasePathOverride);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new DbContextOptionsBuilder<RouterBalancingDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        ISecretProtector protector = OperatingSystem.IsWindows()
            ? new DpapiSecretProtector()
            : new FileKeySecretProtector(Path.Combine(Path.GetDirectoryName(path)!, SecretKeyFileName));
        return new ServerEnvironment(path, new OptionsFactory(options), protector);
    }

    private sealed class OptionsFactory(DbContextOptions<RouterBalancingDbContext> options)
        : IDbContextFactory<RouterBalancingDbContext>
    {
        public RouterBalancingDbContext CreateDbContext() => new(options);
    }
}
