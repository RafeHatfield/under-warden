using UnderWarden.Logic.Endgame;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Thin DTO capturing what happened in one game run.
/// Passed to MemoDeliveryEvaluator after each run ends so it can decide which
/// Under-Warden memos to queue.
///
/// CauseOfDeath and KillerSpecies are null when the player survived.
/// RunNumber is the run counter after incrementing (i.e. 1-based: "run 1" = first ever run).
/// </summary>
public sealed record PostRunContext(
    bool Died,
    string? CauseOfDeath,    // raw engine string, e.g. "spike_trap", "orc_brute", null if survived
    string? KillerSpecies,   // entity species if monster kill, null otherwise
    int FloorReached,        // deepest floor the player visited this run
    int RunNumber,           // run counter (total runs to date, post-increment)
    // EndingType.None for ordinary deaths mid-descent (the Weighing never resolved this run).
    // CleanAudit / Theft / Swap when the run ended in a Weighing win.
    // LossGuardians / LossDebt / LossRefused when the Weighing resolved a loss.
    EndingType Ending = EndingType.None
);
