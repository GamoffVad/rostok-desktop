namespace Rostok.Core;

public sealed class Group
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class Contact
{
    public string Id { get; set; } = Ids.New();
    public string Kind { get; set; } = "";
    public string Value { get; set; } = "";

    public Contact Clone() => new() { Id = Id, Kind = Kind, Value = Value };
}

public sealed class Relative
{
    public string Id { get; set; } = Ids.New();
    public string Role { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Legal { get; set; }
    public string Note { get; set; } = "";
    public List<Contact> Phones { get; set; } = [];
    public List<Contact> Emails { get; set; } = [];
    public List<Contact> Addresses { get; set; } = [];

    public Relative Clone() => new()
    {
        Id = Id, Role = Role, Name = Name, Legal = Legal, Note = Note,
        Phones = Phones.Select(p => p.Clone()).ToList(),
        Emails = Emails.Select(p => p.Clone()).ToList(),
        Addresses = Addresses.Select(p => p.Clone()).ToList(),
    };
}

public sealed class Child
{
    public string Id { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Name { get; set; } = "";
    // ISO «ГГГГ-ММ-ДД» или пустая строка
    public string BirthDate { get; set; } = "";
    public string Note { get; set; } = "";
    public string Tpmpk { get; set; } = "";
    public List<Relative> Relatives { get; set; } = [];

    public Child Clone() => new()
    {
        Id = Id, GroupId = GroupId, Name = Name, BirthDate = BirthDate, Note = Note, Tpmpk = Tpmpk,
        Relatives = Relatives.Select(r => r.Clone()).ToList(),
    };
}

public sealed class Period
{
    public string Id { get; set; } = "";
    public string Year { get; set; } = "";
    // «НГ» — начало года, «КГ» — конец года
    public string Point { get; set; } = "";

    public string Label => $"{Year} · {Point}";
    // Короткая подпись среза для графиков и шапок таблиц: «НГ 25/26».
    public string Short => Year.Length >= 4 ? $"{Point} {Year.Substring(2, 2)}/{Year[^2..]}" : Point;
}

// Программа коррекции ребёнка на срезе: порог, исключённые пробы, свои дополнения и рекомендации.
public sealed class ChildProgram
{
    public int? Threshold { get; set; }
    public List<string> Off { get; set; } = [];
    public Dictionary<string, string> Notes { get; set; } = [];
    public string Recs { get; set; } = "";
    public string Home { get; set; } = "";

    public ChildProgram Clone() => new()
    {
        Threshold = Threshold, Off = [.. Off], Notes = new(Notes), Recs = Recs, Home = Home,
    };
}

public static class Ids
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    public static string New()
    {
        Span<char> s = stackalloc char[8];
        for (var i = 0; i < s.Length; i++) s[i] = Alphabet[Random.Shared.Next(Alphabet.Length)];
        return new string(s);
    }
}
