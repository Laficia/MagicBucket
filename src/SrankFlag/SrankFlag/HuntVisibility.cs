using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SrankFlag;

// Stable English hunt keys apply to every world/DC, independent of list filters.
internal sealed class HuntVisibility
{
    private readonly string file;
    private HashSet<string> hidden = new(StringComparer.Ordinal);
    internal IReadOnlyCollection<string> Hidden => hidden;

    internal HuntVisibility(string directory, Action<string>? warn = null)
    {
        file = Path.Combine(directory, "hidden_hunts.json");
        try
        {
            if (File.Exists(file))
                hidden = new((JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? [])
                    .Where(key => !string.IsNullOrWhiteSpace(key)), StringComparer.Ordinal);
        }
        catch (Exception ex) { warn?.Invoke("隐藏列表读取失败：" + ex.Message); }
    }

    internal bool IsHidden(string en) => hidden.Contains(en);
    internal void Hide(string en)
    {
        if (string.IsNullOrWhiteSpace(en) || hidden.Contains(en)) return;
        Commit(new HashSet<string>(hidden, StringComparer.Ordinal) { en });
    }
    internal void Restore(string en)
    {
        if (!hidden.Contains(en)) return;
        var next = new HashSet<string>(hidden, StringComparer.Ordinal);
        next.Remove(en);
        Commit(next);
    }
    internal void RestoreAll()
    {
        if (hidden.Count != 0) Commit(new(StringComparer.Ordinal));
    }
    private void Commit(HashSet<string> next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(next.OrderBy(key => key, StringComparer.Ordinal)));
        File.Move(temporary, file, true);
        hidden = next;
    }
}
