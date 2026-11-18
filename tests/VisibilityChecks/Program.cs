using SrankFlag;
using System.Text.Json;

string root = Path.Combine(Path.GetTempPath(), "MagicBucket-visibility-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new Exception(message);
}
try
{
    var store = new HuntVisibility(root);
    Check(store.Hidden.Count == 0, "A new install hid hunts by default");
    store.Hide("Narrow-rift");
    store.Hide("Narrow-rift");
    store.Hide("");
    store.Hide("Safat");
    Check(store.Hidden.Count == 2, "Hidden kinds duplicated");
    var restarted = new HuntVisibility(root);
    Check(restarted.IsHidden("Narrow-rift") && restarted.IsHidden("Safat"), "Hidden kinds were lost after reload");
    // Kind keys must suppress every server/DC record, rather than one row key.
    (string En, string World)[] rows = [("Narrow-rift", "水晶塔"), ("Narrow-rift", "红玉海"), ("Safat", "白金幻象"), ("Brontes", "红玉海")];
    Check(rows.Where(row => !restarted.IsHidden(row.En)).Count() == 1, "A hidden kind leaked through another server");
    restarted.Restore("Narrow-rift");
    var restored = new HuntVisibility(root);
    Check(!restored.IsHidden("Narrow-rift") && restored.IsHidden("Safat"), "Restoring one kind changed another");
    Check(rows.Where(row => !restored.IsHidden(row.En)).Count() == 3, "Restoring did not release all server rows");
    restored.RestoreAll();
    Check(new HuntVisibility(root).Hidden.Count == 0, "Restore all did not persist");
    string blocker = Path.Combine(root, "blocked-directory");
    File.WriteAllText(blocker, "keep");
    var blocked = new HuntVisibility(blocker);
    bool failed = false;
    try { blocked.Hide("Safat"); } catch (IOException) { failed = true; }
    Check(failed && !blocked.IsHidden("Safat"), "A failed save was reported as hidden in memory");
    Check(File.ReadAllText(blocker) == "keep", "Failed save overwrote a different file");
    int warnings = 0;
    File.WriteAllText(Path.Combine(root, "hidden_hunts.json"), "broken JSON");
    Check(new HuntVisibility(root, _ => warnings++).Hidden.Count == 0 && warnings == 1, "Corrupt settings did not produce a bounded warning");
    File.WriteAllText(Path.Combine(root, "hidden_hunts.json"), JsonSerializer.Serialize(new string?[] { "Safat", null, "", "Safat" }));
    Check(new HuntVisibility(root).Hidden.Count == 1, "Old/invalid entries broke loading");
    Console.WriteLine($"PASS: {checks} visibility persistence checks (all servers, reload, restore, failed saves, corrupt input).");
}
finally
{
    // Only this newly created unique test directory is removed.
    string resolved = Path.GetFullPath(root);
    if (Path.GetDirectoryName(resolved) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) &&
        Path.GetFileName(resolved).StartsWith("MagicBucket-visibility-", StringComparison.Ordinal))
        Directory.Delete(resolved, true);
}
