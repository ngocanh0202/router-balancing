using System.Text.Json;
using RouterBalancing.Cli;

namespace RouterBalancing.Cli.Tests;

/// <summary>
/// Chạy rbctl với môi trường thật (<see cref="CliEnvironment.CreateDefault"/>) trên DB tạm qua
/// <c>--db</c>: Windows dùng DPAPI, macOS/Linux dùng khoá AES cạnh DB — bắt lỗi chọn protector.
/// </summary>
public sealed class DefaultEnvironmentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rbctl-env-{Guid.NewGuid():N}");

    private string DbPath => Path.Combine(_dir, "router-balancing.db");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private async Task<(int Code, string Out, string Err)> RunAsync(params string[] argv)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CliApp.RunAsync(
            [.. argv, "--db", DbPath], stdout, stderr,
            CliEnvironment.CreateDefault,
            isAppRunning: () => false,
            CancellationToken.None);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task ProviderWithApiKey_IsStoredWithPlatformProtector()
    {
        var add = await RunAsync("provider", "add", "--name", "Env", "--identifier", "env",
            "--base-url", "https://env.example/v1", "--api-key", "sk-env");
        Assert.True(add.Code == 0, add.Err);

        var (code, output, err) = await RunAsync("account", "list", "env", "--json");
        Assert.True(code == 0, err);
        using var doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement[0].GetProperty("hasKey").GetBoolean());

        // macOS/Linux: khoá AES phải nằm cạnh DB, không phải ở ~/.config
        Assert.Equal(!OperatingSystem.IsWindows(), File.Exists(Path.Combine(_dir, CliEnvironment.SecretKeyFileName)));
    }

    [Fact]
    public async Task Status_ReportsKeyStorage()
    {
        var (_, output, _) = await RunAsync("status", "--json");

        using var doc = JsonDocument.Parse(output);
        var storage = doc.RootElement.GetProperty("keyStorage").GetString();
        Assert.StartsWith(OperatingSystem.IsWindows() ? "DPAPI" : "AES-GCM", storage);
    }
}
