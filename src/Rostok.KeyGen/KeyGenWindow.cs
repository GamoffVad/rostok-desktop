using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Rostok.Controls;
using Rostok.Licensing;

namespace Rostok.KeyGen;

// Генератор серийных номеров для администратора: ключ компьютера сотрудника → серийный номер.
// Номер — подпись ключа секретным ключом ECDSA; без файла секретного ключа генератор номера не выдаст.
public sealed class KeyGenWindow : Window
{
    private static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rostok", "KeyGen");
    private static string SettingsFile => Path.Combine(DataDir, "settings.json");
    private static string JournalFile => Path.Combine(DataDir, "issued.csv");
    private const string KeyFileName = "rostok-license.private.pem";

    private sealed record Settings(string? KeyPath);

    private string? _keyPath;
    private string? _keyPem;
    private string? _keyError;
    private readonly Border _body = new() { Padding = new Thickness(44, 0, 44, 28) };
    private string _machineKey = "";
    private string _issuedTo = "";
    private string? _serial;
    private (bool Ok, string Text)? _status;

    public KeyGenWindow()
    {
        Title = "Росток — генератор серийных номеров";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Rostok-KeyGen;component/Assets/rostok.ico"));
        Width = 980;
        Height = 760;
        MinWidth = 820;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        Background = Theme.Paper;
        FontFamily = Theme.UiFont;
        UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 52, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0) });

        // ── шапка с иллюстрацией ──
        var art = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Rostok.Controls;component/Assets/Art/keygen.jpg")), Stretch = Stretch.Uniform, Height = 228, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 110, 0), OpacityMask = Theme.ArtMask() };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(44, 26, 0, 0) };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Rostok.Controls;component/Assets/logo.png")), Width = 38, Height = 38 });
        brand.Children.Add(new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Росток", FontSize = 18, FontWeight = FontWeights.ExtraBold, Foreground = Theme.Ink },
                new TextBlock { Text = "генератор серийных номеров · для администратора", FontSize = 11, Foreground = Theme.Ink3 },
            },
        });
        var min = Ui.IconButton(IconKind.Minus, "Свернуть", () => WindowState = WindowState.Minimized);
        var close = Ui.IconButton(IconKind.Close, "Закрыть", Close);
        WindowChrome.SetIsHitTestVisibleInChrome(min, true);
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        var winButtons = Ui.HStack(6, min, close);
        winButtons.HorizontalAlignment = HorizontalAlignment.Right;
        winButtons.VerticalAlignment = VerticalAlignment.Top;
        winButtons.Margin = new Thickness(0, 14, 14, 0);
        var header = new Grid { Height = 250, ClipToBounds = true, Background = Theme.ArtBg };
        header.Children.Add(art);
        header.Children.Add(brand);
        header.Children.Add(winButtons);
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(44, 0, 0, 22) };
        title.Children.Add(Ui.H1("Серийные номера"));
        title.Children.Add(Ui.Subtitle("Вставьте ключ компьютера, который прислал сотрудник, и нажмите «Сгенерировать». Серийный номер подойдёт только к этому компьютеру.").With(t => t.MaxWidth = 460));
        header.Children.Add(title);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body, Focusable = false });
        Content = new Border { BorderBrush = Theme.Dash, BorderThickness = new Thickness(1), Background = Theme.Paper, Child = root };

        LoadKey(FindKey());
        Render();
    }

    // Файл секретного ключа: сохранённый путь → рядом с программой → папка keys репозитория.
    private static string? FindKey()
    {
        try
        {
            if (File.Exists(SettingsFile) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile))?.KeyPath is { } saved && File.Exists(saved)) return saved;
        }
        catch (Exception) { /* повреждённые настройки */ }
        var dir = AppContext.BaseDirectory;
        foreach (var candidate in new[] { Path.Combine(dir, KeyFileName), Path.Combine(dir, "keys", KeyFileName), Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "..", "keys", KeyFileName)) })
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    private void LoadKey(string? path)
    {
        _keyPath = path;
        _keyPem = null;
        _keyError = null;
        if (path is null) { _keyError = "Файл секретного ключа не найден."; return; }
        try
        {
            var pem = File.ReadAllText(path);
            if (!License.MatchesPublicKey(pem)) { _keyError = "Этот ключ не подходит к установщику «Ростка»: номера, выданные им, не пройдут проверку."; return; }
            _keyPem = pem;
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new Settings(path)));
        }
        catch (Exception e) { _keyError = $"Не удалось прочитать ключ: {e.Message}"; }
    }

    private void Render()
    {
        var s = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };

        // ── ключ подписи ──
        var keyInfo = new StackPanel();
        keyInfo.Children.Add(Ui.Caps("Ключ подписи"));
        if (_keyPem is not null)
        {
            keyInfo.Children.Add(Ui.Status("Подключён и подходит к установщику.", true).Margin(0, 4, 0, 2));
            keyInfo.Children.Add(Ui.Num(_keyPath!, Theme.Ink3, 12).With(t => t.TextWrapping = TextWrapping.Wrap));
        }
        else keyInfo.Children.Add(Ui.Status(_keyError ?? "", false).Margin(0, 4, 0, 2));
        var pick = new FilePick(Ui.Content(IconKind.Key, _keyPem is null ? "Выбрать файл ключа" : "Другой файл"), "Секретный ключ (*.pem)|*.pem|Все файлы|*.*", f => { LoadKey(f); Render(); });
        var keyRow = new DockPanel();
        DockPanel.SetDock(pick, Dock.Right);
        pick.VerticalAlignment = VerticalAlignment.Center;
        keyRow.Children.Add(pick);
        keyRow.Children.Add(keyInfo.With(k => k.Margin = new Thickness(0, 0, 16, 0)));
        s.Children.Add(new DashBorder { Sides = Sides.Top | Sides.Bottom, Padding = new Thickness(0, 14, 0, 14), Margin = new Thickness(0, 0, 0, 22), Child = keyRow });

        // ── ключ компьютера ──
        var key = Ui.Input(_machineKey, "RSTK-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX");
        key.FontFamily = Theme.MonoFont;
        key.FontSize = 15;
        key.TextChanged += (_, _) => { _machineKey = key.Text; };
        var paste = Ui.TextAction("вставить из буфера обмена", () => { try { if (Clipboard.ContainsText()) key.Text = Clipboard.GetText().Trim(); } catch (Exception) { } });
        s.Children.Add(Ui.Field("Ключ компьютера сотрудника", key));
        s.Children.Add(paste);
        var who = Ui.Input(_issuedTo, "ФИО сотрудника, учреждение, кабинет — для журнала", v => _issuedTo = v);
        s.Children.Add(Ui.Field("Кому выдаётся (необязательно)", who).Margin(0, 10, 0, 0));

        var generate = Ui.Primary("Сгенерировать", Generate, IconKind.Key);
        generate.IsEnabled = _keyPem is not null;
        generate.HorizontalAlignment = HorizontalAlignment.Left;
        generate.Margin = new Thickness(0, 16, 0, 0);
        s.Children.Add(generate);
        if (_status is { } st) s.Children.Add(Ui.Status(st.Text, st.Ok).Margin(0, 10, 0, 0));

        // ── серийный номер ──
        if (_serial is not null)
        {
            var box = new TextBox
            {
                Text = _serial, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = Theme.MonoFont, FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Accent, Padding = new Thickness(14, 12, 14, 12), AcceptsReturn = true,
            };
            var copy = Ui.Ghost("Скопировать серийный номер", null, IconKind.Copy);
            copy.Click += (_, _) => { try { Clipboard.SetText(_serial); copy.Content = Ui.Content(IconKind.Check, "Скопировано"); } catch (Exception) { } };
            copy.HorizontalAlignment = HorizontalAlignment.Left;
            s.Children.Add(new DashBorder
            {
                Sides = Sides.Top, Margin = new Thickness(0, 24, 0, 0), Padding = new Thickness(0, 18, 0, 0),
                Child = Ui.VStack(10, Ui.Field("Серийный номер", box), copy, Ui.Faint("Передайте номер сотруднику: он вставит его в установщик в поле «Серийный номер от администратора».")),
            });
        }

        // ── журнал ──
        var journal = ReadJournal();
        if (journal.Count > 0)
        {
            var t = new Tbl(Col.Auto(), Col.Star(1.2), Col.Star(1.6)) { Margin = new Thickness(0, 4, 0, 0) };
            t.Header("Дата", "Кому", "Ключ компьютера");
            foreach (var (date, to, mk) in journal.AsEnumerable().Reverse().Take(10))
                t.Row(Ui.Num(date, Theme.Ink3, 12), to.Length > 0 ? to : Ui.Faint("—"), Ui.Num(mk, Theme.Ink2, 12));
            s.Children.Add(new DashBorder
            {
                Sides = Sides.Top, Margin = new Thickness(0, 28, 0, 0), Padding = new Thickness(0, 18, 0, 0),
                Child = Ui.VStack(4, Ui.BlockHead(Ui.H2("Журнал выдачи"), Ui.Faint($"последние {Math.Min(10, journal.Count)} из {journal.Count} · {JournalFile}").With(f => f.TextWrapping = TextWrapping.NoWrap)), t),
            });
        }
        _body.Child = s;
    }

    // Снимок окна с примером выдачи (режим --shot): ключ этого компьютера, номер не пишется в журнал.
    public async Task SaveShot(string file)
    {
        _machineKey = License.MachineKey();
        _issuedTo = "Иванова М. В., логопед, кабинет 12";
        if (_keyPem is not null) { _serial = License.Sign(_machineKey, _keyPem); _status = (true, "Серийный номер создан и проверен."); }
        Render();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Task.Delay(200);
        Snapshot.Save((FrameworkElement)Content, file);
        Application.Current.Shutdown();
    }

    private void Generate()
    {
        _serial = null;
        if (_keyPem is null) return;
        if (!License.IsKeyWellFormed(_machineKey))
        {
            _status = (false, "Ключ компьютера указан неверно: он должен выглядеть как RSTK-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX. Проверьте, что он скопирован полностью.");
            Render();
            return;
        }
        try
        {
            var normalized = "RSTK-" + string.Join("-", License.Normalize(_machineKey).Chunk(4).Select(c => new string(c)));
            _serial = License.Sign(normalized, _keyPem);
            _status = License.Verify(normalized, _serial) ? (true, "Серийный номер создан и проверен.") : (false, "Номер создан, но не прошёл проверку — проверьте файл ключа.");
            AppendJournal(normalized, _issuedTo.Trim());
        }
        catch (Exception e) { _status = (false, e.Message); }
        Render();
    }

    private static List<(string Date, string To, string Key)> ReadJournal()
    {
        try
        {
            if (!File.Exists(JournalFile)) return [];
            return File.ReadAllLines(JournalFile, Encoding.UTF8).Skip(1).Select(l => l.Split(';')).Where(p => p.Length >= 3)
                .Select(p => (p[0], p[1].Replace("\\;", ";"), p[2])).ToList();
        }
        catch (Exception) { return []; }
    }

    private static void AppendJournal(string key, string to)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            if (!File.Exists(JournalFile)) File.WriteAllText(JournalFile, "Дата;Кому;Ключ компьютера\r\n", new UTF8Encoding(true));
            File.AppendAllText(JournalFile, $"{DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)};{to.Replace(";", ",")};{key}\r\n", Encoding.UTF8);
        }
        catch (Exception) { /* журнал не обязателен */ }
    }
}
