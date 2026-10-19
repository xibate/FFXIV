using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Hud;
using LucentUI.Style;

namespace LucentUI.Windows;

public sealed partial class OptionsWindow : Window
{
    private enum Page
    {
        General,
        Style,
        Player,
        Target,
        TargetOfTarget,
        Focus,
        Party,
        CastBars,
        Auras,
        JobGauge,
        CooldownManager,
        PartyCooldowns,
        Nameplates,
        EnemyList,
        Extras,
        Hotbars,
        DefaultHud,
        Profiles,
        About,
    }

    private static readonly (string Group, (Page Page, string Label)[] Items)[] Navigation =
    {
        ("GENERAL", new[] { (Page.General, "General"), (Page.Style, "Style") }),
        ("FRAMES", new[] { (Page.Player, "Player"), (Page.Target, "Target"), (Page.TargetOfTarget, "Target of Target"), (Page.Focus, "Focus"), (Page.Party, "Party"), (Page.EnemyList, "Enemy List"), (Page.Nameplates, "Nameplates") }),
        ("COMBAT", new[] { (Page.JobGauge, "Job Gauge"), (Page.CooldownManager, "Cooldown Manager"), (Page.CastBars, "Cast Bars"), (Page.Auras, "Auras"), (Page.PartyCooldowns, "Party Cooldowns"), (Page.Extras, "Extras") }),
        ("SYSTEM", new[] { (Page.Hotbars, "Hotbars"), (Page.DefaultHud, "Default HUD"), (Page.Profiles, "Profiles"), (Page.About, "About") }),
    };

    // Pages included in search results (the custom ones are left out).
    private static readonly Page[] SearchablePages =
    {
        Page.General, Page.Style, Page.Player, Page.Target, Page.TargetOfTarget, Page.Focus,
        Page.Party, Page.EnemyList, Page.Nameplates, Page.JobGauge, Page.CooldownManager, Page.CastBars, Page.Auras,
        Page.PartyCooldowns, Page.Extras, Page.Hotbars, Page.DefaultHud,
    };

    private readonly HudRenderer hud;
    private Page page = Page.General;
    private string search = string.Empty;
    private string newProfileName = string.Empty;
    private string renameTo = string.Empty;
    private string importName = string.Empty;
    private string profileMessage = string.Empty;
    private int pushedColors;
    private int pushedVars;

