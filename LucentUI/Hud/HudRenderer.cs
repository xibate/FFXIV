using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using LucentUI.Data;
using LucentUI.Hud.Elements;
using LucentUI.Style;

namespace LucentUI.Hud;

/// <summary>Draws every enabled element into one full-screen, click-through ImGui window.</summary>
public sealed class HudRenderer : IDisposable
{
    private const float FadeSpeed = 6f;

    private readonly Fonts fonts = new();
    private readonly Interaction input = new();

    public Interaction Input => input;
    private readonly Smoother smoother = new();
    private readonly ClipRects clips = new();
    private readonly DrawContext ctx = new();
    private readonly Dictionary<string, float> alphas = new();
    private readonly HashSet<string> failedElements = new();

    public HudRenderer(Data.PartyCooldownTracker cooldowns)
    {
        Elements = new HudElement[]
        {
            new NameplatesElement(),
            new PartyFramesElement(),
            new PartyCooldownsElement(cooldowns),
            new EnemyListElement(),
            new UnitFrameElement(UnitKind.Player),
            new UnitFrameElement(UnitKind.Target),
            new UnitFrameElement(UnitKind.TargetOfTarget),
            new UnitFrameElement(UnitKind.Focus),
            new JobGaugeElement(),
            new CooldownBarElement(CooldownBarKind.Cooldowns),
            new CooldownBarElement(CooldownBarKind.Utility),
            new CooldownBarElement(CooldownBarKind.Buffs),
            new CastBarElement(UnitKind.Player),
            new CastBarElement(UnitKind.Target),
            new CastBarElement(UnitKind.Focus),
            new AuraElement(AuraGroup.PlayerBuffs),
            new AuraElement(AuraGroup.PlayerDebuffs),
            new AuraElement(AuraGroup.Target),
            new GcdElement(),
            new MpTickerElement(),
            new LimitBreakElement(),
            new PullTimerElement(),
            new ExperienceElement(),
        };

        // One element per job gauge bar, used when the user detaches that bar from the stack.
        var all = new List<HudElement>(Elements);
        foreach (var (job, parts) in Gauges.JobGaugeDefinitions.ByJob)
        {
            for (var i = 0; i < parts.Length; i++)
            {
                all.Add(new JobGaugePartElement(job, parts[i], i));
            }
        }

        Elements = all;
        Unlock = new UnlockMode(Elements);
    }

    public IReadOnlyList<HudElement> Elements { get; }

    public UnlockMode Unlock { get; }

    /// <summary>Set while the settings window is open with previews on: elements show sample data.</summary>
    public bool SettingsPreview { get; set; }

    public void Draw()
    {
        var config = Plugin.Config;
        var profile = Plugin.Profiles.Current;

        if (!ShouldDraw())
        {
            input.Clear();
            return;
        }

        fonts.Ensure(profile.Style);

        var viewport = ImGui.GetMainViewport();
        var origin = viewport.Pos + viewport.Size / 2f;

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;

        var open = ImGui.Begin("##LucentUI_HUD", flags);
        ImGui.PopStyleVar(3);

        try
        {
            if (open)
            {
                clips.Update(config.General.ClipBehindWindows && !Unlock.Active, viewport.Pos);
                input.BeginFrame(clips);

                ctx.DrawList = ImGui.GetWindowDrawList();
                ctx.Profile = profile;
                ctx.General = config.General;
                ctx.Fonts = fonts;
                ctx.Input = input;
                ctx.Smoother = smoother;
                ctx.Clips = clips;
                ctx.DeltaTime = ImGui.GetIO().DeltaTime;
                ctx.Preview = Unlock.Active || SettingsPreview;
                smoother.Enabled = profile.Style.SmoothBars;
                smoother.NextFrame();

                if (Unlock.Active)
                {
                    Unlock.DrawGrid(ctx.DrawList, viewport.Pos, viewport.Size, origin, profile);
                }

                DrawElements(origin);
                input.EndFrame(config.General);
            }
        }
        finally
        {
            ImGui.End();
        }

        if (Unlock.Active)
        {
            Unlock.Draw(profile, origin, viewport.Pos, viewport.Size);
        }
    }

    private void DrawElements(Vector2 origin)
    {
        var profile = ctx.Profile;
        var delta = ImGui.GetIO().DeltaTime;

        foreach (var element in Elements)
        {
            if (!element.IsEnabled(profile))
            {
                continue;
            }

            var alpha = ctx.Preview ? 1f : CurrentAlpha(element, Visibility.TargetAlpha(element.GetVisibility(profile)), delta);
            if (alpha <= 0.001f)
            {
                continue;
            }

            ctx.Alpha = alpha;
            try
            {
                using var font = fonts.PushMain();
                DrawClipped(element, origin + element.GetPosition(profile));
            }
            catch (Exception ex)
            {
                // Log once per element so a single broken element cannot flood the log.
                if (failedElements.Add(element.Id))
                {
                    Svc.Log.Error(ex, $"Failed to draw {element.Id}");
                }
            }
        }

        ctx.Alpha = 1f;
    }

    /// <summary>Draws an element, cut away (or hidden) where a game window covers it.</summary>
    private void DrawClipped(HudElement element, Vector2 pos)
    {
        if (!clips.Enabled || !element.Movable)
        {
            element.Draw(ctx, pos);
            return;
        }

        // Elements may draw a little outside their box (labels, debuff icons), so look at a wider area.
        var margin = new Vector2(80f, 40f);
        var area = (pos - margin, pos + element.GetSize(ctx.Profile) + margin);
        if (clips.FirstOverlap(area.Item1, area.Item2) is not { } window)
        {
            element.Draw(ctx, pos);
            return;
        }

        if (Plugin.Config.General.ClipMode == Config.ClipMode.Hide)
        {
            return;
        }

        // Draw once per uncovered strip; only the first pass handles the mouse.
        var first = true;
        foreach (var (min, max) in ClipRects.Subtract(area, window))
        {
            ctx.DrawList.PushClipRect(min, max, true);
            input.Suppressed = !first;
            element.Draw(ctx, pos);
            ctx.DrawList.PopClipRect();
            first = false;
        }

        input.Suppressed = false;
    }

    private float CurrentAlpha(HudElement element, float target, float delta)
    {
        if (!Plugin.Config.General.SmoothFades)
        {
            alphas[element.Id] = target;
            return target;
        }

        if (!alphas.TryGetValue(element.Id, out var current))
        {
            current = target;
        }

        current = current < target
            ? Math.Min(target, current + FadeSpeed * delta)
            : Math.Max(target, current - FadeSpeed * delta);
        alphas[element.Id] = current;
        return current;
    }

    private bool ShouldDraw()
    {
        var general = Plugin.Config.General;
        if (Svc.ObjectTable.LocalPlayer == null || Visibility.InCutsceneOrLoading)
        {
            return false;
        }

        if (Unlock.Active)
        {
            return true;
        }

        return general.ShowHud && !(general.HideWhenGameUiHidden && Svc.GameGui.GameUiHidden);
    }

    public void Dispose()
    {
        input.Clear();
        fonts.Dispose();
    }
}
