using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.JobGauge.Enums;
using Dalamud.Game.ClientState.JobGauge.Types;
using LucentUI.Style;
using static LucentUI.Gauges.GaugeContext;

namespace LucentUI.Gauges;

/// <summary>
/// The bars shown for each job. Status ids are the game's (Status sheet row ids); timers that come from the job
/// gauge itself are in milliseconds.
/// </summary>
public static class JobGaugeDefinitions
{
    private static readonly Vector4 Red = Colors.Rgb(0xD0473F);
    private static readonly Vector4 Orange = Colors.Rgb(0xE8893A);
    private static readonly Vector4 Gold = Colors.Rgb(0xE8C547);
    private static readonly Vector4 Green = Colors.Rgb(0x5CBF5C);
    private static readonly Vector4 Teal = Colors.Rgb(0x3FD0C9);
    private static readonly Vector4 Blue = Colors.Rgb(0x3D8BF2);
    private static readonly Vector4 LightBlue = Colors.Rgb(0x8FC8FF);
    private static readonly Vector4 Purple = Colors.Rgb(0x9D6BE0);
    private static readonly Vector4 Pink = Colors.Rgb(0xE87BC0);
    private static readonly Vector4 White = Colors.Rgb(0xF2F2F2);
    private static readonly Vector4 Grey = Colors.Rgb(0x9A9A9A);

