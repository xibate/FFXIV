using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;

namespace LucentUI.Config;

/// <summary>
/// Converts an EllesmereUI (World of Warcraft) profile string into a LucentUI profile.
/// <para>
/// Format: <c>!EUI_</c> + LibDeflate print encoding of a raw DEFLATE stream of EllesmereUI's own table serializer.
/// Only settings with an FFXIV equivalent are carried over: unit frame, cast bar, aura and party/raid frame sizes,
/// positions, text, colours and the accent colour. Everything else is listed in the notes.
/// </para>
/// </summary>
public static class EllesmereImport
{
    public const string Prefix = "!EUI_";

    private const int Border = 1;

    public static bool LooksLike(string text) => text.TrimStart().StartsWith(Prefix, StringComparison.Ordinal);

    public sealed class Result
    {
        public Profile Profile = new();
        public List<string> Notes = new();
    }

    /// <summary>Converts a profile string. <paramref name="screenSize"/> is used to scale sizes and positions.</summary>
    public static bool TryConvert(string text, Vector2 screenSize, out Result result, out string error)
    {
        result = new Result();
        error = string.Empty;

        object? payload;
        try
        {
            payload = Decode(text);
        }
        catch (Exception)
        {
            error = "This is not a valid EllesmereUI profile string.";
            return false;
        }

        var data = Table(Get(payload, "data"));
        if (data == null)
        {
            error = "The EllesmereUI string has no profile data.";
            return false;
        }

        // A full-account export wraps the active profile: data.profiles[name], with account-wide
        // settings (UI scale, unlock-mode links) at data's root.
        Dictionary<object, object?> profile = data;
        Dictionary<object, object?>? account = null;
        if (Bool(Get(payload, "fullExport")) == true || Str(Get(payload, "type")) == "fullaccount" || Table(Get(data, "profiles")) != null)
        {
            account = data;
            var profiles = Table(Get(data, "profiles"));
            var name = Str(Get(payload, "profileName")) ?? Str(Get(data, "activeProfile")) ?? "Default";
            profile = Table(Get(profiles, name)) ?? profiles?.Values.OfType<Dictionary<object, object?>>().FirstOrDefault() ?? new Dictionary<object, object?>();
        }

        var converter = new Converter(profile, account, screenSize, result);
        converter.Run();
        if (!converter.ConvertedAnything)
        {
            error = "The EllesmereUI string has no unit frames, resource bars, cooldown bars or party frames to convert.";
            return false;
        }

        return true;
    }

    // ── Decoding ─────────────────────────────────────────────────────────────

    /// <summary>Decodes a <c>!EUI_</c> string into nested tables (Dictionary&lt;object, object?&gt;), strings, doubles and bools.</summary>
    public static object? Decode(string text)
    {
        text = text.Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException("Missing !EUI_ prefix");
        }

        var compressed = DecodeForPrint(text[Prefix.Length..]);
        using var input = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        var bytes = output.ToArray();

