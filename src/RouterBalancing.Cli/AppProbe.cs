using System.Net.Http.Json;
using System.Text.Json;

namespace RouterBalancing.Cli;

/// <summary>
/// Kiểm tra app desktop có đang chạy không — để cảnh báo khi CLI ghi DB mà app còn giữ cache cũ.
/// </summary>
internal static class AppProbe
{
    // Trùng tên mutex trong SingleInstanceGuard (bản Release và Debug) — app giữ mutex suốt vòng đời.
    private static readonly string[] MutexNames =
    [
        "router-balancing-single-instance",
        "router-balancing-single-instance-debug",
    ];

    /// <summary>
    /// <see langword="true"/> nếu một instance app (Release hoặc Debug) đang giữ mutex single-instance.
    /// Chỉ mở mutex đã tồn tại, không tạo mới — không bao giờ chặn app khởi động.
    /// </summary>
    public static bool IsAppRunning()
    {
        foreach (var name in MutexNames)
        {
            if (Mutex.TryOpenExisting(name, out var mutex))
            {
                mutex.Dispose();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gọi <c>GET /health</c> của proxy local. Trả về (ok, message) — không ném lỗi mạng ra ngoài
    /// vì "proxy không chạy" là trạng thái hợp lệ để báo cáo.
    /// </summary>
    public static async Task<(bool Ok, string Message)> CheckHealthAsync(int port, CancellationToken ct)
    {
        // Proxy chỉ nghe loopback; bỏ qua HTTPS_PROXY hệ thống để không đi vòng ra ngoài
        using var handler = new SocketsHttpHandler { UseProxy = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/health", ct);
            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP {(int)response.StatusCode}");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var status = body.TryGetProperty("status", out var s) ? s.GetString() : null;
            return (status == "ok", status ?? "không rõ");
        }
        catch (HttpRequestException ex)
        {
            return (false, ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, "timeout");
        }
        catch (JsonException)
        {
            return (false, "phản hồi /health không phải JSON");
        }
    }
}
