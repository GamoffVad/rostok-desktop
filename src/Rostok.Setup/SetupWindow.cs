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
    private bool _installing, _installed, _selfTest;
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
        _back.MinHeight = _next.MinHeight = 38;
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
            case 2: _ = RunInstall(); break;
            case 3: if (_error is not null) { _ = RunInstall(); } else Go(4); break;
            case 4:
                // окно скрывается сразу: мастер не ждёт запуска программы
                Hide();
                if (_launch) UserLauncher.Launch(Path.Combine(_installDir, Installer.ExeName));
                Application.Current.Shutdown();
                break;
            default: Go(_step + 1); break;
        }
    }

    private static StackPanel Head(string title, string subtitle) => new()
    {
        Children =
        {
            Ui.H1(title).With(t => { t.FontSize = 26; t.LineHeight = 30; }),
            Ui.Subtitle(subtitle).With(t => { t.MaxWidth = 540; t.FontSize = 13.5; t.LineHeight = 20; t.Margin = new Thickness(0, 6, 0, 20); }),
        },
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

    // Поле и кнопка-иконка одной высоты 38 px; строка сжимается под ширину окна, кнопка не обрезается
    private static DockPanel Row(FrameworkElement input, Button button)
    {
        var d = new DockPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (input is Control c) { c.MinHeight = 38; c.Padding = new Thickness(7, 5, 7, 5); c.VerticalContentAlignment = VerticalAlignment.Center; }
        button.Width = button.Height = 38;
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
        // полоса и подпись переходят в новый вариант шага: из прежнего их нужно вынуть, иначе WPF не даст их добавить
        if (_progress.Parent is Panel p1) p1.Children.Remove(_progress);
        if (_progressText.Parent is Panel p2) p2.Children.Remove(_progressText);
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
            if (i == 1) { Go(1); await _activation.GenerateAsync(save: false); _activation.SetSerial("7N3QK-1AZ8M-…"); }
            else if (i == 3) { Go(3); _progress.Value = 0.62; _progressText.Text = "Копирую Rostok.Core.dll"; UpdateButtons(); }
            else Go(i);
            await Settle();
            Snapshot.Save(root, Path.Combine(dir, $"setup-{i + 1}.png"));
        }
        Application.Current.Shutdown();
    }

    // Самопроверка мастера (режим --selftest <файл отчёта> [--payload <zip>]):
    // кнопки, ошибка установки без пакета, а с пакетом — полная установка во временную папку, «Готово» и запуск программы.
    // Лицензия, ярлыки, права и реестр при этом не трогаются.
    public async Task SelfTest(string report, string? payload)
    {
        var log = new List<string>();
        void Check(bool ok, string what) { log.Add($"{(ok ? "ok  " : "FAIL")} {what}"); try { File.WriteAllLines(report, log); } catch (Exception) { } }
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _selfTest = true;
        async Task Settle(int ms = 150) { await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(ms); }
        async Task WaitInstall() { for (var i = 0; i < 600 && (_installing || _step == 3 && _error is null && !_installed); i++) await Settle(100); }
        // нажатие через автоматизацию выполняется не сразу, а ставится в очередь — после него всегда await Settle()
        void Press(Button b) => ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(b).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();

        Go(0); await Settle();
        Press(_next); await Settle();
        Check(_step == 1, "«Далее» на приветствии ведёт к активации");

        var temp = Path.Combine(Path.GetTempPath(), $"rostok-setup-selftest-{Guid.NewGuid():N}");
        _installDir = Path.Combine(temp, "Росток");
        _dbPath = Path.Combine(temp, "Data", "rostok.db");

        // 1. без пакета: ошибка показывается в окне, мастер не зависает
        Installer.PayloadOverride = null;
        if (!Installer.HasPayload)
        {
            Go(2); await Settle();
            Press(_next);
            await Settle();
            await WaitInstall();
            Check(_step == 3 && _error is not null && !_installing && _next.IsEnabled, $"без пакета: ошибка видна, кнопка «Повторить» доступна, мастер не завис [шаг {_step + 1}, установлено {_installed}, идёт {_installing}, ошибка: {_error ?? "нет"}]");
        }

        // 2. с пакетом: полная установка во временную папку
        if (payload is not null)
        {
            Installer.PayloadOverride = payload;
            _error = null;
            _installed = false;
            Go(2); await Settle();
            Press(_next);
            await Settle(50);
            Check(_step == 3 && _progress.Parent is not null, "шаг «Установка» открылся, полоса прогресса на месте");
            await WaitInstall();
            Check(_installed && _step == 4, $"установка завершилась{(_error is null ? "" : $": {_error}")}");
            Check(_progress.Value >= 0.999, "полоса прогресса дошла до конца");
            Check(File.Exists(Path.Combine(_installDir, Installer.ExeName)), "программа скопирована в папку установки");
            var settings = Path.Combine(_installDir, "appsettings.json");
            Check(File.Exists(settings) && File.ReadAllText(settings).Contains("rostok.db"), "appsettings.json указывает на выбранную базу");
            _launch = true;
        }
        else
        {
            _installed = true;
            _launch = false;
            Go(4); await Settle();
        }
        Check(_next.IsEnabled, "кнопка «Готово» доступна");

        // «Готово» скрывает окно, запускает программу и завершает установщик — итог пишем при завершении
        var exe = Path.Combine(_installDir, Installer.ExeName);
        Application.Current.Exit += (_, _) =>
        {
            Check(!IsVisible, "«Готово» скрывает окно и завершает установщик");
            if (payload is not null)
            {
                System.Diagnostics.Process? started = null;
                for (var i = 0; i < 40 && started is null; i++)
                {
                    Thread.Sleep(250);
                    started = System.Diagnostics.Process.GetProcessesByName("Rostok").FirstOrDefault(p =>
                    {
                        try { return string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase); } catch (Exception) { return false; }
                    });
                }
                Check(started is not null, "установленная программа запустилась");
                try { started?.Kill(); started?.WaitForExit(5000); } catch (Exception) { }
                try { Directory.Delete(temp, true); } catch (Exception) { }
            }
            File.WriteAllLines(report, log);
        };
        Press(_next);
        await Task.Delay(5000);
        Check(false, "«Готово» не завершил установщик за 5 секунд");
        File.WriteAllLines(report, log);
        Application.Current.Shutdown(1);
    }

    private async Task RunInstall()
    {
        _error = null;
        _progress.Value = 0;
        _progressText.Text = "Готовлю установку…";
        Go(3);
        _installing = true;
        UpdateButtons();
        try
        {
            var options = new InstallOptions(_installDir.Trim(), _dbPath.Trim(), _desktop, _startMenu, _activation.Key ?? "", _activation.Serial, !_selfTest);
            var progress = new Progress<(double Value, string Text)>(p => { _progress.Value = p.Value; _progressText.Text = p.Text; });
            await Task.Run(() => Installer.Install(options, progress));
            _installed = true;
        }
        catch (Exception e)
        {
            // любая ошибка — в окне, с кнопкой «Повторить»; раньше ошибка терялась, и мастер «висел» на пустой полосе
            _error = $"Установка не завершена: {e.Message}";
        }
        finally
        {
            _installing = false;
        }
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
