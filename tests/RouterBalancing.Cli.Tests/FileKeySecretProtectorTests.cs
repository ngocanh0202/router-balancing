using System.Security.Cryptography;
using RouterBalancing.Cli;

namespace RouterBalancing.Cli.Tests;

public sealed class FileKeySecretProtectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rbctl-key-{Guid.NewGuid():N}");

    private string KeyPath => Path.Combine(_dir, "nested", "secret.key");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("sk-test-123")]
    [InlineData("")]
    [InlineData("khoá tiếng Việt 🔑")]
    public void Protect_ThenUnprotect_RoundTrips(string plaintext)
    {
        var protector = new FileKeySecretProtector(KeyPath);

        var protectedValue = protector.Protect(plaintext);

        Assert.StartsWith(FileKeySecretProtector.Prefix, protectedValue);
        // Ordinal: so sánh theo culture (ICU) coi ký tự điều khiển là "bỏ qua được" nên dễ báo khớp giả
        if (plaintext.Length > 0) Assert.DoesNotContain(plaintext, protectedValue, StringComparison.Ordinal);
        Assert.Equal(plaintext, protector.Unprotect(protectedValue));
    }

    [Fact]
    public void Protect_CreatesKeyFileOnce_SharedAcrossInstances()
    {
        var first = new FileKeySecretProtector(KeyPath).Protect("k");

        Assert.True(File.Exists(KeyPath));
        Assert.Equal("k", new FileKeySecretProtector(KeyPath).Unprotect(first));
    }

    [Fact]
    public void Protect_SamePlaintext_ProducesDifferentCiphertext()
    {
        var protector = new FileKeySecretProtector(KeyPath);

        Assert.NotEqual(protector.Protect("same"), protector.Protect("same"));
    }

    [Fact]
    public void Unprotect_TamperedValue_Throws()
    {
        var protector = new FileKeySecretProtector(KeyPath);
        var bytes = Convert.FromBase64String(protector.Protect("secret")[FileKeySecretProtector.Prefix.Length..]);
        bytes[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() =>
            protector.Unprotect(FileKeySecretProtector.Prefix + Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Unprotect_WithDifferentKeyFile_Throws()
    {
        var value = new FileKeySecretProtector(KeyPath).Protect("secret");
        var other = new FileKeySecretProtector(Path.Combine(_dir, "other.key"));

        Assert.ThrowsAny<CryptographicException>(() => other.Unprotect(value));
    }

    [Fact]
    public void Unprotect_DpapiStyleValue_ThrowsWithHint()
    {
        var protector = new FileKeySecretProtector(KeyPath);

        var ex = Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("AQAAANCMnd8BFdERjHoAwE"));
        Assert.Contains("rbctl account update", ex.Message);
    }

    [Fact]
    public async Task ConcurrentFirstUse_AllInstancesAgreeOnOneKey()
    {
        var values = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => new FileKeySecretProtector(KeyPath).Protect("z"))));

        var reader = new FileKeySecretProtector(KeyPath);
        Assert.All(values, v => Assert.Equal("z", reader.Unprotect(v)));
        // Không còn file tạm sót lại sau khi race tạo khoá
        Assert.Equal(["secret.key"], Directory.GetFiles(Path.GetDirectoryName(KeyPath)!).Select(Path.GetFileName));
    }

    [Fact]
    public void KeyFile_IsOwnerOnly_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Quyền file Unix không áp dụng trên Windows.");
        }
        else
        {
            new FileKeySecretProtector(KeyPath).Protect("k");

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyPath));
        }
    }

    [Fact]
    public void KeyFile_ReadableByOthers_IsRejected_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Quyền file Unix không áp dụng trên Windows.");
        }
        else
        {
            var value = new FileKeySecretProtector(KeyPath).Protect("k");
            File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

            var ex = Assert.ThrowsAny<CryptographicException>(() => new FileKeySecretProtector(KeyPath).Unprotect(value));
            Assert.Contains("chmod 600", ex.Message);
        }
    }
}
