using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;

namespace LucentUI.Hud.Elements;

/// <summary>Global cooldown, as a bar or a ring (optionally following the mouse).</summary>
public sealed unsafe class GcdElement : HudElement
{
    // The GCD is recast group 58 in game terms (index 57).
    private const int GcdGroup = 57;

    public override string Id => "gcd";

    public override string Label => "GCD";

    public override Vector2 DefaultPosition => new(-130, 274);

    public override bool IsEnabled(Profile profile) => profile.Modules.Extras && profile.Gcd.Enabled;

    public override VisibilitySettings? GetVisibility(Profile profile) => profile.Gcd.Visibility;

    public override Vector2 GetSize(Profile profile) =>
        profile.Gcd.Circle ? new Vector2(profile.Gcd.CircleRadius * 2f + 4f) : profile.Gcd.Size;

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.Gcd;
        float elapsed = 0f, total = 0f;
        var manager = ActionManager.Instance();
        var detail = manager != null ? manager->GetRecastGroupDetail(GcdGroup) : null;
        if (detail != null && detail->IsActive)
        {
            elapsed = detail->Elapsed;
            total = detail->Total;
        }

        if (total <= 0f)
        {
            if (!ctx.Preview)
            {
                return;
            }

            elapsed = 1.4f;
            total = 2.5f;
        }

        var fraction = Math.Clamp(elapsed / total, 0f, 1f);
        if (s.AttachToMouse && !ctx.Preview)
        {
            var size = GetSize(ctx.Profile);
            pos = ImGui.GetMousePos() + s.MouseOffset - size / 2f;
        }

        if (s.Circle)
        {
            var centre = pos + new Vector2(s.CircleRadius + 2f);
            var dl = ctx.DrawList;
            dl.AddCircle(centre, s.CircleRadius, ctx.Col(ctx.Style.Background), 48, 5f);
            dl.PathArcTo(centre, s.CircleRadius, -MathF.PI / 2f, -MathF.PI / 2f + MathF.Tau * fraction, 48);
            dl.PathStroke(ctx.Col(s.Color), ImDrawFlags.None, 3f);
        }
        else
        {
            Gfx.FramedBar(ctx, pos, s.Size, fraction, s.Color);
        }
    }
}

/// <summary>A bar that shows MP regeneration ticks (every 3 seconds).</summary>
public sealed class MpTickerElement : HudElement
{
    private const double TickRate = 3.0;
    private uint lastMp;
    private double lastTick;

    public override string Id => "mptick";

    public override string Label => "MP Ticker";

    public override Vector2 DefaultPosition => new(-150, 324);

    public override bool IsEnabled(Profile profile) => profile.Modules.Extras && profile.MpTicker.Enabled;

    public override VisibilitySettings? GetVisibility(Profile profile) => profile.MpTicker.Visibility;

    public override Vector2 GetSize(Profile profile) => profile.MpTicker.Size;

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.MpTicker;
        var player = Svc.ObjectTable.LocalPlayer;
        if (player == null || player.MaxMp == 0)
        {
            return;
        }

        var now = ImGui.GetTime();
        var mp = player.CurrentMp;
        if (mp > lastMp && lastMp != 0)
        {
            // MP went up without us knowing why: assume a server tick.
            lastTick = now;
        }

        lastMp = mp;
        while (now - lastTick > TickRate)
        {
            lastTick += TickRate;
        }

        if (s.HideWhenInactive && mp >= player.MaxMp && !ctx.Preview)
        {
            return;
        }

        var fraction = (float)((now - lastTick) / TickRate);
        Gfx.FramedBar(ctx, pos, s.Size, fraction, s.Color);
    }
}

/// <summary>Limit break bars.</summary>
public sealed unsafe class LimitBreakElement : HudElement
{
    public override string Id => "limitbreak";

    public override string Label => "Limit Break";

    public override Vector2 DefaultPosition => new(-150, -380);

    public override bool IsEnabled(Profile profile) => profile.Modules.Extras && profile.LimitBreak.Enabled;

    public override VisibilitySettings? GetVisibility(Profile profile) => profile.LimitBreak.Visibility;

    public override Vector2 GetSize(Profile profile) => profile.LimitBreak.Size;

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.LimitBreak;
        var controller = LimitBreakController.Instance();
        int bars = controller != null ? controller->BarCount : 0;
        float units = controller != null ? controller->CurrentUnits : 0;
        float perBar = controller != null ? controller->BarUnits : 0;

