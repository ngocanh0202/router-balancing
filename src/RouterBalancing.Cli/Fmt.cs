using System.Globalization;

namespace RouterBalancing.Cli;

/// <summary>Định dạng giá trị cho bảng text (JSON giữ nguyên kiểu gốc, không qua đây).</summary>
internal static class Fmt
{
    public static string YesNo(bool value) => value ? "yes" : "no";

    public static string? TestStatus(bool? success) => success switch
    {
        true => "ok",
        false => "fail",
        null => null,
    };

    /// <summary>Thời điểm theo giờ máy — người dùng đọc log theo múi giờ của họ.</summary>
    public static string? Time(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string? Number(long? value) =>
        value?.ToString("N0", CultureInfo.InvariantCulture);

    public static string? Number(int? value) =>
        value?.ToString("N0", CultureInfo.InvariantCulture);

    public static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }
}
