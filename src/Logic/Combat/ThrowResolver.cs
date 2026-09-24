using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Resolves a throw action. Single source of truth for all throw logic.
/// Three resolution paths (potion, weapon, junk) are handled here.
/// No accuracy roll — if a monster is on the final tile it's hit; otherwise miss.
///
/// PoC reference: ~/development/rlike/throwing.py
/// </summary>
public static class ThrowResolver
{
    /// <summary>Maximum throw range in tiles. PoC: fixed at 10, may be STR-gated in future.</summary>
    public const int MaxRange = 10;

    // ─── Public entry point ───────────────────────────────────────────────────

    /// <summary>
    /// Resolve a throw action. Returns all TurnEvents produced.
    ///
    /// The three paths:
    ///   Potion (SpellEffect with ThrowSpellId): shatter on impact. Hit = apply throw spell.
    ///     Miss = consumed with no effect. Never lands on ground.
    ///   Weapon (Equippable with IsWeapon): roll weapon dice - 2 (min 1). Hit = deal damage.
    ///     Either way: removed from inventory, placed as floor item at final path tile.
    ///   Junk (everything else): removed from inventory, placed as floor item. No damage.
    ///
    /// Auto-unequip: if the thrown item is currently equipped, it is unequipped first.
    /// Invisibility: throwing always breaks invisibility (offensive action).
    /// Momentum: reset after throw.
    /// </summary>
    public static List<TurnEvent> Resolve(
        Entity thrower, Entity item, int targetX, int targetY, GameState state)
    {
        var events = new List<TurnEvent>();

        // ── Break invisibility ────────────────────────────────────────────────
        // Throwing is an offensive action — it always reveals the thrower.
        if (thrower.Has<InvisibilityEffect>())
        {
            thrower.Remove<InvisibilityEffect>();
            events.Add(new StatusExpiredEvent
            {
                ActorId = thrower.Id,
                EntityId = thrower.Id,
                EffectName = "invisibility",
                Reason = "threw_item",
            });
        }

        // ── Auto-unequip if item is currently equipped ────────────────────────
        var equipment = thrower.Get<Equipment>();
        if (equipment != null)
        {
            var equippable = item.Get<Equippable>();
            if (equippable != null)
            {
                var slot = equippable.Slot;
                if (equipment.GetSlot(slot)?.Id == item.Id)
                {
                    equipment.SetSlot(slot, null);
                    events.Add(new UnequipEvent
                    {
                        ActorId  = thrower.Id,
                        ItemId   = item.Id,
                        ItemName = item.Name,
                        Slot     = slot,
                    });
                }
            }
        }

        // ── Trace projectile path ─────────────────────────────────────────────
        var path = CalculatePath(thrower.X, thrower.Y, targetX, targetY, state.Map, MaxRange);

        // Final tile: last element of path, or thrower position if path is empty (shouldn't happen)
        int landX = path.Count > 0 ? path[^1].X : thrower.X;
        int landY = path.Count > 0 ? path[^1].Y : thrower.Y;

        // Check for a living monster at the final tile
        var targetMonster = FindAliveMonsterAt(state, landX, landY);
        bool hit = targetMonster != null;

        // ── Dispatch to the appropriate resolution path ───────────────────────
        var spell = item.Get<SpellEffect>();
        var equip = item.Get<Equippable>();

        if (spell?.ThrowSpellId != null)
        {
            ResolvePotion(thrower, item, spell, targetMonster, landX, landY, hit, state, events);
        }
        else if (equip != null && equip.IsWeapon)
        {
            ResolveWeapon(thrower, item, equip, targetMonster, targetX, targetY, landX, landY, hit, state, events);
        }
        else
        {
            ResolveJunk(thrower, item, targetX, targetY, landX, landY, state, events);
        }

        // ── Reset momentum ────────────────────────────────────────────────────
        thrower.Get<SpeedBonusTracker>()?.ResetMomentum();

        return events;
    }

    // ─── Resolution paths ─────────────────────────────────────────────────────

    /// <summary>
    /// Potion path. Potion is always consumed regardless of hit/miss.
    /// On hit: delegate to SpellResolver with the throw spell ID.
    /// On miss: shatter on ground, no effect.
    /// </summary>
    private static void ResolvePotion(
        Entity thrower, Entity item, SpellEffect spell,
        Entity? targetMonster, int landX, int landY, bool hit,
        GameState state, List<TurnEvent> events)
    {
        // Consume the potion stack
        var consumable = item.Get<Consumable>();
        var inventory = state.PlayerInventory;
        if (consumable != null && inventory != null)
        {
            consumable.StackSize--;
            if (consumable.StackSize <= 0)
                inventory.Remove(item);
        }

        if (hit && targetMonster != null)
        {
            // Apply throw spell effect to the target monster
            var spellEvents = SpellResolver.Resolve(
                thrower,
                spell,
                state,
                targetEntityId: targetMonster.Id,
                overrideSpellId: spell.ThrowSpellId);

            events.AddRange(spellEvents);
        }

        events.Add(new ThrowEvent
        {
            ActorId           = thrower.Id,
            ActorX            = thrower.X,
            ActorY            = thrower.Y,
            ItemId            = item.Id,
            ItemName          = item.Name,
            TargetX           = landX,
            TargetY           = landY,
            LandX             = landX,
            LandY             = landY,
            Hit               = hit,
            Damage            = 0,
            TargetKilled      = false, // SpellResolver handles death separately via DeathEvent
            TargetEntityId    = hit ? targetMonster!.Id : null,
            ItemLandsOnGround = false, // potions are consumed on throw
            ResultType        = ThrowResultType.PotionShatter,
        });
    }

