using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;

namespace LucentUI.Windows;

/// <summary>
/// Option widgets laid out as cards of label/control rows. When a search filter is set, rows that do not
/// match are skipped and a card only appears if at least one of its rows (or its title) matches.
/// </summary>
internal static partial class Ui
{
    private static readonly Dictionary<Type, (Array Values, string[] Names)> EnumCache = new();

    private static string? pendingTitle;
    private static string? pendingDescription;
    private static bool titleMatches;
    private static bool cardOpen;
    private static Vector2 cardStart;
    private static uint cardId;

    // Card heights from the previous frame, so the background can be drawn before the content.
    private static readonly Dictionary<uint, float> CardHeights = new();

    public static string Filter = string.Empty;
    public static Vector4 Accent = Colors.Rgb(0x3FD0C9);

    /// <summary>Set when any row in the current search produced output.</summary>
    public static bool AnyVisible;

    public static bool Searching => Filter.Length > 0;

    private const float CardPadding = 12f;

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();

    // ── Cards ────────────────────────────────────────────────────────────────

    public static void Card(string title, string? description = null)
    {
        EndCard();
        pendingTitle = title;
        pendingDescription = description;
        titleMatches = Matches(title);
    }

    public static void EndCard()
    {
        if (cardOpen)
        {
            ImGui.EndTable();
            ImGui.Unindent(CardPadding);
            ImGui.Dummy(new Vector2(0, CardPadding - ImGui.GetStyle().ItemSpacing.Y));

            CardHeights[cardId] = ImGui.GetCursorScreenPos().Y - cardStart.Y;
            ImGui.PopID();
            ImGui.Dummy(new Vector2(0, 6));
        }

        cardOpen = false;
        pendingTitle = null;
    }

