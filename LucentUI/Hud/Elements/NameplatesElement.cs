using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using LucentUI.Hud;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;
using FFXIVClientStructs.FFXIV.Client.UI;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace LucentUI.Hud.Elements;

/// <summary>
/// Nameplates above units: health-bar plates (with cast bars) or name-only plates, per kind of unit.
/// Units come from the game's own nameplate list when it has any (so line of sight and the game's settings apply),
/// otherwise from every unit nearby. Plates sit where the game puts its own nameplate.
/// </summary>
public sealed unsafe class NameplatesElement : HudElement
{
    private readonly List<Plate> plates = new();
    private readonly HashSet<nint> listed = new();

    private struct Plate
    {
        public IGameObject Object;
        public Vector2 Screen;
        public float Distance;
        public PlateMode Mode;
        public bool IsTarget;
    }

    public override string Id => "nameplates";

    public override string Label => "Nameplates";

    public override Vector2 DefaultPosition => Vector2.Zero;

    public override bool Movable => false;

    public override bool IsEnabled(Profile profile) => profile.Modules.Nameplates && profile.Nameplates.Enabled;

    public override Vector2 GetSize(Profile profile) => Vector2.Zero;

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.Nameplates;
        var player = Svc.ObjectTable.LocalPlayer;
        if (player == null || UnlockActive)
        {
            return;
        }

        var target = Svc.TargetManager.Target;
        Collect(s, player, target, ctx.DeltaTime);

        // Far plates first so nearer ones (and the target, last) end up on top.
        plates.Sort((a, b) => a.IsTarget != b.IsTarget ? (a.IsTarget ? 1 : -1) : b.Distance.CompareTo(a.Distance));
        if (s.Stacking)
        {
            Stack(s, ctx.DeltaTime);
        }

        var threat = ThreatActive(s);
        if (threat)
        {
            ReadEnmity();
        }

        var isTank = Jobs.Get(player.ClassJob.RowId).Role == JobRole.Tank;

        // Layers: bars, then icons, then all text, so names are never covered by another plate's bar or icons.
        var dl = ctx.DrawList;
        var layered = s.TextOnTop;
        if (layered)
        {
            dl.ChannelsSplit(3);
            dl.ChannelsSetCurrent(0);
            ctx.TextChannel = 2;
            ctx.BaseChannel = 0;
        }

