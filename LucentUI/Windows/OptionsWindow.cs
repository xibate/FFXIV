using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
        Alliance,
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
        ("FRAMES", new[] { (Page.Player, "Player"), (Page.Target, "Target"), (Page.TargetOfTarget, "Target of Target"), (Page.Focus, "Focus"), (Page.Party, "Party"), (Page.Alliance, "Alliance"), (Page.EnemyList, "Enemy List"), (Page.Nameplates, "Nameplates") }),
        ("COMBAT", new[] { (Page.JobGauge, "Job Gauge"), (Page.CooldownManager, "Cooldown Manager"), (Page.CastBars, "Cast Bars"), (Page.Auras, "Auras"), (Page.PartyCooldowns, "Party Cooldowns"), (Page.Extras, "Extras") }),
        ("SYSTEM", new[] { (Page.Hotbars, "Hotbars"), (Page.DefaultHud, "Default HUD"), (Page.Profiles, "Profiles"), (Page.About, "About") }),
    };

    // Pages included in search results (the custom ones are left out).
    private static readonly Page[] SearchablePages =
    {
        Page.General, Page.Style, Page.Player, Page.Target, Page.TargetOfTarget, Page.Focus,
        Page.Party, Page.Alliance, Page.EnemyList, Page.Nameplates, Page.JobGauge, Page.CooldownManager, Page.CastBars, Page.Auras,
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

    private static HudRenderer? previewHud;

    public OptionsWindow(HudRenderer hud)
        : base("LucentUI##LucentUI_Options", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.hud = hud;
        previewHud = hud;
        Size = new Vector2(1020, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(700, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void OnClose()
    {
        hud.SettingsPreview = false;
        Plugin.SaveNow();
    }

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

    /// <summary>Opens the settings page (and tab) of one HUD element, e.g. from Unlock Mode.</summary>
    public void OpenFor(string elementId)
    {
        IsOpen = true;
        search = string.Empty;
        (page, var tabId, var tab) = elementId switch
        {
            "unit.player" => (Page.Player, null, 0),
            "unit.target" => (Page.Target, null, 0),
            "unit.tot" => (Page.TargetOfTarget, null, 0),
            "unit.focus" => (Page.Focus, null, 0),
            "party" => (Page.Party, null, 0),
            "alliance" => (Page.Alliance, null, 0),
            "cast.player" => (Page.CastBars, "cast", 0),
            "cast.target" => (Page.CastBars, "cast", 1),
            "cast.focus" => (Page.CastBars, "cast", 2),
            "auras.player.buffs" => (Page.Auras, "auras", 0),
            "auras.player.debuffs" => (Page.Auras, "auras", 1),
            "auras.target" => (Page.Auras, "auras", 2),
            "cdm.cooldowns" => (Page.CooldownManager, "cdm", 0),
            "cdm.utility" => (Page.CooldownManager, "cdm", 1),
            "cdm.buffs" => (Page.CooldownManager, "cdm", 2),
            _ when elementId.StartsWith("cdm.custom", StringComparison.Ordinal) && int.TryParse(elementId["cdm.custom".Length..], out var n) => OpenCustomBar(n - 1),
            "partycooldowns" => (Page.PartyCooldowns, null, 0),
            "enemylist" => (Page.EnemyList, null, 0),
            "nameplates" => (Page.Nameplates, null, 0),
            _ when elementId == "jobgauge" || elementId.StartsWith("gauge.", StringComparison.Ordinal) => (Page.JobGauge, (string?)"jobgauge", elementId == "jobgauge" ? 0 : 1),
            _ => (Page.Extras, null, 0),
        };

        if (tabId != null)
        {
            Ui.OpenTab(tabId, tab);
        }
    }

    private static (Page, string?, int) OpenCustomBar(int index)
    {
        Ui.OpenTab("cdmcustom", index);
        return (Page.CooldownManager, "cdm", 3);
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
        hud.SettingsPreview = Plugin.Config.PreviewWhileEditing;
        // Scales with the font, so larger fonts still fit.
        var sidebarWidth = MathF.Max(210f, ImGui.GetFontSize() * 12f);

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

    // Sidebar icon per page.
    private static readonly Dictionary<Page, FontAwesomeIcon> PageIcons = new()
    {
        [Page.General] = FontAwesomeIcon.Cog,
        [Page.Style] = FontAwesomeIcon.PaintBrush,
        [Page.Player] = FontAwesomeIcon.User,
        [Page.Target] = FontAwesomeIcon.Crosshairs,
        [Page.TargetOfTarget] = FontAwesomeIcon.ArrowRight,
        [Page.Focus] = FontAwesomeIcon.Eye,
        [Page.Party] = FontAwesomeIcon.Users,
        [Page.Alliance] = FontAwesomeIcon.LayerGroup,
        [Page.EnemyList] = FontAwesomeIcon.ListUl,
        [Page.Nameplates] = FontAwesomeIcon.Tag,
        [Page.JobGauge] = FontAwesomeIcon.ChartBar,
        [Page.CooldownManager] = FontAwesomeIcon.Stopwatch,
        [Page.CastBars] = FontAwesomeIcon.Magic,
        [Page.Auras] = FontAwesomeIcon.Star,
        [Page.PartyCooldowns] = FontAwesomeIcon.History,
        [Page.Extras] = FontAwesomeIcon.PuzzlePiece,
        [Page.Hotbars] = FontAwesomeIcon.Th,
        [Page.DefaultHud] = FontAwesomeIcon.EyeSlash,
        [Page.Profiles] = FontAwesomeIcon.Copy,
        [Page.About] = FontAwesomeIcon.InfoCircle,
    };

    /// <summary>Whether the module behind a page is on (null for pages without a module).</summary>
    private static bool? ModuleOn(Page p, Profile profile) => p switch
    {
        Page.Player or Page.Target or Page.TargetOfTarget or Page.Focus => profile.Modules.UnitFrames,
        Page.Party => profile.Modules.PartyFrames,
        Page.Alliance => profile.Modules.Alliance,
        Page.EnemyList => profile.Modules.EnemyList,
        Page.Nameplates => profile.Modules.Nameplates,
        Page.JobGauge => profile.Modules.JobGauges,
        Page.CooldownManager => profile.Modules.CooldownManager,
        Page.CastBars => profile.Modules.CastBars,
        Page.Auras => profile.Modules.Auras,
        Page.PartyCooldowns => profile.Modules.PartyCooldowns,
        Page.Extras => profile.Modules.Extras,
        Page.Hotbars => profile.Modules.Hotbars,
        _ => null,
    };

    private readonly HashSet<string> collapsedGroups = new();

    private void DrawSidebar()
    {
        var profile = Plugin.Profiles.Current;
        var accent = profile.Style.Accent;

        ImGui.SetWindowFontScale(1.5f);
        ImGui.TextColored(accent, "LUCENT");
        ImGui.SameLine(0, 4);
        ImGui.TextUnformatted("UI");
        ImGui.SetWindowFontScale(1f);

        // Profile switcher, like DelvUI's.
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##profile", Plugin.Profiles.CurrentName))
        {
            foreach (var name in Plugin.Config.Profiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (ImGui.Selectable(name, name == Plugin.Profiles.CurrentName))
                {
                    Plugin.Profiles.SetActive(name);
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Active profile. Manage profiles on the Profiles page.");
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search settings...", ref search, 64);
        ImGui.Spacing();

        var unlock = hud.Unlock.Active;
        ImGui.PushStyleColor(ImGuiCol.Button, Colors.Darken(accent, unlock ? 0.1f : 0.45f));
        if (ImGui.Button(unlock ? "Lock Elements" : "Unlock Elements", new Vector2(-1, ImGui.GetFrameHeight() + 8f)))
        {
            hud.Unlock.Toggle();
        }

        ImGui.PopStyleColor();
        if (Ui.Switch("##previewframes", ref Plugin.Config.PreviewWhileEditing))
        {
            Plugin.MarkDirty();
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Preview frames");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("While this window is open, show every element with sample data (a target, casts, a party) so you can see your changes.");
        }

        ImGui.Spacing();
        var dl = ImGui.GetWindowDrawList();
        foreach (var (group, items) in Navigation)
        {
            // Group header: click to fold it away.
            ImGui.Spacing();
            var folded = collapsedGroups.Contains(group);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 1, 0.45f));
            if (ImGui.Selectable($"{(folded ? "+" : "-")}  {group}##group", false, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetTextLineHeight() + 6f)))
            {
                if (!collapsedGroups.Add(group))
                {
                    collapsedGroups.Remove(group);
                }
            }

            ImGui.PopStyleColor();
            if (folded)
            {
                continue;
            }

            foreach (var (target, label) in items)
            {
                var selected = page == target && search.Length == 0;
                var rowHeight = MathF.Max(32f, ImGui.GetTextLineHeight() + 16f);
                var iconColumn = MathF.Max(34f, ImGui.GetFontSize() * 2.2f);
                var rowStart = ImGui.GetCursorScreenPos();
                var rowWidth = ImGui.GetContentRegionAvail().X;
                if (ImGui.Selectable($"##nav{target}", selected, ImGuiSelectableFlags.None, new Vector2(0, rowHeight)))
                {
                    page = target;
                    search = string.Empty;
                }

                // Accent bar on the open page, an icon, the name and the module's on/off dot.
                if (selected)
                {
                    dl.AddRectFilled(rowStart, rowStart + new Vector2(3, rowHeight), Colors.ToU32(accent));
                }

                var textY = rowStart.Y + (rowHeight - ImGui.GetTextLineHeight()) / 2f;
                if (PageIcons.TryGetValue(target, out var icon))
                {
                    using (Svc.PluginInterface.UiBuilder.IconFontHandle.Push())
                    {
                        var iconText = icon.ToIconString();
                        var iconSize = ImGui.CalcTextSize(iconText);
                        dl.AddText(new Vector2(rowStart.X + 8 + (iconColumn - 8 - iconSize.X) / 2f, rowStart.Y + (rowHeight - iconSize.Y) / 2f), Colors.ToU32(selected ? accent : new Vector4(1, 1, 1, 0.6f)), iconText);
                    }
                }

                // The name is clipped before the module dot so long names never run into it.
                var textMin = new Vector2(rowStart.X + iconColumn + 4, rowStart.Y);
                var textMax = new Vector2(rowStart.X + rowWidth - 20, rowStart.Y + rowHeight);
                dl.PushClipRect(textMin, textMax, true);
                dl.AddText(new Vector2(textMin.X, textY), Colors.ToU32(selected ? Vector4.One : new Vector4(1, 1, 1, 0.85f)), label);
                dl.PopClipRect();
                if (ModuleOn(target, profile) is { } on)
                {
                    var dot = new Vector2(rowStart.X + rowWidth - 10, rowStart.Y + rowHeight / 2f);
                    dl.AddCircleFilled(dot, 3.5f, Colors.ToU32(on ? Colors.Rgb(0x58D27A) : new Vector4(1, 1, 1, 0.18f)));
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
            case Page.Alliance:
                DrawAlliance(profile.Alliance);
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
        Ui.Toggle("Alliance Frames", ref profile.Modules.Alliance, "The two other parties in 24-player alliance raids.");
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
        Ui.Toggle("Mouseover casting", ref general.MouseoverCasting, "While hovering a frame, your actions go to that unit (heals to the party member, attacks to the enemy). No macros needed.");
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

    private void DrawStyle(StyleSettings s)
    {
        Ui.PageHeader("Style", "One place for the look of every element.");
        CopyAndReset("style", s, () => new Profile().Style);
        var tab = Ui.Tabs("style", "Look", "Text", "Colours", "Media");

        if (Ui.Tab(tab, 0))
        {
            Ui.Card("Theme");
            Ui.Color("Accent", ref s.Accent, "Used for highlights, the settings window and Unlock Mode.");
            Ui.Color("Background", ref s.Background);
            Ui.Color("Border", ref s.Border);
            Ui.Slider("Border size", ref s.BorderSize, 0, 4);
            Ui.Slider("Corner rounding", ref s.Rounding, 0f, 12f, "%.0f", "Rounded corners turn off the bar gradient.");
            Ui.Slider("Shadow size", ref s.ShadowSize, 0, 8, "A soft drop shadow behind every frame. 0 turns it off.");
            if (Ui.Enum("Status icon shape", ref s.IconShape, "Buff and debuff icons everywhere: Portrait is the game's default 3:4 icon, Square crops it like WoW's, Round crops it to a circle. Elements set to Style Default follow this.") && s.IconShape == IconShape.StyleDefault)
            {
                s.IconShape = IconShape.Portrait;
            }
            Ui.Color("Shadow colour", ref s.ShadowColor);

            Ui.Card("Bars");
            Ui.MediaPicker("Bar texture", ref s.BarTexture, false, "None (use bar style)", "A status bar texture from your media folders, tinted with each bar's colour. Elements can pick their own.");
            Ui.Enum("Bar style", ref s.BarStyle, "Used when no bar texture is picked. Rounded corners always draw flat bars.");
            Ui.Percent("Style strength", ref s.GradientStrength, "How strong the gradient, gloss or stripes are.");
            Ui.Toggle("Smooth bars", ref s.SmoothBars, "Health bars slide to their new value instead of jumping.");
        }

        if (Ui.Tab(tab, 1))
        {
            Ui.Card("Text");
            Ui.MediaPicker("Font", ref s.FontName, true, "Game font: " + Ui.PrettyName(s.Font), "A .ttf or .otf font from your media folders. Empty uses the game font below.");
            if (string.IsNullOrEmpty(s.FontName))
            {
                Ui.Enum("Game font", ref s.Font, "TrumpGothic and Jupiter are condensed, MiedingerMid is the number font.");
            }

            Ui.Slider("Font size", ref s.FontSize, 8, 40);
            Ui.Slider("Small font size", ref s.SmallFontSize, 8, 40, "Used for aura and cooldown timers.");
            Ui.Toggle("Text outline", ref s.TextOutline);
            Ui.Toggle("Short numbers", ref s.AbbreviateNumbers, "Show 45.2k instead of 45,213.");
        }

        if (Ui.Tab(tab, 2))
        {
            Ui.Card("Health Colours");
            Ui.Enum("Colour by", ref s.HealthColors, "Frames set to Inherit use this.");
            Ui.Color("Flat colour", ref s.FlatHealth);
            Ui.Color("Dark mode fill", ref s.DarkFill);
            Ui.Color("Hostile", ref s.Hostile);
            Ui.Color("Neutral", ref s.Neutral);
            Ui.Color("Friendly NPC", ref s.Friendly);
            Ui.Color("Dead", ref s.Dead);
            Ui.Percent("Out of range opacity", ref s.OutOfRangeAlpha);

            Ui.Card("Shields", "How barrier/shield amounts show on health bars. Party and alliance frames can use their own look.");
            Ui.Color("Shield colour", ref s.Shield);
            DrawShieldLook(s.Shields, false);

            Ui.Card("Resource Colours");
            Ui.Color("Mana", ref s.Mana);
            Ui.Color("GP", ref s.Gp);
            Ui.Color("CP", ref s.Cp);

            Ui.Card("Cast Colours");
            Ui.Color("Cast", ref s.Cast);
            Ui.Color("Interruptible", ref s.CastInterruptible);
            Ui.Color("Slidecast window", ref s.Slidecast);
        }

        if (Ui.Tab(tab, 3))
        {
            DrawMedia();
        }
    }

    private string newMediaFolder = string.Empty;
    private string newMediaPack = string.Empty;
    private string mediaPackMessage = string.Empty;

    private void DrawMedia()
    {
        var media = Plugin.Media;
        var folders = Plugin.Config.MediaFolders;

        Ui.Card("Media Packs", "The easiest way: add a media pack (.zip, .7z or .rar, for example NaowhUI_Media.zip or Media.7z). It is unpacked into LucentUI's media folder on this computer, and its fonts and textures are used by their WoW names (or their file names). You can also drop packs into the LucentUI media folder yourself, or give a folder that is already unpacked.");
        Ui.Text("Media pack", ref newMediaPack, "The full path of the .zip, .7z or .rar file, or of a folder.", "e.g. C:\\Users\\you\\Downloads\\Media.7z");
        if (Ui.Button("Add pack", "Add media pack"))
        {
            var source = newMediaPack.Trim().Trim('"');
            try
            {
                if (System.IO.Directory.Exists(source))
                {
                    if (!folders.Contains(source, StringComparer.OrdinalIgnoreCase))
                    {
                        folders.Add(source);
                        Plugin.MarkDirty();
                    }

                    mediaPackMessage = "Added the folder " + source + ". Scanning...";
                }
                else if (!MediaLibrary.ArchiveExtensions.Contains(System.IO.Path.GetExtension(source).ToLowerInvariant()))
                {
                    throw new InvalidOperationException("use a .zip, .7z or .rar file, or a folder.");
                }
                else
                {
                    System.IO.Directory.CreateDirectory(media.DefaultFolder);
                    System.IO.File.Copy(source, System.IO.Path.Combine(media.DefaultFolder, System.IO.Path.GetFileName(source)), overwrite: true);
                    mediaPackMessage = "Added " + System.IO.Path.GetFileName(source) + ". Unpacking...";
                }

                newMediaPack = string.Empty;
                media.Rescan(folders);
            }
            catch (Exception ex)
            {
                mediaPackMessage = "Could not add it: " + ex.Message;
            }
        }

        if (mediaPackMessage.Length > 0)
        {
            Ui.Button("Pack", mediaPackMessage);
        }

        Ui.Card("Media Folders", "Fonts (.ttf, .otf) and bar textures (.tga, .blp, .png) are found in these folders and everything below them. Add your WoW Interface\\AddOns folder to use SharedMedia packs and EllesmereUI's media by the same names as in WoW.");
        Ui.Button("Status", media.Scanning ? "Scanning..." : media.Status);
        if (Ui.Button("LucentUI media folder", "Open"))
        {
            System.IO.Directory.CreateDirectory(media.DefaultFolder);
            Dalamud.Utility.Util.OpenLink(media.DefaultFolder);
        }

        foreach (var folder in folders.ToList())
        {
            if (Ui.Button(folder, "Remove"))
            {
                folders.Remove(folder);
                Plugin.MarkDirty();
                media.Rescan(folders);
            }
        }

        Ui.Text("Add a folder", ref newMediaFolder, "Paste a folder path, e.g. C:\\Program Files (x86)\\World of Warcraft\\_retail_\\Interface\\AddOns", "Folder path");
        if (Ui.Button("Add", "Add folder and scan"))
        {
            var folder = newMediaFolder.Trim().Trim('"');
            if (folder.Length > 0 && !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(folder);
                Plugin.MarkDirty();
            }

            newMediaFolder = string.Empty;
            media.Rescan(folders);
        }

        if (Ui.Button("Rescan", "Look for new files"))
        {
            media.Rescan(folders);
        }
    }

    /// <summary>A live preview of elements (with sample data) at the top of their settings page, like EllesmereUI's.</summary>
    private static void ElementPreview(params string[] ids)
    {
        if (Ui.Searching || previewHud == null)
        {
            return;
        }

        Ui.EndCard();
        Ui.ToolbarOpen = false;
        var config = Plugin.Config;
        var profile = Plugin.Profiles.Current;
        var dl = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;

        if (!config.ShowPagePreview)
        {
            if (ImGui.SmallButton("Show preview"))
            {
                config.ShowPagePreview = true;
                Plugin.MarkDirty();
            }

            ImGui.Dummy(new Vector2(0, 2));
            return;
        }

        // Room around the elements for text above them and icons beside them.
        const float padX = 48f;
        const float padTop = 34f;
        const float padBottom = 22f;
        const float gap = 40f;
        var sizes = new Vector2[ids.Length];
        var total = 0f;
        var tallest = 0f;
        for (var i = 0; i < ids.Length; i++)
        {
            var element = previewHud.Elements.FirstOrDefault(e => e.Id == ids[i]);
            sizes[i] = element?.GetSize(profile) ?? Vector2.Zero;
            total += sizes[i].X + (i > 0 ? gap : 0);
            tallest = Math.Max(tallest, sizes[i].Y);
        }

        var height = Math.Clamp(tallest + padTop + padBottom, 70f, 340f);
        var start = ImGui.GetCursorScreenPos();
        var end = start + new Vector2(width, height);
        dl.AddRectFilled(start, end, Colors.ToU32(new Vector4(0.035f, 0.037f, 0.045f, 1f)), 8f);
        dl.AddRect(start, end, Colors.ToU32(new Vector4(1, 1, 1, 0.06f)), 8f);
        dl.AddText(start + new Vector2(12, 8), Colors.ToU32(new Vector4(1, 1, 1, 0.35f)), "PREVIEW");

        dl.PushClipRect(start + Vector2.One, end - Vector2.One, true);
        var x = start.X + Math.Max(padX, (width - total) / 2f);
        for (var i = 0; i < ids.Length; i++)
        {
            var y = start.Y + padTop + Math.Max(0f, (height - padTop - padBottom - sizes[i].Y) / 2f);
            previewHud.DrawPreview(ids[i], new Vector2(MathF.Floor(x), MathF.Floor(y)));
            x += sizes[i].X + gap;
        }

        dl.PopClipRect();

        // "Hide" in the top-right corner.
        var hideSize = ImGui.CalcTextSize("Hide");
        ImGui.SetCursorScreenPos(new Vector2(end.X - hideSize.X - 14, start.Y + 6));
        if (ImGui.SmallButton("Hide##preview"))
        {
            config.ShowPagePreview = false;
            Plugin.MarkDirty();
        }

        ImGui.SetCursorScreenPos(new Vector2(start.X, end.Y));
        ImGui.Dummy(new Vector2(width, 8));
    }

    /// <summary>"Copy from..." and "Reset" buttons for a page's settings.</summary>
    private static void CopyAndReset<T>(string id, T target, Func<T> defaults, params (string Name, T Source)[] sources)
        where T : class
    {
        if (Ui.Searching)
        {
            return;
        }

        Ui.EndCard();
        if (Ui.ToolbarOpen)
        {
            ImGui.SameLine(0, 28);
            Ui.ToolbarOpen = false;
        }

        ImGui.PushID("actions_" + id);
        if (sources.Length > 0)
        {
            if (ImGui.Button("Copy from..."))
            {
                ImGui.OpenPopup("copy");
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Copy every setting on this page from another element.");
            }

            if (ImGui.BeginPopup("copy"))
            {
                foreach (var (name, source) in sources)
                {
                    if (!ReferenceEquals(source, target) && ImGui.Selectable(name))
                    {
                        SettingsCopy.Into(source, target);
                        Plugin.MarkDirty();
                    }
                }

                ImGui.EndPopup();
            }

            ImGui.SameLine();
        }

        var ctrl = ImGui.GetIO().KeyCtrl;
        if (ImGui.Button("Reset") && ctrl)
        {
            SettingsCopy.Into(defaults(), target);
            Plugin.MarkDirty();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(ctrl ? "Reset this page to its defaults." : "Hold Ctrl and click to reset this page to its defaults.");
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Tip: Ctrl+click a slider to type a value.");
        ImGui.PopID();
        ImGui.Dummy(new Vector2(0, 2));
    }

    private static void DrawUnitFrame(string title, string subtitle, UnitFrameSettings s, bool isPlayer)
    {
        var profile = Plugin.Profiles.Current;
        Ui.PageHeader(title, subtitle);
        ModuleSwitch("Unit frames", ref profile.Modules.UnitFrames);
        CopyAndReset("unit_" + title, s, () => DefaultFrame(s),
            ("Player", profile.Player), ("Target", profile.Target), ("Target of Target", profile.TargetOfTarget), ("Focus", profile.Focus));
        ElementPreview(ReferenceEquals(s, profile.Player) ? "unit.player"
            : ReferenceEquals(s, profile.Target) ? "unit.target"
            : ReferenceEquals(s, profile.TargetOfTarget) ? "unit.tot"
            : "unit.focus");
        var tab = Ui.Tabs("unit", "Frame", "Bars", "Text", "Visibility");

        if (Ui.Tab(tab, 0))
        {
            Ui.Card(title + " Frame");
            Ui.Toggle("Enabled", ref s.Enabled);
            Ui.Size("Size", ref s.Size, "Width and height in pixels.");
            Ui.Enum("Job icon", ref s.JobIcon, "A job icon portrait beside the bars.");
            Ui.Toggle("Own background colour", ref s.CustomBackground);
            if (s.CustomBackground)
            {
                Ui.Color("Background colour", ref s.Background);
            }

            if (!isPlayer)
            {
                Ui.Toggle("Clickable", ref s.Interactive, "Click to target, right-click menu, mouseover and mouseover casting.");
            }
        }

        if (Ui.Tab(tab, 1))
        {
            Ui.Card(title + " Health");
            Ui.MediaPicker("Bar texture", ref s.Texture, false, "Profile texture", "Empty uses the texture from the Style page.");
            Ui.Enum("Health colour", ref s.ColorMode);
            Ui.Toggle("Show shield", ref s.ShowShield);
            Ui.Toggle("Fill right to left", ref s.ReverseFill);

            Ui.Card(title + " Power", "Mana, GP or CP.");
            Ui.Toggle("Show power bar", ref s.ShowPower);
            Ui.Enum("Power bar position", ref s.PowerPosition);
            Ui.Slider("Power bar height", ref s.PowerHeight, 1, 30);
        }

        if (Ui.Tab(tab, 2))
        {
            DrawTextSlot(title + " Left Text", ref s.LeftText, s.LeftStyle);
            DrawTextSlot(title + " Centre Text", ref s.CenterText, s.CenterStyle);
            DrawTextSlot(title + " Right Text", ref s.RightText, s.RightStyle);
        }

        if (Ui.Tab(tab, 3))
        {
            DrawVisibility(title, s.Visibility);
        }
    }

    private static UnitFrameSettings DefaultFrame(UnitFrameSettings s)
    {
        var profile = Plugin.Profiles.Current;
        var defaults = new Profile();
        return ReferenceEquals(s, profile.Player) ? defaults.Player
            : ReferenceEquals(s, profile.Target) ? defaults.Target
            : ReferenceEquals(s, profile.TargetOfTarget) ? defaults.TargetOfTarget
            : defaults.Focus;
    }

    private static void DrawVisibility(string title, VisibilitySettings v)
    {
        Ui.Card(title + " Visibility");
        Ui.Enum("Show", ref v.Mode);
        Ui.Percent("Opacity", ref v.Opacity, "How see-through the element is while shown.");
        Ui.Percent("Opacity when hidden", ref v.HiddenAlpha, "0% hides it completely; higher values fade it instead.");
    }

    private static void DrawTextSlot(string title, ref string template, TextStyle style)
    {
        Ui.Card(title);
        Ui.TextContent("Shows", ref template);
        Ui.TextLook("Text", style);
    }

    private static void DrawParty(PartySettings s)
    {
        Ui.PageHeader("Party", "Party frames with role sorting and range fading.");
        ModuleSwitch("Party frames", ref Plugin.Profiles.Current.Modules.PartyFrames);
        CopyAndReset("party", s, () => new Profile().Party);
        ElementPreview("party");
        var tab = Ui.Tabs("party", "Layout", "Health", "Text", "Trackers", "Buffs & Debuffs", "Visibility");

        if (Ui.Tab(tab, 0))
        {
            Ui.Card("Party Frames");
            Ui.Toggle("Enabled", ref s.Enabled);
            Ui.Size("Member size", ref s.Size);
            Ui.Slider("Spacing", ref s.Spacing, 0, 30);
            Ui.Toggle("Horizontal", ref s.Horizontal);
            Ui.Enum("Sort by", ref s.Sort);
            if (s.Sort == PartySortMode.Role)
            {
                Ui.Enum("Role order", ref s.RoleOrder);
            }
            Ui.Toggle("Include yourself", ref s.IncludePlayer);
            Ui.Toggle("Show when solo", ref s.ShowWhenSolo);
            Ui.Toggle("Clickable", ref s.Interactive);
            Ui.Toggle("Job icon", ref s.ShowJobIcon);
        }

        if (Ui.Tab(tab, 1))
        {
            Ui.Card("Party Health");
            Ui.MediaPicker("Bar texture", ref s.Texture, false, "Profile texture", "Empty uses the texture from the Style page.");
            Ui.Enum("Health colour", ref s.ColorMode);
            Ui.Toggle("Fade out of range", ref s.RangeFade, "Members further than 30 yalms fade out.");
            Ui.Toggle("Highlight target", ref s.HighlightTarget);
            Ui.Toggle("Show power bar", ref s.ShowPower);
            Ui.Slider("Power bar height", ref s.PowerHeight, 1, 20);

            DrawFrameShields("Party Shields", ref s.ShowShield, ref s.OwnShieldStyle, s.Shields);
        }

        if (Ui.Tab(tab, 2))
        {
            DrawTextSlot("Party Left Text", ref s.LeftText, s.LeftStyle);
            DrawTextSlot("Party Center Text", ref s.CenterText, s.CenterStyle);
            DrawTextSlot("Party Right Text", ref s.RightText, s.RightStyle);
        }

        if (Ui.Tab(tab, 3))
        {
            Ui.Card("Party Trackers");
            Ui.Toggle("Raise", ref s.ShowRaise, "Shows when a dead member has a raise waiting or one is being cast on them.");
            Ui.Toggle("Invulnerability", ref s.ShowInvulnerability, "Hallowed Ground, Holmgang, Living Dead and Superbolide.");
            Ui.Toggle("Cleansable debuffs", ref s.ShowCleansable, "Outlines members with a debuff Esuna can remove.");
            Ui.Toggle("Only if I can cleanse", ref s.CleansableOnlyIfICanCleanse, "Healers and bards.");
            Ui.Color("Cleansable colour", ref s.CleansableColor);
            Ui.Toggle("Ready check", ref s.ShowReadyCheck);
        }

        if (Ui.Tab(tab, 4))
        {
            DrawStatusIcons("Party Buffs", s.Buffs, false, "Buff icons on each member, like EllesmereUI's raid frame indicators. By default only yours: regens, shields, mitigation.");
            DrawStatusIcons("Party Debuffs", s.Debuffs, true, "Debuff icons on each member.");
        }

        if (Ui.Tab(tab, 5))
        {
            DrawVisibility("Party", s.Visibility);
        }
    }

    /// <summary>Settings for buff or debuff icons on member frames (party, alliance).</summary>
    private static void DrawStatusIcons(string title, StatusIconSettings s, bool debuffs, string description)
    {
        Ui.Card(title, description);
        Ui.Toggle($"Show {(debuffs ? "debuffs" : "buffs")}", ref s.Enabled);
        Ui.Toggle("Only mine", ref s.OnlyMine, debuffs ? "Only debuffs you applied." : "Only buffs you put on them.");
        Ui.Slider("Icons per member", ref s.Max, 1, 10);
        Ui.Enum("Shape", ref s.Shape, "Style Default follows the Style page. Portrait is the game's default 3:4 icon; Square and Round crop its middle.");
        Ui.Slider("Size", ref s.Size, 8f, 64f, "%.0f", "Icon height in pixels.");
        Ui.Enum("Position", ref s.Anchor, "The point of the frame the icons line up against.");
        Ui.Toggle("Outside the frame", ref s.Outside, "Beside the frame (or above/below it for Top and Bottom) instead of inside.");
        Ui.Enum("Grow", ref s.Grow, "Auto grows away from the side the icons are on.");
        Ui.Offset("Offset", ref s.Offset);
        Ui.Slider("Spacing", ref s.Spacing, 0, 12);
        Ui.Enum("Border", ref s.Border, "By Type colours debuffs red and cleansable ones purple.");
        Ui.Toggle("Timers", ref s.Timers);
        Ui.Slider("Timer font size", ref s.TimerFontSize, 0, 40, "0 uses the small font size from the Style page.");
        Ui.Enum("Filter", ref s.Filter, debuffs ? "Show only listed debuffs, or hide listed ones." : "Track only listed buffs (e.g. Regen, Galvanize, Kerachole), or hide listed ones.");
        if (s.Filter != ListFilter.None)
        {
            var source = Svc.TargetManager.Target ?? Svc.ObjectTable.LocalPlayer;
            StatusListEditor(title + " List", s.Ids, source, false,
                (s.Filter == ListFilter.OnlyListed ? "Only these are shown. " : "These are hidden. ") +
                "Target a party member to add statuses that are on them right now.");
        }
    }

    /// <summary>Placement, look, opacity, overshield and edge line for shields on health bars.</summary>
    private static void DrawShieldLook(ShieldSettings sh, bool ownColor)
    {
        Ui.Enum("Placement", ref sh.Placement, "After health: fills the missing health, like the game. Overlay: covers the end of the health bar. From left/right: grows from that side of the bar.");
        Ui.Enum("Look", ref sh.Look, "Solid fill or EllesmereUI-style stripes.");
        Ui.Percent("Opacity", ref sh.Opacity);
        if (sh.Placement == ShieldPlacement.AfterHealth)
        {
            Ui.Enum("Overshield", ref sh.Overshield, "What to show when the shield is bigger than the missing health.");
        }

        Ui.Toggle("Edge line", ref sh.EdgeLine, "A bright line at the shield's edge.");
        if (ownColor)
        {
            Ui.Toggle("Own colour", ref sh.OwnColor, "Off uses the shield colour from the Style page.");
            if (sh.OwnColor)
            {
                Ui.Color("Shield colour", ref sh.Color);
            }
        }
    }

    private static void DrawFrameShields(string title, ref bool show, ref bool own, ShieldSettings sh)
    {
        Ui.Card(title);
        Ui.Toggle("Show shield", ref show);
        if (!show)
        {
            return;
        }

        Ui.Toggle("Own shield style", ref own, "Off uses the shield look from Style > Colours.");
        if (own)
        {
            DrawShieldLook(sh, true);
        }
    }

    private static void DrawAlliance(AllianceSettings s)
    {
        Ui.PageHeader("Alliance", "The two other parties in 24-player alliance raids. Shows only in an alliance.");
        ModuleSwitch("Alliance frames", ref Plugin.Profiles.Current.Modules.Alliance);
        CopyAndReset("alliance", s, () => new Profile().Alliance);
        ElementPreview("alliance");
        var tab = Ui.Tabs("alliance", "Layout", "Text", "Buffs & Debuffs", "Visibility");

        if (Ui.Tab(tab, 0))
        {
            Ui.Card("Alliance Frames");
            Ui.Toggle("Enabled", ref s.Enabled);
            Ui.Size("Member size", ref s.Size);
            Ui.Slider("Spacing", ref s.Spacing, 0, 20);
            Ui.Slider("Space between parties", ref s.GroupSpacing, 0, 60);
            Ui.Toggle("Parties side by side", ref s.SideBySide, "Off puts one party above the other.");
            Ui.Toggle("Members in a row", ref s.HorizontalMembers, "Each party's members side by side instead of in a column.");
            Ui.Slider(s.HorizontalMembers ? "Members per row" : "Members per column", ref s.MembersPerRow, 1, 8, "After this many, members wrap to a new line, e.g. 4 gives two lines of 4.");
            Ui.MediaPicker("Bar texture", ref s.Texture, false, "Profile texture", "Empty uses the texture from the Style page.");
            Ui.Enum("Health colour", ref s.ColorMode);
            Ui.Toggle("Job icon", ref s.ShowJobIcon);
            Ui.Toggle("Fade out of range", ref s.RangeFade, "Members further than 40 yalms fade out.");
            Ui.Toggle("Clickable", ref s.Interactive, "Click to target (when they are near), mouseover and mouseover casting.");

            DrawFrameShields("Alliance Shields", ref s.ShowShield, ref s.OwnShieldStyle, s.Shields);
        }

        if (Ui.Tab(tab, 1))
        {
            DrawTextSlot("Alliance Left Text", ref s.LeftText, s.LeftStyle);
            DrawTextSlot("Alliance Center Text", ref s.CenterText, s.CenterStyle);
            DrawTextSlot("Alliance Right Text", ref s.RightText, s.RightStyle);
        }

        if (Ui.Tab(tab, 2))
        {
            DrawStatusIcons("Alliance Buffs", s.Buffs, false, "Buff icons on alliance members (only while they are near enough to be loaded).");
            DrawStatusIcons("Alliance Debuffs", s.Debuffs, true, "Debuff icons on alliance members (only while they are near enough to be loaded).");
        }

        if (Ui.Tab(tab, 3))
        {
            DrawVisibility("Alliance", s.Visibility);
        }
    }

    private static void DrawCastBars(Profile profile)
    {
        Ui.PageHeader("Cast Bars", "Player, target and focus cast bars.");
        ModuleSwitch("Cast bars", ref Plugin.Profiles.Current.Modules.CastBars);
        var tab = Ui.Tabs("cast", "Player", "Target", "Focus");
        var bars = new[] { ("Player Cast Bar", profile.PlayerCast), ("Target Cast Bar", profile.TargetCast), ("Focus Cast Bar", profile.FocusCast) };
        var defaults = new Profile();
        var defaultBars = new[] { defaults.PlayerCast, defaults.TargetCast, defaults.FocusCast };
        for (var i = 0; i < bars.Length; i++)
        {
            if (!Ui.Tab(tab, i))
            {
                continue;
            }

            var (title, settings) = bars[i];
            var index = i;
            if (tab >= 0)
            {
                ElementPreview(i == 0 ? "cast.player" : i == 1 ? "cast.target" : "cast.focus");
            }

            CopyAndReset("cast" + i, settings, () => defaultBars[index], ("Player cast bar", profile.PlayerCast), ("Target cast bar", profile.TargetCast), ("Focus cast bar", profile.FocusCast));
            DrawCastBar(title, settings, i == 0);
        }
    }

    private static void DrawCastBar(string title, CastBarSettings s, bool isPlayer)
    {
        Ui.Card(title);
        Ui.Toggle("Enabled", ref s.Enabled);
        Ui.Size("Size", ref s.Size);
        Ui.Toggle("Show icon", ref s.ShowIcon);
        Ui.Toggle("Icon on the right", ref s.IconOnRight);
        Ui.Toggle("Own colour", ref s.CustomColor, "Interruptible casts keep the interruptible colour.");
        if (s.CustomColor)
        {
            Ui.Color("Colour", ref s.Color);
        }

        Ui.Toggle("Own background colour", ref s.CustomBackground);
        if (s.CustomBackground)
        {
            Ui.Color("Background colour", ref s.Background);
        }

        Ui.MediaPicker("Bar texture", ref s.Texture, false, "Profile texture", "Empty uses the texture from the Style page.");
        Ui.Percent("Opacity", ref s.Opacity);
        Ui.Toggle("Show spell name", ref s.ShowName);
        Ui.TextLook("Spell name", s.NameStyle);
        Ui.Toggle("Show timer", ref s.ShowTimer);
        Ui.TextLook("Timer", s.TimerStyle);
        if (isPlayer)
        {
            Ui.Toggle("Show slidecast window", ref s.ShowSlidecast, "Marks the end of the cast where you can already move.");
            Ui.Slider("Slidecast window", ref s.SlidecastWindow, 0f, 1.5f, "%.2fs");
        }
    }

    private static void DrawAuras(Profile profile)
    {
        Ui.PageHeader("Auras", "Buffs and debuffs as icon grids with timers.");
        ModuleSwitch("Auras", ref Plugin.Profiles.Current.Modules.Auras);
        var tab = Ui.Tabs("auras", "Player Buffs", "Player Debuffs", "Target");
        var groups = new[] { ("Player Buffs", profile.PlayerBuffs), ("Player Debuffs", profile.PlayerDebuffs), ("Target Auras", profile.TargetAuras) };
        var defaults = new Profile();
        var defaultGroups = new[] { defaults.PlayerBuffs, defaults.PlayerDebuffs, defaults.TargetAuras };
        for (var i = 0; i < groups.Length; i++)
        {
            if (!Ui.Tab(tab, i))
            {
                continue;
            }

            var (title, settings) = groups[i];
            var index = i;
            if (tab >= 0)
            {
                ElementPreview(i == 0 ? "auras.player.buffs" : i == 1 ? "auras.player.debuffs" : "auras.target");
            }

            CopyAndReset("aura" + i, settings, () => defaultGroups[index], ("Player buffs", profile.PlayerBuffs), ("Player debuffs", profile.PlayerDebuffs), ("Target auras", profile.TargetAuras));
            DrawAura(title, settings, i == 0);
        }
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

        Ui.Enum("Shape", ref s.Shape, "Style Default follows the Style page. Portrait is the game's default 3:4 icon; Square and Round crop its middle.");
        Ui.Enum("Border", ref s.Border, "By Type colours debuffs red and cleansable ones purple.");
        Ui.Enum("Timer position", ref s.TimerPosition);
        Ui.Slider("Timer font size", ref s.TimerFontSize, 0, 40, "0 uses the small font size from the Style page.");
        Ui.Enum("Filter", ref s.Filter, "Hide the statuses in a list, or show only those.");

        if (s.Filter != ListFilter.None)
        {
            var source = title.StartsWith("Target") ? Svc.TargetManager.Target : Svc.ObjectTable.LocalPlayer;
            StatusListEditor($"{title} Filter List", s.FilterIds, source, false,
                s.Filter == ListFilter.HideListed ? "These statuses are hidden." : "Only these statuses are shown.");
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
        Ui.Toggle("Alliance lists", ref s.HideAlliance);
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

        Ui.Card("Lucent Profile", "The built-in Lucent layout (EllesmereUI style), scaled to your screen. It uses the Naowh font and textures from NaowhUI_Media: add that pack under Style > Media > Media Packs to see them; otherwise the game font and bar style are used.");
        if (Ui.Button("Lucent", "Add the Lucent profile"))
        {
            var added = profiles.AddLucent(Plugin.ScreenHeight, activate: true);
            Report(true, $"Added and switched to \"{added}\"", string.Empty);
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
