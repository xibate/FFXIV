using Dalamud.Game.ClientState.Objects.Types;

namespace LucentUI.Data;

/// <summary>Quick status lookups for gauges and trackers.</summary>
public static class Statuses
{
    /// <summary>Remaining time and stack count of the first matching status, or (0, 0).</summary>
    public static (float Remaining, int Stacks) Find(IGameObject? obj, uint sourceId, params uint[] ids)
    {
        if (obj is not IBattleChara chara)
        {
            return (0f, 0);
        }

        foreach (var status in chara.StatusList)
        {
            if (status == null || status.StatusId == 0)
            {
                continue;
            }

            if (sourceId != 0 && status.SourceId != sourceId)
            {
                continue;
            }

            foreach (var id in ids)
            {
                if (status.StatusId == id)
                {
                    // Permanent statuses report 0 or less; treat them as "present".
                    var remaining = status.RemainingTime > 0 ? status.RemainingTime : float.PositiveInfinity;
                    return (remaining, status.Param);
                }
            }
        }

        return (0f, 0);
    }

    public static bool Has(IGameObject? obj, params uint[] ids) => Find(obj, 0, ids).Remaining > 0f;
}
