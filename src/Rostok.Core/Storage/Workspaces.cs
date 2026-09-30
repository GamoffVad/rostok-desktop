using System.Security.Cryptography;
using System.Text;

namespace Rostok.Core.Storage;

public static class Roles
{
    public const string Employee = "employee";
    // Руководитель (старший специалист): видит пространства всех сотрудников без их паролей — только просмотр.
    public const string Supervisor = "supervisor";
}

public sealed record WorkspaceInfo(string Id, string Name, DateTime CreatedAt, DateTime? LastOpenedAt, int Children, string Role = Roles.Employee)
{
    public bool IsSupervisor => Role == Roles.Supervisor;
}

public sealed record AccessEntry(DateTime At, string ViewerName, string Action)
{
    public string ActionText => Action switch
    {
        "view" => "просмотр пространства",
        "password_reset" => "сброс пароля",
        _ => Action,
    };
}

// Рабочие пространства сотрудников: у каждого — свои группы, дети, баллы, библиотека, словари и настройки.
// Вход по паролю; в базе хранится только соль и хеш PBKDF2-SHA256.
// Роль «руководитель» назначается паролем администратора базы — сотрудник не может выдать её себе сам.
public sealed class Workspaces(Database db)
{
    private const int Iterations = 210_000;
    public const int MinPassword = 4;

    public List<WorkspaceInfo> List()
    {
        using var c = db.Open();
        return Database.Query(c,
            "SELECT w.id, w.name, w.created_at, w.last_opened_at, (SELECT COUNT(*) FROM children ch WHERE ch.workspace_id = w.id), w.role FROM workspaces w ORDER BY w.name COLLATE NOCASE",
            r => new WorkspaceInfo(r.GetString(0), r.GetString(1), DateTime.Parse(r.GetString(2)),
                r.IsDBNull(3) ? null : DateTime.Parse(r.GetString(3)), r.GetInt32(4), r.IsDBNull(5) ? Roles.Employee : r.GetString(5)));
    }

    public WorkspaceInfo? Get(string id) => List().FirstOrDefault(w => w.Id == id);

