using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Rostok.Core.Storage;

// Файл базы SQLite. Путь — из настроек подключения: файл в папке программы или в общей папке локальной сети.
// Если файла нет, он создаётся при первом открытии вместе со всеми таблицами.
public sealed class Database
{
    public const int SchemaVersion = 1;

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
        Exec(c, tx, "INSERT INTO meta(key, value) VALUES ('schema_version', $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", ("$v", SchemaVersion.ToString()));
        Exec(c, tx, "INSERT OR IGNORE INTO meta(key, value) VALUES ('created_at', $v)", ("$v", DateTime.Now.ToString("s")));
        tx.Commit();
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
