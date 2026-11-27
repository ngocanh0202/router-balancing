using System.Text;
using RouterBalancing.Cli;

// Console Windows mặc định code page OEM — không đặt UTF-8 thì tiếng Việt và ký tự "∞", "→" bị vỡ.
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Lần Ctrl+C đầu: huỷ mềm (logs --follow thoát gọn, request đang chạy được cancel);
    // lần hai: để runtime kill process như mặc định.
    if (cancellation.IsCancellationRequested) return;
    e.Cancel = true;
    cancellation.Cancel();
};

return await CliApp.RunAsync(args, Console.Out, Console.Error, cancellation.Token);
