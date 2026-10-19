using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using LucentUI.Config;
using Lumina.Excel.Sheets;

namespace LucentUI.Data;

public struct AuraData
{
    public bool CanDispel;
    public uint StatusId;
    public uint SourceId;
    public string Name;
    public string Description;
    public uint Icon;
    public float Remaining;
    public bool IsDebuff;
    public bool IsMine;
    public bool IsPermanent;
}

public static class Auras
{
    private readonly record struct StatusInfo(string Name, string Description, uint Icon, byte MaxStacks, bool IsDebuff, bool IsPermanent, bool CanDispel);

    private static readonly Dictionary<uint, StatusInfo?> Cache = new();
    private static List<AuraData>? previewAuras;

    /// <summary>Fills <paramref name="into"/> with the unit's statuses that pass the filters.</summary>
    public static void Collect(IGameObject? obj, AuraSettings settings, List<AuraData> into)
    {
        into.Clear();
        if (obj is not IBattleChara chara)
        {
            return;
        }

        var playerId = Svc.ObjectTable.LocalPlayer?.EntityId ?? 0;
        foreach (var status in chara.StatusList)
        {
            if (status == null || status.StatusId == 0 || Info(status.StatusId) is not { } info)
            {
                continue;
            }

            if (settings.Filter != ListFilter.None && settings.FilterIds.Contains(status.StatusId) == (settings.Filter == ListFilter.HideListed))
            {
                continue;
            }

            var permanent = info.IsPermanent || status.RemainingTime <= 0f;
            var mine = status.SourceId == playerId;
            if ((info.IsDebuff ? !settings.ShowDebuffs : !settings.ShowBuffs) ||
                (settings.OnlyMine && !mine) ||
                (permanent && !settings.ShowPermanent))
            {
                continue;
            }

            var icon = info.Icon;
            if (info.MaxStacks > 1 && status.Param > 1)
            {
                // Stacked statuses have one icon per stack count, stored consecutively.
                icon += (uint)(status.Param - 1);
            }

            into.Add(new AuraData
            {
                StatusId = status.StatusId,
                SourceId = status.SourceId,
                Name = info.Name,
                Description = info.Description,
                Icon = icon,
                Remaining = status.RemainingTime,
                IsDebuff = info.IsDebuff,
                IsMine = mine,
                IsPermanent = permanent,
                CanDispel = info.CanDispel,
            });

            if (!settings.SortByTime && into.Count >= settings.MaxIcons)
            {
                break;
            }
        }

        if (settings.SortByTime)
        {
            // Shortest first, permanent statuses last.
            into.Sort((a, b) => (a.IsPermanent ? float.MaxValue : a.Remaining).CompareTo(b.IsPermanent ? float.MaxValue : b.Remaining));
        }

        if (into.Count > settings.MaxIcons)
        {
            into.RemoveRange(settings.MaxIcons, into.Count - settings.MaxIcons);
        }
    }

    /// <summary>A handful of real status icons for Unlock Mode previews.</summary>
    public static IReadOnlyList<AuraData> Preview()
    {
        if (previewAuras != null)
        {
            return previewAuras;
        }

        previewAuras = Svc.DataManager.GetExcelSheet<Status>()
            .Where(s => s.Icon != 0 && !s.Name.IsEmpty)
            .Skip(40)
            .Take(24)
            .Select((s, i) => new AuraData
            {
                StatusId = s.RowId,
                Name = s.Name.ExtractText(),
                Description = "Preview",
                Icon = s.Icon,
                Remaining = 5f + i * 7f,
                IsDebuff = s.StatusCategory == 2,
            })
            .ToList();
        return previewAuras;
    }

    public static uint IconOf(uint statusId) => Info(statusId)?.Icon ?? 0;

    public static string NameOf(uint statusId) => Info(statusId)?.Name is { Length: > 0 } name ? name : $"Status {statusId}";

    /// <summary>Statuses currently on a unit, for "add from what is active now" pickers.</summary>
    public static List<(uint Id, string Name)> ActiveOn(IGameObject? obj, bool onlyMine)
    {
        var list = new List<(uint, string)>();
        if (obj is not IBattleChara chara)
        {
            return list;
        }

        var playerId = Svc.ObjectTable.LocalPlayer?.EntityId ?? 0;
        foreach (var status in chara.StatusList)
        {
            if (status == null || status.StatusId == 0 || (onlyMine && status.SourceId != playerId) || list.Exists(e => e.Item1 == status.StatusId))
            {
                continue;
            }

            list.Add((status.StatusId, NameOf(status.StatusId)));
        }

        return list;
    }

    /// <summary>Statuses whose name contains <paramref name="text"/> (or whose id is <paramref name="text"/>).</summary>
    public static List<(uint Id, string Name)> Search(string text, int max = 8)
    {
        var list = new List<(uint, string)>();
        text = text.Trim();
        if (text.Length == 0)
        {
            return list;
        }

        if (uint.TryParse(text, out var id) && Info(id) != null)
        {
            list.Add((id, NameOf(id)));
            return list;
        }

        if (text.Length < 3)
        {
            return list;
        }

        foreach (var row in Svc.DataManager.GetExcelSheet<Status>())
        {
            if (row.Icon == 0 || row.Name.IsEmpty)
            {
                continue;
            }

            var name = row.Name.ExtractText();
            if (name.Contains(text, System.StringComparison.OrdinalIgnoreCase))
            {
                list.Add((row.RowId, name));
                if (list.Count >= max)
                {
                    break;
                }
            }
        }

        return list;
    }

    public static bool IsDispellable(uint statusId) => Info(statusId)?.CanDispel ?? false;

    private static StatusInfo? Info(uint statusId)
    {
        if (Cache.TryGetValue(statusId, out var cached))
        {
            return cached;
        }

        StatusInfo? info = null;
        if (Svc.DataManager.GetExcelSheet<Status>().GetRowOrDefault(statusId) is { } row && row.Icon != 0)
        {
            info = new StatusInfo(
                row.Name.ExtractText(),
                row.Description.ExtractText(),
                row.Icon,
                row.MaxStacks,
                row.StatusCategory == 2,
                row.IsPermanent,
                row.CanDispel);
        }

        Cache[statusId] = info;
        return info;
    }
}
