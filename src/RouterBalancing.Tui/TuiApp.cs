using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NStack;
using RouterBalancing.Core.Combos;
using RouterBalancing.Core.Domain;
using RouterBalancing.Core.Providers;
using RouterBalancing.Core.Server;
using RouterBalancing.Core.Settings;
using RouterBalancing.Core.Tools;
using Terminal.Gui;

namespace RouterBalancing.Tui;

/// <summary>
/// Màn hình chính: cột trái chọn mục (Providers/Models/Combos/Keys/OpenCode/Status),
/// khung phải là danh sách của mục đó; thao tác bằng phím, gợi ý phím ở dòng dưới khung.
/// Mọi ghi/đọc đi qua service Core (giống rbctl/app) — luật validate giống hệt.
/// </summary>
internal sealed class TuiApp
{
    private static readonly string[] Sections = ["Providers", "Models", "Combos", "Client keys", "OpenCode", "Status"];

    private readonly IServiceProvider _sp;
    private readonly string _dbPath;
    private readonly ListView _menu = new(Sections) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
    private readonly FrameView _content = new("Providers") { X = 20, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(1) };
    private readonly ListView _list = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(2) };
    private readonly Label _hint = new("") { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill() };
    private readonly Label _status = new("") { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill() };

    // dữ liệu đang hiển thị (song song với dòng trong _list)
    private List<Provider> _providers = [];
    private List<Model> _models = [];
    private List<Combo> _combos = [];
    private List<ClientKey> _keys = [];
    private List<(bool IsCombo, long Id, string Label)> _ocRows = [];
    private readonly HashSet<string> _ocMarked = [];
    private Provider? _modelProvider;
    private int _section;

    public TuiApp(IServiceProvider sp, string dbPath)
    {
        _sp = sp;
        _dbPath = dbPath;
    }

    private T S<T>() where T : notnull => _sp.GetRequiredService<T>();

    /// <summary>Chạy tác vụ async ngoài UI thread rồi chờ (tránh deadlock SynchronizationContext của Terminal.Gui).</summary>
    private static T Wait<T>(Func<Task<T>> f) => Task.Run(f).GetAwaiter().GetResult();
    private static void Wait(Func<Task> f) => Task.Run(f).GetAwaiter().GetResult();

    public void Run()
    {
        Application.Init();
        var top = Application.Top;

        var win = new Window("Router Balancing — rbtui") { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1) };
        var side = new FrameView("Mục") { X = 0, Y = 1, Width = 20, Height = Dim.Fill(1) };
        side.Add(_menu);
        _content.Add(_list, _hint, _status);
        var db = new Label($"DB: {_dbPath}") { X = 1, Y = 0, Width = Dim.Fill() };
        win.Add(db, side, _content);

        _menu.SelectedItemChanged += e => ShowSection(e.Item);
        _menu.OpenSelectedItem += _ => _list.SetFocus();
        _list.KeyPress += OnListKey;
        _list.OpenSelectedItem += _ => OnEnter();

        var bar = new StatusBar(new StatusItem[]
        {
            new(Key.Tab, "~Tab~ Đổi khung", () => { if (_menu.HasFocus) _list.SetFocus(); else _menu.SetFocus(); }),
            new(Key.F5, "~F5~ Tải lại", () => ShowSection(_section)),
            new(Key.F1, "~F1~ Trợ giúp", ShowHelp),
            new(Key.CtrlMask | Key.Q, "~^Q~ Thoát", () => Application.RequestStop()),
        });
        top.Add(win, bar);
        ShowSection(0);
        _menu.SetFocus();
        Application.Run();
        Application.Shutdown();
    }

    // ───────────────────────── hiển thị từng mục ─────────────────────────

    private void ShowSection(int i)
    {
        _section = i;
        _content.Title = Sections[i];
        try
        {
            switch (i)
            {
                case 0: ShowProviders(); break;
                case 1: ShowModels(); break;
                case 2: ShowCombos(); break;
                case 3: ShowKeys(); break;
                case 4: ShowOpenCode(); break;
                default: ShowStatus(); break;
            }
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    private void SetRows(IEnumerable<string> rows, string hint)
    {
        var keep = _list.SelectedItem;
        var list = rows.ToList();
        _list.SetSource(list);
        if (list.Count > 0) _list.SelectedItem = Math.Clamp(keep, 0, list.Count - 1);
        _hint.Text = hint;
    }

    private void ShowProviders()
    {
        _providers = Wait(() => S<IProviderService>().ListAsync()).OrderBy(p => p.Id).ToList();
        SetRows(_providers.Select(p =>
                $"{(p.Enabled ? "[ON] " : "[off]")} {p.Name,-22} {p.Identifier ?? "-",-14} {p.Models.Count(m => m.Enabled),3}/{p.Models.Count,-4} model  {p.BaseUrl}"),
            "a Thêm · Space Bật/tắt · Enter Xem model · f Lấy model · t Test · k Đổi API key · Del Xoá");
    }

    private void ShowModels()
    {
        if (_modelProvider is null)
        {
            _providers = Wait(() => S<IProviderService>().ListAsync()).OrderBy(p => p.Id).ToList();
            _modelProvider = _providers.FirstOrDefault(p => p.Models.Count > 0) ?? _providers.FirstOrDefault();
        }
        else
        {
            var id = _modelProvider.Id;
            _modelProvider = Wait(() => S<IProviderService>().ListAsync()).FirstOrDefault(p => p.Id == id) ?? _modelProvider;
        }

        _models = _modelProvider?.Models.OrderBy(m => m.ModelId).ToList() ?? [];
        _content.Title = $"Models — {_modelProvider?.Name ?? "(chưa có provider)"}";
        SetRows(_models.Select(m =>
                $"{(m.Enabled ? "[x]" : "[ ]")} {m.ModelId,-52} {(m.ContextWindow is { } c ? $"{c / 1000}k ctx" : "")}"),
            "Space Bật/tắt · e Bật hết · n Tắt hết · p Chọn provider · f Lấy lại danh sách · + Thêm tay");
    }

    private void ShowCombos()
    {
        _combos = Wait(() => S<IComboService>().ListAsync()).ToList();
        var names = ModelNames();
        var comboNames = _combos.ToDictionary(c => c.Id, c => c.Name);
        SetRows(_combos.Select(c =>
                $"{c.Name,-20} {(c.Mode == ComboMode.Fallback ? "Fallback   " : "RoundRobin ")} " +
                string.Join(" → ", c.Items.OrderBy(x => x.Position).Select(x =>
                    x.TargetModelId is { } m ? names.GetValueOrDefault(m, $"#{m}") :
                    x.TargetComboId is { } k ? "combo:" + comboNames.GetValueOrDefault(k, $"#{k}") : "?"))),
            "a Thêm · Enter Sửa · m Đổi chế độ · Del Xoá");
    }

    private void ShowKeys()
    {
        _keys = Wait(() => S<IClientKeyService>().ListAsync()).ToList();
        SetRows(_keys.Select(k =>
                $"{(k.Enabled ? "[ON] " : "[off]")} {k.Name,-20} {k.KeyMask,-20} dùng lần cuối: {k.LastUsedAt?.ToLocalTime().ToString("g") ?? "-"}"),
            "c Tạo key · Space Bật/tắt · Del Xoá   (đổi key → khởi động lại rb-server/app)");
    }

    private void ShowOpenCode()
    {
        var data = Wait(() => S<IOpenCodeConfigService>().GetPageDataAsync());
        _ocRows = [];
        foreach (var c in data.Combos)
            _ocRows.Add((true, c.Id, $"combo  {c.Name,-28} {c.MemberCount} model{(c.ContextWindow is { } w ? $" · {w / 1000}k" : "")}"));
        foreach (var g in data.Groups.Where(g => g.ProviderEnabled))
            foreach (var m in g.Models)
                _ocRows.Add((false, m.Id, $"model  {g.ProviderName}/{m.ModelId}{(m.ContextWindow is { } w ? $" · {w / 1000}k" : "")}{(m.Tools == true ? " · tools" : "")}"));

        if (_ocMarked.Count == 0)
        {
            // khởi tạo theo lựa chọn đã lưu (giống trang Tools / rbctl opencode)
            var stored = S<IAppSettingsService>().Get<ToolsSelection?>(SettingsKeys.ToolsSelection, null);
            var sel = ToolsSelectionResolver.Resolve(stored,
                data.Groups.SelectMany(g => g.Models).Select(m => m.Id).ToList(),
                data.Combos.Select(c => c.Id).ToList(), data.DefaultSelectedIds);
            foreach (var id in sel.ModelIds) _ocMarked.Add($"m{id}");
            foreach (var id in sel.ComboIds) _ocMarked.Add($"c{id}");
        }

        SetRows(_ocRows.Select(r => $"{(_ocMarked.Contains(Tag(r)) ? "[x]" : "[ ]")} {r.Label}"),
            $"Space Chọn · w Ghi vào {S<IAppSettingsService>().OpenCodeConfigPath} · r Ghi + khởi động lại opencode (tmux)");
    }

    private static string Tag((bool IsCombo, long Id, string Label) r) => (r.IsCombo ? "c" : "m") + r.Id;

    private void ShowStatus()
    {
        var settings = S<IAppSettingsService>();
        var port = settings.Port;
        string health;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var r = Wait(() => http.GetAsync($"http://127.0.0.1:{port}/health"));
            health = r.IsSuccessStatusCode ? $"đang chạy (http://127.0.0.1:{port}/v1)" : $"trả {(int)r.StatusCode}";
        }
        catch (Exception ex)
        {
            health = $"không phản hồi ở cổng {port} ({ex.GetBaseException().Message})";
        }

        var providers = Wait(() => S<IProviderService>().ListAsync());
        var combos = Wait(() => S<IComboService>().ListAsync());
        var keys = Wait(() => S<IClientKeyService>().ListAsync());
        SetRows(
        [
            $"Proxy        : {health}",
            $"Database     : {_dbPath}",
            $"Provider     : {providers.Count(p => p.Enabled)} bật / {providers.Count}",
            $"Model bật    : {providers.Where(p => p.Enabled).Sum(p => p.Models.Count(m => m.Enabled))}",
            $"Combo        : {combos.Count}",
            $"Client key   : {keys.Count(k => k.Enabled)} bật / {keys.Count}",
            $"opencode.json: {settings.OpenCodeConfigPath}",
        ], "F5 Tải lại");
    }

    private Dictionary<long, string> ModelNames()
    {
        var providers = Wait(() => S<IProviderService>().ListAsync());
        return providers.SelectMany(p => p.Models.Select(m => (m.Id, Name: $"{p.Identifier ?? p.Name}/{m.ModelId}")))
            .ToDictionary(x => x.Id, x => x.Name);
    }

    // ───────────────────────── phím ─────────────────────────

    private void OnListKey(View.KeyEventEventArgs e)
    {
        var key = e.KeyEvent.Key;
        var ch = char.ToLowerInvariant((char)e.KeyEvent.KeyValue);
        var i = _list.SelectedItem;
        try
        {
            var handled = _section switch
            {
                0 => ProviderKey(key, ch, i),
                1 => ModelKey(key, ch, i),
                2 => ComboKey(key, ch, i),
                3 => KeyKey(key, ch, i),
                4 => OpenCodeKey(key, ch, i),
                _ => false,
            };
            if (handled) e.Handled = true;
        }
        catch (Exception ex)
        {
            e.Handled = true;
            Error(ex);
        }
    }

    private void OnEnter()
    {
        try
        {
            var i = _list.SelectedItem;
            if (_section == 0 && i >= 0 && i < _providers.Count)
            {
                _modelProvider = _providers[i];
                _menu.SelectedItem = 1;   // → ShowSection(1) qua SelectedItemChanged
                ShowSection(1);
                _list.SetFocus();
            }
            else if (_section == 2 && i >= 0 && i < _combos.Count)
            {
                EditCombo(_combos[i]);
            }
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    private static bool IsDelete(Key key) => key is Key.DeleteChar or Key.Backspace;

    private bool ProviderKey(Key key, char ch, int i)
    {
        var p = i >= 0 && i < _providers.Count ? _providers[i] : null;
        if (ch == 'a') { AddProvider(); return true; }
        if (p is null) return false;
        if (key == Key.Space)
        {
            Wait(() => S<IProviderService>().SetEnabledAsync(p.Id, !p.Enabled));
            ShowProviders();
            return true;
        }
        if (ch == 'f') { FetchModels(p); return true; }
        if (ch == 't') { TestProvider(p); return true; }
        if (ch == 'k') { ChangeKey(p); return true; }
        if (IsDelete(key))
        {
            if (Confirm($"Xoá provider '{p.Name}' (kèm model/account)?"))
            {
                Wait(() => S<IProviderService>().DeleteAsync(p.Id));
                ShowProviders();
            }
            return true;
        }
        return false;
    }

    private bool ModelKey(Key key, char ch, int i)
    {
        var m = i >= 0 && i < _models.Count ? _models[i] : null;
        if (ch == 'p') { PickProvider(); return true; }
        if (_modelProvider is null) return false;
        var pid = _modelProvider.Id;
        if (key == Key.Space && m is not null)
        {
            Wait(() => S<IModelService>().SetEnabledAsync(m.Id, !m.Enabled));
            ShowModels();
            if (i + 1 < _models.Count) _list.SelectedItem = i + 1;
            return true;
        }
        if (ch == 'e') { Wait(() => S<IModelService>().SetAllEnabledAsync(pid, true)); ShowModels(); return true; }
        if (ch == 'n') { Wait(() => S<IModelService>().SetAllEnabledAsync(pid, false)); ShowModels(); return true; }
        if (ch == 'f') { FetchModels(_modelProvider); return true; }
        if (ch == '+')
        {
            var id = Prompt("Thêm model thủ công", "Model ID (vd deepseek-ai/deepseek-v4-pro):");
            if (!string.IsNullOrWhiteSpace(id))
            {
                Wait(() => S<IModelService>().AddManualAsync(pid, id.Trim()));
                ShowModels();
            }
            return true;
        }
        return false;
    }

    private bool ComboKey(Key key, char ch, int i)
    {
        var c = i >= 0 && i < _combos.Count ? _combos[i] : null;
        if (ch == 'a') { EditCombo(null); return true; }
        if (c is null) return false;
        if (ch == 'm')
        {
            var draft = ToDraft(c);
            draft.Mode = c.Mode == ComboMode.Fallback ? ComboMode.RoundRobin : ComboMode.Fallback;
            Wait(() => S<IComboService>().UpdateAsync(c.Id, draft));
            ShowCombos();
            return true;
        }
        if (IsDelete(key))
        {
            if (Confirm($"Xoá combo '{c.Name}'?"))
            {
                Wait(() => S<IComboService>().DeleteAsync(c.Id));
                ShowCombos();
            }
            return true;
        }
        return false;
    }

    private bool KeyKey(Key key, char ch, int i)
    {
        var k = i >= 0 && i < _keys.Count ? _keys[i] : null;
        if (ch == 'c')
        {
            var name = Prompt("Tạo client key", "Tên key (vd opencode):");
            if (!string.IsNullOrWhiteSpace(name))
            {
                var (_, plain) = Wait(() => S<IClientKeyService>().CreateAsync(new ClientKeyDraft(name.Trim(), null, null)));
                ShowKeys();
                ShowSecret("Client key mới — CHỈ hiện 1 lần", plain);
            }
            return true;
        }
        if (k is null) return false;
        if (key == Key.Space)
        {
            Wait(() => S<IClientKeyService>().SetEnabledAsync(k.Id, !k.Enabled));
            ShowKeys();
            return true;
        }
        if (IsDelete(key))
        {
            if (Confirm($"Xoá client key '{k.Name}'?"))
            {
                Wait(() => S<IClientKeyService>().DeleteAsync(k.Id));
                ShowKeys();
            }
            return true;
        }
        return false;
    }

    private bool OpenCodeKey(Key key, char ch, int i)
    {
        if (key == Key.Space && i >= 0 && i < _ocRows.Count)
        {
            var tag = Tag(_ocRows[i]);
            if (!_ocMarked.Remove(tag)) _ocMarked.Add(tag);
            ShowOpenCode();
            if (i + 1 < _ocRows.Count) _list.SelectedItem = i + 1;
            return true;
        }
        if (ch is 'w' or 'r')
        {
            WriteOpenCode(restart: ch == 'r');
            return true;
        }
        return false;
    }

    // ───────────────────────── hành động ─────────────────────────

    private void AddProvider()
    {
        var name = new TextField("") { X = 14, Y = 1, Width = Dim.Fill(2) };
        var ident = new TextField("") { X = 14, Y = 3, Width = Dim.Fill(2) };
        var url = new TextField("https://") { X = 14, Y = 5, Width = Dim.Fill(2) };
        var key = new TextField("") { X = 14, Y = 7, Width = Dim.Fill(2), Secret = true };
        var ok = false;
        var save = new Button("Lưu", is_default: true);
        var cancel = new Button("Huỷ");
        save.Clicked += () => { ok = true; Application.RequestStop(); };
        cancel.Clicked += () => Application.RequestStop();
        var d = new Dialog("Thêm provider (OpenAI-compatible)", 76, 14, save, cancel);
        d.Add(new Label("Tên:") { X = 1, Y = 1 }, name,
              new Label("Identifier:") { X = 1, Y = 3 }, ident,
              new Label("Base URL:") { X = 1, Y = 5 }, url,
              new Label("API key:") { X = 1, Y = 7 }, key,
              new Label("vd NVIDIA · nvidia · https://integrate.api.nvidia.com/v1") { X = 1, Y = 9 });
        Application.Run(d);
        if (!ok) return;

        var draft = new ProviderDraft
        {
            Name = name.Text.ToString()!.Trim(),
            Identifier = ident.Text.ToString()!.Trim(),
            Type = ProviderType.OpenAI,
            BaseUrl = url.Text.ToString()!.Trim(),
            ApiKey = key.Text.ToString()!.Trim(),
        };
        var p = Wait(() => S<IProviderService>().CreateAsync(draft));
        ShowProviders();
        if (Confirm($"Đã tạo '{p.Name}'. Lấy danh sách model ngay?")) FetchModels(p);
    }

    private void ChangeKey(Provider p)
    {
        var accounts = Wait(() => S<IProviderAccountService>().ListAsync(p.Id));
        var acc = accounts.FirstOrDefault();
        var newKey = Prompt($"API key mới cho {p.Name}", acc is null ? "API key (tạo account 'Default'):" : $"API key cho account '{acc.Name}':", secret: true);
        if (string.IsNullOrWhiteSpace(newKey)) return;
        var draft = new ProviderAccountDraft { ProviderId = p.Id, Name = acc?.Name ?? "Default", ApiKey = newKey.Trim(), Enabled = true };
        if (acc is null) Wait(() => S<IProviderAccountService>().CreateAsync(draft));
        else Wait(() => S<IProviderAccountService>().UpdateAsync(acc.Id, draft));
        Info("Đã lưu API key.");
    }

    private void FetchModels(Provider p)
    {
        Busy($"Đang lấy danh sách model từ {p.Name}…", async () =>
        {
            var (added, skipped, filled) = await S<IModelService>().FetchFromProviderAsync(p.Id);
            return $"{p.Name}: thêm {added}, bỏ qua {skipped} (đã có), điền metadata {filled}.";
        });
    }

    private void TestProvider(Provider p)
    {
        Busy($"Đang test {p.Name}…", async () =>
        {
            var results = await S<IProviderAccountService>().TestAllAsync(p.Id);
            return results.Count == 0 ? "Provider chưa có account." : string.Join("\n", results.Select(r => $"{(r.Success ? "OK " : "LỖI")} {r.AccountName}: {r.Message}"));
        });
    }

    private void PickProvider()
    {
        _providers = Wait(() => S<IProviderService>().ListAsync()).OrderBy(p => p.Id).ToList();
        if (_providers.Count == 0) return;
        var lv = new ListView(_providers.Select(p => $"{p.Name} ({p.Models.Count} model)").ToList())
            { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(2) };
        Provider? picked = null;
        lv.OpenSelectedItem += e => { picked = _providers[e.Item]; Application.RequestStop(); };
        var cancel = new Button("Huỷ");
        cancel.Clicked += () => Application.RequestStop();
        var d = new Dialog("Chọn provider (Enter)", 60, Math.Min(_providers.Count + 6, 24), cancel);
        d.Add(lv);
        lv.SetFocus();
        Application.Run(d);
        if (picked is null) return;
        _modelProvider = picked;
        ShowModels();
    }

    private static ComboDraft ToDraft(Combo c) => new()
    {
        Name = c.Name,
        Mode = c.Mode,
        Items = c.Items.OrderBy(x => x.Position)
            .Select(x => new ComboItemDraft { TargetModelId = x.TargetModelId, TargetComboId = x.TargetComboId }).ToList(),
    };

    /// <summary>Tạo/sửa combo: tên, chế độ, chọn model theo THỨ TỰ bấm Space (số [1],[2]… = thứ tự fallback).</summary>
    private void EditCombo(Combo? existing)
    {
        var providers = Wait(() => S<IProviderService>().ListAsync());
        var models = providers.Where(p => p.Enabled)
            .SelectMany(p => p.Models.Where(m => m.Enabled).Select(m => (m.Id, Name: $"{p.Identifier ?? p.Name}/{m.ModelId}")))
            .OrderBy(x => x.Name).ToList();
        if (models.Count == 0)
        {
            Info("Chưa có model nào đang bật — vào Models để bật model trước.");
            return;
        }

        var order = existing?.Items.OrderBy(x => x.Position).Where(x => x.TargetModelId is not null)
            .Select(x => x.TargetModelId!.Value).ToList() ?? [];
        var name = new TextField(existing?.Name ?? "") { X = 10, Y = 1, Width = 30 };
        var mode = new RadioGroup(new ustring[] { "Fallback (thử lần lượt)", "Round-robin (chia đều)" })
            { X = 44, Y = 1, SelectedItem = existing?.Mode == ComboMode.RoundRobin ? 1 : 0 };
        var lv = new ListView { X = 1, Y = 4, Width = Dim.Fill(1), Height = Dim.Fill(2) };
        void Refresh()
        {
            var keep = lv.SelectedItem;
            lv.SetSource(models.Select(m =>
            {
                var pos = order.IndexOf(m.Id);
                return $"{(pos >= 0 ? $"[{pos + 1,2}]" : "[  ]")} {m.Name}";
            }).ToList());
            lv.SelectedItem = Math.Clamp(keep, 0, models.Count - 1);
        }
        lv.KeyPress += e =>
        {
            if (e.KeyEvent.Key != Key.Space) return;
            var id = models[lv.SelectedItem].Id;
            if (!order.Remove(id)) order.Add(id);
            Refresh();
            e.Handled = true;
        };
        Refresh();

        var ok = false;
        var save = new Button("Lưu", is_default: true);
        var cancel = new Button("Huỷ");
        save.Clicked += () => { ok = true; Application.RequestStop(); };
        cancel.Clicked += () => Application.RequestStop();
        var d = new Dialog(existing is null ? "Tạo combo" : $"Sửa combo {existing.Name}", 96, 28, save, cancel);
        d.Add(new Label("Tên:") { X = 1, Y = 1 }, name, mode,
              new Label("Space = chọn/bỏ model; số trong [ ] là thứ tự dùng (Fallback thử từ 1)") { X = 1, Y = 3 }, lv);
        Application.Run(d);
        if (!ok) return;

        var draft = new ComboDraft
        {
            Name = name.Text.ToString()!.Trim(),
            Mode = mode.SelectedItem == 1 ? ComboMode.RoundRobin : ComboMode.Fallback,
            Items = order.Select(id => new ComboItemDraft { TargetModelId = id }).ToList(),
        };
        if (existing is null) Wait(() => S<IComboService>().CreateAsync(draft));
        else Wait(() => S<IComboService>().UpdateAsync(existing.Id, draft));
        ShowCombos();
    }

    private void WriteOpenCode(bool restart)
    {
        var settings = S<IAppSettingsService>();
        var modelIds = _ocMarked.Where(t => t[0] == 'm').Select(t => long.Parse(t[1..])).ToArray();
        var comboIds = _ocMarked.Where(t => t[0] == 'c').Select(t => long.Parse(t[1..])).ToArray();
        if (modelIds.Length + comboIds.Length == 0)
        {
            Info("Chưa chọn model/combo nào (Space để chọn).");
            return;
        }

        // client key: ~/vm/rb.key (notebook Kaggle VM) nếu có, không thì hỏi (bỏ trống nếu proxy không bật key)
        var keyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "vm", "rb.key");
        var apiKey = File.Exists(keyFile) ? File.ReadAllText(keyFile).Trim() : null;
        if (string.IsNullOrEmpty(apiKey))
            apiKey = Prompt("Client key cho opencode", "Client key (sk-rb-…; bỏ trống nếu proxy không bật key):", secret: true)?.Trim();

        var baseUrl = OpenCodeConfigService.BuildBaseUrl(null, settings.Port);
        var path = settings.OpenCodeConfigPath;
        var result = Wait(() => S<IOpenCodeConfigService>().WriteAsync(path, modelIds, comboIds, baseUrl,
            string.IsNullOrEmpty(apiKey) ? null : apiKey));
        settings.Set(SettingsKeys.ToolsSelection, new ToolsSelection(modelIds, comboIds));   // nhớ lựa chọn như trang Tools

        var msg = $"Đã ghi {result.Path}" + (result.BackupPath is null ? "" : $"\n(backup: {result.BackupPath})");
        if (restart)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("tmux", "respawn-window -k -t main:opencode")
                    { RedirectStandardError = true, UseShellExecute = false });
                p?.WaitForExit(5000);
                msg += p?.ExitCode == 0 ? "\n↻ Đã khởi động lại opencode server (tmux main:opencode)."
                                        : "\n⚠ Không khởi động lại được cửa sổ tmux 'opencode'.";
            }
            catch (Exception ex)
            {
                msg += $"\n⚠ Không chạy được tmux: {ex.Message}";
            }
        }
        Info(msg);
    }

    // ───────────────────────── hộp thoại ─────────────────────────

    /// <summary>Chạy tác vụ mạng ở nền, hiện hộp "đang làm…" rồi kết quả; xong thì tải lại mục hiện tại.</summary>
    private void Busy(string title, Func<Task<string>> work)
    {
        var label = new Label(title) { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(2) };
        var close = new Button("Đóng") { Enabled = false };
        close.Clicked += () => Application.RequestStop();
        var d = new Dialog("Đang xử lý", 80, 12, close);
        d.Add(label);
        Task.Run(work).ContinueWith(t => Application.MainLoop.Invoke(() =>
        {
            label.Text = t.IsFaulted ? "Lỗi: " + t.Exception!.GetBaseException().Message : t.Result;
            close.Enabled = true;
            close.SetFocus();
        }));
        Application.Run(d);
        ShowSection(_section);
    }

    private static string? Prompt(string title, string label, bool secret = false)
    {
        var field = new TextField("") { X = 1, Y = 2, Width = Dim.Fill(1), Secret = secret };
        string? value = null;
        var ok = new Button("OK", is_default: true);
        var cancel = new Button("Huỷ");
        ok.Clicked += () => { value = field.Text.ToString(); Application.RequestStop(); };
        cancel.Clicked += () => Application.RequestStop();
        var d = new Dialog(title, 76, 8, ok, cancel);
        d.Add(new Label(label) { X = 1, Y = 1 }, field);
        field.SetFocus();
        Application.Run(d);
        return value;
    }

    private static void ShowSecret(string title, string secret)
    {
        var field = new TextField(secret) { X = 1, Y = 2, Width = Dim.Fill(1), ReadOnly = true };
        var ok = new Button("Đã lưu lại", is_default: true);
        ok.Clicked += () => Application.RequestStop();
        var d = new Dialog(title, 80, 9, ok);
        d.Add(new Label("Bôi đen để copy — DB chỉ giữ hash, không xem lại được:") { X = 1, Y = 1 }, field);
        Application.Run(d);
    }

    private static bool Confirm(string message) => MessageBox.Query("Xác nhận", message, "Có", "Không") == 0;

    private static void Info(string message) => MessageBox.Query("Router Balancing", message, "OK");

    private static void Error(Exception ex) => MessageBox.ErrorQuery("Lỗi", ex.GetBaseException().Message, "OK");

    private static void ShowHelp() => MessageBox.Query("Trợ giúp", """
        Tab: đổi giữa cột Mục và danh sách · ↑↓ chọn · Enter mở/sửa · F5 tải lại · Ctrl+Q thoát

        Providers : a thêm · Space bật/tắt · Enter xem model · f lấy model · t test · k đổi key · Del xoá
        Models    : Space bật/tắt · e bật hết · n tắt hết · p chọn provider · f lấy lại · + thêm tay
        Combos    : a thêm · Enter sửa (Space chọn model theo thứ tự) · m đổi chế độ · Del xoá
        Client keys: c tạo · Space bật/tắt · Del xoá
        OpenCode  : Space chọn model/combo · w ghi opencode.json · r ghi + khởi động lại opencode (tmux)

        Đổi provider/model/combo có hiệu lực ngay với rb-server/app.
        Đổi client key/settings: khởi động lại rb-server (hoặc app).
        """, "OK");
}
