using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace QuestItemClicker;

/// <summary>
/// Reads the game's own tracked Duty List quest item shortcuts.
/// This tells us which item icon(s) belong to a tracked quest, not whether
/// any given objective accepts an item. It must not be used to brute-force.
/// </summary>
internal static unsafe class DutyListInspector
{
    internal static List<uint> GetItemIds(ushort questId)
    {
        var items = new HashSet<uint>();
        var state = UIState.Instance();
        if (state == null) return new List<uint>();

        ref var tracked = ref state->QuestTodoList.Todo.TrackedQuests;
        // Be conservative about malformed or temporarily uninitialized UI state.
        if (tracked.Count < 0 || tracked.Count > 64) return new List<uint>();
        for (var i = 0; i < tracked.Count; i++)
        {
            var quest = tracked[i];
            if (quest.QuestId != questId) continue;

            // Generated FFXIVClientStructs fixed-array accessor.
            foreach (var itemId in quest.ItemIds)
                if (itemId != 0) items.Add(itemId);
        }
        return items.Order().ToList();
    }
}
