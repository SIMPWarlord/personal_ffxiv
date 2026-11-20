namespace QuestItemClicker;

public sealed record QuestItemRule(
    uint QuestId,
    int Sequence,
    uint TerritoryId,
    uint TargetDataId,
    uint ItemId,
    string Kind,
    float? X = null,
    float? Y = null,
    float? Z = null,
    string? CombatCondition = null,
    uint? CombatValue = null);