    public OptionsWindow(HudRenderer hud)
        : base("LucentUI##LucentUI_Options", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.hud = hud;
        Size = new Vector2(860, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(700, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void OnClose() => Plugin.SaveNow();

    public override void PreDraw()
    {
        var accent = Plugin.Profiles.Current.Style.Accent;
        Ui.Accent = accent;

        PushColor(ImGuiCol.WindowBg, new Vector4(0.075f, 0.075f, 0.085f, 0.98f));
        PushColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
        PushColor(ImGuiCol.TitleBg, new Vector4(0.06f, 0.06f, 0.07f, 1f));
        PushColor(ImGuiCol.TitleBgActive, new Vector4(0.06f, 0.06f, 0.07f, 1f));
        PushColor(ImGuiCol.Border, new Vector4(1, 1, 1, 0.07f));
        PushColor(ImGuiCol.FrameBg, new Vector4(1, 1, 1, 0.06f));
        PushColor(ImGuiCol.FrameBgHovered, new Vector4(1, 1, 1, 0.10f));
        PushColor(ImGuiCol.FrameBgActive, new Vector4(1, 1, 1, 0.14f));
        PushColor(ImGuiCol.Button, new Vector4(1, 1, 1, 0.07f));
        PushColor(ImGuiCol.ButtonHovered, Colors.WithAlpha(accent, 0.45f));
        PushColor(ImGuiCol.ButtonActive, Colors.WithAlpha(accent, 0.65f));
        PushColor(ImGuiCol.Header, Colors.WithAlpha(accent, 0.22f));
        PushColor(ImGuiCol.HeaderHovered, Colors.WithAlpha(accent, 0.30f));
        PushColor(ImGuiCol.HeaderActive, Colors.WithAlpha(accent, 0.40f));
        PushColor(ImGuiCol.CheckMark, accent);
        PushColor(ImGuiCol.SliderGrab, accent);
        PushColor(ImGuiCol.SliderGrabActive, Colors.Lighten(accent, 0.2f));
        PushColor(ImGuiCol.Separator, new Vector4(1, 1, 1, 0.07f));
        PushColor(ImGuiCol.PopupBg, new Vector4(0.09f, 0.09f, 0.10f, 0.98f));

        PushVar(ImGuiStyleVar.WindowRounding, 8f);
        PushVar(ImGuiStyleVar.FrameRounding, 4f);
        PushVar(ImGuiStyleVar.ChildRounding, 6f);
        PushVar(ImGuiStyleVar.PopupRounding, 6f);
        PushVar(ImGuiStyleVar.GrabRounding, 4f);
        PushVar(ImGuiStyleVar.FramePadding, new Vector2(8, 5));
        PushVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 7));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(pushedVars);
        ImGui.PopStyleColor(pushedColors);
        pushedVars = 0;
        pushedColors = 0;
    }

    public void Open(bool general = false)
    {
        IsOpen = true;
        if (general)
        {
            page = Page.General;
        }
    }

    public override void Draw()
    {
        var sidebarWidth = 190f;

        using (var sidebar = ImRaii.Child("##sidebar", new Vector2(sidebarWidth, 0), false))
        {
            if (sidebar.Success)
            {
                DrawSidebar();
            }
        }

        ImGui.SameLine();

        using var content = ImRaii.Child("##content", Vector2.Zero, false);
        if (!content.Success)
        {
            return;
        }

        Ui.Filter = search.Trim();
        if (Ui.Searching)
        {
            DrawSearchResults();
        }
        else
        {
            DrawPage(page);
        }

        Ui.EndCard();
        Ui.Filter = string.Empty;
    }

    // ── Sidebar ──────────────────────────────────────────────────────────────

    private void DrawSidebar()
    {
        var accent = Plugin.Profiles.Current.Style.Accent;

        ImGui.SetWindowFontScale(1.5f);
        ImGui.TextColored(accent, "LUCENT");
        ImGui.SameLine(0, 4);
        ImGui.TextUnformatted("UI");
        ImGui.SetWindowFontScale(1f);
        ImGui.TextDisabled($"Profile: {Plugin.Profiles.CurrentName}");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search settings...", ref search, 64);
        ImGui.Spacing();

        var unlock = hud.Unlock.Active;
        ImGui.PushStyleColor(ImGuiCol.Button, Colors.Darken(accent, unlock ? 0.1f : 0.45f));
        if (ImGui.Button(unlock ? "Lock Elements" : "Unlock Elements", new Vector2(-1, 30)))
        {
            hud.Unlock.Toggle();
        }

        ImGui.PopStyleColor();
        ImGui.Spacing();

        foreach (var (group, items) in Navigation)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(group);
            foreach (var (target, label) in items)
            {
                if (ImGui.Selectable($"  {label}##nav", page == target && search.Length == 0, ImGuiSelectableFlags.None, new Vector2(0, 22)))
                {
                    page = target;
                    search = string.Empty;
                }
            }
        }
    }

    // ── Pages ────────────────────────────────────────────────────────────────

    private void DrawSearchResults()
    {
        Ui.AnyVisible = false;
        foreach (var p in SearchablePages)
        {
            ImGui.PushID((int)p);
            DrawPage(p);
            Ui.EndCard();
            ImGui.PopID();
        }

        if (!Ui.AnyVisible)
        {
            ImGui.TextDisabled($"No settings match \"{Ui.Filter}\".");
        }
    }

