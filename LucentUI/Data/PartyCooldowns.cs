using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using LucentUI.Style;
using GameObjectId = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObjectId;

namespace LucentUI.Data;

public enum CooldownCategory
{
    RaidBuff,
    Mitigation,
    Healing,
    TankInvuln,
}

/// <summary>
/// A tracked party cooldown. <see cref="JobId"/> 0 means a role action available to every job of <see cref="Role"/>.
/// Levels are the range the action exists in (some are replaced by an upgrade).
/// </summary>
public sealed record CooldownInfo(uint ActionId, uint JobId, JobRole Role, int MinLevel, float Cooldown, float Duration, CooldownCategory Category, int MaxLevel = 100)
{
    public bool AppliesTo(uint jobId, int level)
    {
        if (level < MinLevel || level > MaxLevel)
        {
            return false;
        }

        if (JobId != 0)
        {
            return JobId == jobId;
        }

        var role = Jobs.Get(jobId).Role;
        return Role == JobRole.Melee ? role is JobRole.Melee : role == Role;
    }
}

/// <summary>Records when party members use the cooldowns in <see cref="Table"/>, from the game's action-effect handler.</summary>
public sealed unsafe class PartyCooldownTracker : IDisposable
{
    public static readonly CooldownInfo[] Table =
    {
        // Role actions
        new(7535, 0, JobRole.Tank, 22, 60, 15, CooldownCategory.Mitigation),     // Reprisal
        new(7549, 0, JobRole.Melee, 22, 90, 15, CooldownCategory.Mitigation),    // Feint
        new(7560, 0, JobRole.Caster, 8, 90, 15, CooldownCategory.Mitigation),    // Addle

        // Tanks
        new(3540, 19, JobRole.Tank, 56, 90, 30, CooldownCategory.Mitigation),    // Divine Veil
        new(7385, 19, JobRole.Tank, 70, 120, 18, CooldownCategory.Mitigation),   // Passage of Arms
        new(30, 19, JobRole.Tank, 50, 420, 10, CooldownCategory.TankInvuln),     // Hallowed Ground
        new(7388, 21, JobRole.Tank, 68, 90, 30, CooldownCategory.Mitigation),    // Shake It Off
        new(43, 21, JobRole.Tank, 42, 240, 10, CooldownCategory.TankInvuln),     // Holmgang
        new(16471, 32, JobRole.Tank, 66, 90, 15, CooldownCategory.Mitigation),   // Dark Missionary
        new(3638, 32, JobRole.Tank, 50, 300, 10, CooldownCategory.TankInvuln),   // Living Dead
        new(16160, 37, JobRole.Tank, 64, 90, 15, CooldownCategory.Mitigation),   // Heart of Light
        new(16152, 37, JobRole.Tank, 50, 360, 10, CooldownCategory.TankInvuln),  // Superbolide

        // Healers
        new(16536, 24, JobRole.Healer, 80, 120, 20, CooldownCategory.Mitigation), // Temperance
        new(3569, 24, JobRole.Healer, 52, 90, 24, CooldownCategory.Healing),      // Asylum
        new(25862, 24, JobRole.Healer, 90, 180, 20, CooldownCategory.Healing),    // Liturgy of the Bell
        new(7436, 28, JobRole.Healer, 66, 120, 20, CooldownCategory.RaidBuff),    // Chain Stratagem
        new(188, 28, JobRole.Healer, 50, 30, 15, CooldownCategory.Mitigation),    // Sacred Soil
        new(805, 28, JobRole.Healer, 40, 120, 20, CooldownCategory.Mitigation),   // Fey Illumination
        new(25868, 28, JobRole.Healer, 90, 120, 20, CooldownCategory.Mitigation), // Expedient
        new(37014, 28, JobRole.Healer, 100, 180, 20, CooldownCategory.Healing),   // Seraphism
        new(16552, 33, JobRole.Healer, 50, 120, 20, CooldownCategory.RaidBuff),   // Divination
        new(3613, 33, JobRole.Healer, 58, 60, 18, CooldownCategory.Mitigation),   // Collective Unconscious
        new(16559, 33, JobRole.Healer, 80, 120, 20, CooldownCategory.Healing),    // Neutral Sect
        new(25874, 33, JobRole.Healer, 90, 180, 15, CooldownCategory.Healing),    // Macrocosmos
        new(24298, 40, JobRole.Healer, 50, 30, 15, CooldownCategory.Mitigation),  // Kerachole
        new(24310, 40, JobRole.Healer, 76, 120, 20, CooldownCategory.Mitigation), // Holos
        new(24311, 40, JobRole.Healer, 80, 120, 15, CooldownCategory.Mitigation), // Panhaima
        new(37035, 40, JobRole.Healer, 100, 180, 20, CooldownCategory.Healing),   // Philosophia

        // Melee
        new(7396, 20, JobRole.Melee, 70, 120, 20, CooldownCategory.RaidBuff),     // Brotherhood
        new(65, 20, JobRole.Melee, 42, 90, 15, CooldownCategory.Healing),         // Mantra
        new(3557, 22, JobRole.Melee, 52, 120, 20, CooldownCategory.RaidBuff),     // Battle Litany
        new(2248, 30, JobRole.Melee, 15, 120, 20, CooldownCategory.RaidBuff, 67), // Mug
        new(36957, 30, JobRole.Melee, 68, 120, 20, CooldownCategory.RaidBuff),    // Dokumori
        new(24405, 39, JobRole.Melee, 72, 120, 20, CooldownCategory.RaidBuff),    // Arcane Circle

        // Physical ranged
        new(118, 23, JobRole.Ranged, 50, 120, 20, CooldownCategory.RaidBuff),     // Battle Voice
        new(25785, 23, JobRole.Ranged, 90, 110, 20, CooldownCategory.RaidBuff),   // Radiant Finale
        new(7405, 23, JobRole.Ranged, 62, 90, 15, CooldownCategory.Mitigation),   // Troubadour
        new(7408, 23, JobRole.Ranged, 66, 120, 15, CooldownCategory.Healing),     // Nature's Minne
        new(16004, 38, JobRole.Ranged, 70, 120, 20, CooldownCategory.RaidBuff),   // Technical Finish
        new(16012, 38, JobRole.Ranged, 56, 90, 15, CooldownCategory.Mitigation),  // Shield Samba
        new(16889, 31, JobRole.Ranged, 56, 90, 15, CooldownCategory.Mitigation),  // Tactician
        new(2887, 31, JobRole.Ranged, 62, 120, 10, CooldownCategory.Mitigation),  // Dismantle

        // Casters
        new(25801, 27, JobRole.Caster, 66, 120, 20, CooldownCategory.RaidBuff),   // Searing Light
        new(7520, 35, JobRole.Caster, 58, 120, 20, CooldownCategory.RaidBuff),    // Embolden
        new(25857, 35, JobRole.Caster, 86, 120, 10, CooldownCategory.Mitigation), // Magick Barrier
        new(35349, 42, JobRole.Caster, 70, 120, 20, CooldownCategory.RaidBuff),   // Starry Muse (Scenic Muse)
    };

