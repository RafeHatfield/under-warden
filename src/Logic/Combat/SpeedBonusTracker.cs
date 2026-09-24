using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Tracks momentum for bonus attacks. Each attack increments a counter.
/// Chance of bonus = counter * speed_bonus_ratio.
///
/// At chance >= 1.0: guaranteed bonus, counter resets.
/// Below 1.0: RNG roll. Early success gives the bonus but does NOT reset
/// the counter — momentum cascades.
///
/// Only the faster entity (relative speed) builds momentum.
/// Counter resets on: guaranteed bonus, non-attack action, or switching targets.
/// Preserving momentum across target switches is a potential boon (not default).
/// </summary>
public sealed class SpeedBonusTracker : IComponent
{
    public Entity? Owner { get; set; }

    private int _attackCounter;
    private int _lastTargetId = -1;

    /// <summary>Base speed ratio (from entity definition, e.g. monster speed_bonus).</summary>
    public double BaseRatio { get; set; }

    /// <summary>Additive bonus from equipment (stacks).</summary>
    public double EquipmentRatio { get; set; }

    /// <summary>Additive bonus from equipped rings (stacks across both ring slots).</summary>
    public double RingRatio { get; set; }

    /// <summary>Effective speed bonus ratio: base + weapon equipment + rings.</summary>
    public double SpeedBonusRatio => BaseRatio + EquipmentRatio + RingRatio;

    /// <summary>Current attack counter (for diagnostics).</summary>
    public int AttackCounter => _attackCounter;

    /// <summary>Id of the last target this attacker hit (-1 = none). Read-only accessor for the
    /// mid-run serializer; drives momentum reset, so it must survive save/load for determinism.</summary>
    public int LastTargetId => _lastTargetId;

    public SpeedBonusTracker(double baseRatio = 0.0)
    {
        BaseRatio = baseRatio;
    }

    /// <summary>Restore the private momentum counters after a mid-run load. Serializer-only —
    /// gameplay mutates these solely through RollForBonusAttack.</summary>
    public void RestoreMomentum(int attackCounter, int lastTargetId)
    {
        _attackCounter = attackCounter;
        _lastTargetId = lastTargetId;
    }

    /// <summary>
    /// Roll for a bonus attack after a successful hit.
    /// Resets momentum if target changed since last attack.
    /// Call this after each attack lands. Returns true if a bonus attack triggers.
    /// </summary>
    public bool RollForBonusAttack(SeededRandom rng, Entity? target = null)
    {
        // Target switch breaks momentum
        if (target != null)
        {
            int targetId = target.Id;
            if (_lastTargetId >= 0 && targetId != _lastTargetId)
                _attackCounter = 0;
            _lastTargetId = targetId;
        }
        double ratio = SpeedBonusRatio;
        if (ratio <= 0) return false;

        _attackCounter++;
        double chance = _attackCounter * ratio;

        if (chance >= 1.0)
        {
            // Guaranteed bonus — reset counter
            _attackCounter = 0;
            return true;
        }

        // RNG roll — early bonus does NOT reset counter (momentum cascades)
        double roll = rng.NextDouble();
        return roll < chance;
    }

    /// <summary>
    /// Reset momentum. Called when the entity takes a non-attack action
    /// (moves, uses item, etc.).
    /// </summary>
    public void ResetMomentum()
    {
        _attackCounter = 0;
        _lastTargetId = -1;
    }

    /// <summary>
    /// Check if this entity is faster than a defender.
    /// Only the faster entity builds momentum.
    /// </summary>
    public static bool CanBuildMomentum(Entity attacker, Entity defender)
    {
        double atkSpeed = attacker.Get<SpeedBonusTracker>()?.SpeedBonusRatio ?? 0;
        double defSpeed = defender.Get<SpeedBonusTracker>()?.SpeedBonusRatio ?? 0;
        return atkSpeed > defSpeed;
    }
}
