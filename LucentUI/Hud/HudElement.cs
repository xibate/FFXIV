using System.Numerics;
using Dalamud.Bindings.ImGui;
using LucentUI.Config;
using LucentUI.Style;

namespace LucentUI.Hud;

/// <summary>Per-frame state handed to every element while it draws.</summary>
public sealed class DrawContext
{
    public ImDrawListPtr DrawList;
    public Profile Profile = null!;
    public GeneralSettings General = null!;
    public Fonts Fonts = null!;
    public Interaction Input = null!;
    public Smoother Smoother = null!;
    public ClipRects Clips = null!;
    public float DeltaTime;

    /// <summary>True in Unlock Mode: elements draw sample data when the real unit is missing.</summary>
    public bool Preview;

    /// <summary>Opacity multiplier applied to every colour.</summary>
    public float Alpha = 1f;

    public StyleSettings Style => Profile.Style;

    public uint Col(Vector4 color) => Colors.ToU32(color, Alpha);

    /// <summary>Eased value for a bar, keyed by element and unit.</summary>
    public float Smooth(long key, float target) => key == 0 ? target : Smoother.Get(key, target, DeltaTime);

    public static long Key(string elementId, ulong objectId) => ((long)elementId.GetHashCode() << 32) ^ (long)objectId;
}

/// <summary>One movable piece of the HUD (a unit frame, a cast bar, an aura row...).</summary>
public abstract class HudElement
{
    /// <summary>Stable id used as the key for saved positions.</summary>
    public abstract string Id { get; }

    public abstract string Label { get; }

    /// <summary>Default top-left offset from the screen centre.</summary>
    public abstract Vector2 DefaultPosition { get; }

    public abstract bool IsEnabled(Profile profile);

    public abstract Vector2 GetSize(Profile profile);

    public virtual VisibilitySettings? GetVisibility(Profile profile) => null;

    public abstract void Draw(DrawContext ctx, Vector2 pos);

    /// <summary>False for elements drawn across the screen (like nameplates) that have no mover.</summary>
    public virtual bool Movable => true;

    public Vector2 GetPosition(Profile profile) =>
        profile.Layout.Positions.TryGetValue(Id, out var pos) ? pos : DefaultPosition;

    public void SetPosition(Profile profile, Vector2 pos) => profile.Layout.Positions[Id] = pos;

    public void ResetPosition(Profile profile) => profile.Layout.Positions.Remove(Id);
}
