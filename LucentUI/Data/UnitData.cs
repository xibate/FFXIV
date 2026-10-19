using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using LucentUI.Style;

namespace LucentUI.Data;

public enum UnitKind
{
    Player,
    Target,
    TargetOfTarget,
    Focus,
}

public enum UnitReaction
{
    Player,
    Hostile,
    Neutral,
    Friendly,
}

public enum PowerKind
{
    Mana,
    Gp,
    Cp,
}

/// <summary>A snapshot of everything a frame needs to draw one unit. Also used for preview data.</summary>
public struct UnitData
{
    public IGameObject? Object;
    public string Name;
    public uint Level;
    public uint Hp;
    public uint MaxHp;
    public uint Power;
    public uint MaxPower;
    public PowerKind PowerKind;
    public float ShieldFraction;
    public uint JobId;
    public UnitReaction Reaction;
    public bool InRange;

    public readonly float HpFraction => MaxHp == 0 ? 1f : (float)Hp / MaxHp;
    public readonly float PowerFraction => MaxPower == 0 ? 0f : (float)Power / MaxPower;
    public readonly bool IsDead => MaxHp > 0 && Hp == 0;
    public readonly bool HasHealth => MaxHp > 0;
}

public static class Units
{
    public static IGameObject? Get(UnitKind kind)
    {
        switch (kind)
        {
            case UnitKind.Player:
                return Svc.ObjectTable.LocalPlayer;
            case UnitKind.Target:
                return Svc.TargetManager.SoftTarget ?? Svc.TargetManager.Target;
            case UnitKind.TargetOfTarget:
                var target = Svc.TargetManager.SoftTarget ?? Svc.TargetManager.Target;
                return target?.TargetObject;
            case UnitKind.Focus:
                return Svc.TargetManager.FocusTarget;
            default:
                return null;
        }
    }

    public static bool TryRead(IGameObject? obj, out UnitData data)
    {
        data = default;
        if (obj == null || !obj.IsValid())
        {
            return false;
        }

        data.Object = obj;
        data.Name = obj.Name.TextValue;
        data.InRange = true;
        data.Reaction = UnitReaction.Neutral;

        if (obj is ICharacter character)
        {
            data.Hp = character.CurrentHp;
            data.MaxHp = character.MaxHp;
            data.Level = character.Level;
            data.JobId = character.ClassJob.RowId;
            data.ShieldFraction = character.ShieldPercentage / 100f;

            switch (Jobs.Get(data.JobId).Role)
            {
                case JobRole.Crafter:
                    data.Power = character.CurrentCp;
                    data.MaxPower = character.MaxCp;
                    data.PowerKind = PowerKind.Cp;
                    break;
                case JobRole.Gatherer:
                    data.Power = character.CurrentGp;
                    data.MaxPower = character.MaxGp;
                    data.PowerKind = PowerKind.Gp;
                    break;
                default:
                    data.Power = character.CurrentMp;
                    data.MaxPower = character.MaxMp;
                    data.PowerKind = PowerKind.Mana;
                    break;
            }

            data.Reaction = obj switch
            {
                IPlayerCharacter => UnitReaction.Player,
                IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant } => (character.StatusFlags & StatusFlags.Hostile) != 0
                    ? UnitReaction.Hostile
                    : UnitReaction.Neutral,
                _ => UnitReaction.Friendly,
            };
        }

        return true;
    }

    /// <summary>Fake data shown in Unlock Mode when the real unit does not exist.</summary>
    public static UnitData Preview(UnitKind kind)
    {
        var player = Svc.ObjectTable.LocalPlayer;
        var playerJob = player?.ClassJob.RowId ?? 19u;
        var playerName = player?.Name.TextValue ?? "Your Name";

        return kind switch
        {
            UnitKind.Player => Sample(playerName, playerJob, 0.86f, UnitReaction.Player),
            UnitKind.Target => Sample("Striking Dummy", 0, 0.62f, UnitReaction.Hostile),
            UnitKind.TargetOfTarget => Sample(playerName, playerJob, 0.86f, UnitReaction.Player),
            _ => Sample("Ifrit", 0, 0.35f, UnitReaction.Hostile),
        };
    }

    public static UnitData Sample(string name, uint jobId, float hpFraction, UnitReaction reaction, float shield = 0.12f)
    {
        const uint maxHp = 128_400;
        return new UnitData
        {
            Name = name,
            Level = 100,
            Hp = (uint)(maxHp * hpFraction),
            MaxHp = maxHp,
            Power = 7_800,
            MaxPower = 10_000,
            PowerKind = PowerKind.Mana,
            ShieldFraction = shield,
            JobId = jobId,
            Reaction = reaction,
            InRange = true,
        };
    }
}
