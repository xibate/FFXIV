using System.Collections.Generic;
using System.Numerics;
using LucentUI.Style;

namespace LucentUI.Config;

// Settings use public fields so they can be bound directly with ImGui's `ref` widgets.
// Every new setting must have a default that keeps current behaviour (opt-in only).
// Avoid List<T> fields with default items: Newtonsoft appends to them on load.

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
    /// <summary>The same order as the game's party list.</summary>
    Game,
    Role,
}

public enum BarStyle
{
    Flat,
    Gradient,
    Glossy,
    Inset,
    Striped,
}

public enum FontFamily
{
    Axis,
    Jupiter,
    JupiterNumeric,
    Meidinger,
    MiedingerMid,
    TrumpGothic,
}

public enum ClipMode
{
    /// <summary>Cut the part of an element that a game window covers.</summary>
    Clip,
    /// <summary>Hide an element entirely while a game window covers any of it.</summary>
    Hide,
}

public enum TextColorMode
{
    White,
    Job,
    Custom,
}

public enum PowerPosition
{
    Below,
    Above,
}

public enum IconSide
{
    None,
    Left,
    Right,
}

public enum AuraBorder
{
    None,
    Thin,
    ByType,
}

public enum TimerPosition
{
    Bottom,
    Center,
    Below,
    Hidden,
}

public enum ListFilter
{
    /// <summary>Show everything.</summary>
    None,
    /// <summary>Hide the listed statuses.</summary>
    HideListed,
    /// <summary>Show only the listed statuses.</summary>
    OnlyListed,
}

/// <summary>How one piece of text looks: size (0 = the profile's font size), offset and colour.</summary>
public sealed class TextStyle
{
    public int FontSize = 0;
    public Vector2 Offset = Vector2.Zero;
    public TextColorMode Color = TextColorMode.White;
    public Vector4 CustomColor = Vector4.One;
}

public sealed class VisibilitySettings
{
    public VisibilityMode Mode = VisibilityMode.Always;

    /// <summary>Opacity used while the element is "hidden" by <see cref="Mode"/>. 0 hides it completely.</summary>
    public float HiddenAlpha = 0f;

    /// <summary>Overall opacity of the element while it is shown.</summary>
    public float Opacity = 1f;
}

/// <summary>Global settings that do not change between profiles.</summary>
public sealed class GeneralSettings
{
    public bool ShowHud = true;
    public bool HideWhenGameUiHidden = true;
    public bool ClickToTarget = true;
    public bool RightClickMenu = true;
    public bool MouseoverTarget = true;

    /// <summary>Actions used while hovering a frame go to that unit (when the action can target it).</summary>
    public bool MouseoverCasting = true;
    public bool SmoothFades = true;
    public bool ClipBehindWindows = false;
    public ClipMode ClipMode = ClipMode.Clip;
}

public sealed class ModuleSettings
{
    public bool UnitFrames = true;
    public bool CastBars = true;
    public bool Auras = true;
    public bool PartyFrames = true;
    public bool JobGauges = true;
    public bool CooldownManager = true;
    public bool PartyCooldowns = true;
    public bool Nameplates = false;
    public bool EnemyList = true;
    public bool Alliance = true;
    public bool Extras = true;
    public bool Hotbars = false;
}

public sealed class StyleSettings
{
    public Vector4 Accent = Colors.Rgb(0x3FD0C9);
    public Vector4 Background = new(0.05f, 0.05f, 0.06f, 0.85f);
    public Vector4 Border = new(0f, 0f, 0f, 1f);
    public int BorderSize = 1;
    public float Rounding = 0f;
    public BarStyle BarStyle = BarStyle.Gradient;
    public float GradientStrength = 0.18f;
    public int ShadowSize = 0;
    public Vector4 ShadowColor = new(0f, 0f, 0f, 0.45f);
    public bool SmoothBars = true;

    public FontFamily Font = FontFamily.Axis;

    /// <summary>A font from the media library by name; empty uses <see cref="Font"/>.</summary>
    public string FontName = string.Empty;

    /// <summary>A bar texture from the media library by name; empty uses <see cref="BarStyle"/>.</summary>
    public string BarTexture = string.Empty;

