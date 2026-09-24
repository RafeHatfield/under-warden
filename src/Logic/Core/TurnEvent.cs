using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Core;

/// <summary>
/// A single discrete thing that happened during a turn. The Presentation layer
/// animates these in sequence. The balance pipeline derives metrics from them.
/// Events use entity IDs (not references) — safe to serialize and hold after state changes.
/// </summary>
public abstract class TurnEvent
{
    public int ActorId { get; init; }
}

public sealed class AttackEvent : TurnEvent
{
    public int TargetId { get; init; }
    public bool Hit { get; init; }
    public int Damage { get; init; }
    public bool IsCritical { get; init; }
    public bool IsFumble { get; init; }
    public bool TargetKilled { get; init; }
    public bool IsBonusAttack { get; init; }

    /// <summary>
    /// Non-null when the attack was blocked by a status effect rather than a die roll.
    /// Values: "disarmed" (DisarmedEffect active, weapon equipped), "" / null = normal miss.
    /// </summary>
    public string? FailReason { get; init; }

    /// <summary>Human-readable name of the actor and target. Populated at event creation so
    /// the transcript doesn't need a separate entity-ID lookup pass.</summary>
    public string ActorName { get; init; } = "";
    public string TargetName { get; init; } = "";
}

public sealed class MoveEvent : TurnEvent
{
    public int FromX { get; init; }
    public int FromY { get; init; }
    public int ToX { get; init; }
    public int ToY { get; init; }
}

public sealed class HealEvent : TurnEvent
{
    public int AmountHealed { get; init; }
    public int ItemId { get; init; }
    public string ItemName { get; init; } = "";
}

public sealed class WaitEvent : TurnEvent { }

/// <summary>
/// Emitted when a door is opened (by player or monster).
/// Presentation layer uses this to swap the door sprite from closed to open.
/// </summary>
public sealed class DoorOpenedEvent : TurnEvent
{
    public int X { get; init; }
    public int Y { get; init; }
    public int OpenedById { get; init; }
}

public sealed class DescendEvent : TurnEvent
{
    /// <summary>The new depth the player is descending to.</summary>
    public int NewDepth { get; init; }

    /// <summary>
    /// Why the descent happened. Default "player" for voluntary stair use.
    /// "hole_trap" when a hole_trap floor trap fires — presentation layer
    /// uses this to suppress the confirmation prompt.
    /// </summary>
    public string Cause { get; init; } = "player";
}

public sealed class DeathEvent : TurnEvent
{
    public int KillerId { get; init; }

    /// <summary>
    /// True when the death was triggered by the possession system (§8.2 host-died, §8.5 warden-dispelled).
    /// UpdateKnowledge skips RecordKilled for these deaths — the player held the body, not killed the species.
    /// </summary>
    public bool IsPossessionInduced { get; init; }

    /// <summary>Name of who died and who killed them (empty string for hazard kills where KillerId == -1).</summary>
    public string ActorName { get; init; } = "";
    public string KillerName { get; init; } = "";
}

public sealed class PickUpEvent : TurnEvent
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = "";
}

public sealed class DropEvent : TurnEvent
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = "";
}

public sealed class EquipEvent : TurnEvent
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = "";
    public EquipmentSlot Slot { get; init; }
    /// <summary>Item that was displaced from the slot and returned to inventory, if any.</summary>
    public int? DisplacedItemId { get; init; }
    public string? DisplacedItemName { get; init; }
}

public sealed class UnequipEvent : TurnEvent
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = "";
    public EquipmentSlot Slot { get; init; }
}

/// <summary>
/// A splitting monster has divided into children after HP dropped below its threshold.
/// Original entity is removed from the map; children are added to state.Monsters.
/// No XP is awarded — split is not a kill.
/// </summary>
public sealed class SplitEvent : TurnEvent
{
    /// <summary>ID of the monster that split.</summary>
    public int OriginalId { get; init; }

    /// <summary>IDs of the spawned child entities.</summary>
    public IReadOnlyList<int> ChildIds { get; init; } = Array.Empty<int>();
}

/// <summary>
/// A metal weapon was corroded by acid on a successful monster hit.
/// Presentation layer should show an orange toast with the degradation percentage.
/// Format: "The [MonsterName] corrodes your [WeaponName]! [X%]"
/// where X% = (NewDamageMax / BaseDamageMax) * 100.
/// </summary>
public sealed class CorrosionEvent : TurnEvent
{
    /// <summary>ID of the weapon entity that was degraded.</summary>
    public int WeaponId { get; init; }

