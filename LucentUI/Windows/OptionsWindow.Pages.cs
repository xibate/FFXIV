using System.Linq;
using Dalamud.Bindings.ImGui;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Gauges;
using LucentUI.Hud.Elements;
using LucentUI.Style;

namespace LucentUI.Windows;

/// <summary>Settings pages for the job gauge, party cooldowns, nameplates, enemy list, extras and hotbars.</summary>
public sealed partial class OptionsWindow
{
    private static void DrawJobGauge(JobGaugeSettings s)
    {
        Ui.PageHeader("Job Gauge", "Your job's resources, procs and DoTs as bars. Every job has its own set.");

        Ui.Card("Job Gauge");
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Slider("Width", ref s.Width, 80f, 800f, "%.0f");
        Ui.Slider("Bar height", ref s.BarHeight, 4f, 60f, "%.0f");
        Ui.Slider("Spacing", ref s.Spacing, 0, 20);
        Ui.Toggle("Show values", ref s.ShowText);
        Ui.Toggle("Hide empty timers", ref s.HideEmptyTimers, "Buff and DoT bars disappear while they are not running.");
        DrawVisibility("Job Gauge", s.Visibility);

        // The current job first, then the rest in role order.
        var current = Svc.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
        var jobs = JobGaugeDefinitions.Jobs.OrderBy(j => j == current ? 0 : 1).ToList();
        foreach (var job in jobs)
        {
            var info = Jobs.Get(job);
            Ui.Card(job == current ? $"{info.Abbreviation} (current job)" : info.Abbreviation, "Turn bars on or off and pick your own colours.");
            foreach (var part in JobGaugeDefinitions.ByJob[job])
            {
                var key = JobGaugeElement.PartKey(job, part);
                var exists = s.Parts.TryGetValue(key, out var settings);
                settings ??= new GaugePartSettings { Color = part.DefaultColor };

                var changed = Ui.Toggle(part.Label, ref settings.Enabled, part.MinLevel > 1 ? $"From level {part.MinLevel}." : null);
                changed |= Ui.Toggle($"{part.Label}: own colour", ref settings.CustomColor);
                if (settings.CustomColor)
                {
                    changed |= Ui.Color($"{part.Label}: colour", ref settings.Color);
                }

                if (changed && !exists)
                {
                    s.Parts[key] = settings;
                }
            }
        }
    }

    private static void DrawPartyCooldowns(PartyCooldownSettings s)
    {
        Ui.PageHeader("Party Cooldowns", "Raid buffs and mitigation from everyone in your party: active, on cooldown or ready.");

        Ui.Card("Party Cooldowns");
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Size("Row size", ref s.RowSize);
        Ui.Slider("Spacing", ref s.Spacing, 0, 20);
        Ui.Slider("Maximum rows", ref s.MaxRows, 1, 40);
        Ui.Toggle("Grow upwards", ref s.GrowUp);
        Ui.Toggle("Show ready cooldowns", ref s.ShowReady);
        Ui.Toggle("Show when solo", ref s.ShowWhenSolo);

        Ui.Card("Cooldown Types");
        Ui.Toggle("Raid buffs", ref s.ShowRaidBuffs, "Divination, Chain Stratagem, Battle Litany, Technical Finish...");
        Ui.Toggle("Party mitigation", ref s.ShowMitigation, "Reprisal, Feint, Addle, Divine Veil, Kerachole, Tactician...");
        Ui.Toggle("Big heals", ref s.ShowHealing, "Liturgy of the Bell, Neutral Sect, Philosophia, Nature's Minne...");
        Ui.Toggle("Tank invulnerabilities", ref s.ShowTankInvulns);

        DrawVisibility("Party Cooldowns", s.Visibility);
    }