    private void DrawPage(Page p)
    {
        var profile = Plugin.Profiles.Current;
        switch (p)
        {
            case Page.General:
                DrawGeneral(profile);
                break;
            case Page.Style:
                DrawStyle(profile.Style);
                break;
            case Page.Player:
                DrawUnitFrame("Player", "Your own health, mana and shield.", profile.Player, true);
                break;
            case Page.Target:
                DrawUnitFrame("Target", "Your current (or soft) target.", profile.Target, false);
                break;
            case Page.TargetOfTarget:
                DrawUnitFrame("Target of Target", "Whoever your target is targeting.", profile.TargetOfTarget, false);
                break;
            case Page.Focus:
                DrawUnitFrame("Focus", "Your focus target.", profile.Focus, false);
                break;
            case Page.Party:
                DrawParty(profile.Party);
                break;
            case Page.CastBars:
                DrawCastBars(profile);
                break;
            case Page.Auras:
                DrawAuras(profile);
                break;
            case Page.JobGauge:
                DrawJobGauge(profile.JobGauge);
                break;
            case Page.CooldownManager:
                DrawCooldownManager(profile.CooldownManager);
                break;
            case Page.PartyCooldowns:
                DrawPartyCooldowns(profile.PartyCooldowns);
                break;
            case Page.Nameplates:
                DrawNameplates(profile.Nameplates);
                break;
            case Page.EnemyList:
                DrawEnemyList(profile.EnemyList);
                break;
            case Page.Extras:
                DrawExtras(profile);
                break;
            case Page.Hotbars:
                DrawHotbars(profile.Hotbars);
                break;
            case Page.DefaultHud:
                DrawDefaultHud(profile.DefaultHud);
                break;
            case Page.Profiles:
                DrawProfiles();
                break;
            case Page.About:
                DrawAbout();
                break;
        }
    }

    private void DrawGeneral(Profile profile)
    {
        var config = Plugin.Config;
        var general = config.General;

        Ui.PageHeader("General", "Modules and behaviour that apply everywhere.");

        if (config.FirstRun && !Ui.Searching)
        {
            DrawWelcome(profile);
        }

        Ui.Card("Modules", "Turn whole modules on or off. A disabled module costs nothing.");
        Ui.Toggle("Unit Frames", ref profile.Modules.UnitFrames);
        Ui.Toggle("Cast Bars", ref profile.Modules.CastBars);
        Ui.Toggle("Auras", ref profile.Modules.Auras);
        Ui.Toggle("Party Frames", ref profile.Modules.PartyFrames);
        Ui.Toggle("Job Gauges", ref profile.Modules.JobGauges);
        Ui.Toggle("Cooldown Manager", ref profile.Modules.CooldownManager, "Your own cooldowns and buffs as icon bars.");
        Ui.Toggle("Party Cooldowns", ref profile.Modules.PartyCooldowns);
        Ui.Toggle("Enemy List", ref profile.Modules.EnemyList);
        Ui.Toggle("Nameplates", ref profile.Modules.Nameplates, "Health bars above enemies (and optionally players). Off by default.");
        Ui.Toggle("Extras", ref profile.Modules.Extras, "GCD, MP ticker, limit break, pull timer and experience bar.");
        Ui.Toggle("Hotbar rules", ref profile.Modules.Hotbars, "Show or hide the game's hotbars by combat state. Off by default.");

        Ui.Card("Display");
        Ui.Toggle("Show HUD", ref general.ShowHud, "Master switch. Also available as /lucent toggle.");
        Ui.Toggle("Hide when game UI is hidden", ref general.HideWhenGameUiHidden, "Follows the game's hide-UI key (Scroll Lock by default).");
        Ui.Toggle("Smooth fades", ref general.SmoothFades, "Fade elements in and out when their visibility rules change.");
        Ui.Toggle("Stay behind game windows", ref general.ClipBehindWindows, "Cut LucentUI away where a game window (inventory, character...) is open on top of it, and let clicks there reach the window.");
        Ui.Enum("Behind windows", ref general.ClipMode, "Clip cuts the covered part away; Hide hides any element a window touches.");
        if (Ui.Button("Layout", hud.Unlock.Active ? "Lock elements" : "Unlock elements to move them"))
        {
            hud.Unlock.Toggle();
        }

        Ui.Card("Mouse");
        Ui.Toggle("Click to target", ref general.ClickToTarget);
        Ui.Toggle("Right-click menu", ref general.RightClickMenu, "Right-click a frame to open the game's context menu.");
        Ui.Toggle("Mouseover target", ref general.MouseoverTarget, "Hovering a frame sets <mo> for mouseover macros.");
    }

