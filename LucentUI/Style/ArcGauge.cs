using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LucentUI.Config;
using LucentUI.Gauges;
using LucentUI.Hud;

namespace LucentUI.Style;

/// <summary>
/// The "arcs &amp; rings" job gauge look: one round dial. Each gauge part is a 270° ring (open at the bottom),
/// outermost first; bars fill along the ring and stacks split it into segments. The first part's value sits in
/// the middle.
/// </summary>
public static class ArcGauge
{
    private const float Start = MathF.PI * 0.75f;
    private const float Span = MathF.PI * 1.5f;
    private static readonly Vector4 Track = new(0.114f, 0.122f, 0.149f, 0.95f);
    private static readonly Vector4 Black = new(0f, 0f, 0f, 1f);

    public static void Dial(DrawContext ctx, JobGaugeSettings s, Vector2 pos, float diameter, IReadOnlyList<(GaugePart Part, GaugeValue Value, Vector4 Color)> parts)
    {
        var dl = ctx.DrawList;
        var centre = Gfx.Floor(pos + new Vector2(diameter / 2f));
        var outer = diameter / 2f - 2f;
        var count = Math.Max(1, parts.Count);

        // Rings share the outer 70% of the radius; the middle keeps room for the number.
        var thickness = Math.Clamp(diameter * 0.075f, 3f, 14f);
        var gap = MathF.Max(2f, thickness * 0.35f);
        thickness = MathF.Max(2f, MathF.Min(thickness, (outer * 0.7f - gap * (count - 1)) / count));

        var radius = outer - thickness / 2f;
        string? centreText = null;
        foreach (var (part, value, color) in parts)
        {
            switch (part.Kind)
            {
                case GaugeKind.Stacks:
                {
                    var max = Math.Max(1, (int)value.Max);
                    var filled = (int)value.Value;
                    Segments(dl, ctx, centre, radius, thickness, max, i => i < filled ? color : null);
                    break;
                }

                case GaugeKind.Pips:
                {
                    var pips = Math.Max(1, (int)value.Max);
                    Segments(dl, ctx, centre, radius, thickness, pips, value.PipColor ?? (_ => null));
                    break;
                }

                default:
                {
                    Arc(dl, ctx, centre, radius, thickness, Start, Start + Span, null);
                    var fraction = value.Hidden ? 0f : value.Fraction;
                    if (fraction > 0f)
                    {
                        Arc(dl, ctx, centre, radius, thickness, Start, Start + Span * fraction, color);
                    }

                    centreText ??= value.Hidden ? null : value.Text;
                    break;
                }
            }

            radius -= thickness + gap;
        }

        if (s.ShowText && !string.IsNullOrEmpty(centreText))
        {
            var size = s.TextFontSize > 0 ? s.TextFontSize : (int)Math.Clamp(MathF.Round(diameter * 0.2f), 10f, 48f);
            using var font = ctx.Fonts.Push(size);
            Gfx.Text(ctx, centreText, centre, TextAlign.Center, Vector4.One);
        }
    }

    /// <summary>One ring split into <paramref name="count"/> segments, filled per segment.</summary>
    private static void Segments(ImDrawListPtr dl, DrawContext ctx, Vector2 centre, float radius, float thickness, int count, Func<int, Vector4?> colorOf)
    {
        var gapAngle = MathF.Min(0.12f, Span / count * 0.18f);
        var each = (Span - gapAngle * (count - 1)) / count;
        for (var i = 0; i < count; i++)
        {
            var a0 = Start + i * (each + gapAngle);
            Arc(dl, ctx, centre, radius, thickness, a0, a0 + each, colorOf(i));
        }
    }

    /// <summary>A thick arc with a black edge; null colour draws the empty track.</summary>
    private static void Arc(ImDrawListPtr dl, DrawContext ctx, Vector2 centre, float radius, float thickness, float a0, float a1, Vector4? color)
    {
        if (a1 - a0 < 0.001f)
        {
            return;
        }

        var segments = Math.Max(4, (int)(radius * (a1 - a0) / 3f));
        if (color is { } fill)
        {
            // Soft glow, then the fill.
            Stroke(dl, centre, radius, a0, a1, segments, ctx.Col(Colors.WithAlpha(fill, fill.W * 0.18f)), thickness + 5f);
            Stroke(dl, centre, radius, a0, a1, segments, ctx.Col(Black), thickness + 2f);
            Stroke(dl, centre, radius, a0, a1, segments, ctx.Col(fill), thickness);
        }
        else
        {
            Stroke(dl, centre, radius, a0, a1, segments, ctx.Col(Black), thickness + 2f);
            Stroke(dl, centre, radius, a0, a1, segments, ctx.Col(Track), thickness);
        }
    }

    private static void Stroke(ImDrawListPtr dl, Vector2 centre, float radius, float a0, float a1, int segments, uint col, float thickness)
    {
        dl.PathClear();
        dl.PathArcTo(centre, radius, a0, a1, segments);
        dl.PathStroke(col, ImDrawFlags.None, thickness);
    }
}
