using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is crippled — suffers a penalty to both attack accuracy and armor class.
/// Applied by: Orc Shaman Crippling Hex ability.
/// PoC values: ToHitPenalty=1, AcPenalty=1, RemainingTurns=5.
/// Penalties applied in CombatResolver: attacker suffers ToHitPenalty, defender suffers AcPenalty.
/// </summary>
public sealed class CrippledEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "crippled";
    public int RemainingTurns { get; set; } = 5;
    public bool IsPermanent => false;

    /// <summary>Subtracted from the crippled entity's attack roll. PoC value: 1.</summary>
    public int ToHitPenalty { get; set; } = 1;

    /// <summary>Subtracted from the crippled entity's armor class when defending. PoC value: 1.</summary>
    public int AcPenalty { get; set; } = 1;
}
