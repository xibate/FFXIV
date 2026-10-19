using System;
using System.Collections.Generic;

namespace LucentUI.Hud;

/// <summary>Eases bar values towards their real value so health changes slide instead of jumping.</summary>
public sealed class Smoother
{
    private const float Speed = 4f;
    private const int PurgeEvery = 600;

    private readonly Dictionary<long, (float Value, int Frame)> values = new();
    private int frame;

    public bool Enabled = true;

    public void NextFrame()
    {
        frame++;
        if (frame % PurgeEvery == 0)
        {
            var stale = new List<long>();
            foreach (var (key, entry) in values)
            {
                if (frame - entry.Frame > 120)
                {
                    stale.Add(key);
                }
            }

            foreach (var key in stale)
            {
                values.Remove(key);
            }
        }
    }

    /// <summary>Returns the eased value for <paramref name="key"/>, moving it towards <paramref name="target"/>.</summary>
    public float Get(long key, float target, float deltaTime)
    {
        if (!Enabled)
        {
            return target;
        }

        if (!values.TryGetValue(key, out var entry) || frame - entry.Frame > 2)
        {
            // New (or reappearing) unit: start at the real value.
            values[key] = (target, frame);
            return target;
        }

        var value = entry.Value;
        var step = Math.Max(0.004f, Math.Abs(target - value) * Speed * deltaTime + 0.2f * deltaTime);
        value = value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);
        values[key] = (value, frame);
        return value;
    }
}
