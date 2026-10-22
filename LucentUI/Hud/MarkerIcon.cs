using System;
using System.Numerics;
using Dalamud.Interface.Textures;
using LucentUI.Config;
using LucentUI.Style;

namespace LucentUI.Hud;

/// <summary>Draws a head marker icon at a point of a frame (or nameplate).</summary>
public static class MarkerIcon
{
    private const float OutsideGap = 2f;

    public static void Draw(DrawContext ctx, MarkerSettings s, uint icon, Vector2 framePos, Vector2 frameSize)
    {
        if (icon == 0 || !s.Enabled)
        {
            return;
        }

        var size = Math.Clamp(s.Size, 6f, 96f);
        var (column, row) = s.Anchor switch
        {
            IconAnchor.TopLeft => (0, 0),
            IconAnchor.Top => (1, 0),
            IconAnchor.TopRight => (2, 0),
            IconAnchor.Left => (0, 1),
            IconAnchor.Center => (1, 1),
            IconAnchor.Right => (2, 1),
            IconAnchor.BottomLeft => (0, 2),
            IconAnchor.Bottom => (1, 2),
            _ => (2, 2),
        };

        var centre = framePos + new Vector2(frameSize.X * column / 2f, frameSize.Y * row / 2f);
        if (s.Outside)
        {
            var push = size / 2f + OutsideGap;
            centre += new Vector2((column - 1) * push, (row - 1) * push);
        }

        var min = Gfx.Floor(centre - new Vector2(size / 2f) + s.Offset);
        var texture = Svc.TextureProvider.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
        ctx.DrawList.AddImage(texture.Handle, min, min + new Vector2(size), Vector2.Zero, Vector2.One, ctx.Col(Vector4.One));
    }
}
