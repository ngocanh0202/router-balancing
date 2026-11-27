using Microsoft.Extensions.DependencyInjection;
using RouterBalancing.Cli;
using RouterBalancing.Core.Storage;
using RouterBalancing.Tui;

// rbtui — giao diện terminal toàn màn hình cho Router Balancing.
// Cùng DB/khoá với rbctl và rb-server (macOS/Linux: ~/.config/router-balancing, Windows: %AppData%).

string? dbOverride = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--db" when i + 1 < args.Length:
            dbOverride = args[++i];
            break;
        case "-v" or "--version":
            Console.WriteLine(typeof(TuiApp).Assembly.GetName().Version?.ToString(3));
            return 0;
        case "-h" or "--help":
            Console.WriteLine("rbtui [--db <file DB>] — giao diện quản lý Router Balancing (dùng chung DB với rbctl/rb-server)");
            return 0;
        default:
            Console.Error.WriteLine($"Tham số không hợp lệ: {args[i]} (xem rbtui --help)");
            return 2;
    }
}

var env = CliEnvironment.CreateDefault(dbOverride);
try
{
    DbInitializer.Initialize(env.Factory, env.Protector);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"rbtui: khởi tạo DB lỗi ({env.DatabasePath}): {ex.Message}");
    return 1;
}

await using var services = CliServices.Build(env);
new TuiApp(services, env.DatabasePath).Run();
return 0;
