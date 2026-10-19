using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using LucentUI.Config;
using LucentUI.Style;

namespace LucentUI.Hud;

/// <summary>
/// Edit mode: every enabled element gets a mover that can be dragged, snapped to a grid and nudged with the
/// arrow keys. Positions are stored per profile as offsets from the screen centre.
/// </summary>
public sealed class UnlockMode
{
    private readonly IReadOnlyList<HudElement> elements;
    private string? selectedId;
    private Vector2 dragStart;

    public UnlockMode(IReadOnlyList<HudElement> elements)
    {
        this.elements = elements;
    }

    public bool Active { get; private set; }

    public event Action? OpenSettingsRequested;

    /// <summary>Asks for the settings of one element (double-click a mover, or Settings with one selected).</summary>
    public event Action<string>? OpenElementSettingsRequested;

    public void Toggle() => SetActive(!Active);

    public void SetActive(bool active)
    {
        Hud.Elements.NameplatesElement.UnlockActive = active;
        Active = active;
        if (!active)
        {
            selectedId = null;
            Plugin.MarkDirty();
        }
    }

    /// <summary>Draws the alignment grid underneath the HUD.</summary>
    public void DrawGrid(ImDrawListPtr dl, Vector2 vpPos, Vector2 vpSize, Vector2 origin, Profile profile)
    {
        var accent = profile.Style.Accent;
        dl.AddRectFilled(vpPos, vpPos + vpSize, Colors.ToU32(new Vector4(0, 0, 0, 0.35f)));

        var grid = Math.Max(4, profile.Layout.GridSize);
        var line = Colors.ToU32(new Vector4(1, 1, 1, 0.05f));
        var major = Colors.ToU32(new Vector4(1, 1, 1, 0.10f));

        var steps = (int)(vpSize.X / 2f / grid) + 1;
        for (var i = -steps; i <= steps; i++)
        {
            var x = MathF.Floor(origin.X + i * grid);
            dl.AddLine(new Vector2(x, vpPos.Y), new Vector2(x, vpPos.Y + vpSize.Y), i % 4 == 0 ? major : line);
        }

        steps = (int)(vpSize.Y / 2f / grid) + 1;
        for (var i = -steps; i <= steps; i++)
        {
            var y = MathF.Floor(origin.Y + i * grid);
            dl.AddLine(new Vector2(vpPos.X, y), new Vector2(vpPos.X + vpSize.X, y), i % 4 == 0 ? major : line);
        }

        var centre = Colors.ToU32(Colors.WithAlpha(accent, 0.6f));
        dl.AddLine(new Vector2(origin.X, vpPos.Y), new Vector2(origin.X, vpPos.Y + vpSize.Y), centre);
        dl.AddLine(new Vector2(vpPos.X, origin.Y), new Vector2(vpPos.X + vpSize.X, origin.Y), centre);
    }