    /// <summary>Name of the weapon (for toast display).</summary>
    public string WeaponName { get; init; } = "";

    /// <summary>The weapon's new DamageMax after corrosion.</summary>
    public int NewDamageMax { get; init; }

    /// <summary>The weapon's original DamageMax at creation (never changes).</summary>
    public int BaseDamageMax { get; init; }

    /// <summary>Name of the monster that caused the corrosion (for toast display).</summary>
    public string MonsterName { get; init; } = "";
}

/// <summary>
/// Emitted when the player casts a spell (via scroll or wand).
/// AoE spells list all affected entity IDs in AffectedIds. Single-target spells
/// set TargetId. The harness uses this event to measure spell usage frequency
/// and effectiveness across scenario runs.
/// </summary>
public sealed class SpellEvent : TurnEvent
{
    public string SpellId { get; init; } = "";
    public string SpellName { get; init; } = "";

    /// <summary>Entity ID of primary target (single-target spells). Null for Self/AoE.</summary>
    public int? TargetId { get; init; }

    /// <summary>Damage dealt to each target (for damage spells).</summary>
    public int Damage { get; init; }

    /// <summary>IDs of all entities affected by the spell (AoE includes all, single-target is one).</summary>
    public IReadOnlyList<int> AffectedIds { get; init; } = Array.Empty<int>();

    /// <summary>True if the spell succeeded (had a valid target, not wasted).</summary>
    public bool Success { get; init; }

    /// <summary>
    /// Name of the status effect applied by this spell (e.g., "confused", "slowed").
    /// Empty string if no status effect was applied.
    /// Presentation layer uses this for toast messages: "The orc is confused!"
    /// </summary>
    public string StatusApplied { get; init; } = "";

    /// <summary>
    /// Duration of the applied status effect in turns.
    /// 0 if no status effect was applied, or -1 for permanent effects.
    /// </summary>
    public int StatusDuration { get; init; }

    /// <summary>
    /// All map tiles affected by the spell (for area/path visual effects).
    /// Null for single-target or self spells. Used by the Presentation layer
    /// to drive VFX overlays — pure grid coordinates, no Godot types.
    /// </summary>
    public IReadOnlyList<(int X, int Y)>? AffectedTiles { get; init; }

    /// <summary>Position of the spell caster (for travel projectile origin).</summary>
    public (int X, int Y)? CasterPos { get; init; }

    /// <summary>Target tile position (for travel projectile destination / cone direction).</summary>
    public (int X, int Y)? TargetPos { get; init; }
}

/// <summary>
/// Emitted when the player uses a wand (before spell resolution).
/// Lets the presentation layer show charge-count badge updates and the harness
/// track wand usage vs. charge depletion rates.
/// </summary>
public sealed class WandUseEvent : TurnEvent
{
    public string WandName { get; init; } = "";
    public int RemainingCharges { get; init; }

    /// <summary>True when the wand fired successfully (charges consumed).</summary>
    public bool Success { get; init; }

    /// <summary>True when wand was destroyed (charges reached 0 on this use).</summary>
    public bool WandDestroyed { get; init; }
}

/// <summary>
/// Emitted when a scroll is picked up and auto-recharged into a matching wand
/// instead of entering inventory. The presentation layer should show a toast:
/// "[WandName] recharged! ([NewCharges] charges)"
/// </summary>
public sealed class WandRechargeEvent : TurnEvent
{
    public string WandName { get; init; } = "";
    public string ScrollName { get; init; } = "";
    public int NewCharges { get; init; }
}

/// <summary>
/// Emitted when a map-reveal spell fires.
/// RevealType: "fov" for Light Scroll (marks current FOV tiles explored),
///             "full" for Magic Mapping (marks entire floor explored).
/// </summary>
public sealed class MapRevealEvent : TurnEvent
{
    public string RevealType { get; init; } = "";
}

/// <summary>
/// Emitted by Detect Monsters scroll. All monster positions are snapshotted here
/// so the presentation layer can briefly flash them even through walls.
/// Duration is in turns (20 for the detect monsters scroll).
/// </summary>
public sealed class DetectMonstersEvent : TurnEvent
{
    public IReadOnlyList<(int X, int Y, int MonsterId)> MonsterPositions { get; init; }
        = Array.Empty<(int, int, int)>();
    public int Duration { get; init; }
}

