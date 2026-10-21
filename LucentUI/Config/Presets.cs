using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;

namespace LucentUI.Config;

/// <summary>Profiles that ship with the plugin, scaled to the player's screen.</summary>
public static class Presets
{
    public const string LucentName = "Lucent";

    /// <summary>The Lucent profile was laid out on a 2160px tall (4K) screen.</summary>
    private const float LucentReferenceHeight = 2160f;

    // Settings measured in pixels; everything else (colours, ratios, times, distances) stays as it is.
    private static readonly HashSet<string> PixelFloats = new()
    {
        "IconSize", "IconHeight", "Size", "BarHeight", "Width", "CastHeight", "YOffset", "CircleRadius", "Rounding",
    };

    private static readonly HashSet<string> PixelInts = new()
    {
        "FontSize", "SmallFontSize", "TimerFontSize", "NameOnlyFontSize", "CountFontSize", "TextFontSize", "TrackerFontSize", "Spacing", "GroupSpacing", "PowerHeight", "ShadowSize",
    };

    /// <summary>The export string of the Lucent profile, embedded in the plugin.</summary>
    public static string LucentExport()
    {
        using var stream = typeof(Presets).Assembly.GetManifestResourceStream("LucentUI.Presets.Lucent.lui")
            ?? throw new InvalidOperationException("The Lucent profile is missing from the plugin.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    /// <summary>The Lucent profile for a screen <paramref name="screenHeight"/> pixels tall.</summary>
    public static Profile Lucent(float screenHeight)
    {
        var profile = ProfileManager.Decode(LucentExport());
        if (screenHeight > 0f && Math.Abs(screenHeight - LucentReferenceHeight) > 1f)
        {
            Scale(profile, screenHeight / LucentReferenceHeight);
        }

        return profile;
    }

    /// <summary>Scales every pixel measurement in a profile (sizes, offsets, positions, font sizes, spacing).</summary>
    public static void Scale(object settings, float factor)
    {
        foreach (var field in settings.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = field.GetValue(settings);
            switch (value)
            {
                case Vector2 v:
                    field.SetValue(settings, new Vector2(MathF.Round(v.X * factor), MathF.Round(v.Y * factor)));
                    break;
                case float f when PixelFloats.Contains(field.Name):
                    field.SetValue(settings, MathF.Round(f * factor));
                    break;
                case int i when PixelInts.Contains(field.Name):
                    // 0 means "use the default size" for fonts; keep it.
                    field.SetValue(settings, i == 0 ? 0 : Math.Max(1, (int)MathF.Round(i * factor)));
                    break;
                case IDictionary dictionary:
                    ScaleDictionary(dictionary, factor);
                    break;
                case IList list:
                    foreach (var item in list)
                    {
                        if (IsSettings(item))
                        {
                            Scale(item!, factor);
                        }
                    }

                    break;
                default:
                    if (IsSettings(value))
                    {
                        Scale(value!, factor);
                    }

                    break;
            }
        }
    }

    private static void ScaleDictionary(IDictionary dictionary, float factor)
    {
        var keys = new List<object>();
        foreach (var key in dictionary.Keys)
        {
            keys.Add(key);
        }

        foreach (var key in keys)
        {
            switch (dictionary[key])
            {
                case Vector2 v:
                    dictionary[key] = new Vector2(MathF.Round(v.X * factor), MathF.Round(v.Y * factor));
                    break;
                case { } item when IsSettings(item):
                    Scale(item, factor);
                    break;
            }
        }
    }

    // LucentUI's own settings classes (not strings, lists of ids, enums...).
    private static bool IsSettings(object? value) =>
        value != null && value.GetType().IsClass && value.GetType().Namespace == typeof(Profile).Namespace;
}