    private static void DrawWelcome(Profile profile)
    {
        Ui.Card("Welcome to LucentUI", "LucentUI draws its own frames on top of the game's HUD. Do you want to hide the parts of the default HUD that LucentUI replaces? You can change this any time under Default HUD.");
        if (Ui.Button("Default HUD", "Hide the replaced default HUD"))
        {
            var hud = profile.DefaultHud;
            hud.HidePlayer = hud.HideTarget = hud.HideFocus = hud.HideCastBar = hud.HideParty = hud.HideStatus = true;
            Plugin.Config.FirstRun = false;
            Plugin.MarkDirty();
        }

        if (Ui.Button("Keep it", "Keep the default HUD for now"))
        {
            Plugin.Config.FirstRun = false;
            Plugin.MarkDirty();
        }
    }

    private static void DrawStyle(StyleSettings s)
    {
        Ui.PageHeader("Style", "One place for the look of every element.");

        Ui.Card("Theme");
        Ui.Color("Accent", ref s.Accent, "Used for highlights, the settings window and Unlock Mode.");
        Ui.Color("Background", ref s.Background);
        Ui.Color("Border", ref s.Border);
        Ui.Slider("Border size", ref s.BorderSize, 0, 4);
        Ui.Slider("Corner rounding", ref s.Rounding, 0f, 12f, "%.0f", "Rounded corners turn off the bar gradient.");
        Ui.Enum("Bar style", ref s.BarStyle, "Rounded corners always draw flat bars.");
        Ui.Percent("Style strength", ref s.GradientStrength, "How strong the gradient, gloss or stripes are.");
        Ui.Slider("Shadow size", ref s.ShadowSize, 0, 8, "A soft drop shadow behind every frame. 0 turns it off.");
        Ui.Color("Shadow colour", ref s.ShadowColor);
        Ui.Toggle("Smooth bars", ref s.SmoothBars, "Health bars slide to their new value instead of jumping.");

        Ui.Card("Text");
        Ui.Enum("Font", ref s.Font, "The game's own fonts. TrumpGothic and Jupiter are condensed, MiedingerMid is the number font.");
        Ui.Slider("Font size", ref s.FontSize, 8, 40);
        Ui.Slider("Small font size", ref s.SmallFontSize, 8, 40, "Used for aura timers.");
        Ui.Toggle("Text outline", ref s.TextOutline);
        Ui.Toggle("Short numbers", ref s.AbbreviateNumbers, "Show 45.2k instead of 45,213.");

        Ui.Card("Health Colours");
        Ui.Enum("Colour by", ref s.HealthColors, "Frames set to Inherit use this.");
        Ui.Color("Flat colour", ref s.FlatHealth);
        Ui.Color("Dark mode fill", ref s.DarkFill);
        Ui.Color("Hostile", ref s.Hostile);
        Ui.Color("Neutral", ref s.Neutral);
        Ui.Color("Friendly NPC", ref s.Friendly);
        Ui.Color("Dead", ref s.Dead);
        Ui.Color("Shield", ref s.Shield);
        Ui.Percent("Out of range opacity", ref s.OutOfRangeAlpha);

        Ui.Card("Resource Colours");
        Ui.Color("Mana", ref s.Mana);
        Ui.Color("GP", ref s.Gp);
        Ui.Color("CP", ref s.Cp);

        Ui.Card("Cast Colours");
        Ui.Color("Cast", ref s.Cast);
        Ui.Color("Interruptible", ref s.CastInterruptible);
        Ui.Color("Slidecast window", ref s.Slidecast);
    }

    private static void DrawUnitFrame(string title, string subtitle, UnitFrameSettings s, bool isPlayer)
    {
        Ui.PageHeader(title, subtitle);

        Ui.Card(title + " Frame");
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Size("Size", ref s.Size, "Width and height in pixels.");
        Ui.Enum("Health colour", ref s.ColorMode);
        Ui.Toggle("Show shield", ref s.ShowShield);
        Ui.Toggle("Fill right to left", ref s.ReverseFill);
        if (!isPlayer)
        {
            Ui.Toggle("Clickable", ref s.Interactive, "Click to target, right-click menu and mouseover.");
        }

        Ui.Card(title + " Power", "Mana, GP or CP, shown under the health bar.");
        Ui.Toggle("Show power bar", ref s.ShowPower);
        Ui.Slider("Power bar height", ref s.PowerHeight, 1, 30);

        Ui.Card(title + " Text", TextTags.Help);
        Ui.Text("Left text", ref s.LeftText);
        Ui.Text("Centre text", ref s.CenterText);
        Ui.Text("Right text", ref s.RightText);

        DrawVisibility(title, s.Visibility);
    }

