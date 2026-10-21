using System;
using System.Collections.Generic;
using System.Numerics;
using LucentUI.Config;
using LucentUI.Gauges;
using LucentUI.Style;

namespace LucentUI.Hud.Elements;

/// <summary>The current job's gauge parts stacked as bars.</summary>
public sealed class JobGaugeElement : HudElement
{
    private readonly GaugeContext context = new();
    private readonly List<(GaugePart Part, GaugeValue Value, Vector4 Color)> visible = new();

    public override string Id => "jobgauge";

    public override string Label => "Job Gauge";

    public override Vector2 DefaultPosition => new(-130, 330);

    public override bool IsEnabled(Profile profile) =>
        profile.Modules.JobGauges && profile.JobGauge.Enabled && CurrentParts() != null;

    public override VisibilitySettings? GetVisibility(Profile profile) => profile.JobGauge.Visibility;

    public override Vector2 GetSize(Profile profile)
    {
        var s = profile.JobGauge;
        var count = 0;
        if (CurrentParts() is { } parts)
        {
            var level = Svc.ObjectTable.LocalPlayer?.Level ?? 100;
            foreach (var part in parts)
            {
                var settings = PartSettings(profile, part);
                if ((settings?.Enabled ?? part.DefaultEnabled) && settings?.Detached != true && level >= part.MinLevel)
                {
                    count++;
                }
            }
        }

        count = Math.Max(1, count);
        return new Vector2(s.Width, count * s.BarHeight + (count - 1) * s.Spacing);
    }

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        using var barTexture = ctx.UseTexture(ctx.Profile.JobGauge.Texture);
        var player = Svc.ObjectTable.LocalPlayer;
        var parts = CurrentParts();
        if (player == null || parts == null)
        {
            return;
        }

        var s = ctx.Profile.JobGauge;
        context.Player = player;
        context.Target = Svc.TargetManager.Target;
        context.Level = player.Level;

        visible.Clear();
        foreach (var part in parts)
        {
            var settings = PartSettings(ctx.Profile, part);
            if (!(settings?.Enabled ?? part.DefaultEnabled) || settings?.Detached == true || player.Level < part.MinLevel)
            {
                continue;
            }

            GaugeValue value;
            try
            {
                value = part.Read(context);
            }
            catch (Exception)
            {
                // A gauge type that does not match the current job (e.g. right after a job change).
                continue;
            }

            if (value.Hidden)
            {
                if (ctx.Preview)
                {
                    value.Hidden = false;
                    value.Value = value.Max * 0.6f;
                }
                else if (s.HideEmptyTimers)
                {
                    continue;
                }
            }

            var color = settings is { CustomColor: true } ? settings.Color : value.Color ?? part.DefaultColor;
            visible.Add((part, value, color));
        }

        var size = new Vector2(s.Width, s.BarHeight);
        for (var i = 0; i < visible.Count; i++)
        {
            var (part, value, color) = visible[i];
            var barPos = pos + new Vector2(0, i * (s.BarHeight + s.Spacing));
            DrawPart(ctx, s, part, value, color, barPos, size);
        }
    }

    internal static void DrawPart(DrawContext ctx, JobGaugeSettings s, GaugePart part, GaugeValue value, Vector4 color, Vector2 pos, Vector2 size)
    {
        switch (part.Kind)
        {
            case GaugeKind.Stacks:
            {
                var max = Math.Max(1, (int)value.Max);
                var filled = (int)value.Value;
                Gfx.Pips(ctx, pos, size, max, i => i < filled ? color : null);
                break;
            }

            case GaugeKind.Pips:
            {
                var count = Math.Max(1, (int)value.Max);
                var colorOf = value.PipColor ?? (_ => null);
                Gfx.Pips(ctx, pos, size, count, colorOf);
                break;
            }

            default:
            {
                var (min, max) = Gfx.FramedBar(ctx, pos, size, value.Hidden ? 0f : value.Fraction, color);
                if (s.ShowText && !string.IsNullOrEmpty(value.Text))
                {
                    using var font = s.TextFontSize > 0 ? ctx.Fonts.Push(s.TextFontSize) : ctx.Fonts.PushSmall();
                    Gfx.Text(ctx, value.Text, (min + max) / 2f, TextAlign.Center, Vector4.One);
                }

                break;
            }
        }
    }

    public static string PartKey(uint jobId, GaugePart part) => $"{Jobs.Get(jobId).Abbreviation}.{part.Id}";

    private static GaugePartSettings? PartSettings(Profile profile, GaugePart part)
    {
        var job = Svc.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
        return profile.JobGauge.Parts.TryGetValue(PartKey(job, part), out var settings) ? settings : null;
    }

    private static GaugePart[]? CurrentParts()
    {
        var job = Svc.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
        return JobGaugeDefinitions.ByJob.TryGetValue(job, out var parts) ? parts : null;
    }
}