    public static readonly IReadOnlyDictionary<uint, GaugePart[]> ByJob = new Dictionary<uint, GaugePart[]>
    {
        // ── Tanks ────────────────────────────────────────────────────────────
        [19] = new GaugePart[] // PLD
        {
            new("oath", "Oath Gauge", GaugeKind.Resource, Gold, c => Resource(c.Gauge<PLDGauge>().OathGauge, 100), 35),
            new("fof", "Fight or Flight", GaugeKind.Timer, Orange, c => c.Buff(20, 76), 2),
            new("requiescat", "Requiescat", GaugeKind.Stacks, LightBlue, c => c.BuffStacks(4, 1368), 68),
        },
        [21] = new GaugePart[] // WAR
        {
            new("beast", "Beast Gauge", GaugeKind.Resource, Orange, c => Resource(c.Gauge<WARGauge>().BeastGauge, 100), 35),
            new("tempest", "Surging Tempest", GaugeKind.Timer, Red, c => c.Buff(60, 2677), 50),
            new("innerRelease", "Inner Release", GaugeKind.Stacks, Gold, c => c.BuffStacks(3, 1177, 86), 6),
        },
        [32] = new GaugePart[] // DRK
        {
            new("blood", "Blood Gauge", GaugeKind.Resource, Red, c => Resource(c.Gauge<DRKGauge>().Blood, 100), 62),
            new("mana", "MP", GaugeKind.Resource, Purple, c => c.Mana()),
            new("darkside", "Darkside", GaugeKind.Timer, Purple, c => TimerMs(c.Gauge<DRKGauge>().DarksideTimeRemaining, 60), 30),
            new("darkArts", "Dark Arts", GaugeKind.Stacks, Pink, c => Stacks(c.Gauge<DRKGauge>().HasDarkArts ? 1 : 0, 1), 30),
            new("delirium", "Delirium", GaugeKind.Stacks, Red, c => c.BuffStacks(3, 1972, 742), 35),
            new("shadow", "Living Shadow", GaugeKind.Timer, Grey, c => TimerMs(c.Gauge<DRKGauge>().ShadowTimeRemaining, 20), 80),
        },
        [37] = new GaugePart[] // GNB
        {
            new("ammo", "Cartridges", GaugeKind.Stacks, LightBlue, c => Stacks(c.Gauge<GNBGauge>().Ammo, c.Level >= 88 ? 3 : 2), 30),
            new("noMercy", "No Mercy", GaugeKind.Timer, Orange, c => c.Buff(20, 1831), 2),
        },

        // ── Healers ──────────────────────────────────────────────────────────
        [24] = new GaugePart[] // WHM
        {
            new("lily", "Lilies", GaugeKind.Stacks, LightBlue, c => Stacks(c.Gauge<WHMGauge>().Lily, 3), 52, Shape: GaugeShape.Petal),
            new("lilyTimer", "Next Lily", GaugeKind.Timer, White, c => TimerMs(c.Gauge<WHMGauge>().LilyTimer, 20), 52),
            new("bloodLily", "Blood Lily", GaugeKind.Stacks, Red, c => Stacks(c.Gauge<WHMGauge>().BloodLily, 3), 74, Shape: GaugeShape.Petal),
            new("dia", "Dia", GaugeKind.Timer, LightBlue, c => c.Dot(30, 143, 144, 1871), 4),
            new("mana", "MP", GaugeKind.Resource, Blue, c => c.Mana(), 1, false),
        },
        [28] = new GaugePart[] // SCH
        {
            new("aetherflow", "Aetherflow", GaugeKind.Stacks, Teal, c => Stacks(c.Gauge<SCHGauge>().Aetherflow, 3), 45),
            new("fairy", "Fairy Gauge", GaugeKind.Resource, Green, c => Resource(c.Gauge<SCHGauge>().FairyGauge, 100), 52),
            new("seraph", "Seraph", GaugeKind.Timer, Gold, c => TimerMs(c.Gauge<SCHGauge>().SeraphTimer, 22), 80),
            new("bio", "Biolysis", GaugeKind.Timer, Green, c => c.Dot(30, 179, 189, 1895), 2),
            new("mana", "MP", GaugeKind.Resource, Blue, c => c.Mana(), 1, false),
        },
        [33] = new GaugePart[] // AST
        {
            new("cards", "Cards", GaugeKind.Pips, Gold, c => AstCards(c.Gauge<ASTGauge>()), 30),
            new("combust", "Combust", GaugeKind.Timer, Gold, c => c.Dot(30, 838, 843, 1881), 4),
            new("lightspeed", "Lightspeed", GaugeKind.Timer, LightBlue, c => c.Buff(15, 841), 6),
            new("mana", "MP", GaugeKind.Resource, Blue, c => c.Mana(), 1, false),
        },
        [40] = new GaugePart[] // SGE
        {
            new("addersgall", "Addersgall", GaugeKind.Stacks, Teal, c => Stacks(c.Gauge<SGEGauge>().Addersgall, 3), 45),
            new("addersgallTimer", "Next Addersgall", GaugeKind.Timer, White, c => TimerMs(c.Gauge<SGEGauge>().AddersgallTimer, 20), 45),
            new("addersting", "Addersting", GaugeKind.Stacks, Red, c => Stacks(c.Gauge<SGEGauge>().Addersting, 3), 66),
            new("eukrasia", "Eukrasia", GaugeKind.Stacks, LightBlue, c => Stacks(c.Gauge<SGEGauge>().Eukrasia ? 1 : 0, 1), 30),
            new("dosis", "Eukrasian Dosis", GaugeKind.Timer, LightBlue, c => c.Dot(30, 2614, 2615, 2616, 3897), 30),
            new("mana", "MP", GaugeKind.Resource, Blue, c => c.Mana(), 1, false),
        },

        // ── Melee ────────────────────────────────────────────────────────────
        [20] = new GaugePart[] // MNK
        {
            new("chakra", "Chakra", GaugeKind.Stacks, Gold, c => Stacks(c.Gauge<MNKGauge>().Chakra, 5), 15),
            new("beastChakra", "Beast Chakra", GaugeKind.Pips, Pink, c => MnkBeastChakra(c.Gauge<MNKGauge>()), 60),
            new("nadi", "Nadi", GaugeKind.Pips, White, c => MnkNadi(c.Gauge<MNKGauge>()), 60),
            new("perfectBalance", "Perfect Balance", GaugeKind.Stacks, Orange, c => c.BuffStacks(3, 110), 50),
            new("riddleOfFire", "Riddle of Fire", GaugeKind.Timer, Red, c => c.Buff(20, 1181), 68),
        },
        [22] = new GaugePart[] // DRG
        {
            new("focus", "Firstminds' Focus", GaugeKind.Stacks, Red, c => Stacks(c.Gauge<DRGGauge>().FirstmindsFocusCount, 2), 90),
            new("lotd", "Life of the Dragon", GaugeKind.Timer, Orange, c => TimerMs(c.Gauge<DRGGauge>().LOTDTimer, 20), 70),
            new("powerSurge", "Power Surge", GaugeKind.Timer, Gold, c => c.Buff(30, 2720), 18),
            new("chaosThrust", "Chaotic Spring", GaugeKind.Timer, Purple, c => c.Dot(24, 118, 1312, 2719), 50),
        },
        [30] = new GaugePart[] // NIN
        {
            new("ninki", "Ninki", GaugeKind.Resource, Purple, c => Resource(c.Gauge<NINGauge>().Ninki, 100), 62),
            new("kazematoi", "Kazematoi", GaugeKind.Stacks, LightBlue, c => Stacks(c.Gauge<NINGauge>().Kazematoi, 5), 94),
            new("kunai", "Kunai's Bane", GaugeKind.Timer, Red, c => c.Dot(16, 3254, 3906), 18),
            new("shadowWalker", "Shadow Walker", GaugeKind.Timer, Teal, c => c.Buff(20, 3848), 45),
        },
        [34] = new GaugePart[] // SAM
        {
            new("kenki", "Kenki", GaugeKind.Resource, Red, c => Resource(c.Gauge<SAMGauge>().Kenki, 100), 52, Segments: 4),
            new("sen", "Sen", GaugeKind.Pips, White, c => SamSen(c.Gauge<SAMGauge>()), 30, Shape: GaugeShape.Diamond),
            new("meditation", "Meditation", GaugeKind.Stacks, Orange, c => Stacks(c.Gauge<SAMGauge>().MeditationStacks, 3), 80),
            new("fugetsu", "Fugetsu", GaugeKind.Timer, LightBlue, c => c.Buff(40, 1298), 4),
            new("fuka", "Fuka", GaugeKind.Timer, Green, c => c.Buff(40, 1299), 18),
            new("higanbana", "Higanbana", GaugeKind.Timer, Orange, c => c.Dot(60, 1228, 1319), 30),
        },
        [39] = new GaugePart[] // RPR
        {
            new("soul", "Soul Gauge", GaugeKind.Resource, Red, c => Resource(c.Gauge<RPRGauge>().Soul, 100), 10),
            new("shroud", "Shroud Gauge", GaugeKind.Resource, Teal, c => Resource(c.Gauge<RPRGauge>().Shroud, 100), 70),
            new("lemure", "Lemure Shroud", GaugeKind.Stacks, LightBlue, c => Stacks(c.Gauge<RPRGauge>().LemureShroud, 5), 80),
            new("void", "Void Shroud", GaugeKind.Stacks, Purple, c => Stacks(c.Gauge<RPRGauge>().VoidShroud, 5), 80),
            new("enshroud", "Enshroud", GaugeKind.Timer, Teal, c => TimerMs(c.Gauge<RPRGauge>().EnshroudedTimeRemaining, 30), 80),
            new("deathsDesign", "Death's Design", GaugeKind.Timer, Purple, c => c.Dot(60, 2586), 10),
        },
        [41] = new GaugePart[] // VPR
        {
            new("offering", "Serpent Offering", GaugeKind.Resource, Teal, c => Resource(c.Gauge<VPRGauge>().SerpentOffering, 100), 70),
            new("coils", "Rattling Coils", GaugeKind.Stacks, Red, c => Stacks(c.Gauge<VPRGauge>().RattlingCoilStacks, c.Level >= 88 ? 3 : 2), 82),
            new("tribute", "Anguine Tribute", GaugeKind.Stacks, LightBlue, c => Stacks(c.Gauge<VPRGauge>().AnguineTribute, c.Level >= 96 ? 5 : 4), 90),
            new("reawakened", "Reawakened", GaugeKind.Timer, Gold, c => c.Buff(30, 3670, 4094), 90),
        },

        // ── Physical ranged ──────────────────────────────────────────────────
        [23] = new GaugePart[] // BRD
        {
            new("song", "Song", GaugeKind.Timer, Green, c => BrdSong(c.Gauge<BRDGauge>()), 30),
            new("repertoire", "Repertoire", GaugeKind.Stacks, Gold, c => BrdRepertoire(c.Gauge<BRDGauge>()), 30),
            new("soulVoice", "Soul Voice", GaugeKind.Resource, LightBlue, c => Resource(c.Gauge<BRDGauge>().SoulVoice, 100), 80),
            new("coda", "Coda", GaugeKind.Pips, White, c => BrdCoda(c.Gauge<BRDGauge>()), 90),
            new("caustic", "Caustic Bite", GaugeKind.Timer, Purple, c => c.Dot(45, 124, 1200), 6),
            new("storm", "Stormbite", GaugeKind.Timer, LightBlue, c => c.Dot(45, 129, 1201), 30),
        },
        [31] = new GaugePart[] // MCH
        {
            new("heat", "Heat Gauge", GaugeKind.Resource, Orange, c => Resource(c.Gauge<MCHGauge>().Heat, 100), 30),
            new("battery", "Battery", GaugeKind.Resource, LightBlue, c => Resource(c.Gauge<MCHGauge>().Battery, 100), 40),
            new("overheat", "Overheated", GaugeKind.Timer, Red, c => TimerMs(c.Gauge<MCHGauge>().OverheatTimeRemaining, 10), 30),
            new("robot", "Automaton", GaugeKind.Timer, Teal, c => MchRobot(c.Gauge<MCHGauge>()), 40),
            new("wildfire", "Wildfire", GaugeKind.Timer, Orange, c => c.Buff(10, 1946), 45),
        },
        [38] = new GaugePart[] // DNC
        {
            new("steps", "Dance Steps", GaugeKind.Pips, White, c => DncSteps(c.Gauge<DNCGauge>()), 15),
            new("feathers", "Fourfold Feathers", GaugeKind.Stacks, Green, c => Stacks(c.Gauge<DNCGauge>().Feathers, 4), 62),
            new("esprit", "Esprit", GaugeKind.Resource, Pink, c => Resource(c.Gauge<DNCGauge>().Esprit, 100), 76),
            new("standard", "Standard Finish", GaugeKind.Timer, Gold, c => c.Buff(60, 1821, 2024, 2105, 2113), 15),
            new("technical", "Technical Finish", GaugeKind.Timer, Pink, c => c.Buff(20, 1822, 2050), 70),
            new("devilment", "Devilment", GaugeKind.Timer, Red, c => c.Buff(20, 1825), 62),
            new("cascade", "Silken Symmetry", GaugeKind.Timer, LightBlue, c => c.Buff(30, 2693, 3017), 1),
            new("fountain", "Silken Flow", GaugeKind.Timer, Green, c => c.Buff(30, 2694, 3018), 2),
        },

        // ── Casters ──────────────────────────────────────────────────────────
        [25] = new GaugePart[] // BLM
        {
            new("mana", "MP", GaugeKind.Resource, Blue, c => c.Mana()),
            new("element", "Astral Fire / Umbral Ice", GaugeKind.Pips, Red, c => BlmElement(c.Gauge<BLMGauge>()), 1),
            new("hearts", "Umbral Hearts", GaugeKind.Stacks, LightBlue, c => Stacks(c.Gauge<BLMGauge>().UmbralHearts, 3), 58),
            new("polyglot", "Polyglot", GaugeKind.Stacks, Purple, c => Stacks(c.Gauge<BLMGauge>().PolyglotStacks, c.Level >= 98 ? 3 : c.Level >= 80 ? 2 : 1), 70),
            new("polyglotTimer", "Next Polyglot", GaugeKind.Timer, Purple, c => BlmPolyglotTimer(c.Gauge<BLMGauge>()), 70),
            new("astralSoul", "Astral Soul", GaugeKind.Stacks, Orange, c => Stacks(c.Gauge<BLMGauge>().AstralSoulStacks, 6), 100),
            new("paradox", "Paradox", GaugeKind.Stacks, White, c => Stacks(c.Gauge<BLMGauge>().IsParadoxActive ? 1 : 0, 1), 90),
            new("thunder", "Thunder", GaugeKind.Timer, LightBlue, c => c.Dot(30, 161, 162, 163, 1210, 3871, 3872), 6),
            new("triplecast", "Triplecast", GaugeKind.Stacks, Gold, c => c.BuffStacks(3, 1211), 66),
        },
        [27] = new GaugePart[] // SMN
        {
            new("aetherflow", "Aetherflow", GaugeKind.Stacks, Teal, c => Stacks(c.Gauge<SMNGauge>().AetherflowStacks, 2), 2),
            new("attunement", "Attunement", GaugeKind.Stacks, Gold, c => SmnAttunement(c.Gauge<SMNGauge>()), 6),
            new("summon", "Demi-Summon", GaugeKind.Timer, Orange, c => TimerMs(c.Gauge<SMNGauge>().SummonTimerRemaining, 15), 58),
            new("primals", "Primals Ready", GaugeKind.Pips, White, c => SmnPrimals(c.Gauge<SMNGauge>()), 6),
            new("mana", "MP", GaugeKind.Resource, Blue, c => c.Mana(), 1, false),
        },
        [35] = new GaugePart[] // RDM
        {
            new("white", "White Mana", GaugeKind.Resource, White, c => Resource(c.Gauge<RDMGauge>().WhiteMana, 100), 2),
            new("black", "Black Mana", GaugeKind.Resource, Purple, c => Resource(c.Gauge<RDMGauge>().BlackMana, 100), 2),
            new("manaStacks", "Mana Stacks", GaugeKind.Stacks, Red, c => Stacks(c.Gauge<RDMGauge>().ManaStacks, 3), 35),
            new("dualcast", "Dualcast", GaugeKind.Timer, Pink, c => c.Buff(15, 1249), 1),
            new("verfire", "Verfire Ready", GaugeKind.Timer, Red, c => c.Buff(30, 1234), 26),
            new("verstone", "Verstone Ready", GaugeKind.Timer, White, c => c.Buff(30, 1235), 30),
            new("mana", "MP", GaugeKind.Resource, Blue, c => c.Mana(), 1, false),
        },
        [42] = new GaugePart[] // PCT
        {
            new("palette", "Palette Gauge", GaugeKind.Resource, Pink, c => Resource(c.Gauge<PCTGauge>().PalleteGauge, 100), 60),
            new("paint", "White Paint", GaugeKind.Stacks, White, c => Stacks(c.Gauge<PCTGauge>().Paint, 5), 60),
            new("motifs", "Motifs", GaugeKind.Pips, Teal, c => PctMotifs(c.Gauge<PCTGauge>()), 30),
            new("hammer", "Hammer Time", GaugeKind.Stacks, Gold, c => c.BuffStacks(3, 3680), 86),
            new("hyperphantasia", "Hyperphantasia", GaugeKind.Stacks, LightBlue, c => c.BuffStacks(5, 3688), 70),
            new("mana", "MP", GaugeKind.Resource, Blue, c => c.Mana(), 1, false),
        },
    };

