using System;
using System.Collections.Generic;
using Dalamud.Game.Gui.NamePlate;
using LucentUI.Config;
using LucentUI.Hud.Elements;

namespace LucentUI.Game;

/// <summary>
/// Removes the game's own nameplate text from units that LucentUI draws a plate for, so names are not shown
/// twice. Quest markers, icons and the names of everything else stay.
/// </summary>
public sealed class GameNameplateHider : IDisposable
{
    private bool active;

    public void Update(Profile profile)
    {
        var s = profile.Nameplates;
        var wanted = profile.Modules.Nameplates && s.Enabled && s.GameNameplates == GameNameplateMode.HideReplaced;
        if (wanted == active)
        {
            return;
        }

        active = wanted;
        if (wanted)
        {
            Svc.NamePlateGui.OnDataUpdate += OnDataUpdate;
        }
        else
        {
            Svc.NamePlateGui.OnDataUpdate -= OnDataUpdate;
        }

        // Let the game rebuild its plates with (or without) the names.
        Svc.NamePlateGui.RequestRedraw();
    }

    private static void OnDataUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        var player = Svc.ObjectTable.LocalPlayer;
        if (player == null)
        {
            return;
        }

        var s = Plugin.Profiles.Current.Nameplates;
        foreach (var handler in handlers)
        {
            var obj = handler.GameObject;
            if (obj == null || !NameplatesElement.Replaces(s, obj, player))
            {
                continue;
            }

            handler.RemoveName();
            handler.RemoveTitle();
            handler.RemoveFreeCompanyTag();
            handler.RemoveStatusPrefix();
            handler.RemoveTargetSuffix();
            handler.RemoveLevelPrefix();
        }
    }

    public void Dispose()
    {
        if (active)
        {
            Svc.NamePlateGui.OnDataUpdate -= OnDataUpdate;
            active = false;
            Svc.NamePlateGui.RequestRedraw();
        }
    }
}
