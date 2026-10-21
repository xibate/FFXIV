using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
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
    private static readonly Dictionary<string, string> StatusSearches = new();

    /// <summary>
    /// Edits a list of status ids: remove listed ones, add statuses that are on <paramref name="activeFrom"/> right
    /// now, or find one by name or id.
    /// </summary>
    private static void StatusListEditor(string title, List<uint> ids, IGameObject? activeFrom, bool onlyMine, string description)
    {
        Ui.Card(title, description);

        foreach (var id in ids.ToList())
        {
            if (Ui.Button($"{Auras.NameOf(id)} (#{id})", "Remove"))
            {
                ids.Remove(id);
                Plugin.MarkDirty();
            }
        }

        foreach (var (id, name) in Auras.ActiveOn(activeFrom, onlyMine))
        {
            if (!ids.Contains(id) && Ui.Button($"{name} (active now, #{id})", "Add"))
            {
                ids.Add(id);
                Plugin.MarkDirty();
            }
        }

        var search = StatusSearches.TryGetValue(title, out var saved) ? saved : string.Empty;
        Ui.Text("Find a status", ref search, "Type at least 3 letters of its name, or its id.", "Name or id");
        StatusSearches[title] = search;
        foreach (var (id, name) in Auras.Search(search))
        {
            if (!ids.Contains(id) && Ui.Button($"{name} (#{id})", "Add"))
            {
                ids.Add(id);
                Plugin.MarkDirty();
            }
        }
    }

    /// <summary>The module's master switch, shown at the top of its page.</summary>
    private static void ModuleSwitch(string name, ref bool enabled, string? note = null)
    {
        Ui.Card("Module", note);
        Ui.Toggle($"{name} on", ref enabled, enabled ? null : "This module is off, so nothing on this page shows in game.");
    }

    private static void DrawJobGauge(JobGaugeSettings s)
    {
        Ui.PageHeader("Job Gauge", "Your job's resources, procs and DoTs as bars. Every job has its own set.");
        ModuleSwitch("Job gauge", ref Plugin.Profiles.Current.Modules.JobGauges);
        CopyAndReset("jobgauge", s, () => new Profile().JobGauge);
        var tab = Ui.Tabs("jobgauge", "Layout", "Current Job", "All Jobs");

        if (Ui.Tab(tab, 0))
        {
            Ui.Card("Job Gauge");
            Ui.Toggle("Enabled", ref s.Enabled);
            Ui.Slider("Width", ref s.Width, 80f, 800f, "%.0f");
            Ui.Slider("Bar height", ref s.BarHeight, 4f, 60f, "%.0f");
            Ui.Slider("Spacing", ref s.Spacing, 0, 20);
            Ui.MediaPicker("Bar texture", ref s.Texture, false, "Profile texture", "Empty uses the texture from the Style page.");
            Ui.Toggle("Show values", ref s.ShowText);
            Ui.Toggle("Hide empty timers", ref s.HideEmptyTimers, "Buff and DoT bars disappear while they are not running.");
            DrawVisibility("Job Gauge", s.Visibility);
        }

        // The current job first, then the rest in role order.
        var current = Svc.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
        var jobs = JobGaugeDefinitions.Jobs.OrderBy(j => j == current ? 0 : 1).ToList();
        if (tab == 0)
        {
            return;
        }

        if (tab == 1 && !JobGaugeDefinitions.ByJob.ContainsKey(current))
        {
            Ui.Card("Current Job", "Log in on a combat job to see its bars here, or use All Jobs.");
            Ui.EndCard();
            return;
        }

        foreach (var job in jobs)
        {
            if (tab == 1 && job != current)
            {
                continue;
            }

            var info = Jobs.Get(job);
            Ui.Card(job == current ? $"{info.Abbreviation} (current job)" : info.Abbreviation, "Turn bars on or off and pick your own colours.");
            foreach (var part in JobGaugeDefinitions.ByJob[job])
            {
                var key = JobGaugeElement.PartKey(job, part);
                var exists = s.Parts.TryGetValue(key, out var settings);
                settings ??= new GaugePartSettings { Enabled = part.DefaultEnabled, Color = part.DefaultColor };

                var changed = Ui.Toggle(part.Label, ref settings.Enabled, part.MinLevel > 1 ? $"From level {part.MinLevel}." : null);
                changed |= Ui.Toggle($"{part.Label}: own colour", ref settings.CustomColor);
                if (settings.CustomColor)
                {
                    changed |= Ui.Color($"{part.Label}: colour", ref settings.Color);
                }

                changed |= Ui.Toggle($"{part.Label}: detach", ref settings.Detached, "Show this bar on its own, so you can move and size it separately in Unlock Mode.");
                if (settings.Detached)
                {
                    changed |= Ui.Size($"{part.Label}: size", ref settings.Size, "0 uses the job gauge's width and bar height.");
                }

                if (changed && !exists)
                {
                    s.Parts[key] = settings;
                }
            }
        }
    }

    private static void DrawCooldownManager(CooldownManagerSettings s)
    {
        Ui.PageHeader("Cooldown Manager", "Your own cooldowns as icon bars with a timer, like EllesmereUI's Cooldown Manager.");
        ModuleSwitch("Cooldown manager", ref Plugin.Profiles.Current.Modules.CooldownManager);
        var tab = Ui.Tabs("cdm", "Cooldowns", "Utility", "Buffs", "Custom Bars", "Buff Filter", "Actions");
        var defaults = new Profile().CooldownManager;
        var bars = new[]
        {
            ("Cooldowns Bar", s.Cooldowns, defaults.Cooldowns, "Your job's abilities with a 30s or longer recast."),
            ("Utility Bar", s.Utility, defaults.Utility, "Role actions such as Rampart, Feint, Addle or Swiftcast."),
            ("Buffs Bar", s.Buffs, defaults.Buffs, "Buffs you put on yourself, with time left."),
        };

        for (var i = 0; i < bars.Length; i++)
        {
            if (Ui.Tab(tab, i))
            {
                var (title, bar, fallback, description) = bars[i];
                CopyAndReset("cdm" + i, bar, () => fallback, ("Cooldowns bar", s.Cooldowns), ("Utility bar", s.Utility), ("Buffs bar", s.Buffs));
                DrawCooldownBar(title, bar, description);
            }
        }

        if (Ui.Tab(tab, 3))
        {
            DrawCustomBars(s);
        }

        if (Ui.Tab(tab, 4))
        {
            DrawBuffFilter(s);
        }

        if (Ui.Tab(tab, 5))
        {
            DrawJobActions(s);
        }
    }

    private static string customActionSearch = string.Empty;
    private static string customItemSearch = string.Empty;
    private static bool customItemHq = true;

    /// <summary>Extra bars with chosen actions and items (potions, food), like EllesmereUI's custom bars.</summary>
    private static void DrawCustomBars(CooldownManagerSettings s)
    {
        var bars = s.CustomBars;
        Ui.Card("Custom Bars", $"Bars with the actions and items you pick, e.g. potions or a few defensives next to your frame. Up to {CooldownManagerSettings.MaxCustomBars} bars; each is moved in Unlock Mode like any other element.");
        if (bars.Count < CooldownManagerSettings.MaxCustomBars && Ui.Button("New bar", "Add a custom bar"))
        {
            bars.Add(new CustomCooldownBar { Name = $"Custom Bar {bars.Count + 1}" });
            Ui.OpenTab("cdmcustom", bars.Count - 1);
            Plugin.MarkDirty();
        }

        if (bars.Count == 0)
        {
            Ui.EndCard();
            return;
        }

        var tab = Ui.Tabs("cdmcustom", bars.Select((b, i) => string.IsNullOrWhiteSpace(b.Name) ? $"Bar {i + 1}" : b.Name).ToArray());
        for (var i = 0; i < bars.Count; i++)
        {
            if (!Ui.Tab(tab, i))
            {
                continue;
            }

            var bar = bars[i];
            ImGui.PushID("custombar" + i);
            Ui.Card(bar.Name.Length > 0 ? bar.Name : $"Bar {i + 1}");
            Ui.Text("Name", ref bar.Name);
            Ui.Toggle("Hide items you have none of", ref bar.HideMissingItems);
            var ctrl = ImGui.GetIO().KeyCtrl;
            if (Ui.Button("Delete", ctrl ? "Delete this bar" : "Hold Ctrl to delete") && ctrl)
            {
                DeleteCustomBar(s, i);
                ImGui.PopID();
                return;
            }

            DrawCooldownBar("Bar Layout", bar.Bar, "Size, rows and look of this bar.");
            DrawCustomEntries(bar);
            ImGui.PopID();
        }
    }

    private static void DrawCustomEntries(CustomCooldownBar bar)
    {
        var player = Svc.ObjectTable.LocalPlayer;
        var job = player?.ClassJob.RowId ?? 0;
        var abbreviation = job != 0 ? Jobs.Get(job).Abbreviation : "?";

        Ui.Card("Icons", "Actions show on the job they were added on; items on every job.");
        var actionSheet = Svc.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        var itemSheet = Svc.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        foreach (var entry in bar.Entries.ToList())
        {
            var name = entry.Kind == CooldownEntryKind.Item
                ? (itemSheet.GetRowOrDefault(entry.Id)?.Name.ExtractText() ?? $"Item {entry.Id}") + (entry.HighQuality ? " (HQ)" : string.Empty)
                : actionSheet.GetRowOrDefault(entry.Id)?.Name.ExtractText() ?? $"Action {entry.Id}";
            var where = entry.Job == 0 ? "all jobs" : Jobs.Get(entry.Job).Abbreviation;
            if (Ui.Button($"{name} ({where})##{entry.Kind}{entry.Id}{entry.Job}", "Remove"))
            {
                bar.Entries.Remove(entry);
                Plugin.MarkDirty();
            }
        }

        if (player == null)
        {
            Ui.Card("Add Actions", "Log in to add your job's actions.");
        }
        else
        {
            Ui.Card($"Add {abbreviation} Actions", "Type part of an action's name.");
            Ui.Text("Find an action", ref customActionSearch, hint: "e.g. Rampart");
            if (customActionSearch.Trim().Length >= 2)
            {
                foreach (var action in JobActions.AllForJob(job).Where(a => a.Name.Contains(customActionSearch.Trim(), System.StringComparison.OrdinalIgnoreCase)).Take(15))
                {
                    var exists = bar.Entries.Any(e => e.Kind == CooldownEntryKind.Action && e.Id == action.Id && (e.Job == 0 || e.Job == job));
                    if (!exists && Ui.Button($"{action.Name} (Lv {action.Level}, {action.Recast:0.#}s)##add{action.Id}", "Add"))
                    {
                        bar.Entries.Add(new CustomCooldownEntry { Kind = CooldownEntryKind.Action, Id = action.Id, Job = job });
                        Plugin.MarkDirty();
                    }
                }
            }
        }

        Ui.Card("Add Items", "Potions, food and other items with a use. Shows how many you have.");
        Ui.Toggle("High quality", ref customItemHq, "Track the HQ version of the item.");
        Ui.Text("Find an item", ref customItemSearch, "Type at least 3 letters.", "e.g. Grade 2 Gemdraught");
        foreach (var (id, name, _) in JobActions.SearchItems(customItemSearch))
        {
            if (Ui.Button($"{name}##item{id}", "Add"))
            {
                bar.Entries.Add(new CustomCooldownEntry { Kind = CooldownEntryKind.Item, Id = id, HighQuality = customItemHq });
                Plugin.MarkDirty();
            }
        }
    }

    /// <summary>Removes a custom bar and moves the positions of the bars after it down one slot.</summary>
    private static void DeleteCustomBar(CooldownManagerSettings s, int index)
    {
        var positions = Plugin.Profiles.Current.Layout.Positions;
        s.CustomBars.RemoveAt(index);
        for (var i = index; i < CooldownManagerSettings.MaxCustomBars; i++)
        {
            var next = CooldownBarElement.CustomId(i + 1);
            if (positions.TryGetValue(next, out var pos))
            {
                positions[CooldownBarElement.CustomId(i)] = pos;
            }
            else
            {
                positions.Remove(CooldownBarElement.CustomId(i));
            }
        }

        positions.Remove(CooldownBarElement.CustomId(CooldownManagerSettings.MaxCustomBars));
        Plugin.MarkDirty();
    }

    private static void DrawBuffFilter(CooldownManagerSettings s)
    {
        var me = Svc.ObjectTable.LocalPlayer;
        var currentJob = me?.ClassJob.RowId ?? 0;
        Ui.Card("Buffs Bar Filter", "By default the bar shows every buff you put on yourself. Track only chosen buffs per job, or hide some everywhere.");
        Ui.Toggle("Only tracked buffs", ref s.OnlyTrackedBuffs, "Show only the buffs listed for your current job below.");
        if (me != null)
        {
            if (!s.TrackedBuffs.TryGetValue(currentJob, out var tracked))
            {
                tracked = new List<uint>();
            }

            StatusListEditor($"Tracked Buffs ({Jobs.Get(currentJob).Abbreviation})", tracked, me, true, "Used when \"Only tracked buffs\" is on. Each job has its own list.");
            if (tracked.Count > 0)
            {
                s.TrackedBuffs[currentJob] = tracked;
            }
            else
            {
                s.TrackedBuffs.Remove(currentJob);
            }
        }

        StatusListEditor("Hidden Buffs", s.HiddenBuffs, me, true, "Never shown on the Buffs bar, on any job.");
    }

    private static void DrawJobActions(CooldownManagerSettings s)
    {
        var player = Svc.ObjectTable.LocalPlayer;
        if (player == null)
        {
            Ui.Card("Actions", "Log in to choose which of your job's actions the bars show.");
            Ui.EndCard();
            return;
        }

        var job = player.ClassJob.RowId;
        var abbreviation = Jobs.Get(job).Abbreviation;
        var (cooldowns, utility) = JobActions.Selection(s, job);
        var custom = s.Jobs.ContainsKey(job);

        Ui.Card($"{abbreviation} Actions", custom
            ? "Your own list for this job. Change where each action goes."
            : "The automatic list for this job. Changing any action switches this job to your own list.");

        foreach (var action in JobActions.ForJob(job))
        {
            var place = cooldowns.Contains(action.Id) ? CooldownPlace.Cooldowns : utility.Contains(action.Id) ? CooldownPlace.Utility : CooldownPlace.Hidden;
            var label = $"{action.Name} (Lv {action.Level}, {action.Recast:0}s)";
            if (Ui.Enum(label, ref place))
            {
                var selection = JobActions.Customise(s, job);
                selection.Cooldowns.Remove(action.Id);
                selection.Utility.Remove(action.Id);
                if (place == CooldownPlace.Cooldowns)
                {
                    selection.Cooldowns.Add(action.Id);
                }
                else if (place == CooldownPlace.Utility)
                {
                    selection.Utility.Add(action.Id);
                }
            }
        }

        if (custom && Ui.Button("Reset", $"Use the automatic list for {abbreviation}"))
        {
            s.Jobs.Remove(job);
            Plugin.MarkDirty();
        }
    }

    private enum CooldownPlace
    {
        Hidden,
        Cooldowns,
        Utility,
    }

    private static void DrawCooldownBar(string title, CooldownBarSettings s, string description)
    {
        Ui.Card(title, description);
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Slider("Icon size", ref s.IconSize, 16f, 96f, "%.0f");
        Ui.Slider("Spacing", ref s.Spacing, 0, 20);
        Ui.Slider("Rows", ref s.Rows, 1, 4);
        Ui.Slider("Icons per row", ref s.PerRow, 0, 20, "0 splits the icons evenly over the rows.");
        Ui.Slider("Maximum icons", ref s.MaxIcons, 1, 30, "The bar is sized for this many icons.");
        Ui.Enum("Align", ref s.Align);
        Ui.Toggle("Timer text", ref s.ShowCooldownText);
        Ui.Toggle("Charges", ref s.ShowCharges);
        Ui.Toggle("Dim while on cooldown", ref s.DimOnCooldown);
        Ui.Toggle("Icon background", ref s.ShowBackground);
        if (title.StartsWith("Buffs", System.StringComparison.Ordinal))
        {
            Ui.Enum("Buff icon shape", ref s.BuffShape, "Style Default follows the Style page.");
        }
        Ui.Slider("Icon zoom", ref s.IconZoom, 0f, 0.3f, "%.2f", "Crops the icon's edges.");
        Ui.Slider("Timer font size", ref s.TimerFontSize, 0, 40, "0 uses the small font size from the Style page.");
        DrawVisibility(title, s.Visibility);
    }

    private static void DrawPartyCooldowns(PartyCooldownSettings s)
    {
        Ui.PageHeader("Party Cooldowns", "Raid buffs and mitigation from everyone in your party: active, on cooldown or ready.");
        ModuleSwitch("Party cooldowns", ref Plugin.Profiles.Current.Modules.PartyCooldowns);

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
        Ui.PageHeader("Nameplates", "Health bars and names above units, like EllesmereUI's nameplates.");
        ModuleSwitch("Nameplates", ref Plugin.Profiles.Current.Modules.Nameplates, "Off by default because it draws over the game world.");
        CopyAndReset("nameplates", s, () => new Profile().Nameplates);
        var tab = Ui.Tabs("nameplates", "Units", "Bars", "Text", "Cast Bar", "Names");

        if (Ui.Tab(tab, 0))
        {
            Ui.Card("Nameplates");
            Ui.Toggle("Enabled", ref s.Enabled);
            Ui.Enum("Enemies", ref s.Enemies);
            Ui.Toggle("Only enemies in combat", ref s.EnemiesOnlyInCombat);
            Ui.Enum("Players", ref s.Players);
            Ui.Enum("Friendly NPCs", ref s.FriendlyNpcs);
            Ui.Toggle("Your own nameplate", ref s.ShowSelf);
            Ui.Slider("Maximum distance", ref s.MaxDistance, 5f, 100f, "%.0f yalms");
            Ui.Toggle("Follow the game's nameplates", ref s.FollowGameNameplates, "Only units the game would show a nameplate for (line of sight, the game's name settings). When the game shows none, every unit nearby is used.");
            Ui.Enum("Game nameplates", ref s.GameNameplates, "HideReplaced removes the game's name from units that get a LucentUI plate. HideAll hides the game's nameplates completely, including NPC names and quest markers.");
            Ui.Toggle("Click to target", ref s.Interactive, "Left-click targets, right-click opens the menu, and the plate works as a mouseover target.");
            Ui.Slider("Vertical offset", ref s.YOffset, -100f, 100f, "%.0f");
        }

        if (Ui.Tab(tab, 1))
        {
            Ui.Card("Health Bar Plates");
            Ui.Size("Size", ref s.Size);
            Ui.MediaPicker("Bar texture", ref s.Texture, false, "Profile texture", "Empty uses the texture from the Style page.");
            Ui.Enum("Health colour", ref s.ColorMode);
            Ui.Toggle("Shields", ref s.ShowShield);
            Ui.Toggle("Highlight target", ref s.HighlightTarget);
            Ui.Slider("Target size", ref s.TargetScale, 1f, 1.5f, "%.2fx");
            Ui.Percent("Other plates' opacity", ref s.NonTargetAlpha, "Fades every plate except your target's while you have one.");
        }

        if (Ui.Tab(tab, 2))
        {
            DrawTextSlot("Plate Left Text", ref s.LeftText, s.LeftStyle);
            DrawTextSlot("Plate Center Text", ref s.CenterText, s.CenterStyle);
            DrawTextSlot("Plate Right Text", ref s.RightText, s.RightStyle);
        }

        if (Ui.Tab(tab, 3))
        {
            Ui.Card("Plate Cast Bar");
            Ui.Toggle("Cast bar", ref s.ShowCast);
            Ui.Slider("Height", ref s.CastHeight, 4f, 30f, "%.0f");
            Ui.Toggle("Spell name", ref s.ShowCastName);
            Ui.Toggle("Spell icon", ref s.ShowCastIcon);
            Ui.TextLook("Spell name", s.CastNameStyle);
        }

        if (Ui.Tab(tab, 4))
        {
            Ui.Card("Name-Only Plates");
            Ui.TextContent("Shows", ref s.NameOnlyText);
            Ui.Slider("Font size", ref s.NameOnlyFontSize, 0, 40, "0 uses the HUD font size.");
            Ui.Toggle("Job colour for players", ref s.NameOnlyJobColor);
        }
    }

    private static void DrawEnemyList(EnemyListSettings s)
    {
        Ui.PageHeader("Enemy List", "The enemies you are fighting, with casts and who they are attacking.");
        ModuleSwitch("Enemy list", ref Plugin.Profiles.Current.Modules.EnemyList);

        Ui.Card("Enemy List");
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Size("Row size", ref s.Size);
        Ui.Slider("Spacing", ref s.Spacing, 0, 20);
        Ui.Slider("Maximum enemies", ref s.MaxEnemies, 1, 8);
        Ui.Toggle("Grow upwards", ref s.GrowUp);
        Ui.Toggle("Show casts", ref s.ShowCast);
        Ui.Toggle("Mark enemies attacking you", ref s.ShowAggro);

        Ui.Card("Enemy List Text");
        Ui.TextContent("Left text", ref s.LeftText);
        Ui.TextContent("Right text", ref s.RightText);

        DrawVisibility("Enemy List", s.Visibility);
    }

    private static void DrawExtras(Profile profile)
    {
        Ui.PageHeader("Extras", "Small helpers: GCD, MP ticks, limit break, pull timer and experience.");
        ModuleSwitch("Extras", ref Plugin.Profiles.Current.Modules.Extras);

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
        Ui.PageHeader("Hotbars", "Show the game's hotbars only when you need them.");
        ModuleSwitch("Hotbar rules", ref Plugin.Profiles.Current.Modules.Hotbars, "Choosing a rule below turns this on for you.");

        Ui.Card("Hotbar Visibility", "Keybinds keep working while a hotbar is hidden.");
        for (var bar = 1; bar <= 11; bar++)
        {
            var mode = s.Bars.TryGetValue(bar, out var saved) ? saved : VisibilityMode.Always;
            var label = bar == 11 ? "Cross hotbar" : $"Hotbar {bar}";
            if (Ui.Enum(label, ref mode))
            {
                Plugin.Profiles.Current.Modules.Hotbars = true;
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
        var name = importName.Trim();
        if (name.Length == 0)
        {
            // No name typed: pick a free one, so importing again never clashes.
            name = "EllesmereUI";
            for (var i = 2; Plugin.Config.Profiles.ContainsKey(name); i++)
            {
                name = $"EllesmereUI {i}";
            }
        }
        var screen = ImGui.GetMainViewport().Size;
        var ok = Plugin.Profiles.TryImportEllesmere(text, name, screen, out var notes, out var error);
        if (ok)
        {
            Plugin.Profiles.SetActive(name);
        }

        Report(ok, $"Imported \"{name}\" from EllesmereUI and switched to it.\n" + string.Join("\n", notes), error);
    }
}