    /// <summary>All jobs with a gauge, for the settings page.</summary>
    public static IEnumerable<uint> Jobs => ByJob.Keys.OrderBy(j => System.Array.IndexOf(Style.Jobs.CombatJobs, j));

    // ── Job-specific readers ─────────────────────────────────────────────────

    private static GaugeValue AstCards(ASTGauge g)
    {
        var cards = new[] { g.Card1, g.Card2, g.Card3, g.DrawnCrownCard };
        return Pips(4, i => cards[i] switch
        {
            CardType.Balance => Orange,
            CardType.Spear => Blue,
            CardType.Bole or CardType.Arrow or CardType.Ewer or CardType.Spire => Green,
            CardType.Lord => Gold,
            CardType.Lady => Pink,
            _ => null,
        });
    }

    private static GaugeValue MnkBeastChakra(MNKGauge g)
    {
        var chakra = new[] { g.BeastChakra1, g.BeastChakra2, g.BeastChakra3 };
        return Pips(3, i => chakra[i] switch
        {
            BeastChakra.OpoOpo => Pink,
            BeastChakra.Raptor => Gold,
            BeastChakra.Coeurl => LightBlue,
            _ => null,
        });
    }

    private static GaugeValue MnkNadi(MNKGauge g) => Pips(2, i => i switch
    {
        0 when g.Nadi.HasFlag(Nadi.Lunar) => Purple,
        1 when g.Nadi.HasFlag(Nadi.Solar) => Gold,
        _ => null,
    });