    private static void DrawVisibility(string title, VisibilitySettings v)
    {
        Ui.Card(title + " Visibility");
        Ui.Enum("Show", ref v.Mode);
        Ui.Percent("Opacity when hidden", ref v.HiddenAlpha, "0% hides it completely; higher values fade it instead.");
    }

    private static void DrawParty(PartySettings s)
    {
        Ui.PageHeader("Party", "Party frames with role sorting and range fading.");

        Ui.Card("Party Frames");
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Size("Member size", ref s.Size);
        Ui.Slider("Spacing", ref s.Spacing, 0, 30);
        Ui.Toggle("Horizontal", ref s.Horizontal);
        Ui.Enum("Sort by", ref s.Sort);
        Ui.Toggle("Include yourself", ref s.IncludePlayer);
        Ui.Toggle("Show when solo", ref s.ShowWhenSolo);

        Ui.Card("Party Health");
        Ui.Enum("Health colour", ref s.ColorMode);
        Ui.Toggle("Show shield", ref s.ShowShield);
        Ui.Toggle("Fade out of range", ref s.RangeFade, "Members further than 30 yalms fade out.");
        Ui.Toggle("Highlight target", ref s.HighlightTarget);
        Ui.Toggle("Clickable", ref s.Interactive);
        Ui.Toggle("Show power bar", ref s.ShowPower);
        Ui.Slider("Power bar height", ref s.PowerHeight, 1, 20);
        Ui.Toggle("Job icon", ref s.ShowJobIcon);

        Ui.Card("Party Trackers");
        Ui.Toggle("Raise", ref s.ShowRaise, "Shows when a dead member has a raise waiting or one is being cast on them.");
        Ui.Toggle("Invulnerability", ref s.ShowInvulnerability, "Hallowed Ground, Holmgang, Living Dead and Superbolide.");
        Ui.Toggle("Cleansable debuffs", ref s.ShowCleansable, "Outlines members with a debuff Esuna can remove.");
        Ui.Toggle("Only if I can cleanse", ref s.CleansableOnlyIfICanCleanse, "Healers and bards.");
        Ui.Color("Cleansable colour", ref s.CleansableColor);
        Ui.Toggle("Ready check", ref s.ShowReadyCheck);
        Ui.Toggle("Debuff icons", ref s.ShowDebuffs);
        Ui.Slider("Debuffs per member", ref s.MaxDebuffs, 1, 8);
        Ui.Slider("Debuff icon height", ref s.DebuffIconHeight, 10f, 64f, "%.0f");

        Ui.Card("Party Text", TextTags.Help);
        Ui.Text("Left text", ref s.LeftText);
        Ui.Text("Right text", ref s.RightText);

        DrawVisibility("Party", s.Visibility);
    }

    private static void DrawCastBars(Profile profile)
    {
        Ui.PageHeader("Cast Bars", "Player, target and focus cast bars.");
        DrawCastBar("Player Cast Bar", profile.PlayerCast, true);
        DrawCastBar("Target Cast Bar", profile.TargetCast, false);
        DrawCastBar("Focus Cast Bar", profile.FocusCast, false);
    }

    private static void DrawCastBar(string title, CastBarSettings s, bool isPlayer)
    {
        Ui.Card(title);
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Size("Size", ref s.Size);
        Ui.Toggle("Show icon", ref s.ShowIcon);
        Ui.Toggle("Show spell name", ref s.ShowName);
        Ui.Toggle("Show timer", ref s.ShowTimer);
        if (isPlayer)
        {
            Ui.Toggle("Show slidecast window", ref s.ShowSlidecast, "Marks the end of the cast where you can already move.");
            Ui.Slider("Slidecast window", ref s.SlidecastWindow, 0f, 1.5f, "%.2fs");
        }
    }

    private static void DrawAuras(Profile profile)
    {
        Ui.PageHeader("Auras", "Buffs and debuffs as icon grids with timers.");
        DrawAura("Player Buffs", profile.PlayerBuffs, true);
        DrawAura("Player Debuffs", profile.PlayerDebuffs, false);
        DrawAura("Target Auras", profile.TargetAuras, false);
    }