        var savedAlpha = ctx.Alpha;
        try
        {
            foreach (var plate in plates)
            {
                if (!Units.TryRead(plate.Object, out var unit))
                {
                    continue;
                }

                ctx.Alpha = savedAlpha * (target != null && !plate.IsTarget ? Math.Clamp(s.NonTargetAlpha, 0f, 1f) : 1f);
                if (plate.Mode == PlateMode.Bar && unit.HasHealth)
                {
                    DrawBarPlate(ctx, s, unit, plate, threat ? ThreatColor(s, plate.Object, isTank) : null);
                }
                else
                {
                    DrawNamePlate(ctx, s, unit, plate, IsFriendlyNpc(plate.Object));
                }
            }
        }
        finally
        {
            ctx.Alpha = savedAlpha;
            if (layered)
            {
                ctx.TextChannel = -1;
                ctx.BaseChannel = 0;
                dl.ChannelsMerge();
            }
        }
    }

    private static bool IsFriendlyNpc(IGameObject obj) =>
        obj.ObjectKind is ObjectKind.EventNpc or ObjectKind.Retainer or ObjectKind.Companion;

    /// <summary>While on, drawing goes to the icon layer (between bars and text).</summary>
    private static void IconLayer(DrawContext ctx, bool on)
    {
        if (ctx.TextChannel < 0)
        {
            return;
        }

        ctx.BaseChannel = on ? 1 : 0;
        ctx.DrawList.ChannelsSetCurrent(ctx.BaseChannel);
    }

    // Threat: your enmity on each enemy (100 = you are at the top of its list), read once per frame.
    private static readonly Dictionary<uint, int> Enmity = new();

    private static bool ThreatActive(NameplateSettings s) => s.Threat switch
    {
        ThreatMode.Always => true,
        ThreatMode.InDuties => Svc.Condition[ConditionFlag.BoundByDuty] || Svc.Condition[ConditionFlag.BoundByDuty56] || Svc.Condition[ConditionFlag.BoundByDuty95],
        _ => false,
    };

    private static void ReadEnmity()
    {
        Enmity.Clear();
        var ui = UIState.Instance();
        if (ui == null)
        {
            return;
        }

        ref var hater = ref ui->Hater;
        var count = Math.Min(hater.HaterCount, hater.Haters.Length);
        for (var i = 0; i < count; i++)
        {
            Enmity[hater.Haters[i].EntityId] = hater.Haters[i].Enmity;
        }
    }

    /// <summary>Tanks: green with aggro, yellow when close, red without. Others: red with aggro, yellow when close.</summary>
    private static Vector4? ThreatColor(NameplateSettings s, IGameObject obj, bool isTank)
    {
        if (obj is not IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant } || !Enmity.TryGetValue(obj.EntityId, out var value))
        {
            return null;
        }

        if (isTank)
        {
            return value >= 100 ? s.TankHasAggro : value >= 50 ? s.TankLosingAggro : s.TankNoAggro;
        }

        return value >= 100 ? s.DpsHasAggro : value >= 80 ? s.DpsNearAggro : null;
    }

    // Stacking: per unit, how far its plate is currently lifted (eased so plates glide instead of jumping).
    private static readonly Dictionary<ulong, float> StackLift = new();
    private readonly List<(float Left, float Right, float Top, float Bottom)> placed = new();
    private readonly List<int> stackOrder = new();

    /// <summary>Lifts enemy bar plates that would overlap a nearer one, so every plate and name stays readable.</summary>
    private void Stack(NameplateSettings s, float deltaTime)
    {
        if (StackLift.Count > 400)
        {
            StackLift.Clear();
        }

        var height = s.Size.Y + (s.ShowCast ? s.CastHeight + 1f : 0f) + Math.Max(0f, -Math.Min(s.LeftStyle.Offset.Y, s.RightStyle.Offset.Y)) + 2f;
        var halfWidth = s.Size.X / 2f;

        stackOrder.Clear();
        for (var i = 0; i < plates.Count; i++)
        {
            if (plates[i].Mode == PlateMode.Bar)
            {
                stackOrder.Add(i);
            }
        }

        // Nearest plates keep their spot; farther ones move up out of the way.
        stackOrder.Sort((a, b) => plates[a].Distance.CompareTo(plates[b].Distance));
        placed.Clear();
        foreach (var index in stackOrder)
        {
            var plate = plates[index];
            var lift = 0f;
            for (var pass = 0; pass < 12; pass++)
            {
                var bottom = plate.Screen.Y - lift;
                var top = bottom - height;
                var moved = false;
                foreach (var r in placed)
                {
                    if (plate.Screen.X + halfWidth > r.Left && plate.Screen.X - halfWidth < r.Right && bottom > r.Top && top < r.Bottom)
                    {
                        lift = plate.Screen.Y - r.Top;
                        moved = true;
                    }
                }

                if (!moved)
                {
                    break;
                }
            }

            placed.Add((plate.Screen.X - halfWidth, plate.Screen.X + halfWidth, plate.Screen.Y - lift - height, plate.Screen.Y - lift));

            var key = plate.Object.GameObjectId;
            StackLift.TryGetValue(key, out var shown);
            shown += (lift - shown) * Math.Min(1f, deltaTime * 12f);
            if (Math.Abs(lift - shown) < 0.5f)
            {
                shown = lift;
            }

            StackLift[key] = shown;
            plate.Screen.Y -= MathF.Round(shown);
            plates[index] = plate;
        }
    }

    // Settings preview: an enemy health-bar plate (with a cast), a player name and an NPC name.
    private const float SampleGap = 36f;
    private const float SampleNameWidth = 170f;

    public override Vector2 GetPreviewSize(Profile profile)
    {
        var s = profile.Nameplates;
        var castHeight = s.ShowCast ? s.CastHeight + 1f : 0f;
        var iconsAbove = s.Debuffs.Enabled && s.Debuffs.Outside && s.Debuffs.Anchor is IconAnchor.Top or IconAnchor.TopLeft or IconAnchor.TopRight
            ? s.Debuffs.Size + 3f + Math.Max(0f, -s.Debuffs.Offset.Y)
            : 0f;
        var small = s.TitleFontSize > 0 ? s.TitleFontSize : profile.Style.SmallFontSize;
        var nameHeight = (s.NameOnlyFontSize > 0 ? s.NameOnlyFontSize : profile.Style.FontSize) + 6f + Math.Abs(s.NameOnlyOffset.Y)
            + (s.ShowTitle ? small + 2f : 0f)
            + (s.ShowFreeCompany && s.FreeCompanyPosition == FreeCompanyPlacement.Below ? small + 2f : 0f);
        return new Vector2(s.Size.X + 2 * (SampleGap + SampleNameWidth), Math.Max(s.Size.Y + castHeight + iconsAbove, nameHeight));
    }

    public override void DrawSample(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.Nameplates;
        var size = GetPreviewSize(ctx.Profile);
        var bottom = pos.Y + size.Y;

        var enemy = Units.Sample("Striking Dummy", 0, 0.62f, UnitReaction.Hostile, 0f);
        var castHeight = s.ShowCast ? MathF.Round(s.CastHeight) : 0f;
        var barSize = Gfx.Floor(s.Size);
        var barMin = Gfx.Floor(new Vector2(pos.X, bottom - s.Size.Y - (s.ShowCast ? castHeight + 1 : 0)));
        var threat = s.Threat != ThreatMode.Off ? s.TankHasAggro : (Vector4?)null;
        DrawBarUnit(ctx, s, enemy, barMin, barSize, true, false, threat);
        if (s.ShowCast)
        {
            var cast = new CastData { Name = "Fire IV", Icon = 2660, Current = 1.1f, Total = 2.8f, Interruptible = false };
            DrawCast(ctx, s, cast, new Vector2(barMin.X, barMin.Y + barSize.Y + 1), new Vector2(barSize.X, castHeight));
        }

        DrawPlateIcons(ctx, s, null, barMin, barSize, true);

        var player = Svc.ObjectTable.LocalPlayer;
        var name = Units.Sample(player?.Name.TextValue ?? "Your Name", player?.ClassJob.RowId ?? 19u, 1f, UnitReaction.Player, 0f);
        var npc = Units.Sample("Alphinaud", 0, 1f, UnitReaction.Friendly, 0f);
        var nameX = pos.X + s.Size.X + SampleGap + SampleNameWidth / 2f;
        var nameBottom = bottom - Math.Max(0f, s.NameOnlyOffset.Y);
        DrawNameText(ctx, s, name, new Vector2(nameX, nameBottom), false, false,
            s.ShowTitle ? "Warrior of Light" : string.Empty, true, s.ShowFreeCompany ? "LUI" : string.Empty, Markers.SampleIcon);
        DrawNameText(ctx, s, npc, new Vector2(nameX + SampleNameWidth + SampleGap, nameBottom), false, true,
            s.ShowTitle ? "Scions of the Seventh Dawn" : string.Empty, false, string.Empty, 0);
    }

    /// <summary>Plates hide in Unlock Mode, but stay while settings are previewed so changes show on real units.</summary>
    internal static bool UnlockActive;

    private const int MaxGamePlates = 50;
    private readonly Dictionary<nint, byte> plateSlots = new();

    /// <summary>
    /// Where the game draws its own nameplate for a unit, the way DelvUI reads it: the bottom centre of the plate's
    /// root node. It moves in step with the frame, so plates stick to the character.
    /// </summary>
    private bool GamePlateAnchor(AddonNamePlate* addon, nint address, Vector2 viewportPos, out Vector2 anchor)
    {
        anchor = default;
        if (addon == null || addon->NamePlateObjectArray == null || !plateSlots.TryGetValue(address, out var slot) || slot >= MaxGamePlates)
        {
            return false;
        }

        var node = addon->NamePlateObjectArray[slot].RootComponentNode;
        if (node == null)
        {
            return false;
        }

        var res = &node->AtkResNode;
        anchor = viewportPos + new Vector2(res->X + res->Width / 2f, res->Y + res->Height);
        return float.IsFinite(anchor.X) && float.IsFinite(anchor.Y);
    }

    // Per unit: the whole-pixel spot the plate was last drawn at.
    private readonly Dictionary<ulong, Vector2> drawnAt = new();

    /// <summary>
    /// Snaps to whole pixels with a little hysteresis: the plate only moves once the projected spot is a full pixel
    /// away, so sub-pixel noise from frame to frame cannot make it flicker between two pixels.
    /// </summary>
    private Vector2 Steady(ulong key, Vector2 projected)
    {
        if (drawnAt.Count > 400)
        {
            drawnAt.Clear();
        }

        if (drawnAt.TryGetValue(key, out var last) && Math.Abs(projected.X - last.X) < 1f && Math.Abs(projected.Y - last.Y) < 1f)
        {
            return last;
        }

        var snapped = new Vector2(MathF.Round(projected.X), MathF.Round(projected.Y));
        drawnAt[key] = snapped;
        return snapped;
    }

    // Per unit: plate height above its feet, smoothed so head-bob animations do not shake the plate.
    private readonly Dictionary<ulong, float> heights = new();

    private void Collect(NameplateSettings s, IPlayerCharacter player, IGameObject? target, float deltaTime)
    {
        plates.Clear();
        if (heights.Count > 400)
        {
            heights.Clear();
        }

        listed.Clear();

        // The game's own nameplate list: who has a nameplate (line of sight, the game's name settings), and
        // which of its plate slots shows them.
        plateSlots.Clear();
        var uiModule = GameFramework.Instance()->GetUIModule();
        var ui3d = uiModule != null ? uiModule->GetUI3DModule() : null;
        if (ui3d != null)
        {
            var count = Math.Min(ui3d->NamePlateObjectInfoCount, ui3d->NamePlateObjectInfoPointers.Length);
            for (var i = 0; i < count; i++)
            {
                var info = ui3d->NamePlateObjectInfoPointers[i].Value;
                if (info != null && info->GameObject != null)
                {
                    listed.Add((nint)info->GameObject);
                    plateSlots[(nint)info->GameObject] = info->NamePlateIndex;
                }
            }
        }

        var addon = (AddonNamePlate*)Svc.GameGui.GetAddonByName("NamePlate", 1).Address;
        var viewportPos = Dalamud.Bindings.ImGui.ImGui.GetMainViewport().Pos;

        // With nothing in the game's list (e.g. its nameplates are turned off), use every unit nearby.
        var useList = s.FollowGameNameplates && listed.Count > 0;

        foreach (var obj in Svc.ObjectTable)
        {
            if (!HasPlate(obj) || (!s.ShowSelf && obj.Address == player.Address))
            {
                continue;
            }

            var isTarget = target != null && target.Address == obj.Address;
            if (useList && !isTarget && !listed.Contains(obj.Address))
            {
                continue;
            }

            var mode = ModeFor(s, obj);
            if (mode == PlateMode.Hidden)
            {
                continue;
            }

            var distance = Vector3.Distance(player.Position, obj.Position);
            if (distance > s.MaxDistance)
            {
                continue;
            }

            var go = (GameObjectStruct*)obj.Address;
            if (!useList && ((int)go->RenderFlags != 0 || !go->GetIsTargetable()))
            {
                continue;
            }

            // The game's nameplate point follows the head as it animates; keep only its height above the feet,
            // smoothed, and stand it on the unit's position so names stay still.
            FFXIVClientStructs.FFXIV.Common.Math.Vector3 gameWorld;
            go->GetNamePlateWorldPosition(&gameWorld);
            // The model glides between the unit's network positions; follow the model, not the logical position,
            // so plates move with what is drawn instead of stepping.
            var feet = go->DrawObject != null
                ? new Vector3(go->DrawObject->Position.X, go->DrawObject->Position.Y, go->DrawObject->Position.Z)
                : obj.Position;
            if (!float.IsFinite(feet.X) || Vector3.Distance(feet, obj.Position) > 10f)
            {
                feet = obj.Position;
            }

            var measured = gameWorld.Y - feet.Y;
            if (!float.IsFinite(measured) || measured < 0f || measured > 30f)
            {
                measured = go->Height * 2.2f;
            }

            var key = obj.GameObjectId;
            // Ignore tiny changes (breathing, idle sway); ease towards real ones (mounting, scaling).
            var height = heights.TryGetValue(key, out var previous)
                ? Math.Abs(measured - previous) < 0.08f ? previous : previous + (measured - previous) * (1f - MathF.Exp(-Math.Max(0f, deltaTime) / 0.4f))
                : measured;
            heights[key] = height;
            var world = new Vector3(feet.X, feet.Y + height, feet.Z);

            // Prefer the spot the game puts its own nameplate: it moves exactly with the character on screen.
            // Units without a game nameplate fall back to projecting their position.
            if (!Svc.GameGui.WorldToScreen(world, out var projected))
            {
                continue;
            }

            // The game's own plate position when it has one (as DelvUI does); our projection otherwise.
            var screen = Steady(key, GamePlateAnchor(addon, obj.Address, viewportPos, out var gameAnchor) ? gameAnchor : projected);

            plates.Add(new Plate { Object = obj, Screen = screen + new Vector2(0, s.YOffset), Distance = distance, Mode = mode, IsTarget = isTarget });
        }
    }

    /// <summary>Kinds of objects that can get a plate: players, battle NPCs, town NPCs, chocobos and retainers.</summary>
    private static bool HasPlate(IGameObject obj) =>
        obj.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc or ObjectKind.Companion or ObjectKind.Retainer;

    /// <summary>Whether a unit gets a LucentUI plate (before the on-screen checks).</summary>
    public static bool Replaces(NameplateSettings s, IGameObject obj, IGameObject player) =>
        HasPlate(obj) &&
        (s.ShowSelf || obj.Address != player.Address) &&
        ModeFor(s, obj) != PlateMode.Hidden &&
        Vector3.Distance(player.Position, obj.Position) <= s.MaxDistance;

    private static PlateMode ModeFor(NameplateSettings s, IGameObject obj)
    {
        switch (obj)
        {
            case IPlayerCharacter:
                return s.Players;
            case IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant } enemy:
                if (enemy.IsDead || (s.EnemiesOnlyInCombat && (enemy.StatusFlags & StatusFlags.InCombat) == 0))
                {
                    return PlateMode.Hidden;
                }

                return s.Enemies;
            case IBattleNpc:
                return s.FriendlyNpcs;
            case not null when obj.ObjectKind is ObjectKind.EventNpc or ObjectKind.Companion or ObjectKind.Retainer:
                return s.FriendlyNpcs;
            default:
                return PlateMode.Hidden;
        }
    }

    private static readonly StatusIcons DebuffIcons = new();
    private static readonly StatusIcons BuffIcons = new();

    private static void DrawBarPlate(DrawContext ctx, NameplateSettings s, in UnitData unit, in Plate plate, Vector4? fill)
    {
        var scale = plate.IsTarget ? Math.Clamp(s.TargetScale, 1f, 2f) : 1f;
        var size = Gfx.Floor(s.Size * scale);
        var hasCast = s.ShowCast && Casts.TryGet(unit.Object, out _);
        var castHeight = hasCast ? MathF.Round(s.CastHeight * scale) : 0f;

        // The plate's bottom (cast bar included) sits on the game's nameplate point.
        var min = Gfx.Floor(plate.Screen - new Vector2(size.X / 2f, size.Y + castHeight + (hasCast ? 1 : 0)));
        if (ctx.Clips.Contains(min + size / 2f))
        {
            return;
        }

        var hovered = s.Interactive && ctx.Input.HandleUnit(min, min + size, unit.Object, ctx.General);
        DrawBarUnit(ctx, s, unit, min, size, plate.IsTarget, hovered, fill);

        if (hasCast && Casts.TryGet(unit.Object, out var cast))
        {
            DrawCast(ctx, s, cast, new Vector2(min.X, min.Y + size.Y + 1), new Vector2(size.X, castHeight));
        }

        DrawPlateIcons(ctx, s, unit.Object, min, size, plate.IsTarget);
    }

    private static void DrawBarUnit(DrawContext ctx, NameplateSettings s, in UnitData unit, Vector2 min, Vector2 size, bool isTarget, bool hovered, Vector4? fill)
    {
        UnitBar.Render(ctx, min, size, unit, new UnitBarOptions
        {
            Texture = s.Texture,
            ShowPower = false,
            LeftText = s.LeftText,
            CenterText = s.CenterText,
            RightText = s.RightText,
            LeftStyle = s.LeftStyle,
            CenterStyle = s.CenterStyle,
            RightStyle = s.RightStyle,
            ColorMode = s.ColorMode,
            ShowShield = s.ShowShield,
            Highlight = s.HighlightTarget && isTarget,
            Hovered = hovered,
            FillColor = fill,
            SmoothKey = unit.Object != null ? DrawContext.Key("nameplate", unit.Object.GameObjectId) : 0,
        });

        // Execute line: where the health bar reaches the chosen percentage.
        if (s.ExecutePercent > 0 && s.ExecutePercent < 100 && unit.Reaction == UnitReaction.Hostile)
        {
            var b = Math.Max(0, ctx.Style.BorderSize);
            var x = MathF.Round(min.X + b + (size.X - 2 * b) * s.ExecutePercent / 100f);
            ctx.DrawList.AddRectFilled(new Vector2(x - 1, min.Y + b), new Vector2(x + 1, min.Y + size.Y - b), ctx.Col(new Vector4(1, 1, 1, 0.85f)));
        }
    }

    /// <summary>Debuffs, buffs, head marker and target arrows around a health-bar plate.</summary>
    private static void DrawPlateIcons(DrawContext ctx, NameplateSettings s, IGameObject? obj, Vector2 min, Vector2 size, bool isTarget)
    {
        IconLayer(ctx, true);
        DebuffIcons.Draw(ctx, s.Debuffs, obj, true, min, size);
        BuffIcons.Draw(ctx, s.Buffs, obj, false, min, size);

        if (s.Marker.Enabled)
        {
            var icon = Markers.IconFor(obj);
            if (icon == 0 && obj == null && ctx.Preview)
            {
                icon = Markers.SampleIcon;
            }

            MarkerIcon.Draw(ctx, s.Marker, icon, min, size);
        }

        if (s.TargetArrows && isTarget)
        {
            // Two arrows pointing at the plate from its sides.
            var h = MathF.Max(8f, size.Y);
            var w = MathF.Round(h * 0.6f);
            var cy = min.Y + size.Y / 2f;
            var color = ctx.Col(s.TargetArrowColor);
            var left = min.X - 4f;
            var right = min.X + size.X + 4f;
            ctx.DrawList.AddTriangleFilled(new Vector2(left - w, cy - h / 2f), new Vector2(left, cy), new Vector2(left - w, cy + h / 2f), color);
            ctx.DrawList.AddTriangleFilled(new Vector2(right + w, cy - h / 2f), new Vector2(right + w, cy + h / 2f), new Vector2(right, cy), color);
        }

        IconLayer(ctx, false);
    }

    private static void DrawCast(DrawContext ctx, NameplateSettings s, in CastData cast, Vector2 pos, Vector2 size)
    {
        var (min, max) = Gfx.Panel(ctx, pos, size);
        if (s.ShowCastIcon && cast.Icon != 0)
        {
            var iconSize = max.Y - min.Y;
            var texture = Svc.TextureProvider.GetFromGameIcon(new GameIconLookup(cast.Icon)).GetWrapOrEmpty();
            ctx.DrawList.AddImage(texture.Handle, min, min + new Vector2(iconSize), new Vector2(0.08f), new Vector2(0.92f), ctx.Col(Vector4.One));
            min.X += iconSize + Math.Max(0, ctx.Style.BorderSize);
        }

        var color = cast.Interruptible ? ctx.Style.CastInterruptible : ctx.Style.Cast;
        Gfx.Bar(ctx, min, max, cast.Fraction, color);

        var name = s.ShowCastName ? cast.Name : string.Empty;
        var timer = Math.Max(0f, cast.Remaining).ToString("0.0", CultureInfo.InvariantCulture);
        Gfx.TextRow(ctx, min, max, (name, s.CastNameStyle), (string.Empty, null), (timer, s.CastNameStyle), color, 3f);
    }

    private static void DrawNamePlate(DrawContext ctx, NameplateSettings s, in UnitData unit, in Plate plate, bool isNpc)
    {
        if (ctx.Clips.Contains(plate.Screen))
        {
            return;
        }

        var title = s.ShowTitle ? NameplateInfo.Title(plate.Object) : string.Empty;
        var titleAbove = s.TitlePosition == TitlePlacement.Above || (s.TitlePosition == TitlePlacement.Game && NameplateInfo.TitleAbove(plate.Object));
        var fc = s.ShowFreeCompany && !isNpc ? NameplateInfo.FreeCompany(plate.Object) : string.Empty;
        DrawNameText(ctx, s, unit, plate.Screen, plate.IsTarget, isNpc, title, titleAbove, fc, s.Marker.Enabled ? Markers.IconFor(plate.Object) : 0);
    }

    /// <summary>
    /// A name-only plate: the name (with the FC tag after it, if wanted), a title above or below, and the FC tag on
    /// its own line if wanted. The bottom of the block sits on <paramref name="screen"/>, moved by the offset.
    /// </summary>
    private static void DrawNameText(DrawContext ctx, NameplateSettings s, in UnitData unit, Vector2 screen, bool isTarget, bool isNpc, string title, bool titleAbove, string fc, uint marker)
    {
        var name = TextTags.Format(s.NameOnlyText, unit, true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var scale = isTarget ? Math.Clamp(s.TargetScale, 1f, 2f) : 1f;
        var nameFont = (int)MathF.Round((s.NameOnlyFontSize > 0 ? s.NameOnlyFontSize : ctx.Style.FontSize) * scale);
        var smallFont = (int)MathF.Round((s.TitleFontSize > 0 ? s.TitleFontSize : ctx.Style.SmallFontSize) * scale);

        var fcAfterName = fc.Length > 0 && s.FreeCompanyPosition == FreeCompanyPlacement.AfterName;
        var nameLine = fcAfterName ? name + "  «" + fc + "»" : name;
        var titleLine = title.Length > 0 ? "«" + title + "»" : string.Empty;
        var fcLine = fc.Length > 0 && !fcAfterName ? "«" + fc + "»" : string.Empty;

        var nameSize = Measure(ctx, nameLine, nameFont);
        var titleSize = Measure(ctx, titleLine, smallFont);
        var fcSize = Measure(ctx, fcLine, smallFont);
        const float lineGap = 1f;

        var total = nameSize.Y
            + (titleLine.Length > 0 ? titleSize.Y + lineGap : 0f)
            + (fcLine.Length > 0 ? fcSize.Y + lineGap : 0f);
        var anchor = Gfx.Floor(screen + s.NameOnlyOffset * scale);
        var y = anchor.Y - total;

        var color = isNpc
            ? s.NameOnlyOwnNpcColor ? s.NameOnlyNpcColor : ctx.Style.Friendly
            : s.NameOnlyJobColor ? UnitBar.UnitColor(ctx.Style, unit) : s.NameOnlyPlayerColor;

        // Hover first (so the highlight shows this frame), over the whole block.
        var width = Math.Max(nameSize.X, Math.Max(titleSize.X, fcSize.X));
        var blockMin = new Vector2(anchor.X - width / 2f, y);
        var blockMax = new Vector2(anchor.X + width / 2f, anchor.Y);
        var hovered = s.Interactive && s.NameOnlyInteractive && ctx.Input.HandleUnit(blockMin, blockMax, unit.Object, ctx.General);
        if ((s.HighlightTarget && isTarget) || hovered)
        {
            color = Vector4.Lerp(color, Vector4.One, 0.35f);
        }

        if (titleLine.Length > 0 && titleAbove)
        {
            DrawLine(ctx, titleLine, smallFont, anchor.X, ref y, titleSize.Y + lineGap, s.TitleColor);
        }

        var nameTop = y;
        DrawLine(ctx, nameLine, nameFont, anchor.X, ref y, nameSize.Y + lineGap, color);

        if (titleLine.Length > 0 && !titleAbove)
        {
            DrawLine(ctx, titleLine, smallFont, anchor.X, ref y, titleSize.Y + lineGap, s.TitleColor);
        }

        if (fcLine.Length > 0)
        {
            DrawLine(ctx, fcLine, smallFont, anchor.X, ref y, fcSize.Y + lineGap, s.TitleColor);
        }

        // Job icon and head marker to the left of the name line.
        var lineMin = new Vector2(MathF.Floor(anchor.X - nameSize.X / 2f), nameTop);
        if (s.NameOnlyJobIcon && !isNpc && unit.JobId > 0)
        {
            var iconSize = MathF.Round(nameSize.Y);
            var iconMin = new Vector2(lineMin.X - iconSize - 3f, nameTop);
            var texture = Svc.TextureProvider.GetFromGameIcon(new GameIconLookup(62100 + unit.JobId)).GetWrapOrEmpty();
            ctx.DrawList.AddImage(texture.Handle, iconMin, iconMin + new Vector2(iconSize), Vector2.Zero, Vector2.One, ctx.Col(Vector4.One));
            lineMin.X = iconMin.X;
        }

        if (marker != 0 && s.Marker.Enabled)
        {
            var lineSize = new Vector2(anchor.X + nameSize.X / 2f - lineMin.X, nameSize.Y);
            MarkerIcon.Draw(ctx, s.Marker, marker, lineMin, lineSize);
        }
    }

    private static Vector2 Measure(DrawContext ctx, string text, int fontSize)
    {
        if (text.Length == 0)
        {
            return Vector2.Zero;
        }

        using var font = ctx.Fonts.Push(fontSize);
        return Dalamud.Bindings.ImGui.ImGui.CalcTextSize(text);
    }

    private static void DrawLine(DrawContext ctx, string text, int fontSize, float centreX, ref float y, float advance, Vector4 color)
    {
        using var font = ctx.Fonts.Push(fontSize);
        var size = Dalamud.Bindings.ImGui.ImGui.CalcTextSize(text);
        Gfx.Text(ctx, text, new Vector2(centreX, MathF.Floor(y + size.Y / 2f)), TextAlign.Center, color);
        y += advance;
    }
}
