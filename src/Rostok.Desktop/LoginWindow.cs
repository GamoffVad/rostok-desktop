using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rostok.Controls;
using Rostok.Core;
using Rostok.Core.Storage;
using Rostok.Desktop.Components;
using Rostok.Desktop.Services;

namespace Rostok.Desktop;

// Вход: выбрать рабочее пространство и ввести его пароль — или создать новое.
// Каждый сотрудник работает в своём пространстве: группы, дети, баллы, библиотека, словари и настройки хранятся только в нём.
public sealed class LoginWindow : Window
{
    private enum Mode { Enter, Create, Connection }

    private readonly Border _body = new();
    private Mode _mode;
    private string? _selected;
    private string? _error;

    public LoginWindow()
    {
        Title = "Росток — вход";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Rostok;component/Assets/rostok.ico"));
        Background = Theme.Paper;
        Width = 1040;
        Height = 700;
        MinWidth = 900;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.UiFont;
        UseLayoutRounding = true;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(SidePanel());
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Padding = new Thickness(56, 48, 56, 32), Child = _body }, Focusable = false };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;

        _selected = AppHost.Settings.LastWorkspaceId;
        _mode = AppHost.Db is null ? Mode.Connection : Mode.Enter;
        if (_mode == Mode.Enter && List().Count == 0) _mode = Mode.Create;
        Closed += (_, _) => { if (AppHost.Main is null && Application.Current.MainWindow == this) AppHost.Quit(); };
        Render();
    }

    private static FrameworkElement SidePanel()
    {
        var panel = new DockPanel { Background = Theme.ArtBg };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(40, 40, 40, 0) };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Rostok;component/Assets/logo.png")), Width = 40, Height = 40 });
        var bt = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bt.Children.Add(new TextBlock { Text = "Росток", FontSize = 18, FontWeight = FontWeights.ExtraBold, Foreground = Theme.Ink });
        bt.Children.Add(new TextBlock { Text = "динамика развития ребёнка", FontSize = 11, Foreground = Theme.Ink3 });
        brand.Children.Add(bt);
        DockPanel.SetDock(brand, Dock.Top);
        panel.Children.Add(brand);

        var foot = new StackPanel { Margin = new Thickness(40, 0, 40, 32) };
        foot.Children.Add(Ui.Text("Мониторинг речевого и нейропсихологического развития детей для психолога и логопеда: баллы, уровни, динамика, программа коррекции и отчёт для родителей.", null, Theme.Ink2, 13.5, wrap: true));
        foot.Children.Add(Ui.Num($"v{AppHost.Version}", Theme.Faint, 11).Margin(0, 14, 0, 0));
        DockPanel.SetDock(foot, Dock.Bottom);
        panel.Children.Add(foot);

        var art = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(32, 24, 32, 24), VerticalAlignment = VerticalAlignment.Center, OpacityMask = Theme.ArtMask() };
        try { art.Source = new BitmapImage(new Uri("pack://application:,,,/Rostok.Controls;component/Assets/Art/login.jpg")); }
        catch (Exception) { art.Source = new BitmapImage(new Uri("pack://application:,,,/Rostok;component/Assets/logo.png")); art.Width = 180; }
        panel.Children.Add(art);
        return panel;
    }

    private static List<WorkspaceInfo> List()
    {
        try { return AppHost.Db is null ? [] : new Workspaces(AppHost.Db).List(); }
        catch (Exception) { return []; }
    }

    private void Render()
    {
        _body.Child = _mode switch
        {
            Mode.Create => CreateView(),
            Mode.Connection => ConnectionView(),
            _ => EnterView(),
        };
    }

    private FrameworkElement Footer()
    {
        var s = new DashBorder { Sides = Sides.Top, Margin = new Thickness(0, 28, 0, 0), Padding = new Thickness(0, 12, 0, 0) };
        var row = new DockPanel();
        var link = Ui.TextAction("настроить подключение", () => { _mode = Mode.Connection; _error = null; Render(); });
        link.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(link, Dock.Right);
        row.Children.Add(link);
        var info = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        info.Children.Add(Ui.Caps("База данных").Margin(0, 6, 0, 2));
        info.Children.Add(Ui.Num(AppHost.Settings.EffectiveDatabasePath, Theme.Ink3, 12).With(t => t.TextWrapping = TextWrapping.Wrap));
        row.Children.Add(info);
        s.Child = row;
        return s;
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Ui.Upper(string.Concat(parts.Take(2).Select(p => p[0])));
    }

    private FrameworkElement EnterView()
    {
        var list = List();
        var s = new StackPanel { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(Ui.H1("Рабочее пространство"));
        s.Children.Add(Ui.Subtitle("Выберите своё пространство и введите пароль. У каждого сотрудника — свои группы, дети, баллы и настройки; другие сотрудники их не видят."));

        var rows = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        rows.Children.Add(Ui.Line(0, 0, dashed: false, color: Theme.Accent));
        foreach (var w in list)
        {
            var initials = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1),
                BorderBrush = w.Id == _selected ? Theme.Accent : Theme.Dash,
                Background = w.Id == _selected ? Theme.Accent : Brushes.Transparent,
                Child = new TextBlock { Text = Initials(w.Name), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = w.Id == _selected ? Theme.Sheet : Theme.Ink2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = w.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var meta = $"детей: {w.Children}" + (w.LastOpenedAt is { } lo ? $" · открывалось {lo:dd.MM.yyyy}" : $" · создано {w.CreatedAt:dd.MM.yyyy}");
            text.Children.Add(new TextBlock { Text = meta, FontSize = 12, Foreground = Theme.Ink3 });
            var content = new DockPanel();
            content.Children.Add(initials);
            content.Children.Add(text);
            var row = new Button { Style = Ui.Style("TreeRow"), Content = content, MinHeight = 56 };
            Ui.SetIsActive(row, w.Id == _selected);
            var id = w.Id;
            row.Click += (_, _) => { _selected = id; _error = null; Render(); };
            rows.Children.Add(row);
        }
        s.Children.Add(rows);

        var sel = list.FirstOrDefault(w => w.Id == _selected);
        if (sel is not null)
        {
            var pwd = new PasswordBox();
            Ui.SetPlaceholder(pwd, "пароль рабочего пространства");
            void Enter()
            {
                if (pwd.Password.Length == 0) { _error = "Введите пароль."; Render(); return; }
                bool ok;
                try { ok = new Workspaces(AppHost.Db!).Verify(sel.Id, pwd.Password); }
                catch (Exception e) { _error = $"База недоступна: {e.Message}"; Render(); return; }
                if (!ok) { _error = "Неверный пароль."; Render(); return; }
                AppHost.SignIn(sel.Id, sel.Name);
            }
            pwd.KeyDown += (_, e) => { if (e.Key == Key.Enter) Enter(); };
            Ui.SetIsInvalid(pwd, _error is not null);
            var form = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
            form.Children.Add(Ui.Field($"Пароль · {sel.Name}", pwd));
            var btns = Ui.Row(8, Ui.Primary("Войти", Enter, IconKind.Lock));
            btns.Margin = new Thickness(0, 12, 0, 0);
            form.Children.Add(btns);
            if (_error is not null) form.Children.Add(Ui.Status(_error, false).Margin(0, 10, 0, 0));
            s.Children.Add(form);
            Dispatcher.BeginInvoke(() => pwd.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
        else if (list.Count > 0) s.Children.Add(Ui.Faint("Нажмите на своё рабочее пространство, чтобы ввести пароль.").Margin(0, 14, 0, 0));
        else s.Children.Add(Ui.Faint("В этой базе пока нет рабочих пространств — создайте первое.").Margin(0, 14, 0, 0));
        if (_error is not null && sel is null) s.Children.Add(Ui.Status(_error, false).Margin(0, 10, 0, 0));

        var create = Ui.Ghost("Создать новое рабочее пространство", () => { _mode = Mode.Create; _error = null; Render(); }, IconKind.Plus);
        create.Margin = new Thickness(0, 22, 0, 0);
        create.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(create);
        s.Children.Add(Footer());
        return s;
    }

    private FrameworkElement CreateView()
    {
        var s = new StackPanel { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(Ui.H1("Новое рабочее пространство"));
        s.Children.Add(Ui.Subtitle("Пространство — ваша личная рабочая область в общей базе: все группы, дети, баллы, упражнения, словари и настройки сохраняются только в нём. Вход — по паролю."));

        var name = Ui.Input("", "Например: Иванова Мария, логопед");
        var pwd = new PasswordBox();
        Ui.SetPlaceholder(pwd, $"не короче {Workspaces.MinPassword} символов");
        var repeat = new PasswordBox();
        Ui.SetPlaceholder(repeat, "ещё раз");
        var demo = new Checkbox("Добавить группу-пример с вымышленными детьми", true);

        var pwdRow = new Grid();
        pwdRow.ColumnDefinitions.Add(new ColumnDefinition());
        pwdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        pwdRow.ColumnDefinitions.Add(new ColumnDefinition());
        pwdRow.Children.Add(Ui.Field("Пароль", pwd));
        var rf = Ui.Field("Повторите пароль", repeat);
        Grid.SetColumn(rf, 2);
        pwdRow.Children.Add(rf);

        void Create()
        {
            var ws = new Workspaces(AppHost.Db!);
            _error = ws.ValidateName(name.Text) ?? Workspaces.ValidatePassword(pwd.Password, repeat.Password);
            if (_error is not null) { ShowError(); return; }
            try
            {
                var id = ws.Create(name.Text, pwd.Password);
                if (demo.IsChecked == true)
                {
                    var store = Store.Open(AppHost.Db!, id, name.Text.Trim());
                    store.Merge(Demo.Build());
                }
                AppHost.SignIn(id, name.Text.Trim());
            }
            catch (Exception e) { _error = e.Message; ShowError(); }
        }

        var err = new StackPanel();
        void ShowError()
        {
            err.Children.Clear();
            if (_error is not null) err.Children.Add(Ui.Status(_error, false).Margin(0, 10, 0, 0));
        }
        repeat.KeyDown += (_, e) => { if (e.Key == Key.Enter) Create(); };

        var form = Ui.VStack(12,
            Ui.Field("Название рабочего пространства", name),
            pwdRow,
            demo);
        form.Margin = new Thickness(0, 22, 0, 0);
        s.Children.Add(form);
        var hasAny = List().Count > 0;
        var btns = Ui.Row(8, Ui.Primary("Создать и войти", Create, IconKind.Plus), hasAny ? Ui.Ghost("Отмена", () => { _mode = Mode.Enter; _error = null; Render(); }) : null);
        btns.Margin = new Thickness(0, 16, 0, 0);
        s.Children.Add(btns);
        s.Children.Add(err);
        s.Children.Add(Ui.HelpNote(Ui.Faint("Пароль восстановить нельзя: он хранится в базе только в виде хеша. Запишите его в надёжном месте. Сменить пароль можно в «Администрирование → Рабочее пространство».")));
        s.Children.Add(Footer());
        Dispatcher.BeginInvoke(() => name.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        return s;
    }

    private FrameworkElement ConnectionView()
    {
        var s = new StackPanel { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(Ui.H1("Подключение к базе данных"));
        s.Children.Add(Ui.Subtitle(AppHost.Db is null
            ? "Не удалось открыть базу данных. Укажите путь к файлу базы — на этом компьютере или в общей папке сети."
            : "Файл базы, с которым работает программа на этом компьютере. Сотрудники в локальной сети могут работать с одним файлом в общей папке."));
        var editor = new ConnectionEditor(() => { _selected = null; _mode = List().Count == 0 ? Mode.Create : Mode.Enter; Render(); });
        editor.Margin = new Thickness(0, 22, 0, 0);
        s.Children.Add(editor);
        if (AppHost.Db is not null)
        {
            var back = Ui.Ghost("Назад ко входу", () => { _mode = Mode.Enter; Render(); }, IconKind.ArrowBack);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new Thickness(0, 18, 0, 0);
            s.Children.Add(back);
        }
        return s;
    }
}
