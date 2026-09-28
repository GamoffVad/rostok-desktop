namespace Rostok.Core;

// Родители и родственники ребёнка: заготовки, очистка перед сохранением, текст для таблиц и выгрузок.
public static class Relatives
{
    public static Contact Entry(string kind = "") => new() { Kind = kind };

    public static Relative New(string? role = null)
    {
        role ??= M.Roles.FirstOrDefault() ?? "";
        return new Relative
        {
            Role = role,
            Legal = role is "мама" or "папа",
            Phones = [Entry(M.PhoneKinds.FirstOrDefault() ?? "")],
        };
    }

    // Следующая роль, которой ещё нет среди контактов: мама → папа → бабушка…
    public static string NextRole(IEnumerable<Relative> list)
    {
        var used = list.Select(r => r.Role).ToHashSet();
        return M.Roles.FirstOrDefault(r => !used.Contains(r)) ?? M.Roles.FirstOrDefault() ?? "";
    }

    private static List<Contact> Filled(IEnumerable<Contact> list) =>
        list.Select(x => new Contact { Id = x.Id, Kind = x.Kind.Trim(), Value = x.Value.Trim() }).Where(x => x.Value.Length > 0).ToList();

    // Пустые телефоны, адреса и почты не сохраняются; контакт без ФИО и без телефона — тоже.
    public static List<Relative> Clean(IEnumerable<Relative> list) => list
        .Select(r => new Relative
        {
            Id = r.Id, Role = r.Role.Trim(), Name = r.Name.Trim(), Legal = r.Legal, Note = r.Note.Trim(),
            Phones = Filled(r.Phones), Emails = Filled(r.Emails), Addresses = Filled(r.Addresses),
        })
        .Where(r => r.Name.Length > 0 || r.Phones.Count > 0)
        .ToList();

    public static string PhonesText(Relative r) => string.Join(", ", r.Phones.Select(p => p.Kind.Length > 0 ? $"{p.Value} ({p.Kind})" : p.Value));
    public static string EmailsText(Relative r) => string.Join(", ", r.Emails.Select(e => e.Value));
    public static string AddressesText(Relative r) => string.Join("; ", r.Addresses.Select(a => a.Kind.Length > 0 ? $"{a.Kind}: {a.Value}" : a.Value));
}
