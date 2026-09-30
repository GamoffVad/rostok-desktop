using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Rostok.Core.Storage;

// Файл базы SQLite. Путь — из настроек подключения: файл в папке программы или в общей папке локальной сети.
// Если файла нет, он создаётся при первом открытии вместе со всеми таблицами.
public sealed class Database
{
    // 1 — первая версия; 2 — роль пространства (руководитель) и журнал просмотров;
    // 3 — пользователи с логином и паролем, пространства без паролей принадлежат пользователям
    public const int SchemaVersion = 3;

    public string Path { get; }
    private readonly string _connectionString;

    public Database(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // без пула: файл в общей папке не удерживается открытым между операциями
            Pooling = false,
            DefaultTimeout = 30,
        }.ToString();
    }

    public bool Exists => File.Exists(Path);

    // Создаёт папку и файл, если их нет, и доводит схему до текущей версии.
    public void EnsureCreated()
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var c = Open();
        using var tx = c.BeginTransaction();
        Exec(c, tx, LoadSchema());
        Migrate(c, tx);
        Exec(c, tx, "INSERT INTO meta(key, value) VALUES ('schema_version', $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", ("$v", SchemaVersion.ToString()));
        Exec(c, tx, "INSERT OR IGNORE INTO meta(key, value) VALUES ('created_at', $v)", ("$v", DateTime.Now.ToString("s")));
        tx.Commit();
    }

    // Базы прежних версий: CREATE TABLE IF NOT EXISTS не меняет существующих таблиц — доводим их сами.
    private static void Migrate(SqliteConnection c, SqliteTransaction tx)
    {
        // главный администратор есть в каждой базе: логин admin, пароль admin (программа напомнит сменить)
        var mainId = Scalar(c, tx, "SELECT id FROM users WHERE role = $r", ("$r", UserRoles.MainAdmin)) as string;
        if (mainId is null)
        {
            var login = UniqueLogin(c, tx, UserRoles.DefaultLogin);
            mainId = Users.Insert(c, tx, login, "Главный администратор", UserRoles.DefaultPassword, UserRoles.MainAdmin);
        }

        var columns = Columns(c, tx, "workspaces");
        if (!columns.Contains("owner_id")) Exec(c, tx, "ALTER TABLE workspaces ADD COLUMN owner_id TEXT");

        // версии 1.0–1.1: у каждого пространства был свой пароль. Пространство становится пользователем
        // с тем же паролем (логин — название пространства); руководитель — администратором.
        if (columns.Contains("password_hash"))
        {
            var hasRole = columns.Contains("role");
            var rows = new List<(string Id, string Name, string Hash, string Salt, int It, string Role)>();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"SELECT id, name, password_hash, password_salt, iterations, {(hasRole ? "role" : "'employee'")} FROM workspaces WHERE owner_id IS NULL";
                using var r = cmd.ExecuteReader();
                while (r.Read()) rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetString(5)));
            }
            foreach (var w in rows)
            {
                var login = UniqueLogin(c, tx, w.Name);
                var uid = Users.InsertHashed(c, tx, login, w.Name, w.Role == "supervisor" ? UserRoles.Admin : UserRoles.User, w.Hash, w.Salt, w.It);
                Exec(c, tx, "UPDATE workspaces SET owner_id = $u WHERE id = $id", ("$u", uid), ("$id", w.Id));
            }
            foreach (var col in new[] { "password_hash", "password_salt", "iterations", "role" })
                if (columns.Contains(col)) Exec(c, tx, $"ALTER TABLE workspaces DROP COLUMN {col}");
        }
        Exec(c, tx, "UPDATE workspaces SET owner_id = $m WHERE owner_id IS NULL OR owner_id NOT IN (SELECT id FROM users)", ("$m", mainId));
        // пароль администратора базы (версия 1.1) больше не нужен: роли назначает администратор
        Exec(c, tx, "DELETE FROM meta WHERE key IN ('admin_hash', 'admin_salt', 'admin_iterations')");
    }

    private static HashSet<string> Columns(SqliteConnection c, SqliteTransaction tx, string table)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read()) columns.Add(r.GetString(1));
        return columns;
    }

    private static string UniqueLogin(SqliteConnection c, SqliteTransaction tx, string wanted)
    {
        var login = wanted.Trim();
        if (login.Length == 0) login = "пользователь";
        var candidate = login;
        for (var i = 2; Convert.ToInt32(Scalar(c, tx, "SELECT COUNT(*) FROM users WHERE login = $l COLLATE NOCASE", ("$l", candidate))) > 0; i++)
            candidate = $"{login} {i}";
        return candidate;
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        // журнал DELETE вместо WAL: WAL не работает, когда файл лежит в общей папке сети
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 15000; PRAGMA journal_mode = DELETE;";
        cmd.ExecuteNonQuery();
        return c;
    }

    // Проверка подключения для «Администрирование → Подключение»: открыть, прочитать, при необходимости создать.
    public static (bool Ok, string Message) Test(string path, bool create)
    {
        try
        {
            var db = new Database(path);
            var existed = db.Exists;
            if (!existed && !create) return (false, "Файла базы по этому пути нет. Он будет создан при подключении.");
            db.EnsureCreated();
            using var c = db.Open();
            var workspaces = Convert.ToInt32(Scalar(c, null, "SELECT COUNT(*) FROM workspaces"));
            return (true, existed
                ? $"Подключение работает. Рабочих пространств в базе: {workspaces}."
                : "Файл базы создан, подключение работает. Рабочих пространств пока нет.");
        }
        catch (Exception e)
        {
            return (false, $"Не удалось открыть базу: {e.Message}");
        }
    }

    private static string LoadSchema()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Rostok.Core.Resources.schema.sql")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static int Exec(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    public static object? Scalar(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd.ExecuteScalar();
    }

    public static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }
}
