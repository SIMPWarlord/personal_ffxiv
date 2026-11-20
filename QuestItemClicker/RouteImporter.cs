using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuestItemClicker;

/// <summary>Imports local authored Questionable quest paths, not bundled data.</summary>
internal static partial class RouteImporter
{
    [GeneratedRegex(@"^(\d+)_", RegexOptions.Compiled)]
    private static partial Regex QuestFilenamePattern();

    public static List<QuestItemRule> Import(string root, out int fileCount, out int skipped)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"QuestPaths folder does not exist: {root}");
        var found = new HashSet<QuestItemRule>();
        fileCount = 0;
        skipped = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var match = QuestFilenamePattern().Match(Path.GetFileName(path));
            if (!match.Success || !uint.TryParse(match.Groups[1].Value, out var questId))
                continue;
            fileCount++;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (!Get(document.RootElement, "QuestSequence", out var seqArray) ||
                    seqArray.ValueKind != JsonValueKind.Array) continue;
                foreach (var seq in seqArray.EnumerateArray())
                {
                    if (!Number(seq, "Sequence", out var seqNum) ||
                        !Get(seq, "Steps", out var steps) || steps.ValueKind != JsonValueKind.Array) continue;
                    foreach (var step in steps.EnumerateArray())
                    {
                        if (!String(step, "InteractionType", out var interaction)) continue;
                        if (interaction == "UseItem")
                        {
                            if (!Number(step, "ItemId", out var item) ||
                                !Number(step, "TerritoryId", out var territory))
                            { skipped++; continue; }
                            Number(step, "DataId", out var dataId);
                            Position(step, out var x, out var y, out var z);
                            if (dataId == 0 && x is null) { skipped++; continue; }
                            found.Add(new QuestItemRule(questId, (int)seqNum, territory, dataId,
                                item, "UseItem", x, y, z));
                        }
                        else if (interaction == "Combat" &&
                                 Get(step, "CombatItemUse", out var combat) && combat.ValueKind == JsonValueKind.Object)
                        {
                            if (!Number(combat, "ItemId", out var item) ||
                                !Number(step, "TerritoryId", out var territory))
                            { skipped++; continue; }
                            var ids = new HashSet<uint>();
                            if (Get(step, "KillEnemyDataIds", out var killIds) && killIds.ValueKind == JsonValueKind.Array)
                                foreach (var id in killIds.EnumerateArray())
                                    if (id.TryGetUInt32(out var value)) ids.Add(value);
                            if (Get(step, "ComplexCombatDatas", out var complexIds) && complexIds.ValueKind == JsonValueKind.Array)
                                foreach (var obj in complexIds.EnumerateArray())
                                    if (Number(obj, "DataId", out var value)) ids.Add(value);
                            String(combat, "Condition", out var condition);
                            Number(combat, "Value", out var valueNum);
                            foreach (var dataId in ids.Where(v => v > 0))
                                found.Add(new QuestItemRule(questId, (int)seqNum, territory,
                                    dataId, item, "CombatItemUse", null, null, null, condition, valueNum));
                        }
                    }
                }
            }
            catch (JsonException) { skipped++; }
            catch (IOException) { skipped++; }
        }
        return found.ToList();
    }

    private static bool Get(JsonElement element, string name, out JsonElement result)
    {
        result = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out result);
    }
    private static bool Number(JsonElement element, string name, out uint result)
    {
        result = default;
        return Get(element, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt32(out result);
    }
    private static bool String(JsonElement element, string name, out string? result)
    {
        result = null;
        if (!Get(element, name, out var value) || value.ValueKind != JsonValueKind.String) return false;
        result = value.GetString();
        return result != null;
    }
    private static void Position(JsonElement element, out float? x, out float? y, out float? z)
    {
        x = y = z = null;
        if (!Get(element, "Position", out var p) || p.ValueKind != JsonValueKind.Object) return;
        if (Get(p, "X", out var vx) && vx.ValueKind == JsonValueKind.Number && vx.TryGetSingle(out var fx) &&
            Get(p, "Y", out var vy) && vy.ValueKind == JsonValueKind.Number && vy.TryGetSingle(out var fy) &&
            Get(p, "Z", out var vz) && vz.ValueKind == JsonValueKind.Number && vz.TryGetSingle(out var fz))
        { x = fx; y = fy; z = fz; }
    }
}
