using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Core;

/// <summary>
/// What the player (or UI) submits as their action for a turn.
/// Immutable. The presentation layer creates these; the logic layer consumes them.
/// The bot creates these via BotBrain.ToPlayerAction().
/// </summary>
public sealed class PlayerAction
{
    public enum ActionKind { Wait, Attack, Move, UseItem, Descend, DropItem, EquipItem, UnequipItem, CastSpell, ThrowItem, EnterPossessionTargeting, Possess, ExitPossession, UseMonsterAbility, RangedAttack }

    public ActionKind Kind { get; }

    /// <summary>Attack target, or entity to move toward.</summary>
    public Entity? Target { get; }

    /// <summary>Move destination (for tap-to-move from UI) or spell target tile.</summary>
    public int? TargetX { get; }
    public int? TargetY { get; }

    /// <summary>Specific item to use, equip, or cast (scroll/wand). Null = auto-find first healing potion (bot behavior).</summary>
    public Entity? Item { get; }

    /// <summary>Equipment slot to unequip. Only set for UnequipItem actions.</summary>
    public EquipmentSlot? Slot { get; }

    /// <summary>
    /// For targeted spells: the entity ID to target (single-target spells).
    /// Null for Self, AoeSelf, and AutoClosest targeting modes (resolver picks the target).
    /// </summary>
    public int? TargetEntityId { get; }

    /// <summary>
    /// Second target tile for portal placement — the exit position.
    /// TargetX/Y hold the entrance; TargetX2/Y2 hold the exit.
    /// Only set for Portal targeting mode (Wand of Portals).
    /// </summary>
    public int? TargetX2 { get; }
    public int? TargetY2 { get; }

    /// <summary>
    /// Ability ID for UseMonsterAbility actions.
    /// Maps to MonsterAbilityDefinition.AbilityId on the controlled entity's HostAbilityComponent.
    /// </summary>
    public string? AbilityId { get; }

    private readonly string? _abilityId;

    private PlayerAction(ActionKind kind, Entity? target = null,
        int? targetX = null, int? targetY = null, Entity? item = null,
        EquipmentSlot? slot = null, int? targetEntityId = null,
        int? targetX2 = null, int? targetY2 = null, string? abilityId = null)
    {
        Kind = kind;
        Target = target;
        TargetX = targetX;
        TargetY = targetY;
        Item = item;
        Slot = slot;
        TargetEntityId = targetEntityId;
        TargetX2 = targetX2;
        TargetY2 = targetY2;
        _abilityId = abilityId;
        AbilityId = abilityId;
    }

    public static PlayerAction Wait => new(ActionKind.Wait);
    public static PlayerAction Descend => new(ActionKind.Descend);
    public static PlayerAction Attack(Entity target) => new(ActionKind.Attack, target: target);
    public static PlayerAction MoveTo(int x, int y) => new(ActionKind.Move, targetX: x, targetY: y);
    public static PlayerAction MoveToward(Entity target) => new(ActionKind.Move, target: target);
    public static PlayerAction UseItem(Entity? item = null) => new(ActionKind.UseItem, item: item);
    public static PlayerAction Drop(Entity item) => new(ActionKind.DropItem, item: item);
    public static PlayerAction Equip(Entity item) => new(ActionKind.EquipItem, item: item);
    public static PlayerAction Unequip(EquipmentSlot slot) => new(ActionKind.UnequipItem, slot: slot);

    /// <summary>
    /// Cast a spell via a scroll or wand item.
    /// For Self/AoeSelf/AutoClosest targeting modes, omit targetEntityId/targetX/targetY —
    /// SpellResolver finds the target automatically.
    /// For SingleTarget spells, pass targetEntityId.
    /// For Location spells, pass targetX/targetY.
    /// </summary>
    public static PlayerAction CastSpell(Entity item, int? targetEntityId = null,
        int? targetX = null, int? targetY = null)
        => new(ActionKind.CastSpell, item: item,
               targetEntityId: targetEntityId, targetX: targetX, targetY: targetY);

    /// <summary>
    /// Cast the Wand of Portals — a two-point targeting action.
    /// Entrance is placed at (entranceX, entranceY); exit at (exitX, exitY).
    /// Both must be walkable tiles. TurnController validates before calling PortalSystem.
    /// </summary>
    public static PlayerAction CastSpellPortal(Entity item,
        int entranceX, int entranceY, int exitX, int exitY)
        => new(ActionKind.CastSpell, item: item,
               targetX: entranceX, targetY: entranceY,
               targetX2: exitX, targetY2: exitY);

    /// <summary>
    /// Throw an item at a target tile. ThrowResolver handles the three resolution paths:
    /// potion → apply throw spell on hit, weapon → deal weapon dice - 2 damage then land on ground,
    /// junk → land on ground with no effect. Equipped weapons are auto-unequipped before throw.
    /// </summary>
    public static PlayerAction ThrowItem(Entity item, int targetX, int targetY)
        => new(ActionKind.ThrowItem, item: item, targetX: targetX, targetY: targetY);

    /// <summary>Free action: open the possession targeting overlay.</summary>
    public static PlayerAction EnterPossessionTargeting() => new(ActionKind.EnterPossessionTargeting);

    /// <summary>Costs one turn: possess the given host entity.</summary>
    public static PlayerAction Possess(Entity host) => new(ActionKind.Possess, target: host);

    /// <summary>Free action: voluntarily exit the current possession.</summary>
    public static PlayerAction ExitPossession() => new(ActionKind.ExitPossession);

    /// <summary>
    /// Use a species-specific ability while possessing the host.
    /// TurnController routes abilityId to the appropriate resolver.
    /// Unknown ability IDs resolve as Wait (graceful degradation).
    /// </summary>
    public static PlayerAction UseAbility(string abilityId) => new(ActionKind.UseMonsterAbility, abilityId: abilityId);

    /// <summary>
    /// Shoot the equipped ranged weapon at the given target.
    /// RangedCombatService validates LoS and range band before resolving.
    /// The target must be a living monster — TurnController validates.
    /// </summary>
    public static PlayerAction ShootAt(Entity target) => new(ActionKind.RangedAttack, target: target);
}
