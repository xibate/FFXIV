using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.Game;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;

namespace LucentUI.Hud.Elements;

public enum CooldownBarKind
{
    Cooldowns,
    Utility,
    Buffs,
    Custom,
}

/// <summary>
/// A row of your own action icons with a cooldown swipe and timer (Cooldowns and Utility), or your own active
/// buffs (Buffs). Mirrors EllesmereUI's Cooldown Manager bars.
/// </summary>
public sealed unsafe class CooldownBarElement : HudElement
{
    private const int SwipeSegments = 36;

    private readonly CooldownBarKind kind;
    private readonly List<Icon> icons = new();
    private readonly List<AuraData> buffs = new();
    private readonly AuraSettings buffFilter = new() { ShowDebuffs = false, OnlyMine = true, ShowPermanent = false, SortByTime = true, MaxIcons = 30 };
    private readonly Dictionary<uint, uint> iconCache = new();

    private readonly int slot;

    public CooldownBarElement(CooldownBarKind kind)
    {
        this.kind = kind;
    }

    /// <summary>The element for custom bar number <paramref name="customSlot"/> (0-based).</summary>
    public CooldownBarElement(int customSlot)
    {
        kind = CooldownBarKind.Custom;
        slot = customSlot;
    }

    /// <summary>The custom bar this element draws, if it exists in the profile.</summary>
    private CustomCooldownBar? Custom(Profile p) =>
        kind == CooldownBarKind.Custom && slot < p.CooldownManager.CustomBars.Count ? p.CooldownManager.CustomBars[slot] : null;

    /// <summary>Settings for this element; a placeholder while its custom bar does not exist.</summary>
    private CooldownBarSettings Bar(Profile p) => kind == CooldownBarKind.Custom ? Custom(p)?.Bar ?? Placeholder : Settings(p, kind);

    private static readonly CooldownBarSettings Placeholder = new() { Enabled = false };

    public static string CustomId(int customSlot) => "cdm.custom" + (customSlot + 1);

    private struct Icon
    {
        public uint IconId;
        public float Remaining;
        public float Total;
        public int Charges;
        public int MaxCharges;
        public bool Active;

        /// <summary>Items: how many you have (-1 for actions).</summary>
        public int Count;
    }

    public override string Id => kind switch
    {
        CooldownBarKind.Cooldowns => "cdm.cooldowns",
        CooldownBarKind.Utility => "cdm.utility",
        CooldownBarKind.Buffs => "cdm.buffs",
        _ => CustomId(slot),
    };

    public override string Label => kind switch
    {
        CooldownBarKind.Cooldowns => "Cooldowns",
        CooldownBarKind.Utility => "Utility Cooldowns",
        CooldownBarKind.Buffs => "Tracked Buffs",
        _ => Plugin.Profiles?.Current is { } p && Custom(p) is { } bar ? bar.Name : $"Custom Bar {slot + 1}",
    };

    public override Vector2 DefaultPosition => kind switch
    {
        CooldownBarKind.Cooldowns => new(-215, 360),
        CooldownBarKind.Utility => new(-148, 408),
        CooldownBarKind.Buffs => new(-133, -60),
        _ => new(-400 + slot * 30, 250 + slot * 30),
    };

    public static CooldownBarSettings Settings(Profile p, CooldownBarKind kind) => kind switch
    {
        CooldownBarKind.Cooldowns => p.CooldownManager.Cooldowns,
        CooldownBarKind.Utility => p.CooldownManager.Utility,
        _ => p.CooldownManager.Buffs,
    };

    public override bool IsEnabled(Profile profile) => profile.Modules.CooldownManager && Bar(profile).Enabled;

    public override VisibilitySettings? GetVisibility(Profile profile) => Bar(profile).Visibility;

    public override Vector2 GetSize(Profile profile) => BoxSize(Bar(profile));

    public static Vector2 BoxSize(CooldownBarSettings s)
    {
        var (columns, rows) = Grid(s, Math.Max(1, s.MaxIcons));
        var size = MathF.Round(s.IconSize);
        return new Vector2(columns * size + (columns - 1) * s.Spacing, rows * size + (rows - 1) * s.Spacing);
    }

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = Bar(ctx.Profile);
        var player = Svc.ObjectTable.LocalPlayer;
        if (player == null)
        {
            return;
        }

