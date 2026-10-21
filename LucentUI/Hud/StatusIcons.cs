using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;

namespace LucentUI.Hud;

/// <summary>
/// Buff and debuff icons on a member frame (party, alliance): which statuses, and where and how they are drawn
/// (anchor, inside or outside, growth, offset, shape). Shared so every frame offers the same settings.
/// </summary>
public sealed class StatusIcons
{
    private const float Padding = 2f;
    private const float OutsideGap = 3f;

    private readonly AuraSettings filter = new() { ShowPermanent = false, MaxIcons = 30 };
    private readonly List<AuraData> auras = new();

    /// <summary>The statuses that would be drawn last time <see cref="Draw"/> ran.</summary>
    public IReadOnlyList<AuraData> Current => auras;

    /// <summary>Collects and draws the buffs (or debuffs) of <paramref name="obj"/> around a frame.</summary>
    public void Draw(DrawContext ctx, StatusIconSettings s, IGameObject? obj, bool debuffs, Vector2 framePos, Vector2 frameSize)
    {
        auras.Clear();
        if (!s.Enabled)
        {
            return;
        }

        // Preview members have no game object: show sample icons so the layout can be seen.
        if (obj == null)
        {
            if (ctx.Preview)
            {
                foreach (var sample in Auras.Preview())
                {
                    if (sample.IsDebuff == debuffs)
                    {
                        auras.Add(sample);
                    }
                }

                DrawIcons(ctx, s, auras, framePos, frameSize);
            }

            return;
        }

        filter.ShowBuffs = !debuffs;
        filter.ShowDebuffs = debuffs;
        filter.OnlyMine = s.OnlyMine;
        filter.Filter = s.Filter;
        filter.FilterIds = s.Ids;
        Auras.Collect(obj, filter, auras);
        DrawIcons(ctx, s, auras, framePos, frameSize);
    }

    /// <summary>Draws icons for <paramref name="list"/> (also used for previews).</summary>
    public static void DrawIcons(DrawContext ctx, StatusIconSettings s, IReadOnlyList<AuraData> list, Vector2 framePos, Vector2 frameSize)
    {
        var count = Math.Min(list.Count, Math.Max(0, s.Max));
        if (count == 0)
        {
            return;
        }

        var size = IconSize(s);
        var grow = Grow(s);
        var step = grow switch
        {
            IconGrow.Left => new Vector2(-(size.X + s.Spacing), 0),
            IconGrow.Up => new Vector2(0, -(size.Y + s.Spacing)),
            IconGrow.Down => new Vector2(0, size.Y + s.Spacing),
            _ => new Vector2(size.X + s.Spacing, 0),
        };

        var first = FirstIcon(s, size, grow, count, framePos, frameSize) + s.Offset;
        using var font = s.TimerFontSize > 0 ? ctx.Fonts.Push(s.TimerFontSize) : ctx.Fonts.PushSmall();
        for (var i = 0; i < count; i++)
        {
            var min = Gfx.Floor(first + step * i);
            DrawIcon(ctx, s, list[i], min, min + size);
        }
    }

    public static Vector2 IconSize(StatusIconSettings s)
    {
        var height = MathF.Round(Math.Clamp(s.Size, 6f, 96f));
        return s.Shape == IconShape.Portrait ? new Vector2(MathF.Round(height * 0.75f), height) : new Vector2(height, height);
    }

    private static int Column(IconAnchor a) => a switch
    {
        IconAnchor.TopLeft or IconAnchor.Left or IconAnchor.BottomLeft => 0,
        IconAnchor.Top or IconAnchor.Center or IconAnchor.Bottom => 1,
        _ => 2,
    };

    private static int Row(IconAnchor a) => a switch
    {
        IconAnchor.TopLeft or IconAnchor.Top or IconAnchor.TopRight => 0,
        IconAnchor.Left or IconAnchor.Center or IconAnchor.Right => 1,
        _ => 2,
    };

    /// <summary>Auto growth: away from the anchor's side (outside icons keep going outwards).</summary>
    private static IconGrow Grow(StatusIconSettings s)
    {
        if (s.Grow != IconGrow.Auto)
        {
            return s.Grow;
        }

        return Column(s.Anchor) switch
        {
            0 => s.Outside ? IconGrow.Left : IconGrow.Right,
            2 => s.Outside ? IconGrow.Right : IconGrow.Left,
            _ => IconGrow.Right,
        };
    }

