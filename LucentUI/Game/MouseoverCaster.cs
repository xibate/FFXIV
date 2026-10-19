using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using LucentUI.Hud;
using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;

namespace LucentUI.Game;

/// <summary>
/// Mouseover casting: while the mouse is over a LucentUI frame, actions you use go to that unit instead of your
/// target, as long as the action can be used on it (heals on party members, damage on enemies).
/// </summary>
public sealed unsafe class MouseoverCaster : IDisposable
{
    private readonly Interaction input;
    private readonly Hook<ActionManager.Delegates.UseAction>? hook;

    public MouseoverCaster(Interaction input)
    {
        this.input = input;
        try
        {
            hook = Svc.Interop.HookFromAddress<ActionManager.Delegates.UseAction>(ActionManager.MemberFunctionPointers.UseAction, OnUseAction);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Mouseover casting: could not hook UseAction.");
        }
    }

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
        }
    }

    private bool OnUseAction(ActionManager* manager, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted)
    {
        try
        {
            var target = input.Hovered;
            if (actionType == ActionType.Action && target != null && target.IsValid())
            {
                var adjusted = manager->GetAdjustedActionId(actionId);
                if (CanTarget(adjusted, target) &&
                    ActionManager.CanUseActionOnTarget(adjusted, (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address))
                {
                    return hook!.Original(manager, actionType, actionId, target.GameObjectId, extraParam, mode, comboRouteId, outOptAreaTargeted);
                }
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Mouseover casting failed; using the normal target.");
        }

        return hook!.Original(manager, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
    }

    /// <summary>Whether an action makes sense on <paramref name="target"/> (same rules as DelvUI's mouseover mode).</summary>
    private static bool CanTarget(uint actionId, IGameObject target)
    {
        if (Svc.DataManager.GetExcelSheet<Action>().GetRowOrDefault(actionId) is not { } action || action.TargetArea)
        {
            return false;
        }

        // Actions that change into other actions (e.g. Continuation) have no attack type or animation and only
        // "target" self: treat them as hostile.
        if (action.AttackType.RowId == 0 && action.AnimationStart.RowId == 0 &&
            !action.CanTargetAlly && !action.CanTargetHostile && !action.CanTargetParty && action.CanTargetSelf)
        {
            // Astrologian cards and Rekindle go to friends.
            if (actionId is 37019 or 37020 or 37021 or 25822)
            {
                return target is IPlayerCharacter or IBattleNpc { BattleNpcKind: BattleNpcSubKind.Buddy };
            }

            return target is IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant };
        }

        return target switch
        {
            IPlayerCharacter => action.CanTargetAlly || action.CanTargetParty || action.CanTargetSelf,
            IBattleNpc { BattleNpcKind: not BattleNpcSubKind.Combatant } => action.CanTargetAlly || action.CanTargetParty || action.CanTargetSelf,
            _ => action.CanTargetHostile,
        };
    }

    public void Dispose()
    {
        hook?.Disable();
        hook?.Dispose();
    }
}
