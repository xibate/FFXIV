using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
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

        var savedAlpha = ctx.Alpha;
        foreach (var plate in plates)
        {
            if (!Units.TryRead(plate.Object, out var unit))
            {
                continue;
            }

            ctx.Alpha = savedAlpha * (target != null && !plate.IsTarget ? Math.Clamp(s.NonTargetAlpha, 0f, 1f) : 1f);
            if (plate.Mode == PlateMode.Bar && unit.HasHealth)
            {
                DrawBarPlate(ctx, s, unit, plate);
            }
            else
            {
                DrawNamePlate(ctx, s, unit, plate, IsFriendlyNpc(plate.Object));
            }
        }

        ctx.Alpha = savedAlpha;
    }

    private static bool IsFriendlyNpc(IGameObject obj) =>
        obj.ObjectKind is ObjectKind.EventNpc or ObjectKind.Retainer or ObjectKind.Companion;

    // Settings preview: an enemy health-bar plate (with a cast), a player name and an NPC name.
    private const float SampleGap = 36f;
    private const float SampleNameWidth = 150f;

    public override Vector2 GetPreviewSize(Profile profile)
    {
        var s = profile.Nameplates;
        var castHeight = s.ShowCast ? s.CastHeight + 1f : 0f;
        var nameHeight = (s.NameOnlyFontSize > 0 ? s.NameOnlyFontSize : profile.Style.FontSize) + 6f + Math.Abs(s.NameOnlyOffset.Y);
        return new Vector2(s.Size.X + 2 * (SampleGap + SampleNameWidth), Math.Max(s.Size.Y + castHeight, nameHeight));
    }

    public override void DrawSample(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.Nameplates;
        var size = GetPreviewSize(ctx.Profile);
        var bottom = pos.Y + size.Y;

        var enemy = Units.Sample("Striking Dummy", 0, 0.62f, UnitReaction.Hostile, 0f);
        var castHeight = s.ShowCast ? MathF.Round(s.CastHeight) : 0f;
        var barMin = Gfx.Floor(new Vector2(pos.X, bottom - s.Size.Y - (s.ShowCast ? castHeight + 1 : 0)));
        DrawBarUnit(ctx, s, enemy, barMin, Gfx.Floor(s.Size), false, false);
        if (s.ShowCast)
        {
            var cast = new CastData { Name = "Fire IV", Icon = 2660, Current = 1.1f, Total = 2.8f, Interruptible = false };
            DrawCast(ctx, s, cast, new Vector2(barMin.X, barMin.Y + MathF.Floor(s.Size.Y) + 1), new Vector2(MathF.Floor(s.Size.X), castHeight));
        }

        var player = Svc.ObjectTable.LocalPlayer;
        var name = Units.Sample(player?.Name.TextValue ?? "Your Name", player?.ClassJob.RowId ?? 19u, 1f, UnitReaction.Player, 0f);
        var npc = Units.Sample("Alphinaud", 0, 1f, UnitReaction.Friendly, 0f);
        var nameX = pos.X + s.Size.X + SampleGap + SampleNameWidth / 2f;
        var nameBottom = bottom - Math.Max(0f, s.NameOnlyOffset.Y);
        DrawNameText(ctx, s, name, new Vector2(nameX, nameBottom), false, false);
        DrawNameText(ctx, s, npc, new Vector2(nameX + SampleNameWidth + SampleGap, nameBottom), false, true);
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

    private static void DrawBarPlate(DrawContext ctx, NameplateSettings s, in UnitData unit, in Plate plate)
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
        DrawBarUnit(ctx, s, unit, min, size, plate.IsTarget, hovered);

        if (hasCast && Casts.TryGet(unit.Object, out var cast))
        {
            DrawCast(ctx, s, cast, new Vector2(min.X, min.Y + size.Y + 1), new Vector2(size.X, castHeight));
        }
    }

    private static void DrawBarUnit(DrawContext ctx, NameplateSettings s, in UnitData unit, Vector2 min, Vector2 size, bool isTarget, bool hovered)
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
            SmoothKey = unit.Object != null ? DrawContext.Key("nameplate", unit.Object.GameObjectId) : 0,
        });
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

        DrawNameText(ctx, s, unit, plate.Screen, plate.IsTarget, isNpc);
    }

    /// <summary>A name-only plate: its text's bottom centre sits on <paramref name="screen"/>, moved by the offset.</summary>
    private static void DrawNameText(DrawContext ctx, NameplateSettings s, in UnitData unit, Vector2 screen, bool isTarget, bool isNpc)
    {
        var text = TextTags.Format(s.NameOnlyText, unit, true);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var scale = isTarget ? Math.Clamp(s.TargetScale, 1f, 2f) : 1f;
        var fontSize = (int)MathF.Round((s.NameOnlyFontSize > 0 ? s.NameOnlyFontSize : ctx.Style.FontSize) * scale);
        using var font = ctx.Fonts.Push(fontSize);

        var textSize = Dalamud.Bindings.ImGui.ImGui.CalcTextSize(text);
        var anchor = Gfx.Floor(screen - new Vector2(0, textSize.Y / 2f) + s.NameOnlyOffset * scale);
        var min = anchor - textSize / 2f;
        var max = anchor + textSize / 2f;

        var hovered = s.Interactive && ctx.Input.HandleUnit(min, max, unit.Object, ctx.General);
        var color = isNpc
            ? s.NameOnlyOwnNpcColor ? s.NameOnlyNpcColor : ctx.Style.Friendly
            : s.NameOnlyJobColor ? UnitBar.UnitColor(ctx.Style, unit) : s.NameOnlyPlayerColor;
        if ((s.HighlightTarget && isTarget) || hovered)
        {
            color = Vector4.Lerp(color, Vector4.One, 0.35f);
        }

        Gfx.Text(ctx, text, anchor, TextAlign.Center, color);
    }
}