    private static void DrawNameplates(NameplateSettings s)
    {
        Ui.PageHeader("Nameplates", "Health bars above units. Turn the module on under General first.");

        Ui.Card("Nameplates");
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Size("Size", ref s.Size);
        Ui.Slider("Vertical offset", ref s.YOffset, -100f, 100f, "%.0f");
        Ui.Slider("Maximum distance", ref s.MaxDistance, 5f, 100f, "%.0f yalms");
        Ui.Toggle("Highlight target", ref s.HighlightTarget);
        Ui.Toggle("Hide the game's nameplates", ref s.HideGameNameplates, "Hides every game nameplate, including names of NPCs and objects.");

        Ui.Card("Nameplate Units");
        Ui.Toggle("Enemies", ref s.ShowEnemies);
        Ui.Toggle("Only enemies in combat", ref s.EnemiesOnlyInCombat);
        Ui.Toggle("Players", ref s.ShowPlayers);
        Ui.Toggle("Friendly NPCs", ref s.ShowFriendlyNpcs);

        Ui.Card("Nameplate Text");
        Ui.Toggle("Name", ref s.ShowName);
        Ui.Toggle("Level", ref s.ShowLevel);
        Ui.Toggle("Health %", ref s.ShowHealthText);
        Ui.Toggle("Cast bar", ref s.ShowCast);
    }

    private static void DrawEnemyList(EnemyListSettings s)
    {
        Ui.PageHeader("Enemy List", "The enemies you are fighting, with casts and who they are attacking.");

        Ui.Card("Enemy List");
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Size("Row size", ref s.Size);
        Ui.Slider("Spacing", ref s.Spacing, 0, 20);
        Ui.Slider("Maximum enemies", ref s.MaxEnemies, 1, 8);
        Ui.Toggle("Grow upwards", ref s.GrowUp);
        Ui.Toggle("Show casts", ref s.ShowCast);
        Ui.Toggle("Mark enemies attacking you", ref s.ShowAggro);

        Ui.Card("Enemy List Text", TextTags.Help);
        Ui.Text("Left text", ref s.LeftText);
        Ui.Text("Right text", ref s.RightText);

        DrawVisibility("Enemy List", s.Visibility);
    }

    private static void DrawExtras(Profile profile)
    {
        Ui.PageHeader("Extras", "Small helpers: GCD, MP ticks, limit break, pull timer and experience.");

        var gcd = profile.Gcd;
        Ui.Card("GCD");
        Ui.Toggle("Enabled", ref gcd.Enabled);
        Ui.Size("Size", ref gcd.Size);
        Ui.Toggle("Ring instead of bar", ref gcd.Circle);
        Ui.Slider("Ring radius", ref gcd.CircleRadius, 6f, 80f, "%.0f");
        Ui.Toggle("Follow the mouse", ref gcd.AttachToMouse);
        Ui.Color("Colour", ref gcd.Color);
        DrawVisibility("GCD", gcd.Visibility);

        DrawSimpleBar("MP Ticker", profile.MpTicker, "Hide when MP is full");
        DrawSimpleBar("Limit Break", profile.LimitBreak, "Hide when empty");
        DrawSimpleBar("Pull Timer", profile.PullTimer, null);
        DrawSimpleBar("Experience", profile.Experience, "Hide at max level");
    }

    private static void DrawSimpleBar(string title, SimpleBarSettings s, string? hideLabel)
    {
        Ui.Card(title);
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Size("Size", ref s.Size);
        Ui.Color("Colour", ref s.Color);
        Ui.Toggle("Show text", ref s.ShowText);
        if (hideLabel != null)
        {
            Ui.Toggle(hideLabel, ref s.HideWhenInactive);
        }

        DrawVisibility(title, s.Visibility);
    }

    private static void DrawHotbars(HotbarSettings s)
    {
        Ui.PageHeader("Hotbars", "Show the game's hotbars only when you need them. Turn on \"Hotbar rules\" under General first.");

        Ui.Card("Hotbar Visibility", "Keybinds keep working while a hotbar is hidden.");
        for (var bar = 1; bar <= 11; bar++)
        {
            var mode = s.Bars.TryGetValue(bar, out var saved) ? saved : VisibilityMode.Always;
            var label = bar == 11 ? "Cross hotbar" : $"Hotbar {bar}";
            if (Ui.Enum(label, ref mode))
            {
                if (mode == VisibilityMode.Always)
                {
                    s.Bars.Remove(bar);
                }
                else
                {
                    s.Bars[bar] = mode;
                }
            }
        }
    }

    private void ImportEllesmere(string text)
    {
        var name = importName.Trim().Length > 0 ? importName : "EllesmereUI";
        var screen = ImGui.GetMainViewport().Size;
        var ok = Plugin.Profiles.TryImportEllesmere(text, name, screen, out var notes, out var error);
        Report(ok, $"Imported \"{name}\" from EllesmereUI. Switch to it above to use it.\n" + string.Join("\n", notes), error);
    }
}
