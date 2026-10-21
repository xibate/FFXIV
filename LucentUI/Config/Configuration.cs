using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace LucentUI.Config;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Shows the welcome card in the options window until dismissed.</summary>
    public bool FirstRun = true;

    /// <summary>Show elements with sample data while the settings window is open.</summary>
    public bool PreviewWhileEditing = true;

    /// <summary>Show a live preview of the element at the top of its settings page.</summary>
    public bool ShowPagePreview = true;

    public GeneralSettings General = new();

    public string ActiveProfile = ProfileManager.DefaultName;
    public Dictionary<string, Profile> Profiles = new();

    /// <summary>When on, changing job switches to the profile assigned to that job (if any).</summary>
    public bool AutoSwitchByJob = true;

    /// <summary>ClassJob row id → profile name.</summary>
    public Dictionary<uint, string> JobProfiles = new();

    /// <summary>Extra folders scanned for fonts and bar textures (e.g. WoW's Interface\AddOns).</summary>
    public List<string> MediaFolders = new();
}
