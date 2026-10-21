using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using LucentUI.Gauges;

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

        /// <summary>More LucentUI ids that get the same position (e.g. one resource bar mapped to every job).</summary>
        public List<string> AlsoIds = new();

        /// <summary>For bars that grow from a fixed edge: LEFT, RIGHT, UP or DOWN.</summary>
        public string? GrowDirection;
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
            scale = uiScale is > 0 ? (float)uiScale.Value * screen.Y / 768f : 1f;
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
                AllianceModule(raid);
            }

            if (addons?.ContainsKey(Nameplates) == true)
            {
                NameplateModule(Table(Get(addons, Nameplates)) ?? new Dictionary<object, object?>());
            }

            ApplyLinks();

            foreach (var placement in placed.Values)
            {
                if (placement.TopLeft is not { } pos)
                {
                    continue;
                }

                if (placement.Id.Length > 0)
                {
                    P.Layout.Positions[placement.Id] = pos;
                }

                foreach (var id in placement.AlsoIds)
                {
                    P.Layout.Positions[id] = pos;
                }
            }

            PlaceRemainingGauge();
            PlaceTargetAuras();

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

            if (texturesUsed.Count > 0 || fontsUsed.Count > 0)
            {
                var media = Plugin.Media;
                var all = fontsUsed.Select(f => (Name: f, Found: media?.HasFont(f) == true))
                    .Concat(texturesUsed.Select(t => (Name: t, Found: media?.HasTexture(t) == true))).ToList();
                var missing = all.Where(m => !m.Found).Select(m => m.Name).ToList();
                result.Notes.Add(missing.Count == 0
                    ? "Fonts and textures found in your media folders: " + string.Join(", ", all.Select(m => m.Name)) + "."
                    : "This profile uses these fonts and textures: " + string.Join(", ", all.Select(m => m.Name)) +
                      ". Not found yet: " + string.Join(", ", missing) + ". Add your WoW Interface\\AddOns folder under Style > Media and they are picked up by name.");
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

            // WoW aura icons are square; EllesmereUI layouts are sized for that.
            P.Style.IconShape = IconShape.Square;

            if (Table(Get(profile, "fonts")) is { } fonts)
            {
                if (Str(Get(fonts, "outlineMode")) is { } outline)
                {
                    P.Style.TextOutline = outline != "none";
                }

                // The global font by its SharedMedia name, or else by the file name of its path.
                var name = Str(Get(fonts, "global"));
                if (string.IsNullOrEmpty(name) || name == "__global")
                {
                    name = Path.GetFileNameWithoutExtension((Str(Get(fonts, "unitNameFontPath")) ?? string.Empty).Replace('\\', '/'));
                }

                if (!string.IsNullOrEmpty(name))
                {
                    P.Style.FontName = name;
                    fontsUsed.Add(name);
                }
            }
        }

        private readonly SortedSet<string> texturesUsed = new(StringComparer.OrdinalIgnoreCase);
        private readonly SortedSet<string> fontsUsed = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>An EllesmereUI texture key ("melli", "sm:SharedMediaName") → a media library name; empty for none.</summary>
        private string TextureName(object? value)
        {
            var name = Str(value) ?? string.Empty;
            if (name.StartsWith("sm:", StringComparison.OrdinalIgnoreCase))
            {
                name = name[3..];
            }

            if (name.Length == 0 || name is "none" or "inherit" or "blizzard" or "solid" or "default" or "__global")
            {
                return string.Empty;
            }

            texturesUsed.Add(name);
            return name;
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
            var globalTexture = TextureName(Get(uf, "healthBarTexture"));
            if (globalTexture.Length > 0)
            {
                P.Style.BarTexture = globalTexture;
            }

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
                TextLook(frame, "leftText", s.LeftStyle, 12);
                TextLook(frame, "rightText", s.RightStyle, 12);
                TextLook(frame, "centerText", s.CenterStyle, 12);
                s.PowerPosition = powerPosition == "above" ? PowerPosition.Above : PowerPosition.Below;

                if (d.EuiKey != "player" && Color(Get(frame, "customBgColor")) is { } frameBackground)
                {
                    s.CustomBackground = true;
                    s.Background = frameBackground with { W = P.Style.Background.W };
                }
                s.ReverseFill = Bool(Get(frame, "healthReverseFill")) ?? false;
                var frameTexture = TextureName(Get(frame, "healthBarTexture"));
                s.Texture = frameTexture.Equals(globalTexture, StringComparison.OrdinalIgnoreCase) ? string.Empty : frameTexture;
                s.Enabled = Bool(Get(enabled, d.EuiKey)) ?? true;
                if (Get(frame, "showPlayerAbsorb") is { } absorbStyle)
                {
                    s.ShowShield = ShieldStyle(absorbStyle) != null;
                }

                if (d.EuiKey == "player" && ImportShield(key => Get(frame, key), "showPlayerAbsorb", P.Style.Shields) is { } shieldColor)
                {
                    P.Style.Shield = shieldColor;
                }

                var captured = s;
                Place(d.EuiKey, d.Element, s.Size,
                    Position(Table(Get(positions, d.EuiKey)), s.Size) ?? FromCentre(d.X, d.Y, s.Size),
                    w => captured.Size = captured.Size with { X = MathF.Round(w) });
                converted.Add(d.Label);
            }

            UnitCastBars(uf, positions);
            Auras(Table(Get(uf, "playerAuraBars")));
            TargetAuras(Table(Get(uf, "target")));
        }

        /// <summary>EllesmereUI absorb style ("clean", "striped", "none", or an old true/false); null means no shields.</summary>
        private static ShieldLook? ShieldStyle(object? value) => value switch
        {
            false => null,
            string text when text.Equals("none", StringComparison.OrdinalIgnoreCase) => null,
            string text when text.Contains("strip", StringComparison.OrdinalIgnoreCase) => ShieldLook.Striped,
            _ => ShieldLook.Solid,
        };

        /// <summary>Copies EllesmereUI's absorb look into <paramref name="target"/> and returns its colour, if set.</summary>
        private static Vector4? ImportShield(Func<string, object?> value, string styleKey, ShieldSettings target)
        {
            if (ShieldStyle(value(styleKey)) is { } look)
            {
                target.Look = look;
            }

            target.Placement = (Str(value("absorbEdgeMode")) ?? string.Empty).ToLowerInvariant() switch
            {
                var mode when mode.StartsWith("overlay", StringComparison.Ordinal) => ShieldPlacement.Overlay,
                "left" or "fromleft" => ShieldPlacement.FromLeft,
                "right" or "fromright" => ShieldPlacement.FromRight,
                "" => target.Placement,
                _ => ShieldPlacement.AfterHealth,
            };

            if (Num(value("absorbOpacity")) is { } opacity)
            {
                target.Opacity = Math.Clamp((float)opacity / 100f, 0f, 1f);
            }

            if (Bool(value("absorbGlowLine")) is { } line)
            {
                target.EdgeLine = line;
            }

            if (Bool(value("showOvershield")) is { } overshield)
            {
                target.Overshield = overshield ? OvershieldMode.Strip : OvershieldMode.Hidden;
            }

            // Opacity is its own setting, so the colour is kept fully opaque.
            return Color(value("absorbColor")) is { } color ? color with { W = 1f } : null;
        }

        private static void OwnShieldColor(Vector4? color, ShieldSettings target)
        {
            if (color is { } c)
            {
                target.OwnColor = true;
                target.Color = c;
            }
        }

        /// <summary>Text size, offset and class-colour flag stored as prefixSize / prefixX / prefixY / prefixClassColor.</summary>
        private void TextLook(Dictionary<object, object?> table, string prefix, TextStyle style, double defaultSize)
        {
            style.FontSize = (int)MathF.Round((float)(Num(Get(table, prefix + "Size")) ?? defaultSize) * scale);
            style.Offset = new Vector2(
                MathF.Round((float)(Num(Get(table, prefix + "X")) ?? 0) * scale),
                MathF.Round(-(float)(Num(Get(table, prefix + "Y")) ?? 0) * scale));
            if (Bool(Get(table, prefix + "ClassColor")) == true)
            {
                style.Color = TextColorMode.Job;
            }
            else if (Color(Get(table, prefix + "Color")) is { } color && color != Vector4.One)
            {
                style.Color = TextColorMode.Custom;
                style.CustomColor = color;
            }
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
                UnitCastLook(player!, P.PlayerCast);
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
                settings.ShowIcon = Bool(Get(frame, "showCastIcon")) ?? true;
                if (frame != null)
                {
                    UnitCastLook(frame, settings);
                }

                PlaceCastBar(key, element, unit, size, positions, settings);
            }

            converted.Add("target and focus cast bars");
        }

        private void UnitCastLook(Dictionary<object, object?> frame, CastBarSettings settings)
        {
            TextLook(frame, "castSpellName", settings.NameStyle, 11);
            TextLook(frame, "castDuration", settings.TimerStyle, 10);
            settings.ShowTimer = Bool(Get(frame, "showCastDuration")) ?? true;
            if (Color(Get(frame, "castbarFillColor")) is { } fill)
            {
                settings.CustomColor = true;
                settings.Color = fill;
            }
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
            AuraGrid(P.PlayerBuffs, auras, "Buffs", 11, 3, 32, "PAB_Buffs", Hud.Elements.AuraGroup.PlayerBuffs);
            AuraGrid(P.PlayerDebuffs, auras, "Debuffs", 8, 2, 16, "PAB_Debuffs", Hud.Elements.AuraGroup.PlayerDebuffs);
            converted.Add("player auras");
        }

        /// <summary>
        /// One of EllesmereUI's player aura bars. Newer versions keep the grid in defaultBuffs / defaultDebuffs and the
        /// spot in buffsPos / debuffsPos; older ones use flat keys (iconsPerRowBuffs, maxBuffs, paddingBuffs).
        /// </summary>
        private void AuraGrid(AuraSettings s, Dictionary<object, object?> auras, string kind, int defaultPerRow, int defaultRows, int defaultMax, string key, Hud.Elements.AuraGroup group)
        {
            var grid = Table(Get(auras, "default" + kind));
            var size = Num(Get(grid, "iconSize")) ?? Num(Get(auras, "iconSize")) ?? 32;
            var perRow = Math.Max(1, (int)(Num(Get(grid, "iconsPerRow")) ?? Num(Get(auras, "iconsPerRow" + kind)) ?? defaultPerRow));
            var rows = Math.Max(1, (int)(Num(Get(grid, "maxRows")) ?? Num(Get(auras, "maxRows" + kind)) ?? defaultRows));
            var padding = Num(Get(grid, "padding")) ?? Num(Get(auras, "padding" + kind)) ?? 5;
            var max = grid != null ? perRow * rows : (int)(Num(Get(auras, "max" + kind)) ?? defaultMax);

            // WoW aura icons are square, and imported profiles draw status icons square too.
            s.IconHeight = MathF.Round((float)size * scale);
            s.PerRow = perRow;
            s.MaxIcons = Math.Max(1, max);
            s.Spacing = (int)MathF.Round((float)padding * scale);
            s.GrowLeft = (Str(Get(grid, "growDirection")) ?? "LEFT").ToUpperInvariant() == "LEFT";
            if ((Num(Get(grid, "durationTextSize")) ?? Num(Get(auras, "durationTextSize"))) is { } timerSize)
            {
                s.TimerFontSize = (int)MathF.Round((float)timerSize * scale);
            }

            if ((Str(Get(auras, "durationPosition")) ?? string.Empty).ToUpperInvariant() == "CENTER")
            {
                s.TimerPosition = TimerPosition.Center;
            }

            var position = Table(Get(auras, kind == "Buffs" ? "buffsPos" : "debuffsPos"));
            if (position == null)
            {
                return;
            }

            // Keep the edge the icons grow from where EllesmereUI had it (our icons are taller, so the box differs).
            var euiBox = Scaled(perRow * size + (perRow - 1) * padding, rows * size + (rows - 1) * padding);
            if (Position(position, euiBox) is not { } euiTopLeft)
            {
                return;
            }

            var box = new Hud.Elements.AuraElement(group).GetSize(P);
            var topLeft = s.GrowLeft ? euiTopLeft with { X = euiTopLeft.X + euiBox.X - box.X } : euiTopLeft;
            Place(key, new Hud.Elements.AuraElement(group).Id, box, topLeft, null);
        }

        /// <summary>The target frame's buffs and debuffs → LucentUI's target auras, placed against the target frame.</summary>
        private void TargetAuras(Dictionary<object, object?>? target)
        {
            target ??= new Dictionary<object, object?>();
            var s = P.TargetAuras;
            var showBuffs = Bool(Get(target, "showBuffs")) ?? true;
            var debuffAnchor = Str(Get(target, "debuffAnchor")) ?? "topleft";
            var showDebuffs = debuffAnchor != "none";
            s.Enabled = showBuffs || showDebuffs;
            if (!s.Enabled)
            {
                return;
            }

            s.ShowBuffs = showBuffs;
            s.ShowDebuffs = showDebuffs;
            var prefix = showBuffs ? "buff" : "debuff";
            var size = Num(Get(target, prefix + "Size")) ?? 22;
            s.IconHeight = MathF.Round((float)size * scale);
            s.PerRow = Math.Max(1, (int)(Num(Get(target, prefix + "MaxPerRow")) ?? 8));
            s.MaxIcons = Math.Max(1, (showBuffs ? (int)(Num(Get(target, "maxBuffs")) ?? 8) : 0) + (showDebuffs ? (int)(Num(Get(target, "maxDebuffs")) ?? 8) : 0));
            s.Spacing = (int)MathF.Round((float)(Num(Get(target, prefix + "SpacingX")) ?? 1) * scale);
            s.OnlyMine = Bool(Get(target, "onlyPlayerDebuffs")) ?? false;
            if (Num(Get(target, prefix + "CooldownTextSize")) is { } timerSize)
            {
                s.TimerFontSize = (int)MathF.Round((float)timerSize * scale);
            }

            s.ShowTimer = Bool(Get(target, prefix + "ShowCooldownText")) ?? true;
            var anchor = (showBuffs ? Str(Get(target, "buffAnchor")) ?? "topleft" : debuffAnchor).ToLowerInvariant();
            s.GrowLeft = anchor.Contains("right");
            s.GrowUp = !anchor.StartsWith("bottom", StringComparison.Ordinal);
            targetAuraAnchor = (anchor, Num(Get(target, prefix + "OffsetX")) ?? 0, Num(Get(target, prefix + "OffsetY")) ?? 0);
        }

        private (string Anchor, double X, double Y)? targetAuraAnchor;

        /// <summary>Puts the target auras on the side of the target frame EllesmereUI anchors them to.</summary>
        private void PlaceTargetAuras()
        {
            if (targetAuraAnchor is not { } a || !placed.TryGetValue("target", out var frame) || frame.TopLeft is not { } framePos)
            {
                return;
            }

            var box = new Hud.Elements.AuraElement(Hud.Elements.AuraGroup.Target).GetSize(P);
            var x = a.Anchor.Contains("right") ? framePos.X + frame.Size.X - box.X : framePos.X;
            var y = a.Anchor.StartsWith("bottom", StringComparison.Ordinal) ? framePos.Y + frame.Size.Y + 1 : framePos.Y - box.Y - 1;
            P.Layout.Positions["auras.target"] = new Vector2(MathF.Round(x + (float)a.X * scale), MathF.Round(y - (float)a.Y * scale));
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
                s.IconOnRight = Bool(Get(cast, "iconOnRight")) ?? false;
                s.CustomColor = true;
                s.Color = RgbFields(cast, "fill") ?? Colors(0.898f, 0.729f, 0.267f);
                s.CustomBackground = true;
                s.Background = RgbFields(cast, "bg", Num(Get(cast, "bgA")) ?? 0.7) ?? new Vector4(0, 0, 0, 0.7f);
                s.Texture = TextureName(Get(cast, "texture"));
                s.NameStyle.FontSize = (int)MathF.Round((float)(Num(Get(cast, "spellTextSize")) ?? 11) * scale);
                s.TimerStyle.FontSize = (int)MathF.Round((float)(Num(Get(cast, "timerSize")) ?? 11) * scale);
                s.NameStyle.Offset = new Vector2(MathF.Round((float)(Num(Get(cast, "spellTextX")) ?? 0) * scale), MathF.Round(-(float)(Num(Get(cast, "spellTextY")) ?? 0) * scale));
                s.TimerStyle.Offset = new Vector2(MathF.Round((float)(Num(Get(cast, "timerX")) ?? 0) * scale), MathF.Round(-(float)(Num(Get(cast, "timerY")) ?? 0) * scale));
                var topLeft = Position(Table(Get(cast, "unlockPos")), s.Size)
                    ?? FromCentre(Num(Get(cast, "anchorX")) ?? 0, Num(Get(cast, "anchorY")) ?? -54, s.Size);
                Place("ERB_CastBar", "cast.player", s.Size, topLeft, w => s.Size = s.Size with { X = MathF.Round(w) });
                converted.Add("cast bar");
            }

            // Power bar → each job's main resource bar; class resource → each job's stacks. Both become detached
            // job gauge bars with EllesmereUI's size and position; the rest of the job gauge goes underneath.
            var primary = Table(Get(rb, "primary")) ?? new Dictionary<object, object?>();
            var secondary = Table(Get(rb, "secondary")) ?? new Dictionary<object, object?>();
            if (Bool(Get(primary, "enabled")) != false)
            {
                var size = Scaled(Num(Get(primary, "width")) ?? 214, Num(Get(primary, "height")) ?? 14);
                var centre = groupCentre + new Vector2((float)(Num(Get(primary, "offsetX")) ?? 0), (float)(Num(Get(primary, "offsetY")) ?? -54));
                var topLeft = Position(Table(Get(primary, "unlockPos")), size) ?? FromCentre(centre.X, centre.Y, size);
                // Healers and casters: EllesmereUI's power bar is mana. Everyone else: the job's own resource.
                DetachGaugeBars("ERB_Power", size, topLeft, (job, p) =>
                    Style.Jobs.Get(job).Role is Style.JobRole.Healer or Style.JobRole.Caster ? p.Id == "mana" || (p.Kind == GaugeKind.Resource && !HasMana(job)) : p.Kind == GaugeKind.Resource);
                converted.Add("power bar");
            }

            if (Bool(Get(secondary, "enabled")) != false)
            {
                var size = Scaled(Num(Get(secondary, "pipWidth")) ?? 214, Num(Get(secondary, "pipHeight")) ?? 20);
                var centre = groupCentre + new Vector2((float)(Num(Get(secondary, "offsetX")) ?? 0), (float)(Num(Get(secondary, "offsetY")) ?? -38));
                var topLeft = Position(Table(Get(secondary, "unlockPos")), size) ?? FromCentre(centre.X, centre.Y, size);
                DetachGaugeBars("ERB_ClassResource", size, topLeft, (_, p) => p.Kind is GaugeKind.Stacks or GaugeKind.Pips);
                converted.Add("class resource bar");
            }

            P.JobGauge.Texture = TextureName(Get(general, "barTexture"));
            if (placed.ContainsKey("ERB_Power") || placed.ContainsKey("ERB_ClassResource"))
            {
                P.JobGauge.Enabled = true;
                P.Modules.JobGauges = true;
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

        /// <summary>Detaches, on every job, the first gauge bar matching <paramref name="pick"/> and gives it this size and position.</summary>
        private static bool HasMana(uint job) => Gauges.JobGaugeDefinitions.ByJob[job].Any(p => p.Id == "mana");

        private void DetachGaugeBars(string euiKey, Vector2 size, Vector2? topLeft, Func<uint, Gauges.GaugePart, bool> pick)
        {
            var placement = new Placement { Size = size, TopLeft = topLeft };
            var taken = euiKey == "ERB_ClassResource" && placed.TryGetValue("ERB_Power", out var power) ? power.AlsoIds : new List<string>();
            foreach (var (job, parts) in Gauges.JobGaugeDefinitions.ByJob)
            {
                foreach (var part in parts)
                {
                    var key = Hud.Elements.JobGaugeElement.PartKey(job, part);
                    if (!pick(job, part) || taken.Contains("gauge." + key))
                    {
                        continue;
                    }

                    P.JobGauge.Parts[key] = new GaugePartSettings { Enabled = true, Detached = true, Size = size, Color = part.DefaultColor };
                    placement.AlsoIds.Add("gauge." + key);
                    break;
                }
            }

            placement.SetWidth = w =>
            {
                foreach (var id in placement.AlsoIds)
                {
                    var key = id["gauge.".Length..];
                    P.JobGauge.Parts[key].Size = P.JobGauge.Parts[key].Size with { X = MathF.Round(w) };
                }
            };
            placed[euiKey] = placement;
        }

        /// <summary>The job gauge bars EllesmereUI has no equivalent for (timers, procs) go under the resource bars.</summary>
        private void PlaceRemainingGauge()
        {
            var bars = new[] { "ERB_Power", "ERB_ClassResource" }
                .Where(placed.ContainsKey)
                .Select(k => placed[k])
                .Where(p => p.TopLeft != null)
                .ToList();
            if (bars.Count == 0)
            {
                return;
            }

            // Sized like the power bar (after any width match), slightly slimmer.
            var reference = placed.TryGetValue("ERB_Power", out var power) ? power : bars[0];
            P.JobGauge.Width = reference.Size.X;
            P.JobGauge.BarHeight = Math.Max(6f, MathF.Round(reference.Size.Y * 0.8f));

            var left = bars.Min(b => b.TopLeft!.Value.X);
            var y = bars.Max(b => b.TopLeft!.Value.Y + b.Size.Y) + 4;

            // Go below anything else that would be in the way (cooldown icons are often right under the bars).
            var height = P.JobGauge.BarHeight * 3 + P.JobGauge.Spacing * 2;
            // Custom cooldown bars start empty (WoW spells do not carry over), so they do not count.
            var others = placed.Values.Where(p => p.TopLeft != null && !bars.Contains(p) && p.Id.Length > 0 && !p.Id.StartsWith("cdm.custom", StringComparison.Ordinal)).ToList();
            for (var moved = true; moved;)
            {
                moved = false;
                foreach (var other in others)
                {
                    var o = other.TopLeft!.Value;
                    if (o.X < left + P.JobGauge.Width && o.X + other.Size.X > left && o.Y < y + height && o.Y + other.Size.Y > y)
                    {
                        y = o.Y + other.Size.Y + 4;
                        moved = true;
                    }
                }
            }

            P.Layout.Positions["jobgauge"] = new Vector2(left, y);
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
            var skipped = 0;
            var customBars = new List<string>();
            var ordered = (list ?? new Dictionary<object, object?>())
                .OrderBy(kv => kv.Key is double d ? d : double.MaxValue)
                .Select(kv => kv.Value)
                .OfType<Dictionary<object, object?>>();

            foreach (var entry in ordered)
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
                    // The user's own bars become LucentUI custom bars (layout only: WoW spells cannot carry over).
                    if (key.StartsWith("custom_", StringComparison.Ordinal) && Bool(Get(entry, "isGhostBar")) != true &&
                        P.CooldownManager.CustomBars.Count < CooldownManagerSettings.MaxCustomBars)
                    {
                        var customBar = new CustomCooldownBar { Name = Str(Get(entry, "name")) ?? $"Custom Bar {P.CooldownManager.CustomBars.Count + 1}" };
                        var slot = P.CooldownManager.CustomBars.Count;
                        P.CooldownManager.CustomBars.Add(customBar);
                        ApplyCooldownBar(customBar.Bar, entry, Table(Get(positions, key)), key, Hud.Elements.CooldownBarElement.CustomId(slot));
                        customBars.Add(customBar.Name);
                    }
                    else
                    {
                        skipped++;
                    }

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
            if (customBars.Count > 0)
            {
                result.Notes.Add($"Custom cooldown bars kept with their size and position: {string.Join(", ", customBars)}. Add your FFXIV actions or potions to them under Cooldown Manager > Custom Bars.");
            }

            if (skipped > 0)
            {
                result.Notes.Add($"{skipped} WoW-only cooldown bar(s) were skipped (e.g. hidden or interrupt bars).");
            }
        }

        private void ApplyCooldownBar(CooldownBarSettings s, Dictionary<object, object?> bar, Dictionary<object, object?>? position, string key, string? elementId = null)
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
            s.ShowCharges = Bool(Get(bar, "showCharges")) ?? s.ShowCharges;
            if (Num(Get(bar, "iconZoom")) is { } zoom)
            {
                s.IconZoom = Math.Clamp((float)zoom, 0f, 0.3f);
            }

            if (Num(Get(bar, "cooldownFontSize")) is { } fontSize)
            {
                s.TimerFontSize = (int)MathF.Round((float)fontSize * scale);
            }

            // A fixed top row ("custom top row") sets the bar's width: that many icons per row.
            var topRow = Bool(Get(bar, "customTopRowEnabled")) == true ? (int)(Num(Get(bar, "topRowCount")) ?? 0) : 0;
            if (topRow > 0)
            {
                s.PerRow = topRow;
                s.MaxIcons = topRow * s.Rows;
            }
            else
            {
                s.PerRow = 0;
                s.MaxIcons = Math.Max(s.MaxIcons, s.Rows);
            }

            // EllesmereUI bars grow from a fixed edge (or the centre); without one, from the anchor point they were saved with.
            var grow = (Str(Get(bar, "growDirection")) ?? string.Empty).ToUpperInvariant();
            var point = (Str(Get(position, "point")) ?? "CENTER").ToUpperInvariant();
            s.Align = grow switch
            {
                "RIGHT" => BarAlign.Left,
                "LEFT" => BarAlign.Right,
                "CENTER" => BarAlign.Center,
                _ => point.Contains("LEFT") ? BarAlign.Left : point.Contains("RIGHT") ? BarAlign.Right : BarAlign.Center,
            };

            var size = Hud.Elements.CooldownBarElement.BoxSize(s);
            var defaultY = key switch
            {
                "cooldowns" => -275,
                "utility" => -320,
                _ => -365,
            };
            Place("CDM_" + key, elementId ?? "cdm." + key, size, Position(position, size) ?? FromCentre(0, defaultY, size), null);
            placed["CDM_" + key].GrowDirection = grow is "LEFT" or "RIGHT" or "UP" or "DOWN" ? grow : null;

            // A bar can also be attached to the player frame or a resource bar from its own settings.
            var anchorTo = Str(Get(bar, "anchorTo")) ?? "none";
            var gap = Num(Get(bar, "spacing")) ?? 2;
            var ox = Num(Get(bar, "anchorOffsetX")) ?? 0;
            var oy = Num(Get(bar, "anchorOffsetY")) ?? 0;
            switch (anchorTo)
            {
                case "playerframe":
                    barLinks["CDM_" + key] = ("player", (Str(Get(bar, "playerFrameSide")) ?? "LEFT").ToUpperInvariant(), Num(Get(bar, "playerFrameOffsetX")) ?? 0, Num(Get(bar, "playerFrameOffsetY")) ?? 0);
                    s.Align = BarAlign.Center;
                    break;
                case "erb_castbar" or "erb_powerbar" or "erb_classresource":
                {
                    var target = anchorTo switch
                    {
                        "erb_castbar" => "ERB_CastBar",
                        "erb_powerbar" => "ERB_Power",
                        _ => "ERB_ClassResource",
                    };
                    var side = (Str(Get(bar, "anchorPosition")) ?? "left").ToUpperInvariant();
                    var (dx, dy) = side switch
                    {
                        "LEFT" => (-gap + ox, oy),
                        "RIGHT" => (gap + ox, oy),
                        "TOP" => (ox, gap + oy),
                        _ => (ox, -gap + oy),
                    };
                    barLinks["CDM_" + key] = (target, side, dx, dy);
                    s.Align = BarAlign.Center;
                    break;
                }
            }
        }

        /// <summary>Cooldown bars attached through their own "anchor to" setting: key → (target, side, x, y).</summary>
        private readonly Dictionary<string, (string Target, string Side, double X, double Y)> barLinks = new();

        // ── Nameplates ───────────────────────────────────────────────────────

        private void NameplateModule(Dictionary<object, object?> np)
        {
            var s = P.Nameplates;
            P.Modules.Nameplates = true;
            s.Enabled = true;
            s.GameNameplates = GameNameplateMode.HideReplaced;

            // Bar width is EllesmereUI's 150 plus the "health bar width" setting.
            s.Size = Scaled(150 + (Num(Get(np, "healthBarWidth")) ?? 6), Num(Get(np, "healthBarHeight")) ?? 17);
            s.Texture = TextureName(Get(np, "healthBarTexture"));
            s.Enemies = PlateMode.Bar;
            var friendlyPlayers = Bool(Get(np, "showFriendlyPlayers")) ?? true;
            s.Players = !friendlyPlayers ? PlateMode.Hidden : (Bool(Get(np, "friendlyNameOnly")) ?? true) ? PlateMode.NameOnly : PlateMode.Bar;
            s.FriendlyNpcs = (Bool(Get(np, "showFriendlyNPCs")) ?? false) ? PlateMode.NameOnly : PlateMode.Hidden;
            s.NameOnlyFontSize = (int)MathF.Round((float)(Num(Get(np, "friendlyNameSize")) ?? 15) * scale);
            s.NameOnlyJobColor = Bool(Get(np, "classColorFriendly")) ?? true;
            s.TargetScale = Math.Clamp((float)(Num(Get(np, "targetScale")) ?? 100) / 100f, 1f, 1.5f);
            s.NonTargetAlpha = Math.Clamp((float)(Num(Get(np, "nonTargetAlpha")) ?? 100) / 100f, 0f, 1f);
            s.YOffset = -MathF.Round((float)(Num(Get(np, "nameplateYOffset")) ?? 0) * scale);

            // Text slots: left / right / centre sit on the bar, "top" above it.
            s.LeftText = PlateText(Str(Get(np, "textSlotLeft")) ?? "none");
            s.LeftStyle = PlateTextStyle(np, "textSlotLeft", 0);
            s.RightText = PlateText(Str(Get(np, "textSlotRight")) ?? "healthPercent");
            s.RightStyle = PlateTextStyle(np, "textSlotRight", 0);
            var center = PlateText(Str(Get(np, "textSlotCenter")) ?? "none");
            var top = PlateText(Str(Get(np, "textSlotTop")) ?? "enemyName");
            if (center.Length > 0 || top.Length == 0)
            {
                s.CenterText = center;
                s.CenterStyle = PlateTextStyle(np, "textSlotCenter", 0);
            }
            else
            {
                var size = (Num(Get(np, "textSlotTopSize")) ?? 10) * scale;
                s.CenterText = top;
                s.CenterStyle = PlateTextStyle(np, "textSlotTop", -(s.Size.Y / 2f + (float)size / 2f + 2f));
            }

            s.ShowCast = true;
            s.CastHeight = MathF.Round((float)(Num(Get(np, "castBarHeight")) ?? 17) * scale);
            s.ShowCastIcon = Bool(Get(np, "showCastIcon")) ?? true;
            s.CastNameStyle = new TextStyle
            {
                FontSize = (int)MathF.Round((float)(Num(Get(np, "castNameSize")) ?? 10) * scale),
                Offset = new Vector2(MathF.Round((float)(Num(Get(np, "castNameOffsetX")) ?? 0) * scale), MathF.Round(-(float)(Num(Get(np, "castNameOffsetY")) ?? 0) * scale)),
            };

            converted.Add("nameplates");
        }

        private TextStyle PlateTextStyle(Dictionary<object, object?> np, string prefix, float baseY) => new()
        {
            FontSize = (int)MathF.Round((float)(Num(Get(np, prefix + "Size")) ?? 10) * scale),
            Offset = new Vector2(
                MathF.Round((float)(Num(Get(np, prefix + "XOffset")) ?? 0) * scale),
                MathF.Round(baseY - (float)(Num(Get(np, prefix + "YOffset")) ?? 0) * scale)),
            Color = Bool(Get(np, prefix + "ClassColor")) == true ? TextColorMode.Job : TextColorMode.White,
        };

        /// <summary>EllesmereUI nameplate text elements → LucentUI text tags.</summary>
        private static string PlateText(string element) => element switch
        {
            "enemyName" or "name" => "{name}",
            "healthPercent" => "{hp:pct}%",
            "healthPercentNoSign" => "{hp:pct}",
            "healthNumber" => "{hp}",
            "healthPctNum" => "{hp:pct}% | {hp}",
            "healthNumPct" => "{hp} | {hp:pct}%",
            "healthPctNumDash" => "{hp:pct}% - {hp}",
            "healthNumPctDash" => "{hp} - {hp:pct}%",
            "healthNumMax" => "{hp} / {hp:max}",
            "level" => "{level}",
            _ => string.Empty,
        };

        // ── Party frames ─────────────────────────────────────────────────────

        private void PartyModule(Dictionary<object, object?> raid)
        {
            var s = P.Party;

            // Party settings are "party_"-prefixed overrides of the raid frame settings.
            object? PartyValue(string key) => Get(raid, "party_" + key) ?? Get(raid, key);

            var width = Num(Get(raid, "partyFrameWidth")) ?? Num(Get(raid, "frameWidth")) ?? 125;
            var height = Num(Get(raid, "partyFrameHeight")) ?? Num(Get(raid, "frameHeight")) ?? 60;
            s.Size = Scaled(width, height);
            s.Spacing = Math.Max(0, (int)MathF.Round((float)(Num(Get(raid, "partyCellSpacing")) ?? Num(Get(raid, "cellSpacing")) ?? -1) * scale) + 1);
            s.Sort = (Str(Get(raid, "partySortMode")) ?? Str(Get(raid, "sortMode")) ?? "ROLE") == "ROLE" ? PartySortMode.Role : PartySortMode.Game;
            s.Horizontal = Bool(Get(raid, "partyHorizontal")) ?? false;

            // Role order, e.g. { "TANK", "DAMAGER", "HEALER" }.
            var roles = Table(Get(raid, "partyRoleOrder")) ?? Table(Get(raid, "roleOrder"));
            var order = string.Concat((roles ?? new Dictionary<object, object?>())
                .OrderBy(kv => kv.Key is double d ? d : double.MaxValue)
                .Select(kv => (Str(kv.Value) ?? string.Empty) switch { "TANK" => "T", "HEALER" => "H", "DAMAGER" => "D", _ => string.Empty }));
            s.RoleOrder = order switch
            {
                "TDH" => PartyRoleOrder.TankDpsHealer,
                "HTD" => PartyRoleOrder.HealerTankDps,
                "HDT" => PartyRoleOrder.HealerDpsTank,
                "DTH" => PartyRoleOrder.DpsTankHealer,
                "DHT" => PartyRoleOrder.DpsHealerTank,
                _ => PartyRoleOrder.TankHealerDps,
            };
            s.ShowWhenSolo = Bool(Get(raid, "partyShowWhenSolo")) ?? Bool(Get(raid, "showWhenSolo")) ?? false;
            s.IncludePlayer = !(Bool(Get(raid, "partyHideSelf")) ?? false);
            s.Texture = TextureName(PartyValue("healthBarTexture"));
            if (PartyValue("absorbStyle") != null)
            {
                s.ShowShield = ShieldStyle(PartyValue("absorbStyle")) != null;
                s.OwnShieldStyle = true;
                OwnShieldColor(ImportShield(PartyValue, "absorbStyle", s.Shields), s.Shields);
            }

            // Power bar: EllesmereUI shows it per role; LucentUI shows it for everyone or no one.
            var anyPower = new[] { "Healer", "Tank", "DPS" }.Any(role => Bool(PartyValue("powerShowFor" + role)) ?? role == "Healer");
            s.ShowPower = (Bool(PartyValue("showPowerBar")) ?? true) && anyPower;
            s.PowerHeight = Math.Max(1, (int)MathF.Round((float)(Num(PartyValue("powerHeight")) ?? 4) * scale));

            var texts = FrameTexts(PartyValue, s.Size.Y, Num(Get(raid, "party_nameMaxLength")));
            (s.LeftText, s.LeftStyle, s.CenterText, s.CenterStyle, s.RightText, s.RightStyle) = texts;
            StatusIconsFrom(PartyValue, "debuff", s.Debuffs);

            // EllesmereUI's party container holds five frames; place the first one where it was.
            var container = s.Horizontal
                ? new Vector2(5 * s.Size.X + 4 * s.Spacing, s.Size.Y)
                : new Vector2(s.Size.X, 5 * s.Size.Y + 4 * s.Spacing);
            Place("RF_PartyFrames", "party", container, Position(Table(Get(raid, "partyUnlockPos")), container), null);
            converted.Add("party frames");
        }

        /// <summary>
        /// Name and health text of a raid or party frame (name and health positions, sizes and offsets) mapped onto
        /// left / centre / right text slots.
        /// </summary>
        private (string Left, TextStyle LeftStyle, string Center, TextStyle CenterStyle, string Right, TextStyle RightStyle) FrameTexts(Func<string, object?> value, float frameHeight, double? nameLength)
        {
            // Name: anchored in a corner or side of the health bar with 2px padding (EllesmereUI's raid frame layout).
            var namePosition = Str(value("namePosition")) ?? "topleft";
            var nameText = namePosition == "none" ? string.Empty : "{name}";
            if (nameText.Length > 0 && nameLength is > 0 and < 40)
            {
                nameText = $"{{name:{(int)nameLength.Value}}}";
            }

            var nameStyle = SlotStyle(namePosition, Num(value("nameSize")) ?? 10, Num(value("nameOffsetX")) ?? 0, Num(value("nameOffsetY")) ?? 0, frameHeight);

            // Health text: none / percent / number / deficit, centred by default.
            var healthText = (Str(value("healthTextMode")) ?? "none") switch
            {
                "percent" => "{hp:pct}%",
                "deficit" => "{hp:deficit}",
                "number" or "current" or "health" => "{hp}",
                "both" or "currentpercent" => "{hp} | {hp:pct}%",
                _ => string.Empty,
            };
            var healthPosition = Str(value("healthTextPosition")) ?? "center";
            var healthStyle = SlotStyle(healthPosition, Num(value("healthTextSize")) ?? 9, Num(value("healthTextOffsetX")) ?? 0, Num(value("healthTextOffsetY")) ?? 0, frameHeight);

            var slots = new PartySettings { LeftText = string.Empty, CenterText = string.Empty, RightText = string.Empty };
            AssignPartySlot(slots, namePosition, nameText, nameStyle);
            AssignPartySlot(slots, healthPosition, healthText, healthStyle);
            return (slots.LeftText, slots.LeftStyle, slots.CenterText, slots.CenterStyle, slots.RightText, slots.RightStyle);
        }

        /// <summary>EllesmereUI's debuff (or other aura) indicator settings → LucentUI status icon settings.</summary>
        private void StatusIconsFrom(Func<string, object?> value, string prefix, StatusIconSettings s)
        {
            s.Enabled = Bool(value(prefix == "debuff" ? "showDebuffs" : "show" + prefix)) ?? true;
            s.Shape = IconShape.StyleDefault; // follows the profile's square icons (see Appearance)
            s.Size = MathF.Round((float)(Num(value(prefix + "Size")) ?? 18) * scale);
            s.Max = Math.Max(1, (int)(Num(value(prefix + "Cap")) ?? Num(value("debuffCap")) ?? 3));
            s.Spacing = Math.Max(0, (int)MathF.Round((float)(Num(value(prefix + "Spacing")) ?? 1) * scale));
            s.Offset = new Vector2(MathF.Round((float)(Num(value(prefix + "OffsetX")) ?? 0) * scale), MathF.Round(-(float)(Num(value(prefix + "OffsetY")) ?? 0) * scale));
            s.Timers = Bool(value(prefix + "ShowDurText")) ?? false;
            if (Num(value(prefix + "DurTextSize")) is { } timerSize)
            {
                s.TimerFontSize = (int)MathF.Round((float)timerSize * scale);
            }

            s.Outside = false;
            s.Anchor = (Str(value(prefix + "Position")) ?? "bottomright").ToLowerInvariant() switch
            {
                "topleft" => IconAnchor.TopLeft,
                "top" => IconAnchor.Top,
                "topright" => IconAnchor.TopRight,
                "left" => IconAnchor.Left,
                "center" => IconAnchor.Center,
                "right" => IconAnchor.Right,
                "bottomleft" => IconAnchor.BottomLeft,
                "bottom" => IconAnchor.Bottom,
                _ => IconAnchor.BottomRight,
            };
            s.Grow = (Str(value(prefix + "GrowDirection")) ?? "auto").ToUpperInvariant() switch
            {
                "LEFT" => IconGrow.Left,
                "RIGHT" => IconGrow.Right,
                "UP" => IconGrow.Up,
                "DOWN" => IconGrow.Down,
                _ => IconGrow.Auto,
            };
            s.Border = AuraBorder.ByType;
        }

        // ── Raid frames → alliance frames ────────────────────────────────────

        /// <summary>
        /// EllesmereUI's raid frames become the alliance frames: frame size (the 25-player size if set), spacing,
        /// growth, text, texture, colours, debuff icons and position.
        /// </summary>
        private void AllianceModule(Dictionary<object, object?> raid)
        {
            var s = P.Alliance;
            object? RaidValue(string key) => Get(raid, key);

            if (RaidValue("absorbStyle") != null)
            {
                s.ShowShield = ShieldStyle(RaidValue("absorbStyle")) != null;
                s.OwnShieldStyle = true;
                OwnShieldColor(ImportShield(RaidValue, "absorbStyle", s.Shields), s.Shields);
            }

            var sizes = Table(Get(raid, "raidSizeOverrides"));
            var override25 = sizes != null && sizes.TryGetValue(25d, out var bySize) ? Table(bySize) : Table(Get(sizes, "25"));
            var width = Num(Get(override25, "width")) ?? Num(Get(raid, "frameWidth")) ?? 125;
            var height = Num(Get(override25, "height")) ?? Num(Get(raid, "frameHeight")) ?? 60;
            s.Size = Scaled(width, height);
            s.Spacing = Math.Max(0, (int)MathF.Round((float)(Num(Get(raid, "cellSpacing")) ?? -1) * scale) + 1);
            s.GroupSpacing = Math.Max(0, (int)MathF.Round((float)(Num(Get(raid, "groupSpacing")) ?? -1) * scale) + 1);

            // Members along unitGrowth, parties along groupGrowth.
            var unitGrowth = (Str(Get(raid, "unitGrowth")) ?? "DOWN").ToUpperInvariant();
            var groupGrowth = (Str(Get(raid, "groupGrowth")) ?? "RIGHT").ToUpperInvariant();
            s.HorizontalMembers = unitGrowth is "LEFT" or "RIGHT";
            s.SideBySide = groupGrowth.Contains("RIGHT") || groupGrowth.Contains("LEFT");
            if (s.HorizontalMembers && s.SideBySide)
            {
                s.SideBySide = false;
            }

            s.Texture = TextureName(RaidValue("healthBarTexture"));
            s.ColorMode = (Str(RaidValue("healthColorMode")) ?? "class") switch
            {
                "dark" => HealthColorMode.Dark,
                "custom" => HealthColorMode.Flat,
                _ => HealthColorMode.Job,
            };

            var texts = FrameTexts(RaidValue, s.Size.Y, Num(Get(raid, "nameMaxLength")));
            (s.LeftText, s.LeftStyle, s.CenterText, s.CenterStyle, s.RightText, s.RightStyle) = texts;
            StatusIconsFrom(RaidValue, "debuff", s.Debuffs);
            s.ShowJobIcon = false;

            // The raid container's centre stays where it was.
            var box = new Hud.Elements.AllianceFramesElement().GetSize(P);
            Place("RF_RaidFrames", "alliance", box, Position(Table(Get(raid, "unlockPos")), box), null);
            converted.Add("alliance frames (from raid frames)");
        }

        /// <summary>
        /// A text anchored at a corner/side of the frame with an offset (WoW, y up) → a LucentUI text style, whose
        /// offset is measured from the left/centre/right slot at the frame's vertical middle.
        /// </summary>
        private TextStyle SlotStyle(string position, double size, double offsetX, double offsetY, float frameHeight)
        {
            var px = (int)MathF.Round((float)size * scale);
            var x = (float)offsetX * scale;
            var y = -(float)offsetY * scale;
            if (position.StartsWith("top", StringComparison.Ordinal))
            {
                y += -frameHeight / 2f + 2f * scale + px / 2f;
            }
            else if (position.StartsWith("bottom", StringComparison.Ordinal))
            {
                y += frameHeight / 2f - 2f * scale - px / 2f;
            }

            // LucentUI pads left/right text by 5px; EllesmereUI by 2px.
            if (position.EndsWith("left", StringComparison.Ordinal))
            {
                x -= 3f;
            }
            else if (position.EndsWith("right", StringComparison.Ordinal))
            {
                x += 3f;
            }

            return new TextStyle { FontSize = px, Offset = new Vector2(MathF.Round(x), MathF.Round(y)) };
        }

        private static void AssignPartySlot(PartySettings s, string position, string text, TextStyle style)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (position.EndsWith("left", StringComparison.Ordinal))
            {
                s.LeftText = text;
                s.LeftStyle = style;
            }
            else if (position.EndsWith("right", StringComparison.Ordinal))
            {
                s.RightText = text;
                s.RightStyle = style;
            }
            else
            {
                s.CenterText = text;
                s.CenterStyle = style;
            }
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
            anchors = anchors != null ? new Dictionary<object, object?>(anchors) : new Dictionary<object, object?>();
            foreach (var (child, (target, side, x, y)) in barLinks)
            {
                anchors[child] = new Dictionary<object, object?> { ["target"] = target, ["side"] = side, ["offsetX"] = x, ["offsetY"] = y };
            }
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
                    child.TopLeft = GrowPinned(child, child.TopLeft.Value, targetPos, target.Size, info);
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

        /// <summary>
        /// A bar that grows from a fixed edge keeps that edge at a stored offset from one of the target's edges
        /// (EllesmereUI's "growth-edge pin": refX/refY + edgeOffX/edgeOffY, valid while refFor matches the growth).
        /// </summary>
        private Vector2 GrowPinned(Placement child, Vector2 topLeft, Vector2 targetPos, Vector2 targetSize, Dictionary<object, object?> info)
        {
            var grow = child.GrowDirection;
            if (grow == null || !string.Equals(Str(Get(info, "refFor")), grow, StringComparison.OrdinalIgnoreCase))
            {
                return topLeft;
            }

            if (grow is "LEFT" or "RIGHT")
            {
                var reference = (Str(Get(info, "refX")) ?? "CENTER").ToUpperInvariant() switch
                {
                    "LEFT" => targetPos.X,
                    "RIGHT" => targetPos.X + targetSize.X,
                    _ => targetPos.X + targetSize.X / 2f,
                };
                var edge = reference + (float)(Num(Get(info, "edgeOffX")) ?? 0) * scale;
                return topLeft with { X = MathF.Round(grow == "RIGHT" ? edge : edge - child.Size.X) };
            }

            var referenceY = (Str(Get(info, "refY")) ?? "CENTER").ToUpperInvariant() switch
            {
                "TOP" => targetPos.Y,
                "BOTTOM" => targetPos.Y + targetSize.Y,
                _ => targetPos.Y + targetSize.Y / 2f,
            };
            var edgeY = referenceY - (float)(Num(Get(info, "edgeOffY")) ?? 0) * scale;
            return topLeft with { Y = MathF.Round(grow == "UP" ? edgeY - child.Size.Y : edgeY) };
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
        private static Vector4? RgbFields(Dictionary<object, object?> table, string prefix, double alpha = 1)
        {
            var r = Num(Get(table, prefix + "R"));
            var g = Num(Get(table, prefix + "G"));
            var b = Num(Get(table, prefix + "B"));
            return r != null && g != null && b != null ? new Vector4((float)r, (float)g, (float)b, (float)alpha) : null;
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