    /// <summary>Shape of every buff and debuff icon whose own shape is "Style Default".</summary>
    public IconShape IconShape = IconShape.Portrait;
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
    /// <summary>Bar texture for this element; empty uses the profile's.</summary>
    public string Texture = string.Empty;

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
    public PowerPosition PowerPosition = PowerPosition.Below;
    public TextStyle LeftStyle = new();
    public TextStyle CenterStyle = new();
    public TextStyle RightStyle = new();
    public bool CustomBackground = false;
    public Vector4 Background = new(0.05f, 0.05f, 0.06f, 0.85f);
    public IconSide JobIcon = IconSide.None;
    public VisibilitySettings Visibility = new();
}

public sealed class CastBarSettings
{
    /// <summary>Bar texture for this element; empty uses the profile's.</summary>
    public string Texture = string.Empty;

    public bool Enabled = true;
    public Vector2 Size = new(300, 20);
    public bool ShowIcon = true;
    public bool ShowName = true;
    public bool ShowTimer = true;
    public bool ShowSlidecast = true;
    public float SlidecastWindow = 0.5f;
    public bool IconOnRight = false;
    public TextStyle NameStyle = new();
    public TextStyle TimerStyle = new();
    public bool CustomColor = false;
    public Vector4 Color = Colors.Rgb(0xE8C547);
    public bool CustomBackground = false;
    public Vector4 Background = new(0f, 0f, 0f, 0.7f);
    public float Opacity = 1f;
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
    public AuraBorder Border = AuraBorder.None;
    public IconShape Shape = IconShape.StyleDefault;
    public TimerPosition TimerPosition = TimerPosition.Bottom;
    public int TimerFontSize = 0;
    public ListFilter Filter = ListFilter.None;

    /// <summary>Status ids used by <see cref="Filter"/>.</summary>
    public List<uint> FilterIds = new();
}

public sealed class PartySettings
{
    /// <summary>Bar texture for this element; empty uses the profile's.</summary>
    public string Texture = string.Empty;

    public bool Enabled = true;
    public Vector2 Size = new(190, 38);
    public int Spacing = 4;
    public bool Horizontal = false;
    public bool ShowWhenSolo = false;
    public bool IncludePlayer = true;
    public PartySortMode Sort = PartySortMode.Game;
    public PartyRoleOrder RoleOrder = PartyRoleOrder.TankHealerDps;
    public bool ShowPower = true;
    public int PowerHeight = 4;
    public string LeftText = "{name:14}";
    public string RightText = "{hp:pct}%";
    public string CenterText = string.Empty;
    public TextStyle LeftStyle = new();
    public TextStyle RightStyle = new();
    public TextStyle CenterStyle = new();
    public HealthColorMode ColorMode = HealthColorMode.Inherit;
    public bool ShowShield = true;
    public bool RangeFade = true;
    public bool HighlightTarget = true;
    public bool Interactive = true;
    public bool ShowJobIcon = true;

    // Trackers
    public bool ShowRaise = true;
    public bool ShowInvulnerability = true;
    public bool ShowCleansable = true;
    public bool CleansableOnlyIfICanCleanse = true;
    public Vector4 CleansableColor = Colors.Rgb(0xB65CE6);
    public bool ShowReadyCheck = true;

    /// <summary>Buff icons on each member: by default your own (HoTs, shields, mitigation).</summary>
    public StatusIconSettings Buffs = new() { OnlyMine = true, Max = 4, Size = 20f, Anchor = IconAnchor.BottomRight };

    /// <summary>Debuff icons on each member: by default just outside the frame on the right.</summary>
    public StatusIconSettings Debuffs = new() { Max = 3, Size = 22f, Anchor = IconAnchor.Right, Outside = true, Border = AuraBorder.ByType };

    public VisibilitySettings Visibility = new();
}

public sealed class GaugePartSettings
{
    public bool Enabled = true;
    public bool CustomColor = false;
    public Vector4 Color = Vector4.One;

    /// <summary>Shown as its own movable bar instead of in the stacked job gauge.</summary>
    public bool Detached = false;

    /// <summary>Size while detached; 0 uses the job gauge's width and bar height.</summary>
    public Vector2 Size = Vector2.Zero;
}

