using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using LucentUI.Config;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace LucentUI.Hud;

/// <summary>
/// Mouse handling for frames: click to target, right-click menu and the game's &lt;mo&gt; mouseover target.
/// Frames are drawn in a click-through window, so hovering is checked against rectangles instead.
/// </summary>
public sealed unsafe class Interaction
{
    private IGameObject? hovered;
    private nint currentMouseover;
    private bool blocked;

    /// <summary>The unit under the mouse on the last drawn frame (used for mouseover casting).</summary>
    public IGameObject? Hovered { get; private set; }

    /// <summary>True while an element is redrawn for clipping; only the first pass handles input.</summary>
    public bool Suppressed;

    /// <summary>Call inside the HUD window, before elements draw.</summary>
    public void BeginFrame(ClipRects clips)
    {
        hovered = null;
        Suppressed = false;

        // Another ImGui window (settings, other plugins) or a game window is under the mouse: leave input to it.
        blocked = ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow) ||
                  ImGui.GetIO().WantTextInput ||
                  (clips.Enabled && clips.Contains(ImGui.GetMousePos()));
    }

    public bool IsHovering(Vector2 min, Vector2 max) => !blocked && !Suppressed && ImGui.IsMouseHoveringRect(min, max, false);

    /// <summary>Claims the mouse for a frame rectangle and handles clicks. Returns true while hovered.</summary>
    public bool HandleUnit(Vector2 min, Vector2 max, IGameObject? obj, GeneralSettings general)
    {
        if (obj == null || !IsHovering(min, max))
        {
            return false;
        }

        // Stop the game from also reacting to the click.
        ImGui.SetNextFrameWantCaptureMouse(true);
        hovered = obj;

        if (general.ClickToTarget && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            Svc.TargetManager.Target = obj;
        }
        else if (general.RightClickMenu && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            AgentHUD.Instance()->OpenContextMenuFromTarget((GameObjectStruct*)obj.Address);
        }

        return true;
    }

    /// <summary>Call after all elements drew: updates the game's mouseover target.</summary>
    public void EndFrame(GeneralSettings general)
    {
        Hovered = hovered;
        var address = general.MouseoverTarget && hovered != null ? hovered.Address : nint.Zero;
        SetMouseover(address);
    }

    public void Clear()
    {
        Hovered = null;
        SetMouseover(nint.Zero);
    }

    private void SetMouseover(nint address)
    {
        // Keep writing while hovering (the game can overwrite it), but only clear once.
        if (address == nint.Zero && currentMouseover == nint.Zero)
        {
            return;
        }

        currentMouseover = address;

        var uiModule = GameFramework.Instance()->GetUIModule();
        if (uiModule == null)
        {
            return;
        }

        var pronouns = uiModule->GetPronounModule();
        if (pronouns != null)
        {
            pronouns->UiMouseOverTarget = (GameObjectStruct*)address;
        }
    }
}