    public string? ValidateName(string name, string? exceptId = null)
    {
        name = name.Trim();
        if (name.Length == 0) return "Введите название рабочего пространства.";
        if (name.Length > 80) return "Название слишком длинное: не больше 80 символов.";
        if (List().Any(w => w.Id != exceptId && string.Equals(w.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            return "Рабочее пространство с таким названием уже есть.";
        return null;
    }

    public static string? ValidatePassword(string password, string repeat)
    {
        if (password.Length < MinPassword) return $"Пароль — не короче {MinPassword} символов.";
        if (password != repeat) return "Пароли не совпадают.";
        return null;
    }

    public string Create(string name, string password)
    {
        var error = ValidateName(name);
        if (error is not null) throw new InvalidOperationException(error);
        var id = Ids.New() + Ids.New();
        var (hash, salt) = Hash(password);
        using var c = db.Open();
        Database.Exec(c, null,
            "INSERT INTO workspaces(id, name, password_hash, password_salt, iterations, created_at) VALUES ($id, $name, $hash, $salt, $it, $at)",
            ("$id", id), ("$name", name.Trim()), ("$hash", hash), ("$salt", salt), ("$it", Iterations), ("$at", DateTime.Now.ToString("s")));
        return id;
    }

    public bool Verify(string id, string password)
    {
        using var c = db.Open();
        var row = Database.Query(c, "SELECT password_hash, password_salt, iterations FROM workspaces WHERE id = $id",
            r => (Hash: r.GetString(0), Salt: r.GetString(1), It: r.GetInt32(2)), ("$id", id)).FirstOrDefault();
        return row.Hash is not null && Matches(password, row.Hash, row.Salt, row.It);
    }

    public void MarkOpened(string id)
    {
        using var c = db.Open();
        Database.Exec(c, null, "UPDATE workspaces SET last_opened_at = $at WHERE id = $id", ("$at", DateTime.Now.ToString("s")), ("$id", id));
    }

    public void Rename(string id, string name)
    {
        var error = ValidateName(name, id);
        if (error is not null) throw new InvalidOperationException(error);
        using var c = db.Open();
        Database.Exec(c, null, "UPDATE workspaces SET name = $name WHERE id = $id", ("$name", name.Trim()), ("$id", id));
    }

    public void ChangePassword(string id, string current, string next)
    {
        if (!Verify(id, current)) throw new InvalidOperationException("Текущий пароль указан неверно.");
        SetPassword(id, next);
    }

    // Сброс забытого пароля руководителем: старый пароль не нужен, событие попадает в журнал сотрудника.
    public void ResetPassword(string id, string next, string viewerId, string viewerName)
    {
        SetPassword(id, next);
        LogAccess(id, viewerId, viewerName, "password_reset");
    }

    private void SetPassword(string id, string next)
    {
        var (hash, salt) = Hash(next);
        using var c = db.Open();
        Database.Exec(c, null, "UPDATE workspaces SET password_hash = $hash, password_salt = $salt, iterations = $it WHERE id = $id",
            ("$hash", hash), ("$salt", salt), ("$it", Iterations), ("$id", id));
    }

    // Удаление пространства вместе со всеми его данными — только с паролем.
    public void Delete(string id, string password)
    {
        if (!Verify(id, password)) throw new InvalidOperationException("Пароль указан неверно.");
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        foreach (var table in new[] { "scores", "notes", "programs", "library", "settings", "children", "groups", "periods", "access_log" })
            Database.Exec(c, tx, $"DELETE FROM {table} WHERE workspace_id = $id", ("$id", id));
        Database.Exec(c, tx, "DELETE FROM workspaces WHERE id = $id", ("$id", id));
        tx.Commit();
    }

    // ── Пароль администратора базы ──────────────────────
    public bool HasAdminPassword()
    {
        using var c = db.Open();
        return Database.Scalar(c, null, "SELECT value FROM meta WHERE key = 'admin_hash'") is string;
    }

    public bool VerifyAdmin(string password)
    {
        using var c = db.Open();
        string? Meta(string key) => Database.Scalar(c, null, "SELECT value FROM meta WHERE key = $k", ("$k", key)) as string;
        var hash = Meta("admin_hash");
        var salt = Meta("admin_salt");
        return hash is not null && salt is not null && int.TryParse(Meta("admin_iterations"), out var it) && Matches(password, hash, salt, it);
    }

    // Первый раз пароль задаётся без текущего; дальше — только зная текущий.
    public void SetAdminPassword(string? current, string next)
    {
        if (HasAdminPassword() && (current is null || !VerifyAdmin(current)))
            throw new InvalidOperationException("Текущий пароль администратора указан неверно.");
        var (hash, salt) = Hash(next);
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        foreach (var (k, v) in new[] { ("admin_hash", hash), ("admin_salt", salt), ("admin_iterations", Iterations.ToString()) })
            Database.Exec(c, tx, "INSERT INTO meta(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", ("$k", k), ("$v", v));
        tx.Commit();
    }

    public void SetRole(string id, string role, string adminPassword)
    {
        if (role is not (Roles.Employee or Roles.Supervisor)) throw new ArgumentException("Неизвестная роль", nameof(role));
        if (!VerifyAdmin(adminPassword)) throw new InvalidOperationException("Пароль администратора указан неверно.");
        using var c = db.Open();
        Database.Exec(c, null, "UPDATE workspaces SET role = $r WHERE id = $id", ("$r", role), ("$id", id));
    }

    // ── Журнал просмотров ───────────────────────────────
    public void LogAccess(string workspaceId, string viewerId, string viewerName, string action)
    {
        using var c = db.Open();
        Database.Exec(c, null, "INSERT INTO access_log(workspace_id, viewer_id, viewer_name, action, at) VALUES ($w, $v, $n, $a, $at)",
            ("$w", workspaceId), ("$v", viewerId), ("$n", viewerName), ("$a", action), ("$at", DateTime.Now.ToString("s")));
    }

    public List<AccessEntry> AccessLog(string workspaceId, int limit = 20)
    {
        using var c = db.Open();
        return Database.Query(c, "SELECT at, viewer_name, action FROM access_log WHERE workspace_id = $w ORDER BY id DESC LIMIT $l",
            r => new AccessEntry(DateTime.Parse(r.GetString(0)), r.GetString(1), r.GetString(2)), ("$w", workspaceId), ("$l", limit));
    }

    private static bool Matches(string password, string hash, string salt, int iterations)
    {
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(salt), iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(hash));
    }

    private static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }
}