    /// <summary>Draws the movers (in their own input-capturing window) and the toolbar.</summary>
    public void Draw(Profile profile, Vector2 origin, Vector2 vpPos, Vector2 vpSize)
    {
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(vpPos);
        ImGui.SetNextWindowSize(vpSize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollWithMouse;

        var open = ImGui.Begin("##LucentUI_Unlock", flags);
        ImGui.PopStyleVar(2);

        try
        {
            if (open)
            {
                DrawMovers(profile, origin);
                HandleKeyboard(profile);
            }
        }
        finally
        {
            ImGui.End();
        }

        DrawToolbar(profile, vpPos, vpSize);
    }

    private void DrawMovers(Profile profile, Vector2 origin)
    {
        var dl = ImGui.GetWindowDrawList();
        var accent = profile.Style.Accent;
        var layout = profile.Layout;

        foreach (var element in elements)
        {
            if (!element.IsEnabled(profile) || !element.Movable)
            {
                continue;
            }

            var offset = element.GetPosition(profile);
            var size = Vector2.Max(element.GetSize(profile), new Vector2(12));
            var min = origin + offset;

            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton("##mover_" + element.Id, size);
            var hovered = ImGui.IsItemHovered();

            if (ImGui.IsItemActivated())
            {
                selectedId = element.Id;
                dragStart = offset;
            }

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
            {
                var next = dragStart + ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0f);
                if (layout.SnapToGrid && !ImGui.GetIO().KeyShift)
                {
                    next = Snap(next, layout.GridSize);
                }

                element.SetPosition(profile, Gfx.Floor(next));
                Plugin.MarkDirty();
            }

            if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                OpenElementSettingsRequested?.Invoke(element.Id);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                element.ResetPosition(profile);
                Plugin.MarkDirty();
            }

            if (hovered && !ImGui.IsItemActive())
            {
                ImGui.SetTooltip($"{element.Label}\nDrag to move (hold Shift to ignore the grid)\nDouble-click to open its settings\nArrow keys nudge the selected element\nRight-click to reset");
            }

            var selected = selectedId == element.Id;
            var outline = selected ? accent : hovered ? Colors.Lighten(accent, 0.3f) : new Vector4(1, 1, 1, 0.45f);
            dl.AddRectFilled(min, min + size, Colors.ToU32(Colors.WithAlpha(accent, selected ? 0.22f : 0.10f)));
            dl.AddRect(min, min + size, Colors.ToU32(outline), 0f, default, selected ? 2f : 1f);

            var labelSize = ImGui.CalcTextSize(element.Label);
            var labelPos = Gfx.Floor(min + new Vector2(4, -labelSize.Y - 2));
            dl.AddRectFilled(labelPos - new Vector2(3, 1), labelPos + labelSize + new Vector2(3, 1), Colors.ToU32(new Vector4(0, 0, 0, 0.7f)), 3f);
            dl.AddText(labelPos, Colors.ToU32(selected ? accent : Vector4.One), element.Label);
        }
    }

    private void HandleKeyboard(Profile profile)
    {
        var element = Selected();
        if (element == null || ImGui.GetIO().WantTextInput)
        {
            return;
        }

        var step = ImGui.GetIO().KeyShift ? 10f : 1f;
        var move = Vector2.Zero;
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow)) move.X -= step;
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow)) move.X += step;
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) move.Y -= step;
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) move.Y += step;

        if (move != Vector2.Zero)
        {
            element.SetPosition(profile, element.GetPosition(profile) + move);
            Plugin.MarkDirty();
        }
    }

    private void DrawToolbar(Profile profile, Vector2 vpPos, Vector2 vpSize)
    {
        var accent = profile.Style.Accent;
        var layout = profile.Layout;

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(vpPos + new Vector2(vpSize.X / 2f, 40f), ImGuiCond.Appearing, new Vector2(0.5f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 10));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.07f, 0.08f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, Colors.WithAlpha(accent, 0.6f));

        if (ImGui.Begin("LucentUI Unlock Mode##LucentUI_Toolbar", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextColored(accent, "UNLOCK MODE");
            ImGui.SameLine();
            ImGui.TextDisabled($"Profile: {Plugin.Profiles.CurrentName}");

            ImGui.SetNextItemWidth(120);
            if (ImGui.SliderInt("Grid", ref layout.GridSize, 4, 64))
            {
                Plugin.MarkDirty();
            }

            ImGui.SameLine();
            if (ImGui.Checkbox("Snap", ref layout.SnapToGrid))
            {
                Plugin.MarkDirty();
            }

            var selected = Selected();
            if (selected != null)
            {
                var pos = selected.GetPosition(profile);
                ImGui.SetNextItemWidth(160);
                if (ImGui.DragFloat2($"{selected.Label}##pos", ref pos, 1f, -4000f, 4000f, "%.0f"))
                {
                    selected.SetPosition(profile, Gfx.Floor(pos));
                    Plugin.MarkDirty();
                }

                ImGui.SameLine();
                if (ImGui.SmallButton("Reset"))
                {
                    selected.ResetPosition(profile);
                    Plugin.MarkDirty();
                }
            }
            else
            {
                ImGui.TextDisabled("Click an element to select it.");
            }

            ImGui.Separator();

            if (ImGui.Button(selected != null ? $"{selected.Label} Settings" : "Settings"))
            {
                if (selected != null)
                {
                    OpenElementSettingsRequested?.Invoke(selected.Id);
                }
                else
                {
                    OpenSettingsRequested?.Invoke();
                }
            }

            ImGui.SameLine();
            var ctrl = ImGui.GetIO().KeyCtrl;
            using (Dalamud.Interface.Utility.Raii.ImRaii.Disabled(!ctrl))
            {
                if (ImGui.Button("Reset All"))
                {
                    layout.Positions.Clear();
                    Plugin.MarkDirty();
                }
            }

            if (!ctrl && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Hold Ctrl to reset every element to its default position.");
            }

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, Colors.Darken(accent, 0.35f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Colors.Darken(accent, 0.15f));
            if (ImGui.Button("Lock"))
            {
                SetActive(false);
            }

            ImGui.PopStyleColor(2);
        }

        ImGui.End();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(2);
    }

    private HudElement? Selected()
    {
        if (selectedId == null)
        {
            return null;
        }

        foreach (var element in elements)
        {
            if (element.Id == selectedId)
            {
                return element;
            }
        }

        return null;
    }

    private static Vector2 Snap(Vector2 v, int grid)
    {
        grid = Math.Max(1, grid);
        return new Vector2(MathF.Round(v.X / grid) * grid, MathF.Round(v.Y / grid) * grid);
    }
}
