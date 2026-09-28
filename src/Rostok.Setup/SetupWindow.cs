using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;
using Rostok.Controls;
using Rostok.Licensing;

namespace Rostok.Setup;

// Мастер установки «Ростка» в стиле дизайн-системы «Тёплый мел»:
// приветствие → активация (ключ компьютера и серийный номер) → размещение → установка → готово.
public sealed class SetupWindow : Window
{
    private static readonly (string Title, string Art)[] Steps =
    [
        ("Добро пожаловать", "setup-welcome.jpg"),
        ("Активация", "setup-activation.jpg"),
        ("Размещение", "setup-welcome.jpg"),
        ("Установка", "setup-done.jpg"),
        ("Готово", "setup-done.jpg"),
    ];

    private int _step;
    private readonly Image _art = new() { Stretch = Stretch.Uniform, Margin = new Thickness(28, 10, 28, 10), OpacityMask = Theme.ArtMask() };
    private readonly StackPanel _stepList = new() { Margin = new Thickness(32, 0, 32, 0) };
    private readonly Border _content = new() { Padding = new Thickness(44, 8, 44, 24) };
    private readonly Button _back, _next, _cancel;
    private readonly ActivationPanel _activation = new();
    private string _installDir;
    private string _dbPath;
    private bool _dbNetwork;
    private bool _desktop = true, _startMenu = true, _launch = true;
    private bool _installing, _installed;
    private string? _error;
    private readonly ProgressBarView _progress = new();
    private readonly TextBlock _progressText = Ui.Muted("", 12.5);

