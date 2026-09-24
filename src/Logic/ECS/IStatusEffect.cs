namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marker interface for all status effect components.
/// Extends IComponent so effects are first-class ECS citizens.
///
/// All duration-based effects implement this interface so StatusEffectProcessor
/// can iterate them via entity.GetAllComponents().OfType&lt;IStatusEffect&gt;() without
/// maintaining a hardcoded list of known effect types.
/// </summary>
public interface IStatusEffect : IComponent
{
    /// <summary>Stable string identifier for this effect (used in TurnEvents and UI).</summary>
    string EffectName { get; }

    /// <summary>
    /// Turns remaining until this effect expires.
    /// Decremented each turn by StatusEffectProcessor.ProcessTurnEnd.
    /// Set to a large value (e.g. 1000) for effectively-permanent effects like TauntedEffect.
    /// </summary>
    int RemainingTurns { get; set; }

    /// <summary>
    /// When true, RemainingTurns is never decremented and the effect never expires via duration.
    /// Use for effects that only clear via explicit removal (e.g. AggravatedEffect).
    /// </summary>
    bool IsPermanent { get; }
}
