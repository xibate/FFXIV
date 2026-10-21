using System.Numerics;
using Dalamud.Interface.Textures.TextureWraps;
using LucentUI.Config;
using LucentUI.Hud;

namespace LucentUI.Style;

/// <summary>Drawing status icons as the game's 3:4 portrait, a square or a circle.</summary>
public static class IconShapes
{
    /// <summary>The shape to draw: an element's own, or the Style page's when it says "Style Default".</summary>
    public static IconShape Resolve(IconShape shape, StyleSettings style)
    {
        var resolved = shape == IconShape.StyleDefault ? style.IconShape : shape;
        return resolved == IconShape.StyleDefault ? IconShape.Portrait : resolved;
    }

    /// <summary>Width / height of an icon in this shape.</summary>
    public static float Aspect(IconShape resolved) => resolved == IconShape.Portrait ? 0.75f : 1f;

    /// <summary>Icon size for a given height.</summary>
    public static Vector2 Size(IconShape resolved, float height) =>
        new(System.MathF.Round(height * Aspect(resolved)), System.MathF.Round(height));

    /// <summary>Draws a status icon texture; square and round crop the middle of the 3:4 icon.</summary>
    public static void Draw(DrawContext ctx, IDalamudTextureWrap texture, Vector2 min, Vector2 max, IconShape resolved, Vector4 tint)
    {
        var uv0 = resolved == IconShape.Portrait ? Vector2.Zero : new Vector2(0f, 0.125f);
        var uv1 = resolved == IconShape.Portrait ? Vector2.One : new Vector2(1f, 0.875f);
        if (resolved == IconShape.Round)
        {
            ctx.DrawList.AddImageRounded(texture.Handle, min, max, uv0, uv1, ctx.Col(tint), (max.X - min.X) / 2f);
        }
        else
        {
            ctx.DrawList.AddImage(texture.Handle, min, max, uv0, uv1, ctx.Col(tint));
        }
    }

    /// <summary>An outline matching the icon's shape.</summary>
    public static void Outline(DrawContext ctx, Vector2 min, Vector2 max, IconShape resolved, Vector4 color)
    {
        if (resolved == IconShape.Round)
        {
            ctx.DrawList.AddCircle((min + max) / 2f, (max.X - min.X) / 2f + 0.5f, ctx.Col(color), 24, 1.5f);
        }
        else
        {
            ctx.DrawList.AddRect(min - Vector2.One, max + Vector2.One, ctx.Col(color), 0f, default, 1f);
        }
    }
}