public sealed class JobGaugeSettings
{
    /// <summary>Bar texture for this element; empty uses the profile's.</summary>
    public string Texture = string.Empty;

    public bool Enabled = true;
    public float Width = 260f;
    public float BarHeight = 14f;
    public int Spacing = 3;
    public bool ShowText = true;
    public bool HideEmptyTimers = true;

    /// <summary>"JOB.part" → overrides. Missing parts use their defaults.</summary>
    public Dictionary<string, GaugePartSettings> Parts = new();

    public VisibilitySettings Visibility = new();
}

public enum BarAlign
{
    Left,
    Center,
    Right,
}

/// <summary>One cooldown/buff icon bar (like EllesmereUI's Cooldown Manager bars).</summary>
public sealed class CooldownBarSettings
{
    public bool Enabled = true;
    public float IconSize = 42f;
    public int Spacing = 2;
    public int Rows = 1;

    /// <summary>Icons per row; 0 splits the icons evenly over <see cref="Rows"/>.</summary>
    public int PerRow = 0;

    /// <summary>Icons the bar's box is sized for; icons are aligned inside it.</summary>
    public int MaxIcons = 10;
    public BarAlign Align = BarAlign.Center;
    public bool ShowCooldownText = true;
    public bool ShowCharges = true;
    public bool DimOnCooldown = true;
    public float IconZoom = 0.06f;
    public int TimerFontSize = 0;
    public bool ShowBackground = true;

    /// <summary>Shape of buff icons (Buffs bar only).</summary>
    public IconShape BuffShape = IconShape.StyleDefault;
    public VisibilitySettings Visibility = new();
}

/// <summary>Which actions a job shows on the cooldown bars. Only stored once the user changes them.</summary>
public sealed class JobCooldownSelection
{
    public List<uint> Cooldowns = new();
    public List<uint> Utility = new();
}

public enum CooldownEntryKind
{
    Action,
    Item,
}

/// <summary>One icon on a custom cooldown bar: an action (for one job, or every job) or an item such as a potion.</summary>
public sealed class CustomCooldownEntry
{
    public CooldownEntryKind Kind = CooldownEntryKind.Action;
    public uint Id;

    /// <summary>ClassJob the entry shows on; 0 shows it on every job.</summary>
    public uint Job;

    /// <summary>For items: use the high-quality version.</summary>
    public bool HighQuality;
}

/// <summary>A cooldown bar with icons the user picks, like EllesmereUI's custom Cooldown Manager bars.</summary>
public sealed class CustomCooldownBar
{
    public string Name = "Custom Bar";
    public CooldownBarSettings Bar = new() { IconSize = 36f, MaxIcons = 6 };
    public List<CustomCooldownEntry> Entries = new();

    /// <summary>Hide items you have none of (instead of showing them dimmed).</summary>
    public bool HideMissingItems = false;
}

public sealed class CooldownManagerSettings
{
    /// <summary>Extra bars with chosen actions and items (up to <see cref="MaxCustomBars"/>).</summary>
    public List<CustomCooldownBar> CustomBars = new();

    public const int MaxCustomBars = 8;

    public CooldownBarSettings Cooldowns = new();
    public CooldownBarSettings Utility = new() { IconSize = 36f, MaxIcons = 8 };
    public CooldownBarSettings Buffs = new() { IconSize = 32f, MaxIcons = 8, ShowBackground = false };

    /// <summary>Job id → custom action lists. Jobs without an entry use the automatic list.</summary>
    public Dictionary<uint, JobCooldownSelection> Jobs = new();

    /// <summary>Buffs bar: show all your own buffs, or only the ones tracked for the current job.</summary>
    public bool OnlyTrackedBuffs = false;

    /// <summary>Job id → status ids shown on the Buffs bar when <see cref="OnlyTrackedBuffs"/> is on.</summary>
    public Dictionary<uint, List<uint>> TrackedBuffs = new();

    /// <summary>Status ids never shown on the Buffs bar.</summary>
    public List<uint> HiddenBuffs = new();
}

public sealed class PartyCooldownSettings
{
    public bool Enabled = true;
    public Vector2 RowSize = new(200, 20);
    public int Spacing = 2;
    public int MaxRows = 12;
    public bool GrowUp = false;
    public bool ShowRaidBuffs = true;
    public bool ShowMitigation = true;
    public bool ShowHealing = true;
    public bool ShowTankInvulns = true;
    public bool ShowWhenSolo = false;
    public bool ShowReady = true;
    public VisibilitySettings Visibility = new();
}

