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

    private static readonly Dictionary<uint, List<ActionInfo>> AllCache = new();
    private static List<(uint Id, string Name, uint Icon)>? usableItems;

    /// <summary>Every action a job can use (any recast), its role actions and Sprint, sorted by level.</summary>
    public static IReadOnlyList<ActionInfo> AllForJob(uint jobId)
    {
        if (AllCache.TryGetValue(jobId, out var cached))
        {
            return cached;
        }

        var list = new List<ActionInfo>();
        var parent = Svc.DataManager.GetExcelSheet<ClassJob>().GetRowOrDefault(jobId)?.ClassJobParent.RowId ?? 0;
        var actions = Svc.DataManager.GetExcelSheet<Action>();
        foreach (var row in actions)
        {
            var owner = row.ClassJob.RowId;
            if (!row.IsPlayerAction || row.IsPvP || row.Icon == 0 || row.Name.IsEmpty || owner == 0 || (owner != jobId && owner != parent))
            {
                continue;
            }

            list.Add(new ActionInfo(row.RowId, row.Name.ExtractText(), row.Icon, row.ClassJobLevel, row.Recast100ms / 10f, false));
        }

        var extra = new List<uint> { 3 }; // Sprint
        if (RoleActions.TryGetValue(Jobs.Get(jobId).Role, out var roleIds))
        {
            extra.AddRange(roleIds);
        }

        foreach (var id in extra)
        {
            if (actions.GetRowOrDefault(id) is { } row && row.Icon != 0)
            {
                list.Add(new ActionInfo(id, row.Name.ExtractText(), row.Icon, row.ClassJobLevel, row.Recast100ms / 10f, id != 3));
            }
        }

        list = list.OrderBy(a => a.Level).ThenBy(a => a.Name).ToList();
        AllCache[jobId] = list;
        return list;
    }

    /// <summary>Items with a use effect (potions, food, ...) whose name contains <paramref name="search"/>.</summary>
    public static IEnumerable<(uint Id, string Name, uint Icon)> SearchItems(string search, int max = 15)
    {
        if (search.Trim().Length < 3)
        {
            return Enumerable.Empty<(uint, string, uint)>();
        }

        usableItems ??= Svc.DataManager.GetExcelSheet<Item>()
            .Where(i => i.ItemAction.RowId != 0 && !i.Name.IsEmpty && i.Icon != 0)
            .Select(i => (i.RowId, i.Name.ExtractText(), (uint)i.Icon))
            .ToList();
        return usableItems.Where(i => i.Name.Contains(search.Trim(), System.StringComparison.OrdinalIgnoreCase)).Take(max);
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