    private static readonly Dictionary<uint, CooldownInfo> ById = Table.ToDictionary(c => c.ActionId);

    private readonly Hook<ActionEffectHandler.Delegates.Receive>? hook;
    private readonly Dictionary<(uint Entity, uint Action), double> lastUsed = new();

    public PartyCooldownTracker()
    {
        try
        {
            hook = Svc.Interop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(ActionEffectHandler.MemberFunctionPointers.Receive, OnActionEffect);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Party cooldowns: could not hook action effects; the tracker will stay empty.");
        }

        Svc.DutyState.DutyWiped += OnReset;
        Svc.DutyState.DutyRecommenced += OnReset;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public static double Now => Environment.TickCount64 / 1000.0;

    /// <summary>Turns the hook on only while the module is in use.</summary>
    public void SetEnabled(bool enabled)
    {
        if (hook == null || hook.IsEnabled == enabled)
        {
            return;
        }

        if (enabled)
        {
            hook.Enable();
        }
        else
        {
            hook.Disable();
            lastUsed.Clear();
        }
    }

    /// <summary>Seconds since <paramref name="entityId"/> last used <paramref name="actionId"/>, or null if never.</summary>
    public double? SinceUsed(uint entityId, uint actionId) =>
        lastUsed.TryGetValue((entityId, actionId), out var at) ? Now - at : null;

    public static IReadOnlyDictionary<uint, CooldownInfo> Lookup => ById;

    private void OnActionEffect(uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
    {
        hook!.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);

        try
        {
            // ActionType 1 = a normal action.
            if (header == null || header->ActionType != 1 || !ById.ContainsKey(header->ActionId))
            {
                return;
            }

            // Actions used by pets (e.g. a fairy's Fey Illumination) count for the owner.
            var owner = casterEntityId;
            if (Svc.ObjectTable.SearchByEntityId(casterEntityId) is IBattleNpc npc && npc.OwnerId != 0 && npc.OwnerId != 0xE0000000)
            {
                owner = npc.OwnerId;
            }

            lastUsed[(owner, header->ActionId)] = Now;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Party cooldowns: failed to record an action");
        }
    }

    private void OnReset(Dalamud.Game.DutyState.IDutyStateEventArgs args) => lastUsed.Clear();

    private void OnTerritoryChanged(uint territory) => lastUsed.Clear();

    public void Dispose()
    {
        Svc.DutyState.DutyWiped -= OnReset;
        Svc.DutyState.DutyRecommenced -= OnReset;
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        hook?.Disable();
        hook?.Dispose();
    }
}