/// <summary>
/// Emitted when a status effect is applied to an entity.
/// Complements SpellEvent for cases where multiple status effects are applied
/// in a single spell resolution (e.g., AoE fear applying FearEffect to many targets).
/// Presentation layer uses this for per-entity toast messages.
/// </summary>
public sealed class StatusAppliedEvent : TurnEvent
{
    public int TargetId { get; init; }
    public string EffectName { get; init; } = "";

    /// <summary>Duration in turns, or -1 for permanent effects.</summary>
    public int Duration { get; init; }
}

/// <summary>
/// Emitted when a status effect expires (duration hits 0 or is explicitly removed).
/// Reason is typically "duration" for natural expiry or a specific cause like "woke_on_damage".
/// </summary>
public sealed class StatusExpiredEvent : TurnEvent
{
    public int EntityId { get; init; }
    public string EffectName { get; init; } = "";

    /// <summary>"duration" for natural expiry, or a specific cause (e.g. "woke_on_damage").</summary>
    public string Reason { get; init; } = "duration";
}

/// <summary>
/// Emitted each turn when a DOT effect (poison, burning, plague) deals damage.
/// </summary>
public sealed class DotDamageEvent : TurnEvent
{
    public int EntityId { get; init; }
    public string EffectName { get; init; } = "";
    public int Damage { get; init; }
}

/// <summary>
/// Emitted each turn when a HOT effect (regeneration) heals the entity.
/// </summary>
public sealed class HotHealEvent : TurnEvent
{
    public int EntityId { get; init; }
    public string EffectName { get; init; } = "";
    public int Amount { get; init; }
}

/// <summary>
/// Emitted when an item type is identified for the first time this run.
/// Presentation layer should show a toast: "You realize this was a [IdentifiedName]!"
/// </summary>
public sealed class IdentificationEvent : TurnEvent
{
    /// <summary>The YAML type ID of the item type that was identified (e.g. "healing_potion").</summary>
    public string TypeId { get; init; } = "";

    /// <summary>The true name revealed (e.g. "Healing Potion", "Scroll of Lightning").</summary>
    public string IdentifiedName { get; init; } = "";

    /// <summary>
    /// Trigger that caused identification.
    /// Values: "used" (potion/scroll/wand used), "equipped" (ring equipped).
    /// </summary>
    public string Trigger { get; init; } = "used";
}

/// <summary>
/// Describes how a throw resolved.
/// PotionShatter: potion consumed; if Hit, spell applied to target monster.
/// WeaponHit: weapon dealt damage to a monster, then landed on ground (retrievable).
/// WeaponMiss: weapon missed (no monster at final tile), landed on ground (retrievable).
/// JunkLand: non-potion, non-weapon item landed on ground with no effect (retrievable).
/// </summary>
public enum ThrowResultType
{
    PotionShatter,
    WeaponHit,
    WeaponMiss,
    JunkLand,
}

/// <summary>
/// Emitted when the player throws an item.
/// Weapons deal damage and land on ground (retrievable). Potions shatter on impact.
/// Junk (rings, scrolls, armor) lands on ground with no effect (retrievable).
/// Throwing always breaks invisibility and resets momentum.
/// PoC reference: ~/development/rlike/throwing.py
/// </summary>
public sealed class ThrowEvent : TurnEvent
{
    /// <summary>Entity ID of the item being thrown.</summary>
    public int ItemId { get; init; }

    /// <summary>Name of the item (for toast display).</summary>
    public string ItemName { get; init; } = "";

    /// <summary>Position of the thrower at the time of throw (for animation path length).</summary>
    public int ActorX { get; init; }
    public int ActorY { get; init; }

    /// <summary>Target tile chosen by the player.</summary>
    public int TargetX { get; init; }
    public int TargetY { get; init; }

    /// <summary>Actual landing tile after Bresenham path + wall/range clipping.</summary>
    public int LandX { get; init; }
    public int LandY { get; init; }

    /// <summary>True if a monster was at the final tile when the item arrived.</summary>
    public bool Hit { get; init; }

    /// <summary>Damage dealt to the target (weapons only). 0 for misses, potions, junk.</summary>
    public int Damage { get; init; }

    /// <summary>True if the hit killed the target monster.</summary>
    public bool TargetKilled { get; init; }

    /// <summary>Entity ID of the monster hit (null on miss).</summary>
    public int? TargetEntityId { get; init; }

    /// <summary>
    /// True when the item is left on the ground as a floor item (weapons, junk).
    /// False when the item is consumed (potions always consumed regardless of hit/miss).
    /// </summary>
    public bool ItemLandsOnGround { get; init; }

    /// <summary>How the throw resolved.</summary>
    public ThrowResultType ResultType { get; init; }
}

