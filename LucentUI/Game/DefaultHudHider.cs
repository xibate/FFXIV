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
