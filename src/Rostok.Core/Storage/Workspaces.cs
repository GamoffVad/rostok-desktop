namespace Rostok.Core.Storage;

public sealed record WorkspaceInfo(string Id, string Name, string OwnerId, string OwnerName, DateTime CreatedAt, DateTime? LastOpenedAt, int Children);

public sealed record AccessEntry(DateTime At, string ViewerName, string Action)
{
    public string ActionText => Action switch
    {
        "view" => "просмотр пространства",
        "password_reset" => "сброс пароля",
        _ => Action,
    };
}

// Рабочие пространства (хранилища): у каждого — свои группы, дети, баллы, библиотека, словари и настройки.
// Пространство принадлежит пользователю. Пользователь видит только свои пространства, администраторы — все.
public sealed class Workspaces(Database db)
{
    private const string Select =
        "SELECT w.id, w.name, w.owner_id, COALESCE(NULLIF(u.name, ''), u.login, ''), w.created_at, w.last_opened_at, " +
        "(SELECT COUNT(*) FROM children ch WHERE ch.workspace_id = w.id) FROM workspaces w LEFT JOIN users u ON u.id = w.owner_id";

    private static WorkspaceInfo Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.GetString(3),
        DateTime.Parse(r.GetString(4)), r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5)), r.GetInt32(6));

    public List<WorkspaceInfo> List()
    {
        using var c = db.Open();
        return Database.Query(c, Select + " ORDER BY w.name COLLATE NOCASE", Map);
    }

    // Что видно пользователю при входе: свои пространства, а администраторам — все.
    public List<WorkspaceInfo> ListFor(UserInfo user) => user.IsAdmin ? List() : List().Where(w => w.OwnerId == user.Id).ToList();

    public WorkspaceInfo? Get(string id) => List().FirstOrDefault(w => w.Id == id);

    // Изменять данные может только владелец; администратор открывает чужие пространства только для просмотра.
    public static bool CanEdit(UserInfo user, WorkspaceInfo w) => w.OwnerId == user.Id;
    public static bool CanOpen(UserInfo user, WorkspaceInfo w) => user.IsAdmin || w.OwnerId == user.Id;

    public string? ValidateName(string name, string? exceptId = null)
    {
        name = name.Trim();
        if (name.Length == 0) return "Введите название рабочего пространства.";
        if (name.Length > 80) return "Название слишком длинное: не больше 80 символов.";
        if (List().Any(w => w.Id != exceptId && string.Equals(w.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            return "Рабочее пространство с таким названием уже есть.";
        return null;
    }

    public string Create(string name, string ownerId)
    {
        var error = ValidateName(name);
        if (error is not null) throw new InvalidOperationException(error);
        var id = Ids.New() + Ids.New();
        using var c = db.Open();
        Database.Exec(c, null, "INSERT INTO workspaces(id, name, owner_id, created_at) VALUES ($id, $name, $owner, $at)",
            ("$id", id), ("$name", name.Trim()), ("$owner", ownerId), ("$at", DateTime.Now.ToString("s")));
        return id;
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

    // Передать пространство другому пользователю (администрирование → пользователи).
    public void SetOwner(string id, string ownerId)
    {
        using var c = db.Open();
        Database.Exec(c, null, "UPDATE workspaces SET owner_id = $o WHERE id = $id", ("$o", ownerId), ("$id", id));
    }

    // Удаление пространства вместе со всеми его данными.
    public void Delete(string id)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        foreach (var table in new[] { "scores", "notes", "programs", "library", "settings", "children", "groups", "periods", "access_log" })
            Database.Exec(c, tx, $"DELETE FROM {table} WHERE workspace_id = $id", ("$id", id));
        Database.Exec(c, tx, "DELETE FROM workspaces WHERE id = $id", ("$id", id));
        tx.Commit();
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
}
