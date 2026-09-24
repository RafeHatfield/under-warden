using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Carries player state forward across dungeon floors.
///
/// When descending, the player gets a new entity (new floor, position reset to spawn)
/// but preserves combat-relevant stats: current HP, equipment, and inventory.
///
/// Intentionally NOT carried: SpeedBonusTracker momentum (reset between floors),
/// position (set by DungeonFloorBuilder to the new spawn point), and entity ID
/// (player is always ID 0 on every floor).
/// </summary>
public static class PlayerCarryForward
{
    /// <summary>
    /// Create a new player entity for a new floor, copying survivable state from the previous floor.
    ///
    /// The new entity always gets ID 0 (player is always entity 0).
    /// Position is set to (0,0) — DungeonFloorBuilder overrides this to the floor's player spawn.
    ///
    /// Copies: Fighter (with current HP, not MaxHp), Equipment, Inventory.
    /// Does NOT copy: SpeedBonusTracker (momentum resets between floors).
    /// </summary>
    public static Entity Apply(Entity existingPlayer)
    {
        var newPlayer = new Entity(0, "Player", 0, 0, blocksMovement: true);

        // Copy Fighter — preserve current HP so wounds carry between floors.
        // We construct a new Fighter with the same base stats, then set Hp to the actual current value.
        var oldFighter = existingPlayer.Require<Fighter>();
        var newFighter = new Fighter(
            hp: oldFighter.BaseMaxHp,
            defense: oldFighter.BaseDefense,
            power: oldFighter.BasePower,
            xp: oldFighter.Xp,
            damageMin: oldFighter.DamageMin,
            damageMax: oldFighter.DamageMax,
            strength: oldFighter.Strength,
            dexterity: oldFighter.Dexterity,
            constitution: oldFighter.Constitution,
            accuracy: oldFighter.Accuracy,
            evasion: oldFighter.Evasion);

        // Preserve current HP — the player carries wounds between floors
        newFighter.Hp = oldFighter.Hp;

        // Preserve boon max HP bonus — not a constructor param, must be copied explicitly.
        // Same pattern as RingMaxHpBonus (which is restored by ReapplyRingEffects instead).
        newFighter.BoonMaxHpBonus = oldFighter.BoonMaxHpBonus;
        // Potion cooldown persists across floors — can't spam potions at the stair transition.
        newFighter.PotionCooldownRemaining = oldFighter.PotionCooldownRemaining;
        newFighter.CanOpenDoors = true; // player always opens doors

        newPlayer.Add(newFighter);

        // Copy Equipment if present (equipped items persist between floors)
        var oldEquipment = existingPlayer.Get<Equipment>();
        if (oldEquipment != null)
        {
            var newEquipment = newPlayer.Add(new Equipment());
            newEquipment.MainHand  = oldEquipment.MainHand;
            newEquipment.OffHand   = oldEquipment.OffHand;
            newEquipment.Head      = oldEquipment.Head;
            newEquipment.Chest     = oldEquipment.Chest;
            newEquipment.Feet      = oldEquipment.Feet;
            newEquipment.LeftRing  = oldEquipment.LeftRing;
            newEquipment.RightRing = oldEquipment.RightRing;
            newEquipment.Neck      = oldEquipment.Neck;
        }

        // Copy Inventory if present (items in bag persist between floors)
        var oldInventory = existingPlayer.Get<Inventory>();
        if (oldInventory != null)
        {
            var newInventory = newPlayer.Add(new Inventory());
            foreach (var item in oldInventory.Items)
                newInventory.Add(item);
        }

        // Carry the run-scoped aggression tally (TASK-003) — unprovoked kills accumulate across
        // floors within a run. A new run gets a fresh default player (and thus a fresh tally).
        var oldTally = existingPlayer.Get<RunAggressionTally>();
        if (oldTally != null)
        {
            var newTally = newPlayer.Add(new RunAggressionTally());
            foreach (var (faction, count) in oldTally.UnprovokedKillsByFaction)
                newTally.UnprovokedKillsByFaction[faction] = count;
        }

        return newPlayer;
    }
}