    public SetupWindow()
    {
        Title = "Установка «Ростка»";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Rostok-Setup;component/Assets/rostok.ico"));
        Width = 960;
        Height = 640;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.Paper;
        FontFamily = Theme.UiFont;
        UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 52, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0) });

        var existing = Installer.Existing();
        _installDir = existing.Dir ?? Installer.DefaultInstallDir;
        _dbPath = (existing.Dir is not null ? Installer.ExistingDatabasePath(existing.Dir) : null) ?? Installer.DefaultDatabasePath;
        _dbNetwork = _dbPath.StartsWith(@"\\", StringComparison.Ordinal);

        _back = Ui.Ghost("Назад", () => Go(_step - 1), IconKind.ArrowBack);
        _next = Ui.Primary("Далее", Next);
        _cancel = Ui.TextAction("отменить установку", Close);
        _activation.ValidityChanged += _ => UpdateButtons();

        // ── левая панель: иллюстрация шага и список шагов ──
        var side = new DockPanel { Background = Theme.ArtBg };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 30, 32, 0) };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Rostok.Controls;component/Assets/logo.png")), Width = 38, Height = 38 });
        brand.Children.Add(new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Росток", FontSize = 18, FontWeight = FontWeights.ExtraBold, Foreground = Theme.Ink },
                new TextBlock { Text = $"установка · версия {Installer.Version}", FontSize = 11, Foreground = Theme.Ink3 },
            },
        });
        DockPanel.SetDock(brand, Dock.Top);
        side.Children.Add(brand);
        DockPanel.SetDock(_stepList, Dock.Bottom);
        _stepList.Margin = new Thickness(32, 0, 32, 30);
        side.Children.Add(_stepList);
        side.Children.Add(_art);

        // ── правая часть ──
        var close = Ui.IconButton(IconKind.Close, "Закрыть", Close);
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        var minimize = Ui.IconButton(IconKind.Minus, "Свернуть", () => WindowState = WindowState.Minimized);
        WindowChrome.SetIsHitTestVisibleInChrome(minimize, true);
        var titleBar = new DockPanel { Height = 52, Margin = new Thickness(44, 8, 12, 0) };
        var winButtons = Ui.HStack(6, minimize, close);
        DockPanel.SetDock(winButtons, Dock.Right);
        titleBar.Children.Add(winButtons);
        titleBar.Children.Add(Ui.Caps("Установка «Ростка»").With(t => t.VerticalAlignment = VerticalAlignment.Center));

        var nav = new DockPanel { Margin = new Thickness(44, 0, 44, 0) };
        var right = Ui.HStack(8, _back, _next);
        DockPanel.SetDock(right, Dock.Right);
        nav.Children.Add(right);
        nav.Children.Add(_cancel.With(b => b.VerticalAlignment = VerticalAlignment.Center));
        var navBar = new DashBorder { Sides = Sides.Top, Padding = new Thickness(0, 16, 0, 20), Child = nav };

        var main = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        DockPanel.SetDock(navBar, Dock.Bottom);
        main.Children.Add(titleBar);
        main.Children.Add(navBar);
        main.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _content, Focusable = false });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(side);
        Grid.SetColumn(main, 1);
        grid.Children.Add(main);
        Content = new Border { BorderBrush = Theme.Dash, BorderThickness = new Thickness(1), Background = Theme.Paper, Child = grid };

        Closing += (_, e) => { if (_installing) e.Cancel = true; };
        Go(0);
    }

    private void Go(int step)
    {
        _step = Math.Clamp(step, 0, Steps.Length - 1);
        _art.Source = new BitmapImage(new Uri($"pack://application:,,,/Rostok.Controls;component/Assets/Art/{Steps[_step].Art}"));
        _stepList.Children.Clear();
        for (var i = 0; i < Steps.Length; i++)
        {
            var current = i == _step;
            var done = i < _step;
            var num = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 12, 0),
                Background = current ? Theme.Accent : done ? Theme.Sheet : Brushes.Transparent,
                BorderBrush = current ? Theme.Accent : Theme.Dash, BorderThickness = new Thickness(1),
                Child = done
                    ? new Icon(IconKind.Check, 13) { Foreground = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    : Ui.Num((i + 1).ToString(), current ? Theme.Sheet : Theme.Ink3, 12, FontWeights.SemiBold).With(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
            row.Children.Add(num);
            row.Children.Add(Ui.Text(Steps[i].Title, null, current ? Theme.Accent : done ? Theme.Ink2 : Theme.Ink3, 13.5, current ? FontWeights.Bold : FontWeights.Normal).With(t => t.VerticalAlignment = VerticalAlignment.Center));
            _stepList.Children.Add(row);
        }
        _content.Child = _step switch
        {
            1 => ActivationView(),
            2 => PlacementView(),
            3 => InstallView(),
            4 => DoneView(),
            _ => WelcomeView(),
        };
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _back.Visibility = _step is > 0 and < 3 ? Visibility.Visible : Visibility.Collapsed;
        _cancel.Visibility = _step < 3 || (_step == 3 && !_installing && !_installed) ? Visibility.Visible : Visibility.Collapsed;
        _next.Content = _step switch
        {
            2 => Ui.Content(IconKind.Download, "Установить"),
            3 => _error is not null ? Ui.Content(IconKind.Refresh, "Повторить") : "Далее",
            4 => "Готово",
            _ => "Далее",
        };
        _next.IsEnabled = _step switch
        {
            1 => _activation.IsValid,
            2 => _installDir.Trim().Length > 0 && _dbPath.Trim().Length > 0,
            3 => !_installing,
            _ => true,
        };
    }

    private void Next()
    {
        switch (_step)
        {
            case 2: Go(3); _ = RunInstall(); break;
            case 3: if (_error is not null) { _ = RunInstall(); } else Go(4); break;
            case 4:
                // окно скрывается сразу: мастер не ждёт запуска программы
                Hide();
                if (_launch) LaunchAsUser(Path.Combine(_installDir, Installer.ExeName));
                Application.Current.Shutdown();
                break;
            default: Go(_step + 1); break;
        }
    }

    // Установщик работает с правами администратора. Программу запускаем через Проводник — от имени обычного пользователя,
    // как при открытии ярлыка: так она не получает лишних прав, а мастер не ждёт системного вызова «открыть файл».
    private static void LaunchAsUser(string exe)
    {
        if (!File.Exists(exe)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exe}\"") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        catch (Exception)
        {
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! }); }
            catch (Exception) { /* запуск не обязателен: программу можно открыть ярлыком */ }
        }
    }

    private static StackPanel Head(string title, string subtitle) => new()
    {
        Margin = new Thickness(0, 0, 0, 22),
        Children = { Ui.H1(title), Ui.Subtitle(subtitle).With(t => t.MaxWidth = 520) },
    };

    private FrameworkElement WelcomeView()
    {
        var s = Head("Установка «Ростка»", "«Росток» — мониторинг речевого и нейропсихологического развития детей для психолога и логопеда: баллы, уровни, динамика, программа коррекции и отчёт для родителей.");
        var existing = Installer.Existing();
        if (existing.Version is not null)
            s.Children.Add(Ui.HelpNote(Ui.Faint($"На компьютере уже установлена версия {existing.Version}. Программа будет обновлена, данные в базе сохранятся.")));
        var items = new (IconKind Icon, string Text)[]
        {
            (IconKind.Lock, "Активация по серийному номеру: ключ этого компьютера передаётся администратору, он присылает номер."),
            (IconKind.Folder, "Выбор папки программы и места для базы данных — на этом компьютере или в общей папке сети."),
            (IconKind.User, "Каждый сотрудник работает в своём рабочем пространстве с паролем."),
        };
        foreach (var (icon, text) in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
            var ic = new Icon(icon, 18) { Foreground = Theme.Accent, Margin = new Thickness(0, 1, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            row.Children.Add(ic);
            row.Children.Add(Ui.Body(text));
            s.Children.Add(row);
        }
        if (!Installer.HasPayload)
            s.Children.Add(Ui.Status("В эту сборку установщика не встроен пакет программы: установка завершится ошибкой. Соберите установщик скриптом build\\build-installer.ps1.", false).Margin(0, 12, 0, 0));
        return s;
    }

    private FrameworkElement ActivationView()
    {
        var s = Head("Активация", "Нажмите «Сгенерировать ключ» и передайте ключ администратору. Администратор получит по нему серийный номер в генераторе ключей — вставьте номер ниже и продолжите установку.");
        if (_activation.Parent is Panel p) p.Children.Remove(_activation);
        _activation.MaxWidth = 560;
        _activation.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(_activation);
        return s;
    }

    private FrameworkElement PlacementView()
    {
        var s = Head("Размещение", "Куда установить программу и где хранить базу данных. Базу можно положить в общую папку сети — тогда сотрудники на разных компьютерах работают с одним файлом, каждый в своём рабочем пространстве.");

        var dir = Ui.Input(_installDir, Installer.DefaultInstallDir, v => { _installDir = v; UpdateButtons(); });
        var browse = Ui.IconButton(IconKind.Folder, "Выбрать папку", () =>
        {
            var dlg = new OpenFolderDialog { Title = "Папка для «Ростка»", InitialDirectory = Directory.Exists(_installDir) ? _installDir : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            if (dlg.ShowDialog(this) == true) { _installDir = Path.Combine(dlg.FolderName, dlg.FolderName.EndsWith(Installer.AppName, StringComparison.OrdinalIgnoreCase) ? "" : Installer.AppName); dir.Text = _installDir; }
        });
        s.Children.Add(Ui.Field("Папка программы", Row(dir, browse)));

        var local = Choice("На этом компьютере", Installer.DefaultDatabasePath, !_dbNetwork, () => { _dbNetwork = false; _dbPath = Installer.DefaultDatabasePath; Go(2); });
        var network = Choice("В общей папке локальной сети", @"\\СЕРВЕР\Росток\rostok.db", _dbNetwork, () => { _dbNetwork = true; if (!_dbPath.StartsWith(@"\\")) _dbPath = ""; Go(2); });
        var choices = Ui.VStack(8, local, network);
        s.Children.Add(Ui.Field("База данных", choices).Margin(0, 18, 0, 0));
        if (_dbNetwork)
        {
            var path = Ui.Input(_dbPath, @"\\СЕРВЕР\Росток\rostok.db", v => { _dbPath = v; UpdateButtons(); });
            path.FontFamily = Theme.MonoFont;
            var pick = Ui.IconButton(IconKind.Folder, "Выбрать файл базы", () =>
            {
                var dlg = new SaveFileDialog { Title = "Файл базы данных «Ростка»", Filter = "База «Ростка» (*.db)|*.db", FileName = "rostok.db", OverwritePrompt = false, CheckFileExists = false };
                if (dlg.ShowDialog(this) == true) { _dbPath = dlg.FileName; path.Text = _dbPath; }
            });
            s.Children.Add(Row(path, pick).Margin(0, 8, 0, 0));
            s.Children.Add(Ui.Faint("Папка должна быть доступна на запись всем сотрудникам. Если файла нет, программа создаст его при первом запуске. Путь можно изменить позже: «Администрирование → Подключение».").Margin(0, 6, 0, 0).With(t => t.MaxWidth = 560));
        }
        s.Children.Add(Ui.Row(18,
            new Checkbox("Ярлык на рабочем столе", _desktop, v => _desktop = v),
            new Checkbox("Ярлык в меню «Пуск»", _startMenu, v => _startMenu = v)).Margin(0, 18, 0, 0));
        return s;
    }

    private static DockPanel Row(FrameworkElement input, Button button)
    {
        var d = new DockPanel { Width = 560, HorizontalAlignment = HorizontalAlignment.Left };
        button.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(button, Dock.Right);
        d.Children.Add(button);
        d.Children.Add(input);
        return d;
    }

    private static Button Choice(string title, string hint, bool active, Action onClick)
    {
        var content = new StackPanel();
        content.Children.Add(Ui.Text(title, null, null, 13.5, FontWeights.SemiBold));
        content.Children.Add(Ui.Num(hint, Theme.Ink3, 12));
        var b = Ui.Button("GhostButton", content, onClick);
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        b.Padding = new Thickness(14, 8, 14, 8);
        b.MaxWidth = 560;
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        Ui.SetIsActive(b, active);
        return b;
    }

    private FrameworkElement InstallView()
    {
        var s = Head("Установка", "Копирую файлы программы, настраиваю базу данных, лицензию и ярлыки.");
        s.Children.Add(_progress);
        s.Children.Add(_progressText.Margin(0, 10, 0, 0));
        if (_error is not null) s.Children.Add(Ui.Status(_error, false).Margin(0, 14, 0, 0).With(t => t.MaxWidth = 560));
        return s;
    }

    private FrameworkElement DoneView()
    {
        var s = Head("«Росток» установлен", "Программа готова к работе. При первом запуске создайте своё рабочее пространство — или выберите существующее, если база уже используется коллегами.");
        s.Children.Add(Ui.VStack(6,
            Ui.Rich(Ui.Run("Папка: ", Theme.Ink3), Ui.Run(_installDir, Theme.Ink, mono: true, size: 12.5)),
            Ui.Rich(Ui.Run("База данных: ", Theme.Ink3), Ui.Run(_dbPath, Theme.Ink, mono: true, size: 12.5))));
        s.Children.Add(new Checkbox("Запустить «Росток»", _launch, v => _launch = v).Margin(0, 18, 0, 0));
        return s;
    }

    // Снимки всех шагов мастера (режим --shots): для проверки дизайна и документации.
    public async Task SaveShots(string dir)
    {
        Directory.CreateDirectory(dir);
        async Task Settle() { await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(200); }
        var root = (FrameworkElement)Content;
        for (var i = 0; i < Steps.Length; i++)
        {
            if (i == 1) { Go(1); await _activation.GenerateAsync(); _activation.SetSerial("7N3QK-1AZ8M-…"); }
            else if (i == 3) { Go(3); _progress.Value = 0.62; _progressText.Text = "Копирую Rostok.Core.dll"; UpdateButtons(); }
            else Go(i);
            await Settle();
            Snapshot.Save(root, Path.Combine(dir, $"setup-{i + 1}.png"));
        }
        Application.Current.Shutdown();
    }

    // Самопроверка кнопок мастера без установки (режим --selftest <файл отчёта>).
    public async Task SelfTest(string report)
    {
        var log = new List<string>();
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        async Task Settle() { await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(150); }
        void Press(Button b) => ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(b).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();
        Go(0); await Settle();
        Press(_next); await Settle();
        log.Add($"{(_step == 1 ? "ok  " : "FAIL")} «Далее» на приветствии ведёт к активации");
        _installed = true;
        var fake = Environment.GetEnvironmentVariable("ROSTOK_SETUP_FAKE_DIR");
        _launch = fake is not null;
        if (fake is not null) _installDir = fake;
        Go(4); await Settle();
        log.Add($"{(_next.IsEnabled ? "ok  " : "FAIL")} кнопка «Готово» доступна");
        // «Готово» скрывает окно, запускает программу и завершает установщик — итог пишем при завершении
        Application.Current.Exit += (_, _) =>
        {
            log.Add($"{(!IsVisible ? "ok  " : "FAIL")} «Готово» скрывает окно и завершает установщик");
            File.WriteAllLines(report, log);
        };
        Press(_next);
        await Task.Delay(3000);
        log.Add("FAIL «Готово» не завершил установщик за 3 секунды");
        File.WriteAllLines(report, log);
        Application.Current.Shutdown(1);
    }

    private async Task RunInstall()
    {
        _installing = true;
        _error = null;
        Go(3);
        var options = new InstallOptions(_installDir.Trim(), _dbPath.Trim(), _desktop, _startMenu, _activation.Key!, _activation.Serial);
        var progress = new Progress<(double Value, string Text)>(p => { _progress.Value = p.Value; _progressText.Text = p.Text; });
        try
        {
            await Task.Run(() => Installer.Install(options, progress));
            _installed = true;
        }
        catch (Exception e)
        {
            _error = $"Установка не завершена: {e.Message}";
        }
        _installing = false;
        if (_installed) Go(4); else Go(3);
    }
}

// Полоса прогресса: тонкая дорожка цвета линии и заливка акцентом.
public sealed class ProgressBarView : FrameworkElement
{
    private double _value;
    public double Value { get => _value; set { _value = Math.Clamp(value, 0, 1); InvalidateVisual(); } }

    public ProgressBarView()
    {
        Height = 10;
        MaxWidth = 560;
        HorizontalAlignment = HorizontalAlignment.Left;
        Width = 560;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRoundedRectangle(Theme.Line, null, r, 2, 2);
        if (_value > 0) dc.DrawRoundedRectangle(Theme.Accent, null, new Rect(0, 0, ActualWidth * _value, ActualHeight), 2, 2);
    }
}