    /// <summary>
    /// Weapon path. Deal weapon dice - 2 (min 1) damage on hit.
    /// Weapon is always removed from inventory and placed on the ground.
    /// </summary>
    private static void ResolveWeapon(
        Entity thrower, Entity item, Equippable equip,
        Entity? targetMonster, int targetX, int targetY, int landX, int landY, bool hit,
        GameState state, List<TurnEvent> events)
    {
        int damage = 0;
        bool killed = false;

        if (hit && targetMonster != null)
        {
            // PoC: weapon throw damage = weapon dice roll - 2, minimum 1.
            // No STR modifier, no accuracy roll, no crit.
            int rolled = equip.RollDamage(state.Rng);
            damage = Math.Max(1, rolled - 2);

            var targetFighter = targetMonster.Get<Fighter>();
            if (targetFighter != null)
            {
                targetFighter.TakeDamage(damage);
                killed = !targetFighter.IsAlive;

                // Wake sleeping targets on throw damage (same rule as melee damage)
                StatusEffectProcessor.OnDamageTaken(targetMonster, events);
            }

            if (killed)
            {
                // Emit DeathEvent. Loot drop and corpse transform are NOT done here —
                // matching the SpellResolver pattern where spell kills emit DeathEvent only.
                // TurnController.UpdateKnowledge reads DeathEvent for knowledge tracking.
                events.Add(new DeathEvent { ActorId = targetMonster.Id, KillerId = thrower.Id });
            }
        }

        // Weapon always lands on the ground at the final path tile, regardless of hit/miss
        PlaceOnGround(state, item, landX, landY);

        events.Add(new ThrowEvent
        {
            ActorId           = thrower.Id,
            ActorX            = thrower.X,
            ActorY            = thrower.Y,
            ItemId            = item.Id,
            ItemName          = item.Name,
            TargetX           = targetX,
            TargetY           = targetY,
            LandX             = landX,
            LandY             = landY,
            Hit               = hit,
            Damage            = damage,
            TargetKilled      = killed,
            TargetEntityId    = hit ? targetMonster!.Id : null,
            ItemLandsOnGround = true,
            ResultType        = hit ? ThrowResultType.WeaponHit : ThrowResultType.WeaponMiss,
        });
    }

    /// <summary>
    /// Junk path. Rings, scrolls, armor, wands — removed from inventory and placed on ground.
    /// No damage, no effect. Item is retrievable.
    /// </summary>
    private static void ResolveJunk(
        Entity thrower, Entity item,
        int targetX, int targetY, int landX, int landY,
        GameState state, List<TurnEvent> events)
    {
        PlaceOnGround(state, item, landX, landY);

        events.Add(new ThrowEvent
        {
            ActorId           = thrower.Id,
            ActorX            = thrower.X,
            ActorY            = thrower.Y,
            ItemId            = item.Id,
            ItemName          = item.Name,
            TargetX           = targetX,
            TargetY           = targetY,
            LandX             = landX,
            LandY             = landY,
            Hit               = false,
            Damage            = 0,
            TargetKilled      = false,
            TargetEntityId    = null,
            ItemLandsOnGround = true,
            ResultType        = ThrowResultType.JunkLand,
        });
    }

    // ─── Utilities ────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculate the Bresenham line from (x0,y0) toward (x1,y1), stopping at walls
    /// or at MaxRange tiles. The starting tile is excluded from the path.
    /// Matches PoC tcod.los.bresenham behavior for cardinal and diagonal throws.
    ///
    /// Returns a list of (X, Y) tiles the projectile passes through (in order).
    /// The last element is where the item lands.
    /// </summary>
    public static List<(int X, int Y)> CalculatePath(
        int x0, int y0, int x1, int y1, GameMap map, int maxRange)
    {
        var path = new List<(int X, int Y)>();

        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;

        int err = dx - dy;
        int cx = x0;
        int cy = y0;

        for (int step = 0; step < maxRange; step++)
        {
            // Advance one step along the line
            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                cx += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                cy += sy;
            }

            // Bounds check — clamp to map edge
            if (!map.InBounds(cx, cy))
                break;

            path.Add((cx, cy));

            // Check if we've reached the target
            if (cx == x1 && cy == y1)
                break;

            // Stop if this tile is a wall — item cannot pass through walls
            if (!map.IsWalkable(cx, cy))
                break;
        }

        return path;
    }

    /// <summary>Find a living monster at the given map tile. Returns null if none.</summary>
    private static Entity? FindAliveMonsterAt(GameState state, int x, int y)
    {
        foreach (var monster in state.AliveMonsters)
        {
            if (monster.X == x && monster.Y == y)
                return monster;
        }
        return null;
    }

    /// <summary>
    /// Remove item from player inventory and place it on the floor at (x, y).
    /// Same pattern as TurnController.ResolveDrop, but targets an arbitrary position
    /// rather than always the player's position.
    /// </summary>
    private static void PlaceOnGround(GameState state, Entity item, int x, int y)
    {
        // Remove from inventory (if present — auto-unequip already removed from Equipment,
        // but inventory removal is a separate step)
        state.PlayerInventory?.Remove(item);

        item.X = x;
        item.Y = y;

        state.FloorItems.Add(item);
        state.Map.RegisterEntity(item);
    }

}
