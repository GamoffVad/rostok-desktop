using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rostok.Controls;
using Rostok.Desktop.Pages;
using Rostok.Desktop.Services;
using RIcon = Rostok.Controls.Icon;

namespace Rostok.Desktop;

// Окно программы: закреплённая шапка с разделами, содержимое с прокруткой, закреплённый футер с версией.
public sealed class MainWindow : Window
{
    private static readonly (string Path, string Label, string[] Match)[] Nav =
    [
        ("/", "Дети", ["", "child"]),
        ("/exam", "Обследование", ["exam"]),
        ("/protocol", "Протокол группы", ["protocol"]),
        ("/dynamics", "Динамика", ["dynamics"]),
        ("/library", "Упражнения", ["library"]),
    ];

    private readonly StackPanel _nav = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly StackPanel _tools = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer _scroll;
    private readonly Border _page = new() { MaxWidth = 1320, Padding = new Thickness(0, 0, 0, 40) };
    private readonly TextBlock _status;
    private readonly TextBlock _footerText = new() { FontSize = 12, Foreground = Theme.Ink3, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Stack<Route> _history = new();
    private Route _route = Route.Of("/");
    private PageBase? _current;
    private readonly FrameworkElement _head;
    private readonly FrameworkElement _footer;

    public MainWindow()
    {
        Title = $"Росток — {AppHost.Store!.WorkspaceName}";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Rostok;component/Assets/rostok.ico"));
        Background = Theme.Paper;
        Width = 1400;
        Height = 920;
        MinWidth = 1080;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.UiFont;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        // ── шапка ──
        var brand = new Button { Style = Ui.Style("ButtonBase"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 28, 8) };
        var brandRow = new StackPanel { Orientation = Orientation.Horizontal };
        brandRow.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Rostok;component/Assets/logo.png")), Width = 40, Height = 40 });
        var brandText = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock { Text = "Росток", FontSize = 18, FontWeight = FontWeights.ExtraBold, Foreground = Theme.Ink });
        brandText.Children.Add(new TextBlock { Text = "динамика развития ребёнка", FontSize = 11, Foreground = Theme.Ink3 });
        brandRow.Children.Add(brandText);
        brand.Content = brandRow;
        brand.Click += (_, _) => Navigate(Route.Of("/"));

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(brand);
        Grid.SetColumn(_nav, 1);
        top.Children.Add(_nav);
        Grid.SetColumn(_tools, 2);
        _tools.Margin = new Thickness(0, 0, 0, 8);
        top.Children.Add(_tools);
        var topbar = new DashBorder { Sides = Sides.Bottom, Padding = new Thickness(0, 10, 0, 0), Child = top };

        _status = Ui.Status("", false);
        _status.Margin = new Thickness(0, 10, 0, 0);
        _status.Visibility = Visibility.Collapsed;

        // ── футер ──
        var version = new TextBlock
        {
            Text = $"v{AppHost.Version}", FontFamily = Theme.MonoFont, FontSize = 11, Foreground = Theme.Faint, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"Сборка от {AppHost.BuildDate:dd.MM.yyyy}",
        };
        var footRow = new DockPanel { MaxWidth = 1320 };
        DockPanel.SetDock(version, Dock.Right);
        footRow.Children.Add(version);
        footRow.Children.Add(_footerText);
        var footer = new DashBorder { Sides = Sides.Top, Height = 40, Padding = new Thickness(40, 0, 40, 0), Child = footRow, Background = Theme.Paper };

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = new Border { Padding = new Thickness(40, 0, 40, 0), Child = _page },
        };

        var head = _head = new Border { Padding = new Thickness(40, 0, 40, 0), Background = Theme.Paper, Child = new StackPanel { MaxWidth = 1320, Children = { topbar, _status } } };
        _footer = footer;
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(footer);
        root.Children.Add(_scroll);
        Content = root;