        if (kind == CooldownBarKind.Custom)
        {
            CollectCustom(Custom(ctx.Profile)!, player.ClassJob.RowId, player.Level, ctx.Preview);
        }
        else
        {
            Collect(ctx.Profile.CooldownManager, player.ClassJob.RowId, player.Level, ctx.Preview);
        }

        if (icons.Count == 0)
        {
            return;
        }

        var count = Math.Min(icons.Count, Math.Max(1, s.MaxIcons));
        var (columns, _) = Grid(s, count);
        var size = MathF.Round(s.IconSize);
        var box = BoxSize(s);

        using var font = s.TimerFontSize > 0 ? ctx.Fonts.Push(s.TimerFontSize) : ctx.Fonts.PushSmall();
        for (var i = 0; i < count; i++)
        {
            var col = i % columns;
            var row = i / columns;

            // Each row is aligned on its own, so a short last row sits centred (or left/right) like the rest.
            var inRow = Math.Min(columns, count - row * columns);
            var rowWidth = inRow * size + (inRow - 1) * s.Spacing;
            var startX = s.Align switch
            {
                BarAlign.Left => 0f,
                BarAlign.Right => box.X - rowWidth,
                _ => MathF.Floor((box.X - rowWidth) / 2f),
            };

            var iconPos = pos + new Vector2(startX + col * (size + s.Spacing), row * (size + s.Spacing));
            DrawIcon(ctx, s, icons[i], Gfx.Floor(iconPos), size);
        }
    }

    private void DrawIcon(DrawContext ctx, CooldownBarSettings s, in Icon icon, Vector2 pos, float size)
    {
        var dl = ctx.DrawList;
        var (min, max) = s.ShowBackground ? Gfx.Panel(ctx, pos, new Vector2(size)) : (pos, pos + new Vector2(size));
        var texture = Svc.TextureProvider.GetFromGameIcon(new GameIconLookup(icon.IconId)).GetWrapOrEmpty();

        // Buff icons are 3:4 portraits; action icons are square with a frame we crop slightly.
        if (kind == CooldownBarKind.Buffs)
        {
            // Portrait buffs sit centred in the square slot; square and round ones fill it.
            var shape = IconShapes.Resolve(s.BuffShape, ctx.Style);
            var w = (max.Y - min.Y) * IconShapes.Aspect(shape);
            var x = MathF.Floor((min.X + max.X - w) / 2f);
            IconShapes.Draw(ctx, texture, new Vector2(x, min.Y), new Vector2(x + w, max.Y), shape, Vector4.One);
        }
        else
        {
            var tint = (s.DimOnCooldown && !icon.Active && icon.Charges == 0) || icon.Count == 0 ? new Vector4(0.6f, 0.6f, 0.6f, 1f) : Vector4.One;
            var zoom = Math.Clamp(s.IconZoom, 0f, 0.3f);
            dl.AddImage(texture.Handle, min, max, new Vector2(zoom), new Vector2(1f - zoom), ctx.Col(tint));
        }

        if (icon.Total > 0f && icon.Remaining > 0f)
        {
            if (kind != CooldownBarKind.Buffs)
            {
                DrawSwipe(ctx, min, max, icon.Remaining / icon.Total);
            }

            if (s.ShowCooldownText)
            {
                var text = icon.Remaining >= 60f
                    ? TextTags.Duration(icon.Remaining)
                    : icon.Remaining < 3f && kind != CooldownBarKind.Buffs
                        ? icon.Remaining.ToString("0.0", CultureInfo.InvariantCulture)
                        : ((int)Math.Ceiling(icon.Remaining)).ToString(CultureInfo.InvariantCulture);
                Gfx.Text(ctx, text, (min + max) / 2f, TextAlign.Center, Vector4.One);
            }
        }

        if (icon.Count >= 0)
        {
            Gfx.Text(ctx, icon.Count.ToString(CultureInfo.InvariantCulture), new Vector2(max.X - 3, max.Y - 7), TextAlign.Right, icon.Count > 0 ? Vector4.One : Colors.Rgb(0xFF6B5E));
        }
        else if (s.ShowCharges && icon.MaxCharges > 1)
        {
            Gfx.Text(ctx, icon.Charges.ToString(CultureInfo.InvariantCulture), new Vector2(max.X - 3, max.Y - 7), TextAlign.Right, Vector4.One);
        }
    }

    /// <summary>A clockwise dark wedge over the part of the cooldown still to go.</summary>
    private static void DrawSwipe(DrawContext ctx, Vector2 min, Vector2 max, float remainingFraction)
    {
        remainingFraction = Math.Clamp(remainingFraction, 0f, 1f);
        var centre = (min + max) / 2f;
        var radius = (max - min).Length();
        var color = ctx.Col(new Vector4(0, 0, 0, 0.6f));
        var start = -MathF.PI / 2f + MathF.Tau * (1f - remainingFraction);
        var end = MathF.PI * 1.5f;
        var segments = Math.Max(1, (int)MathF.Ceiling(SwipeSegments * remainingFraction));

        ctx.DrawList.PushClipRect(min, max, true);
        for (var i = 0; i < segments; i++)
        {
            var a0 = start + (end - start) * i / segments;
            var a1 = start + (end - start) * (i + 1) / segments;
            var p0 = centre + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
            var p1 = centre + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
            ctx.DrawList.AddTriangleFilled(centre, p0, p1, color);
        }

        ctx.DrawList.PopClipRect();
    }

    private void Collect(CooldownManagerSettings settings, uint jobId, int level, bool preview)
    {
        icons.Clear();
        if (kind == CooldownBarKind.Buffs)
        {
            Auras.Collect(Svc.ObjectTable.LocalPlayer, buffFilter, buffs);
            settings.TrackedBuffs.TryGetValue(jobId, out var tracked);
            foreach (var buff in buffs)
            {
                if (settings.HiddenBuffs.Contains(buff.StatusId) ||
                    (settings.OnlyTrackedBuffs && (tracked == null || !tracked.Contains(buff.StatusId))))
                {
                    continue;
                }

                icons.Add(new Icon { IconId = buff.Icon, Remaining = buff.Remaining, Total = Math.Max(buff.Remaining, 1f), Active = true, Count = -1 });
            }

            if (icons.Count == 0 && preview)
            {
                foreach (var sample in Auras.Preview())
                {
                    if (!sample.IsDebuff && icons.Count < 4)
                    {
                        icons.Add(new Icon { IconId = sample.Icon, Remaining = sample.Remaining, Total = 30f, Active = true, Count = -1 });
                    }
                }
            }

            return;
        }

        var (cooldowns, utility) = JobActions.Selection(settings, jobId);
        var ids = kind == CooldownBarKind.Cooldowns ? cooldowns : utility;
        var manager = ActionManager.Instance();
        if (manager == null)
        {
            return;
        }

        var seen = new HashSet<uint>();
        var levels = JobActions.ForJob(jobId);
        foreach (var id in ids)
        {
            var info = FindInfo(levels, id);
            if (info != null && info.Level > level)
            {
                continue;
            }

            // Follow upgrades (e.g. Vengeance → Damnation) and skip duplicates that share a button.
            var adjusted = manager->GetAdjustedActionId(id);
            if (!seen.Add(adjusted))
            {
                continue;
            }

            var total = manager->GetRecastTime(ActionType.Action, adjusted);
            var elapsed = manager->GetRecastTimeElapsed(ActionType.Action, adjusted);
            int maxCharges = ActionManager.GetMaxCharges(adjusted, (uint)level);
            var icon = new Icon { IconId = IconOf(adjusted, info?.Icon ?? 0), MaxCharges = maxCharges, Count = -1 };

            if (total > 0f && elapsed > 0f && elapsed < total)
            {
                var perCharge = maxCharges > 1 ? total / maxCharges : total;
                icon.Charges = maxCharges > 1 ? (int)(elapsed / perCharge) : 0;
                icon.Remaining = maxCharges > 1 ? perCharge - elapsed % perCharge : total - elapsed;
                icon.Total = perCharge;
            }
            else
            {
                icon.Charges = Math.Max(1, maxCharges);
                icon.Active = true;
            }

            if (preview && icons.Count % 3 == 1)
            {
                icon.Remaining = 34f;
                icon.Total = 60f;
                icon.Active = false;
                icon.Charges = 0;
            }

            icons.Add(icon);
        }
    }

    private void CollectCustom(CustomCooldownBar bar, uint jobId, int level, bool preview)
    {
        icons.Clear();
        var manager = ActionManager.Instance();
        if (manager == null)
        {
            return;
        }

        foreach (var entry in bar.Entries)
        {
            if (entry.Job != 0 && entry.Job != jobId)
            {
                continue;
            }

            if (entry.Kind == CooldownEntryKind.Item)
            {
                var itemId = entry.HighQuality ? entry.Id + 1_000_000 : entry.Id;
                var count = InventoryManager.Instance() is var inventory && inventory != null ? inventory->GetInventoryItemCount(entry.Id, entry.HighQuality) : 0;
                if (count == 0 && bar.HideMissingItems && !preview)
                {
                    continue;
                }

                var item = new Icon { IconId = ItemIcon(entry.Id), Count = count, MaxCharges = 0 };
                Recast(manager, ActionType.Item, itemId, 1, ref item);
                icons.Add(item);
                continue;
            }

            var required = Svc.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(entry.Id)?.ClassJobLevel ?? 0;
            if (required > level)
            {
                continue;
            }

            var adjusted = manager->GetAdjustedActionId(entry.Id);
            int maxCharges = ActionManager.GetMaxCharges(adjusted, (uint)level);
            var icon = new Icon { IconId = IconOf(adjusted, 0), MaxCharges = maxCharges, Count = -1 };
            Recast(manager, ActionType.Action, adjusted, maxCharges, ref icon);
            icons.Add(icon);
        }

        if (icons.Count == 0 && preview)
        {
            icons.Add(new Icon { IconId = 62118, Count = -1, Active = true });
            icons.Add(new Icon { IconId = 62118, Count = -1, Remaining = 40f, Total = 90f });
        }
    }

    /// <summary>Fills in cooldown progress and charges from the game's recast timers.</summary>
    private static void Recast(ActionManager* manager, ActionType type, uint id, int maxCharges, ref Icon icon)
    {
        var total = manager->GetRecastTime(type, id);
        var elapsed = manager->GetRecastTimeElapsed(type, id);
        if (total > 0f && elapsed > 0f && elapsed < total)
        {
            var perCharge = maxCharges > 1 ? total / maxCharges : total;
            icon.Charges = maxCharges > 1 ? (int)(elapsed / perCharge) : 0;
            icon.Remaining = maxCharges > 1 ? perCharge - elapsed % perCharge : total - elapsed;
            icon.Total = perCharge;
        }
        else
        {
            icon.Charges = Math.Max(1, maxCharges);
            icon.Active = true;
        }
    }

    private uint ItemIcon(uint itemId)
    {
        var key = 0x8000_0000u | itemId;
        if (!iconCache.TryGetValue(key, out var icon))
        {
            icon = Svc.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRowOrDefault(itemId)?.Icon ?? 0;
            iconCache[key] = icon;
        }

        return icon;
    }

    private static ActionInfo? FindInfo(IReadOnlyList<ActionInfo> list, uint id)
    {
        foreach (var info in list)
        {
            if (info.Id == id)
            {
                return info;
            }
        }

        return null;
    }

    private uint IconOf(uint actionId, uint fallback)
    {
        if (!iconCache.TryGetValue(actionId, out var icon))
        {
            icon = Svc.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(actionId)?.Icon ?? fallback;
            iconCache[actionId] = icon;
        }

        return icon;
    }

    private static (int Columns, int Rows) Grid(CooldownBarSettings s, int count)
    {
        count = Math.Max(1, count);
        if (s.PerRow > 0)
        {
            var perRow = Math.Min(s.PerRow, count);
            return (perRow, (count + perRow - 1) / perRow);
        }

        var rows = Math.Clamp(s.Rows, 1, count);
        var columns = (int)Math.Ceiling(count / (float)rows);
        return (Math.Max(1, columns), rows);
    }
}
