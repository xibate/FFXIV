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
    public PowerPosition PowerPosition;
    public string LeftText;
    public string CenterText;
    public string RightText;
    public TextStyle? LeftStyle;
    public TextStyle? CenterStyle;
    public TextStyle? RightStyle;
    public HealthColorMode ColorMode;
    public bool ShowShield;

    /// <summary>Overrides the Style page's shield look; null uses it.</summary>
    public ShieldSettings? Shields;
    public bool Reverse;
    public bool Highlight;
    public bool Hovered;
    public bool FadeOutOfRange;

    /// <summary>Overrides the profile's frame background colour.</summary>
    public Vector4? Background;

    /// <summary>Game icon drawn at the left of the health bar (e.g. a job icon); 0 for none.</summary>
    public uint Icon;

    /// <summary>A job icon "portrait" beside the bars.</summary>
    public IconSide Portrait;

    /// <summary>Key for smooth health animation; 0 turns it off.</summary>
    public long SmoothKey;

    /// <summary>Bar texture name; null or empty uses the profile's.</summary>
    public string? Texture;
}

/// <summary>Draws a health (+ optional power) bar for a unit. Shared by unit frames and party frames.</summary>
public static class UnitBar
{
    public static void Render(DrawContext ctx, Vector2 pos, Vector2 size, in UnitData unit, in UnitBarOptions o)
    {
        using var barTexture = ctx.UseTexture(o.Texture);
        var s = ctx.Style;
        var savedAlpha = ctx.Alpha;
        if (o.FadeOutOfRange && !unit.InRange)
        {
            ctx.Alpha *= s.OutOfRangeAlpha;
        }

        var (min, max) = Gfx.Panel(ctx, pos, size, o.Background);
        var b = Math.Max(0, s.BorderSize);

        // Job icon portrait: a square the height of the frame, on one side of the bars.
        if (o.Portrait != IconSide.None)
        {
            var side = max.Y - min.Y;
            var portraitMin = o.Portrait == IconSide.Left ? min : new Vector2(max.X - side, min.Y);
            if (unit.JobId > 0)
            {
                var texture = Svc.TextureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(62100 + unit.JobId)).GetWrapOrEmpty();
                ctx.DrawList.AddImage(texture.Handle, portraitMin, portraitMin + new Vector2(side), Vector2.Zero, Vector2.One, ctx.Col(Vector4.One));
            }

            if (o.Portrait == IconSide.Left)
            {
                min.X += side + b;
                if (b > 0)
                {
                    ctx.DrawList.AddRectFilled(new Vector2(min.X - b, min.Y), new Vector2(min.X, max.Y), ctx.Col(s.Border));
                }
            }
            else
            {
                max.X -= side + b;
                if (b > 0)
                {
                    ctx.DrawList.AddRectFilled(new Vector2(max.X, min.Y), new Vector2(max.X + b, max.Y), ctx.Col(s.Border));
                }
            }
        }

        var showPower = o.ShowPower && o.PowerHeight > 0 && unit.MaxPower > 0;
        var powerAbove = o.PowerPosition == PowerPosition.Above;
        Vector2 healthMin = min, healthMax = max, powerMin = default, powerMax = default;
        if (showPower)
        {
            if (powerAbove)
            {
                powerMin = min;
                powerMax = new Vector2(max.X, min.Y + o.PowerHeight);
                healthMin = new Vector2(min.X, powerMax.Y + b);
            }
            else
            {
                healthMax = new Vector2(max.X, max.Y - o.PowerHeight - b);
                powerMin = new Vector2(min.X, healthMax.Y + b);
                powerMax = max;
            }
        }

        // Health
        var mode = o.ColorMode == HealthColorMode.Inherit ? s.HealthColors : o.ColorMode;
        var (fill, background) = HealthColors(s, unit, mode);
        if (background.HasValue)
        {
            Gfx.Fill(ctx, healthMin, healthMax, background.Value, allowGradient: false);
        }

        var hpFraction = ctx.Smooth(o.SmoothKey, unit.HpFraction);
        if (!unit.IsDead)
        {
            Gfx.Bar(ctx, healthMin, healthMax, hpFraction, fill, o.Reverse);
        }

        if (o.ShowShield && unit.ShieldFraction > 0f && unit.HasHealth && !unit.IsDead)
        {
            DrawShield(ctx, o.Shields ?? s.Shields, healthMin, healthMax, unit, o.Reverse);
        }

        // Power
        if (showPower)
        {
            if (b > 0)
            {
                var lineY = powerAbove ? powerMax.Y : healthMax.Y;
                ctx.DrawList.AddRectFilled(new Vector2(min.X, lineY), new Vector2(max.X, lineY + b), ctx.Col(s.Border));
            }

            var powerColor = unit.PowerKind switch
            {
                PowerKind.Gp => s.Gp,
                PowerKind.Cp => s.Cp,
                _ => s.Mana,
            };
            Gfx.Bar(ctx, powerMin, powerMax, unit.PowerFraction, powerColor, o.Reverse);
        }

        // Hover and target highlights
        if (o.Hovered)
        {
            ctx.DrawList.AddRectFilled(healthMin, healthMax, ctx.Col(new Vector4(1, 1, 1, 0.08f)));
        }

        if (o.Highlight)
        {
            ctx.DrawList.AddRect(pos - Vector2.One, pos + size + Vector2.One, ctx.Col(s.Accent), s.Rounding, default, 2f);
        }

