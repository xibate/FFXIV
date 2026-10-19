using System;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace LucentUI.Hud.Elements;

/// <summary>
/// Health-bar nameplates above units. Uses the game's own list of units that currently have a nameplate (so line
/// of sight and the game's nameplate settings still apply) and its nameplate anchor position.
/// </summary>
public sealed unsafe class NameplatesElement : HudElement
{
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
        if (player == null)
        {
            return;
        }

        var uiModule = GameFramework.Instance()->GetUIModule();
        var ui3d = uiModule != null ? uiModule->GetUI3DModule() : null;
        if (ui3d == null)
        {
            return;
        }

        var target = Svc.TargetManager.Target;
        var count = Math.Min(ui3d->NamePlateObjectInfoCount, ui3d->NamePlateObjectInfoPointers.Length);

        // The game lists nearest first; draw far plates first so near ones end up on top.
        for (var i = count - 1; i >= 0; i--)
        {
            var info = ui3d->NamePlateObjectInfoPointers[i].Value;
            if (info == null || info->GameObject == null)
            {
                continue;
            }

            var obj = Svc.ObjectTable.CreateObjectReference((nint)info->GameObject);
            if (obj == null || obj.Address == player.Address || !Wanted(s, obj))
            {
                continue;
            }

            if (Vector3.Distance(player.Position, obj.Position) > s.MaxDistance)
            {
                continue;
            }

            // Just above the unit's head (same height DelvUI uses).
            var go = info->GameObject;
            var head = new Vector3(go->Position.X, go->Position.Y + go->Height * 2.2f, go->Position.Z);
            if (!Svc.GameGui.WorldToScreen(head, out var screen))
            {
                continue;
            }

            if (!Units.TryRead(obj, out var unit) || !unit.HasHealth)
            {
                continue;
            }

            var platePos = Gfx.Floor(screen + new Vector2(-s.Size.X / 2f, -s.Size.Y + s.YOffset));
            if (ctx.Clips.Covers(platePos, platePos + s.Size))
            {
                continue;
            }

            DrawPlate(ctx, s, unit, platePos, target != null && target.Address == obj.Address);
        }
    }

    private static void DrawPlate(DrawContext ctx, NameplateSettings s, in UnitData unit, Vector2 pos, bool isTarget)
    {
        UnitBar.Render(ctx, pos, s.Size, unit, new UnitBarOptions
        {
            ShowPower = false,
            LeftText = string.Empty,
            CenterText = string.Empty,
            RightText = string.Empty,
            ColorMode = HealthColorMode.Inherit,
            ShowShield = true,
            Highlight = s.HighlightTarget && isTarget,
            SmoothKey = unit.Object != null ? DrawContext.Key("nameplate", unit.Object.GameObjectId) : 0,
        });

        using var font = ctx.Fonts.PushSmall();
        var white = Vector4.One;
        var above = new Vector2(pos.X, pos.Y - 8);

        if (s.ShowName)
        {
            var name = s.ShowLevel && unit.Level > 0 ? $"{unit.Level}  {unit.Name}" : unit.Name;
            Gfx.Text(ctx, name, above, TextAlign.Left, isTarget ? ctx.Style.Accent : white);
        }

        if (s.ShowHealthText)
        {
            Gfx.Text(ctx, $"{(int)(unit.HpFraction * 100)}%", new Vector2(pos.X + s.Size.X, above.Y), TextAlign.Right, white);
        }

        if (s.ShowCast && Casts.TryGet(unit.Object, out var cast))
        {
            var castPos = pos + new Vector2(0, s.Size.Y + 2);
            var castSize = new Vector2(s.Size.X, Math.Max(4f, s.Size.Y * 0.6f));
            var color = cast.Interruptible ? ctx.Style.CastInterruptible : ctx.Style.Cast;
            Gfx.FramedBar(ctx, castPos, castSize, cast.Fraction, color);
            Gfx.Text(ctx, cast.Name, castPos + new Vector2(s.Size.X / 2f, castSize.Y + 8), TextAlign.Center, white);
        }
    }

    private static bool Wanted(NameplateSettings s, IGameObject obj)
    {
        switch (obj)
        {
            case IPlayerCharacter:
                return s.ShowPlayers;
            case IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant } enemy:
                return s.ShowEnemies && (!s.EnemiesOnlyInCombat || (enemy.StatusFlags & StatusFlags.InCombat) != 0);
            case IBattleNpc:
                return s.ShowFriendlyNpcs;
            default:
                return false;
        }
    }
}