        PreviewMouseDown += (_, e) => { if (e.ChangedButton == MouseButton.XButton1) { e.Handled = true; Back(); } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.System && e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) { e.Handled = true; Back(); }
        };
        Closed += (_, _) => { if (AppHost.Main == this) AppHost.Quit(); };

        Navigate(Route.Of("/"), push: false);
    }

    public Route CurrentRoute => _route;

    public void Navigate(Route route, bool push = true)
    {
        if (push && _route.ToString() != route.ToString()) _history.Push(_route);
        _route = route;
        var empty = AppHost.Store!.Data.Groups.Count == 0;
        var section = route.Section;
        _current = section switch
        {
            "help" => new HelpPage(route),
            "admin" => new AdminPage(route),
            "library" => new LibraryPage(route),
            _ when empty => new WelcomePage(route),
            "child" => new ChildPage(route),
            "exam" => new ExamPage(route),
            "protocol" => new ProtocolPage(route),
            "dynamics" => new DynamicsPage(route),
            "parent" => new ParentReportPage(route),
            _ => new ChildrenPage(route),
        };
        BuildTopbar(section == "parent" && !empty, section);
        _current.Refresh();
        _page.Child = _current;
        _scroll.ScrollToTop();
        _current.Focus();
    }

    public void Back()
    {
        if (_history.Count == 0) return;
        Navigate(_history.Pop(), push: false);
    }

    // Пересобрать текущий экран (после восстановления копии, смены словарей и т. п.)
    public void Reload() => Navigate(_route, push: false);

    private void BuildTopbar(bool parentMode, string section)
    {
        _nav.Children.Clear();
        _tools.Children.Clear();
        // Режим показа родителям: без разделов, чтобы не было видно других детей.
        if (parentMode) return;
        var first = true;
        foreach (var (path, label, match) in Nav)
        {
            var b = new Button { Style = Ui.Style("NavTab"), Content = label, Margin = new Thickness(first ? 0 : 18, 0, 0, 0) };
            Ui.SetIsActive(b, match.Contains(section));
            var p = path;
            b.Click += (_, _) => Navigate(Route.Of(p));
            _nav.Children.Add(b);
            first = false;
        }

        var ws = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        ws.Children.Add(new RIcon(IconKind.User, 14) { Foreground = Theme.Ink3, VerticalAlignment = VerticalAlignment.Center });
        ws.Children.Add(new TextBlock { Text = AppHost.Store!.WorkspaceName, FontSize = 12.5, Foreground = Theme.Ink2, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });
        ws.ToolTip = "Рабочее пространство: все данные и настройки сохраняются только в нём";
        _tools.Children.Add(ws);

        var admin = Ui.IconButton(IconKind.Gear, "Администрирование: данные, словари, подключение к базе", () => Navigate(Route.Of("/admin/data")));
        Ui.SetIsActive(admin, section == "admin");
        var help = Ui.IconButton(IconKind.Help, "Справка по методике", () => Navigate(Route.Of("/help")));
        Ui.SetIsActive(help, section == "help");
        var logout = Ui.IconButton(IconKind.Logout, "Сменить рабочее пространство", AppHost.SignOut);
        admin.Margin = help.Margin = new Thickness(0, 0, 6, 0);
        _tools.Children.Add(admin);
        _tools.Children.Add(help);
        _tools.Children.Add(logout);
    }

    // Снимок экрана целиком — с шапкой, всей прокручиваемой страницей и футером (режим --shots).
    public void SaveFullPage(string file)
    {
        var content = (FrameworkElement)_scroll.Content;
        static BitmapSource Snap(FrameworkElement el)
        {
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth), (int)Math.Ceiling(el.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // элемент рисуется от своего начала координат, без смещения внутри окна
                var offset = VisualTreeHelper.GetOffset(el);
                dc.PushTransform(new TranslateTransform(-offset.X, -offset.Y));
                dc.Pop();
            }
            rtb.Render(el);
            return rtb;
        }
        var parts = new[] { Snap(_head), Snap(content), Snap(_footer) };
        var w = parts.Max(p => p.PixelWidth);
        var h = parts.Sum(p => p.PixelHeight);
        var final = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Theme.Paper, null, new Rect(0, 0, w, h));
            double y = 0;
            foreach (var p in parts) { dc.DrawImage(p, new Rect(0, y, p.PixelWidth, p.PixelHeight)); y += p.PixelHeight; }
        }
        final.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(final));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    public void UpdateStatus()
    {
        var store = AppHost.Store;
        if (store is null) return;
        _footerText.Text = $"Росток — динамика развития ребёнка · рабочее пространство «{store.WorkspaceName}» · база: {store.Db.Path}";
        _footerText.ToolTip = store.Db.Path;
        if (store.SaveError is not null)
        {
            _status.Text = $"Не удалось сохранить изменения в базе: {store.SaveError}. Проверьте подключение в разделе «Администрирование → Подключение».";
            _status.Visibility = Visibility.Visible;
        }
        else _status.Visibility = Visibility.Collapsed;
    }
}
