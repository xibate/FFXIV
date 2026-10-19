using System;
using Dalamud.Game.ClientState.Conditions;
using LucentUI.Config;

namespace LucentUI.Hud;

public static class Visibility
{
    /// <summary>Target opacity for an element with these visibility rules (1 = fully shown).</summary>
    public static float TargetAlpha(VisibilitySettings? settings)
    {
        if (settings == null)
        {
            return 1f;
        }

        var opacity = Math.Clamp(settings.Opacity, 0f, 1f);
        return (IsVisible(settings.Mode) ? 1f : Math.Clamp(settings.HiddenAlpha, 0f, 1f)) * opacity;
    }

    public static bool IsVisible(VisibilityMode mode) => mode switch
    {
        VisibilityMode.Always => true,
        VisibilityMode.InCombat => InCombat,
        VisibilityMode.InCombatOrTargeting => InCombat || Svc.TargetManager.Target != null,
        VisibilityMode.OutOfCombat => !InCombat,
        VisibilityMode.InDuty => InDuty,
        _ => false,
    };

    public static bool InCombat => Svc.Condition[ConditionFlag.InCombat];

    public static bool InDuty =>
        Svc.Condition[ConditionFlag.BoundByDuty] ||
        Svc.Condition[ConditionFlag.BoundByDuty56] ||
        Svc.Condition[ConditionFlag.BoundByDuty95];

    /// <summary>States where the game hides its own HUD, so ours should go too.</summary>
    public static bool InCutsceneOrLoading =>
        Svc.Condition[ConditionFlag.WatchingCutscene] ||
        Svc.Condition[ConditionFlag.WatchingCutscene78] ||
        Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
        Svc.Condition[ConditionFlag.BetweenAreas] ||
        Svc.Condition[ConditionFlag.BetweenAreas51] ||
        Svc.Condition[ConditionFlag.CreatingCharacter] ||
        Svc.Condition[ConditionFlag.OccupiedSummoningBell];
}
