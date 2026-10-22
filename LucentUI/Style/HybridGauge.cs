using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LucentUI.Gauges;
using LucentUI.Hud;

namespace LucentUI.Style;

/// <summary>
/// The game-shaped job gauge looks: angled bars split at spending points, diamonds, orbs and petals.
/// Hybrid draws them flat in LucentUI's style (thin black outlines, a soft glow); Classic gives them a bronze
/// trim, a metallic sheen, diamond end studs and gold-rimmed gems, closer to the game's own gauges.
/// </summary>
public static class HybridGauge
{
    private static readonly Vector4 HybridTrack = new(0.114f, 0.122f, 0.149f, 0.95f);
    private static readonly Vector4 ClassicTrack = new(0.06f, 0.05f, 0.045f, 0.95f);
    private static readonly Vector4 Black = new(0f, 0f, 0f, 1f);
    private static readonly Vector4 Gold = new(0.80f, 0.63f, 0.32f, 1f);
    private static readonly Vector4 GoldLight = new(0.96f, 0.84f, 0.55f, 1f);
    private static readonly List<Vector2> Points = new();
    private static readonly List<Vector2> Clipped = new();
    private static readonly List<Vector2> Sheen = new();

    /// <summary>An angled bar (pointed ends) filled to <paramref name="fraction"/>, split into equal segments.</summary>
    public static (Vector2 Min, Vector2 Max) Bar(DrawContext ctx, Vector2 pos, Vector2 size, float fraction, Vector4 color, int segments, bool classic = false)
    {
        var min = Gfx.Floor(pos);
        var max = min + Gfx.Floor(size);
        var height = max.Y - min.Y;
        var midY = min.Y + height / 2f;

        // Classic leaves room at both ends for its diamond studs.
        var stud = classic ? MathF.Max(3f, MathF.Round(height * 0.3f)) : 0f;
        if (classic)
        {
            min.X += stud + 2f;
            max.X -= stud + 2f;
        }

        var notch = MathF.Min(MathF.Round(height / 2f), (max.X - min.X) / 4f);
        Points.Clear();
        Points.Add(new Vector2(min.X, midY));
        Points.Add(new Vector2(min.X + notch, min.Y));
        Points.Add(new Vector2(max.X - notch, min.Y));
        Points.Add(new Vector2(max.X, midY));
        Points.Add(new Vector2(max.X - notch, max.Y));
        Points.Add(new Vector2(min.X + notch, max.Y));

        var dl = ctx.DrawList;
        FillConvex(dl, Points, ctx.Col(classic ? ClassicTrack : HybridTrack));

        fraction = Math.Clamp(fraction, 0f, 1f);
        if (fraction > 0f)
        {
            var fillX = min.X + (max.X - min.X) * fraction;
            ClipLeftOf(Points, fillX, Clipped);
            if (!classic)
            {
                Glow(dl, ctx, Clipped, color);
            }

            FillConvex(dl, Clipped, ctx.Col(color));
            if (classic)
            {
                // Metallic sheen: a lighter upper half and a darker lower edge.
                ClipAbove(Clipped, midY, Sheen);
                FillConvex(dl, Sheen, ctx.Col(Colors.WithAlpha(Colors.Lighten(color, 0.45f), 0.35f)));
                var shadowY = max.Y - MathF.Max(1f, MathF.Round(height * 0.18f));
                var right = Math.Min(fillX, max.X - notch);
                if (right > min.X + notch)
                {
                    dl.AddRectFilled(new Vector2(min.X + notch, shadowY), new Vector2(right, max.Y), ctx.Col(Colors.WithAlpha(Colors.Darken(color, 0.4f), 0.5f)));
                }
            }
            else
            {
                // A lighter top edge gives the fill a little depth.
                var highlight = ctx.Col(Colors.WithAlpha(Colors.Lighten(color, 0.35f), 0.55f));
                var topRight = Math.Min(fillX, max.X - notch);
                if (topRight > min.X + notch)
                {
                    dl.AddLine(new Vector2(min.X + notch, min.Y + 1.5f), new Vector2(topRight, min.Y + 1.5f), highlight, 1f);
                }
            }

            // The fill's leading edge.
            if (classic && fraction < 1f)
            {
                dl.AddLine(new Vector2(MathF.Round(fillX), min.Y + 1f), new Vector2(MathF.Round(fillX), max.Y - 1f), ctx.Col(GoldLight), 1.5f);
            }
        }

        // Spending points: slanted black dividers, or gold ticks with small studs on the trim.
        if (segments > 1)
        {
            for (var i = 1; i < segments; i++)
            {
                var x = MathF.Round(min.X + (max.X - min.X) * i / segments);
                if (classic)
                {
                    dl.AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y), ctx.Col(Colors.WithAlpha(Gold, 0.9f)), 1.5f);
                    Diamond(dl, ctx, new Vector2(x, min.Y), MathF.Max(2f, height * 0.16f), Gold);
                    Diamond(dl, ctx, new Vector2(x, max.Y), MathF.Max(2f, height * 0.16f), Gold);
                }
                else
                {
                    var lean = MathF.Max(1.5f, height * 0.18f);
                    dl.AddLine(new Vector2(x + lean, min.Y), new Vector2(x - lean, max.Y), ctx.Col(Black), 2f);
                }
            }
        }

        if (classic)
        {
            // Bronze trim: a dark outer line with a gold line inside it, and diamond studs at the ends.
            Stroke(dl, Points, ctx.Col(Black), 3f);
            Stroke(dl, Points, ctx.Col(Gold), 1.2f);
            Diamond(dl, ctx, new Vector2(min.X - 2f - stud / 2f, midY), stud / 2f + 0.5f, Gold);
            Diamond(dl, ctx, new Vector2(max.X + 2f + stud / 2f, midY), stud / 2f + 0.5f, Gold);
        }
        else
        {
            Stroke(dl, Points, ctx.Col(Black), 1f);
        }

        return (new Vector2(min.X + notch, min.Y), new Vector2(max.X - notch, max.Y));
    }

    /// <summary>A row of stacks or pips as shapes, centred in the row. Empty ones are dark with an outline.</summary>
    public static void Shapes(DrawContext ctx, Vector2 pos, Vector2 size, int count, Func<int, Vector4?> colorOf, GaugeShape shape, bool classic = false)
    {
        if (count <= 0)
        {
            return;
        }

        if (shape == GaugeShape.Petal)
        {
            Petals(ctx, pos, size, count, colorOf, classic);
            return;
        }

        var dl = ctx.DrawList;
        var item = MathF.Max(8f, MathF.Min(size.Y, (size.X - 4f * (count - 1)) / count));
        var gap = MathF.Round(item * (classic ? 0.5f : 0.4f));
        var total = count * item + (count - 1) * gap;
        var x = MathF.Floor(pos.X + (size.X - total) / 2f);
        var cy = pos.Y + size.Y / 2f;
        for (var i = 0; i < count; i++)
        {
            var centre = new Vector2(x + item / 2f, cy);
            var fill = colorOf(i);
            Points.Clear();
            var r = item / 2f - (classic ? 1f : 0.5f);
            if (shape == GaugeShape.Orb)
            {
                for (var a = 0; a < 20; a++)
                {
                    var t = a / 20f * MathF.Tau;
                    Points.Add(centre + new Vector2(MathF.Cos(t), MathF.Sin(t)) * r);
                }
            }
            else
            {
                Points.Add(centre + new Vector2(0, -r));
                Points.Add(centre + new Vector2(r, 0));
                Points.Add(centre + new Vector2(0, r));
                Points.Add(centre + new Vector2(-r, 0));
            }

            DrawShape(dl, ctx, Points, fill, classic);
            if (classic && fill is { } lit)
            {
                // A small shine on gems and orbs.
                var shine = centre + new Vector2(-r * 0.3f, -r * 0.35f);
                dl.AddCircleFilled(shine, MathF.Max(1f, r * 0.28f), ctx.Col(Colors.WithAlpha(Colors.Lighten(lit, 0.7f), 0.75f)));
            }

            x += item + gap;
        }
    }

    /// <summary>Petals in a fan (like the White Mage lilies), pivoting at the bottom centre of the row.</summary>
    private static void Petals(DrawContext ctx, Vector2 pos, Vector2 size, int count, Func<int, Vector4?> colorOf, bool classic)
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

            DrawShape(dl, ctx, Points, colorOf(i), classic);
        }

        if (classic)
        {
            Diamond(dl, ctx, pivot, MathF.Max(2f, length * 0.12f), Gold);
        }
    }

    private static void DrawShape(ImDrawListPtr dl, DrawContext ctx, List<Vector2> points, Vector4? fill, bool classic)
    {
        if (fill is { } color)
        {
            if (!classic)
            {
                Glow(dl, ctx, points, color);
            }

            FillConvex(dl, points, ctx.Col(color));
        }
        else
        {
            FillConvex(dl, points, ctx.Col(classic ? ClassicTrack : HybridTrack));
        }

        if (classic)
        {
            Stroke(dl, points, ctx.Col(Black), 2.5f);
            Stroke(dl, points, ctx.Col(Gold), 1.2f);
        }
        else
        {
            Stroke(dl, points, ctx.Col(Black), 1f);
        }
    }

    private static void Diamond(ImDrawListPtr dl, DrawContext ctx, Vector2 centre, float radius, Vector4 color)
    {
        dl.AddQuadFilled(
            centre + new Vector2(0, -radius),
            centre + new Vector2(radius, 0),
            centre + new Vector2(0, radius),
            centre + new Vector2(-radius, 0),
            ctx.Col(color));
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
    private static void ClipLeftOf(List<Vector2> polygon, float x, List<Vector2> output) =>
        Clip(polygon, output, p => p.X <= x, (a, b) => new Vector2(x, a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X)));

    /// <summary>The part of a convex polygon above <paramref name="y"/>.</summary>
    private static void ClipAbove(List<Vector2> polygon, float y, List<Vector2> output) =>
        Clip(polygon, output, p => p.Y <= y, (a, b) => new Vector2(a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y), y));

    private static void Clip(List<Vector2> polygon, List<Vector2> output, Func<Vector2, bool> inside, Func<Vector2, Vector2, Vector2> cross)
    {
        output.Clear();
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var aIn = inside(a);
            if (aIn)
            {
                output.Add(a);
            }

            if (aIn != inside(b))
            {
                output.Add(cross(a, b));
            }
        }
    }

    private static Vector2 Rotate(Vector2 v, float angle)
    {
        var (sin, cos) = MathF.SinCos(angle);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