    private static GaugeValue SamSen(SAMGauge g) => Pips(3, i => i switch
    {
        0 when g.HasSetsu => LightBlue,
        1 when g.HasGetsu => Purple,
        2 when g.HasKa => Red,
        _ => null,
    });

    private static GaugeValue BrdSong(BRDGauge g)
    {
        var value = TimerMs(g.SongTimer, 45);
        value.Color = g.Song switch
        {
            Song.WanderersMinuet => Green,
            Song.MagesBallad => Purple,
            Song.ArmysPaeon => Orange,
            _ => null,
        };
        if (!value.Hidden)
        {
            value.Text = g.Song switch
            {
                Song.WanderersMinuet => "Minuet ",
                Song.MagesBallad => "Ballad ",
                Song.ArmysPaeon => "Paeon ",
                _ => string.Empty,
            } + value.Text;
        }

        return value;
    }

    private static GaugeValue BrdRepertoire(BRDGauge g) => Stacks(g.Repertoire, g.Song == Song.ArmysPaeon ? 4 : 3);

    private static GaugeValue BrdCoda(BRDGauge g)
    {
        var coda = g.Coda;
        return Pips(3, i => (i < coda.Length ? coda[i] : Song.None) switch
        {
            Song.WanderersMinuet => Green,
            Song.MagesBallad => Purple,
            Song.ArmysPaeon => Orange,
            _ => null,
        });
    }