        // Icon inside the health bar
        var textMin = healthMin;
        if (o.Icon != 0)
        {
            var iconSize = Math.Max(8f, healthMax.Y - healthMin.Y - 4f);
            var iconPos = new Vector2(healthMin.X + 2f, MathF.Floor((healthMin.Y + healthMax.Y - iconSize) / 2f));
            var texture = Svc.TextureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(o.Icon)).GetWrapOrEmpty();
            ctx.DrawList.AddImage(texture.Handle, iconPos, iconPos + new Vector2(iconSize), Vector2.Zero, Vector2.One, ctx.Col(Vector4.One));
            textMin.X += iconSize + 2f;
        }

        // Text
        var abbreviate = s.AbbreviateNumbers;
        var left = TextTags.Format(o.LeftText, unit, abbreviate);
        var center = TextTags.Format(o.CenterText, unit, abbreviate);
        var right = unit.IsDead ? "Dead" : TextTags.Format(o.RightText, unit, abbreviate);
        Gfx.TextRow(ctx, textMin, healthMax, (left, o.LeftStyle), (center, o.CenterStyle), (right, o.RightStyle), UnitColor(s, unit));

        ctx.Alpha = savedAlpha;
    }

    /// <summary>The unit's own colour (job colour for players, reaction colour for NPCs), used for "Job" text colour.</summary>
    public static Vector4 UnitColor(StyleSettings s, in UnitData unit) => unit.Reaction switch
    {
        UnitReaction.Hostile => s.Hostile,
        UnitReaction.Neutral => s.Neutral,
        UnitReaction.Friendly => s.Friendly,
        _ => Jobs.Get(unit.JobId).Color,
    };

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

    private static void DrawShield(DrawContext ctx, ShieldSettings settings, Vector2 min, Vector2 max, in UnitData unit, bool reverse)
    {
        var width = max.X - min.X;
        var hpWidth = MathF.Round(width * unit.HpFraction);
        var shieldWidth = MathF.Round(width * Math.Min(unit.ShieldFraction, 1f));
        if (shieldWidth <= 0f)
        {
            return;
        }

        // Work in "distance from the bar's start" (left, or right when reversed) and flip at the end.
        float from, to;
        var overflow = 0f;
        switch (settings.Placement)
        {
            case ShieldPlacement.Overlay:
                from = Math.Max(0f, hpWidth - shieldWidth);
                to = hpWidth;
                break;
            case ShieldPlacement.FromLeft:
                (from, to) = reverse ? (width - shieldWidth, width) : (0f, shieldWidth);
                break;
            case ShieldPlacement.FromRight:
                (from, to) = reverse ? (0f, shieldWidth) : (width - shieldWidth, width);
                break;
            default:
                var fits = Math.Min(shieldWidth, width - hpWidth);
                from = hpWidth;
                to = hpWidth + fits;
                overflow = shieldWidth - fits;
                break;
        }

        Vector2 Span(float a, float b, float top, float bottom) => reverse
            ? new Vector2(max.X - b, top)
            : new Vector2(min.X + a, top);

        if (to > from)
        {
            DrawShieldSegment(ctx, settings, Span(from, to, min.Y, max.Y), new Vector2(to - from, max.Y - min.Y), reverse ? -1 : 1, settings.Placement == ShieldPlacement.Overlay);
        }

        if (overflow > 0f)
        {
            switch (settings.Overshield)
            {
                case OvershieldMode.Strip:
                {
                    var strip = Math.Max(2f, MathF.Round((max.Y - min.Y) * 0.2f));
                    DrawShieldSegment(ctx, settings, Span(0f, overflow, min.Y, min.Y + strip), new Vector2(overflow, strip), reverse ? -1 : 1, false);
                    break;
                }

                case OvershieldMode.FromStart:
                    DrawShieldSegment(ctx, settings, Span(0f, overflow, min.Y, max.Y), new Vector2(overflow, max.Y - min.Y), reverse ? -1 : 1, true);
                    break;
            }
        }
    }

    /// <summary>One shield rectangle: solid or striped, with an optional bright line at its leading edge.</summary>
    private static void DrawShieldSegment(DrawContext ctx, ShieldSettings settings, Vector2 pos, Vector2 size, int direction, bool overHealth)
    {
        var dl = ctx.DrawList;
        var min = Gfx.Floor(pos);
        var max = min + Gfx.Floor(size);
        if (max.X - min.X < 1f || max.Y - min.Y < 1f)
        {
            return;
        }

        var opacity = Math.Clamp(settings.Opacity, 0f, 1f);
        var color = settings.OwnColor ? settings.Color : ctx.Style.Shield;
        if (settings.Look == ShieldLook.Striped)
        {
            // A faint fill with bright diagonal stripes, like EllesmereUI's striped absorbs.
            dl.AddRectFilled(min, max, ctx.Col(Colors.WithAlpha(color, color.W * opacity * (overHealth ? 0.25f : 0.35f))));
            var stripe = ctx.Col(Colors.WithAlpha(color, color.W * opacity));
            var height = max.Y - min.Y;
            dl.PushClipRect(min, max, true);
            for (var x = min.X - height; x < max.X; x += 6f)
            {
                dl.AddLine(new Vector2(x, max.Y), new Vector2(x + height, min.Y), stripe, 2f);
            }

            dl.PopClipRect();
        }
        else
        {
            dl.AddRectFilled(min, max, ctx.Col(Colors.WithAlpha(color, color.W * opacity * (overHealth ? 0.75f : 1f))));
        }

        if (settings.EdgeLine)
        {
            var x = direction > 0 ? max.X - 1f : min.X;
            dl.AddRectFilled(new Vector2(x - 1f, min.Y), new Vector2(x + 1f, max.Y), ctx.Col(Colors.Lighten(color, 0.6f)));
        }
    }
}
