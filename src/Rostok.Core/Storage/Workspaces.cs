using System.Security.Cryptography;
using System.Text;

namespace Rostok.Core.Storage;

public sealed record WorkspaceInfo(string Id, string Name, DateTime CreatedAt, DateTime? LastOpenedAt, int Children);

// Рабочие пространства сотрудников: у каждого — свои группы, дети, баллы, библиотека, словари и настройки.
// Вход по паролю; в базе хранится только соль и хеш PBKDF2-SHA256.
public sealed class Workspaces(Database db)
{
    private const int Iterations = 210_000;
    public const int MinPassword = 4;

    public List<WorkspaceInfo> List()
    {
        using var c = db.Open();
        return Database.Query(c,
            "SELECT w.id, w.name, w.created_at, w.last_opened_at, (SELECT COUNT(*) FROM children ch WHERE ch.workspace_id = w.id) FROM workspaces w ORDER BY w.name COLLATE NOCASE",
            r => new WorkspaceInfo(r.GetString(0), r.GetString(1), DateTime.Parse(r.GetString(2)),
                r.IsDBNull(3) ? null : DateTime.Parse(r.GetString(3)), r.GetInt32(4)));
    }

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
        if (row.Hash is null) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(row.Salt), row.It, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(row.Hash));
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
        foreach (var table in new[] { "scores", "notes", "programs", "library", "settings", "children", "groups", "periods" })
            Database.Exec(c, tx, $"DELETE FROM {table} WHERE workspace_id = $id", ("$id", id));
        Database.Exec(c, tx, "DELETE FROM workspaces WHERE id = $id", ("$id", id));
        tx.Commit();
    }

    private static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }
}
