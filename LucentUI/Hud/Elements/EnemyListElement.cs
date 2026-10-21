using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;

namespace LucentUI.Hud.Elements;

/// <summary>The enemies the game lists in its enemy list, as small frames.</summary>
public sealed unsafe class EnemyListElement : HudElement
{
    private readonly List<UnitData> enemies = new();

    public override string Id => "enemylist";

    public override string Label => "Enemy List";

    public override Vector2 DefaultPosition => new(700, -120);

    public override bool IsEnabled(Profile profile) => profile.Modules.EnemyList && profile.EnemyList.Enabled;

    public override VisibilitySettings? GetVisibility(Profile profile) => profile.EnemyList.Visibility;

    public override Vector2 GetSize(Profile profile)
    {
        var s = profile.EnemyList;
        var count = Math.Max(1, s.MaxEnemies);
        return new Vector2(s.Size.X, count * s.Size.Y + (count - 1) * s.Spacing);
    }

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.EnemyList;
        Collect(s);

        if (enemies.Count == 0 && ctx.Preview)
        {
            enemies.Add(Units.Sample("Enemy One", 0, 0.8f, UnitReaction.Hostile, 0f));
            enemies.Add(Units.Sample("Enemy Two", 0, 0.45f, UnitReaction.Hostile, 0f));
            enemies.Add(Units.Sample("Enemy Three", 0, 0.2f, UnitReaction.Hostile, 0f));
        }

        var player = Svc.ObjectTable.LocalPlayer;
        var target = Svc.TargetManager.Target;
        var size = GetSize(ctx.Profile);

        for (var i = 0; i < enemies.Count; i++)
        {
            var unit = enemies[i];
            var y = s.GrowUp ? size.Y - (i + 1) * s.Size.Y - i * s.Spacing : i * (s.Size.Y + s.Spacing);
            var rowPos = pos + new Vector2(0, y);

            var hovered = !ctx.Preview && ctx.Input.HandleUnit(rowPos, rowPos + s.Size, unit.Object, ctx.General);
            var isTarget = target != null && unit.Object != null && target.Address == unit.Object.Address;

            UnitBar.Render(ctx, rowPos, s.Size, unit, new UnitBarOptions
            {
                LeftText = s.LeftText,
                CenterText = string.Empty,
                RightText = s.RightText,
                LeftStyle = s.LeftStyle,
                RightStyle = s.RightStyle,
                ColorMode = HealthColorMode.Inherit,
                ShowShield = false,
                Highlight = isTarget,
                Hovered = hovered,
                SmoothKey = unit.Object != null ? DrawContext.Key(Id, unit.Object.GameObjectId) : 0,
            });

            // Aggro: a marker when the enemy is targeting you.
            if (s.ShowAggro && player != null && unit.Object?.TargetObjectId == player.GameObjectId)
            {
                var marker = new Vector2(rowPos.X - 6, rowPos.Y + s.Size.Y / 2f);
                ctx.DrawList.AddCircleFilled(marker, 3.5f, ctx.Col(Colors.Rgb(0xE05A50)));
            }

            if (s.ShowCast && Casts.TryGet(unit.Object, out var cast))
            {
                var stripMin = new Vector2(rowPos.X + 1, rowPos.Y + s.Size.Y - 4);
                var stripMax = new Vector2(rowPos.X + 1 + (s.Size.X - 2) * cast.Fraction, rowPos.Y + s.Size.Y - 1);
                ctx.DrawList.AddRectFilled(stripMin, stripMax, ctx.Col(cast.Interruptible ? ctx.Style.CastInterruptible : ctx.Style.Cast));
            }
        }
    }

    private void Collect(EnemyListSettings s)
    {
        enemies.Clear();
        var array = EnemyListNumberArray.Instance();
        if (array == null)
        {
            return;
        }

        var count = Math.Min(array->EnemyCount, array->Enemies.Length);
        for (var i = 0; i < count && enemies.Count < s.MaxEnemies; i++)
        {
            var obj = Svc.ObjectTable.SearchByEntityId((uint)array->Enemies[i].EntityId);
            if (Units.TryRead(obj, out var unit))
            {
                enemies.Add(unit);
            }
        }
    }
}
