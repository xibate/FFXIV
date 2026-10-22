using System;
using System.Numerics;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using LucentUI.Data;

namespace LucentUI.Gauges;

public enum GaugeKind
{
    /// <summary>A continuous bar, e.g. Beast Gauge 0-100.</summary>
    Resource,
    /// <summary>Equal segments filled left to right, e.g. cartridges.</summary>
    Stacks,
    /// <summary>A countdown bar, e.g. a buff or DoT.</summary>
    Timer,
    /// <summary>Segments with their own colours, e.g. Sen or dance steps.</summary>
    Pips,
}

/// <summary>How stacks and pips are drawn in the hybrid gauge look.</summary>
public enum GaugeShape
{
    /// <summary>Orbs for stacks, diamonds for pips.</summary>
    Auto,
    Diamond,
    Orb,
    Petal,
}

/// <summary>The state of one gauge part this frame.</summary>
public struct GaugeValue
{
    public float Value;
    public float Max;
    public bool Hidden;
    public string? Text;
    public Vector4? Color;

    /// <summary>For <see cref="GaugeKind.Pips"/>: colour of segment i, or null when empty.</summary>
    public Func<int, Vector4?>? PipColor;

    public readonly float Fraction => Max <= 0f ? 0f : Math.Clamp(Value / Max, 0f, 1f);
}

/// <summary>One bar of a job gauge.</summary>
/// <param name="Shape">Shape of stacks/pips in the hybrid look.</param>
/// <param name="Segments">Spending points the hybrid bar is split at (e.g. 4 for Kenki's 25s); 0 picks: halves for 0-100 gauges.</param>
public sealed record GaugePart(string Id, string Label, GaugeKind Kind, Vector4 DefaultColor, Func<GaugeContext, GaugeValue> Read, int MinLevel = 1, bool DefaultEnabled = true, GaugeShape Shape = GaugeShape.Auto, int Segments = 0);

/// <summary>What a gauge part can read: the player, their target, their level and helpers.</summary>
public sealed class GaugeContext
{
    public IPlayerCharacter Player = null!;
    public IGameObject? Target;
    public int Level;

    public T Gauge<T>()
        where T : JobGaugeBase => Svc.JobGauges.Get<T>();

    public static GaugeValue Resource(float value, float max, string? text = null) =>
        new() { Value = value, Max = max, Text = text ?? ((int)value).ToString() };

    public static GaugeValue Stacks(int value, int max) =>
        new() { Value = Math.Min(value, max), Max = max, Text = value.ToString() };

    public static GaugeValue Timer(float seconds, float max)
    {
        if (float.IsPositiveInfinity(seconds))
        {
            return new GaugeValue { Value = max, Max = max, Text = string.Empty };
        }

        seconds = Math.Max(0f, seconds);
        return new GaugeValue
        {
            Value = seconds,
            Max = max,
            Hidden = seconds <= 0f,
            Text = seconds <= 0f ? string.Empty : seconds < 10f ? seconds.ToString("0.0") : ((int)Math.Ceiling(seconds)).ToString(),
        };
    }

    public static GaugeValue Pips(int count, Func<int, Vector4?> colorOf) =>
        new() { Value = count, Max = count, PipColor = colorOf };

    /// <summary>Milliseconds (as most gauge timers report) to a timer value.</summary>
    public static GaugeValue TimerMs(float milliseconds, float maxSeconds) => Timer(milliseconds / 1000f, maxSeconds);

    /// <summary>A buff on the player.</summary>
    public GaugeValue Buff(float max, params uint[] ids) => Timer(Statuses.Find(Player, 0, ids).Remaining, max);

    /// <summary>Stacks of a buff on the player.</summary>
    public GaugeValue BuffStacks(int max, params uint[] ids)
    {
        var (remaining, stacks) = Statuses.Find(Player, 0, ids);
        return Stacks(remaining > 0f ? stacks : 0, max);
    }

    /// <summary>A debuff the player applied to their target.</summary>
    public GaugeValue Dot(float max, params uint[] ids) => Timer(Statuses.Find(Target, Player.EntityId, ids).Remaining, max);

    public GaugeValue Mana() => Resource(Player.CurrentMp, Math.Max(1, Player.MaxMp));
}
