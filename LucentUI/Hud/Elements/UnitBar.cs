using System;
using System.Numerics;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;

namespace LucentUI.Hud.Elements;

public struct UnitBarOptions
{
    public bool ShowPower;
    public int PowerHeight;
    public string LeftText;
    public string CenterText;
    public string RightText;
    public HealthColorMode ColorMode;
    public bool ShowShield;
    public bool Reverse;
    public bool Highlight;
    public bool Hovered;
    public bool FadeOutOfRange;

    /// <summary>Game icon drawn at the left of the health bar (e.g. a job icon); 0 for none.</summary>
    public uint Icon;

    /// <summary>Key for smooth health animation; 0 turns it off.</summary>
    public long SmoothKey;
}

/// <summary>Draws a health (+ optional power) bar for a unit. Shared by unit frames and party frames.</summary>
public static class UnitBar
{
    public static void Render(DrawContext ctx, Vector2 pos, Vector2 size, in UnitData unit, in UnitBarOptions o)
    {
        var s = ctx.Style;
        var savedAlpha = ctx.Alpha;
        if (o.FadeOutOfRange && !unit.InRange)
        {
            ctx.Alpha *= s.OutOfRangeAlpha;
        }

        var (min, max) = Gfx.Panel(ctx, pos, size);
        var b = Math.Max(0, s.BorderSize);

        var showPower = o.ShowPower && o.PowerHeight > 0 && unit.MaxPower > 0;
        var healthMax = showPower ? new Vector2(max.X, max.Y - o.PowerHeight - b) : max;

        // Health
        var mode = o.ColorMode == HealthColorMode.Inherit ? s.HealthColors : o.ColorMode;
        var (fill, background) = HealthColors(s, unit, mode);
        if (background.HasValue)
        {
            Gfx.Fill(ctx, min, healthMax, background.Value, allowGradient: false);
        }

        var hpFraction = ctx.Smooth(o.SmoothKey, unit.HpFraction);
        if (!unit.IsDead)
        {
            Gfx.Bar(ctx, min, healthMax, hpFraction, fill, o.Reverse);
        }

        if (o.ShowShield && unit.ShieldFraction > 0f && unit.HasHealth && !unit.IsDead)
        {
            DrawShield(ctx, min, healthMax, unit, o.Reverse);
        }

        // Power
        if (showPower)
        {
            var powerMin = new Vector2(min.X, healthMax.Y + b);
            if (b > 0)
            {
                ctx.DrawList.AddRectFilled(new Vector2(min.X, healthMax.Y), new Vector2(max.X, powerMin.Y), ctx.Col(s.Border));
            }

            var powerColor = unit.PowerKind switch
            {
                PowerKind.Gp => s.Gp,
                PowerKind.Cp => s.Cp,
                _ => s.Mana,
            };
            Gfx.Bar(ctx, powerMin, max, unit.PowerFraction, powerColor, o.Reverse);
        }

        // Hover and target highlights
        if (o.Hovered)
        {
            ctx.DrawList.AddRectFilled(min, healthMax, ctx.Col(new Vector4(1, 1, 1, 0.08f)));
        }

        if (o.Highlight)
        {
            ctx.DrawList.AddRect(pos - Vector2.One, pos + size + Vector2.One, ctx.Col(s.Accent), s.Rounding, default, 2f);
        }

        // Icon
        var textMin = min;
        if (o.Icon != 0)
        {
            var iconSize = Math.Max(8f, healthMax.Y - min.Y - 4f);
            var iconPos = new Vector2(min.X + 2f, MathF.Floor((min.Y + healthMax.Y - iconSize) / 2f));
            var texture = Svc.TextureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(o.Icon)).GetWrapOrEmpty();
            ctx.DrawList.AddImage(texture.Handle, iconPos, iconPos + new Vector2(iconSize), Vector2.Zero, Vector2.One, ctx.Col(Vector4.One));
            textMin.X += iconSize + 2f;
        }

        // Text
        var abbreviate = s.AbbreviateNumbers;
        var left = TextTags.Format(o.LeftText, unit, abbreviate);
        var center = TextTags.Format(o.CenterText, unit, abbreviate);
        var right = unit.IsDead ? "Dead" : TextTags.Format(o.RightText, unit, abbreviate);
        Gfx.TextRow(ctx, textMin, healthMax, left, center, right);

        ctx.Alpha = savedAlpha;
    }

    /// <summary>Returns the fill colour and (if any) a coloured background for the missing-health area.</summary>
    private static (Vector4 Fill, Vector4? Background) HealthColors(StyleSettings s, in UnitData unit, HealthColorMode mode)
    {
        if (unit.IsDead)
        {
            return (s.Dead, s.Dead);
        }

        var unitColor = unit.Reaction switch
        {
            UnitReaction.Hostile => s.Hostile,
            UnitReaction.Neutral => s.Neutral,
            UnitReaction.Friendly => s.Friendly,
            _ => mode switch
            {
                HealthColorMode.Role => Jobs.RoleColor(Jobs.Get(unit.JobId).Role),
                HealthColorMode.Flat => s.FlatHealth,
                HealthColorMode.Gradient => Colors.HealthGradient(unit.HpFraction),
                _ => Jobs.Get(unit.JobId).Color,
            },
        };

        if (mode == HealthColorMode.Dark)
        {
            return (s.DarkFill, Colors.Darken(unitColor, 0.15f));
        }

        if (mode == HealthColorMode.Flat && unit.Reaction != UnitReaction.Player)
        {
            return (s.FlatHealth, null);
        }

        return (unitColor, null);
    }

    private static void DrawShield(DrawContext ctx, Vector2 min, Vector2 max, in UnitData unit, bool reverse)
    {
        var width = max.X - min.X;
        var hpWidth = MathF.Round(width * unit.HpFraction);
        var shieldWidth = MathF.Round(width * Math.Min(unit.ShieldFraction, 1f));
        var color = ctx.Style.Shield;

        // Shield sits in the missing-health area; anything that does not fit shows as a strip along the top.
        var fits = Math.Min(shieldWidth, width - hpWidth);
        var overflow = shieldWidth - fits;

        if (fits > 0)
        {
            var start = reverse ? max.X - hpWidth - fits : min.X + hpWidth;
            Gfx.Fill(ctx, new Vector2(start, min.Y), new Vector2(start + fits, max.Y), color, allowGradient: false);
        }

        if (overflow > 0)
        {
            var strip = Math.Max(2f, MathF.Round((max.Y - min.Y) * 0.2f));
            var start = reverse ? max.X - overflow : min.X;
            Gfx.Fill(ctx, new Vector2(start, min.Y), new Vector2(start + overflow, min.Y + strip), color, allowGradient: false);
        }
    }
}
