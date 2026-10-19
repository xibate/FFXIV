using System;
using System.Collections.Generic;
using System.Numerics;

namespace LucentUI.Style;

public static class Colors
{
    public static Vector4 Rgb(uint hex, float alpha = 1f) =>
        new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, alpha);

    /// <summary>Packs a colour as ImGui's ABGR uint, multiplying its alpha.</summary>
    public static uint ToU32(Vector4 c, float alphaMultiplier = 1f)
    {
        static uint Byte(float v) => (uint)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);
        return (Byte(c.W * alphaMultiplier) << 24) | (Byte(c.Z) << 16) | (Byte(c.Y) << 8) | Byte(c.X);
    }

    public static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    public static Vector4 Lighten(Vector4 c, float amount) =>
        new(c.X + (1f - c.X) * amount, c.Y + (1f - c.Y) * amount, c.Z + (1f - c.Z) * amount, c.W);

    public static Vector4 Darken(Vector4 c, float amount) =>
        new(c.X * (1f - amount), c.Y * (1f - amount), c.Z * (1f - amount), c.W);

    /// <summary>Red at 0%, yellow at 50%, green at 100%.</summary>
    public static Vector4 HealthGradient(float fraction)
    {
        var red = Rgb(0xC8423C);
        var yellow = Rgb(0xD8B23A);
        var green = Rgb(0x3E9F52);
        fraction = Math.Clamp(fraction, 0f, 1f);
        return fraction < 0.5f
            ? Vector4.Lerp(red, yellow, fraction * 2f)
            : Vector4.Lerp(yellow, green, (fraction - 0.5f) * 2f);
    }
}

public enum JobRole
{
    None,
    Tank,
    Healer,
    Melee,
    Ranged,
    Caster,
    Crafter,
    Gatherer,
}

public readonly record struct JobInfo(string Abbreviation, JobRole Role, Vector4 Color);

/// <summary>Static job data keyed by ClassJob row id.</summary>
public static class Jobs
{
    private static readonly JobInfo Unknown = new("?", JobRole.None, Colors.Rgb(0x9A9A9A));

    private static readonly Dictionary<uint, JobInfo> Table = BuildTable();

    public static JobInfo Get(uint jobId) => Table.TryGetValue(jobId, out var info) ? info : Unknown;

    public static Vector4 RoleColor(JobRole role) => role switch
    {
        JobRole.Tank => Colors.Rgb(0x3A6BD8),
        JobRole.Healer => Colors.Rgb(0x3CA864),
        JobRole.Melee or JobRole.Ranged or JobRole.Caster => Colors.Rgb(0xC8423C),
        JobRole.Crafter => Colors.Rgb(0xC9A04A),
        JobRole.Gatherer => Colors.Rgb(0x8BC34A),
        _ => Unknown.Color,
    };

    public static int RoleSortOrder(JobRole role) => role switch
    {
        JobRole.Tank => 0,
        JobRole.Healer => 1,
        JobRole.Melee => 2,
        JobRole.Ranged => 3,
        JobRole.Caster => 4,
        _ => 5,
    };

    /// <summary>Combat jobs (no base classes), grouped by role, for the per-job profile table.</summary>
    public static readonly uint[] CombatJobs =
    {
        19, 21, 32, 37,         // PLD WAR DRK GNB
        24, 28, 33, 40,         // WHM SCH AST SGE
        20, 22, 30, 34, 39, 41, // MNK DRG NIN SAM RPR VPR
        23, 31, 38,             // BRD MCH DNC
        25, 27, 35, 42, 36,     // BLM SMN RDM PCT BLU
    };

    private static Dictionary<uint, JobInfo> BuildTable()
    {
        var pld = Colors.Rgb(0xA8D2E6);
        var mnk = Colors.Rgb(0xD69C00);
        var war = Colors.Rgb(0xCF2621);
        var drg = Colors.Rgb(0x4164CD);
        var brd = Colors.Rgb(0x91BA5E);
        var whm = Colors.Rgb(0xFFF0DC);
        var blm = Colors.Rgb(0xA579D6);
        var smn = Colors.Rgb(0x2D9B78);
        var nin = Colors.Rgb(0xAF1964);
        var crafter = Colors.Rgb(0xC9A04A);
        var gatherer = Colors.Rgb(0x8BC34A);

        return new Dictionary<uint, JobInfo>
        {
            [1] = new("GLA", JobRole.Tank, pld),
            [2] = new("PGL", JobRole.Melee, mnk),
            [3] = new("MRD", JobRole.Tank, war),
            [4] = new("LNC", JobRole.Melee, drg),
            [5] = new("ARC", JobRole.Ranged, brd),
            [6] = new("CNJ", JobRole.Healer, whm),
            [7] = new("THM", JobRole.Caster, blm),
            [8] = new("CRP", JobRole.Crafter, crafter),
            [9] = new("BSM", JobRole.Crafter, crafter),
            [10] = new("ARM", JobRole.Crafter, crafter),
            [11] = new("GSM", JobRole.Crafter, crafter),
            [12] = new("LTW", JobRole.Crafter, crafter),
            [13] = new("WVR", JobRole.Crafter, crafter),
            [14] = new("ALC", JobRole.Crafter, crafter),
            [15] = new("CUL", JobRole.Crafter, crafter),
            [16] = new("MIN", JobRole.Gatherer, gatherer),
            [17] = new("BTN", JobRole.Gatherer, gatherer),
            [18] = new("FSH", JobRole.Gatherer, gatherer),
            [19] = new("PLD", JobRole.Tank, pld),
            [20] = new("MNK", JobRole.Melee, mnk),
            [21] = new("WAR", JobRole.Tank, war),
            [22] = new("DRG", JobRole.Melee, drg),
            [23] = new("BRD", JobRole.Ranged, brd),
            [24] = new("WHM", JobRole.Healer, whm),
            [25] = new("BLM", JobRole.Caster, blm),
            [26] = new("ACN", JobRole.Caster, smn),
            [27] = new("SMN", JobRole.Caster, smn),
            [28] = new("SCH", JobRole.Healer, Colors.Rgb(0x8657FF)),
            [29] = new("ROG", JobRole.Melee, nin),
            [30] = new("NIN", JobRole.Melee, nin),
            [31] = new("MCH", JobRole.Ranged, Colors.Rgb(0x6EE1D6)),
            [32] = new("DRK", JobRole.Tank, Colors.Rgb(0xD126CC)),
            [33] = new("AST", JobRole.Healer, Colors.Rgb(0xFFE74A)),
            [34] = new("SAM", JobRole.Melee, Colors.Rgb(0xE46D04)),
            [35] = new("RDM", JobRole.Caster, Colors.Rgb(0xE87B7B)),
            [36] = new("BLU", JobRole.Caster, Colors.Rgb(0x3366FF)),
            [37] = new("GNB", JobRole.Tank, Colors.Rgb(0x796D30)),
            [38] = new("DNC", JobRole.Ranged, Colors.Rgb(0xE2B0AF)),
            [39] = new("RPR", JobRole.Melee, Colors.Rgb(0x965A90)),
            [40] = new("SGE", JobRole.Healer, Colors.Rgb(0x80A0F0)),
            [41] = new("VPR", JobRole.Melee, Colors.Rgb(0x108210)),
            [42] = new("PCT", JobRole.Caster, Colors.Rgb(0xFC92E1)),
        };
    }
}