    private static bool Row(string label, string? help)
    {
        if (Searching && !titleMatches && !Matches(label))
        {
            return false;
        }

        if (!cardOpen)
        {
            OpenCard();
        }

        AnyVisible = true;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (help != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(help);
            }
        }

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        return true;
    }

    private static void OpenCard()
    {
        cardOpen = true;
        ImGui.PushID(pendingTitle ?? "card");
        cardId = ImGui.GetID("##card");
        cardStart = ImGui.GetCursorScreenPos();

        if (CardHeights.TryGetValue(cardId, out var height))
        {
            var dl = ImGui.GetWindowDrawList();
            var end = new Vector2(cardStart.X + ImGui.GetContentRegionAvail().X, cardStart.Y + height);
            dl.AddRectFilled(cardStart, end, Colors.ToU32(new Vector4(1, 1, 1, 0.035f)), 8f);
            dl.AddRect(cardStart, end, Colors.ToU32(new Vector4(1, 1, 1, 0.06f)), 8f);
        }

        ImGui.Dummy(new Vector2(0, CardPadding - ImGui.GetStyle().ItemSpacing.Y));
        ImGui.Indent(CardPadding);
        ImGui.TextColored(Accent, (pendingTitle ?? string.Empty).ToUpperInvariant());
        if (!string.IsNullOrEmpty(pendingDescription))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - CardPadding);
            ImGui.TextDisabled(pendingDescription);
            ImGui.PopTextWrapPos();
        }

        ImGui.Spacing();
        var width = ImGui.GetContentRegionAvail().X - CardPadding;
        ImGui.BeginTable("##rows", 2, ImGuiTableFlags.SizingStretchProp, new Vector2(width, 0));
        ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthStretch, 0.42f);
        ImGui.TableSetupColumn("control", ImGuiTableColumnFlags.WidthStretch, 0.58f);
    }

    private static bool Matches(string text) =>
        !Searching || text.Contains(Filter, StringComparison.OrdinalIgnoreCase);

    private static bool Changed(bool changed)
    {
        if (changed)
        {
            Plugin.MarkDirty();
        }

        return changed;
    }

    // ── Widgets ──────────────────────────────────────────────────────────────

    public static bool Toggle(string label, ref bool value, string? help = null) =>
        Row(label, help) && Changed(ImGui.Checkbox("##" + label, ref value));

    public static bool Slider(string label, ref int value, int min, int max, string? help = null) =>
        Row(label, help) && Changed(ImGui.SliderInt("##" + label, ref value, min, max));

    public static bool Slider(string label, ref float value, float min, float max, string format = "%.1f", string? help = null) =>
        Row(label, help) && Changed(ImGui.SliderFloat("##" + label, ref value, min, max, format));

    public static bool Percent(string label, ref float value, string? help = null)
    {
        if (!Row(label, help))
        {
            return false;
        }

        var percent = value * 100f;
        if (!ImGui.SliderFloat("##" + label, ref percent, 0f, 100f, "%.0f%%"))
        {
            return false;
        }

        value = Math.Clamp(percent / 100f, 0f, 1f);
        return Changed(true);
    }

    public static bool Size(string label, ref Vector2 value, string? help = null)
    {
        if (!Row(label, help))
        {
            return false;
        }

        if (!ImGui.DragFloat2("##" + label, ref value, 1f, 4f, 2000f, "%.0f"))
        {
            return false;
        }

        value = new Vector2(MathF.Round(Math.Max(4f, value.X)), MathF.Round(Math.Max(4f, value.Y)));
        return Changed(true);
    }

    public static bool Color(string label, ref Vector4 value, string? help = null) =>
        Row(label, help) && Changed(ImGui.ColorEdit4("##" + label, ref value, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.AlphaBar));

    public static bool Text(string label, ref string value, string? help = null, string hint = "") =>
        Row(label, help) && Changed(ImGui.InputTextWithHint("##" + label, hint, ref value, 256));

    public static bool Enum<T>(string label, ref T value, string? help = null)
        where T : struct, Enum
    {
        if (!Row(label, help))
        {
            return false;
        }

        var (values, names) = EnumInfo<T>();
        var changed = false;
        if (ImGui.BeginCombo("##" + label, PrettyName(value)))
        {
            for (var i = 0; i < values.Length; i++)
            {
                var option = (T)values.GetValue(i)!;
                if (ImGui.Selectable(names[i], option.Equals(value)))
                {
                    value = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return Changed(changed);
    }

    public static bool Offset(string label, ref Vector2 value, string? help = null)
    {
        if (!Row(label, help))
        {
            return false;
        }

        if (!ImGui.DragFloat2("##" + label, ref value, 0.5f, -500f, 500f, "%.0f"))
        {
            return false;
        }

        value = new Vector2(MathF.Round(value.X), MathF.Round(value.Y));
        return Changed(true);
    }

    private static readonly (string Name, string? Template)[] TextPresets =
    {
        ("None", ""),
        ("Name", "{name}"),
        ("First name", "{name:first}"),
        ("Name (14 letters)", "{name:14}"),
        ("Level and name", "{level} {name}"),
        ("Health", "{hp}"),
        ("Health %", "{hp:pct}%"),
        ("Health | %", "{hp} | {hp:pct}%"),
        ("% | Health", "{hp:pct}% | {hp}"),
        ("Health / max", "{hp} / {hp:max}"),
        ("Missing health", "{hp:deficit}"),
        ("Mana", "{mp}"),
        ("Mana %", "{mp:pct}%"),
        ("Level", "{level}"),
        ("Job", "{job}"),
        ("Custom", null),
    };

    private static readonly HashSet<string> CustomText = new();

    /// <summary>What a text shows, picked from presets like EllesmereUI's, or a custom tag template.</summary>
    public static bool TextContent(string label, ref string template, string? help = null)
    {
        var current = template;
        var key = (pendingTitle ?? string.Empty) + "/" + label;
        var index = Array.FindIndex(TextPresets, p => p.Template == current);
        var custom = index < 0 || CustomText.Contains(key);
        var changed = false;

        if (Row(label, help))
        {
            var preview = custom ? "Custom" : TextPresets[index].Name;
            if (ImGui.BeginCombo("##" + label, preview))
            {
                foreach (var (name, value) in TextPresets)
                {
                    var selected = value == null ? custom : !custom && value == template;
                    if (ImGui.Selectable(name, selected))
                    {
                        if (value == null)
                        {
                            CustomText.Add(key);
                        }
                        else
                        {
                            CustomText.Remove(key);
                            template = value;
                            changed = true;
                        }
                    }
                }

                ImGui.EndCombo();
            }
        }

        if (custom || CustomText.Contains(key))
        {
            changed |= Text(label + ": custom", ref template, TextTags.Help, "e.g. {name} [hp:pct]%");
        }

        return Changed(changed);
    }

    /// <summary>Size, offset and colour rows for one piece of text.</summary>
    public static void TextLook(string label, TextStyle style)
    {
        Slider(label + ": size", ref style.FontSize, 0, 40, "0 uses the font size from the Style page.");
        Offset(label + ": offset", ref style.Offset, "Move the text (x, y) in pixels.");
        Enum(label + ": colour", ref style.Color, "Job uses the unit's job colour (or reaction colour for NPCs).");
        if (style.Color == TextColorMode.Custom)
        {
            Color(label + ": custom colour", ref style.CustomColor);
        }
    }

    public static bool Button(string label, string buttonText, string? help = null) =>
        Row(label, help) && ImGui.Button(buttonText + "##" + label, new Vector2(-1, 0));

    // ── Media ────────────────────────────────────────────────────────────────

    private static string mediaFilter = string.Empty;

    /// <summary>
    /// Picks a font or bar texture from the media library by name, with a search box and previews. Empty means
    /// <paramref name="defaultLabel"/> (the game font or the profile's texture).
    /// </summary>
    public static bool MediaPicker(string label, ref string value, bool fonts, string defaultLabel, string? help = null)
    {
        if (!Row(label, help))
        {
            return false;
        }

        var media = Plugin.Media;
        var names = fonts ? media.FontNames : media.TextureNames;
        var known = string.IsNullOrEmpty(value) || (fonts ? media.HasFont(value) : media.HasTexture(value));
        var preview = string.IsNullOrEmpty(value) ? defaultLabel : known ? value : value + " (not found)";
        var changed = false;

        ImGui.SetNextWindowSizeConstraints(new Vector2(0, 0), new Vector2(float.MaxValue, 360));
        if (ImGui.BeginCombo("##" + label, preview, ImGuiComboFlags.HeightLargest))
        {
            if (ImGui.IsWindowAppearing())
            {
                mediaFilter = string.Empty;
                ImGui.SetKeyboardFocusHere();
            }

            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##filter", names.Count > 0 ? $"Search {names.Count} {(fonts ? "fonts" : "textures")}..." : "Nothing found yet: add a media folder on the Style page", ref mediaFilter, 64);

            if (ImGui.Selectable(defaultLabel, string.IsNullOrEmpty(value)))
            {
                value = string.Empty;
                changed = true;
            }

            var shown = 0;
            foreach (var name in names)
            {
                if (mediaFilter.Length > 0 && !name.Contains(mediaFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (++shown > 300)
                {
                    ImGui.TextDisabled("More found: type to narrow it down.");
                    break;
                }

                var start = ImGui.GetCursorScreenPos();
                if (ImGui.Selectable(name + "##media", name.Equals(value, StringComparison.OrdinalIgnoreCase)))
                {
                    value = name;
                    changed = true;
                }

                // Texture preview on the right of the row, tinted with the accent colour.
                if (!fonts && ImGui.IsItemVisible() && media.Texture(name) is { } texture)
                {
                    var width = ImGui.GetContentRegionAvail().X;
                    var height = ImGui.GetTextLineHeight();
                    var min = new Vector2(start.X + Math.Max(140f, width * 0.45f), start.Y + 2);
                    var max = new Vector2(start.X + width, start.Y + height - 2);
                    if (max.X > min.X)
                    {
                        ImGui.GetWindowDrawList().AddImage(texture.Handle, min, max, Vector2.Zero, Vector2.One, Colors.ToU32(Accent));
                    }
                }
            }

            ImGui.EndCombo();
        }

        return Changed(changed);
    }

    // ── Tabs ─────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, int> TabState = new();

    /// <summary>
    /// A row of tabs for a page. Returns the selected tab, or -1 while searching (every tab's settings show).
    /// </summary>
    public static int Tabs(string id, params string[] names)
    {
        if (Searching)
        {
            return -1;
        }

        EndCard();
        TabState.TryGetValue(id, out var selected);
        selected = Math.Clamp(selected, 0, names.Length - 1);

        ImGui.PushID("tabs_" + id);
        for (var i = 0; i < names.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0, 4);
            }

            var active = i == selected;
            ImGui.PushStyleColor(ImGuiCol.Button, active ? Colors.WithAlpha(Accent, 0.55f) : new Vector4(1, 1, 1, 0.05f));
            ImGui.PushStyleColor(ImGuiCol.Text, active ? Vector4.One : new Vector4(1, 1, 1, 0.75f));
            if (ImGui.Button(names[i]))
            {
                selected = i;
            }

            ImGui.PopStyleColor(2);
        }

        ImGui.PopID();
        TabState[id] = selected;
        ImGui.Dummy(new Vector2(0, 4));
        return selected;
    }

    /// <summary>True when the settings of tab <paramref name="index"/> should be drawn.</summary>
    public static bool Tab(int selected, int index) => selected < 0 || selected == index;

    /// <summary>Opens a tab next time its page draws (used to jump to an element's settings).</summary>
    public static void OpenTab(string id, int index) => TabState[id] = index;

    // ── Page chrome ──────────────────────────────────────────────────────────

    public static void PageHeader(string title, string subtitle)
    {
        if (Searching)
        {
            return;
        }

        ImGui.SetWindowFontScale(1.35f);
        ImGui.TextUnformatted(title);
        ImGui.SetWindowFontScale(1f);
        ImGui.TextDisabled(subtitle);
        ImGui.Dummy(new Vector2(0, 6));
    }

    public static string PrettyName<T>(T value)
        where T : struct, Enum => WordBoundary().Replace(value.ToString(), " ");

    private static (Array Values, string[] Names) EnumInfo<T>()
        where T : struct, Enum
    {
        if (!EnumCache.TryGetValue(typeof(T), out var info))
        {
            var values = System.Enum.GetValues<T>();
            var names = Array.ConvertAll(values, v => PrettyName(v));
            info = (values, names);
            EnumCache[typeof(T)] = info;
        }

        return info;
    }
}