    private static void DrawAura(string title, AuraSettings s, bool canRemove)
    {
        Ui.Card(title);
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Slider("Icon height", ref s.IconHeight, 12f, 96f, "%.0f");
        Ui.Slider("Spacing", ref s.Spacing, 0, 20);
        Ui.Slider("Icons per row", ref s.PerRow, 1, 30);
        Ui.Slider("Maximum icons", ref s.MaxIcons, 1, 60);
        Ui.Toggle("Grow to the left", ref s.GrowLeft);
        Ui.Toggle("Grow upwards", ref s.GrowUp);
        Ui.Toggle("Show buffs", ref s.ShowBuffs);
        Ui.Toggle("Show debuffs", ref s.ShowDebuffs);
        Ui.Toggle("Only mine", ref s.OnlyMine, "Only statuses you applied.");
        Ui.Toggle("Show permanent", ref s.ShowPermanent, "Statuses without a timer, like stances.");
        Ui.Toggle("Show timer", ref s.ShowTimer);
        Ui.Toggle("Sort by time left", ref s.SortByTime);
        Ui.Toggle("Tooltips", ref s.Tooltips);
        if (canRemove)
        {
            Ui.Toggle("Right-click to remove", ref s.RightClickToRemove, "Right-click a buff to click it off, like the default status bar.");
        }
    }

    private static void DrawDefaultHud(DefaultHudSettings s)
    {
        Ui.PageHeader("Default HUD", "Hide the game's own elements that LucentUI replaces.");

        Ui.Card("Hide Game Elements", "An element is only hidden while its LucentUI replacement is turned on, and everything comes back when the plugin unloads.");
        Ui.Toggle("Player HP/MP bars", ref s.HidePlayer);
        Ui.Toggle("Target info", ref s.HideTarget);
        Ui.Toggle("Focus target info", ref s.HideFocus);
        Ui.Toggle("Cast bar", ref s.HideCastBar);
        Ui.Toggle("Party list", ref s.HideParty);
        Ui.Toggle("Status effects", ref s.HideStatus, "Hides all of the game's status windows (buffs, debuffs and others).");
        Ui.Toggle("Job gauge", ref s.HideJobGauge);
        Ui.Toggle("Enemy list", ref s.HideEnemyList);
        Ui.Toggle("Limit break", ref s.HideLimitBreak);
        Ui.Toggle("Experience bar", ref s.HideExp);
        Ui.Toggle("Countdown", ref s.HidePullTimer);
    }

    // ── Profiles ─────────────────────────────────────────────────────────────

    private void DrawProfiles()
    {
        var config = Plugin.Config;
        var profiles = Plugin.Profiles;

        Ui.PageHeader("Profiles", "Save complete setups and switch between them, per job if you like.");

        Ui.Card("Profiles");
        foreach (var name in config.Profiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (Ui.Button(name == profiles.CurrentName ? $"{name}  (active)" : name, name == profiles.CurrentName ? "Active" : "Switch to this profile"))
            {
                profiles.SetActive(name);
            }
        }

        Ui.Card("Manage Active Profile", $"These actions apply to \"{profiles.CurrentName}\".");
        Ui.Text("New profile name", ref newProfileName, hint: "e.g. Raiding");
        if (Ui.Button("Create", "Create from defaults"))
        {
            Report(profiles.Create(newProfileName), "Created " + newProfileName, "Pick a new, unused name.");
        }

        if (Ui.Button("Duplicate", "Copy the active profile"))
        {
            Report(profiles.Create(newProfileName, profiles.Current), "Copied to " + newProfileName, "Pick a new, unused name.");
        }

        Ui.Text("Rename to", ref renameTo);
        if (Ui.Button("Rename", "Rename the active profile"))
        {
            Report(profiles.Rename(profiles.CurrentName, renameTo), "Renamed", "Pick a new, unused name.");
        }

        var ctrl = ImGui.GetIO().KeyCtrl;
        if (Ui.Button("Reset", ctrl ? "Reset to defaults" : "Hold Ctrl to reset") && ctrl)
        {
            profiles.ResetCurrent();
            Report(true, "Profile reset", string.Empty);
        }

        if (Ui.Button("Delete", ctrl ? "Delete this profile" : "Hold Ctrl to delete") && ctrl)
        {
            Report(profiles.Delete(profiles.CurrentName), "Profile deleted", "You cannot delete your last profile.");
        }

        Ui.Card("Share", "Export copies the active profile to your clipboard as text. Import reads one from your clipboard.");
        if (Ui.Button("Export", "Copy to clipboard"))
        {
            ImGui.SetClipboardText(profiles.Export(profiles.CurrentName));
            Report(true, "Copied to clipboard", string.Empty);
        }

        Ui.Text("Imported profile name", ref importName, hint: "Name for the imported profile");
        if (Ui.Button("Import", "Import from clipboard"))
        {
            var clipboard = ImGui.GetClipboardText() ?? string.Empty;
            if (EllesmereImport.LooksLike(clipboard))
            {
                ImportEllesmere(clipboard);
            }
            else
            {
                var ok = profiles.TryImport(clipboard, importName, out var error);
                Report(ok, "Imported " + importName, error);
            }
        }

        Ui.Card("Import from EllesmereUI", "Copy a profile string from EllesmereUI in WoW (it starts with !EUI_), enter a name above, then import. Frame sizes, positions, text, colours, cast bars, auras and party frames are converted; WoW-only modules are skipped.");
        if (Ui.Button("EllesmereUI", "Import EllesmereUI profile from clipboard"))
        {
            ImportEllesmere(ImGui.GetClipboardText() ?? string.Empty);
        }

        Ui.EndCard();
        if (profileMessage.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Ui.Accent);
            ImGui.TextWrapped(profileMessage);
            ImGui.PopStyleColor();
        }