    private static GaugeValue MchRobot(MCHGauge g)
    {
        // Queen/Rook last longer with more battery: roughly 12s at 50 and 21s at 100.
        var max = g.LastSummonBatteryPower > 0 ? 3f + g.LastSummonBatteryPower * 0.18f : 21f;
        return TimerMs(g.SummonTimeRemaining, max);
    }

    private static readonly Dictionary<uint, Vector4> StepColors = new()
    {
        [15999] = Red,      // Emboite
        [16000] = Blue,     // Entrechat
        [16001] = Green,    // Jete
        [16002] = Gold,     // Pirouette
    };

    private static GaugeValue DncSteps(DNCGauge g)
    {
        if (!g.IsDancing)
        {
            return new GaugeValue { Hidden = true, Max = 4 };
        }

        var steps = g.Steps.Where(s => s != 0).ToArray();
        int completed = g.CompletedSteps;
        return Pips(steps.Length, i =>
        {
            var color = StepColors.TryGetValue(steps[i], out var c) ? c : White;
            return i < completed ? Colors.WithAlpha(color, 0.35f) : color;
        });
    }

    private static GaugeValue BlmElement(BLMGauge g)
    {
        var fire = g.InAstralFire;
        int stacks = fire ? g.AstralFireStacks : g.UmbralIceStacks;
        return Pips(3, i => i < stacks ? (fire ? Red : LightBlue) : null);
    }

