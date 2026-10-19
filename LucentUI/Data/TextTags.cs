using System;
using System.Globalization;
using System.Text.RegularExpressions;
using LucentUI.Style;

namespace LucentUI.Data;

/// <summary>
/// Expands text templates such as <c>"{name} {hp:pct}%"</c>.
/// Supported tags: {name} {name:N} {name:first} {level} {job} {hp} {hp:max} {hp:pct} {hp:deficit}
/// {mp} {mp:max} {mp:pct}.
/// </summary>
public static partial class TextTags
{
    public const string Help =
        "Tags: {name}  {name:12}  {name:first}  {level}  {job}  {hp}  {hp:max}  {hp:pct}  {hp:deficit}  {mp}  {mp:max}  {mp:pct}";

    // {tag} or [tag], with an optional :argument.
    [GeneratedRegex(@"[\{\[]([a-zA-Z]+)(?::([a-zA-Z0-9]+))?[\}\]]")]
    private static partial Regex TagRegex();

    public static string Format(string template, UnitData unit, bool abbreviate)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        if (!template.Contains('{') && !template.Contains('['))
        {
            return template;
        }

        return TagRegex().Replace(template, m =>
        {
            var resolved = Resolve(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.ToLowerInvariant(), unit, abbreviate);

            // Unknown [words] stay as typed (they may just be text); unknown {tags} disappear.
            return resolved ?? (m.Value[0] == '[' ? m.Value : string.Empty);
        });
    }

    private static string? Resolve(string tag, string arg, UnitData u, bool abbreviate)
    {
        switch (tag)
        {
            case "name":
                if (arg == "first")
                {
                    var space = u.Name.IndexOf(' ');
                    return space > 0 ? u.Name[..space] : u.Name;
                }

                if (int.TryParse(arg, out var max) && max > 0 && u.Name.Length > max)
                {
                    return u.Name[..max].TrimEnd() + ".";
                }

                return u.Name;

            case "level":
                return u.Level > 0 ? u.Level.ToString(CultureInfo.InvariantCulture) : string.Empty;

            case "job":
                return u.JobId > 0 ? Jobs.Get(u.JobId).Abbreviation : string.Empty;

            case "hp":
                return u.HasHealth ? Value(u.Hp, u.MaxHp, arg, abbreviate) : string.Empty;

            case "mp":
            case "power":
                return u.MaxPower > 0 ? Value(u.Power, u.MaxPower, arg, abbreviate) : string.Empty;

            default:
                return null;
        }
    }

    private static string Value(uint current, uint max, string arg, bool abbreviate) => arg switch
    {
        "max" => Number(max, abbreviate),
        "pct" => max == 0 ? "0" : ((int)(100.0 * current / max)).ToString(CultureInfo.InvariantCulture),
        "deficit" => current >= max ? string.Empty : "-" + Number(max - current, abbreviate),
        _ => Number(current, abbreviate),
    };

    public static string Number(uint value, bool abbreviate)
    {
        if (!abbreviate)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        return value switch
        {
            >= 1_000_000 => (value / 1_000_000.0).ToString("0.00", CultureInfo.InvariantCulture) + "M",
            >= 10_000 => (value / 1_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k",
            _ => value.ToString(CultureInfo.InvariantCulture),
        };
    }

    public static string Duration(float seconds)
    {
        if (seconds >= 3600f)
        {
            return ((int)Math.Ceiling(seconds / 3600f)).ToString(CultureInfo.InvariantCulture) + "h";
        }

        if (seconds >= 60f)
        {
            return ((int)Math.Ceiling(seconds / 60f)).ToString(CultureInfo.InvariantCulture) + "m";
        }

        return ((int)Math.Ceiling(seconds)).ToString(CultureInfo.InvariantCulture);
    }
}
