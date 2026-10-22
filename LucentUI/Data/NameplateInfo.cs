using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;

namespace LucentUI.Data;

/// <summary>
/// Titles and free company tags the game puts on its own nameplates, remembered per unit (LucentUI reads them
/// before it removes them from the game's plates). Players without a game plate still get their FC tag.
/// </summary>
public static class NameplateInfo
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ulong, Entry> Entries = new();

    private readonly record struct Entry(string Title, bool TitleAbove, string FreeCompany, uint QuestMarker);

    public static void Set(ulong objectId, string title, bool titleAbove, string freeCompany, int questMarker)
    {
        lock (Gate)
        {
            if (Entries.Count > 800)
            {
                Entries.Clear();
            }

            Entries[objectId] = new Entry(Clean(title), titleAbove, Clean(freeCompany), questMarker > 0 ? (uint)questMarker : 0);
        }
    }

    /// <summary>The unit's title without brackets, or empty.</summary>
    public static string Title(IGameObject? obj) => obj != null && TryGet(obj.GameObjectId, out var e) ? e.Title : string.Empty;

    /// <summary>Whether the game shows the unit's title above its name (prefix titles) rather than below.</summary>
    public static bool TitleAbove(IGameObject? obj) => obj != null && TryGet(obj.GameObjectId, out var e) && e.TitleAbove;

    /// <summary>The quest marker icon the game shows above the unit (e.g. "!" for a new quest), or 0.</summary>
    public static uint QuestMarker(IGameObject? obj) => obj != null && TryGet(obj.GameObjectId, out var e) ? e.QuestMarker : 0;

    /// <summary>The unit's free company tag without brackets, or empty.</summary>
    public static string FreeCompany(IGameObject? obj)
    {
        if (obj == null)
        {
            return string.Empty;
        }

        if (TryGet(obj.GameObjectId, out var e) && e.FreeCompany.Length > 0)
        {
            return e.FreeCompany;
        }

        return obj is IPlayerCharacter player ? Clean(player.CompanyTag.TextValue) : string.Empty;
    }

    private static bool TryGet(ulong id, out Entry entry)
    {
        lock (Gate)
        {
            return Entries.TryGetValue(id, out entry);
        }
    }

    /// <summary>Removes the brackets and spaces the game wraps titles and tags in.</summary>
    private static string Clean(string? text) => (text ?? string.Empty).Trim().Trim('«', '»', '《', '》', '<', '>', '[', ']', ' ');
}
