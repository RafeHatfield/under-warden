using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// In-place corpse transformation: raises a FRESH corpse into a living undead monster.
///
/// Stat formula (PoC-verified):
///   HP       = BaseHp × 2.0           (rounded to int)
///   DamageMin = max(1, BaseMin × 0.5)
///   DamageMax = max(1, BaseMax × 0.5)
///   Strength  = max(6, BaseStr × 0.75)
///   Dexterity = max(6, BaseDex × 0.5)
///   Constitution = min(18, BaseCon × 1.5)
///   Defense   = unchanged
///   Accuracy  = unchanged
///   Evasion   = unchanged
///
/// Faction rules:
///   Player-raised  → "neutral" (attacks both sides)
///   AI-raised      → raiser's faction
///
/// The entity is transformed in-place (same ID). It stays in state.Monsters but
/// is removed from state.Corpses. CorpseComponent is removed; RaisedFromCorpseTag is added.
/// </summary>
public static class RaiseDeadResolver
{
    /// <summary>
    /// Raise a corpse entity in-place. Assumes the corpse is already validated (CanBeRaised == true,
    /// tile not occupied by a blocking entity, within range). Caller is responsible for emitting
    /// RaiseDeadEvent and setting necromancer cooldown.
    /// </summary>
    public static void Raise(Entity corpse, string casterFaction, GameState state)
    {
        var corpseComp = corpse.Require<CorpseComponent>();
        string corpseId = corpseComp.CorpseId;

        // Apply raise-dead stat formula to snapshotted base stats
        int raisedHp       = (int)Math.Round(corpseComp.BaseHp * 2.0);
        int raisedDmgMin   = Math.Max(1, (int)Math.Round(corpseComp.BaseDamageMin * 0.5));
        int raisedDmgMax   = Math.Max(1, (int)Math.Round(corpseComp.BaseDamageMax * 0.5));
        int raisedStr      = Math.Max(6, (int)Math.Round(corpseComp.BaseStrength * 0.75));
        int raisedDex      = Math.Max(6, (int)Math.Round(corpseComp.BaseDexterity * 0.5));
        int raisedCon      = Math.Min(18, (int)Math.Round(corpseComp.BaseConstitution * 1.5));
        int raisedDef      = corpseComp.BaseDefense;    // unchanged
        int raisedAccuracy = corpseComp.BaseAccuracy;   // unchanged
        int raisedEvasion  = corpseComp.BaseEvasion;    // unchanged

        // Determine faction: player raises neutral; AI raises with raiser's faction
        string faction = string.Equals(casterFaction, "player", StringComparison.OrdinalIgnoreCase)
            ? "neutral"
            : casterFaction;

        // Remove corpse component and raised-tag from previous cycle (if any)
        corpse.Remove<CorpseComponent>();
        corpse.Remove<RaisedFromCorpseTag>();

        // Attach living-monster components
        corpse.Add(new Fighter(
            hp: raisedHp,
            defense: raisedDef,
            damageMin: raisedDmgMin,
            damageMax: raisedDmgMax,
            strength: raisedStr,
            dexterity: raisedDex,
            constitution: raisedCon,
            accuracy: raisedAccuracy,
            evasion: raisedEvasion));

        corpse.Add(new AiComponent
        {
            AiType = "basic",
            Faction = faction,
        });

        corpse.Add(new RaisedFromCorpseTag { CorpseId = corpseId });
        corpse.BlocksMovement = true;

        // Remove from corpses list — entity stays in Monsters (dual membership)
        state.Corpses.Remove(corpse);
    }

    /// <summary>
    /// Euclidean distance between a caster entity and a target tile.
    /// Used by spell resolver and necromancer AI for range checks.
    /// </summary>
    public static double DistanceTo(Entity caster, int targetX, int targetY)
        => Math.Sqrt(Math.Pow(caster.X - targetX, 2) + Math.Pow(caster.Y - targetY, 2));

    /// <summary>
    /// Find the nearest raisable corpse within range, excluding tiles occupied by a blocking entity.
    /// Tie-breaking: nearest first, then (y, x) ascending for determinism (matches PoC).
    /// </summary>
    public static Entity? FindNearestRaisableCorpse(
        Entity actor, IEnumerable<Entity> corpses, GameState state, double range)
    {
        return corpses
            .Where(c => c.Get<CorpseComponent>()?.CanBeRaised == true)
            .Where(c => DistanceTo(actor, c.X, c.Y) <= range)
            .Where(c => state.Map.CanMoveTo(c.X, c.Y)) // corpse tile not blocked by a living entity
            .OrderBy(c => DistanceTo(actor, c.X, c.Y))
            .ThenBy(c => c.Y)
            .ThenBy(c => c.X)
            .FirstOrDefault();
    }
}
