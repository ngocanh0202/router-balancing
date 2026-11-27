using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RouterBalancing.Cli;

/// <summary>
/// Ghi kết quả ra stdout dạng bảng (người đọc) hoặc JSON (<c>--json</c>, cho script/pipe).
/// Thông báo phụ (cảnh báo, gợi ý) luôn đi stderr để không làm bẩn JSON.
/// </summary>
internal sealed class CliOutput(TextWriter stdout, TextWriter stderr, bool json)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Giữ nguyên tiếng Việt / ký tự đặc biệt trong tên provider, không escape \uXXXX
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Enum giữ nguyên tên C# (OpenAI, RoundRobin…) — camelCase sẽ ra "openAI" khó đọc
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public TextWriter Out { get; } = stdout;

    public TextWriter Err { get; } = stderr;

    public bool Json { get; } = json;

    public void WriteJson<T>(T value) => Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    /// <summary>Dòng thông tin cho người đọc — bị tắt ở chế độ JSON.</summary>
    public void Line(string text = "")
    {
        if (!Json) Out.WriteLine(text);
    }

    public void Warn(string text) => Err.WriteLine($"! {text}");

    public void Error(string text) => Err.WriteLine($"Lỗi: {text}");

    /// <summary>In thông báo thành công; JSON mode in object <c>{ ok, message, ... }</c>.</summary>
    public void Done(string message, object? data = null)
    {
        if (Json)
        {
            WriteJson(new { ok = true, message, data });
            return;
        }
        Out.WriteLine(message);
    }

    /// <summary>
    /// In bảng căn cột bằng khoảng trắng. Độ rộng tính theo số char — đủ cho tên ASCII/tiếng Việt
    /// dựng sẵn (precomposed); ký tự rộng CJK có thể lệch cột, chấp nhận được cho CLI.
    /// </summary>
    public void Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var materialized = rows.Select(r => r.Select(c => Clean(c)).ToArray()).ToList();
        if (materialized.Count == 0)
        {
            Out.WriteLine("(trống)");
            return;
        }

        var widths = headers.Select(h => h.Length).ToArray();
        foreach (var row in materialized)
        {
            for (var i = 0; i < widths.Length && i < row.Length; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);
        }

        Out.WriteLine(FormatRow(headers.ToArray(), widths));
        Out.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in materialized)
            Out.WriteLine(FormatRow(row, widths));
    }

    /// <summary>In cặp key: value thẳng hàng (cho lệnh show/status).</summary>
    public void Pairs(IEnumerable<(string Key, string? Value)> pairs)
    {
        var list = pairs.ToList();
        var width = list.Count == 0 ? 0 : list.Max(p => p.Key.Length);
        foreach (var (key, value) in list)
            Out.WriteLine($"{key.PadRight(width)} : {Clean(value)}");
    }

    private static string FormatRow(string[] cells, int[] widths)
    {
        var parts = new string[widths.Length];
        for (var i = 0; i < widths.Length; i++)
        {
            var cell = i < cells.Length ? cells[i] : string.Empty;
            // Cột cuối không pad để tránh khoảng trắng thừa cuối dòng
            parts[i] = i == widths.Length - 1 ? cell : cell.PadRight(widths[i]);
        }
        return string.Join("  ", parts).TrimEnd();
    }

    // Xuống dòng trong message log/test làm vỡ bảng — gộp về một dòng
    private static string Clean(string? value) =>
        string.IsNullOrEmpty(value) ? "-" : value.Replace("\r", " ").Replace("\n", " ");
}
