using System;
using System.Collections.Generic;
using Dalamud.Game.Gui.NamePlate;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Hud.Elements;

namespace LucentUI.Game;

/// <summary>
/// Reads titles and free company tags from the game's nameplates (for LucentUI's plates and the {title}/{fc}
/// tags), and removes the game's own text and quest marker from units that LucentUI draws a plate for, so they
/// are not shown twice. Quest markers, other icons and the names of everything else stay.
/// </summary>
public sealed class GameNameplateHider : IDisposable
{
    private bool active;

    public void Update(Profile profile)
    {
        var s = profile.Nameplates;
        var wanted = profile.Modules.Nameplates && s.Enabled;
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
        var hideText = s.GameNameplates == GameNameplateMode.HideReplaced;
        foreach (var handler in handlers)
        {
            var obj = handler.GameObject;
            if (obj == null)
            {
                continue;
            }

            NameplateInfo.Set(handler.GameObjectId, handler.DisplayTitle ? handler.Title.TextValue : string.Empty, handler.IsPrefixTitle, handler.FreeCompanyTag.TextValue, handler.MarkerIconId);
            if (!NameplatesElement.Replaces(s, obj, player))
            {
                continue;
            }

            // The game's quest marker ("!" and the like) moves onto LucentUI's plate.
            if (s.QuestMarker.Enabled && handler.MarkerIconId > 0)
            {
                handler.MarkerIconId = 0;
            }

            if (!hideText)
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
