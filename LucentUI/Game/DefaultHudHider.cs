using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LucentUI.Config;

namespace LucentUI.Game;

/// <summary>
/// Hides parts of the game's HUD by moving them off-screen right before they draw, and puts them back when no
/// longer wanted. Only addons the user opted into are touched; nothing is registered otherwise.
/// </summary>
public sealed unsafe class DefaultHudHider : IDisposable
{
    private const float Offscreen = -9999f;

    private static readonly string[] PlayerAddons = { "_ParameterWidget" };
    private static readonly string[] TargetAddons = { "_TargetInfo", "_TargetInfoMainTarget", "_TargetInfoBuffDebuff", "_TargetInfoCastBar" };
    private static readonly string[] FocusAddons = { "_FocusTargetInfo" };
    private static readonly string[] CastBarAddons = { "_CastBar" };
    private static readonly string[] PartyAddons = { "_PartyList" };
    private static readonly string[] StatusAddons = { "_Status", "_StatusCustom0", "_StatusCustom1", "_StatusCustom2", "_StatusCustom3" };
    private static readonly string[] EnemyListAddons = { "_EnemyList" };
    private static readonly string[] LimitBreakAddons = { "_LimitBreak" };
    private static readonly string[] ExpAddons = { "_Exp" };
    private static readonly string[] PullTimerAddons = { "ScreenInfo_CountDown" };
    private static readonly string[] AllianceAddons = { "_AllianceList1", "_AllianceList2" };
    private static readonly string[] NameplateAddons = { "NamePlate" };

    /// <summary>Hotbar number (1-10, 11 = cross hotbar) → its addons.</summary>
    public static readonly string[][] HotbarAddons =
    {
        new[] { "_ActionBar" },
        new[] { "_ActionBar01" },
        new[] { "_ActionBar02" },
        new[] { "_ActionBar03" },
        new[] { "_ActionBar04" },
        new[] { "_ActionBar05" },
        new[] { "_ActionBar06" },
        new[] { "_ActionBar07" },
        new[] { "_ActionBar08" },
        new[] { "_ActionBar09" },
        new[] { "_ActionCross", "_ActionDoubleCrossL", "_ActionDoubleCrossR" },
    };

    // The game's job gauges are named JobHud<job><n>, with a few exceptions.
    private static readonly string[] JobGaugeAddons = BuildJobGaugeAddons();

    private readonly HashSet<string> hidden = new();
    private readonly HashSet<string> wanted = new();
    private readonly Dictionary<string, Vector2> originalPositions = new();

    /// <summary>Call every framework tick; cheap when nothing changes.</summary>
    public void Update(Profile profile, GeneralSettings general)
    {
        wanted.Clear();
        if (general.ShowHud)
        {
            var hud = profile.DefaultHud;
            var modules = profile.Modules;

            // Only hide a game element while the replacement for it is actually turned on.
            AddIf(hud.HidePlayer && modules.UnitFrames && profile.Player.Enabled, PlayerAddons);
            AddIf(hud.HideTarget && modules.UnitFrames && profile.Target.Enabled, TargetAddons);
            AddIf(hud.HideFocus && modules.UnitFrames && profile.Focus.Enabled, FocusAddons);
            AddIf(hud.HideCastBar && modules.CastBars && profile.PlayerCast.Enabled, CastBarAddons);
            AddIf(hud.HideParty && modules.PartyFrames && profile.Party.Enabled, PartyAddons);
            AddIf(hud.HideStatus && modules.Auras && (profile.PlayerBuffs.Enabled || profile.PlayerDebuffs.Enabled), StatusAddons);
            AddIf(hud.HideJobGauge && modules.JobGauges && profile.JobGauge.Enabled, JobGaugeAddons);
            AddIf(hud.HideEnemyList && modules.EnemyList && profile.EnemyList.Enabled, EnemyListAddons);
            AddIf(hud.HideLimitBreak && modules.Extras && profile.LimitBreak.Enabled, LimitBreakAddons);
            AddIf(hud.HideExp && modules.Extras && profile.Experience.Enabled, ExpAddons);
            AddIf(hud.HidePullTimer && modules.Extras && profile.PullTimer.Enabled, PullTimerAddons);
            AddIf(hud.HideAlliance && modules.Alliance && profile.Alliance.Enabled, AllianceAddons);
            AddIf(profile.Nameplates.GameNameplates == GameNameplateMode.HideAll && modules.Nameplates && profile.Nameplates.Enabled, NameplateAddons);

            // Hotbars follow their visibility rule (e.g. only in combat).
            if (modules.Hotbars)
            {
                foreach (var (bar, mode) in profile.Hotbars.Bars)
                {
                    if (bar >= 1 && bar <= HotbarAddons.Length && !Hud.Visibility.IsVisible(mode))
                    {
                        wanted.UnionWith(HotbarAddons[bar - 1]);
                    }
                }
            }
        }

        if (wanted.SetEquals(hidden))
        {
            return;
        }

        foreach (var name in wanted)
        {
            if (hidden.Add(name))
            {
                Svc.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, name, OnPreDraw);
            }
        }

        hidden.RemoveWhere(name =>
        {
            if (wanted.Contains(name))
            {
                return false;
            }

            Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, name, OnPreDraw);
            Restore(name);
            return true;
        });
    }

    private static string[] BuildJobGaugeAddons()
    {
        var special = new Dictionary<string, string>
        {
            ["JobHudPCT0"] = "JobHudRPM0",
            ["JobHudPCT1"] = "JobHudRPM1",
            ["JobHudNIN1"] = "JobHudNIN1v70",
            ["JobHudVPR0"] = "JobHudRDB0",
            ["JobHudVPR1"] = "JobHudRDB1",
        };

        var names = new List<string>();
        foreach (var job in Style.Jobs.CombatJobs)
        {
            var abbreviation = Style.Jobs.Get(job).Abbreviation;
            for (var i = 0; i < 3; i++)
            {
                var name = $"JobHud{abbreviation}{i}";
                names.Add(special.TryGetValue(name, out var renamed) ? renamed : name);
            }
        }

        return names.ToArray();
    }

    private void AddIf(bool condition, string[] addons)
    {
        if (condition)
        {
            wanted.UnionWith(addons);
        }
    }

    private void OnPreDraw(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || addon->RootNode == null)
        {
            return;
        }

        var root = addon->RootNode;
        var x = root->GetXFloat();
        if (x > Offscreen + 1f)
        {
            // Remember where the game (or the HUD layout) last put it, so it can go back there.
            originalPositions[args.AddonName] = new Vector2(x, root->GetYFloat());
            root->SetPositionFloat(Offscreen, Offscreen);
        }
    }

    private void Restore(string name)
    {
        if (!originalPositions.Remove(name, out var pos))
        {
            return;
        }

        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(name, 1).Address;
        if (addon != null && addon->RootNode != null)
        {
            addon->RootNode->SetPositionFloat(pos.X, pos.Y);
        }
    }

    public void Dispose()
    {
        foreach (var name in hidden)
        {
            Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, name, OnPreDraw);
        }

        var names = new List<string>(hidden);
        hidden.Clear();

        void RestoreAll()
        {
            foreach (var name in names)
            {
                Restore(name);
            }
        }

        if (Svc.Framework.IsInFrameworkUpdateThread)
        {
            RestoreAll();
        }
        else
        {
            Svc.Framework.RunOnFrameworkThread(RestoreAll);
        }
    }
}
