using UnderWarden.Logic.Balance.Transcript;
using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.Balance.LlmPlayer;

/// <summary>One turn's decision plus LLM metadata the transcript captures.</summary>
public sealed record PlayerDecision(
    PlayerAction Action,
    string? Reasoning,
    StructuralAssessment? Assessment,
    IReadOnlyList<StructuralAssessment> ExtraJudgments,
    bool UsedFallback = false,
    string? FallbackReason = null);

public interface IPlayerBrain
{
    /// <summary>Decide the turn. Must never throw — internal fallback on any failure.</summary>
    PlayerDecision Decide(GameState state);

    /// <summary>Called by the harness after ProcessTurn with the turn's resolved events.
    /// Feeds RECENT EVENTS and Phase-4 significant-event hook detection.</summary>
    void OnTurnResolved(int turn, IReadOnlyList<TurnEvent> events, GameState state);

    /// <summary>Called at the start of each floor (after Build, before the first Decide).
    /// For depth > first floor, the brain queues a FLOOR COMPLETE prompt for the next Decide.</summary>
    void OnFloorEnter(int depth);

    /// <summary>Called once after the run ends, BEFORE TranscriptRecorder.Finish.
    /// Synchronous (own internal timeout). Returns the end-of-run narrative (null on failure).</summary>
    string? OnRunEnd(string endingLabel);

    /// <summary>Inject a prompt block that will prepend to the next Decide call's context.
    /// Used by the harness stuck-detection path to warn the brain when the player hasn't
    /// moved in several turns. If a block is already pending (from a hook), appends with
    /// a blank line so both are delivered together. Always forces an API call that turn.</summary>
    void InjectPromptBlock(string block);
}
