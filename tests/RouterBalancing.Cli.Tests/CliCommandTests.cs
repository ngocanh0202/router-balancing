using System.Text.Json;
using RouterBalancing.Cli;
using RouterBalancing.Core.Security;

namespace RouterBalancing.Cli.Tests;

/// <summary>
/// Chạy rbctl end-to-end trên DB SQLite tạm với service Core thật — bắt lỗi wiring DI
/// (CliServices) và luồng lệnh, không đụng %AppData% hay DPAPI.
/// </summary>
public sealed class CliCommandTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<(int Code, string Out, string Err)> RunAsync(params string[] argv)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CliApp.RunAsync(
            argv, stdout, stderr,
            _ => new CliEnvironment(_db.DbPath, _db.CreateFactory(), new ReversibleProtector()),
            isAppRunning: () => false,
            CancellationToken.None);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task Help_DoesNotTouchDatabase()
    {
        var (code, output, _) = await RunAsync("--help");

        Assert.Equal(0, code);
        Assert.Contains("rbctl", output);
        Assert.False(File.Exists(_db.DbPath));
    }

    [Fact]
    public async Task UnknownGroup_ReturnsUsageCode()
    {
        var (code, _, err) = await RunAsync("bogus");

        Assert.Equal(2, code);
        Assert.Contains("bogus", err);
    }

    [Fact]
    public async Task ProviderAdd_ThenList_ShowsProviderWithSlugIdentifier()
    {
        var add = await RunAsync("provider", "add", "--name", "Nhà Cung Cấp", "--base-url", "https://api.example.com/v1",
            "--api-key", "sk-test", "--header", "X-Org=a=b");
        Assert.Equal(0, add.Code);

        var (code, output, _) = await RunAsync("provider", "list", "--json");

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        var created = doc.RootElement.EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "Nhà Cung Cấp");
        Assert.Equal("nha-cung-cap", created.GetProperty("identifier").GetString());
        Assert.Equal(1, created.GetProperty("accounts").GetInt32());
    }

    [Fact]
    public async Task ProviderAdd_InvalidBaseUrl_ReportsTranslatedValidationError()
    {
        var (code, _, err) = await RunAsync("provider", "add", "--name", "Bad", "--base-url", "ftp://x", "--no-key");

        Assert.Equal(1, code);
        Assert.Contains("BaseUrl", err);
    }

    [Fact]
    public async Task ProviderUpdate_KeepsUnspecifiedFieldsAndHeaders()
    {
        await RunAsync("provider", "add", "--name", "Keep", "--identifier", "keep", "--base-url", "https://k/v1",
            "--no-key", "--header", "X-A=1", "--max-concurrent", "7");

        var update = await RunAsync("provider", "update", "keep", "--header", "X-B=2");
        Assert.Equal(0, update.Code);

        var (_, output, _) = await RunAsync("provider", "show", "keep", "--json");
        using var doc = JsonDocument.Parse(output);
        Assert.Equal(7, doc.RootElement.GetProperty("summary").GetProperty("maxConcurrent").GetInt32());
        var headers = doc.RootElement.GetProperty("headers").EnumerateArray()
            .Select(h => h.GetProperty("name").GetString()).OrderBy(n => n).ToArray();
        Assert.Equal(["X-A", "X-B"], headers);
    }

    [Fact]
    public async Task ProviderDelete_WithYes_RemovesProvider()
    {
        await RunAsync("provider", "add", "--name", "Del", "--identifier", "del", "--base-url", "https://d/v1", "--no-key");

        var delete = await RunAsync("provider", "delete", "del", "--yes");
        Assert.Equal(0, delete.Code);

        var show = await RunAsync("provider", "show", "del");
        Assert.Equal(1, show.Code);
    }

    [Fact]
    public async Task ModelAdd_ComboAdd_ShowListsMembersInOrder()
    {
        await RunAsync("provider", "add", "--name", "Local", "--identifier", "local", "--base-url", "http://127.0.0.1:1/v1", "--no-key");
        var add = await RunAsync("model", "add", "local", "alpha", "beta/with-slash");
        Assert.Equal(0, add.Code);

        var combo = await RunAsync("combo", "add", "duo", "--mode", "fallback", "local/beta/with-slash", "local/alpha");
        Assert.Equal(0, combo.Code);

        var (code, output, _) = await RunAsync("combo", "show", "duo", "--json");
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal("fallback", doc.RootElement.GetProperty("mode").GetString());
        var labels = doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("label").GetString()).ToArray();
        Assert.Equal(["local/beta/with-slash", "local/alpha"], labels);
    }

    [Fact]
    public async Task ModelDisable_ThenListEnabledFalse_ShowsModel()
    {
        await RunAsync("provider", "add", "--name", "M", "--identifier", "m", "--base-url", "http://127.0.0.1:1/v1", "--no-key");
        await RunAsync("model", "add", "m", "one", "two");

        Assert.Equal(0, (await RunAsync("model", "disable", "m/one")).Code);

        var (_, output, _) = await RunAsync("model", "list", "m", "--enabled", "false", "--json");
        using var doc = JsonDocument.Parse(output);
        Assert.Equal(["one"], doc.RootElement.EnumerateArray().Select(m => m.GetProperty("modelId").GetString()).ToArray());
    }

    [Fact]
    public async Task AccountAdd_Update_KeepsKeyWhenApiKeyOmitted()
    {
        await RunAsync("provider", "add", "--name", "Acc", "--identifier", "acc", "--base-url", "https://a/v1", "--api-key", "k1");
        Assert.Equal(0, (await RunAsync("account", "add", "acc", "--name", "second", "--api-key", "k2", "--patterns", "gpt*")).Code);

        Assert.Equal(0, (await RunAsync("account", "update", "acc/second", "--weight", "25", "--patterns", "none")).Code);

        var (_, output, _) = await RunAsync("account", "list", "acc", "--json");
        using var doc = JsonDocument.Parse(output);
        var second = doc.RootElement.EnumerateArray().Single(a => a.GetProperty("name").GetString() == "second");
        Assert.True(second.GetProperty("hasKey").GetBoolean());
        Assert.Equal(25, second.GetProperty("weight").GetInt32());
        Assert.Equal(0, second.GetProperty("modelPatterns").GetArrayLength());
    }

    [Fact]
    public async Task KeyCreate_PrintsPlaintextOnce_ListShowsMaskOnly()
    {
        var (code, output, _) = await RunAsync("key", "create", "laptop", "--rpm", "60", "--json");
        Assert.Equal(0, code);
        using var created = JsonDocument.Parse(output);
        var plaintext = created.RootElement.GetProperty("apiKey").GetString();
        Assert.False(string.IsNullOrEmpty(plaintext));

        var list = await RunAsync("key", "list");
        Assert.Contains("laptop", list.Out);
        Assert.DoesNotContain(plaintext!, list.Out);
    }

    [Fact]
    public async Task SettingsSet_OutOfRange_IsRejectedAndNotPersisted()
    {
        // Không hard-code port mặc định: bản Debug của Core dùng 8417, Release dùng 8317
        var before = (await RunAsync("settings", "get", "port")).Out.Trim();

        var (code, _, err) = await RunAsync("settings", "set", "port", "80");
        Assert.Equal(2, code);
        Assert.Contains("port", err);

        var after = await RunAsync("settings", "get", "port");
        Assert.Equal(before, after.Out.Trim());
    }

    [Fact]
    public async Task SettingsSet_ValidValue_Persists()
    {
        Assert.Equal(0, (await RunAsync("settings", "set", "transientMaxRetries", "2")).Code);

        var get = await RunAsync("settings", "get", "transientMaxRetries");
        Assert.Equal("2", get.Out.Trim());
    }

    [Fact]
    public async Task OpenCodeGenerate_WithExplicitModel_PrintsJsonWithBaseUrl()
    {
        await RunAsync("provider", "add", "--name", "Oc", "--identifier", "oc", "--base-url", "https://oc/v1", "--no-key");
        await RunAsync("model", "add", "oc", "coder");

        var (code, output, _) = await RunAsync("opencode", "generate", "--models", "oc/coder", "--base-url", "http://127.0.0.1:9999/v1");

        Assert.Equal(0, code);
        Assert.Contains("http://127.0.0.1:9999/v1", output);
        Assert.Contains("oc/coder", output);
        JsonDocument.Parse(output).Dispose();
    }

    [Fact]
    public async Task Logs_And_Stats_RunOnEmptyDatabase()
    {
        Assert.Equal(0, (await RunAsync("logs", "--limit", "5")).Code);
        Assert.Equal(0, (await RunAsync("stats", "7d", "--json")).Code);
    }

    [Fact]
    public async Task Status_Json_ReportsCounts()
    {
        var (_, output, _) = await RunAsync("status", "--json");

        using var doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("appRunning").GetBoolean());
        // DbInitializer seed các provider preset free (tắt sẵn)
        Assert.True(doc.RootElement.GetProperty("providers").GetProperty("total").GetInt32() > 0);
    }

    /// <summary>Protector đảo ngược được, không cần DPAPI — chạy được trên mọi OS.</summary>
    private sealed class ReversibleProtector : ISecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;

        public string Unprotect(string protectedValue) => protectedValue["enc:".Length..];
    }
}
