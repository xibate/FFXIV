using System;
using System.Numerics;
using LucentUI.Config;
using LucentUI.Gauges;
using LucentUI.Style;

namespace LucentUI.Hud.Elements;

/// <summary>One job gauge bar the user detached from the stack, so it can be moved and sized on its own.</summary>
public sealed class JobGaugePartElement : HudElement
{
    private readonly uint jobId;
    private readonly GaugePart part;
    private readonly int index;
    private readonly GaugeContext context = new();

    public JobGaugePartElement(uint jobId, GaugePart part, int index)
    {
        this.jobId = jobId;
        this.part = part;
        this.index = index;
        Key = JobGaugeElement.PartKey(jobId, part);
    }

    public string Key { get; }

    public override string Id => "gauge." + Key;

    public override string Label => $"{Jobs.Get(jobId).Abbreviation} {part.Label}";

    public override Vector2 DefaultPosition => new(-130, 330 + (index + 1) * 20);

    private GaugePartSettings? Settings(Profile profile) =>
        profile.JobGauge.Parts.TryGetValue(Key, out var settings) ? settings : null;

    public override bool IsEnabled(Profile profile)
    {
        var player = Svc.ObjectTable.LocalPlayer;
        var settings = Settings(profile);
        return profile.Modules.JobGauges && profile.JobGauge.Enabled &&
               player != null && player.ClassJob.RowId == jobId && player.Level >= part.MinLevel &&
               settings is { Detached: true, Enabled: true };
    }

    public override VisibilitySettings? GetVisibility(Profile profile) => profile.JobGauge.Visibility;

    public override Vector2 GetSize(Profile profile)
    {
        var size = Settings(profile)?.Size ?? Vector2.Zero;
        var gauge = profile.JobGauge;
        return new Vector2(size.X > 0 ? size.X : gauge.Width, size.Y > 0 ? size.Y : gauge.BarHeight);
    }

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var player = Svc.ObjectTable.LocalPlayer;
        var settings = Settings(ctx.Profile);
        if (player == null || settings == null)
        {
            return;
        }

        context.Player = player;
        context.Target = Svc.TargetManager.Target;
        context.Level = player.Level;

        GaugeValue value;
        try
        {
            value = part.Read(context);
        }
        catch (Exception)
        {
            return;
        }

        if (value.Hidden)
        {
            if (!ctx.Preview && ctx.Profile.JobGauge.HideEmptyTimers)
            {
                return;
            }

            value.Hidden = false;
            value.Value = ctx.Preview ? value.Max * 0.6f : 0f;
        }

        var color = settings.CustomColor ? settings.Color : value.Color ?? part.DefaultColor;
        JobGaugeElement.DrawPart(ctx, ctx.Profile.JobGauge, part, value, color, pos, GetSize(ctx.Profile));
    }
}
