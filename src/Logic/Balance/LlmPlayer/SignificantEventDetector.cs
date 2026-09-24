using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.Balance.LlmPlayer;

/// <summary>Which significant event hook fired, and what prompt text to inject.</summary>
public sealed record HookFired(string HookName, string PromptBlock);

/// <summary>
/// Stateful detector for significant-event hooks. Tracks per-run state
/// (near-death arm/disarm, first-X flags) and emits HookFired when events cross thresholds.
/// Lives in the Logic layer so tests can reach it without the Harness project.
///
/// Detection rules (from plan-player.md §5):
/// - near_death: edge-triggered at hp_pct &lt; 0.2; re-arms at hp_pct &gt;= 0.3 (hysteresis prevents spam)
/// - first_orc_interaction: first AttackEvent (either direction) involving an orc entity; fires once
/// - mural_read: MuralExaminedEvent seen; Reader persona only; no state guard (fires per mural)
/// - first_possession: first PossessionEnteredEvent; fires once
///
/// Return order when multiple hooks fire in one turn:
/// near_death &gt; first_orc_interaction &gt; mural_read &gt; first_possession.
/// The caller typically takes the first and queues the rest.
/// </summary>
public sealed class SignificantEventDetector
{
    private readonly LlmPersona _persona;

    // Near-death state: starts armed so the first dip below 0.2 always fires.
    // Disarmed immediately after firing; re-armed when hp recovers to >= 0.3.
    // The 0.1 hysteresis band prevents prompt spam when the player hovers at low HP.
    private bool _nearDeathArmed = true;

    // One-shot flags: set to true permanently after the hook fires.
    private bool _firstOrcFired;
    private bool _firstPossessionFired;

    public SignificantEventDetector(LlmPersona persona)
    {
        _persona = persona;
    }

    /// <summary>
    /// Process one turn's resolved events and post-action HP state.
    /// Returns zero or more hooks that fired this turn, in priority order:
    /// near_death, first_orc_interaction, mural_read, first_possession.
    ///
    /// The caller appends the first hook's PromptBlock to the next turn's
    /// DescriberContext.PendingPromptBlock. Additional hooks in the same turn
    /// may be queued or discarded at the caller's discretion (v1: take first only).
    /// </summary>
    /// <param name="turn">The current turn number (informational; not used in detection logic).</param>
    /// <param name="events">All TurnEvents resolved this turn.</param>
    /// <param name="hpPctAfterTurn">Player HP as a fraction [0,1] AFTER the turn resolves.</param>
    /// <param name="isOrcEntity">
    /// Resolver called with an entity ID; returns true when that entity is an orc
    /// (SpeciesTag.TypeId starts with "orc", case-insensitive). Player ID (0) should
    /// always return false. The detector does not hold a reference to GameState — the
    /// caller provides this closure so the logic layer stays state-free.
    /// </param>
    public IReadOnlyList<HookFired> ProcessTurn(
        int turn,
        IReadOnlyList<TurnEvent> events,
        double hpPctAfterTurn,
        Func<int, bool> isOrcEntity)
    {
        var result = new List<HookFired>();

        // ── near_death ────────────────────────────────────────────────────────
        // Re-arm check BEFORE threshold check so re-arm and fire never happen in the same turn.
        if (!_nearDeathArmed && hpPctAfterTurn >= 0.3)
        {
            _nearDeathArmed = true;
        }

        if (_nearDeathArmed && hpPctAfterTurn < 0.2)
        {
            result.Add(new HookFired("near_death",
                """
                REFLECTION (near-death):
                Was this expected given the decisions you made in the last 5 turns,
                or did it arrive from a direction you had no way to anticipate?
                (expected | unexpected-but-navigable | arrived-without-decision-point)
                """));
            _nearDeathArmed = false;
        }

        // ── first_orc_interaction ─────────────────────────────────────────────
        if (!_firstOrcFired)
        {
            foreach (var e in events)
            {
                if (e is AttackEvent attack)
                {
                    bool actorIsOrc = isOrcEntity(attack.ActorId);
                    bool targetIsOrc = isOrcEntity(attack.TargetId);
                    if (actorIsOrc || targetIsOrc)
                    {
                        result.Add(new HookFired("first_orc_interaction",
                            """
                            REFLECTION (first orc interaction):
                            Was the decision surface here clear — did you understand what was at stake
                            and what your options were?
                            (clear | opaque | no-real-choice)
                            """));
                        _firstOrcFired = true;
                        break;
                    }
                }
            }
        }

        // ── mural_read (Reader persona only) ──────────────────────────────────
        // No state guard — murals are unique per floor, so it won't spam within a run.
        if (_persona == LlmPersona.Reader)
        {
            foreach (var e in events)
            {
                if (e is MuralExaminedEvent)
                {
                    result.Add(new HookFired("mural_read",
                        """
                        REFLECTION (mural):
                        Was the content of this mural decision-relevant — did it create or
                        foreclose an option you could act on? Or was it atmospheric only?
                        (decision-relevant | atmospheric | unclear-how-to-act)
                        """));
                    break; // At most one mural hook per turn even if multiple murals examined
                }
            }
        }

        // ── first_possession ─────────────────────────────────────────────────
        if (!_firstPossessionFired)
        {
            foreach (var e in events)
            {
                if (e is PossessionEnteredEvent)
                {
                    result.Add(new HookFired("first_possession",
                        """
                        REFLECTION (first possession):
                        Was the entry point into this mechanic clear, or did you discover it
                        by accident or from text rather than from mechanical affordance?
                        (clear-affordance | discovered-via-text | found-by-accident | system-unreachable-until-now)
                        """));
                    _firstPossessionFired = true;
                    break;
                }
            }
        }

        return result;
    }
}
