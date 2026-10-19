using System.Numerics;
using LucentUI.Config;
using LucentUI.Data;

namespace LucentUI.Hud.Elements;

public sealed class UnitFrameElement : HudElement
{
    private readonly UnitKind kind;

    public UnitFrameElement(UnitKind kind)
    {
        this.kind = kind;
    }

    public override string Id => kind switch
    {
        UnitKind.Player => "unit.player",
        UnitKind.Target => "unit.target",
        UnitKind.TargetOfTarget => "unit.tot",
        _ => "unit.focus",
    };

    public override string Label => kind switch
    {
        UnitKind.Player => "Player",
        UnitKind.Target => "Target",
        UnitKind.TargetOfTarget => "Target of Target",
        _ => "Focus",
    };

    public override Vector2 DefaultPosition => kind switch
    {
        UnitKind.Player => new(-430, 180),
        UnitKind.Target => new(170, 180),
        UnitKind.TargetOfTarget => new(440, 189),
        _ => new(-430, 110),
    };

    public UnitFrameSettings Settings(Profile p) => kind switch
    {
        UnitKind.Player => p.Player,
        UnitKind.Target => p.Target,
        UnitKind.TargetOfTarget => p.TargetOfTarget,
        _ => p.Focus,
    };

    public override bool IsEnabled(Profile profile) => profile.Modules.UnitFrames && Settings(profile).Enabled;

    public override Vector2 GetSize(Profile profile) => Settings(profile).Size;

    public override VisibilitySettings? GetVisibility(Profile profile) => Settings(profile).Visibility;

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = Settings(ctx.Profile);
        var obj = Units.Get(kind);
        if (!Units.TryRead(obj, out var unit))
        {
            if (!ctx.Preview)
            {
                return;
            }

            unit = Units.Preview(kind);
        }

        var hovered = !ctx.Preview && s.Interactive && ctx.Input.HandleUnit(pos, pos + s.Size, unit.Object, ctx.General);

        UnitBar.Render(ctx, pos, s.Size, unit, new UnitBarOptions
        {
            Texture = s.Texture,
            ShowPower = s.ShowPower,
            PowerHeight = s.PowerHeight,
            PowerPosition = s.PowerPosition,
            LeftText = s.LeftText,
            CenterText = s.CenterText,
            RightText = s.RightText,
            LeftStyle = s.LeftStyle,
            CenterStyle = s.CenterStyle,
            RightStyle = s.RightStyle,
            Background = s.CustomBackground ? s.Background : null,
            Portrait = s.JobIcon,
            ColorMode = s.ColorMode,
            ShowShield = s.ShowShield,
            Reverse = s.ReverseFill,
            Hovered = hovered,
            SmoothKey = unit.Object != null ? DrawContext.Key(Id, unit.Object.GameObjectId) : 0,
        });
    }
}
