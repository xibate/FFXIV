using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Interface.Textures;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;

namespace LucentUI.Hud.Elements;

public sealed class CastBarElement : HudElement
{
    private readonly UnitKind kind;

    public CastBarElement(UnitKind kind)
    {
        this.kind = kind;
    }

    public override string Id => kind switch
    {
        UnitKind.Player => "cast.player",
        UnitKind.Target => "cast.target",
        _ => "cast.focus",
    };

    public override string Label => kind switch
    {
        UnitKind.Player => "Player Cast Bar",
        UnitKind.Target => "Target Cast Bar",
        _ => "Focus Cast Bar",
    };

    public override Vector2 DefaultPosition => kind switch
    {
        UnitKind.Player => new(-150, 300),
        UnitKind.Target => new(170, 232),
        _ => new(-430, 146),
    };

    public CastBarSettings Settings(Profile p) => kind switch
    {
        UnitKind.Player => p.PlayerCast,
        UnitKind.Target => p.TargetCast,
        _ => p.FocusCast,
    };

    public override bool IsEnabled(Profile profile) => profile.Modules.CastBars && Settings(profile).Enabled;

    public override Vector2 GetSize(Profile profile) => Settings(profile).Size;

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = Settings(ctx.Profile);
        using var barTexture = ctx.UseTexture(s.Texture);
        if (!Casts.TryGet(Units.Get(kind), out var cast))
        {
            if (!ctx.Preview)
            {
                return;
            }

            cast = Casts.Preview(kind != UnitKind.Player);
        }

        var style = ctx.Style;
        var savedAlpha = ctx.Alpha;
        ctx.Alpha *= Math.Clamp(s.Opacity, 0f, 1f);

        var (min, max) = Gfx.Panel(ctx, pos, s.Size, s.CustomBackground ? s.Background : null);
        var b = Math.Max(0, style.BorderSize);

        // Spell icon as a square the height of the bar, on the chosen side.
        if (s.ShowIcon && cast.Icon != 0)
        {
            var iconSize = max.Y - min.Y;
            var iconMin = s.IconOnRight ? new Vector2(max.X - iconSize, min.Y) : min;
            var texture = Svc.TextureProvider.GetFromGameIcon(new GameIconLookup(cast.Icon)).GetWrapOrEmpty();
            ctx.DrawList.AddImage(texture.Handle, iconMin, iconMin + new Vector2(iconSize), new Vector2(0.08f), new Vector2(0.92f), ctx.Col(Vector4.One));
            if (s.IconOnRight)
            {
                max.X -= iconSize + b;
                if (b > 0)
                {
                    ctx.DrawList.AddRectFilled(new Vector2(max.X, min.Y), new Vector2(max.X + b, max.Y), ctx.Col(style.Border));
                }
            }
            else
            {
                min.X += iconSize + b;
                if (b > 0)
                {
                    ctx.DrawList.AddRectFilled(new Vector2(min.X - b, min.Y), new Vector2(min.X, max.Y), ctx.Col(style.Border));
                }
            }
        }

        var color = cast.Interruptible ? style.CastInterruptible : s.CustomColor ? s.Color : style.Cast;
        Gfx.Bar(ctx, min, max, cast.Fraction, color);

        if (kind == UnitKind.Player && s.ShowSlidecast && s.SlidecastWindow > 0f && cast.Total > s.SlidecastWindow)
        {
            var width = max.X - min.X;
            var start = max.X - width * (s.SlidecastWindow / cast.Total);
            ctx.DrawList.AddRectFilled(new Vector2(start, min.Y), max, ctx.Col(style.Slidecast));
        }

        var name = s.ShowName ? cast.Name : string.Empty;
        var timer = s.ShowTimer ? Math.Max(0f, cast.Remaining).ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;
        Gfx.TextRow(ctx, min, max, (name, s.NameStyle), (string.Empty, null), (timer, s.TimerStyle), color, 4f);
        ctx.Alpha = savedAlpha;
    }
}
