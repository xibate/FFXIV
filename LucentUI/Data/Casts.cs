using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Lumina.Excel.Sheets;

namespace LucentUI.Data;

public struct CastData
{
    public string Name;
    public uint Icon;
    public float Current;
    public float Total;
    public bool Interruptible;

    public readonly float Fraction => Total <= 0f ? 0f : Current / Total;
    public readonly float Remaining => Total - Current;
}

public static class Casts
{
    // Values of FFXIVClientStructs' ActionType used by casts we can name.
    private const byte ActionTypeAction = 1;
    private const byte ActionTypeItem = 2;
    private const byte ActionTypeMount = 13;

    private static readonly Dictionary<(byte, uint), (string Name, uint Icon)> Cache = new();

    public static bool TryGet(IGameObject? obj, out CastData cast)
    {
        cast = default;
        if (obj is not IBattleChara chara || !chara.IsCasting || chara.TotalCastTime <= 0f)
        {
            return false;
        }

        var (name, icon) = Lookup((byte)chara.CastActionType, chara.CastActionId);
        cast = new CastData
        {
            Name = name,
            Icon = icon,
            Current = chara.CurrentCastTime,
            Total = chara.TotalCastTime,
            Interruptible = chara.IsCastInterruptible,
        };
        return true;
    }

    public static CastData Preview(bool interruptible) => new()
    {
        Name = interruptible ? "Interruptible Cast" : "Fire IV",
        Icon = 2660,
        Current = 1.2f,
        Total = 2.8f,
        Interruptible = interruptible,
    };

    private static (string Name, uint Icon) Lookup(byte type, uint id)
    {
        if (Cache.TryGetValue((type, id), out var cached))
        {
            return cached;
        }

        (string, uint) result = ("Casting", 0);
        switch (type)
        {
            case ActionTypeAction when Svc.DataManager.GetExcelSheet<Action>().GetRowOrDefault(id) is { } action:
                result = (action.Name.ExtractText(), action.Icon);
                break;
            case ActionTypeItem when Svc.DataManager.GetExcelSheet<Item>().GetRowOrDefault(id % 1_000_000) is { } item:
                result = (item.Name.ExtractText(), item.Icon);
                break;
            case ActionTypeMount when Svc.DataManager.GetExcelSheet<Mount>().GetRowOrDefault(id) is { } mount:
                result = (mount.Singular.ExtractText(), mount.Icon);
                break;
        }

        if (string.IsNullOrWhiteSpace(result.Item1))
        {
            result.Item1 = "Casting";
        }

        Cache[(type, id)] = result;
        return result;
    }
}
