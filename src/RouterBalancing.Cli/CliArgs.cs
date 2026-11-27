using System.Globalization;

namespace RouterBalancing.Cli;

/// <summary>
/// Lỗi cú pháp dòng lệnh (thiếu tham số, giá trị sai định dạng) — Program trả exit code 2.
/// </summary>
internal sealed class CliUsageException(string message) : Exception(message);

/// <summary>
/// Kết quả parse argv: positional theo thứ tự, option có giá trị (lặp được) và cờ boolean.
/// </summary>
/// <remarks>
/// Tự viết thay vì System.CommandLine: CLI không được kéo thêm package NuGet nào
/// (build offline chỉ dựa vào cache của Core) và cú pháp ở đây đủ đơn giản.
/// </remarks>
internal sealed class CliArgs
{
    /// <summary>
    /// Option không nhận giá trị. Mọi <c>--x</c> khác đều đòi giá trị (<c>--x v</c> hoặc <c>--x=v</c>).
    /// </summary>
    internal static readonly IReadOnlySet<string> BooleanFlags = new HashSet<string>(StringComparer.Ordinal)
    {
        "json", "yes", "help", "no-key", "all", "version", "clear-headers", "save-selection", "follow",
    };

    private readonly List<string> _positionals;
    private readonly Dictionary<string, List<string>> _options;
    private readonly HashSet<string> _flags;
    private int _cursor;

    private CliArgs(List<string> positionals, Dictionary<string, List<string>> options, HashSet<string> flags)
    {
        _positionals = positionals;
        _options = options;
        _flags = flags;
    }

    /// <summary>Positional còn lại chưa bị <see cref="Next"/> lấy.</summary>
    public IReadOnlyList<string> Remaining => _positionals.Skip(_cursor).ToList();

    /// <summary>Parse argv. <c>--</c> kết thúc phần option: mọi token sau đó là positional.</summary>
    /// <exception cref="CliUsageException">Option đòi giá trị nhưng không có.</exception>
    public static CliArgs Parse(IReadOnlyList<string> argv)
    {
        var positionals = new List<string>();
        var options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var onlyPositionals = false;

        for (var i = 0; i < argv.Count; i++)
        {
            var token = argv[i];
            if (onlyPositionals || !token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2)
            {
                if (!onlyPositionals && token == "--")
                {
                    onlyPositionals = true;
                    continue;
                }
                // -h là alias quen thuộc của --help
                if (!onlyPositionals && token == "-h")
                {
                    flags.Add("help");
                    continue;
                }
                positionals.Add(token);
                continue;
            }

            var body = token[2..];
            string name;
            string? value = null;
            var eq = body.IndexOf('=');
            if (eq >= 0)
            {
                name = body[..eq];
                value = body[(eq + 1)..];
            }
            else
            {
                name = body;
            }

            if (BooleanFlags.Contains(name))
            {
                if (value is not null && !IsTrue(value))
                {
                    flags.Remove(name);
                    continue;
                }
                flags.Add(name);
                continue;
            }

            if (value is null)
            {
                if (i + 1 >= argv.Count)
                    throw new CliUsageException($"Option --{name} cần một giá trị (hoặc không phải option hợp lệ).");
                value = argv[++i];
            }

            if (!options.TryGetValue(name, out var list))
            {
                list = [];
                options[name] = list;
            }
            list.Add(value);
        }

        return new CliArgs(positionals, options, flags);
    }

    /// <summary>Lấy positional kế tiếp; <see langword="null"/> khi đã hết.</summary>
    public string? Next() => _cursor < _positionals.Count ? _positionals[_cursor++] : null;

    /// <summary>Lấy positional bắt buộc kế tiếp.</summary>
    /// <exception cref="CliUsageException">Khi đã hết positional.</exception>
    public string Require(string what) =>
        Next() ?? throw new CliUsageException($"Thiếu tham số <{what}>.");

    /// <summary>Báo lỗi nếu còn positional thừa — tránh lệnh gõ nhầm âm thầm bị bỏ qua.</summary>
    public void EnsureNoMorePositionals()
    {
        if (_cursor < _positionals.Count)
            throw new CliUsageException($"Tham số thừa: {string.Join(' ', _positionals.Skip(_cursor))}");
    }

    public bool Flag(string name) => _flags.Contains(name);

    public bool Has(string name) => _options.ContainsKey(name);

    /// <summary>Giá trị cuối cùng của option (last-wins như đa số CLI).</summary>
    public string? Option(string name) =>
        _options.TryGetValue(name, out var list) ? list[^1] : null;

    /// <summary>Mọi giá trị của option lặp, kể cả dạng phân tách bằng dấu phẩy.</summary>
    public IReadOnlyList<string> OptionList(string name) =>
        _options.TryGetValue(name, out var list)
            ? list.SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList()
            : [];

    /// <summary>Mọi giá trị thô của option lặp (không tách dấu phẩy — dùng cho header có thể chứa ',').</summary>
    public IReadOnlyList<string> OptionValues(string name) =>
        _options.TryGetValue(name, out var list) ? list : [];

    public int? IntOption(string name)
    {
        var raw = Option(name);
        if (raw is null) return null;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new CliUsageException($"--{name} phải là số nguyên (nhận '{raw}').");
    }

    /// <summary>
    /// Int có thể "xoá": <c>none</c>/<c>off</c>/<c>0</c> → <see langword="null"/> (bỏ giới hạn).
    /// Trả về (đã truyền?, giá trị) để phân biệt "không đụng tới" với "xoá".
    /// </summary>
    public (bool Present, int? Value) NullableIntOption(string name)
    {
        var raw = Option(name);
        if (raw is null) return (false, null);
        if (raw is "none" or "off" or "0") return (true, null);
        return (true, IntOption(name));
    }

    /// <summary>Option boolean dạng giá trị (<c>--enabled true</c>), khác với cờ.</summary>
    public bool? BoolOption(string name)
    {
        var raw = Option(name);
        if (raw is null) return null;
        if (IsTrue(raw)) return true;
        if (IsFalse(raw)) return false;
        throw new CliUsageException($"--{name} phải là true/false (nhận '{raw}').");
    }

    /// <summary>Trả về tên các option không thuộc danh sách hợp lệ — bắt lỗi gõ sai tên option.</summary>
    public void EnsureOnlyOptions(params string[] allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal) { "json", "yes", "help", "db" };
        var unknown = _options.Keys.Concat(_flags).Where(k => !allowedSet.Contains(k)).ToList();
        if (unknown.Count > 0)
            throw new CliUsageException($"Option không hợp lệ cho lệnh này: {string.Join(", ", unknown.Select(u => "--" + u))}");
    }

    internal static bool IsTrue(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("on", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value == "1";

    internal static bool IsFalse(string value) =>
        value.Equals("false", StringComparison.OrdinalIgnoreCase)
        || value.Equals("off", StringComparison.OrdinalIgnoreCase)
        || value.Equals("no", StringComparison.OrdinalIgnoreCase)
        || value == "0";
}
