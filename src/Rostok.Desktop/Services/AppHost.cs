using System.Reflection;
using System.Windows;
using Rostok.Core.Storage;

namespace Rostok.Desktop.Services;

// Состояние программы: настройки компьютера, подключение к базе, открытое рабочее пространство и окна.
public static class AppHost
{
    public static AppSettings Settings { get; private set; } = new();
    public static Database? Db { get; private set; }
    public static string? DbError { get; private set; }
    // Активное пространство: своё или — у руководителя — открытое для просмотра пространство сотрудника.
    public static Store? Store { get; private set; }
    // Своё пространство вошедшего сотрудника.
    public static Store? OwnStore { get; private set; }
    // Вошедший — руководитель (старший специалист): видит пространства всех сотрудников.
    public static bool IsSupervisor { get; private set; }
    public static bool IsViewing => Store is not null && OwnStore is not null && !ReferenceEquals(Store, OwnStore);
    public static MainWindow? Main { get; private set; }
    private static LoginWindow? _login;

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
            var plus = v.IndexOf('+');
            return plus > 0 ? v[..plus] : v;
        }
    }

    public static DateTime BuildDate => File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location);

    public static void Start()
    {
        Settings = AppSettings.Load();
        OpenDatabase();
        ShowLogin();
    }

    // Открывает базу по пути из настроек; если файла нет — создаёт его со всеми таблицами.
    public static bool OpenDatabase()
    {
        try
        {
            var db = new Database(Settings.EffectiveDatabasePath);
            db.EnsureCreated();
            Db = db;
            DbError = null;
            return true;
        }
        catch (Exception e)
        {
            Db = null;
            DbError = e.Message;
            return false;
        }
    }

    internal static void UseDatabase(Database db) { Db = db; DbError = null; }

    // Вход без записи настроек компьютера — для режима снимков экрана.
    internal static void SignInForShots(string workspaceId, string name)
    {
        Store = OwnStore = Store.Open(Db!, workspaceId, name);
        IsSupervisor = new Workspaces(Db!).Get(workspaceId)?.IsSupervisor ?? false;
        Main = new MainWindow { ShowActivated = false };
        Main.Show();
    }

    public static void ShowLogin()
    {
        _login = new LoginWindow();
        Application.Current.MainWindow = _login;
        _login.Show();
    }

    public static void SignIn(string workspaceId, string name)
    {
        var ws = new Workspaces(Db!);
        ws.MarkOpened(workspaceId);
        Store = OwnStore = Store.Open(Db!, workspaceId, name);
        IsSupervisor = ws.Get(workspaceId)?.IsSupervisor ?? false;
        Settings.LastWorkspaceId = workspaceId;
        try { Settings.Save(); } catch (Exception) { /* настройки компьютера не записались — не критично */ }
        Main = new MainWindow();
        Application.Current.MainWindow = Main;
        Main.Show();
        _login?.Close();
        _login = null;
    }

    // Смена рабочего пространства: окно программы закрывается, снова показывается вход.
    public static void SignOut()
    {
        var main = Main;
        Main = null;
        Store = OwnStore = null;
        IsSupervisor = false;
        ShowLogin();
        main?.Close();
    }

    // Руководитель открывает пространство сотрудника только для просмотра; открытие записывается в журнал сотрудника.
    public static void ViewAs(WorkspaceInfo target)
    {
        if (!IsSupervisor || OwnStore is null || Db is null) return;
        if (target.Id == OwnStore.WorkspaceId) { ReturnToOwn(); return; }
        var view = Store.Open(Db, target.Id, target.Name, readOnly: true);
        view.Blocked += () => Main?.ShowBlocked();
        new Workspaces(Db).LogAccess(target.Id, OwnStore.WorkspaceId, OwnStore.WorkspaceName, "view");
        Store = view;
        Main?.Navigate(Services.Route.Of("/"));
    }

    public static void ReturnToOwn()
    {
        if (OwnStore is null) return;
        Store = OwnStore;
        OwnStore.Activate();
        Main?.Navigate(Services.Route.Of("/org"));
    }

    // Новое подключение к базе: сохраняем путь этого компьютера (null — путь по умолчанию) и открываем базу заново.
    public static bool Reconnect(string? path)
    {
        Settings.DatabasePath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        Settings.LastWorkspaceId = null;
        Settings.Save();
        return OpenDatabase();
    }

    public static void Quit() => Application.Current.Shutdown();
}