        if (bars == 0 || perBar <= 0)
        {
            if (!ctx.Preview)
            {
                return;
            }

            bars = 3;
            perBar = 10000;
            units = 16000;
        }

        if (s.HideWhenInactive && units <= 0 && !ctx.Preview)
        {
            return;
        }

        const float gap = 3f;
        var width = (s.Size.X - gap * (bars - 1)) / bars;
        for (var i = 0; i < bars; i++)
        {
            var fill = Math.Clamp((units - i * perBar) / perBar, 0f, 1f);
            var color = fill >= 1f ? Colors.Lighten(s.Color, 0.15f) : s.Color;
            Gfx.FramedBar(ctx, pos + new Vector2(MathF.Round(i * (width + gap)), 0), new Vector2(MathF.Round(width), s.Size.Y), fill, color);
        }
    }
}

/// <summary>The countdown before a pull.</summary>
public sealed unsafe class PullTimerElement : HudElement
{
    private float startedAt;

    public override string Id => "pulltimer";

    public override string Label => "Pull Timer";

    public override Vector2 DefaultPosition => new(-130, -300);

    public override bool IsEnabled(Profile profile) => profile.Modules.Extras && profile.PullTimer.Enabled;

    public override VisibilitySettings? GetVisibility(Profile profile) => profile.PullTimer.Visibility;

    public override Vector2 GetSize(Profile profile) => profile.PullTimer.Size;

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.PullTimer;
        var agent = AgentCountDownSettingDialog.Instance();
        var remaining = agent != null && agent->Active ? agent->TimeRemaining : 0f;

        if (remaining <= 0f)
        {
            startedAt = 0f;
            if (!ctx.Preview)
            {
                return;
            }

            remaining = 7.4f;
        }

        startedAt = Math.Max(startedAt, remaining);
        var (min, max) = Gfx.FramedBar(ctx, pos, s.Size, remaining / Math.Max(1f, startedAt), s.Color);
        if (s.ShowText)
        {
            using var font = ctx.Fonts.Push(s.FontSize);
            Gfx.Text(ctx, remaining.ToString("0.0", CultureInfo.InvariantCulture), (min + max) / 2f, TextAlign.Center, Vector4.One);
        }
    }
}

/// <summary>Experience bar with rested experience.</summary>
public sealed unsafe class ExperienceElement : HudElement
{
    public override string Id => "experience";

    public override string Label => "Experience";

    public override Vector2 DefaultPosition => new(-250, 500);

    public override bool IsEnabled(Profile profile) => profile.Modules.Extras && profile.Experience.Enabled;

    public override VisibilitySettings? GetVisibility(Profile profile) => profile.Experience.Visibility;

    public override Vector2 GetSize(Profile profile) => profile.Experience.Size;

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.Experience;
        var hud = AgentHUD.Instance();
        if (hud == null)
        {
            return;
        }

        float current = hud->ExpCurrentExperience;
        float needed = hud->ExpNeededExperience;
        float rested = hud->ExpRestedExperience;
        int level = hud->ExpLevel;

        if (needed <= 0f)
        {
            // Max level: nothing to show unless asked to.
            if (s.HideWhenInactive && !ctx.Preview)
            {
                return;
            }

            current = needed = 1f;
        }

        var (min, max) = Gfx.Panel(ctx, pos, s.Size);
        var fraction = Math.Clamp(current / needed, 0f, 1f);
        if (rested > 0f)
        {
            var restedEnd = Math.Clamp((current + rested) / needed, 0f, 1f);
            var restedMin = new Vector2(min.X + (max.X - min.X) * fraction, min.Y);
            var restedMax = new Vector2(min.X + (max.X - min.X) * restedEnd, max.Y);
            ctx.DrawList.AddRectFilled(restedMin, restedMax, ctx.Col(Colors.WithAlpha(s.Color, 0.35f)));
        }

        Gfx.Bar(ctx, min, max, fraction, s.Color);

        if (s.ShowText && s.Size.Y >= 10)
        {
            using var font = s.FontSize > 0 ? ctx.Fonts.Push(s.FontSize) : ctx.Fonts.PushSmall();
            var text = $"Lv {level}  {TextTags.Number((uint)current, true)} / {TextTags.Number((uint)needed, true)}  ({fraction * 100f:0.0}%)";
            Gfx.Text(ctx, text, (min + max) / 2f, TextAlign.Center, Vector4.One);
        }
    }
}
