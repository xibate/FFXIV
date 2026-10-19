using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace LucentUI.Data;

public struct PartyMemberData
{
    public UnitData Unit;
    public ulong ContentId;
    public uint EntityId;
    public bool IsPlayer;
}

/// <summary>
/// The party in the same order as the game's party list. Read from the HUD agent, so it also covers
/// cross-world parties and trust/duty support NPCs; falls back to Dalamud's party list.
/// Refreshed at most once per frame.
/// </summary>
public static unsafe class Party
{
    public const float MaxRange = 30f;

    private static readonly List<PartyMemberData> Members = new(8);
    private static int lastFrame = -1;

    public static IReadOnlyList<PartyMemberData> Current
    {
        get
        {
            var frame = ImGui.GetFrameCount();
            if (frame != lastFrame)
            {
                lastFrame = frame;
                Refresh();
            }

            return Members;
        }
    }

    private static void Refresh()
    {
        Members.Clear();
        var player = Svc.ObjectTable.LocalPlayer;
        if (player == null)
        {
            return;
        }

        var hud = AgentHUD.Instance();
        if (hud != null && hud->PartyMemberCount > 0)
        {
            var span = hud->PartyMembers;
            var count = System.Math.Min((int)hud->PartyMemberCount, span.Length);
            for (var i = 0; i < count; i++)
            {
                var member = span[i];
                var obj = member.Object != null ? Svc.ObjectTable.CreateObjectReference((nint)member.Object) : null;
                Add(player, obj, member.ContentId, member.EntityId, member.Name.ToString());
            }

            return;
        }

        foreach (var member in Svc.PartyList)
        {
            if (member != null)
            {
                Add(player, member.GameObject, member.ContentId, member.EntityId, member.Name.TextValue);
            }
        }

        if (Members.Count == 0)
        {
            // Solo: just yourself.
            Add(player, player, 0, player.EntityId, player.Name.TextValue);
        }
    }

    private static void Add(IGameObject player, IGameObject? obj, ulong contentId, uint entityId, string name)
    {
        var isPlayer = entityId == player.EntityId;
        if (obj is ICharacter && Units.TryRead(obj, out var unit))
        {
            unit.InRange = isPlayer || Vector3.Distance(player.Position, obj.Position) <= MaxRange;
        }
        else
        {
            unit = FromPartyList(contentId, entityId) ?? new UnitData { Name = name, Reaction = UnitReaction.Player };
            unit.Object = obj;
            unit.InRange = false;
        }

        if (string.IsNullOrEmpty(unit.Name))
        {
            unit.Name = name;
        }

        Members.Add(new PartyMemberData { Unit = unit, ContentId = contentId, EntityId = entityId, IsPlayer = isPlayer });
    }

    /// <summary>Stats for a member who is not loaded nearby (other zone or far away).</summary>
    private static UnitData? FromPartyList(ulong contentId, uint entityId)
    {
        foreach (var member in Svc.PartyList)
        {
            if (member == null || (member.ContentId != contentId && member.EntityId != entityId))
            {
                continue;
            }

            return new UnitData
            {
                Name = member.Name.TextValue,
                Level = member.Level,
                Hp = member.CurrentHP,
                MaxHp = member.MaxHP,
                Power = member.CurrentMP,
                MaxPower = member.MaxMP,
                JobId = member.ClassJob.RowId,
                Reaction = UnitReaction.Player,
            };
        }

        return null;
    }
}