public enum GameNameplateMode
{
    /// <summary>Leave the game's nameplates alone.</summary>
    Keep,

    /// <summary>Remove the game's name text from units that get a LucentUI plate (markers and other names stay).</summary>
    HideReplaced,

    /// <summary>Hide the game's nameplate window completely.</summary>
    HideAll,
}

/// <summary>A point on a frame that status icons are lined up against.</summary>
public enum IconAnchor
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>Which way a row of status icons grows; Auto grows away from the anchor's side.</summary>
public enum IconGrow
{
    Auto,
    Left,
    Right,
    Up,
    Down,
}

/// <summary>The shape status icons are drawn in.</summary>
public enum IconShape
{
    /// <summary>The game's own 3:4 status icon.</summary>
    Portrait,

    /// <summary>Cropped to a square, like WoW's aura icons.</summary>
    Square,

    /// <summary>Cropped to a circle.</summary>
    Round,

    /// <summary>Use the shape picked on the Style page.</summary>
    StyleDefault,
}

/// <summary>Buff or debuff icons on a party or alliance member.</summary>
public sealed class StatusIconSettings
{
    public bool Enabled = true;
    public bool OnlyMine = false;
    public ListFilter Filter = ListFilter.None;
    public List<uint> Ids = new();
    public int Max = 3;

    /// <summary>Icon height in pixels (square and round icons are as wide as they are high).</summary>
    public float Size = 20f;
    public IconShape Shape = IconShape.StyleDefault;
    public IconAnchor Anchor = IconAnchor.BottomRight;

    /// <summary>Line the icons up just outside the frame's edge instead of inside it.</summary>
    public bool Outside = false;
    public IconGrow Grow = IconGrow.Auto;
    public Vector2 Offset = Vector2.Zero;
    public int Spacing = 2;
    public bool Timers = true;
    public int TimerFontSize = 0;
    public AuraBorder Border = AuraBorder.None;
}

/// <summary>The order of the role groups when party frames are sorted by role.</summary>
public enum PartyRoleOrder
{
    TankHealerDps,
    TankDpsHealer,
    HealerTankDps,
    HealerDpsTank,
    DpsTankHealer,
    DpsHealerTank,
}

public enum PlateMode
{
    Hidden,
    NameOnly,
    Bar,
}

public sealed class NameplateSettings
{
    /// <summary>Bar texture for this element; empty uses the profile's.</summary>
    public string Texture = string.Empty;

    public bool Enabled = true;

    // Who gets a plate, and what kind.
    public PlateMode Enemies = PlateMode.Bar;
    public PlateMode Players = PlateMode.NameOnly;
    public PlateMode FriendlyNpcs = PlateMode.Hidden;
    public bool EnemiesOnlyInCombat = false;
    public bool ShowSelf = false;

    /// <summary>Only units the game itself would give a nameplate (respects line of sight and the game's settings).</summary>
    public bool FollowGameNameplates = true;
    public float MaxDistance = 40f;
    public GameNameplateMode GameNameplates = GameNameplateMode.HideReplaced;

    // Bar plates.
    public Vector2 Size = new(180, 16);
    public string LeftText = "{name}";
    public string RightText = "{hp:pct}%";
    public TextStyle LeftStyle = new() { Offset = new Vector2(0, -16) };
    public TextStyle RightStyle = new() { Offset = new Vector2(0, -16) };
    public string CenterText = string.Empty;
    public TextStyle CenterStyle = new();
    public HealthColorMode ColorMode = HealthColorMode.Inherit;
    public bool ShowShield = true;

    // Cast bar under bar plates.
    public bool ShowCast = true;
    public float CastHeight = 12f;
    public bool ShowCastName = true;
    public bool ShowCastIcon = true;
    public TextStyle CastNameStyle = new();

    // Name-only plates.
    public string NameOnlyText = "{name}";
    public int NameOnlyFontSize = 0;
    public bool NameOnlyJobColor = true;