    private static Vector2 FirstIcon(StatusIconSettings s, Vector2 size, IconGrow grow, int count, Vector2 pos, Vector2 frame)
    {
        var column = Column(s.Anchor);
        var row = Row(s.Anchor);
        var horizontal = grow is IconGrow.Left or IconGrow.Right;
        var blockWidth = horizontal ? count * size.X + (count - 1) * s.Spacing : size.X;
        var blockHeight = horizontal ? size.Y : count * size.Y + (count - 1) * s.Spacing;

        // Outside: corners and sides go beside the frame; top and bottom centre go above or below it.
        var besides = s.Outside && column != 1;
        var aboveBelow = s.Outside && column == 1 && row != 1;

        float x;
        if (besides)
        {
            x = column == 0 ? pos.X - OutsideGap - size.X : pos.X + frame.X + OutsideGap;
        }
        else
        {
            x = column switch
            {
                0 => pos.X + Padding,
                2 => pos.X + frame.X - Padding - size.X,
                _ => pos.X + (frame.X - blockWidth) / 2f,
            };

            // A centred row starts at its left end; make sure it grows rightwards from there.
            if (column == 1 && grow == IconGrow.Left)
            {
                x += blockWidth - size.X;
            }
        }

        float y;
        if (aboveBelow)
        {
            y = row == 0 ? pos.Y - OutsideGap - size.Y : pos.Y + frame.Y + OutsideGap;
        }
        else
        {
            y = row switch
            {
                0 => pos.Y + (besides ? 0 : Padding),
                2 => pos.Y + frame.Y - (besides ? 0 : Padding) - size.Y,
                _ => pos.Y + (frame.Y - (horizontal ? size.Y : blockHeight)) / 2f,
            };

            if (row == 1 && grow == IconGrow.Up)
            {
                y += blockHeight - size.Y;
            }
        }

        return new Vector2(x, y);
    }

    private static void DrawIcon(DrawContext ctx, StatusIconSettings s, in AuraData aura, Vector2 min, Vector2 max)
    {
        var dl = ctx.DrawList;
        var texture = Svc.TextureProvider.GetFromGameIcon(new GameIconLookup(aura.Icon)).GetWrapOrEmpty();
        var white = ctx.Col(Vector4.One);

        // Square and round icons crop the middle of the 3:4 status icon.
        var uv0 = s.Shape == IconShape.Portrait ? Vector2.Zero : new Vector2(0f, 0.125f);
        var uv1 = s.Shape == IconShape.Portrait ? Vector2.One : new Vector2(1f, 0.875f);

        Vector4? outline = s.Border switch
        {
            AuraBorder.Thin => ctx.Style.Border,
            AuraBorder.ByType => aura.CanDispel ? Colors.Rgb(0xB65CE6) : aura.IsDebuff ? Colors.Rgb(0xC8423C) : ctx.Style.Border,
            _ => null,
        };

        if (s.Shape == IconShape.Round)
        {
            var radius = (max.X - min.X) / 2f;
            dl.AddImageRounded(texture.Handle, min, max, uv0, uv1, white, radius);
            if (outline is { } ring)
            {
                dl.AddCircle((min + max) / 2f, radius + 0.5f, ctx.Col(ring), 24, 1.5f);
            }
        }
        else
        {
            dl.AddImage(texture.Handle, min, max, uv0, uv1, white);
            if (outline is { } line)
            {
                dl.AddRect(min - Vector2.One, max + Vector2.One, ctx.Col(line), 0f, default, 1f);
            }
        }

        if (s.Timers && !aura.IsPermanent && aura.Remaining > 0f)
        {
            var color = aura.Remaining < 5f ? Colors.Rgb(0xFF6B5E) : Vector4.One;
            Gfx.Text(ctx, TextTags.Duration(aura.Remaining), new Vector2((min.X + max.X) / 2f, max.Y - (max.Y - min.Y) * 0.25f), TextAlign.Center, color);
        }
    }
}
