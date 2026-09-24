namespace UnderWarden.Logic.Endgame;

/// <summary>
/// The Tribunal Hall — the authored layout for the Weighing (floor 25). Chosen 2026-06-01; see
/// memory project_weighing_arena_decision for the rationale.
///
/// Portrait hall (15×19). The player enters south and faces north up the hall. The Under-Warden
/// presides at the head (north wall); the Debt rises just before him. The four faction Guardians
/// flank the well in two ranks — Warden/Oathkeeper nearer the player (rise first), Assembly/Auditor
/// nearer the head (rise later). Allies fall back SOUTH, behind the player, so the player advances
/// up the hall alone to face the Debt — distance stages the solo beat, and the retreat is a choice.
///
/// v1 stores the grid as a C# constant (same precedent as DungeonFloorBuilder.CreateDefaultPlayer).
/// Migratable to YAML if/when we want data-driven or multi-template arenas (see the decision memo).
///
/// CO-DESIGN NOTE: the exact anchor positions — especially which Guardian rises when relative to
/// where the player stands — are finalized together with the audit-narration content. Treat the
/// rise order encoded here (Warden→Oathkeeper→Assembly→Auditor→Debt, south-to-north) as the working
/// default, tunable against the dialogue.
/// </summary>
public static class WeighingArenaDefinition
{
    // Legend: # wall · . floor · J Under-Warden (presides, north) · B Debt (head of hall)
    //         A Assembly · U Auditor (north rank) · W Warden · O Oathkeeper (south rank)
    //         P player start (the well) · F ally fall-back (south, behind the player)
    public static readonly string[] TribunalHall =
    {
        "###############", //  0
        "#......J......#", //  1  Under-Warden presides (north wall)
        "#......B......#", //  2  the Debt rises at the head of the hall
        "#.............#", //  3
        "#.............#", //  4
        "#...A.....U...#", //  5  Assembly / Auditor — north rank (rise 3rd, 4th)
        "#.............#", //  6
        "#.............#", //  7
        "#.............#", //  8
        "#...W.....O...#", //  9  Warden / Oathkeeper — south rank (rise 1st, 2nd)
        "#.............#", // 10
        "#.............#", // 11
        "#.............#", // 12
        "#......P......#", // 13  the player stands in the well, facing north
        "#.............#", // 14
        "#.............#", // 15
        "#.....F.F.....#", // 16  allies fall back south, behind the player
        "#.............#", // 17
        "###############", // 18
    };

    /// <summary>Build the Tribunal Hall arena (map + named anchors).</summary>
    public static WeighingArena Build() => WeighingArenaLoader.FromAscii(TribunalHall);
}
