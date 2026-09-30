using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rostok.Core;
using Rostok.Core.Storage;

namespace Rostok.Desktop.Services;

// Режим снимков экрана для проверки дизайна и документации:
//   Rostok.exe --shots <папка> [маршрут …]
// Создаётся временная база с рабочим пространством-примером, каждый экран сохраняется в PNG целиком.
// Первое пространство — руководитель; маршрут с приставкой «view:» снимается в пространстве второго сотрудника в режиме просмотра.
public static class Shots
{
    public static readonly string[] DefaultRoutes =
    [
        "login", "/", "/child/{child}", "/child/{child}?tab=program", "/child/{child}?tab=report", "/exam?child={child}",
        "/protocol", "/dynamics", "/library", "/parent/{child}", "/admin/data", "/admin/dicts", "/admin/ui", "/admin/workspace", "/admin/org", "/admin/connection", "/help",
        "/org", "view:/", "view:/exam?child={child}",
    ];

    public static bool TryRun(string[] args)
    {
        var i = Array.IndexOf(args, "--shots");
        if (i < 0 || i + 1 >= args.Length) return false;
        var dir = Path.GetFullPath(args[i + 1]);
        var routes = args.Skip(i + 2).Where(a => !a.StartsWith("--")).ToArray();
        if (routes.Length == 0) routes = DefaultRoutes;
        Directory.CreateDirectory(dir);
        Application.Current.Dispatcher.BeginInvoke(() => Run(dir, routes), DispatcherPriority.Loaded);
        return true;
    }

    private static async void Run(string dir, string[] routes)
    {
        try
        {
            // окно входа в новой, пустой базе: создание первого пространства и пароль администратора базы
            if (routes.Contains("login-new"))
            {
                var emptyPath = Path.Combine(Path.GetTempPath(), $"rostok-shots-{Guid.NewGuid():N}.db");
                var empty = new Database(emptyPath);
                empty.EnsureCreated();
                AppHost.UseDatabase(empty);
                var login = new LoginWindow { Left = -20000, Top = 0, ShowActivated = false };
                login.Show();
                await Settle();
                var lc = (FrameworkElement)login.Content;
                Save(lc, Path.Combine(dir, "login-new.png"), lc.ActualWidth, lc.ActualHeight);
                login.Close();
                try { File.Delete(emptyPath); } catch (IOException) { }
            }

            var dbPath = Path.Combine(Path.GetTempPath(), $"rostok-shots-{Guid.NewGuid():N}.db");
            var db = new Database(dbPath);
            db.EnsureCreated();
            var ws = new Workspaces(db);
            var id = ws.Create("Иванова Мария, логопед", "1234");
            Store.Open(db, id, "Иванова Мария, логопед").Merge(Demo.Build());
            var other = ws.Create("Петрова Анна, психолог", "1234");
            Store.Open(db, other, "Петрова Анна, психолог").Merge(Demo.Build());
            ws.SetAdminPassword(null, "admin-1");
            ws.SetRole(id, Roles.Supervisor, "admin-1");
            AppHost.UseDatabase(db);

            foreach (var r in routes.Where(r => r == "login"))
            {
                var login = new LoginWindow { Left = -20000, Top = 0, ShowActivated = false };
                login.Show();
                await Settle();
                var lc = (FrameworkElement)login.Content;
                Save(lc, Path.Combine(dir, "login.png"), lc.ActualWidth, lc.ActualHeight);
                login.Close();
            }

            AppHost.SignInForShots(id, "Иванова Мария, логопед");
            var main = AppHost.Main!;
            main.Left = -20000;
            foreach (var route in routes.Where(r => r is not ("login" or "login-new")))
            {
                var view = route.StartsWith("view:");
                if (view && !AppHost.IsViewing) AppHost.ViewAs(ws.Get(other)!);
                if (!view && AppHost.IsViewing) AppHost.ReturnToOwn();
                var data = AppHost.Store!.Data;
                var child = data.ChildrenOf(data.Groups[0].Id)[1].Id;
                var path = (view ? route[5..] : route).Replace("{child}", child);
                var parts = path.Split('?');
                var query = parts.Length > 1
                    ? parts[1].Split('&').Select(kv => kv.Split('=')).Select(kv => (kv[0], (string?)kv[1])).ToArray()
                    : [];
                main.Navigate(Route.Of(parts[0], query));
                await Settle();
                var name = route.Replace("view:/", "view-").Trim('/').Replace("{child}", "child").Replace('/', '-').Replace('?', '-').Replace('=', '-').Replace('&', '-');
                main.SaveFullPage(Path.Combine(dir, $"{(name.Length == 0 ? "children" : name.EndsWith('-') ? name + "children" : name)}.png"));
            }
            main.Close();
            File.Delete(dbPath);
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(dir, "error.txt"), e.ToString());
        }
        Application.Current.Shutdown();
    }

    private static async Task Settle()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(150);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    public static void Save(FrameworkElement? el, string file, double width, double height)
    {
        if (el is null) return;
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Rostok.Controls.Theme.Paper, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, el.ActualWidth, el.ActualHeight) }, null, new Rect(0, 0, el.ActualWidth, el.ActualHeight));
        }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
