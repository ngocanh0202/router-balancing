using Microsoft.Extensions.DependencyInjection;
using RouterBalancing.Core.Combos;
using RouterBalancing.Core.Domain;
using RouterBalancing.Core.Providers;
using RouterBalancing.Core.Server;

namespace RouterBalancing.Cli;

/// <summary>
/// Không tìm thấy đối tượng theo tham chiếu người dùng gõ (id/tên/identifier) — exit code 1.
/// </summary>
internal sealed class CliNotFoundException(string message) : Exception(message);

/// <summary>
/// Mọi thứ một lệnh cần: argv đã parse, output, service Core và trạng thái app desktop.
/// </summary>
internal sealed class CliContext
{
    public CliContext(CliArgs args, CliOutput output, IServiceProvider services,
        CliEnvironment environment, bool appRunning, CancellationToken cancellation)
    {
        Args = args;
        Output = output;
        Services = services;
        Environment = environment;
        AppRunning = appRunning;
        Cancellation = cancellation;
    }

    public CliArgs Args { get; }

    public CliOutput Output { get; }

    public IServiceProvider Services { get; }

    public CliEnvironment Environment { get; }

    /// <summary>App desktop đang chạy (giữ mutex single-instance) tại thời điểm CLI khởi động.</summary>
    public bool AppRunning { get; }

    public CancellationToken Cancellation { get; }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>
    /// Nhắc restart app khi thay đổi nằm trong cache RAM của app (settings, client key auth cache):
    /// các cache này chỉ được làm mới qua event trong cùng process, CLI là process khác.
    /// </summary>
    public void WarnRestartIfRunning(string what)
    {
        if (AppRunning)
            Output.Warn($"App router-balancing đang chạy: {what} chỉ có hiệu lực sau khi khởi động lại app.");
    }

    /// <summary>Hỏi xác nhận thao tác xoá trừ khi có <c>--yes</c>; stdin bị redirect thì bắt buộc <c>--yes</c>.</summary>
    /// <exception cref="CliUsageException">Không thể hỏi (stdin redirect) mà thiếu <c>--yes</c>.</exception>
    public bool Confirm(string question, TextReader input)
    {
        if (Args.Flag("yes")) return true;
        if (Console.IsInputRedirected && ReferenceEquals(input, Console.In))
            throw new CliUsageException("Thao tác cần xác nhận — thêm --yes khi chạy không tương tác.");
        Output.Err.Write($"{question} [y/N] ");
        var answer = input.ReadLine();
        return answer is not null && (answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
            || answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tìm provider theo id, identifier (vd <c>openrouter</c>) hoặc tên hiển thị — không phân biệt hoa thường.
    /// </summary>
    public async Task<Provider> ResolveProviderAsync(string reference)
    {
        var providers = await Get<IProviderService>().ListAsync(Cancellation);
        return RefResolver.Provider(providers, reference);
    }

    /// <summary>Tìm model theo id DB hoặc <c>&lt;identifier&gt;/&lt;modelId&gt;</c>.</summary>
    public async Task<Model> ResolveModelAsync(string reference)
    {
        var providers = await Get<IProviderService>().ListAsync(Cancellation);
        return RefResolver.Model(providers, reference);
    }

    public async Task<Combo> ResolveComboAsync(string reference)
    {
        var combos = await Get<IComboService>().ListAsync(Cancellation);
        return RefResolver.Combo(combos, reference);
    }

    public async Task<ClientKey> ResolveClientKeyAsync(string reference)
    {
        var keys = await Get<IClientKeyService>().ListAsync(Cancellation);
        return RefResolver.ClientKey(keys, reference);
    }

    /// <summary>Tìm account theo id trên toàn bộ provider (id account là duy nhất toàn DB).</summary>
    public async Task<(Provider Provider, ProviderAccount Account)> ResolveAccountAsync(string reference)
    {
        var providers = await Get<IProviderService>().ListAsync(Cancellation);
        return RefResolver.Account(providers, reference);
    }
}
