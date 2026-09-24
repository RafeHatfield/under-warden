namespace UnderWarden.Logic.Endgame;

/// <summary>
/// The Weighing's outcome taxonomy. See tasks/plans/plan_end_game.md.
///
/// Closed set of six endings (three wins, three losses) per decision 8, plus None
/// for "the Weighing has not resolved." The win path is net-new to dungeon mode —
/// dungeon mode had no victory state before the Weighing (PlayerWon is scenario-only).
/// </summary>
public enum EndingType
{
    /// <summary>The Weighing has not resolved (default / in dungeon descent).</summary>
    None = 0,

    // --- Wins ---
    /// <summary>Survived the Weighing with a clean record. The lawful exit; debts paid.</summary>
    CleanAudit,
    /// <summary>Survived the Weighing with a heavy record. Takes the soul by force; the defiant ending.</summary>
    Theft,
    /// <summary>Hidden ending: offered himself instead of being weighed. Gated on the Hael catalog. The true terminus.</summary>
    Swap,

    // --- Losses ---
    /// <summary>Died during the gauntlet, before reaching the Debt. The judgment killed him.</summary>
    LossGuardians,
    /// <summary>Survived the Guardians, reached the Debt, the Debt took him. The closest failure.</summary>
    LossDebt,
    /// <summary>Chose to decline the weighing — turned back, left with the debt open. The only non-death loss.</summary>
    LossRefused,
}

/// <summary>
/// The five Guardians of the Weighing. The first four are faction-themed and scale on a
/// record metric (see <see cref="AuditScorer"/>). The Debt does not scale and cannot be allied.
/// </summary>
public enum GuardianId
{
    /// <summary>Possession / Hall Wardens. Scales on hall_warden_possessions_total + memo tone.</summary>
    WardenOfWardens = 0,
    /// <summary>Bonding / the Unshriven. Scales on orc reputation + unprovoked orc kills.</summary>
    Oathkeeper,
    /// <summary>Death / the past-selves. Scales on CumulativeDeaths + past-Sasha catalog size.</summary>
    AssemblyOfTheLost,
    /// <summary>Purity / restraint. Scales on unprovoked cross-faction kills (the excess metric).</summary>
    AuditorsOwn,
    /// <summary>Anik's soul. Does not scale, cannot be allied, faced alone. The universal constant.</summary>
    Debt,
}

/// <summary>
/// A faction Guardian's disposition for the run, decided by the audit. Better record → more
/// favourable tier. Allied Guardians fight beside Sasha; Savage Guardians are the wall.
/// The Debt has no tier — it is always full strength.
/// </summary>
public enum GuardianTier
{
    /// <summary>Strongest record: the Guardian fights beside Sasha.</summary>
    Allied = 0,
    /// <summary>Good record: a weakened manifestation.</summary>
    Diminished,
    /// <summary>Middling record: full-strength but neutral.</summary>
    Neutral,
    /// <summary>Worst record: the savage form.</summary>
    Savage,
}

/// <summary>
/// How the Weighing concluded for the player, driving <see cref="AuditScorer.DetermineEnding"/>.
/// </summary>
public enum WeighingOutcome
{
    /// <summary>The Weighing is still running.</summary>
    InProgress = 0,
    /// <summary>Player survived the full gauntlet including the Debt.</summary>
    Survived,
    /// <summary>Player died during the faction-Guardian phase, before the Debt.</summary>
    DiedToGuardians,
    /// <summary>Player reached the Debt and died to it.</summary>
    DiedToDebt,
    /// <summary>Player declined the weighing at the audit (a chosen, non-death loss).</summary>
    Refused,
}

/// <summary>
/// A headless decision for the Debt choice gate. Set on <see cref="Core.GameState"/> by the harness
/// (or a test) so the Weighing can resolve the Force / Self / Refuse choice without UI input — the
/// enabler for the balance pass. Null in production: the presentation layer drives the gate instead.
/// </summary>
public enum WeighingGateDecision
{
    /// <summary>Take Anik by force — spawn the Debt as a combatant and fight it.</summary>
    Force,
    /// <summary>Offer himself (the Swap) — valid only when the catalog gate is open.</summary>
    Swap,
    /// <summary>Decline the weighing — the chosen non-death loss.</summary>
    Refuse,
}

/// <summary>
/// Constants for the Weighing. Tunable knobs live here so they have one canonical home.
/// </summary>
public static class WeighingConstants
{
    /// <summary>The descent ends here. Reaching this depth triggers the Weighing instead of a normal floor.</summary>
    public const int FinalFloorDepth = 25;

    /// <summary>True if the given depth is the Weighing floor — build the arena, not a procedural floor.</summary>
    public static bool IsWeighingFloor(int depth) => depth == FinalFloorDepth;

    // Loss cause-codes — the closed set per decision 8. PlayerDeathCause is a free string consumed
    // by the memo evaluator and recorded into past_sashas; these extend it for Weighing losses.
    public const string LossGuardiansCause = "weighing_loss_guardians";
    public const string LossDebtCause = "weighing_loss_debt";
    public const string LossRefusedCause = "weighing_loss_refused";
}
