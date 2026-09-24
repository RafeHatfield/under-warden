using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is enraged — attacks anyone nearby with doubled damage but halved accuracy.
/// Applied by: Rage Scroll / Wand of Rage.
/// Duration default: 8 turns.
///
/// Design note: Enraged makes the target dangerous to everyone including allies.
/// Player can weaponize this against monster groups.
///
/// HostileToAll is set to true on apply and cleared on remove. The faction system
/// is not yet implemented — this is a shim that causes the monster to attack the
/// nearest entity regardless of allegiance. Replace with proper faction switching
/// when plan_faction_system lands.
/// </summary>
public sealed class EnragedEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "enraged";
    public int RemainingTurns { get; set; } = 8;
    public bool IsPermanent => false;

    /// <summary>Multiplier on damage output. Default 2.0x (doubled).</summary>
    public double DamageMultiplier { get; set; } = 2.0;

    /// <summary>Multiplier on accuracy. Default 0.5x (halved).</summary>
    public double AccuracyMultiplier { get; set; } = 0.5;

    /// <summary>
    /// When true, the entity attacks the nearest entity regardless of allegiance.
    /// Set on apply, cleared on remove. Faction system shim.
    /// </summary>
    public bool HostileToAll { get; set; } = true;
}
