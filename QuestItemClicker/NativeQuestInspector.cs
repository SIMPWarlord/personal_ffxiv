using System;
using System.Collections.Generic;
using Dalamud.Game.Inventory;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using NativeEventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;

namespace QuestItemClicker;

/// <summary>
/// Read-only native inspection, no external quest-path database.
/// EventHandler.GetEventItemId() is an unverified clue, NOT a universal
/// item-target compatibility oracle. Unknown and ambiguous cases are skipped.
/// </summary>
internal static unsafe class NativeQuestInspector
{
    internal readonly record struct Hint(ushort QuestId, uint EventItemId, bool Accepted, bool Owned);

    internal static List<Hint> Inspect(IGameObject target, IGameInventory inventory)
    {
        var results = new List<Hint>();
        if (target.Address == IntPtr.Zero) return results;

        var manager = QuestManager.Instance();
        if (manager == null) return results;

        var native = (NativeGameObject*)target.Address;
        // The client method documents an output capacity of 32 pointers.
        NativeEventHandler** handlers = stackalloc NativeEventHandler*[32];
        var count = native->GetEventHandlersImpl(handlers);
        if (count < 0 || count > 32) return results;

        var keyItems = inventory.GetInventoryItems(GameInventoryType.KeyItems);
        for (var i = 0; i < count; i++)
        {
            var handler = handlers[i];
            if (handler == null) continue;
            var eventId = handler->GetEventId();
            if (eventId.ContentId != EventHandlerContent.Quest) continue;

            var questId = eventId.EntryId;
            var itemId = handler->GetEventItemId();
            var owned = false;
            if (itemId != 0)
            {
                foreach (var keyItem in keyItems)
                {
                    if (!keyItem.IsEmpty && keyItem.Quantity > 0 && keyItem.ItemId == itemId)
                    {
                        owned = true;
                        break;
                    }
                }
            }
            results.Add(new Hint(questId, itemId, manager->IsQuestAccepted(questId), owned));
        }
        return results;
    }
}
