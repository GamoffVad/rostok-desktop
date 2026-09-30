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

// Вход: логин и пароль пользователя, затем выбор рабочего пространства (хранилища) или создание нового.
// Пользователь видит только свои пространства; администраторы — все, чужие открываются только для просмотра.
public sealed class LoginWindow : Window
{
    private enum Mode { Login, Choose, Create, Connection }

    private readonly Border _body = new();
    private Mode _mode;
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

        _mode = AppHost.Db is null ? Mode.Connection : AppHost.User is null ? Mode.Login : Mode.Choose;
        // закрыли окно входа, а окна программы нет — выходим. Application.MainWindow к этому моменту WPF уже сбрасывает,
        // поэтому сверяемся с тем, какое окно входа считается текущим
        Closed += (_, _) => { if (AppHost.Main is null && AppHost.IsCurrentLogin(this)) AppHost.Quit(); };
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

    private void Go(Mode mode)
    {
        _mode = mode;
        _error = null;
        Render();
    }

    private void Render()
    {
        _body.Child = _mode switch
        {
            Mode.Choose when AppHost.User is not null => ChooseView(),
            Mode.Create when AppHost.User is not null => CreateView(),
            Mode.Connection => ConnectionView(),
            _ => LoginView(),
        };
    }

    private FrameworkElement Footer()
    {
        var s = new DashBorder { Sides = Sides.Top, Margin = new Thickness(0, 28, 0, 0), Padding = new Thickness(0, 12, 0, 0) };
        var row = new DockPanel();
        var link = Ui.TextAction("настроить подключение", () => Go(Mode.Connection));
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
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => char.IsLetterOrDigit(p[0])).ToArray();
        return Ui.Upper(string.Concat(parts.Take(2).Select(p => p[0])));
    }

    // ── Вход по логину и паролю ─────────────────────────
    private FrameworkElement LoginView()
    {
        var s = new StackPanel { MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(Ui.H1("Вход"));
        s.Children.Add(Ui.Subtitle("Введите логин и пароль. После входа откроется список ваших рабочих пространств."));

        var login = Ui.Input(AppHost.Settings.LastLogin ?? "", "логин");
        var pwd = new PasswordBox();
        Ui.SetPlaceholder(pwd, "пароль");
        Ui.SetIsInvalid(pwd, _error is not null);
        var err = new StackPanel();
        void Enter()
        {
            if (login.Text.Trim().Length == 0 || pwd.Password.Length == 0) { Show("Введите логин и пароль."); return; }
            UserInfo? user;
            try { user = new Users(AppHost.Db!).Authenticate(login.Text, pwd.Password); }
            catch (Exception e) { Show($"База недоступна: {e.Message}"); return; }
            if (user is null) { Show("Неверный логин или пароль."); pwd.Clear(); pwd.Focus(); return; }
            AppHost.SignIn(user);
            Go(Mode.Choose);
        }
        void Show(string text)
        {
            _error = text;
            Ui.SetIsInvalid(pwd, true);
            err.Children.Clear();
            err.Children.Add(Ui.Status(text, false).Margin(0, 10, 0, 0));
        }
        login.KeyDown += (_, e) => { if (e.Key == Key.Enter) pwd.Focus(); };
        pwd.KeyDown += (_, e) => { if (e.Key == Key.Enter) Enter(); };

        var form = Ui.VStack(12, Ui.Field("Логин", login), Ui.Field("Пароль", pwd));
        form.Margin = new Thickness(0, 22, 0, 0);
        s.Children.Add(form);
        var btn = Ui.Row(8, Ui.Primary("Войти", Enter, IconKind.Lock));
        btn.Margin = new Thickness(0, 16, 0, 0);
        s.Children.Add(btn);
        s.Children.Add(err);

        // новая база: пока есть только главный администратор, подсказываем стандартный вход
        var fresh = false;
        try { fresh = new Users(AppHost.Db!).List().Count == 1; } catch (Exception) { /* база недоступна — подсказки нет */ }
        s.Children.Add(Ui.HelpNote(Ui.Faint(fresh
            ? $"База новая. Первый вход — главный администратор: логин {UserRoles.DefaultLogin}, пароль {UserRoles.DefaultPassword}. Затем смените пароль и добавьте пользователей в «Администрирование → Пользователи»."
            : "Логин и пароль выдаёт администратор. Забыли пароль — обратитесь к администратору: он задаст новый.")));
        s.Children.Add(Footer());
        Dispatcher.BeginInvoke(() => { if (login.Text.Length > 0) pwd.Focus(); else login.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);
        return s;
    }

    // ── Выбор рабочего пространства ─────────────────────
    private FrameworkElement ChooseView()
    {
        var user = AppHost.User!;
        List<WorkspaceInfo> list;
        try { list = new Workspaces(AppHost.Db!).ListFor(user); }
        catch (Exception e) { list = []; _error = $"База недоступна: {e.Message}"; }

        var s = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(Ui.H1("Рабочие пространства"));
        s.Children.Add(Ui.Subtitle(user.IsAdmin
            ? "Все рабочие пространства базы. Свои открываются для работы, пространства других пользователей — только для просмотра; открытие записывается в их журнал."
            : "Ваши рабочие пространства: в каждом — свои группы, дети, баллы и настройки. Другие пользователи их не видят."));

        var who = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var logout = Ui.TextAction("сменить пользователя", AppHost.SignOut);
        DockPanel.SetDock(logout, Dock.Right);
        who.Children.Add(logout);
        who.Children.Add(Ui.Rich(Ui.Run("Вы вошли как "), Ui.Run(user.DisplayName, Theme.Ink, FontWeights.SemiBold), Ui.Run($" · {user.RoleTitle} · логин {user.Login}", Theme.Ink3))
            .With(t => { t.FontSize = 13; t.TextWrapping = TextWrapping.Wrap; }));
        s.Children.Add(who);

        try
        {
            if (new Users(AppHost.Db!).HasDefaultPassword(user))
                s.Children.Add(Ui.HelpNote(Ui.Faint($"У главного администратора стандартный пароль «{UserRoles.DefaultPassword}». Смените его в «Администрирование → Учётная запись»."), amber: true).Margin(0, 12, 0, 0));
        }
        catch (Exception) { /* база недоступна */ }

        var own = list.Where(w => w.OwnerId == user.Id).ToList();
        var others = list.Where(w => w.OwnerId != user.Id).ToList();
        var rows = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        void Section(string? title, List<WorkspaceInfo> items)
        {
            if (title is not null) rows.Children.Add(Ui.Caps(title).Margin(0, rows.Children.Count > 0 ? 18 : 0, 0, 6));
            rows.Children.Add(Ui.Line(0, 0, dashed: false, color: Theme.Accent));
            foreach (var w in items) rows.Children.Add(WorkspaceRow(w, w.OwnerId == user.Id));
        }
        if (own.Count > 0 || others.Count == 0) Section(user.IsAdmin ? "Мои пространства" : null, own);
        if (own.Count == 0) rows.Children.Add(Ui.Faint(others.Count > 0 ? "Своих пространств пока нет." : "У вас пока нет рабочих пространств — создайте первое.").Margin(0, 12, 0, 0));
        if (others.Count > 0) Section("Пространства других пользователей · только просмотр", others);
        s.Children.Add(rows);
        if (_error is not null) s.Children.Add(Ui.Status(_error, false).Margin(0, 10, 0, 0));

        var create = Ui.Ghost("Создать рабочее пространство", () => Go(Mode.Create), IconKind.Plus);
        create.Margin = new Thickness(0, 22, 0, 0);
        create.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(create);
        s.Children.Add(Footer());
        return s;
    }

    private static FrameworkElement WorkspaceRow(WorkspaceInfo w, bool own)
    {
        var last = w.Id == AppHost.Settings.LastWorkspaceId;
        var initials = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1),
            BorderBrush = last ? Theme.Accent : Theme.Dash,
            Background = last ? Theme.Accent : Brushes.Transparent,
            Child = new TextBlock { Text = Initials(w.Name), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = last ? Theme.Sheet : Theme.Ink2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = w.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var meta = (own ? "" : $"владелец: {w.OwnerName} · ") + $"детей: {w.Children}" + (w.LastOpenedAt is { } lo ? $" · открывалось {lo:dd.MM.yyyy}" : $" · создано {w.CreatedAt:dd.MM.yyyy}");
        text.Children.Add(new TextBlock { Text = meta, FontSize = 12, Foreground = Theme.Ink3, TextTrimming = TextTrimming.CharacterEllipsis });
        var content = new DockPanel();
        var open = new RIconText(own ? IconKind.Arrow : IconKind.Eye, own ? "открыть" : "смотреть");
        DockPanel.SetDock(open, Dock.Right);
        content.Children.Add(open);
        content.Children.Add(initials);
        content.Children.Add(text);
        var row = new Button { Style = Ui.Style("TreeRow"), Content = content, MinHeight = 56, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Ui.SetIsActive(row, last);
        Ui.AutomationName(row, w.Name);
        row.Click += (_, _) => AppHost.OpenWorkspace(w);
        return row;
    }

    // Подпись «открыть →» справа в строке пространства
    private sealed class RIconText : StackPanel
    {
        public RIconText(IconKind icon, string text)
        {
            Orientation = Orientation.Horizontal;
            VerticalAlignment = VerticalAlignment.Center;
            Margin = new Thickness(12, 0, 4, 0);
            Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = Theme.Ink3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            Children.Add(new Rostok.Controls.Icon(icon, 14) { Foreground = Theme.Ink3, VerticalAlignment = VerticalAlignment.Center });
        }
    }

    // ── Новое рабочее пространство ──────────────────────
    private FrameworkElement CreateView()
    {
        var user = AppHost.User!;
        var s = new StackPanel { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(Ui.H1("Новое рабочее пространство"));
        s.Children.Add(Ui.Subtitle("Пространство — отдельное хранилище в общей базе: свои группы, дети, баллы, упражнения, словари и настройки. Оно будет принадлежать вам; видеть его смогут только вы и администраторы."));

        var name = Ui.Input("", "Например: Иванова Мария, логопед");
        var demo = new Checkbox("Добавить группу-пример с вымышленными детьми", true);
        var err = new StackPanel();
        void Create()
        {
            var ws = new Workspaces(AppHost.Db!);
            _error = ws.ValidateName(name.Text);
            if (_error is null)
            {
                try
                {
                    var id = ws.Create(name.Text, user.Id);
                    if (demo.IsChecked == true) Store.Open(AppHost.Db!, id, name.Text.Trim()).Merge(Demo.Build());
                    AppHost.OpenWorkspace(ws.Get(id)!);
                    return;
                }
                catch (Exception e) { _error = e.Message; }
            }
            err.Children.Clear();
            err.Children.Add(Ui.Status(_error!, false).Margin(0, 10, 0, 0));
        }
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Create(); };

        var form = Ui.VStack(12, Ui.Field("Название рабочего пространства", name), demo);
        form.Margin = new Thickness(0, 22, 0, 0);
        s.Children.Add(form);
        var btns = Ui.Row(8, Ui.Primary("Создать и открыть", Create, IconKind.Plus), Ui.Ghost("Отмена", () => Go(Mode.Choose)));
        btns.Margin = new Thickness(0, 16, 0, 0);
        s.Children.Add(btns);
        s.Children.Add(err);
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
            : "Файл базы, с которым работает программа на этом компьютере. Пользователи в локальной сети могут работать с одним файлом в общей папке."));
        // в другой базе свои пользователи: после подключения — снова вход по логину
        var editor = new ConnectionEditor(() => Go(Mode.Login));
        editor.Margin = new Thickness(0, 22, 0, 0);
        s.Children.Add(editor);
        if (AppHost.Db is not null)
        {
            var back = Ui.Ghost("Назад", () => Go(AppHost.User is null ? Mode.Login : Mode.Choose), IconKind.ArrowBack);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new Thickness(0, 18, 0, 0);
            s.Children.Add(back);
        }
        return s;
    }
}
