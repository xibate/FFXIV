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

    /// <summary>Draws a bordered panel and returns the inner (content) rectangle.</summary>
    public static (Vector2 Min, Vector2 Max) Panel(DrawContext ctx, Vector2 pos, Vector2 size)
    {
        var s = ctx.Style;
        var b = Math.Max(0, s.BorderSize);
        var dl = ctx.DrawList;

        pos = Floor(pos);
        size = Floor(size);

        if (b > 0)
        {
            dl.AddRectFilled(pos, pos + size, ctx.Col(s.Border), s.Rounding);
        }

        var innerMin = pos + new Vector2(b);
        var innerMax = pos + size - new Vector2(b);
        dl.AddRectFilled(innerMin, innerMax, ctx.Col(s.Background), Math.Max(0, s.Rounding - b));
        return (innerMin, innerMax);
    }

    /// <summary>Fills a rectangle, with the profile's subtle vertical gradient if enabled.</summary>
    public static void Fill(DrawContext ctx, Vector2 min, Vector2 max, Vector4 color, bool allowGradient = true)
    {
        if (max.X - min.X < 0.5f || max.Y - min.Y < 0.5f)
        {
            return;
        }

        var s = ctx.Style;
        if (allowGradient && s.Gradient && s.Rounding <= 0f)
        {
            var top = ctx.Col(Colors.Lighten(color, s.GradientStrength));
            var bottom = ctx.Col(Colors.Darken(color, s.GradientStrength * 0.6f));
            ctx.DrawList.AddRectFilledMultiColor(min, max, top, top, bottom, bottom);
        }
        else
        {
            ctx.DrawList.AddRectFilled(min, max, ctx.Col(color), Math.Max(0, s.Rounding - s.BorderSize));
        }
    }

    /// <summary>Fills <paramref name="fraction"/> of a bar, from the left or (reversed) from the right.</summary>
    public static void Bar(DrawContext ctx, Vector2 min, Vector2 max, float fraction, Vector4 color, bool reverse = false)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        var width = MathF.Round((max.X - min.X) * fraction);
        if (reverse)
        {
            Fill(ctx, new Vector2(max.X - width, min.Y), max, color);
        }
        else
        {
            Fill(ctx, min, new Vector2(min.X + width, max.Y), color);
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
        if (ctx.Style.TextOutline)
        {
            var outline = ctx.Col(new Vector4(0, 0, 0, color.W * 0.85f));
            foreach (var offset in OutlineOffsets)
            {
                dl.AddText(pos + offset, outline, text);
            }
        }

        dl.AddText(pos, ctx.Col(color), text);
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
