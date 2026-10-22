using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LucentUI.Gauges;
using LucentUI.Hud;

namespace LucentUI.Style;

/// <summary>
/// The "hybrid" job gauge look: the game's gauge shapes (angled bars split at spending points, diamonds, orbs and
/// petals) drawn flat in LucentUI's style, with thin black outlines and a soft glow on filled pieces.
/// </summary>
public static class HybridGauge
{
    private static readonly Vector4 Track = new(0.114f, 0.122f, 0.149f, 0.95f);
    private static readonly Vector4 Outline = new(0f, 0f, 0f, 1f);
    private static readonly List<Vector2> Points = new();
    private static readonly List<Vector2> Clipped = new();

    /// <summary>An angled bar (pointed ends) filled to <paramref name="fraction"/>, split into equal segments.</summary>
    public static (Vector2 Min, Vector2 Max) Bar(DrawContext ctx, Vector2 pos, Vector2 size, float fraction, Vector4 color, int segments)
    {
        var min = Gfx.Floor(pos);
        var max = min + Gfx.Floor(size);
        var height = max.Y - min.Y;
        var notch = MathF.Min(MathF.Round(height / 2f), (max.X - min.X) / 4f);
        var midY = min.Y + height / 2f;

        Points.Clear();
        Points.Add(new Vector2(min.X, midY));
        Points.Add(new Vector2(min.X + notch, min.Y));
        Points.Add(new Vector2(max.X - notch, min.Y));
        Points.Add(new Vector2(max.X, midY));
        Points.Add(new Vector2(max.X - notch, max.Y));
        Points.Add(new Vector2(min.X + notch, max.Y));

        var dl = ctx.DrawList;
        FillConvex(dl, Points, ctx.Col(Track));

        fraction = Math.Clamp(fraction, 0f, 1f);
        if (fraction > 0f)
        {
            var fillX = min.X + (max.X - min.X) * fraction;
            ClipLeftOf(Points, fillX, Clipped);
            Glow(dl, ctx, Clipped, color);
            FillConvex(dl, Clipped, ctx.Col(color));

            // A lighter top edge gives the fill a little depth.
            var highlight = ctx.Col(Colors.WithAlpha(Colors.Lighten(color, 0.35f), 0.55f));
            var topRight = Math.Min(fillX, max.X - notch);
            if (topRight > min.X + notch)
            {
                dl.AddLine(new Vector2(min.X + notch, min.Y + 1.5f), new Vector2(topRight, min.Y + 1.5f), highlight, 1f);
            }
        }

        // Slanted dividers at the spending points.
        if (segments > 1)
        {
            var divider = ctx.Col(Outline);
            for (var i = 1; i < segments; i++)
            {
                var x = MathF.Round(min.X + (max.X - min.X) * i / segments);
                var lean = MathF.Max(1.5f, height * 0.18f);
                dl.AddLine(new Vector2(x + lean, min.Y), new Vector2(x - lean, max.Y), divider, 2f);
            }
        }

        Stroke(dl, Points, ctx.Col(Outline), 1f);
        return (new Vector2(min.X + notch, min.Y), new Vector2(max.X - notch, max.Y));
    }

    /// <summary>A row of stacks or pips as shapes, centred in the row. Empty ones are dark with an outline.</summary>
    public static void Shapes(DrawContext ctx, Vector2 pos, Vector2 size, int count, Func<int, Vector4?> colorOf, GaugeShape shape)
    {
        if (count <= 0)
        {
            return;
        }

        if (shape == GaugeShape.Petal)
        {
            Petals(ctx, pos, size, count, colorOf);
            return;
        }

        var dl = ctx.DrawList;
        var item = MathF.Max(8f, MathF.Min(size.Y, (size.X - 4f * (count - 1)) / count));
        var gap = MathF.Round(item * 0.4f);
        var total = count * item + (count - 1) * gap;
        var x = MathF.Floor(pos.X + (size.X - total) / 2f);
        var cy = pos.Y + size.Y / 2f;
        for (var i = 0; i < count; i++)
        {
            var centre = new Vector2(x + item / 2f, cy);
            var fill = colorOf(i);
            Points.Clear();
            if (shape == GaugeShape.Orb)
            {
                var r = item / 2f - 0.5f;
                for (var a = 0; a < 20; a++)
                {
                    var t = a / 20f * MathF.Tau;
                    Points.Add(centre + new Vector2(MathF.Cos(t), MathF.Sin(t)) * r);
                }
            }
            else
            {
                var r = item / 2f;
                Points.Add(centre + new Vector2(0, -r));
                Points.Add(centre + new Vector2(r, 0));
                Points.Add(centre + new Vector2(0, r));
                Points.Add(centre + new Vector2(-r, 0));
            }

            DrawShape(dl, ctx, Points, fill);
            x += item + gap;
        }
    }

