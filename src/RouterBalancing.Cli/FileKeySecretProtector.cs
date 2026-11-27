using System.Security.Cryptography;
using System.Text;
using RouterBalancing.Core.Security;

namespace RouterBalancing.Cli;

/// <summary>
/// Mã hoá API key bằng AES-256-GCM với khoá ngẫu nhiên lưu trong file chỉ chủ sở hữu đọc được
/// (0600) — thay DPAPI trên macOS/Linux, nơi <see cref="DpapiSecretProtector"/> không chạy.
/// </summary>
/// <remarks>
/// Bảo vệ tương đương DPAPI CurrentUser ở mức "user khác trên máy không đọc được": ai đọc
/// được file khoá (cùng user, root) giải mã được key. Giá trị mã hoá mang tiền tố
/// <see cref="Prefix"/> để phân biệt với chuỗi DPAPI của DB Windows bị chép sang.
/// </remarks>
internal sealed class FileKeySecretProtector : ISecretProtector
{
    /// <summary>Tiền tố phiên bản định dạng: <c>rbaes1:</c> + base64(nonce ‖ ciphertext ‖ tag).</summary>
    public const string Prefix = "rbaes1:";

    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // Associated data trùng entropy của DpapiSecretProtector: chuỗi mã hoá gắn với mục đích "keys"
    // của app này, không dùng lại được ở ngữ cảnh khác cùng khoá.
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("router-balancing/keys");

    private readonly Lazy<byte[]> _key;

    /// <param name="keyPath">File khoá; chưa có thì tạo mới khi mã hoá/giải mã lần đầu.</param>
    public FileKeySecretProtector(string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        KeyPath = Path.GetFullPath(keyPath);
        // Lazy: lệnh chỉ đọc (list, logs…) không tạo file khoá khi không đụng tới key
        _key = new Lazy<byte[]>(LoadOrCreateKey, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string KeyPath { get; }

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        EnsureSupported();

        var data = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + data.Length + TagSize];
        var nonce = buffer.AsSpan(0, NonceSize);
        // Nonce ngẫu nhiên 96-bit cho mỗi lần mã hoá — số key cần mã hoá rất nhỏ, xác suất trùng không đáng kể
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key.Value, TagSize);
        aes.Encrypt(nonce, data, buffer.AsSpan(NonceSize, data.Length),
            buffer.AsSpan(NonceSize + data.Length, TagSize), Purpose);
        return Prefix + Convert.ToBase64String(buffer);
    }

    /// <inheritdoc />
    /// <exception cref="CryptographicException">Sai định dạng, sai khoá hoặc dữ liệu bị sửa.</exception>
    public string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        EnsureSupported();

        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Key không ở định dạng của rbctl trên macOS/Linux (có thể là key DPAPI từ DB Windows) — nhập lại key bằng `rbctl account update`.");
        }

        byte[] buffer;
        try
        {
            buffer = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Chuỗi key mã hoá bị hỏng (không phải base64).", ex);
        }

        if (buffer.Length < NonceSize + TagSize)
            throw new CryptographicException("Chuỗi key mã hoá bị hỏng (quá ngắn).");

        var cipherLength = buffer.Length - NonceSize - TagSize;
        var plaintext = new byte[cipherLength];
        using var aes = new AesGcm(_key.Value, TagSize);
        // Sai khoá/dữ liệu bị sửa → AuthenticationTagMismatchException (kế thừa CryptographicException)
        aes.Decrypt(buffer.AsSpan(0, NonceSize), buffer.AsSpan(NonceSize, cipherLength),
            buffer.AsSpan(NonceSize + cipherLength, TagSize), plaintext, Purpose);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static void EnsureSupported()
    {
        if (!AesGcm.IsSupported)
            throw new PlatformNotSupportedException("Hệ điều hành này không hỗ trợ AES-GCM.");
    }

    private byte[] LoadOrCreateKey()
    {
        if (!File.Exists(KeyPath)) CreateKeyFile();
        return ReadKeyFile();
    }

    /// <summary>
    /// Ghi khoá vào file tạm (0600) rồi rename không ghi đè: hai process rbctl chạy cùng lúc
    /// lần đầu sẽ không bao giờ đọc phải file khoá ghi dở, và chỉ một khoá thắng.
    /// </summary>
    private void CreateKeyFile()
    {
        var directory = Path.GetDirectoryName(KeyPath)!;
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var key = RandomNumberGenerator.GetBytes(KeySize);
        var tempPath = $"{KeyPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            using (var stream = new FileStream(tempPath, options))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(Convert.ToBase64String(key));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(tempPath, KeyPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(KeyPath))
            {
                // Process khác vừa tạo khoá trước — dùng khoá của nó, bỏ bản của mình
            }
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private byte[] ReadKeyFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode groupOrOther =
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            // Như ssh với private key: file khoá ai cũng đọc được thì coi như đã lộ — từ chối dùng
            if ((File.GetUnixFileMode(KeyPath) & groupOrOther) != 0)
                throw new CryptographicException($"File khoá {KeyPath} đang cho user khác truy cập. Chạy: chmod 600 \"{KeyPath}\"");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(File.ReadAllText(KeyPath).Trim());
        }
        catch (FormatException ex)
        {
            throw new CryptographicException($"File khoá {KeyPath} bị hỏng.", ex);
        }

        return key.Length == KeySize
            ? key
            : throw new CryptographicException($"File khoá {KeyPath} bị hỏng (sai độ dài).");
    }
}