/// <summary>
/// Emitted when an entity's turn is skipped due to a status effect (SlowedEffect, ImmobilizedEffect, SleepEffect).
/// </summary>
public sealed class SkipTurnEvent : TurnEvent
{
    public int EntityId { get; init; }
    public string EffectName { get; init; } = "";
}

/// <summary>
/// Emitted when a teleport spell resolves (Teleport Scroll, Blink Scroll).
/// Misfire=true means the caster ended up at a random tile instead of the chosen one.
/// Presentation layer uses this to animate the teleport flash and show misfire toast.
/// </summary>
public sealed class TeleportEvent : TurnEvent
{
    public int EntityId { get; init; }
    public int FromX { get; init; }
    public int FromY { get; init; }
    public int ToX { get; init; }
    public int ToY { get; init; }
    public bool Misfire { get; init; }

    /// <summary>
    /// Cause of the teleport. Empty for spell-triggered teleports.
    /// "ring_of_teleportation" for on-hit ring procs.
    /// </summary>
    public string Reason { get; init; } = "";
}

/// <summary>
/// Emitted when a portal pair is placed by the Wand of Portals.
/// Two of these are emitted per wand use — one for entrance, one for exit.
/// Presentation layer spawns the portal sprite at Position using Type to pick the tint.
/// </summary>
public sealed class PortalPlacedEvent : TurnEvent
{
    public int PlacerId { get; init; }
    public PortalType Type { get; init; }
    public int PortalEntityId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
}

/// <summary>
/// Emitted when an entity (player or monster) teleports through a portal.
/// Presentation layer plays a teleport flash at From and To positions.
/// </summary>
public sealed class PortalTeleportEvent : TurnEvent
{
    public int EntityId { get; init; }
    public int FromX { get; init; }
    public int FromY { get; init; }
    public int ToX { get; init; }
    public int ToY { get; init; }
    public int PortalEntryId { get; init; }
    public int PortalExitId { get; init; }
}

/// <summary>
/// Emitted when an existing portal pair is removed — either because the Wand of Portals
/// placed a new pair (recycling the old one) or a floor transition cleared portals.
/// Presentation layer despawns the portal sprites.
/// </summary>
public sealed class PortalRemovedEvent : TurnEvent
{
    public int EntranceEntityId { get; init; }
    public int ExitEntityId { get; init; }
}

/// <summary>
/// Emitted when the player opens a chest by bumping into it.
/// DroppedItemIds contains all entity IDs of items placed on the floor.
/// Presentation layer swaps chest sprite from closed (261) to open (262).
/// </summary>
public sealed class ChestOpenedEvent : TurnEvent
{
    public int X { get; init; }
    public int Y { get; init; }
    public IReadOnlyList<int> DroppedItemIds { get; init; } = Array.Empty<int>();
}

/// <summary>
/// Emitted when the player bumps an already-open chest to collect the visible loot (second interaction).
/// Items are moved from the chest tile to the player's inventory via auto-pickup.
/// Presentation layer swaps the chest sprite from open-with-items (262) to empty (264).
/// </summary>
public sealed class ChestLootedEvent : TurnEvent
{
    public int X { get; init; }
    public int Y { get; init; }
}

/// <summary>
/// Emitted when the player reads a signpost by bumping into it. Free action — no turn consumed.
/// Presentation layer shows a popup with Message.
/// </summary>
public sealed class SignpostReadEvent : TurnEvent
{
    public int X { get; init; }
    public int Y { get; init; }
    public string Message { get; init; } = "";
    public string SignType { get; init; } = "";
}

/// <summary>
/// Emitted when the player examines a mural by bumping into it. Costs a turn.
/// Presentation layer shows a popup with Text.
/// </summary>
public sealed class MuralExaminedEvent : TurnEvent
{
    public int X { get; init; }
    public int Y { get; init; }
    public string Text { get; init; } = "";
    public string MuralId { get; init; } = "";
}

/// <summary>
/// Emitted when a wraith (or other life drain monster) heals from melee damage.
/// </summary>
public sealed class LifeDrainEvent : TurnEvent
{
    public int TargetId { get; init; }
    public int Amount { get; init; }
}

/// <summary>
/// Emitted when a lich's Soul Bolt resolves and deals %MaxHp damage.
/// </summary>
public sealed class SoulBoltEvent : TurnEvent
{
    public int TargetId { get; init; }
    public int Damage { get; init; }
}

