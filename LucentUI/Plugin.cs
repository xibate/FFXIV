using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LucentUI.Config;
using LucentUI.Game;
using LucentUI.Hud;
using LucentUI.Style;
using LucentUI.Windows;

namespace LucentUI;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/lucent";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private static bool dirty;
    private static DateTime dirtySince;

    private readonly WindowSystem windows = new("LucentUI");
    private readonly HudRenderer hud;
    private readonly DefaultHudHider hider;
    private readonly GameNameplateHider nameplateHider = new();
    private readonly Data.PartyCooldownTracker cooldowns;
    private readonly MouseoverCaster mouseover;
    private readonly OptionsWindow options;
    private uint lastJobId;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Svc>();

        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Profiles = new ProfileManager(Config);
        Media = new MediaLibrary(System.IO.Path.Combine(pluginInterface.GetPluginConfigDirectory(), "Media"));
        Media.Rescan(Config.MediaFolders);

        cooldowns = new Data.PartyCooldownTracker();
        hud = new HudRenderer(cooldowns);
        mouseover = new MouseoverCaster(hud.Input);
        hider = new DefaultHudHider();
        options = new OptionsWindow(hud);
        windows.AddWindow(options);
        hud.Unlock.OpenSettingsRequested += () => options.Open();
        hud.Unlock.OpenElementSettingsRequested += options.OpenFor;

        Svc.CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open LucentUI settings. Also: unlock, toggle, profile <name>.",
        });

        pluginInterface.UiBuilder.Draw += OnDraw;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleOptions;
        pluginInterface.UiBuilder.OpenMainUi += ToggleOptions;
        Svc.Framework.Update += OnFrameworkUpdate;

        if (Config.FirstRun)
        {
            options.Open(general: true);
        }
    }

    internal static Configuration Config { get; private set; } = null!;

    internal static ProfileManager Profiles { get; private set; } = null!;

    /// <summary>Fonts and bar textures from media folders.</summary>
    internal static MediaLibrary Media { get; private set; } = null!;

    /// <summary>Schedules a config save shortly after the last change.</summary>
    public static void MarkDirty()
    {
        if (!dirty)
        {
            dirtySince = DateTime.UtcNow;
        }

        dirty = true;
    }

    public static void SaveNow()
    {
        dirty = false;
        Svc.PluginInterface.SavePluginConfig(Config);
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        Svc.PluginInterface.UiBuilder.Draw -= OnDraw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= ToggleOptions;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= ToggleOptions;
        Svc.CommandManager.RemoveHandler(Command);

        windows.RemoveAllWindows();
        hider.Dispose();
        nameplateHider.Dispose();
        Media.Dispose();
        hud.Dispose();
        cooldowns.Dispose();
        mouseover.Dispose();

        if (dirty)
        {
            SaveNow();
        }
    }

    private void OnDraw()
    {
        // Before anything draws this frame: textures replaced by a media rescan can be freed now.
        Media.BeginFrame();
        hud.Draw();
        windows.Draw();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var player = Svc.ObjectTable.LocalPlayer;
        if (player != null)
        {
            var jobId = player.ClassJob.RowId;
            if (jobId != lastJobId)
            {
                lastJobId = jobId;
                Profiles.OnJobChanged(jobId);
            }
        }

        var profile = Profiles.Current;
        hider.Update(profile, Config.General);
        nameplateHider.Update(profile);
        cooldowns.SetEnabled(profile.Modules.PartyCooldowns && profile.PartyCooldowns.Enabled);
        mouseover.SetEnabled(Config.General.MouseoverCasting);

        if (dirty && DateTime.UtcNow - dirtySince > SaveDelay)
        {
            SaveNow();
        }
    }

    private void ToggleOptions() => options.Toggle();

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

        switch (verb)
        {
            case "":
            case "config":
            case "settings":
                ToggleOptions();
                break;

            case "unlock":
            case "lock":
            case "move":
                hud.Unlock.Toggle();
                Svc.Chat.Print(hud.Unlock.Active ? "[LucentUI] Unlock Mode on. Drag elements to move them." : "[LucentUI] Elements locked.");
                break;

            case "toggle":
                Config.General.ShowHud = !Config.General.ShowHud;
                MarkDirty();
                Svc.Chat.Print(Config.General.ShowHud ? "[LucentUI] HUD shown." : "[LucentUI] HUD hidden.");
                break;

            case "profile" when parts.Length > 1:
                var name = parts[1].Trim();
                if (Profiles.CurrentName == name || Profiles.SetActive(name))
                {
                    Svc.Chat.Print($"[LucentUI] Profile: {name}");
                }
                else
                {
                    Svc.Chat.PrintError($"[LucentUI] No profile called \"{name}\".");
                }

                break;

            default:
                Svc.Chat.Print("[LucentUI] /lucent [unlock | toggle | profile <name>]");
                break;
        }
    }
}