    private static GaugeValue BlmPolyglotTimer(BLMGauge g)
    {
        if (!g.IsEnochianActive)
        {
            return new GaugeValue { Hidden = true, Max = 30 };
        }

        // EnochianTimer counts up to the next Polyglot.
        var remaining = 30f - g.EnochianTimer / 1000f;
        return Timer(remaining, 30);
    }

    private static GaugeValue SmnAttunement(SMNGauge g)
    {
        var max = g.AttunementType switch
        {
            SummonAttunement.Ifrit => 2,
            SummonAttunement.Titan => 4,
            SummonAttunement.Garuda => 4,
            _ => 4,
        };
        var value = Stacks(g.AttunementCount, max);
        value.Color = g.AttunementType switch
        {
            SummonAttunement.Ifrit => Red,
            SummonAttunement.Titan => Gold,
            SummonAttunement.Garuda => Green,
            _ => null,
        };
        return value;
    }

    private static GaugeValue SmnPrimals(SMNGauge g) => Pips(3, i => i switch
    {
        0 when g.IsIfritReady => Red,
        1 when g.IsTitanReady => Gold,
        2 when g.IsGarudaReady => Green,
        _ => null,
    });

    private static GaugeValue PctMotifs(PCTGauge g) => Pips(3, i => i switch
    {
        0 when g.CreatureMotifDrawn => Pink,
        1 when g.WeaponMotifDrawn => Red,
        2 when g.LandscapeMotifDrawn => LightBlue,
        _ => null,
    });
}