/// <summary>
/// Emitted when a lich begins channeling an ability (Soul Bolt charge turn).
/// </summary>
public sealed class ChannelEvent : TurnEvent
{
    public string AbilityName { get; init; } = "";
}

/// <summary>
/// Emitted when a lich heals from an allied undead dying within Death Siphon radius.
/// </summary>
public sealed class DeathSiphonEvent : TurnEvent
{
    public int DeadMonsterId { get; init; }
    public int Amount { get; init; }
}

/// <summary>
/// Emitted when the player cancels portal targeting after placing an entrance.
/// The entrance entity has been unregistered from the map; presentation despawns its sprite.
/// This event is NOT produced by TurnController — it is emitted directly by GameController
/// when targeting is cancelled, so it does not appear in TurnResult.Events.
/// </summary>
public sealed class PortalEntranceCancelledEvent : TurnEvent
{
    public int EntranceEntityId { get; init; }
}

/// <summary>
/// Emitted when a monster dies and its entity is transformed into a corpse in-place.
/// Presentation layer can use this to update the entity sprite to a corpse graphic.
/// </summary>
public sealed class CorpseCreatedEvent : TurnEvent
{
    /// <summary>Entity ID of the corpse (same as the dead monster's ID — transformed in-place).</summary>
    public int CorpseEntityId { get; init; }

    /// <summary>CorpseId string for lineage tracking (format: "corpse_{x}_{y}_{turn}").</summary>
    public string CorpseId { get; init; } = "";

    /// <summary>Original monster type ID (e.g. "orc", "zombie").</summary>
    public string OriginalMonsterId { get; init; } = "";
}

/// <summary>
/// Emitted when a corpse entity is raised into a living monster (by necromancer AI or Raise Dead scroll).
/// Presentation layer should update the entity sprite back to the living monster graphic.
/// </summary>
public sealed class RaiseDeadEvent : TurnEvent
{
    /// <summary>Entity ID of the raised entity (same as the corpse entity — transformed in-place).</summary>
    public int RaisedEntityId { get; init; }

    /// <summary>CorpseId of the corpse that was raised.</summary>
    public string CorpseId { get; init; } = "";

    /// <summary>
    /// Faction the raised entity was assigned.
    /// "neutral" for player-raised; raiser's faction for necromancer-raised.
    /// </summary>
    public string AssignedFaction { get; init; } = "";
}

/// <summary>
/// Emitted when a monster attempts to use an item from its inventory.
/// Both success and failure paths emit this event so the presentation layer
/// can show appropriate feedback (toast messages) and the harness can measure
/// how often item usage fires and what outcomes result.
/// </summary>
/// <summary>
/// The player used an item, announced from the use seam rather than inferred from
/// whatever the item happened to DO.
///
/// Separate from <see cref="ItemUseEvent"/> deliberately. That one is emitted only by
/// ResolveMonsterItemUse and carries the monster fumble table (Success / FailureMode /
/// EffectAmount); the player has no fumble table, and reusing it would fire the monster's
/// failure wording on the player's path.
///
/// Emitted at the point the use COMMITS - after the silence gate, after the charge check,
/// alongside the stack decrement - so it never announces a use the rules then refuse.
///
/// Exists because the player's item use had no announcement at all. Messaging was derived
/// per-effect: HealEvent has a formatter, so healing potions looked announced by accident,
/// while every self-buff potion resolves through ResolveSelfStatusEffect, which emits only
/// a SpellEvent - an event ToastLog has no case for. Drinking invisibility produced no
/// message of any kind, and neither did speed, protection, heroism or shield.
/// </summary>
public sealed class PlayerItemUseEvent : TurnEvent
{
    /// <summary>Display name, already resolved through identification (may be an appearance name).</summary>
    public string ItemName { get; init; } = "";

    /// <summary>The action verb - "drink", "read", "point". See TurnController.UseVerb.</summary>
    public string Verb { get; init; } = "use";
}

public sealed class ItemUseEvent : TurnEvent
{
    public string ItemName { get; init; } = "";
    public bool Success { get; init; }

    /// <summary>
    /// How the item usage failed. Empty string on success.
    /// Values: "fizzle" (nothing happens), "wrong_target" (player benefits),
    /// "equipment_damage" (monster's weapon loses a point of DamageMax).
    /// </summary>
    public string FailureMode { get; init; } = "";

