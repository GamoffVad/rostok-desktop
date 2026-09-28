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
    public static Store? Store { get; private set; }
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
        Store = Store.Open(Db!, workspaceId, name);
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
        new Workspaces(Db!).MarkOpened(workspaceId);
        Store = Store.Open(Db!, workspaceId, name);
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
        Store = null;
        ShowLogin();
        main?.Close();
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
