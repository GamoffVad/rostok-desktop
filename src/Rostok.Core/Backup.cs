using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rostok.Core;

// Резервная копия .json — тот же формат, что у веб-версии «Ростка»:
// копию из браузера можно восстановить в настольном приложении и наоборот.
public static class Backup
{
    public static string Export(WorkspaceData d)
    {
        var scores = new JsonObject();
        foreach (var (child, byPeriod) in d.Scores)
        {
            var periods = new JsonObject();
            foreach (var (period, items) in byPeriod)
            {
                var obj = new JsonObject();
                foreach (var (item, value) in items)
                {
                    // баллы шкалы в веб-версии — числа
                    var isScale = M.SectionOfItem.TryGetValue(item, out var s) && s.Kind == Kinds.Scale && int.TryParse(value, out _);
                    obj[item] = isScale ? JsonValue.Create(int.Parse(value)) : JsonValue.Create(value);
                }
                periods[period] = obj;
            }
            scores[child] = periods;
        }
        var root = new JsonObject
        {
            ["version"] = 1,
            ["groups"] = JsonSerializer.SerializeToNode(d.Groups, Storage.Store.Json),
            ["children"] = JsonSerializer.SerializeToNode(d.Children, Storage.Store.Json),
            ["periods"] = JsonSerializer.SerializeToNode(d.Periods.Select(p => new { p.Id, p.Year, p.Point }), Storage.Store.Json),
            ["scores"] = scores,
            ["notes"] = JsonSerializer.SerializeToNode(d.Notes, Storage.Store.Json),
            ["programs"] = JsonSerializer.SerializeToNode(d.Programs, Storage.Store.Json),
            ["dicts"] = d.Dicts.DeepClone(),
        };
        if (d.Library is not null) root["library"] = JsonSerializer.SerializeToNode(d.Library, Storage.Store.Json);
        return root.ToJsonString();
    }

    public static WorkspaceData Import(string json)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException(); }
        catch (JsonException) { throw new FormatException(); }
        if (root["groups"] is not JsonArray groups || root["children"] is not JsonArray children || root["scores"] is not JsonObject scores)
            throw new FormatException();

        string S(JsonNode? n) => n is JsonValue v ? (v.TryGetValue<string>(out var s) ? s : v.ToJsonString()) : "";
        var data = new WorkspaceData
        {
            Groups = groups.OfType<JsonObject>().Select(g => new Group { Id = S(g["id"]), Name = S(g["name"]) }).ToList(),
            Children = children.OfType<JsonObject>().Select(c => new Child
            {
                Id = S(c["id"]), GroupId = S(c["groupId"]), Name = S(c["name"]), BirthDate = S(c["birthDate"]),
                Note = S(c["note"]), Tpmpk = S(c["tpmpk"]),
                Relatives = (c["relatives"] as JsonArray ?? []).OfType<JsonObject>().Select(Storage.Store.NormalizeRelative).ToList(),
            }).ToList(),
            Periods = (root["periods"] as JsonArray ?? []).OfType<JsonObject>().Select(p => new Period { Id = S(p["id"]), Year = S(p["year"]), Point = S(p["point"]) }).ToList(),
        };
        foreach (var (child, byPeriod) in scores)
        {
            if (byPeriod is not JsonObject periods) continue;
            data.Scores[child] = periods.Where(p => p.Value is JsonObject).ToDictionary(
                p => p.Key,
                p => ((JsonObject)p.Value!).Where(i => i.Value is JsonValue).ToDictionary(i => i.Key, i => S(i.Value)));
        }
        if (root["notes"] is JsonObject notes)
            foreach (var (child, byPeriod) in notes)
                if (byPeriod is JsonObject o) data.Notes[child] = o.ToDictionary(p => p.Key, p => S(p.Value));
        if (root["programs"] is JsonObject programs)
            foreach (var (child, byPeriod) in programs)
                if (byPeriod is JsonObject o)
                    data.Programs[child] = o.Where(p => p.Value is JsonObject).ToDictionary(p => p.Key, p => ParseProgram((JsonObject)p.Value!));
        if (root["library"] is JsonObject lib) data.Library = lib.ToDictionary(p => p.Key, p => S(p.Value));
        if (root["dicts"] is JsonObject dicts) data.Dicts = (JsonObject)dicts.DeepClone();
        return data;
    }

    private static ChildProgram ParseProgram(JsonObject o)
    {
        string S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        return new ChildProgram
        {
            Threshold = o["threshold"] is JsonValue t && t.TryGetValue<int>(out var th) ? th : null,
            Off = (o["off"] as JsonArray ?? []).Select(x => S(x)).Where(x => x.Length > 0).ToList(),
            Notes = (o["notes"] as JsonObject ?? []).ToDictionary(p => p.Key, p => S(p.Value)),
            Recs = S(o["recs"]),
            Home = S(o["home"]),
        };
    }
}