    // Target.
    public bool HighlightTarget = true;
    public float TargetScale = 1.1f;
    public float NonTargetAlpha = 1f;

    /// <summary>Click a plate to target, right-click for the menu, and use it as a mouseover target.</summary>
    public bool Interactive = true;
    public float YOffset = 0f;
}

/// <summary>The two other alliance parties in 24-player content.</summary>
public sealed class AllianceSettings
{
    public bool Enabled = true;
    public Vector2 Size = new(130, 24);
    public int Spacing = 2;
    public int GroupSpacing = 10;

    /// <summary>Put the two parties next to each other instead of one above the other.</summary>
    public bool SideBySide = true;
    public string LeftText = "{name:10}";
    public string RightText = "{hp:pct}%";
    public TextStyle LeftStyle = new();
    public TextStyle RightStyle = new();
    public HealthColorMode ColorMode = HealthColorMode.Inherit;
    public string Texture = string.Empty;
    public bool RangeFade = true;
    public bool Interactive = true;
    public bool ShowJobIcon = true;

    /// <summary>Put each party's members in a row instead of a column.</summary>
    public bool HorizontalMembers = false;
    public string CenterText = string.Empty;
    public TextStyle CenterStyle = new();

    public StatusIconSettings Buffs = new() { Enabled = false, OnlyMine = true, Max = 2, Size = 16f, Anchor = IconAnchor.BottomRight };
    public StatusIconSettings Debuffs = new() { Max = 2, Size = 16f, Anchor = IconAnchor.Right, Border = AuraBorder.ByType };
    public VisibilitySettings Visibility = new();
}

public sealed class EnemyListSettings
{
    public bool Enabled = true;
    public Vector2 Size = new(180, 22);
    public int Spacing = 3;
    public int MaxEnemies = 8;
    public bool GrowUp = false;
    public bool ShowCast = true;
    public bool ShowAggro = true;
    public string LeftText = "{name:18}";
    public string RightText = "{hp:pct}%";
    public VisibilitySettings Visibility = new();
}

public sealed class GcdSettings
{
    public bool Enabled = true;
    public Vector2 Size = new(260, 6);
    public bool Circle = false;
    public float CircleRadius = 18f;
    public bool AttachToMouse = false;
    public Vector2 MouseOffset = new(0, 30);
    public Vector4 Color = Colors.Rgb(0xE6E6E6);
    public VisibilitySettings Visibility = new() { Mode = VisibilityMode.InCombat };
}

public sealed class SimpleBarSettings
{
    public bool Enabled = true;
    public Vector2 Size = new(260, 8);
    public Vector4 Color = Colors.Rgb(0x3D8BF2);
    public bool ShowText = true;
    public bool HideWhenInactive = true;
    public VisibilitySettings Visibility = new();
}

public sealed class HotbarSettings
{
    /// <summary>Hotbar number (1-10, 11 = cross hotbar) → when it is shown. Missing = always.</summary>
    public Dictionary<int, VisibilityMode> Bars = new();
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
    public bool HideJobGauge = false;
    public bool HideEnemyList = false;
    public bool HideLimitBreak = false;
    public bool HideExp = false;
    public bool HidePullTimer = false;
    public bool HideAlliance = false;
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
    public JobGaugeSettings JobGauge = new();
    public CooldownManagerSettings CooldownManager = new();
    public PartyCooldownSettings PartyCooldowns = new();
    public NameplateSettings Nameplates = new();
    public AllianceSettings Alliance = new();
    public EnemyListSettings EnemyList = new();

    public GcdSettings Gcd = new();
    public SimpleBarSettings MpTicker = new() { Size = new(260, 4), Color = Colors.Rgb(0x7FB2FF), ShowText = false };
    public SimpleBarSettings LimitBreak = new() { Size = new(300, 10), Color = Colors.Rgb(0xF0A030) };
    public SimpleBarSettings PullTimer = new() { Size = new(260, 22), Color = Colors.Rgb(0xE8C547) };
    public SimpleBarSettings Experience = new() { Size = new(500, 8), Color = Colors.Rgb(0xC48BE8), HideWhenInactive = false };

    public HotbarSettings Hotbars = new();
    public DefaultHudSettings DefaultHud = new();
    public LayoutSettings Layout = new();
}
