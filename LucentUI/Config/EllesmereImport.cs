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

        if (Str(Get(payload, "type")) is { } type && type != "full")
        {
            result.Notes.Add($"Imported a \"{type}\" export; only the parts it contains were converted.");
        }

        new Converter(data, screenSize, result).Run();
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

    private sealed class Converter
    {
        private static readonly (string Key, string Element, string Label)[] Frames =
        {
            ("player", "unit.player", "player frame"),
            ("target", "unit.target", "target frame"),
            ("targettarget", "unit.tot", "target of target"),
            ("focus", "unit.focus", "focus frame"),
        };

        private static readonly HashSet<string> Handled = new()
        {
            "EllesmereUIUnitFrames",
            "EllesmereUIRaidFrames",
            "EllesmereUINameplates",
            "EllesmereUIResourceBars",
        };

        private readonly Dictionary<object, object?> data;
        private readonly Vector2 screen;
        private readonly Result result;
        private readonly float scale;
        private readonly List<string> converted = new();

        public Converter(Dictionary<object, object?> data, Vector2 screen, Result result)
        {
            this.data = data;
            this.screen = screen;
            this.result = result;

            // WoW UI units → pixels. EllesmereUI's pixel-perfect scale maps 1 unit to 1 pixel on the exporter's
            // screen; without a scale, assume that on a 1080p screen. Scale to this screen's height.
            var uiScale = Num(Get(data, "uiScale"));
            scale = uiScale is > 0 ? (float)uiScale.Value * screen.Y / 768f : screen.Y / 1080f;
        }

        private Profile P => result.Profile;

        public void Run()
        {
            var addons = Table(Get(data, "addons"));

            Appearance();
            if (Table(Get(addons, "EllesmereUIUnitFrames")) is { } unitFrames)
            {
                UnitFrames(unitFrames);
            }

            if (Table(Get(addons, "EllesmereUIRaidFrames")) is { } raid)
            {
                RaidFrames(raid);
            }

            if (addons?.ContainsKey("EllesmereUINameplates") == true)
            {
                P.Modules.Nameplates = true;
                converted.Add("nameplates (turned on)");
            }

            if (addons?.ContainsKey("EllesmereUIResourceBars") == true)
            {
                P.Modules.JobGauges = true;
                converted.Add("resource bars (job gauges on)");
            }

            if (converted.Count > 0)
            {
                result.Notes.Insert(0, "Converted: " + string.Join(", ", converted) + ".");
            }

            var skipped = addons?.Keys.OfType<string>().Where(k => !Handled.Contains(k)).Select(Pretty).OrderBy(n => n).ToList() ?? new List<string>();
            if (skipped.Count > 0)
            {
                result.Notes.Add("No FFXIV equivalent, skipped: " + string.Join(", ", skipped) + ".");
            }

            result.Notes.Add("WoW fonts and textures are not available in FFXIV; the game font and LucentUI's bar styles are used instead.");
        }

        private void Appearance()
        {
            if (Table(Get(data, "euiAccent")) is { } accent)
            {
                if (Bool(Get(accent, "useClass")) == true)
                {
                    result.Notes.Add("The accent colour followed the WoW class colour; LucentUI's default accent is kept.");
                }
                else if (Color(Get(accent, "custom")) is { } color)
                {
                    P.Style.Accent = color;
                    converted.Add("accent colour");
                }
            }

            if (Table(Get(data, "fonts")) is { } fonts && Str(Get(fonts, "outlineMode")) is { } outline)
            {
                P.Style.TextOutline = outline != "none";
                converted.Add("text outline");
            }
        }

        private void UnitFrames(Dictionary<object, object?> uf)
        {
            if (Bool(Get(uf, "darkTheme")) == true)
            {
                P.Style.HealthColors = HealthColorMode.Dark;
                converted.Add("dark theme");
            }

            if (Color(Get(uf, "castbarColor")) is { } castColor)
            {
                P.Style.Cast = castColor;
            }

            var enabled = Table(Get(uf, "enabledFrames"));
            var positions = Table(Get(uf, "positions"));

            foreach (var (key, elementId, label) in Frames)
            {
                if (Table(Get(uf, key)) is not { } frame)
                {
                    continue;
                }

                var s = key switch
                {
                    "player" => P.Player,
                    "target" => P.Target,
                    "targettarget" => P.TargetOfTarget,
                    _ => P.Focus,
                };

                var width = Num(Get(frame, "frameWidth"));
                var health = Num(Get(frame, "healthHeight"));
                var power = Num(Get(frame, "powerHeight")) ?? 0;
                var powerPosition = Str(Get(frame, "powerPosition"));
                var showPower = power > 0 && powerPosition != "none";

                if (width != null && health != null)
                {
                    var powerPx = showPower ? Math.Max(1, (int)MathF.Round((float)power * scale)) : 0;
                    var height = MathF.Round((float)health * scale) + (showPower ? powerPx + Border : 0) + 2 * Border;
                    s.Size = new Vector2(MathF.Round((float)width * scale) + 2 * Border, height);
                    s.ShowPower = showPower;
                    if (showPower)
                    {
                        s.PowerHeight = powerPx;
                    }
                }

                if (Bool(Get(frame, "healthClassColored")) is { } classColored && P.Style.HealthColors != HealthColorMode.Dark)
                {
                    s.ColorMode = classColored ? HealthColorMode.Job : HealthColorMode.Flat;
                }

                if (key == "player" && Color(Get(frame, "customBgColor")) is { } background)
                {
                    P.Style.Background = background with { W = P.Style.Background.W };
                }

                s.LeftText = TextTemplate(Str(Get(frame, "leftTextContent")), s.LeftText);
                s.RightText = TextTemplate(Str(Get(frame, "rightTextContent")), s.RightText);
                s.CenterText = TextTemplate(Str(Get(frame, "centerTextContent")), s.CenterText);

                if (Bool(Get(frame, "healthReverseFill")) is { } reverse)
                {
                    s.ReverseFill = reverse;
                }

                if (Bool(Get(enabled, key)) == false)
                {
                    s.Enabled = false;
                }

                if (Position(Table(Get(positions, key)), s.Size) is { } topLeft)
                {
                    P.Layout.Positions[elementId] = topLeft;
                }

                converted.Add(label);
            }

            CastBars(uf);
            Auras(Table(Get(uf, "playerAuraBars")));
        }

        private void CastBars(Dictionary<object, object?> uf)
        {
            if (Table(Get(uf, "player")) is { } player &&
                Num(Get(player, "playerCastbarWidth")) is { } pw && Num(Get(player, "playerCastbarHeight")) is { } ph)
            {
                P.PlayerCast.Size = new Vector2(MathF.Round((float)pw * scale), MathF.Round((float)ph * scale));
                if (Bool(Get(player, "showPlayerCastIcon")) is { } icon)
                {
                    P.PlayerCast.ShowIcon = icon;
                }

                // EllesmereUI locks cast bars under their frame by default.
                PlaceUnder("cast.player", "unit.player", P.Player.Size);
                converted.Add("player cast bar");
            }

            if (Table(Get(uf, "target")) is { } target &&
                Num(Get(target, "castbarWidth")) is { } tw && Num(Get(target, "castbarHeight")) is { } th)
            {
                P.TargetCast.Size = new Vector2(MathF.Round((float)tw * scale), MathF.Round((float)th * scale));
                PlaceUnder("cast.target", "unit.target", P.Target.Size);
                converted.Add("target cast bar");
            }
        }

        private void PlaceUnder(string castId, string frameId, Vector2 frameSize)
        {
            if (P.Layout.Positions.TryGetValue(frameId, out var frame))
            {
                P.Layout.Positions[castId] = frame + new Vector2(0, frameSize.Y + 4);
            }
        }

        private void Auras(Dictionary<object, object?>? auras)
        {
            if (auras == null)
            {
                return;
            }

            if (Num(Get(auras, "iconSize")) is { } size)
            {
                // WoW aura icons are square; FFXIV status icons are 3:4, so match the width.
                var height = MathF.Round((float)size * scale / 0.75f);
                P.PlayerBuffs.IconHeight = height;
                P.PlayerDebuffs.IconHeight = height;
            }

            P.PlayerBuffs.PerRow = (int)(Num(Get(auras, "iconsPerRowBuffs")) ?? P.PlayerBuffs.PerRow);
            P.PlayerBuffs.MaxIcons = (int)(Num(Get(auras, "maxBuffs")) ?? P.PlayerBuffs.MaxIcons);
            P.PlayerBuffs.Spacing = (int)(Num(Get(auras, "paddingBuffs")) ?? P.PlayerBuffs.Spacing);
            P.PlayerDebuffs.PerRow = (int)(Num(Get(auras, "iconsPerRowDebuffs")) ?? P.PlayerDebuffs.PerRow);
            P.PlayerDebuffs.MaxIcons = (int)(Num(Get(auras, "maxDebuffs")) ?? P.PlayerDebuffs.MaxIcons);
            P.PlayerDebuffs.Spacing = (int)(Num(Get(auras, "paddingDebuffs")) ?? P.PlayerDebuffs.Spacing);
            converted.Add("player auras");
        }

        private void RaidFrames(Dictionary<object, object?> raid)
        {
            var s = P.Party;
            if (Num(Get(raid, "frameWidth")) is { } w && Num(Get(raid, "frameHeight")) is { } h)
            {
                s.Size = new Vector2(MathF.Round((float)w * scale), MathF.Round((float)h * scale));
            }

            if (Num(Get(raid, "cellSpacing")) is { } spacing)
            {
                s.Spacing = Math.Max(0, (int)MathF.Round((float)spacing * scale));
            }

            if (Str(Get(raid, "sortMode")) is { } sort)
            {
                s.Sort = sort == "ROLE" ? PartySortMode.Role : PartySortMode.Game;
            }

            if (Str(Get(raid, "unitGrowth")) is { } growth)
            {
                s.Horizontal = growth is "RIGHT" or "LEFT";
            }

            if (Bool(Get(raid, "showWhenSolo")) is { } solo)
            {
                s.ShowWhenSolo = solo;
            }

            converted.Add("party frames");
        }

        /// <summary>Converts a WoW anchor (point/relPoint/x/y, y up) to a LucentUI top-left offset from the screen centre.</summary>
        private Vector2? Position(Dictionary<object, object?>? pos, Vector2 size)
        {
            if (pos == null || Num(Get(pos, "x")) is not { } x || Num(Get(pos, "y")) is not { } y)
            {
                return null;
            }

            var point = Anchor(Str(Get(pos, "point")));
            var relPoint = Anchor(Str(Get(pos, "relPoint")) ?? Str(Get(pos, "point")));

            // Screen position of the anchor on the screen, then of the frame's own anchor point.
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

        /// <summary>EllesmereUI text content keys → LucentUI text tags.</summary>
        private static string TextTemplate(string? content, string fallback) => content switch
        {
            null => fallback,
            "none" => string.Empty,
            "name" => "{name}",
            "both" or "bothdash" => content == "both" ? "{hp} | {hp:pct}%" : "{hp} - {hp:pct}%",
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
