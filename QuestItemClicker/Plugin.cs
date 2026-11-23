using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Inventory;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace QuestItemClicker;

/// <summary>Experimental native quest-item matching; disarmed by default. Watches target selection, not clicks.</summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Quest Item Clicker (prototype)";
    private const string Command = "/qic";
    private readonly IFramework framework;
    private readonly ITargetManager targets;
    private readonly IObjectTable objects;
    private readonly IClientState client;
    private readonly IChatGui chat;
    private readonly ICommandManager commands;
    private readonly ICondition conditions;
    private readonly IGameInventory inventory;
    private readonly string rulePath;
    private static readonly QuestItemRule[] ConfirmedExampleRules =
    [
        // Questionable's 4570_Patching Up.json: same quest item on three trunks.
        // A known quest-path exception, not a native dynamic deduction.
        new(4570, 1, 957, 2012900, 2003405, "UseItem"),
        new(4570, 1, 957, 2012901, 2003405, "UseItem"),
        new(4570, 1, 957, 2012902, 2003405, "UseItem"),
    ];
    private List<QuestItemRule> localRules = new();
    private List<QuestItemRule> rules = new();
    private nint lastTargetAddress;
    // A target selected while mounted stays pending until dismount completes.
    // We then try exactly once; we don't spam failed item actions each frame.
    private nint pendingTargetAddress;
    private bool queuedWhileMounted;
    private DateTime dismountReadySince = DateTime.MinValue;
    private DateTime lastAction = DateTime.MinValue;
    private bool armed;

    public Plugin(IDalamudPluginInterface pluginInterface, IFramework framework,
        ITargetManager targets, IObjectTable objects, IClientState client,
        IChatGui chat, ICommandManager commands, ICondition conditions, IGameInventory inventory)
    {
        this.framework = framework;
        this.targets = targets;
        this.objects = objects;
        this.client = client;
        this.chat = chat;
        this.commands = commands;
        this.conditions = conditions;
        this.inventory = inventory;
        rulePath = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "routes.json");
        Load();
        commands.AddHandler(Command, new CommandInfo(OnCommand) {
            HelpMessage = "Quest item clicker: /qic help. Starts DISARMED (dry run)."
        });
        framework.Update += OnUpdate;
        Say($"Native inspection enabled; {rules.Count} known + local mappings. DISARMED (dry run). Try /qic inspect on a quest target.");
    }

    private void OnCommand(string cmd, string args)
    {
        var action = args.Trim();
        if (action.Equals("arm", StringComparison.OrdinalIgnoreCase))
        {
            armed = true;
            // Arming while a target is already selected should not require another click.
            pendingTargetAddress = (nint)(targets.Target?.Address ?? IntPtr.Zero);
            Say("ARMED. Selected targets can use quest items; mounted targets wait until dismount. /qic off disables it.");
        }
        else if (action.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                 action.Equals("disarm", StringComparison.OrdinalIgnoreCase))
        {
            armed = false;
            pendingTargetAddress = 0;
            queuedWhileMounted = false;
            dismountReadySince = DateTime.MinValue;
            Say("DISARMED (dry run): matches are reported but items are not used.");
        }
        else if (action.Equals("reload", StringComparison.OrdinalIgnoreCase))
        {
            armed = false;
            pendingTargetAddress = 0;
            Load();
            Say($"Reloaded {rules.Count} mappings. DISARMED.");
        }
        else if (action.StartsWith("import ", StringComparison.OrdinalIgnoreCase))
        {
            armed = false;
            pendingTargetAddress = 0;
            var root = action.Substring(7).Trim().Trim('"');
            try
            {
                var imported = RouteImporter.Import(root, out var files, out var skipped);
                Directory.CreateDirectory(Path.GetDirectoryName(rulePath)!);
                File.WriteAllText(rulePath, JsonSerializer.Serialize(imported,
                    new JsonSerializerOptions { WriteIndented = true }));
                localRules = imported;
                RebuildRules();
                Say($"Imported {localRules.Count} mappings from {files} quest files; {skipped} malformed/unsupported steps skipped. DISARMED.");
            }
            catch (Exception e) { Say($"Import failed: {e.Message}"); }
        }
        else if (action.Equals("inspect", StringComparison.OrdinalIgnoreCase))
            InspectTarget();
        else if (action.StartsWith("bind ", StringComparison.OrdinalIgnoreCase))
            BindTarget(action.Substring(5).Trim());
        else if (action.Equals("forget", StringComparison.OrdinalIgnoreCase))
            ForgetTarget();
        else if (action.Equals("use", StringComparison.OrdinalIgnoreCase))
            TryObjective(targets.Target, allowPositionOnly: true);
        else if (action.Equals("status", StringComparison.OrdinalIgnoreCase))
            Say($"{(armed ? "ARMED" : "DISARMED")}; native inspection ON; {rules.Count} mappings; " +
                $"target BaseId {targets.Target?.BaseId.ToString() ?? "none"}; territory {client.TerritoryType}; " +
                $"pending mounted selection: {queuedWhileMounted && pendingTargetAddress != 0}.");
        else
            Say("Commands: /qic inspect | /qic bind <itemId> | /qic forget | /qic arm | /qic off | /qic use | /qic status | /qic import \"path\" | /qic reload");
    }

    private bool IsMountBlocked() =>
        conditions[ConditionFlag.Mounted] ||
        conditions[ConditionFlag.RidingPillion] ||
        conditions[ConditionFlag.Mounting] ||
        conditions[ConditionFlag.MountOrOrnamentTransition] ||
        conditions[ConditionFlag.InFlight];

    private void OnUpdate(IFramework _)
    {
        var target = targets.Target;
        var address = (nint)(target?.Address ?? IntPtr.Zero);

        if (address != lastTargetAddress)
        {
            // Changing or clearing the target cancels any old pending attempt.
            lastTargetAddress = address;
            pendingTargetAddress = address;
            queuedWhileMounted = false;
            dismountReadySince = DateTime.MinValue;
        }

        if (address == 0 || !client.IsLoggedIn)
        {
            pendingTargetAddress = 0;
            queuedWhileMounted = false;
            dismountReadySince = DateTime.MinValue;
            return;
        }
        if (pendingTargetAddress != address) return;

        if (IsMountBlocked())
        {
            // Do not attempt an item while mounted: the game can reject it,
            // and previously we never retried without reselecting the target.
            if (!queuedWhileMounted)
            {
                queuedWhileMounted = true;
                dismountReadySince = DateTime.MinValue;
                Say("Target selected while mounted. Will evaluate it after dismount; no need to reselect.");
            }
            return;
        }

        if (queuedWhileMounted)
        {
            // Give the client time to finish the dismount animation/state change.
            if (dismountReadySince == DateTime.MinValue)
                dismountReadySince = DateTime.UtcNow;
            if ((DateTime.UtcNow - dismountReadySince).TotalMilliseconds < 650)
                return;
        }

        if (conditions[ConditionFlag.BetweenAreas] ||
            conditions[ConditionFlag.OccupiedInEvent] ||
            conditions[ConditionFlag.OccupiedInQuestEvent] ||
            conditions[ConditionFlag.OccupiedInCutSceneEvent])
            return;

        // Keep a pending target through the short item cooldown.
        if ((DateTime.UtcNow - lastAction).TotalSeconds < 2)
            return;

        // Exactly one evaluation per selection/arm action. If the target changes,
        // the old queued request cannot fire on a different object.
        pendingTargetAddress = 0;
        queuedWhileMounted = false;
        dismountReadySince = DateTime.MinValue;
        TryObjective(target, allowPositionOnly: false);
    }

    private unsafe void TryObjective(IGameObject? target, bool allowPositionOnly)
    {
        if (!client.IsLoggedIn || objects.LocalPlayer == null ||
            conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.OccupiedInEvent]) return;
        if (IsMountBlocked())
        {
            if (target is not null)
            {
                pendingTargetAddress = (nint)target.Address;
                queuedWhileMounted = true;
                dismountReadySince = DateTime.MinValue;
                Say("Cannot use an item while mounted. Keeping this target queued until dismount.");
            }
            return;
        }
        if ((DateTime.UtcNow - lastAction).TotalSeconds < 2) return;
        var me = objects.LocalPlayer!;
        if (target is not null && TryNativeObjective(target)) return;
        var manager = QuestManager.Instance();
        if (manager == null) return;

        var candidates = rules.Where(rule =>
            rule.TerritoryId == client.TerritoryType &&
            manager->IsQuestAccepted(rule.QuestId) &&
            QuestManager.GetQuestSequence(rule.QuestId) == rule.Sequence &&
            ((target != null && rule.TargetDataId != 0 && target.BaseId == rule.TargetDataId &&
              Vector3.Distance(me.Position, target.Position) < 12f)
             || (allowPositionOnly && rule.TargetDataId == 0 &&
                 rule.X is not null && rule.Y is not null && rule.Z is not null &&
                 Vector3.Distance(me.Position, new Vector3(rule.X.Value, rule.Y.Value, rule.Z.Value)) < 5f))
            && HasKeyItem(rule.ItemId) && CombatConditionMet(rule, target)).ToArray();

        if (candidates.Length == 0) return;
        if (candidates.Select(c => c.ItemId).Distinct().Count() != 1)
        {
            Say("Ambiguous quest item matches: skipped. Use the item manually.");
            return;
        }
        var candidate = candidates[0];
        lastAction = DateTime.UtcNow;
        if (!armed)
        {
            Say($"MAPPING DRY RUN: quest {candidate.QuestId}, sequence {candidate.Sequence}, target {candidate.TargetDataId}, item {candidate.ItemId}. Source: known or learned rule. /qic arm to test.");
            return;
        }
        var agent = AgentInventoryContext.Instance();
        if (agent == null) { Say("Unable to access item-use agent."); return; }
        agent->UseItem(candidate.ItemId, InventoryType.Invalid, 0, 0);
        Say($"Attempted quest item {candidate.ItemId} for quest {candidate.QuestId}. The game determines whether this succeeds.");
    }


    // Native data is a hint, not definitive proof that an item works on this objective.
    // Never brute-force items. Only a unique, accepted-quest handler + owned item can proceed.
    private unsafe bool TryNativeObjective(IGameObject target)
    {
        var hints = NativeQuestInspector.Inspect(target, inventory);
        // First try the event handler's own item ID. When it is zero, use the
        // item shortcut data already attached to this quest in the Duty List.
        // Neither source proves the item is valid on this specific objective.
        var direct = hints.Where(h => h.Accepted && h.Owned && h.EventItemId != 0)
            .Select(h => h.EventItemId).Distinct().ToArray();
        var fromDutyList = hints.Where(h => h.Accepted)
            .SelectMany(h => DutyListInspector.GetItemIds(h.QuestId))
            .Where(HasKeyItem).Distinct().ToArray();
        var candidates = direct.Length > 0 ? direct : fromDutyList;
        var source = direct.Length > 0 ? "EVENT HANDLER" : "DUTY LIST";
        if (candidates.Length == 0) return false;
        if (candidates.Length != 1)
        {
            Say($"{source} scan found {candidates.Length} different owned quest items: skipped.");
            return true;
        }

        var me = objects.LocalPlayer;
        if (me == null || Vector3.Distance(me.Position, target.Position) > 8f) return true;
        if ((DateTime.UtcNow - lastAction).TotalSeconds < 2) return true;
        var itemId = candidates[0];
        lastAction = DateTime.UtcNow;
        if (!armed)
        {
            Say($"{source} DRY RUN: target BaseId {target.BaseId}; candidate quest item {itemId} is held. " +
                "This is only a candidate, not a validated usable item. /qic arm to test.");
            return true;
        }
        var agent = AgentInventoryContext.Instance();
        if (agent == null)
        {
            Say($"{source} match found, but item-use agent is unavailable.");
            return true;
        }
        agent->UseItem(itemId, InventoryType.Invalid, 0, 0);
        Say($"{source} mode attempted item {itemId} on current target (game acceptance unverified).");
        return true;
    }

    private void InspectTarget()
    {
        var target = targets.Target;
        if (!client.IsLoggedIn || target is null)
        {
            Say("Select an NPC, enemy or object first, then /qic inspect.");
            return;
        }
        var hints = NativeQuestInspector.Inspect(target, inventory);
        Say($"Target: {target.Name}, BaseId {target.BaseId}; {hints.Count} quest event handlers.");
        if (hints.Count == 0)
        {
            Say("No quest handlers reported. This objective may not expose usable item data.");
            return;
        }
        foreach (var hint in hints.Take(8))
        {
            Say($"Quest {hint.QuestId}: {(hint.Accepted ? "accepted" : "not accepted")}, " +
                $"EventItemId {hint.EventItemId}, {(hint.Owned ? "owned" : "not in Key Items")}.");
        }
        if (hints.Count > 8) Say($"Showing first 8 of {hints.Count} handlers.");
        foreach (var questId in hints.Where(h => h.Accepted).Select(h => h.QuestId).Distinct())
        {
            var linked = NativeQuestInspector.GetQuestLinkedKeyItems(questId);
            if (linked.Count > 0)
                Say($"Quest {questId} key-item event association: {string.Join(", ", linked)}. NOT proven usable on target.");
            var dutyIds = DutyListInspector.GetItemIds(questId);
            Say(dutyIds.Count == 0
                ? $"Quest {questId} Duty List item IDs: none found (quest might not be tracked)."
                : $"Quest {questId} Duty List item IDs: {string.Join(", ", dutyIds.Select(id => id + (HasKeyItem(id) ? " [held]" : " [not held]")))}.");
        }
        var matches = rules.Where(r => r.TargetDataId == target.BaseId &&
                 r.TerritoryId == client.TerritoryType &&
                 hints.Any(h => h.Accepted && h.QuestId == r.QuestId) &&
                 HasKeyItem(r.ItemId)).Select(r => r.ItemId).Distinct().ToArray();
        if (matches.Length > 0)
            Say($"Known/learned target mapping: {string.Join(", ", matches)}. You can now deselect and reselect for a dry run.");
        Say("A zero EventItemId is expected for some quests. No item guessing is performed.");
    }

    private static unsafe bool CombatConditionMet(QuestItemRule rule, IGameObject? target)
    {
        if (rule.Kind != "CombatItemUse") return true;
        if (target is not IBattleChara battle || target.Address == IntPtr.Zero) return false;
        var character = (Character*)target.Address;
        var condition = rule.CombatCondition;
        if (condition == "HealthPercent")
            return battle.MaxHp > 0 && (100.0 * battle.CurrentHp / battle.MaxHp) < (rule.CombatValue ?? 0);
        if (condition == "Incapacitated") return (character->ActorControlFlags & 0x40) != 0;
        if (condition == "MissingStatus")
            return rule.CombatValue.HasValue && !character->HasStatus(rule.CombatValue.Value);
        return false;
    }

    private bool HasKeyItem(uint itemId)
    {
        foreach (var item in inventory.GetInventoryItems(GameInventoryType.KeyItems))
            if (!item.IsEmpty && item.Quantity > 0 && item.ItemId == itemId)
                return true;
        return false;
    }

    private unsafe void BindTarget(string itemText)
    {
        if (!uint.TryParse(itemText, out var itemId) || itemId == 0)
        {
            Say("Usage: /qic bind <numeric key item ID>. This saves a single target-specific mapping.");
            return;
        }
        var target = targets.Target;
        if (target == null || !client.IsLoggedIn) { Say("Select a quest target first."); return; }
        if (!HasKeyItem(itemId)) { Say($"Item {itemId} is not present in Key Items. Nothing saved."); return; }
        if (objects.LocalPlayer == null || Vector3.Distance(objects.LocalPlayer.Position, target.Position) > 8f)
        {
            Say("Stand within 8 units of the target before binding an item."); return;
        }
        var accepted = NativeQuestInspector.Inspect(target, inventory)
            .Where(h => h.Accepted).Select(h => (uint)h.QuestId).Distinct().ToArray();
        if (accepted.Length != 1) { Say("Could not uniquely identify an accepted quest for this target; nothing saved."); return; }
        var qid = accepted[0];
        var sequence = (int)QuestManager.GetQuestSequence(qid);
        var rule = new QuestItemRule(qid, sequence, client.TerritoryType, target.BaseId, itemId, "UseItem");
        localRules.RemoveAll(r => r.QuestId == qid && r.TerritoryId == client.TerritoryType && r.TargetDataId == target.BaseId);
        localRules.Add(rule);
        SaveLocalRules();
        RebuildRules();
        armed = false;
        Say($"Saved QUEST {qid}, sequence {sequence}, target {target.BaseId} -> item {itemId}. DISARMED. Reselect for dry-run.");
    }

    private void ForgetTarget()
    {
        var target = targets.Target;
        if (target == null) { Say("Select the target whose learned binding you want to forget."); return; }
        var hints = NativeQuestInspector.Inspect(target, inventory);
        var qids = hints.Where(h => h.Accepted).Select(h => (uint)h.QuestId).ToHashSet();
        var removed = localRules.RemoveAll(r => r.TargetDataId == target.BaseId &&
            r.TerritoryId == client.TerritoryType && qids.Contains(r.QuestId));
        if (removed > 0) { SaveLocalRules(); RebuildRules(); }
        armed = false;
        Say($"Removed {removed} local mappings; known example rules are not altered. DISARMED.");
    }

    private void SaveLocalRules()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(rulePath)!);
        File.WriteAllText(rulePath, JsonSerializer.Serialize(localRules,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private void RebuildRules() =>
        rules = localRules.Concat(ConfirmedExampleRules).Distinct().ToList();

    private void Load()
    {
        try
        {
            localRules = File.Exists(rulePath) ? JsonSerializer.Deserialize<List<QuestItemRule>>(File.ReadAllText(rulePath)) ?? new() : new();
        }
        catch (Exception e) { localRules = new(); Say($"Unable to read mapping file: {e.Message}"); }
        RebuildRules();
    }
    private void Say(string msg) => chat.Print($"[QuestItemClicker] {msg}");
    public void Dispose()
    {
        framework.Update -= OnUpdate;
        commands.RemoveHandler(Command);
    }
}
