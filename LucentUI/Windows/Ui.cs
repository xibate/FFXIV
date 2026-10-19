using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
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

    public static bool Button(string label, string buttonText, string? help = null) =>
        Row(label, help) && ImGui.Button(buttonText + "##" + label, new Vector2(-1, 0));

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
