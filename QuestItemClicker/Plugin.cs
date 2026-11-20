using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace QuestItemClicker;

/// <summary>Experimental; dry-run is the default. Watches changes in target selection.</summary>
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
    private readonly string rulePath;
    private List<QuestItemRule> rules = new();
    private nint lastTargetAddress;
    private DateTime lastAction = DateTime.MinValue;
    private bool armed;

    public Plugin(IDalamudPluginInterface pluginInterface, IFramework framework,
        ITargetManager targets, IObjectTable objects, IClientState client,
        IChatGui chat, ICommandManager commands, ICondition conditions)
    {
        this.framework = framework;
        this.targets = targets;
        this.objects = objects;
        this.client = client;
        this.chat = chat;
        this.commands = commands;
        this.conditions = conditions;
        rulePath = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "routes.json");
        Load();
        commands.AddHandler(Command, new CommandInfo(OnCommand) {
            HelpMessage = "Quest item clicker: /qic help. Starts DISARMED (dry run)."
        });
        framework.Update += OnUpdate;
        Say($"Loaded {rules.Count} item mappings. DISARMED (dry run). /qic help");
    }

    private void OnCommand(string cmd, string args)
    {
        var action = args.Trim();
        if (action.Equals("arm", StringComparison.OrdinalIgnoreCase))
        {
            armed = true;
            Say("ARMED. Selecting a matching objective can use a quest item. /qic off disables it.");
        }
        else if (action.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                 action.Equals("disarm", StringComparison.OrdinalIgnoreCase))
        {
            armed = false;
            Say("DISARMED (dry run): matches are reported but items are not used.");
        }
        else if (action.Equals("reload", StringComparison.OrdinalIgnoreCase))
        {
            armed = false;
            Load();
            Say($"Reloaded {rules.Count} mappings. DISARMED.");
        }
        else if (action.StartsWith("import ", StringComparison.OrdinalIgnoreCase))
        {
            armed = false;
            var root = action.Substring(7).Trim().Trim('"');
            try
            {
                var imported = RouteImporter.Import(root, out var files, out var skipped);
                Directory.CreateDirectory(Path.GetDirectoryName(rulePath)!);
                File.WriteAllText(rulePath, JsonSerializer.Serialize(imported,
                    new JsonSerializerOptions { WriteIndented = true }));
                rules = imported;
                Say($"Imported {rules.Count} mappings from {files} quest files; {skipped} malformed/unsupported steps skipped. DISARMED.");
            }
            catch (Exception e) { Say($"Import failed: {e.Message}"); }
        }
        else if (action.Equals("use", StringComparison.OrdinalIgnoreCase))
            TryObjective(targets.Target, allowPositionOnly: true);
        else if (action.Equals("status", StringComparison.OrdinalIgnoreCase))
            Say($"{(armed ? "ARMED" : "DISARMED")}; {rules.Count} mappings; target BaseId {targets.Target?.BaseId.ToString() ?? "none"}; territory {client.TerritoryType}.");
        else
            Say("Commands: /qic import \"C:\\...\\QuestPaths\" | /qic arm | /qic off | /qic use | /qic reload | /qic status");
    }

    private void OnUpdate(IFramework _)
    {
        var address = (nint)(targets.Target?.Address ?? IntPtr.Zero);
        if (address == lastTargetAddress) return;
        lastTargetAddress = address;
        if (address == 0 || !client.IsLoggedIn) return;
        TryObjective(targets.Target, allowPositionOnly: false);
    }

    private unsafe void TryObjective(IGameObject? target, bool allowPositionOnly)
    {
        if (!client.IsLoggedIn || objects.LocalPlayer == null ||
            conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.OccupiedInEvent]) return;
        if ((DateTime.UtcNow - lastAction).TotalSeconds < 2) return;
        var me = objects.LocalPlayer!;
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
            && CombatConditionMet(rule, target)).ToArray();

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
            Say($"DRY RUN: quest {candidate.QuestId}, sequence {candidate.Sequence}, target {candidate.TargetDataId}, would use item {candidate.ItemId}. /qic arm to enable.");
            return;
        }
        var agent = AgentInventoryContext.Instance();
        if (agent == null) { Say("Unable to access item-use agent."); return; }
        agent->UseItem(candidate.ItemId, InventoryType.Invalid, 0, 0);
        Say($"Attempted quest item {candidate.ItemId} for quest {candidate.QuestId}. The game determines whether this succeeds.");
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

    private void Load()
    {
        try
        {
            rules = File.Exists(rulePath) ? JsonSerializer.Deserialize<List<QuestItemRule>>(File.ReadAllText(rulePath)) ?? new() : new();
        }
        catch (Exception e) { rules = new(); Say($"Unable to read mapping file: {e.Message}"); }
    }
    private void Say(string msg) => chat.Print($"[QuestItemClicker] {msg}");
    public void Dispose()
    {
        framework.Update -= OnUpdate;
        commands.RemoveHandler(Command);
    }
}
