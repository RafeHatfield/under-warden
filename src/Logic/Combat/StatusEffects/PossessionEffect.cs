using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

public enum PossessionSource { PlayerInitiated, WardenInitiated }

/// <summary>
/// Applied to the HOST body (the one being possessed, not the originating entity).
/// The same type is used regardless of source — player Hollowmark-channel, Under-Warden
/// bureaucratic possession, or any future system.
///
/// PossessionEffect is removed in exactly four ways (no other code path may do so):
///   1. Voluntary exit   — PossessionSystem.ExitVoluntary
///   2. Visibility break — PossessionSystem.CheckVisibilityConstraint
///   3. Host death       — PossessionSystem.OnPossessionInducedHostDeath
///   4. Spell-break      — PossessionSystem.OnPossessionDispelled
///
/// Duration ticks are effectively suppressed: RemainingTurns defaults to int.MaxValue
/// and IsPermanent=false so the normal StatusEffectProcessor.ProcessTurnEnd path never
/// fires a duration-based removal. Designer-authored duration-capped possessions set a
/// lower RemainingTurns at apply time.
/// </summary>
public sealed class PossessionEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "possessed";
    public int RemainingTurns { get; set; } = int.MaxValue;
    public bool IsPermanent => false;

    /// <summary>The soul currently wearing this body. Player possession: state.Player.Id. Warden: -2.</summary>
    public int PossessorEntityId { get; set; }

    /// <summary>The home body the possessor came from. Player possession: state.Player.Id. Warden: null.</summary>
    public int? OriginatorBodyId { get; set; }

    /// <summary>HP/turn drained from the home body while this effect is active. 0 for Warden-initiated.</summary>
    public int DrainPerTurn { get; set; }

    public PossessionSource Source { get; set; }

    /// <summary>state.TurnCount when possession began — used for knowledge-unlock threshold.</summary>
    public int EnteredTurn { get; set; }

    /// <summary>
    /// Phantom wand position (OQ-2 resolution: no transient entity, position stored on effect).
    /// Defaults to the home body's tile; updated by kick-the-wand monster AI.
    /// -1 means uninitialised (not yet used for Hollowmark ability suppression).
    /// </summary>
    public int WandTileX { get; set; } = -1;
    public int WandTileY { get; set; } = -1;

    /// <summary>
    /// True after the 25%-drain voice warning fires (home body ≤75% MaxHp).
    /// Prevents duplicate warnings within the same possession session.
    /// </summary>
    public bool DrainWarning25Fired { get; set; }

    /// <summary>
    /// True after the 50%-drain voice warning fires (home body ≤50% MaxHp).
    /// Prevents duplicate warnings within the same possession session.
    /// </summary>
    public bool DrainWarning50Fired { get; set; }

    /// <summary>
    /// True after the near-death warning event fires (home body ≤25% MaxHp = 75% drained).
    /// Prevents duplicate warnings within the same possession session.
    /// </summary>
    public bool NearDeathWarningFired { get; set; }

    /// <summary>
    /// True after the home-body-threatened voice fires for this possession.
    /// Caps at one fire per possession so the alarm doesn't repeat every hit turn.
    /// </summary>
    public bool HomeBodyThreatenedFired { get; set; }
}
