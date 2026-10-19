using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LucentUI.Hud;

/// <summary>
/// Screen rectangles of the game's open windows (inventory, character, etc.), so HUD elements can be cut away or
/// hidden where a game window sits on top of them, and clicks there go to the game window.
/// </summary>
public sealed unsafe class ClipRects
{
    private readonly List<(Vector2 Min, Vector2 Max)> rects = new();

    public bool Enabled { get; private set; }

    public IReadOnlyList<(Vector2 Min, Vector2 Max)> All => rects;

    public void Update(bool enabled, Vector2 viewportPos)
    {
        rects.Clear();
        Enabled = enabled;
        if (!enabled)
        {
            return;
        }

        var stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null)
        {
            return;
        }

        ref var list = ref stage->RaptureAtkUnitManager->AtkUnitManager.AllLoadedUnitsList;
        var count = Math.Min((int)list.Count, list.Entries.Length);
        for (var i = 0; i < count; i++)
        {
            var addon = list.Entries[i].Value;
            if (addon == null || !addon->IsVisible || addon->RootNode == null || addon->WindowNode == null || addon->Scale <= 0f)
            {
                continue;
            }

            var root = addon->RootNode;
            var min = viewportPos + new Vector2(root->X, root->Y);
            var max = min + new Vector2(root->Width, root->Height) * addon->Scale;

            // Windows keep a transparent margin around them; trim it a little.
            var margin = 4f * addon->Scale;
            min += new Vector2(margin);
            max -= new Vector2(margin, margin * 2f);
            if (max.X > min.X && max.Y > min.Y && min.X > -5000f)
            {
                rects.Add((min, max));
            }
        }
    }

    public bool Contains(Vector2 point)
    {
        foreach (var (min, max) in rects)
        {
            if (point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y)
            {
                return true;
            }
        }

        return false;
    }

    public bool Covers(Vector2 min, Vector2 max) => FirstOverlap(min, max) != null;

    public (Vector2 Min, Vector2 Max)? FirstOverlap(Vector2 min, Vector2 max)
    {
        foreach (var rect in rects)
        {
            if (rect.Min.X < max.X && rect.Max.X > min.X && rect.Min.Y < max.Y && rect.Max.Y > min.Y)
            {
                return rect;
            }
        }

        return null;
    }

    /// <summary>The parts of <paramref name="area"/> not covered by <paramref name="hole"/> (up to four strips).</summary>
    public static IEnumerable<(Vector2 Min, Vector2 Max)> Subtract((Vector2 Min, Vector2 Max) area, (Vector2 Min, Vector2 Max) hole)
    {
        var (aMin, aMax) = area;
        var hMin = Vector2.Max(hole.Min, aMin);
        var hMax = Vector2.Min(hole.Max, aMax);

        if (hMin.Y > aMin.Y)
        {
            yield return (aMin, new Vector2(aMax.X, hMin.Y));
        }

        if (hMax.Y < aMax.Y)
        {
            yield return (new Vector2(aMin.X, hMax.Y), aMax);
        }

        if (hMin.X > aMin.X)
        {
            yield return (new Vector2(aMin.X, hMin.Y), new Vector2(hMin.X, hMax.Y));
        }

        if (hMax.X < aMax.X)
        {
            yield return (new Vector2(hMax.X, hMin.Y), new Vector2(aMax.X, hMax.Y));
        }
    }
}
