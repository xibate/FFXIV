using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace LucentUI.Config;

/// <summary>Creates, switches, imports and exports profiles.</summary>
public sealed class ProfileManager
{
    public const string DefaultName = "Default";
    private const string ExportPrefix = "LUI1:";

    // Plain settings: imported strings must never carry type names.
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        Formatting = Formatting.None,
    };

    private readonly Configuration config;

    public ProfileManager(Configuration config)
    {
        this.config = config;

        if (config.Profiles.Count == 0)
        {
            config.Profiles[DefaultName] = new Profile();
        }

        if (!config.Profiles.ContainsKey(config.ActiveProfile))
        {
            config.ActiveProfile = config.Profiles.Keys.First();
        }
    }

    public Profile Current => config.Profiles[config.ActiveProfile];
    public string CurrentName => config.ActiveProfile;

    public event Action? ProfileChanged;

    public bool SetActive(string name)
    {
        if (!config.Profiles.ContainsKey(name) || name == config.ActiveProfile)
        {
            return false;
        }

        config.ActiveProfile = name;
        Plugin.MarkDirty();
        ProfileChanged?.Invoke();
        return true;
    }

    public bool Create(string name, Profile? copyFrom = null)
    {
        name = name.Trim();
        if (name.Length == 0 || config.Profiles.ContainsKey(name))
        {
            return false;
        }

        config.Profiles[name] = copyFrom != null ? Clone(copyFrom) : new Profile();
        Plugin.MarkDirty();
        return true;
    }

    public bool Rename(string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || config.Profiles.ContainsKey(newName) || !config.Profiles.TryGetValue(oldName, out var profile))
        {
            return false;
        }

        config.Profiles.Remove(oldName);
        config.Profiles[newName] = profile;

        if (config.ActiveProfile == oldName)
        {
            config.ActiveProfile = newName;
        }

        foreach (var job in config.JobProfiles.Where(kv => kv.Value == oldName).Select(kv => kv.Key).ToList())
        {
            config.JobProfiles[job] = newName;
        }

        Plugin.MarkDirty();
        return true;
    }

    public bool Delete(string name)
    {
        if (config.Profiles.Count <= 1 || !config.Profiles.Remove(name))
        {
            return false;
        }

        foreach (var job in config.JobProfiles.Where(kv => kv.Value == name).Select(kv => kv.Key).ToList())
        {
            config.JobProfiles.Remove(job);
        }

        if (config.ActiveProfile == name)
        {
            config.ActiveProfile = config.Profiles.Keys.First();
            ProfileChanged?.Invoke();
        }

        Plugin.MarkDirty();
        return true;
    }

    public void ResetCurrent()
    {
        config.Profiles[config.ActiveProfile] = new Profile();
        Plugin.MarkDirty();
        ProfileChanged?.Invoke();
    }

    /// <summary>Applies the profile assigned to a job, if auto-switching is on.</summary>
    public void OnJobChanged(uint jobId)
    {
        if (config.AutoSwitchByJob && config.JobProfiles.TryGetValue(jobId, out var name))
        {
            SetActive(name);
        }
    }

    public string Export(string name)
    {
        var json = JsonConvert.SerializeObject(config.Profiles[name], JsonSettings);
        var raw = Encoding.UTF8.GetBytes(json);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(raw, 0, raw.Length);
        }

        return ExportPrefix + Convert.ToBase64String(output.ToArray());
    }

    public bool TryImport(string text, string name, out string error)
    {
        error = string.Empty;
        name = name.Trim();

        if (name.Length == 0)
        {
            error = "Enter a name for the imported profile.";
            return false;
        }

        if (config.Profiles.ContainsKey(name))
        {
            error = $"A profile called \"{name}\" already exists.";
            return false;
        }

        text = text.Trim();
        if (!text.StartsWith(ExportPrefix, StringComparison.Ordinal))
        {
            error = "The clipboard does not contain a LucentUI profile.";
            return false;
        }

        try
        {
            config.Profiles[name] = Decode(text);
            Plugin.MarkDirty();
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "Profile import failed");
            error = "The profile string is damaged or from an incompatible version.";
            return false;
        }
    }

    /// <summary>Converts an EllesmereUI profile string into a new profile.</summary>
    public bool TryImportEllesmere(string text, string name, System.Numerics.Vector2 screenSize, out System.Collections.Generic.List<string> notes, out string error)
    {
        notes = new System.Collections.Generic.List<string>();
        name = name.Trim();
        if (name.Length == 0)
        {
            error = "Enter a name for the imported profile.";
            return false;
        }

        if (config.Profiles.ContainsKey(name))
        {
            error = $"A profile called \"{name}\" already exists.";
            return false;
        }

        if (!EllesmereImport.TryConvert(text, screenSize, out var result, out error))
        {
            return false;
        }

        config.Profiles[name] = result.Profile;
        notes = result.Notes;
        Plugin.MarkDirty();
        return true;
    }

    /// <summary>Reads a LucentUI profile string (LUI1:...). Throws when it is damaged.</summary>
    public static Profile Decode(string text)
    {
        text = text.Trim();
        if (!text.StartsWith(ExportPrefix, StringComparison.Ordinal))
        {
            throw new FormatException("Not a LucentUI profile.");
        }

        var data = Convert.FromBase64String(text[ExportPrefix.Length..]);
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var reader = new StreamReader(input, Encoding.UTF8);
        return JsonConvert.DeserializeObject<Profile>(reader.ReadToEnd(), JsonSettings) ?? throw new FormatException("The profile is empty.");
    }

    /// <summary>
    /// Adds the built-in Lucent profile (scaled to the screen) under the first free name, optionally making it
    /// the active one. Returns the name it was added under.
    /// </summary>
    public string AddLucent(float screenHeight, bool activate)
    {
        var name = Presets.LucentName;
        for (var i = 2; config.Profiles.ContainsKey(name); i++)
        {
            name = $"{Presets.LucentName} {i}";
        }

        config.Profiles[name] = Presets.Lucent(screenHeight);
        Plugin.MarkDirty();
        if (activate)
        {
            SetActive(name);
        }

        return name;
    }

    public static Profile Clone(Profile profile)
    {
        var json = JsonConvert.SerializeObject(profile, JsonSettings);
        return JsonConvert.DeserializeObject<Profile>(json, JsonSettings) ?? new Profile();
    }
}