        var pos = 0;
        return ReadValue(bytes, ref pos);
    }

    /// <summary>LibDeflate's DecodeForPrint: 6 bits per character, least significant bits first.</summary>
    public static byte[] DecodeForPrint(string text)
    {
        var result = new List<byte>(text.Length * 3 / 4);
        uint cache = 0;
        var bits = 0;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                continue;
            }

            cache |= (uint)SixBit(ch) << bits;
            bits += 6;
            while (bits >= 8)
            {
                result.Add((byte)(cache & 0xFF));
                cache >>= 8;
                bits -= 8;
            }
        }

        return result.ToArray();
    }

    private static int SixBit(char c) => c switch
    {
        >= 'a' and <= 'z' => c - 'a',
        >= 'A' and <= 'Z' => c - 'A' + 26,
        >= '0' and <= '9' => c - '0' + 52,
        '(' => 62,
        ')' => 63,
        _ => throw new FormatException($"Unexpected character '{c}'"),
    };

    /// <summary>Port of EllesmereUI's DeserializeValue. String lengths are in bytes, as in Lua.</summary>
    private static object? ReadValue(byte[] s, ref int pos)
    {
        if (pos >= s.Length)
        {
            return null;
        }

        var tag = (char)s[pos];
        switch (tag)
        {
            case 's':
            {
                var colon = Array.IndexOf(s, (byte)':', pos + 1);
                if (colon < 0)
                {
                    throw new FormatException("Bad string");
                }

                var length = int.Parse(Encoding.ASCII.GetString(s, pos + 1, colon - pos - 1), CultureInfo.InvariantCulture);
                var value = Encoding.UTF8.GetString(s, colon + 1, length);
                pos = colon + 1 + length;
                return value;
            }

            case 'n':
            {
                var semi = Array.IndexOf(s, (byte)';', pos + 1);
                if (semi < 0)
                {
                    throw new FormatException("Bad number");
                }

                var raw = Encoding.ASCII.GetString(s, pos + 1, semi - pos - 1);
                pos = semi + 1;
                return ParseLuaNumber(raw);
            }

            case 'T':
                pos++;
                return true;
            case 'F':
                pos++;
                return false;
            case 'N':
                pos++;
                return null;

            case '{':
            {
                var table = new Dictionary<object, object?>();
                var index = 1d;
                pos++;
                while (pos < s.Length)
                {
                    var c = (char)s[pos];
                    if (c == '}')
                    {
                        pos++;
                        return table;
                    }

                    if (c == 'K')
                    {
                        pos++;
                        var key = ReadValue(s, ref pos);
                        var value = ReadValue(s, ref pos);
                        if (key != null)
                        {
                            table[key] = value;
                        }
                    }
                    else
                    {
                        table[index] = ReadValue(s, ref pos);
                        index++;
                    }
                }

                return table;
            }

            default:
                pos++;
                return null;
        }
    }

    private static double? ParseLuaNumber(string raw)
    {
        return raw switch
        {
            "inf" => double.PositiveInfinity,
            "-inf" => double.NegativeInfinity,
            "nan" or "-nan" => double.NaN,
            _ => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
        };
    }

    // ── Table helpers ────────────────────────────────────────────────────────

    private static Dictionary<object, object?>? Table(object? value) => value as Dictionary<object, object?>;

    private static object? Get(object? table, string key) =>
        Table(table) is { } t && t.TryGetValue(key, out var value) ? value : null;

    private static double? Num(object? value) => value as double?;

    private static string? Str(object? value) => value as string;

    private static bool? Bool(object? value) => value as bool?;

    private static Vector4? Color(object? value, float alpha = 1f)
    {
        if (Table(value) is not { } t)
        {
            return null;
        }

        var r = Num(Get(t, "r"));
        var g = Num(Get(t, "g"));
        var b = Num(Get(t, "b"));
        if (r == null || g == null || b == null)
        {
            return null;
        }

        var a = Num(Get(t, "a")) ?? alpha;
        return new Vector4((float)r, (float)g, (float)b, (float)a);
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    /// <summary>A converted element: its LucentUI id, size and top-left offset from the screen centre.</summary>
    private sealed class Placement
    {
        public string Id = string.Empty;
        public Vector2 Size;
        public Vector2? TopLeft;

        /// <summary>Applies a matched width to the element's settings (null when the width is fixed).</summary>
        public Action<float>? SetWidth;

        /// <summary>EllesmereUI key of the frame this cast bar is locked under (no saved position of its own).</summary>
        public string? LockedUnder;
    }

    /// <summary>
    /// Maps an EllesmereUI profile onto LucentUI settings. Every value falls back to EllesmereUI's own default when
    /// the export leaves it out (exports can omit untouched settings), so the result matches what was on screen.
    /// </summary>
    private sealed class Converter
    {
        private const string UnitFrames = "EllesmereUIUnitFrames";
        private const string ResourceBars = "EllesmereUIResourceBars";
        private const string CooldownManager = "EllesmereUICooldownManager";
        private const string RaidFrames = "EllesmereUIRaidFrames";
        private const string Nameplates = "EllesmereUINameplates";

        private static readonly HashSet<string> Handled = new() { UnitFrames, ResourceBars, CooldownManager, RaidFrames, Nameplates };

        private readonly Dictionary<object, object?> profile;
        private readonly Dictionary<object, object?>? account;
        private readonly Vector2 screen;
        private readonly Result result;
        private readonly float scale;
        private readonly List<string> converted = new();
        private readonly Dictionary<string, Placement> placed = new();

        public Converter(Dictionary<object, object?> profile, Dictionary<object, object?>? account, Vector2 screen, Result result)
        {
            this.profile = profile;
            this.account = account;
            this.screen = screen;
            this.result = result;

            // WoW UI units → pixels. EllesmereUI's pixel-perfect scale maps 1 unit to 1 pixel on the exporter's
            // screen; without a scale, assume that on a 1080p screen. Scale to this screen's height.
            var uiScale = Num(Get(profile, "uiScale")) ?? Num(Get(account, "ppUIScale")) ?? Num(Get(account, "uiScale"));
            scale = uiScale is > 0 ? (float)uiScale.Value * screen.Y / 768f : screen.Y / 1080f;
        }

        public bool ConvertedAnything { get; private set; }

        private Profile P => result.Profile;

        public void Run()
        {
            var addons = Table(Get(profile, "addons"));

            Appearance();

            var uf = Table(Get(addons, UnitFrames));
            if (uf != null)
            {
                UnitFrameModule(uf);
            }

            var rb = Table(Get(addons, ResourceBars));
            if (rb != null)
            {
                ResourceBarModule(rb, uf);
            }

            if (Table(Get(addons, CooldownManager)) is { } cdm)
            {
                CooldownModule(cdm);
            }

            if (Table(Get(addons, RaidFrames)) is { } raid)
            {
                PartyModule(raid);
            }

            if (addons?.ContainsKey(Nameplates) == true)
            {
                P.Modules.Nameplates = true;
                converted.Add("nameplates (turned on)");
            }

            ApplyLinks();

            foreach (var placement in placed.Values)
            {
                if (placement.TopLeft is { } pos)
                {
                    P.Layout.Positions[placement.Id] = pos;
                }
            }

            ConvertedAnything = converted.Count > 0;
            if (ConvertedAnything)
            {
                result.Notes.Insert(0, "Converted: " + string.Join(", ", converted) + ".");
            }

            var skipped = addons?.Keys.OfType<string>().Where(k => !Handled.Contains(k)).Select(Pretty).OrderBy(n => n).ToList() ?? new List<string>();
            if (skipped.Count > 0)
            {
                result.Notes.Add("No FFXIV equivalent, skipped: " + string.Join(", ", skipped) + ".");
            }

            result.Notes.Add("WoW spells cannot be carried over: the Cooldown Manager fills its bars with your FFXIV job's own cooldowns (change them on the Cooldown Manager page).");
        }

        // ── Appearance ───────────────────────────────────────────────────────

        private void Appearance()
        {
            if (Table(Get(profile, "euiAccent")) is { } accent && Bool(Get(accent, "useClass")) != true && Color(Get(accent, "custom")) is { } color)
            {
                P.Style.Accent = color;
            }

            if (Table(Get(profile, "fonts")) is { } fonts && Str(Get(fonts, "outlineMode")) is { } outline)
            {
                P.Style.TextOutline = outline != "none";
            }
        }

        // ── Unit frames ──────────────────────────────────────────────────────

        private sealed record FrameDefaults(string EuiKey, string Element, string Label, float Width, float Health, float Power, bool PowerShown, bool ClassColored, string Left, string Right, float X, float Y);

        private static readonly FrameDefaults[] Frames =
        {
            new("player", "unit.player", "player frame", 181, 46, 6, true, true, "name", "both", -317, -193.5f),
            new("target", "unit.target", "target frame", 181, 46, 6, true, true, "name", "both", 317, -201),
            new("targettarget", "unit.tot", "target of target", 101, 25, 0, false, false, "name", "none", 383, -152.5f),
            new("focus", "unit.focus", "focus frame", 160, 34, 6, true, true, "name", "perhp", 0, -285),
        };

        private void UnitFrameModule(Dictionary<object, object?> uf)
        {
            var dark = Bool(Get(uf, "darkTheme")) == true;
            if (dark)
            {
                P.Style.HealthColors = HealthColorMode.Dark;
            }

            if (Color(Get(uf, "castbarColor")) is { } castColor)
            {
                P.Style.Cast = castColor;
            }

            var enabled = Table(Get(uf, "enabledFrames"));
            var positions = Table(Get(uf, "positions"));

            foreach (var d in Frames)
            {
                var frame = Table(Get(uf, d.EuiKey)) ?? new Dictionary<object, object?>();
                var s = FrameSettings(d.EuiKey);

                var width = Num(Get(frame, "frameWidth")) ?? d.Width;
                var health = Num(Get(frame, "healthHeight")) ?? d.Health;
                var power = Num(Get(frame, "powerHeight")) ?? d.Power;
                var powerPosition = Str(Get(frame, "powerPosition")) ?? (d.PowerShown ? "below" : "none");
                var showPower = power > 0 && powerPosition is "below" or "above";

                var powerPx = showPower ? Math.Max(1, (int)MathF.Round((float)power * scale)) : 0;
                s.Size = new Vector2(
                    MathF.Round((float)width * scale) + 2 * Border,
                    MathF.Round((float)health * scale) + (showPower ? powerPx + Border : 0) + 2 * Border);
                s.ShowPower = showPower;
                if (showPower)
                {
                    s.PowerHeight = powerPx;
                }

                if (!dark)
                {
                    s.ColorMode = (Bool(Get(frame, "healthClassColored")) ?? d.ClassColored) ? HealthColorMode.Job : HealthColorMode.Flat;
                }

                if (d.EuiKey == "player" && Color(Get(frame, "customBgColor")) is { } background)
                {
                    P.Style.Background = background with { W = P.Style.Background.W };
                }

                s.LeftText = TextTemplate(Str(Get(frame, "leftTextContent")) ?? d.Left, s.LeftText);
                s.RightText = TextTemplate(Str(Get(frame, "rightTextContent")) ?? d.Right, s.RightText);
                s.CenterText = TextTemplate(Str(Get(frame, "centerTextContent")) ?? "none", s.CenterText);
                s.ReverseFill = Bool(Get(frame, "healthReverseFill")) ?? false;
                s.Enabled = Bool(Get(enabled, d.EuiKey)) ?? true;

                var captured = s;
                Place(d.EuiKey, d.Element, s.Size,
                    Position(Table(Get(positions, d.EuiKey)), s.Size) ?? FromCentre(d.X, d.Y, s.Size),
                    w => captured.Size = captured.Size with { X = MathF.Round(w) });
                converted.Add(d.Label);
            }

            UnitCastBars(uf, positions);
            Auras(Table(Get(uf, "playerAuraBars")));
        }

        private UnitFrameSettings FrameSettings(string euiKey) => euiKey switch
        {
            "player" => P.Player,
            "target" => P.Target,
            "targettarget" => P.TargetOfTarget,
            _ => P.Focus,
        };

        private void UnitCastBars(Dictionary<object, object?> uf, Dictionary<object, object?>? positions)
        {
            var player = Table(Get(uf, "player"));
            if (Bool(Get(player, "showPlayerCastbar")) == true)
            {
                var size = Scaled(Num(Get(player, "playerCastbarWidth")) ?? 181, Num(Get(player, "playerCastbarHeight")) ?? 14);
                P.PlayerCast.Size = size;
                P.PlayerCast.ShowIcon = Bool(Get(player, "showPlayerCastIcon")) ?? true;
                PlaceCastBar("playerCastbar", "cast.player", "player", size, positions, P.PlayerCast);
                converted.Add("player cast bar");
            }

            foreach (var (key, unit, element, settings, defaultWidth) in new[]
            {
                ("targetCastbar", "target", "cast.target", P.TargetCast, 181f),
                ("focusCastbar", "focus", "cast.focus", P.FocusCast, 160f),
            })
            {
                var frame = Table(Get(uf, unit));
                settings.Enabled = Bool(Get(frame, "showCastbar")) ?? true;
                var size = Scaled(Num(Get(frame, "castbarWidth")) ?? defaultWidth, Num(Get(frame, "castbarHeight")) ?? 14);
                settings.Size = size;
                PlaceCastBar(key, element, unit, size, positions, settings);
            }

            converted.Add("target and focus cast bars");
        }

        private void PlaceCastBar(string key, string element, string frameKey, Vector2 size, Dictionary<object, object?>? positions, CastBarSettings settings)
        {
            // Without a saved position, EllesmereUI locks the cast bar under its frame.
            var topLeft = Position(Table(Get(positions, key)), size);
            Place(key, element, size, topLeft, w => settings.Size = settings.Size with { X = MathF.Round(w) });
            if (topLeft == null)
            {
                placed[key].LockedUnder = frameKey;
            }
        }

        private void Auras(Dictionary<object, object?>? auras)
        {
            auras ??= new Dictionary<object, object?>();
            var size = Num(Get(auras, "iconSize")) ?? 32;

            // WoW aura icons are square; FFXIV status icons are 3:4, so match the width.
            var height = MathF.Round((float)size * scale / 0.75f);
            P.PlayerBuffs.IconHeight = height;
            P.PlayerDebuffs.IconHeight = height;
            P.PlayerBuffs.PerRow = (int)(Num(Get(auras, "iconsPerRowBuffs")) ?? 11);
            P.PlayerBuffs.MaxIcons = (int)(Num(Get(auras, "maxBuffs")) ?? 32);
            P.PlayerBuffs.Spacing = (int)MathF.Round((float)(Num(Get(auras, "paddingBuffs")) ?? 5) * scale);
            P.PlayerDebuffs.PerRow = (int)(Num(Get(auras, "iconsPerRowDebuffs")) ?? 8);
            P.PlayerDebuffs.MaxIcons = (int)(Num(Get(auras, "maxDebuffs")) ?? 16);
            P.PlayerDebuffs.Spacing = (int)MathF.Round((float)(Num(Get(auras, "paddingDebuffs")) ?? 5) * scale);
            converted.Add("player auras");
        }

        // ── Resource bars (cast bar, power bar, GCD) ─────────────────────────

        private void ResourceBarModule(Dictionary<object, object?> rb, Dictionary<object, object?>? uf)
        {
            var general = Table(Get(rb, "general"));
            var groupCentre = new Vector2((float)(Num(Get(general, "anchorX")) ?? 0), (float)(Num(Get(general, "anchorY")) ?? -100));

            // Cast bar: Resource Bars owns the player cast bar unless the unit frame one is turned on.
            var cast = Table(Get(rb, "castBar")) ?? new Dictionary<object, object?>();
            var unitFrameCast = Bool(Get(Table(Get(uf, "player")), "showPlayerCastbar")) == true;
            if (!unitFrameCast && Bool(Get(cast, "enabled")) != false)
            {
                var s = P.PlayerCast;
                s.Enabled = true;
                s.Size = Scaled(Num(Get(cast, "width")) ?? 220, Num(Get(cast, "height")) ?? 20);
                s.ShowIcon = Bool(Get(cast, "showIcon")) ?? true;
                s.ShowTimer = Bool(Get(cast, "showTimer")) ?? true;
                s.ShowName = Bool(Get(cast, "showSpellText")) ?? true;
                P.Style.Cast = RgbFields(cast, "fill") ?? Colors(0.898f, 0.729f, 0.267f);
                var topLeft = Position(Table(Get(cast, "unlockPos")), s.Size)
                    ?? FromCentre(Num(Get(cast, "anchorX")) ?? 0, Num(Get(cast, "anchorY")) ?? -54, s.Size);
                Place("ERB_CastBar", "cast.player", s.Size, topLeft, w => s.Size = s.Size with { X = MathF.Round(w) });
                converted.Add("cast bar");
            }

            // Power / class resource bars → the job gauge.
            var primary = Table(Get(rb, "primary")) ?? new Dictionary<object, object?>();
            var secondary = Table(Get(rb, "secondary")) ?? new Dictionary<object, object?>();
            var usePrimary = Bool(Get(primary, "enabled")) != false;
            var bar = usePrimary ? primary : secondary;
            if (usePrimary || Bool(Get(secondary, "enabled")) != false)
            {
                var g = P.JobGauge;
                var width = usePrimary ? Num(Get(primary, "width")) ?? 214 : Num(Get(secondary, "pipWidth")) ?? 214;
                var height = usePrimary ? Num(Get(primary, "height")) ?? 14 : Num(Get(secondary, "pipHeight")) ?? 20;
                g.Enabled = true;
                g.Width = MathF.Round((float)width * scale);
                g.BarHeight = MathF.Round((float)height * scale);
                var size = new Vector2(g.Width, g.BarHeight);
                var offset = new Vector2((float)(Num(Get(bar, "offsetX")) ?? 0), (float)(Num(Get(bar, "offsetY")) ?? (usePrimary ? -54 : -38)));
                var centre = groupCentre + offset;
                var topLeft = Position(Table(Get(bar, "unlockPos")), size) ?? FromCentre(centre.X, centre.Y, size);
                Place(usePrimary ? "ERB_Power" : "ERB_ClassResource", "jobgauge", size, topLeft, w => g.Width = MathF.Round(w));
                converted.Add("resource bars (as the job gauge)");
            }

            // GCD bar.
            var gcd = Table(Get(rb, "gcdBar")) ?? new Dictionary<object, object?>();
            P.Gcd.Enabled = Bool(Get(gcd, "enabled")) ?? false;
            if (P.Gcd.Enabled)
            {
                P.Gcd.Size = Scaled(Num(Get(gcd, "width")) ?? 220, Num(Get(gcd, "height")) ?? 12);
                P.Gcd.Color = RgbFields(gcd, "fill") ?? Colors(0.267f, 0.729f, 0.898f);
                P.Gcd.Visibility.Mode = VisibilityMode.Always;
                Place("ERB_GCDBar", "gcd", P.Gcd.Size, Position(Table(Get(gcd, "unlockPos")), P.Gcd.Size), w => P.Gcd.Size = P.Gcd.Size with { X = MathF.Round(w) });
                converted.Add("GCD bar");
            }
        }

        // ── Cooldown manager ─────────────────────────────────────────────────

        private void CooldownModule(Dictionary<object, object?> cdm)
        {
            var bars = Table(Get(cdm, "cdmBars"));
            if (Bool(Get(bars, "enabled")) == false)
            {
                P.Modules.CooldownManager = false;
                return;
            }

            var positions = Table(Get(cdm, "cdmBarPositions"));
            var list = Table(Get(bars, "bars"));
            var found = new HashSet<string>();
            var custom = 0;

            foreach (var entry in list?.Values.OfType<Dictionary<object, object?>>() ?? Enumerable.Empty<Dictionary<object, object?>>())
            {
                var key = Str(Get(entry, "key")) ?? string.Empty;
                var settings = key switch
                {
                    "cooldowns" => P.CooldownManager.Cooldowns,
                    "utility" => P.CooldownManager.Utility,
                    "buffs" => P.CooldownManager.Buffs,
                    _ => null,
                };

                if (settings == null)
                {
                    custom++;
                    continue;
                }

                found.Add(key);
                ApplyCooldownBar(settings, entry, Table(Get(positions, key)), key);
            }

            // Bars missing from the export use EllesmereUI's defaults.
            foreach (var key in new[] { "cooldowns", "utility", "buffs" })
            {
                if (!found.Contains(key))
                {
                    var settings = key == "cooldowns" ? P.CooldownManager.Cooldowns : key == "utility" ? P.CooldownManager.Utility : P.CooldownManager.Buffs;
                    ApplyCooldownBar(settings, new Dictionary<object, object?> { ["key"] = key }, Table(Get(positions, key)), key);
                }
            }

            P.Modules.CooldownManager = true;
            converted.Add("cooldown manager bars");
            if (custom > 0)
            {
                result.Notes.Add($"{custom} extra cooldown bar(s) were skipped; LucentUI has Cooldowns, Utility and Buffs bars.");
            }
        }

        private void ApplyCooldownBar(CooldownBarSettings s, Dictionary<object, object?> bar, Dictionary<object, object?>? position, string key)
        {
            var defaultSize = key switch
            {
                "cooldowns" => 42,
                "utility" => 36,
                _ => 32,
            };

            s.Enabled = Bool(Get(bar, "enabled")) ?? true;
            s.IconSize = MathF.Round((float)(Num(Get(bar, "iconSize")) ?? defaultSize) * scale);
            s.Rows = Math.Max(1, (int)(Num(Get(bar, "numRows")) ?? 1));
            s.Spacing = Math.Max(0, (int)MathF.Round((float)(Num(Get(bar, "spacing")) ?? 2) * scale));
            s.ShowCooldownText = Bool(Get(bar, "showCooldownText")) ?? true;

            // EllesmereUI bars grow from the anchor point they were saved with.
            var point = (Str(Get(position, "point")) ?? "CENTER").ToUpperInvariant();
            s.Align = point.Contains("LEFT") ? BarAlign.Left : point.Contains("RIGHT") ? BarAlign.Right : BarAlign.Center;

            var size = Hud.Elements.CooldownBarElement.BoxSize(s);
            Place("CDM_" + key, "cdm." + key, size, Position(position, size), null);
        }

        // ── Party frames ─────────────────────────────────────────────────────

        private void PartyModule(Dictionary<object, object?> raid)
        {
            var s = P.Party;
            var width = Num(Get(raid, "partyFrameWidth")) ?? Num(Get(raid, "frameWidth")) ?? 125;
            var height = Num(Get(raid, "partyFrameHeight")) ?? Num(Get(raid, "frameHeight")) ?? 60;
            s.Size = Scaled(width, height);
            s.Spacing = Math.Max(0, (int)MathF.Round((float)(Num(Get(raid, "cellSpacing")) ?? -1) * scale) + 1);
            s.Sort = (Str(Get(raid, "partySortMode")) ?? Str(Get(raid, "sortMode")) ?? "ROLE") == "ROLE" ? PartySortMode.Role : PartySortMode.Game;
            s.Horizontal = Bool(Get(raid, "partyHorizontal")) ?? false;
            s.ShowWhenSolo = Bool(Get(raid, "partyShowWhenSolo")) ?? Bool(Get(raid, "showWhenSolo")) ?? false;
            s.IncludePlayer = !(Bool(Get(raid, "partyHideSelf")) ?? false);

            // EllesmereUI's party container holds five frames; place the first one where it was.
            var container = s.Horizontal
                ? new Vector2(5 * s.Size.X + 4 * s.Spacing, s.Size.Y)
                : new Vector2(s.Size.X, 5 * s.Size.Y + 4 * s.Spacing);
            Place("RF_PartyFrames", "party", container, Position(Table(Get(raid, "partyUnlockPos")), container), null);
            converted.Add("party frames");
        }

        // ── Placement, links and width matches ───────────────────────────────

        private void Place(string euiKey, string id, Vector2 size, Vector2? topLeft, Action<float>? setWidth)
        {
            placed[euiKey] = new Placement { Id = id, Size = size, TopLeft = topLeft, SetWidth = setWidth };
        }

        /// <summary>Applies EllesmereUI's "match width" and "anchor to" links between converted elements.</summary>
        private void ApplyLinks()
        {
            var layout = Table(Get(profile, "unlockLayout"));
            var widthMatch = Table(Get(layout, "widthMatch")) ?? Table(Get(account, "unlockWidthMatch"));
            var anchors = Table(Get(layout, "anchors")) ?? Table(Get(account, "unlockAnchors"));

            foreach (var (childKey, targetKey) in widthMatch ?? new Dictionary<object, object?>())
            {
                if (childKey is string c && targetKey is string t && placed.TryGetValue(c, out var child) && placed.TryGetValue(t, out var target) && child.SetWidth != null)
                {
                    var oldWidth = child.Size.X;
                    child.SetWidth(target.Size.X);
                    child.Size = child.Size with { X = target.Size.X };
                    if (child.TopLeft is { } pos)
                    {
                        // Widths grow from the centre in EllesmereUI.
                        child.TopLeft = pos with { X = MathF.Round(pos.X - (target.Size.X - oldWidth) / 2f) };
                    }
                }
            }

            var linked = 0;
            anchors ??= new Dictionary<object, object?>();
            // Links can chain (cast bar → player frame → ...), so resolve them in a few passes.
            for (var pass = 0; pass < 4; pass++)
            {
                foreach (var (childKey, value) in anchors)
                {
                    if (childKey is not string c || Table(value) is not { } info || Str(Get(info, "target")) is not { } t)
                    {
                        continue;
                    }

                    if (!placed.TryGetValue(c, out var child) || !placed.TryGetValue(t, out var target) || target.TopLeft is not { } targetPos)
                    {
                        continue;
                    }

                    child.TopLeft = Anchored(targetPos, target.Size, child.Size, Str(Get(info, "side")), Num(Get(info, "offsetX")), Num(Get(info, "offsetY")));
                    if (pass == 0)
                    {
                        linked++;
                    }
                }
            }

            // Cast bars locked to a frame follow it wherever it ended up.
            foreach (var placement in placed.Values)
            {
                if (placement.LockedUnder != null && placed.TryGetValue(placement.LockedUnder, out var frame) && frame.TopLeft is { } framePos)
                {
                    placement.TopLeft = new Vector2(framePos.X, framePos.Y + frame.Size.Y + 2);
                }
            }

            if (linked > 0)
            {
                converted.Add($"{linked} linked position(s)");
            }
        }

        /// <summary>Port of EllesmereUI's ReapplyOwnAnchor, in LucentUI's y-down pixel space.</summary>
        private Vector2 Anchored(Vector2 targetPos, Vector2 targetSize, Vector2 childSize, string? side, double? offsetX, double? offsetY)
        {
            var ox = (float)(offsetX ?? 0) * scale;
            var oy = -(float)(offsetY ?? 0) * scale;
            var left = targetPos.X;
            var right = targetPos.X + targetSize.X;
            var top = targetPos.Y;
            var bottom = targetPos.Y + targetSize.Y;
            var cx = (left + right) / 2f;
            var cy = (top + bottom) / 2f;

            var centre = (side ?? string.Empty).ToUpperInvariant() switch
            {
                "LEFT" => new Vector2(left + ox - childSize.X / 2f, cy + oy),
                "RIGHT" => new Vector2(right + ox + childSize.X / 2f, cy + oy),
                "TOP" => new Vector2(cx + ox, top + oy - childSize.Y / 2f),
                "BOTTOM" => new Vector2(cx + ox, bottom + oy + childSize.Y / 2f),
                _ => new Vector2(cx + ox, cy + oy),
            };

            return new Vector2(MathF.Round(centre.X - childSize.X / 2f), MathF.Round(centre.Y - childSize.Y / 2f));
        }

        private Vector2 Scaled(double width, double height) =>
            new(MathF.Round((float)width * scale), MathF.Round((float)height * scale));

        /// <summary>A position given as a CENTER offset from the screen centre (WoW units, y up).</summary>
        private Vector2 FromCentre(double x, double y, Vector2 size) =>
            new(MathF.Round((float)x * scale - size.X / 2f), MathF.Round(-(float)y * scale - size.Y / 2f));

        /// <summary>Converts a WoW anchor (point/relPoint/x/y, y up) to a LucentUI top-left offset from the screen centre.</summary>
        private Vector2? Position(Dictionary<object, object?>? pos, Vector2 size)
        {
            if (pos == null || Num(Get(pos, "x")) is not { } x || Num(Get(pos, "y")) is not { } y)
            {
                return null;
            }

            var point = Anchor(Str(Get(pos, "point")));
            var relPoint = Anchor(Str(Get(pos, "relPoint")) ?? Str(Get(pos, "point")));

            var screenAnchor = new Vector2(relPoint.X * screen.X, (1f - relPoint.Y) * screen.Y);
            var framePoint = screenAnchor + new Vector2((float)x, -(float)y) * scale;
            var topLeft = framePoint - new Vector2(point.X * size.X, (1f - point.Y) * size.Y);
            return new Vector2(MathF.Round(topLeft.X - screen.X / 2f), MathF.Round(topLeft.Y - screen.Y / 2f));
        }

        /// <summary>WoW anchor name → (x, y) fraction, with y measured from the bottom.</summary>
        private static Vector2 Anchor(string? name) => (name ?? "CENTER").ToUpperInvariant() switch
        {
            "TOPLEFT" => new(0, 1),
            "TOP" => new(0.5f, 1),
            "TOPRIGHT" => new(1, 1),
            "LEFT" => new(0, 0.5f),
            "RIGHT" => new(1, 0.5f),
            "BOTTOMLEFT" => new(0, 0),
            "BOTTOM" => new(0.5f, 0),
            "BOTTOMRIGHT" => new(1, 0),
            _ => new(0.5f, 0.5f),
        };

        /// <summary>Colours stored as prefixR/prefixG/prefixB fields (e.g. fillR, fillG, fillB).</summary>
        private static Vector4? RgbFields(Dictionary<object, object?> table, string prefix)
        {
            var r = Num(Get(table, prefix + "R"));
            var g = Num(Get(table, prefix + "G"));
            var b = Num(Get(table, prefix + "B"));
            return r != null && g != null && b != null ? new Vector4((float)r, (float)g, (float)b, 1f) : null;
        }

        private static Vector4 Colors(float r, float g, float b) => new(r, g, b, 1f);

        /// <summary>EllesmereUI text content keys → LucentUI text tags.</summary>
        private static string TextTemplate(string? content, string fallback) => content switch
        {
            null => fallback,
            "none" => string.Empty,
            "name" => "{name}",
            "levelname" => "{level} {name}",
            "both" => "{hp} | {hp:pct}%",
            "bothdash" => "{hp} - {hp:pct}%",
            "perhpnum" => "{hp:pct}% | {hp}",
            "perhpnumdash" => "{hp:pct}% - {hp}",
            "curhpshort" or "healthshort" => "{hp}",
            "curmaxhp" => "{hp} / {hp:max}",
            "perhp" => "{hp:pct}%",
            "perhpnosign" => "{hp:pct}",
            "perpp" => "{mp:pct}%",
            "curpp" => "{mp}",
            "curmaxpp" => "{mp} / {mp:max}",
            "curhp_curpp" => "{hp} | {mp}",
            "perhp_perpp" => "{hp:pct}% | {mp:pct}%",
            "level" => "{level}",
            _ => fallback,
        };

        private static string Pretty(string folder) =>
            folder.StartsWith("EllesmereUI", StringComparison.Ordinal) && folder.Length > "EllesmereUI".Length
                ? folder["EllesmereUI".Length..]
                : folder;
    }
}
