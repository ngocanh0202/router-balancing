using Microsoft.EntityFrameworkCore;
using RouterBalancing.Core.Storage;

namespace RouterBalancing.Cli.Tests;

/// <summary>
/// DB SQLite tạm trong thư mục temp, mỗi test một file — bản riêng cho project này vì
/// TestDb của "router balancing test" là internal của assembly đó.
/// </summary>
internal sealed class TestDb : IDisposable
{
    public string DbPath { get; } =
        Path.Combine(Path.GetTempPath(), $"rbctl-test-{Guid.NewGuid():N}.db");

    public IDbContextFactory<RouterBalancingDbContext> CreateFactory()
    {
        // Pooling=False: pool giữ handle file sau Dispose → File.Delete ném IOException trên Windows
        var options = new DbContextOptionsBuilder<RouterBalancingDbContext>()
            .UseSqlite($"Data Source={DbPath};Pooling=False")
            .Options;
        return new SingleOptionsFactory(options);
    }

    public void Dispose()
    {
        if (!File.Exists(DbPath)) return;
        // Connection vừa đóng có thể còn nhả file chậm — thử lại có giới hạn, hết 2s thì để lỗi nổ
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Delete(DbPath);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }

    private sealed class SingleOptionsFactory(
        DbContextOptions<RouterBalancingDbContext> options) : IDbContextFactory<RouterBalancingDbContext>
    {
        public RouterBalancingDbContext CreateDbContext() => new(options);
    }
}