        DrawJobProfiles();
    }

    private void Report(bool ok, string success, string failure)
    {
        profileMessage = ok ? success : failure;
    }

    private static void DrawJobProfiles()
    {
        var config = Plugin.Config;
        var names = config.Profiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

        Ui.Card("Per-Job Profiles", "Pick a profile for each job. It loads automatically when you change job.");
        Ui.Toggle("Switch automatically", ref config.AutoSwitchByJob);

        foreach (var job in Jobs.CombatJobs)
        {
            var info = Jobs.Get(job);
            config.JobProfiles.TryGetValue(job, out var current);
            var label = info.Abbreviation;

            // Rendered as a combo row; Ui.Button rows are not flexible enough for a dropdown.
            var selectedLabel = current ?? "(no change)";
            if (Ui.Button(label, selectedLabel))
            {
                ImGui.OpenPopup("jobprofile_" + job);
            }

            if (ImGui.BeginPopup("jobprofile_" + job))
            {
                if (ImGui.Selectable("(no change)", current == null))
                {
                    config.JobProfiles.Remove(job);
                    Plugin.MarkDirty();
                }

                foreach (var name in names)
                {
                    if (ImGui.Selectable(name, current == name))
                    {
                        config.JobProfiles[job] = name;
                        Plugin.MarkDirty();
                    }
                }

                ImGui.EndPopup();
            }
        }
    }

    private static void DrawAbout()
    {
        Ui.PageHeader("About", "LucentUI, a modern HUD replacement for FINAL FANTASY XIV.");

        Ui.Card("Commands");
        Ui.Button("/lucent", "Open or close this window");
        Ui.Button("/lucent unlock", "Toggle Unlock Mode");
        Ui.Button("/lucent toggle", "Show or hide the HUD");
        Ui.Button("/lucent profile <name>", "Switch profile");

        Ui.Card("Design", "Inspired by EllesmereUI for World of Warcraft: modules cost nothing while disabled, nothing changes your game until you opt in, and every element is positioned in one Unlock Mode and styled from one place.");
        Ui.Button("Version", Svc.PluginInterface.Manifest.AssemblyVersion.ToString());
    }

    private void PushColor(ImGuiCol col, Vector4 value)
    {
        ImGui.PushStyleColor(col, value);
        pushedColors++;
    }

    private void PushVar(ImGuiStyleVar var, float value)
    {
        ImGui.PushStyleVar(var, value);
        pushedVars++;
    }

    private void PushVar(ImGuiStyleVar var, Vector2 value)
    {
        ImGui.PushStyleVar(var, value);
        pushedVars++;
    }
}
