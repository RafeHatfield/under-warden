namespace UnderWarden.Logic.ECS;

/// <summary>
/// Necromancer-specific AI state. Attached by MonsterFactory when ai_type is
/// "necromancer" or "plague_necromancer".
///
/// Decision priority (per NecromancerAI.Decide):
///   1. Raise: if cooldown == 0 and FRESH corpse within RaiseRange → raise it
///   2. Seek corpse: if raisable corpse exists but out of range → pathfind toward it
///   3. Retreat: if player within DangerRadius → flee
///   4. Preferred range: maintain PreferredDistanceMin–Max from player
///   5. Fallback: BasicMonsterAI (approach + attack)
///
/// All default values match the PoC necromancer_ai.py configuration.
/// </summary>
public sealed class NecromancerAiComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>Euclidean radius within which the necromancer can raise corpses. PoC: 5.</summary>
    public int RaiseRange { get; set; } = 5;

    /// <summary>Turns to wait after a successful raise before raising again. PoC: 4.</summary>
    public int RaiseCooldown { get; set; } = 4;

    /// <summary>Current cooldown turns remaining. 0 = ready to raise.</summary>
    public int CooldownRemaining { get; set; }

    /// <summary>Necromancer retreats if player is closer than this. PoC: 2.</summary>
    public int DangerRadius { get; set; } = 2;

    /// <summary>Preferred approach distance (minimum). PoC: 4.</summary>
    public int PreferredDistanceMin { get; set; } = 4;

    /// <summary>Preferred hang-back distance (maximum). PoC: 7.</summary>
    public int PreferredDistanceMax { get; set; } = 7;
}
