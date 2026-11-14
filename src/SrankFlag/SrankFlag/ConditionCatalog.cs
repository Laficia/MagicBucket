using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SrankFlag;

internal sealed class ConditionDefinition
{
    public string En { get; set; } = "";
    public string Cn { get; set; } = "";
    public string Method { get; set; } = "";
    public string[] Notes { get; set; } = [];
}
internal static class ConditionCatalog
{
    private sealed class FileData { public List<ConditionDefinition> Hunts { get; set; } = []; }
    internal static IReadOnlyList<ConditionDefinition> All { get; } = Load();
    private static List<ConditionDefinition> Load()
    {
        using var stream = typeof(ConditionCatalog).Assembly.GetManifestResourceStream("SrankFlag.HuntConditions.json") ?? throw new InvalidDataException("缺少 S 怪条件目录");
        return JsonSerializer.Deserialize<FileData>(stream)?.Hunts ?? [];
    }
    internal static ConditionDefinition? Find(string en) => All.FirstOrDefault(x => x.En == en || x.Cn == en);
}

internal sealed class ConditionSubscriptions
{
    private readonly string path;
    private HashSet<string> disabled = ConditionCatalog.All.Where(x => !ConditionForecast.HasSpec(x.En)).Select(x => x.En).ToHashSet(StringComparer.Ordinal);
    internal ConditionSubscriptions(string directory)
    {
        path = Path.Combine(directory, "condition_subscriptions.json");
        try { if (File.Exists(path)) disabled = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(path)) ?? new(StringComparer.Ordinal); } catch { }
    }
    internal bool Contains(string en) => !disabled.Contains(en);
    internal void Toggle(string en) { if (!disabled.Remove(en)) disabled.Add(en); Save(); }
    internal void SelectAll(bool value) { disabled = value ? new(StringComparer.Ordinal) : ConditionCatalog.All.Select(x => x.En).ToHashSet(StringComparer.Ordinal); Save(); }
    private void Save() => File.WriteAllText(path, JsonSerializer.Serialize(disabled));
}
