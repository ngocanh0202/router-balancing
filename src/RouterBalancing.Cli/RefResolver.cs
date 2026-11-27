using System.Globalization;
using RouterBalancing.Core.Domain;

namespace RouterBalancing.Cli;

/// <summary>
/// Chuyển tham chiếu người dùng gõ (id số, identifier, tên) thành entity. Thuần — không I/O,
/// để test được mà không cần DB.
/// </summary>
internal static class RefResolver
{
    /// <summary>
    /// Thứ tự ưu tiên: id số → identifier → tên. Identifier thắng tên vì nó là khoá ổn định
    /// dùng trong model key (<c>identifier/modelId</c>) và opencode.json.
    /// </summary>
    public static Provider Provider(IReadOnlyList<Provider> providers, string reference)
    {
        var trimmed = reference.Trim();
        if (TryParseId(trimmed, out var id))
        {
            return providers.FirstOrDefault(p => p.Id == id)
                ?? throw new CliNotFoundException($"Không có provider id {id}.");
        }

        var byIdentifier = providers.FirstOrDefault(p =>
            string.Equals(p.Identifier, trimmed, StringComparison.OrdinalIgnoreCase));
        if (byIdentifier is not null) return byIdentifier;

        var byName = providers.Where(p =>
            string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count switch
        {
            1 => byName[0],
            0 => throw new CliNotFoundException($"Không tìm thấy provider '{reference}'. Dùng `rbctl provider list` để xem id/identifier."),
            _ => throw new CliNotFoundException($"Tên '{reference}' khớp nhiều provider — dùng id hoặc identifier."),
        };
    }

    /// <summary>
    /// Model theo id DB, hoặc <c>&lt;provider&gt;/&lt;modelId&gt;</c>. Tách ở dấu '/' ĐẦU TIÊN:
    /// identifier là slug không chứa '/', còn modelId thì có thể (vd <c>meta/llama-3.1-8b</c>).
    /// </summary>
    public static Model Model(IReadOnlyList<Provider> providers, string reference)
    {
        var trimmed = reference.Trim();
        if (TryParseId(trimmed, out var id))
        {
            foreach (var provider in providers)
            {
                var hit = provider.Models.FirstOrDefault(m => m.Id == id);
                if (hit is not null)
                {
                    hit.Provider ??= provider;
                    return hit;
                }
            }
            throw new CliNotFoundException($"Không có model id {id}.");
        }

        var slash = trimmed.IndexOf('/');
        if (slash <= 0 || slash == trimmed.Length - 1)
        {
            throw new CliUsageException(
                $"Tham chiếu model '{reference}' không hợp lệ — dùng id số hoặc <provider>/<modelId>.");
        }

        var owner = Provider(providers, trimmed[..slash]);
        var modelId = trimmed[(slash + 1)..];
        var model = owner.Models.FirstOrDefault(m => string.Equals(m.ModelId, modelId, StringComparison.Ordinal))
            ?? owner.Models.FirstOrDefault(m => string.Equals(m.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliNotFoundException($"Provider '{owner.Name}' không có model '{modelId}'.");
        model.Provider ??= owner;
        return model;
    }

    public static Combo Combo(IReadOnlyList<Combo> combos, string reference)
    {
        var trimmed = reference.Trim();
        if (TryParseId(trimmed, out var id))
        {
            return combos.FirstOrDefault(c => c.Id == id)
                ?? throw new CliNotFoundException($"Không có combo id {id}.");
        }
        return combos.FirstOrDefault(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliNotFoundException($"Không tìm thấy combo '{reference}'.");
    }

    public static ClientKey ClientKey(IReadOnlyList<ClientKey> keys, string reference)
    {
        var trimmed = reference.Trim();
        if (TryParseId(trimmed, out var id))
        {
            return keys.FirstOrDefault(k => k.Id == id)
                ?? throw new CliNotFoundException($"Không có client key id {id}.");
        }
        var byName = keys.Where(k => string.Equals(k.Name, trimmed, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count switch
        {
            1 => byName[0],
            0 => throw new CliNotFoundException($"Không tìm thấy client key '{reference}'."),
            _ => throw new CliNotFoundException($"Tên '{reference}' khớp nhiều client key — dùng id."),
        };
    }

    /// <summary>Account theo id, hoặc <c>&lt;provider&gt;/&lt;tên account&gt;</c> (tên duy nhất trong provider).</summary>
    public static (Provider Provider, ProviderAccount Account) Account(IReadOnlyList<Provider> providers, string reference)
    {
        var trimmed = reference.Trim();
        if (TryParseId(trimmed, out var id))
        {
            foreach (var provider in providers)
            {
                var hit = provider.Accounts.FirstOrDefault(a => a.Id == id);
                if (hit is not null) return (provider, hit);
            }
            throw new CliNotFoundException($"Không có account id {id}.");
        }

        var slash = trimmed.IndexOf('/');
        if (slash <= 0 || slash == trimmed.Length - 1)
            throw new CliUsageException($"Tham chiếu account '{reference}' không hợp lệ — dùng id hoặc <provider>/<tên account>.");

        var owner = Provider(providers, trimmed[..slash]);
        var name = trimmed[(slash + 1)..];
        var account = owner.Accounts.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliNotFoundException($"Provider '{owner.Name}' không có account '{name}'.");
        return (owner, account);
    }

    /// <summary>Id hợp lệ là số nguyên dương — "0"/"-1" coi như tên.</summary>
    internal static bool TryParseId(string value, out long id) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}
