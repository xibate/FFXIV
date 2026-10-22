using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace LucentUI.Data;

/// <summary>Head markers (attack 1, bind, square, ...) placed on units, read once per frame.</summary>
public static unsafe class Markers
{
    private const int Count = 17;

    // Game icons in the game's marker order: attack 1-5, bind 1-3, ignore 1-2, square, circle, cross, triangle,
    // attack 6-8. Used when the Marker sheet has no icon for a slot.
    private static readonly uint[] FallbackIcons =
    {
        61201, 61202, 61203, 61204, 61205, 61211, 61212, 61213, 61221, 61222,
        61231, 61232, 61233, 61234, 61206, 61207, 61208,
    };

    private static readonly Dictionary<ulong, int> ByObject = new();
    private static uint[]? icons;
    private static int frame = -1;

    /// <summary>Icon of the head marker on <paramref name="obj"/>, or 0 when it has none.</summary>
    public static uint IconFor(IGameObject? obj)
    {
        if (obj == null)
        {
            return 0;
        }

        Refresh();
        return ByObject.TryGetValue(obj.GameObjectId, out var index) ? Icon(index) : 0;
    }

    /// <summary>Icon shown on settings previews (attack 1).</summary>
    public static uint SampleIcon => Icon(0);

    private static void Refresh()
    {
        var now = ImGui.GetFrameCount();
        if (now == frame)
        {
            return;
        }

        frame = now;
        ByObject.Clear();
        var controller = MarkingController.Instance();
        if (controller == null)
        {
            return;
        }

        var markers = controller->Markers;
        for (var i = 0; i < markers.Length && i < Count; i++)
        {
            ulong id = markers[i];
            if (id != 0 && id != 0xE0000000)
            {
                ByObject.TryAdd(id, i);
            }
        }
    }

    private static uint Icon(int index)
    {
        if (icons == null)
        {
            icons = new uint[Count];
            var sheet = Svc.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Marker>();
            for (var i = 0; i < Count; i++)
            {
                var icon = sheet.GetRowOrDefault((uint)(i + 1))?.Icon ?? 0;
                icons[i] = icon > 0 ? (uint)icon : FallbackIcons[i];
            }
        }

        return index >= 0 && index < Count ? icons[index] : 0;
    }
}
