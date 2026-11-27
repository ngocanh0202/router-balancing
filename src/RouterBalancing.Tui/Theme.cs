using Terminal.Gui;
using TAttr = Terminal.Gui.Attribute;

namespace RouterBalancing.Tui;

/// <summary>
/// Giao diện tối (mặc định) / sáng cho rbtui, đổi bằng F2. Lựa chọn lưu ở
/// <c>&lt;ApplicationData&gt;/router-balancing/rbtui-theme</c> (Linux/macOS: ~/.config/router-balancing).
/// </summary>
internal static class Theme
{
    public static bool Dark { get; private set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "router-balancing", "rbtui-theme");

    public static void Load()
    {
        try
        {
            Dark = !File.Exists(FilePath) || File.ReadAllText(FilePath).Trim() != "light";
        }
        catch
        {
            Dark = true;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, Dark ? "dark" : "light");
        }
        catch
        {
            // không ghi được (quyền/đĩa) — chỉ mất ghi nhớ, giao diện vẫn đổi
        }
    }

    private static TAttr A(Color fg, Color bg) => Application.Driver.MakeAttribute(fg, bg);

    private static ColorScheme Scheme(TAttr normal, TAttr focus, TAttr hotNormal, TAttr hotFocus, TAttr disabled) => new()
    {
        Normal = normal, Focus = focus, HotNormal = hotNormal, HotFocus = hotFocus, Disabled = disabled,
    };

    /// <summary>Ghi đè bảng màu dùng chung (cửa sổ, hộp thoại, thanh trạng thái, lỗi). Gọi sau Application.Init.</summary>
    public static void Apply()
    {
        if (Dark)
        {
            Colors.Base = Scheme(A(Color.Gray, Color.Black), A(Color.Black, Color.BrightCyan),
                A(Color.BrightCyan, Color.Black), A(Color.Black, Color.BrightCyan), A(Color.DarkGray, Color.Black));
            Colors.TopLevel = Colors.Base;
            Colors.Dialog = Scheme(A(Color.White, Color.DarkGray), A(Color.Black, Color.BrightCyan),
                A(Color.BrightYellow, Color.DarkGray), A(Color.Black, Color.BrightCyan), A(Color.Gray, Color.DarkGray));
            Colors.Menu = Scheme(A(Color.Gray, Color.DarkGray), A(Color.Black, Color.BrightCyan),
                A(Color.BrightCyan, Color.DarkGray), A(Color.Black, Color.BrightCyan), A(Color.Gray, Color.DarkGray));
            Colors.Error = Scheme(A(Color.White, Color.Red), A(Color.Black, Color.BrightRed),
                A(Color.BrightYellow, Color.Red), A(Color.Black, Color.BrightRed), A(Color.Gray, Color.Red));
        }
        else
        {
            Colors.Base = Scheme(A(Color.Black, Color.White), A(Color.White, Color.Blue),
                A(Color.Blue, Color.White), A(Color.BrightYellow, Color.Blue), A(Color.DarkGray, Color.White));
            Colors.TopLevel = Colors.Base;
            Colors.Dialog = Scheme(A(Color.Black, Color.Gray), A(Color.White, Color.Blue),
                A(Color.Blue, Color.Gray), A(Color.BrightYellow, Color.Blue), A(Color.DarkGray, Color.Gray));
            Colors.Menu = Scheme(A(Color.Black, Color.Gray), A(Color.White, Color.Blue),
                A(Color.Blue, Color.Gray), A(Color.BrightYellow, Color.Blue), A(Color.DarkGray, Color.Gray));
            Colors.Error = Scheme(A(Color.White, Color.Red), A(Color.Black, Color.White),
                A(Color.BrightYellow, Color.Red), A(Color.Red, Color.White), A(Color.Gray, Color.Red));
        }
    }

    /// <summary>Gán lại bảng màu cho cây view đã tạo (view con không đặt màu riêng sẽ kế thừa từ cha).</summary>
    public static void Recolor(View view)
    {
        if (view is StatusBar) view.ColorScheme = Colors.Menu;
        else if (view is Dialog) view.ColorScheme = Colors.Dialog;
        else if (view is Toplevel or Window or FrameView) view.ColorScheme = Colors.Base;
        foreach (var child in view.Subviews) Recolor(child);
    }

    /// <summary>F2: đổi tối ↔ sáng, nhớ lựa chọn, vẽ lại toàn màn hình.</summary>
    public static void Toggle()
    {
        Dark = !Dark;
        Save();
        Apply();
        Recolor(Application.Top);
        Application.Top.SetNeedsDisplay();
        Application.Refresh();
    }
}