    /// <summary>Petals in a fan (like the White Mage lilies), pivoting at the bottom centre of the row.</summary>
    private static void Petals(DrawContext ctx, Vector2 pos, Vector2 size, int count, Func<int, Vector4?> colorOf)
    {
        var dl = ctx.DrawList;
        var length = MathF.Max(8f, size.Y);
        var width = length * 0.42f;
        var pivot = new Vector2(MathF.Round(pos.X + size.X / 2f), pos.Y + size.Y);
        var spread = 0.62f;
        for (var i = 0; i < count; i++)
        {
            var angle = count == 1 ? 0f : (i - (count - 1) / 2f) * spread;
            Points.Clear();

            // A leaf: two curved sides from the pivot to the tip.
            const int steps = 8;
            for (var side = 0; side < 2; side++)
            {
                for (var s = 0; s <= steps; s++)
                {
                    var t = side == 0 ? s / (float)steps : 1f - s / (float)steps;
                    var bulge = MathF.Sin(t * MathF.PI) * width / 2f * (side == 0 ? 1f : -1f);
                    var local = new Vector2(bulge, -t * length);
                    Points.Add(pivot + Rotate(local, angle));
                }
            }

            DrawShape(dl, ctx, Points, colorOf(i));
        }
    }

    private static void DrawShape(ImDrawListPtr dl, DrawContext ctx, List<Vector2> points, Vector4? fill)
    {
        if (fill is { } color)
        {
            Glow(dl, ctx, points, color);
            FillConvex(dl, points, ctx.Col(color));
        }
        else
        {
            FillConvex(dl, points, ctx.Col(Track));
        }

        Stroke(dl, points, ctx.Col(Outline), 1f);
    }

    private static void Glow(ImDrawListPtr dl, DrawContext ctx, List<Vector2> points, Vector4 color)
    {
        if (points.Count < 3)
        {
            return;
        }

        Stroke(dl, points, ctx.Col(Colors.WithAlpha(color, color.W * 0.12f)), 6f);
        Stroke(dl, points, ctx.Col(Colors.WithAlpha(color, color.W * 0.22f)), 3f);
    }

    private static void FillConvex(ImDrawListPtr dl, List<Vector2> points, uint col)
    {
        if (points.Count < 3)
        {
            return;
        }

        dl.PathClear();
        foreach (var p in points)
        {
            dl.PathLineTo(p);
        }

        dl.PathFillConvex(col);
    }

    private static void Stroke(ImDrawListPtr dl, List<Vector2> points, uint col, float thickness)
    {
        if (points.Count < 2)
        {
            return;
        }

        dl.PathClear();
        foreach (var p in points)
        {
            dl.PathLineTo(p);
        }

        dl.PathStroke(col, ImDrawFlags.Closed, thickness);
    }

    /// <summary>The part of a convex polygon left of <paramref name="x"/>.</summary>
    private static void ClipLeftOf(List<Vector2> polygon, float x, List<Vector2> output)
    {
        output.Clear();
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var aIn = a.X <= x;
            var bIn = b.X <= x;
            if (aIn)
            {
                output.Add(a);
            }

            if (aIn != bIn)
            {
                var t = (x - a.X) / (b.X - a.X);
                output.Add(new Vector2(x, a.Y + (b.Y - a.Y) * t));
            }
        }
    }

    private static Vector2 Rotate(Vector2 v, float angle)
    {
        var (sin, cos) = MathF.SinCos(angle);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
