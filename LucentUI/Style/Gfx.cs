using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LucentUI.Config;
using LucentUI.Hud;

namespace LucentUI.Style;

public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>Low-level drawing primitives shared by every HUD element.</summary>
public static class Gfx
{
    private static readonly Vector2[] OutlineOffsets =
    {
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1, 0), new(1, 0),
        new(-1, 1), new(0, 1), new(1, 1),
    };

    /// <summary>Draws a bordered panel (with optional drop shadow) and returns the inner (content) rectangle.</summary>
    public static (Vector2 Min, Vector2 Max) Panel(DrawContext ctx, Vector2 pos, Vector2 size, Vector4? background = null)
    {
        var s = ctx.Style;
        var b = Math.Max(0, s.BorderSize);
        var dl = ctx.DrawList;

        pos = Floor(pos);
        size = Floor(size);

        if (s.ShadowSize > 0)
        {
            Shadow(ctx, pos, pos + size, s.ShadowSize);
        }

        if (b > 0)
        {
            dl.AddRectFilled(pos, pos + size, ctx.Col(s.Border), s.Rounding);
        }

        var innerMin = pos + new Vector2(b);
        var innerMax = pos + size - new Vector2(b);
        dl.AddRectFilled(innerMin, innerMax, ctx.Col(background ?? s.Background), Math.Max(0, s.Rounding - b));
        return (innerMin, innerMax);
    }

    /// <summary>A soft shadow made of a few expanding, fading rectangles.</summary>
    public static void Shadow(DrawContext ctx, Vector2 min, Vector2 max, int size)
    {
        var color = ctx.Style.ShadowColor;
        for (var i = size; i >= 1; i--)
        {
            var alpha = Math.Min(1f, color.W * (1f - (float)(i - 1) / size) * 2f / size);
            var grow = new Vector2(i);
            var drop = new Vector2(0, 1);
            ctx.DrawList.AddRectFilled(min - grow + drop, max + grow + drop, ctx.Col(Colors.WithAlpha(color, alpha)), ctx.Style.Rounding + i);
        }
    }

    /// <summary>Fills a rectangle in the profile's bar style.</summary>
    public static void Fill(DrawContext ctx, Vector2 min, Vector2 max, Vector4 color, bool allowGradient = true, float uvStart = 0f, float uvEnd = 1f)
    {
        if (max.X - min.X < 0.5f || max.Y - min.Y < 0.5f)
        {
            return;
        }

        var s = ctx.Style;
        var dl = ctx.DrawList;

        // A bar texture (from the media library) tinted with the bar's colour, cropped like a WoW status bar.
        if (allowGradient && ctx.BarTexture() is { } texture)
        {
            dl.AddImage(texture.Handle, min, max, new Vector2(uvStart, 0f), new Vector2(uvEnd, 1f), ctx.Col(color));
            return;
        }
        var style = allowGradient && s.Rounding <= 0f ? s.BarStyle : BarStyle.Flat;
        var strength = s.GradientStrength;

        switch (style)
        {
            case BarStyle.Gradient:
            {
                var top = ctx.Col(Colors.Lighten(color, strength));
                var bottom = ctx.Col(Colors.Darken(color, strength * 0.6f));
                dl.AddRectFilledMultiColor(min, max, top, top, bottom, bottom);
                break;
            }

            case BarStyle.Glossy:
            {
                var top = ctx.Col(Colors.Lighten(color, strength * 0.5f));
                var bottom = ctx.Col(Colors.Darken(color, strength));
                dl.AddRectFilledMultiColor(min, max, top, top, bottom, bottom);
                var midY = MathF.Floor((min.Y + max.Y) / 2f);
                var shine = ctx.Col(new Vector4(1, 1, 1, 0.10f + strength * 0.3f));
                var clear = ctx.Col(new Vector4(1, 1, 1, 0.02f));
                dl.AddRectFilledMultiColor(min, new Vector2(max.X, midY), shine, shine, clear, clear);
                break;
            }

            case BarStyle.Inset:
            {
                var top = ctx.Col(Colors.Darken(color, strength * 1.4f));
                var bottom = ctx.Col(Colors.Lighten(color, strength * 0.4f));
                dl.AddRectFilledMultiColor(min, max, top, top, bottom, bottom);
                break;
            }

            case BarStyle.Striped:
            {
                dl.AddRectFilled(min, max, ctx.Col(color));
                var stripe = ctx.Col(Colors.Darken(color, 0.12f + strength * 0.4f));
                var height = max.Y - min.Y;
                dl.PushClipRect(min, max, true);
                for (var x = min.X - height; x < max.X; x += 6f)
                {
                    dl.AddLine(new Vector2(x, max.Y), new Vector2(x + height, min.Y), stripe, 2f);
                }

                dl.PopClipRect();
                break;
            }

            default:
                dl.AddRectFilled(min, max, ctx.Col(color), Math.Max(0, s.Rounding - s.BorderSize));
                break;
        }
    }

    /// <summary>Fills <paramref name="fraction"/> of a bar, from the left or (reversed) from the right.</summary>
    public static void Bar(DrawContext ctx, Vector2 min, Vector2 max, float fraction, Vector4 color, bool reverse = false)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        var width = MathF.Round((max.X - min.X) * fraction);
        if (reverse)
        {
            Fill(ctx, new Vector2(max.X - width, min.Y), max, color, true, 1f - fraction, 1f);
        }
        else
        {
            Fill(ctx, min, new Vector2(min.X + width, max.Y), color, true, 0f, fraction);
        }
    }

    /// <summary>A bordered bar with a fill: the building block for gauges and small bars.</summary>
    public static (Vector2 Min, Vector2 Max) FramedBar(DrawContext ctx, Vector2 pos, Vector2 size, float fraction, Vector4 color, bool reverse = false)
    {
        var (min, max) = Panel(ctx, pos, size);
        Bar(ctx, min, max, fraction, color, reverse);
        return (min, max);
    }

    /// <summary>A row of separate boxes, the first <paramref name="filled"/> of them coloured.</summary>
    public static void Pips(DrawContext ctx, Vector2 pos, Vector2 size, int count, Func<int, Vector4?> colorOf, float gap = 3f)
    {
        if (count <= 0)
        {
            return;
        }

        var width = (size.X - gap * (count - 1)) / count;
        for (var i = 0; i < count; i++)
        {
            var p = pos + new Vector2(MathF.Round(i * (width + gap)), 0);
            var (min, max) = Panel(ctx, p, new Vector2(MathF.Round(width), size.Y));
            if (colorOf(i) is { } color)
            {
                Fill(ctx, min, max, color);
            }
        }
    }

    /// <summary>Draws text vertically centred on <paramref name="anchor"/>, aligned horizontally around it.</summary>
    public static void Text(DrawContext ctx, string text, Vector2 anchor, TextAlign align, Vector4 color)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var size = ImGui.CalcTextSize(text);
        var pos = align switch
        {
            TextAlign.Center => anchor - new Vector2(size.X / 2f, 0),
            TextAlign.Right => anchor - new Vector2(size.X, 0),
            _ => anchor,
        };
        pos.Y -= size.Y / 2f;
        pos = Floor(pos);

        var dl = ctx.DrawList;
        if (ctx.TextChannel >= 0)
        {
            dl.ChannelsSetCurrent(ctx.TextChannel);
        }

        if (ctx.Style.TextOutline)
        {
            var outline = ctx.Col(new Vector4(0, 0, 0, color.W * 0.85f));
            foreach (var offset in OutlineOffsets)
            {
                dl.AddText(pos + offset, outline, text);
            }
        }

        dl.AddText(pos, ctx.Col(color), text);
        if (ctx.TextChannel >= 0)
        {
            dl.ChannelsSetCurrent(ctx.BaseChannel);
        }
    }

    /// <summary>Draws text with its own size, offset and colour.</summary>
    public static void StyledText(DrawContext ctx, string text, Vector2 anchor, TextAlign align, TextStyle? style, Vector4 jobColor)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (style == null)
        {
            Text(ctx, text, anchor, align, Vector4.One);
            return;
        }

        var color = style.Color switch
        {
            TextColorMode.Job => jobColor,
            TextColorMode.Custom => style.CustomColor,
            _ => Vector4.One,
        };

        using var font = ctx.Fonts.Push(style.FontSize);
        Text(ctx, text, anchor + style.Offset, align, color);
    }

    /// <summary>Left/centre/right text in a bar, each with its own style. Long text is clipped to the bar's width.</summary>
    public static void TextRow(DrawContext ctx, Vector2 min, Vector2 max, (string Text, TextStyle? Style) left, (string Text, TextStyle? Style) center, (string Text, TextStyle? Style) right, Vector4 jobColor, float padding = 5f)
    {
        var midY = (min.Y + max.Y) / 2f;

        // Clip sideways only, so text can be moved above or below the bar with an offset.
        ctx.DrawList.PushClipRect(new Vector2(min.X, min.Y - 200f), new Vector2(max.X, max.Y + 200f), true);
        StyledText(ctx, left.Text, new Vector2(min.X + padding, midY), TextAlign.Left, left.Style, jobColor);
        StyledText(ctx, center.Text, new Vector2((min.X + max.X) / 2f, midY), TextAlign.Center, center.Style, jobColor);
        StyledText(ctx, right.Text, new Vector2(max.X - padding, midY), TextAlign.Right, right.Style, jobColor);
        ctx.DrawList.PopClipRect();
    }

    /// <summary>Draws left/centre/right text inside a rectangle, clipped to it.</summary>
    public static void TextRow(DrawContext ctx, Vector2 min, Vector2 max, string left, string center, string right, float padding = 5f)
    {
        var white = new Vector4(1, 1, 1, 1);
        var midY = (min.Y + max.Y) / 2f;

        ctx.DrawList.PushClipRect(min, max, true);
        Text(ctx, left, new Vector2(min.X + padding, midY), TextAlign.Left, white);
        Text(ctx, center, new Vector2((min.X + max.X) / 2f, midY), TextAlign.Center, white);
        Text(ctx, right, new Vector2(max.X - padding, midY), TextAlign.Right, white);
        ctx.DrawList.PopClipRect();
    }

    public static Vector2 Floor(Vector2 v) => new(MathF.Floor(v.X), MathF.Floor(v.Y));
}
