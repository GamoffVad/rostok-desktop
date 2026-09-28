namespace Rostok.Desktop.Services;

// Маршрут экрана — как хеш веб-версии: «/child/abc?tab=program».
public sealed record Route(string Path, IReadOnlyDictionary<string, string> Params)
{
    public string[] Parts => Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    public string Section => Parts.Length > 0 ? Parts[0] : "";
    public string? Part(int i) => Parts.Length > i ? Parts[i] : null;
    public string? Param(string key) => Params.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    public static Route Of(string path, params (string Key, string? Value)[] query) =>
        new(path, query.Where(q => !string.IsNullOrEmpty(q.Value)).ToDictionary(q => q.Key, q => q.Value!));

    public override string ToString() =>
        Params.Count == 0 ? Path : $"{Path}?{string.Join("&", Params.Select(p => $"{p.Key}={p.Value}"))}";
}
