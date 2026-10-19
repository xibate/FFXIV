using System.Collections.Generic;
using System.Linq;
using LucentUI.Config;
using LucentUI.Style;
using Lumina.Excel.Sheets;

namespace LucentUI.Data;

public sealed record ActionInfo(uint Id, string Name, uint Icon, int Level, float Recast, bool IsRoleAction);

/// <summary>
/// The actions with real cooldowns each job can use, read from the game's Action sheet, and the default
/// Cooldowns/Utility split: job abilities on a 30s+ recast go on Cooldowns, role actions on Utility.
/// </summary>
public static class JobActions
{
    private const float MinRecast = 15f;
    private const float DefaultMinRecast = 30f;

    // Role actions by role (they are shared, so the sheet does not tie them to one job).
    private static readonly Dictionary<JobRole, uint[]> RoleActions = new()
    {
        [JobRole.Tank] = new uint[] { 7531, 7533, 7535, 7537, 7538, 7540, 7548 },     // Rampart, Provoke, Reprisal, Shirk, Interject, Low Blow, Arm's Length
        [JobRole.Healer] = new uint[] { 7559, 7561, 7562, 7571 },                    // Surecast, Swiftcast, Lucid Dreaming, Rescue
        [JobRole.Melee] = new uint[] { 7541, 7542, 7546, 7548, 7549, 7863 },         // Second Wind, Bloodbath, True North, Arm's Length, Feint, Leg Sweep
        [JobRole.Ranged] = new uint[] { 7541, 7548, 7551, 7553, 7554, 7557 },        // Second Wind, Arm's Length, Head Graze, Foot Graze, Leg Graze, Peloton
        [JobRole.Caster] = new uint[] { 7559, 7560, 7561, 7562 },                    // Surecast, Addle, Swiftcast, Lucid Dreaming
    };

    private static readonly Dictionary<uint, List<ActionInfo>> Cache = new();

    /// <summary>Every cooldown action available to <paramref name="jobId"/> (any level), sorted by level.</summary>
    public static IReadOnlyList<ActionInfo> ForJob(uint jobId)
    {
        if (Cache.TryGetValue(jobId, out var cached))
        {
            return cached;
        }

        var list = new List<ActionInfo>();
        var classJobs = Svc.DataManager.GetExcelSheet<ClassJob>();
        var parent = classJobs.GetRowOrDefault(jobId)?.ClassJobParent.RowId ?? 0;
        var actions = Svc.DataManager.GetExcelSheet<Action>();

        foreach (var row in actions)
        {
            if (!row.IsPlayerAction || row.IsPvP || row.IsRoleAction || row.Icon == 0 || row.Name.IsEmpty)
            {
                continue;
            }

            var owner = row.ClassJob.RowId;
            if (owner == 0 || (owner != jobId && owner != parent))
            {
                continue;
            }

            var recast = row.Recast100ms / 10f;
            if (recast >= MinRecast)
            {
                list.Add(new ActionInfo(row.RowId, row.Name.ExtractText(), row.Icon, row.ClassJobLevel, recast, false));
            }
        }

        if (RoleActions.TryGetValue(Jobs.Get(jobId).Role, out var roleIds))
        {
            foreach (var id in roleIds)
            {
                if (actions.GetRowOrDefault(id) is { } row && row.Icon != 0)
                {
                    list.Add(new ActionInfo(id, row.Name.ExtractText(), row.Icon, row.ClassJobLevel, row.Recast100ms / 10f, true));
                }
            }
        }

        list = list.OrderBy(a => a.IsRoleAction).ThenBy(a => a.Level).ThenBy(a => a.Id).ToList();
        Cache[jobId] = list;
        return list;
    }

    /// <summary>The actions shown for a job on the Cooldowns and Utility bars.</summary>
    public static (IReadOnlyList<uint> Cooldowns, IReadOnlyList<uint> Utility) Selection(CooldownManagerSettings settings, uint jobId)
    {
        if (settings.Jobs.TryGetValue(jobId, out var custom))
        {
            return (custom.Cooldowns, custom.Utility);
        }

        var all = ForJob(jobId);
        var cooldowns = all.Where(a => !a.IsRoleAction && a.Recast >= DefaultMinRecast).Select(a => a.Id).ToList();
        var utility = all.Where(a => a.IsRoleAction && a.Recast >= DefaultMinRecast).Select(a => a.Id).ToList();
        return (cooldowns, utility);
    }

    /// <summary>Copies the automatic lists into a custom entry the user can edit.</summary>
    public static JobCooldownSelection Customise(CooldownManagerSettings settings, uint jobId)
    {
        if (!settings.Jobs.TryGetValue(jobId, out var custom))
        {
            var (cooldowns, utility) = Selection(settings, jobId);
            custom = new JobCooldownSelection { Cooldowns = cooldowns.ToList(), Utility = utility.ToList() };
            settings.Jobs[jobId] = custom;
        }

        return custom;
    }
}
