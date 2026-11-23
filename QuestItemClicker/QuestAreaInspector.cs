using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using GameMap = FFXIVClientStructs.FFXIV.Client.Game.UI.Map;

namespace QuestItemClicker;

// These are map-quest area markers, not a guaranteed item-use target list.
internal static unsafe class QuestAreaInspector
{
    internal readonly record struct Area(ushort QuestId, Vector3 Center, float Radius);

    internal static List<Area> Nearby(ushort territory, Vector3 position, float maxDistance)
    {
        var result = new List<Area>();
        var map = GameMap.Instance();
        var quests = QuestManager.Instance();
        if (map == null || quests == null) return result;
        foreach (var quest in map->QuestMarkers)
        {
            if (quest.ObjectiveId == 0 || quest.ObjectiveId > ushort.MaxValue) continue;
            var qid = (ushort)quest.ObjectiveId;
            if (!quests->IsQuestAccepted(qid)) continue;
            foreach (var marker in quest.MarkerData)
            {
                if (marker.TerritoryTypeId != territory ||
                    marker.Radius <= 0f || marker.Radius > 200f ||
                    !float.IsFinite(marker.Radius) ||
                    (marker.ObjectiveId != 0 && marker.ObjectiveId != qid) ||
                    !float.IsFinite(marker.Position.X) ||
                    !float.IsFinite(marker.Position.Y) ||
                    !float.IsFinite(marker.Position.Z)) continue;

                if (HorizontalDistance(position, marker.Position) > maxDistance ||
                    MathF.Abs(position.Y - marker.Position.Y) > 20f) continue;
                if (!result.Any(x => x.QuestId == qid &&
                    HorizontalDistance(x.Center, marker.Position) < 0.5f))
                    result.Add(new Area(qid, marker.Position, marker.Radius));
            }
        }
        return result.OrderBy(x => HorizontalDistance(x.Center, position)).ToList();
    }

    internal static List<Area> FindAt(ushort territory, Vector3 click) =>
        Nearby(territory, click, 12f).Where(x =>
            HorizontalDistance(click, x.Center) <= MathF.Min(x.Radius, 12f) &&
            MathF.Abs(click.Y - x.Center.Y) <= 12f).ToList();

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
