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
        if (player == null || ctx.Preview)
        {
            return;
        }

        var target = Svc.TargetManager.Target;
        Collect(s, player, target);

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
                DrawNamePlate(ctx, s, unit, plate);
            }
        }

        ctx.Alpha = savedAlpha;
    }

    private void Collect(NameplateSettings s, IPlayerCharacter player, IGameObject? target)
    {
        plates.Clear();
        listed.Clear();

        if (s.FollowGameNameplates)
        {
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
                    }
                }
            }
        }

        // With nothing in the game's list (e.g. its nameplates are turned off), use every unit nearby.
        var useList = s.FollowGameNameplates && listed.Count > 0;
        foreach (var obj in Svc.ObjectTable)
        {
            if (obj is not ICharacter || (!s.ShowSelf && obj.Address == player.Address))
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

            FFXIVClientStructs.FFXIV.Common.Math.Vector3 gameWorld;
            go->GetNamePlateWorldPosition(&gameWorld);
            var world = new Vector3(gameWorld.X, gameWorld.Y, gameWorld.Z);
            if (!float.IsFinite(world.Y) || Vector3.Distance(world, obj.Position) > 30f)
            {
                world = new Vector3(obj.Position.X, obj.Position.Y + go->Height * 2.2f, obj.Position.Z);
            }

            if (!Svc.GameGui.WorldToScreen(world, out var screen))
            {
                continue;
            }

            plates.Add(new Plate { Object = obj, Screen = screen + new Vector2(0, s.YOffset), Distance = distance, Mode = mode, IsTarget = isTarget });
        }
    }

    /// <summary>Whether a unit gets a LucentUI plate (before the on-screen checks).</summary>
    public static bool Replaces(NameplateSettings s, IGameObject obj, IGameObject player) =>
        obj is ICharacter &&
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
        UnitBar.Render(ctx, min, size, unit, new UnitBarOptions
        {
            ShowPower = false,
            LeftText = s.LeftText,
            CenterText = s.CenterText,
            RightText = s.RightText,
            LeftStyle = s.LeftStyle,
            CenterStyle = s.CenterStyle,
            RightStyle = s.RightStyle,
            ColorMode = s.ColorMode,
            ShowShield = s.ShowShield,
            Highlight = s.HighlightTarget && plate.IsTarget,
            Hovered = hovered,
            SmoothKey = unit.Object != null ? DrawContext.Key("nameplate", unit.Object.GameObjectId) : 0,
        });

        if (hasCast && Casts.TryGet(unit.Object, out var cast))
        {
            DrawCast(ctx, s, cast, new Vector2(min.X, min.Y + size.Y + 1), new Vector2(size.X, castHeight));
        }
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

    private static void DrawNamePlate(DrawContext ctx, NameplateSettings s, in UnitData unit, in Plate plate)
    {
        var text = TextTags.Format(s.NameOnlyText, unit, true);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var scale = plate.IsTarget ? Math.Clamp(s.TargetScale, 1f, 2f) : 1f;
        var fontSize = (int)MathF.Round((s.NameOnlyFontSize > 0 ? s.NameOnlyFontSize : ctx.Style.FontSize) * scale);
        using var font = ctx.Fonts.Push(fontSize);

        var textSize = Dalamud.Bindings.ImGui.ImGui.CalcTextSize(text);
        var anchor = Gfx.Floor(plate.Screen - new Vector2(0, textSize.Y / 2f));
        var min = anchor - textSize / 2f;
        var max = anchor + textSize / 2f;
        if (ctx.Clips.Contains(anchor))
        {
            return;
        }

        var hovered = s.Interactive && ctx.Input.HandleUnit(min, max, unit.Object, ctx.General);
        var color = s.NameOnlyJobColor ? UnitBar.UnitColor(ctx.Style, unit) : Vector4.One;
        if ((s.HighlightTarget && plate.IsTarget) || hovered)
        {
            color = Vector4.Lerp(color, Vector4.One, 0.35f);
        }

        Gfx.Text(ctx, text, anchor, TextAlign.Center, color);
    }
}
