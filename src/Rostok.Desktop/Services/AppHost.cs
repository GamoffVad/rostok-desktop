using System.Reflection;
using System.Windows;
using Rostok.Core.Storage;

namespace Rostok.Desktop.Services;

// Состояние программы: настройки компьютера, подключение к базе, вошедший пользователь, открытое рабочее пространство и окна.
public static class AppHost
{
    public static AppSettings Settings { get; private set; } = new();
    public static Database? Db { get; private set; }
    public static string? DbError { get; private set; }
    // Вошедший пользователь (вход по логину и паролю).
    public static UserInfo? User { get; private set; }
    public static bool IsAdmin => User?.IsAdmin == true;
    // Активное пространство: выбранное при входе или — у администратора — открытое с экрана «Организация».
    public static Store? Store { get; private set; }
    // Пространство, выбранное при входе: к нему ведёт кнопка «Вернуться».
    public static Store? HomeStore { get; private set; }
    // Чужое пространство (администратор смотрит пространство другого пользователя): только чтение.
    public static bool IsViewing => Store?.ReadOnly == true;
    public static bool CanReturnHome => Store is not null && HomeStore is not null && !ReferenceEquals(Store, HomeStore);
    // Владелец открытого пространства — для плашки режима просмотра.
    public static string OwnerName { get; private set; } = "";
    public static MainWindow? Main { get; private set; }
    private static LoginWindow? _login;
    private static string _homeOwner = "";

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

    // Открывает базу по пути из настроек; если файла нет — создаёт его со всеми таблицами и главным администратором.
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
    internal static void UseUser(UserInfo? user) => User = user;
    // самопроверка и снимки не трогают настройки этого компьютера
    internal static bool Ephemeral { get; set; }

    internal static bool IsCurrentLogin(LoginWindow w) => ReferenceEquals(_login, w);

    public static void ShowLogin()
    {
        _login = new LoginWindow();
        Application.Current.MainWindow = _login;
        _login.Show();
    }

    // Логин и пароль проверены: окно входа переходит к выбору рабочего пространства.
    public static void SignIn(UserInfo user)
    {
        User = user;
        Settings.LastLogin = user.Login;
        SaveSettings();
    }

    public static void RefreshUser()
    {
        if (User is not null && Db is not null) User = new Users(Db).Get(User.Id) ?? User;
    }

    // Открыть пространство, выбранное при входе. Своё — для работы, чужое (только администратору) — для просмотра.
    public static void OpenWorkspace(WorkspaceInfo w)
    {
        if (OpenStore(w) is null) return;
        Settings.LastWorkspaceId = w.Id;
        SaveSettings();
        Main = new MainWindow();
        Application.Current.MainWindow = Main;
        Main.Show();
        _login?.Close();
        _login = null;
    }

    // Вход без окна входа и без записи настроек компьютера — для снимков экрана и самопроверки.
    internal static void OpenForShots(UserInfo user, WorkspaceInfo w)
    {
        User = user;
        if (OpenStore(w) is null) return;
        Main = new MainWindow { ShowActivated = false };
        Main.Show();
    }

    private static Store? OpenStore(WorkspaceInfo w)
    {
        if (User is null || Db is null || !Workspaces.CanOpen(User, w)) return null;
        var store = Open(w);
        Store = HomeStore = store;
        OwnerName = _homeOwner = w.OwnerName;
        return store;
    }

    private static Store Open(WorkspaceInfo w)
    {
        var ws = new Workspaces(Db!);
        if (Workspaces.CanEdit(User!, w))
        {
            ws.MarkOpened(w.Id);
            return Store.Open(Db!, w.Id, w.Name);
        }
        // чужое пространство: только чтение, открытие записывается в журнал владельца
        var view = Store.Open(Db!, w.Id, w.Name, readOnly: true);
        view.Blocked += () => Main?.ShowBlocked();
        ws.LogAccess(w.Id, User!.Id, User.DisplayName, "view");
        return view;
    }

    // Администратор открывает пространство с экрана «Организация».
    public static void ViewAs(WorkspaceInfo target)
    {
        if (User is null || Db is null || !Workspaces.CanOpen(User, target)) return;
        if (target.Id == HomeStore?.WorkspaceId) { ReturnHome(); return; }
        var store = Open(target);
        OwnerName = target.OwnerName;
        if (store.ReadOnly) Store = store;
        else
        {
            // своё пространство становится основным
            Store = HomeStore = store;
            _homeOwner = target.OwnerName;
        }
        Main?.Navigate(Services.Route.Of("/"));
    }

    public static void ReturnHome()
    {
        if (HomeStore is null) return;
        Store = HomeStore;
        OwnerName = _homeOwner;
        HomeStore.Activate();
        Main?.Navigate(Services.Route.Of(IsAdmin ? "/org" : "/"));
    }

    // Сменить рабочее пространство: окно программы закрывается, открывается выбор пространства того же пользователя.
    public static void SwitchWorkspace()
    {
        var main = Main;
        Main = null;
        Store = HomeStore = null;
        RefreshUser();
        ShowLogin();
        main?.Close();
    }

    // Выход: следующий вход — снова с логином и паролем. Прежнее окно входа (выбор пространства) закрывается.
    public static void SignOut()
    {
        var main = Main;
        var login = _login;
        Main = null;
        Store = HomeStore = null;
        User = null;
        ShowLogin();
        main?.Close();
        login?.Close();
    }

    // Новое подключение к базе: сохраняем путь этого компьютера (null — путь по умолчанию) и открываем базу заново.
    public static bool Reconnect(string? path)
    {
        Settings.DatabasePath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        Settings.LastWorkspaceId = null;
        Settings.Save();
        User = null;
        return OpenDatabase();
    }

    private static void SaveSettings()
    {
        if (Ephemeral) return;
        try { Settings.Save(); } catch (Exception) { /* настройки компьютера не записались — не критично */ }
    }

    public static void Quit() => Application.Current.Shutdown();
}
