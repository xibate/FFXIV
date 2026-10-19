using System.Collections.Generic;
using System.Numerics;
using LucentUI.Style;

namespace LucentUI.Config;

// Settings use public fields so they can be bound directly with ImGui's `ref` widgets.
// Every new setting must have a default that keeps current behaviour (opt-in only).

public enum HealthColorMode
{
    /// <summary>Use the global choice from the Style page.</summary>
    Inherit,
    Job,
    Role,
    Flat,
    Gradient,
    /// <summary>Dark fill, with the job colour showing in the missing-health area.</summary>
    Dark,
}

public enum VisibilityMode
{
    Always,
    InCombat,
    InCombatOrTargeting,
    OutOfCombat,
    InDuty,
    Never,
}

public enum PartySortMode
{
    Role,
    PartyList,
}

public sealed class VisibilitySettings
{
    public VisibilityMode Mode = VisibilityMode.Always;

    /// <summary>Opacity used while the element is "hidden" by <see cref="Mode"/>. 0 hides it completely.</summary>
    public float HiddenAlpha = 0f;
}

/// <summary>Global settings that do not change between profiles.</summary>
public sealed class GeneralSettings
{
    public bool ShowHud = true;
    public bool HideWhenGameUiHidden = true;
    public bool ClickToTarget = true;
    public bool RightClickMenu = true;
    public bool MouseoverTarget = true;
    public bool SmoothFades = true;
}

public sealed class ModuleSettings
{
    public bool UnitFrames = true;
    public bool CastBars = true;
    public bool Auras = true;
    public bool PartyFrames = true;
}

public sealed class StyleSettings
{
    public Vector4 Accent = Colors.Rgb(0x3FD0C9);
    public Vector4 Background = new(0.05f, 0.05f, 0.06f, 0.85f);
    public Vector4 Border = new(0f, 0f, 0f, 1f);
    public int BorderSize = 1;
    public float Rounding = 0f;
    public bool Gradient = true;
    public float GradientStrength = 0.18f;

    public int FontSize = 16;
    public int SmallFontSize = 13;
    public bool TextOutline = true;
    public bool AbbreviateNumbers = true;

    public HealthColorMode HealthColors = HealthColorMode.Job;
    public Vector4 FlatHealth = Colors.Rgb(0x3E9F52);
    public Vector4 DarkFill = Colors.Rgb(0x1E1E22);
    public Vector4 Hostile = Colors.Rgb(0xC8423C);
    public Vector4 Neutral = Colors.Rgb(0xD8B23A);
    public Vector4 Friendly = Colors.Rgb(0x4CAF50);
    public Vector4 Dead = Colors.Rgb(0x4A4A4A);
    public Vector4 Shield = new(1f, 0.95f, 0.65f, 0.55f);

    public Vector4 Mana = Colors.Rgb(0x3D8BF2);
    public Vector4 Gp = Colors.Rgb(0x5FC4D9);
    public Vector4 Cp = Colors.Rgb(0xB37CE6);

    public Vector4 Cast = Colors.Rgb(0xE8C547);
    public Vector4 CastInterruptible = Colors.Rgb(0xD45BD9);
    public Vector4 Slidecast = new(0.85f, 0.2f, 0.2f, 0.55f);

    public float OutOfRangeAlpha = 0.45f;
}

public sealed class UnitFrameSettings
{
    public bool Enabled = true;
    public Vector2 Size = new(260, 46);
    public bool ShowPower = true;
    public int PowerHeight = 7;
    public string LeftText = "{name}";
    public string CenterText = "";
    public string RightText = "{hp} | {hp:pct}%";
    public HealthColorMode ColorMode = HealthColorMode.Inherit;
    public bool ShowShield = true;
    public bool ReverseFill = false;
    public bool Interactive = true;
    public VisibilitySettings Visibility = new();
}

public sealed class CastBarSettings
{
    public bool Enabled = true;
    public Vector2 Size = new(300, 20);
    public bool ShowIcon = true;
    public bool ShowName = true;
    public bool ShowTimer = true;
    public bool ShowSlidecast = true;
    public float SlidecastWindow = 0.5f;
}

public sealed class AuraSettings
{
    public bool Enabled = true;
    public float IconHeight = 32f;
    public int Spacing = 3;
    public int PerRow = 10;
    public int MaxIcons = 20;
    public bool GrowLeft = false;
    public bool GrowUp = false;
    public bool ShowBuffs = true;
    public bool ShowDebuffs = true;
    public bool OnlyMine = false;
    public bool ShowPermanent = true;
    public bool ShowTimer = true;
    public bool SortByTime = false;
    public bool Tooltips = true;
    public bool RightClickToRemove = false;
}

public sealed class PartySettings
{
    public bool Enabled = true;
    public Vector2 Size = new(190, 38);
    public int Spacing = 4;
    public bool Horizontal = false;
    public bool ShowWhenSolo = false;
    public bool IncludePlayer = true;
    public PartySortMode Sort = PartySortMode.Role;
    public bool ShowPower = true;
    public int PowerHeight = 4;
    public string LeftText = "{name:14}";
    public string RightText = "{hp:pct}%";
    public HealthColorMode ColorMode = HealthColorMode.Inherit;
    public bool ShowShield = true;
    public bool RangeFade = true;
    public bool HighlightTarget = true;
    public bool Interactive = true;
    public VisibilitySettings Visibility = new();
}

/// <summary>Which parts of the game's own HUD to hide. All off by default.</summary>
public sealed class DefaultHudSettings
{
    public bool HidePlayer = false;
    public bool HideTarget = false;
    public bool HideFocus = false;
    public bool HideCastBar = false;
    public bool HideParty = false;
    public bool HideStatus = false;
}

public sealed class LayoutSettings
{
    /// <summary>Element id → top-left offset from the screen centre. Missing ids use the element's default.</summary>
    public Dictionary<string, Vector2> Positions = new();
    public int GridSize = 16;
    public bool SnapToGrid = true;
}

public sealed class Profile
{
    public ModuleSettings Modules = new();
    public StyleSettings Style = new();

    public UnitFrameSettings Player = new();
    public UnitFrameSettings Target = new();
    public UnitFrameSettings TargetOfTarget = new()
    {
        Size = new(150, 28),
        ShowPower = false,
        LeftText = "{name:16}",
        RightText = "",
    };
    public UnitFrameSettings Focus = new()
    {
        Size = new(200, 30),
        ShowPower = false,
        LeftText = "{name:18}",
        RightText = "{hp:pct}%",
    };

    public CastBarSettings PlayerCast = new();
    public CastBarSettings TargetCast = new() { Size = new(260, 18), ShowSlidecast = false };
    public CastBarSettings FocusCast = new() { Size = new(200, 14), ShowSlidecast = false, ShowIcon = false };

    public AuraSettings PlayerBuffs = new() { ShowDebuffs = false, GrowLeft = true };
    public AuraSettings PlayerDebuffs = new() { ShowBuffs = false, GrowLeft = true, IconHeight = 38f, PerRow = 8, MaxIcons = 16 };
    public AuraSettings TargetAuras = new() { GrowUp = true, PerRow = 8, MaxIcons = 16, IconHeight = 30f };

    public PartySettings Party = new();
    public DefaultHudSettings DefaultHud = new();
    public LayoutSettings Layout = new();
}
