using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Entity is asleep — skips all turns until the effect expires or is broken.
/// Applied by: Dragon Fart secondary effect.
/// PoC values: RemainingTurns=3.
///
/// Wake rule: SleepEffect is removed when the entity takes ATTACK damage (not DOT).
/// A poisoned sleeping entity continues taking DOT damage each turn but stays asleep.
/// This is verified PoC behavior.
///
/// Wake is handled by StatusEffectProcessor.OnDamageTaken (called from CombatResolver
/// after applying attack damage, NOT from the DOT path).
/// </summary>
public sealed class SleepEffect : IStatusEffect
{
    public Entity? Owner { get; set; }
    public string EffectName => "sleep";
    public int RemainingTurns { get; set; } = 3;
    public bool IsPermanent => false;
}