    /// <summary>
    /// HP healed (success or wrong_target path) or DamageMax reduction (equipment_damage).
    /// 0 on fizzle.
    /// </summary>
    public int EffectAmount { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Interactive props + trap system events (Phase 1)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Emitted when a destructible prop (barrel, bookshelf, bone pile) is broken/searched by the player.
/// Presentation layer swaps the sprite to the open/broken tile ID.
/// </summary>
public sealed class PropDestroyedEvent : TurnEvent
{
    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>"barrel" | "bookshelf" | "bone_pile"</summary>
    public string PropKind { get; init; } = "";

    /// <summary>Entity IDs of items dropped to the player's tile.</summary>
    public IReadOnlyList<int> DroppedItemIds { get; init; } = Array.Empty<int>();

    /// <summary>True when a trap payload fired during resolution.</summary>
    public bool TrapFired { get; init; }

    /// <summary>True when a monster was roused from this prop.</summary>
    public bool MonsterRoused { get; init; }
}

/// <summary>
/// Emitted when a floor trap fires against a target (player or monster).
/// </summary>
public sealed class TrapTriggeredEvent : TurnEvent
{
    /// <summary>Entity ID of the creature that triggered and is affected by the trap.</summary>
    public int TargetId { get; init; }

    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>Source label: trap type ID or prop kind (e.g. "spike_trap", "barrel_trap").</summary>
    public string Source { get; init; } = "";

    /// <summary>The action kinds that were resolved (e.g. ["damage", "bleed"]).</summary>
    public IReadOnlyList<string> ActionKinds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Emitted when a floor trap is passively detected before triggering.
/// The trap is now marked IsDetected and will be avoided on future entry.
/// </summary>
public sealed class TrapDetectedEvent : TurnEvent
{
    public int X { get; init; }
    public int Y { get; init; }
    public string TrapType { get; init; } = "";
}

/// <summary>
/// Emitted when the player steps onto a previously-detected trap, triggering auto-avoid.
/// </summary>
public sealed class TrapAvoidedEvent : TurnEvent
{
    public int X { get; init; }
    public int Y { get; init; }
    public string TrapType { get; init; } = "";
}

/// <summary>
/// Emitted when a bone pile rouse fires and a monster is spawned nearby.
/// </summary>
public sealed class MonsterRousedEvent : TurnEvent
{
    /// <summary>Entity ID of the newly spawned monster.</summary>
    public int SpawnedEntityId { get; init; }

    public string MonsterType { get; init; } = "";
    public int OriginX { get; init; }
    public int OriginY { get; init; }
}

/// <summary>
/// Emitted each tick when a BleedEffect deals damage to an entity.
/// ActorId (inherited) is the entity taking bleed damage.
/// </summary>
public sealed class BleedTickEvent : TurnEvent
{
    public int Damage { get; init; }
}

/// <summary>
/// Emitted when a status effect (poison or bleed) transfers from one entity to another
/// via a drain attack (wraith, future vampire).
/// </summary>
public sealed class StatusTransferredEvent : TurnEvent
{
    public int SourceId { get; init; }
    public int TargetId { get; init; }

    /// <summary>"poison" | "bleed"</summary>
    public string EffectKind { get; init; } = "";
}

/// <summary>
/// Emitted each turn that InnateRegenComponent is suppressed by an active AcidEffect.
/// ActorId (inherited) is the entity whose innate regen was suppressed.
/// </summary>
public sealed class RegenSuppressedEvent : TurnEvent
{
}

/// <summary>
/// Emitted when the player's equipped weapon is coated with acid after triggering an acid_trap.
/// </summary>
public sealed class WeaponAcidCoatedEvent : TurnEvent
{
    public int WeaponId { get; init; }
    public int HitsRemaining { get; init; }
}

/// <summary>
/// Emitted when a locked chest is successfully unlocked by a matching colored key.
/// The key is consumed immediately; the chest proceeds to open normally (ChestOpenedEvent follows).
/// Presentation layer should show a toast: "The [color] key unlocks the chest!"
/// </summary>
public sealed class ChestUnlockedEvent : TurnEvent
{
    public int ChestId { get; init; }
    public int KeyId { get; init; }
    public int LockColorId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
}

/// <summary>
/// Emitted when a key item is consumed (used to unlock a chest).
/// Presentation layer can use this to remove the key sprite from any inventory display.
/// </summary>
public sealed class KeyConsumedEvent : TurnEvent
{
    public int KeyId { get; init; }
    public int LockColorId { get; init; }
}

/// <summary>
/// Emitted when the player bumps a locked chest but has no matching key in inventory.
/// Presentation layer should show a toast: "This chest is locked. You need a [color] key."
/// </summary>
public sealed class ChestLockedEvent : TurnEvent
{
    public int ChestId { get; init; }
    public int LockColorId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
}

/// <summary>
/// Emitted when the player bumps a locked door but has no matching key in inventory.
/// This is a free action — no turn is consumed (same as bumping a wall).
/// Presentation layer should show a toast: "This door is locked. You need a [color] key."
/// </summary>
public sealed class LockedDoorBumpedEvent : TurnEvent
{
    public int X { get; init; }
    public int Y { get; init; }
    public int LockColorId { get; init; }
}

/// <summary>
/// Emitted when passive detection reveals a SecretDoor tile, converting it to a normal Door.
/// The tile has already been changed to TileKind.Door when this event fires.
/// Presentation layer should: replace the wall sprite with a door overlay at (X, Y),
/// and show Hint as a toast/log message.
/// </summary>
public sealed class SecretDoorFoundEvent : TurnEvent
{
    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>Flavor text hint shown to the player upon discovery.</summary>
    public string Hint { get; init; } = "";
}

/// <summary>
/// Emitted when the player uses a matching key to unlock and open a locked door.
/// The key is consumed; the door tile changes from LockedDoor to DoorOpen.
/// Presentation layer should: swap tile sprite, show toast, remove key icon overlay.
/// </summary>
public sealed class DoorUnlockedEvent : TurnEvent
{
    public int X { get; init; }
    public int Y { get; init; }
    public int KeyId { get; init; }
    public int LockColorId { get; init; }
}

// ─── Possession Events ────────────────────────────────────────────────────────

public sealed class PossessionEnteredEvent : TurnEvent
{
    public int HostEntityId { get; init; }
    public string HostSpecies { get; init; } = "";
    public int OriginatorBodyId { get; init; }
}

/// <summary>
/// Emitted on any possession exit (voluntary, host_died, visibility_broken, dispelled, home_body_died).
/// Presentation layer uses Reason to pick camera behaviour and SFX.
/// </summary>
public sealed class PossessionExitedEvent : TurnEvent
{
    public string Reason { get; init; } = "";
    public int HostEntityId { get; init; }
    public string HostSpecies { get; init; } = "";
}

/// <summary>Drain tick: HP removed from the home body each possessed player-turn.</summary>
public sealed class PossessionDrainEvent : TurnEvent
{
    public int TargetEntityId { get; init; }
    public int Damage { get; init; }
}

/// <summary>
/// Fired once per possession session when the home body falls to ≤25% MaxHp.
/// Presentation layer shows a visual/audio warning. Logic layer sets NearDeathWarningFired
/// on the effect to suppress duplicates.
/// </summary>
public sealed class PossessionNearDeathWarningEvent : TurnEvent
{
    public int HomeBodyEntityId { get; init; }
    public int CurrentHp { get; init; }
    public int MaxHp { get; init; }
}

// ─── Voice Events ─────────────────────────────────────────────────────────────

/// <summary>
/// Emitted when a logic-layer event should trigger a Hollowmark or Quipping Shade voice line.
/// TriggerId maps to a pool key in the voice line YAML files.
/// The presentation layer resolves the actual line text via VoiceLineRegistry.
/// </summary>
public sealed class VoiceLineEvent : TurnEvent
{
    public string TriggerId { get; init; } = "";

    /// <summary>
    /// The resolved player-facing line text. Null at emission time (emit sites have no
    /// VoiceLineRegistry). Populated for transcript capture by the harness recorder, which
    /// resolves TriggerId against the registry using a DEDICATED rng (never the game rng,
    /// so replay determinism is preserved). The live game resolves separately in Main.cs.
    /// Required for the LLM-testing transcript so voice/register checks can run on the
    /// as-fired string in any run (bot or LLM) — see docs/llm-testing/shared-transcript-schema.md.
    /// </summary>
    public string? ResolvedText { get; set; }
}

/// <summary>
/// Emitted when a monster kicks Hollowmark's phantom wand to a new tile.
/// The wand position is stored on PossessionEffect (Option A from §5/OQ-2 resolution) —
/// no transient entity. NewWandPositionX/Y reflect the updated phantom coordinates.
/// </summary>
public sealed class WandKickedEvent : TurnEvent
{
    public int NewWandPositionX { get; init; }
    public int NewWandPositionY { get; init; }
    public int KickerEntityId { get; init; }
}

// ─── Ranged Combat Events (Phase 22.2) ───────────────────────────────────────

/// <summary>
/// Emitted on every ranged attack attempt (hit, miss, or denial).
/// Metrics are derived from this event in RunMetrics.RecordTurn.
///
/// BandName values: "adjacent_threatened" | "close_range" | "optimal_range" |
///                  "far_range" | "extreme_range" | "denied_out_of_range"
///
/// Denied=true means the attack was rejected before resolving (d>8 or no LoS).
/// On denial: no ammo consumed, ranged_attacks_made counter NOT incremented.
/// </summary>
public sealed class RangedAttackEvent : TurnEvent
{
    public int TargetId { get; init; }
    public int Distance { get; init; }
    public string BandName { get; init; } = "";
    public bool Denied { get; init; }
    public bool Hit { get; init; }
    public int Damage { get; init; }
    /// <summary>True when the defender retaliates (d≤1 and defender can_retaliate).</summary>
    public bool RetaliationTriggered { get; init; }
    /// <summary>True when knockback proc fired AND tiles_moved > 0.</summary>
    public bool KnockbackApplied { get; init; }
    /// <summary>True when a special ammo on-hit effect was successfully applied to the target.</summary>
    public bool SpecialEffectApplied { get; init; }
    /// <summary>Damage before range penalty (for penalty calculation). 0 on miss or denial.</summary>
    public int DamageBeforePenalty { get; init; }
    /// <summary>True when this hit killed the target.</summary>
    public bool TargetKilled { get; init; }
}

/// <summary>
/// Emitted when a special ammo shot is consumed (hit OR miss, but NOT on denial and NOT
/// if the player died to retaliation before the shot resolved).
/// Remaining=0 means the quiver was exhausted and auto-unequipped this turn.
/// </summary>
public sealed class SpecialAmmoConsumedEvent : TurnEvent
{
    public string AmmoType { get; init; } = "";
    public int Remaining { get; init; }
}

/// <summary>
/// Emitted when a ranged knockback proc fires AND actually moves the target (tiles_moved > 0).
/// Silent failures (wall, entity, edge) do not emit this event.
/// Direction is the unit vector: each component is -1, 0, or 1.
/// </summary>
public sealed class RangedKnockbackEvent : TurnEvent
{
    public (int X, int Y) Direction { get; init; }
    public int TilesMoved { get; init; }
    public int TargetId { get; init; }
}

/// <summary>
/// A single page of the Weighing audit dialogue. Speaker determines color/label in the panel.
/// </summary>
public sealed record WeighingDialoguePage(
    string Speaker,  // "under_warden" | "guardian" | "narrator"
    string Text);

/// <summary>
/// Emitted by the Weighing orchestrator when a blocking paged dialogue sequence must play
/// before the encounter can continue. The presentation layer shows a WeighingDialoguePanel
/// that the player taps through; the game blocks input until all pages are dismissed.
/// </summary>
public sealed class WeighingDialogueEvent : TurnEvent
{
    public string DialogueKey { get; init; } = "";
    public IReadOnlyList<WeighingDialoguePage> Pages { get; init; } = Array.Empty<WeighingDialoguePage>();
}

/// <summary>
/// Emitted when movement or a special action (leap) is denied because the entity is entangled.
/// BlockedActionType: "move" for normal movement, "leap" for SkirmisherAI leap attempt.
/// EntityId is the entangled entity (player or monster).
/// </summary>
public sealed class EntangleMoveBlockedEvent : TurnEvent
{
    public int EntityId { get; init; }
    public string BlockedActionType { get; init; } = "move";
}

// ─── Faction instrumentation ──────────────────────────────────────────────────

/// <summary>
/// Emitted at the turn where the orc unprovoked-kill tally crosses the Hostile threshold,
/// predicting the run-end faction-state transition that fires in the presentation layer.
/// The actual mutation (FactionsData.ApplyNegativeAction) remains in Main.cs; this event makes the
/// threshold-crossing observable in the logic-layer event stream for the transcript and detectors.
///
/// Not emitted if the rep is already Hostile this run (threshold can only be crossed once).
/// FactionId: the faction crossing the threshold (currently always "orc").
/// ToState: the state it will transition to at run end ("hostile").
/// KillsThisRun: the tally count at the moment of threshold crossing.
/// </summary>
public sealed class OrcRepChangedEvent : TurnEvent
{
    public string FactionId { get; init; } = "";
    public string ToState { get; init; } = "";
    public int KillsThisRun { get; init; }
}

/// <summary>Emitted when the LLM player fell back to the bot brain for a turn.</summary>
public sealed class LlmFallbackEvent : TurnEvent
{
    public string Reason { get; init; } = "";
}
