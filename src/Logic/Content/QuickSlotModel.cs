using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Why a quick-slot item cannot be used right now. One value per source of unavailability,
/// so the UI can dim it, the tap gate can refuse it, and the toast can name it — all from
/// the same answer instead of three independent re-derivations.
/// </summary>
public enum QuickSlotBlock
{
    /// <summary>Usable. The rules would accept a tap.</summary>
    None,

    /// <summary>
    /// A wand with no charges left. Infinite wands never report this.
    /// </summary>
    NoCharges,

    /// <summary>
    /// A cooldown-gated potion while the player's potion cooldown is still running.
    ///
    /// The cooldown is GLOBAL, not per-item: one timer on Fighter (PotionCooldownRemaining),
    /// armed by drinking any consumable that declares use_cooldown_turns. The item's own
    /// UseCooldownTurns says whether it PARTICIPATES in that timer and what it re-arms it to;
    /// it is not a second, separate countdown. An item with UseCooldownTurns == 0 ignores the
    /// timer entirely and is always drinkable.
    /// </summary>
    PotionCooldown,
}

/// <summary>
/// What one quick-slot shows: which item, what badge, and whether it can be used right now.
///
/// BadgeCount is the stack size for consumables and the charge count for wands;
/// Infinite wands report Infinite=true and BadgeCount is meaningless for them.
///
/// Available=false means a tap would be refused — Block names which rule refused it.
/// CooldownRemaining is the turns left on the potion cooldown, and is 0 for every
/// Block other than PotionCooldown (a spent wand has no countdown to show).
/// </summary>
public readonly record struct QuickSlotEntry(
    int            ItemId,
    string         DisplayName,
    int            BadgeCount,
    bool           Infinite,
    bool           Available,
    int            CooldownRemaining,
    QuickSlotBlock Block);

/// <summary>
/// Builds the quick-slot bar's contents as plain data, independent of how it is drawn,
/// and is the ONE place that answers "can this item be used right now?".
///
/// Two jobs, and the second is why the first exists in Logic rather than Presentation:
///
/// 1. Change detection. The presentation layer can ask "did anything the bar shows change
///    this turn?" without enumerating which turn events happen to imply an inventory change.
///    That enumeration is what broke: a buff potion consumed via ResolveSpellAction emits
///    SpellEvent/StatusAppliedEvent, neither of which was on the list, so the bar kept
///    rendering an item the player had already drunk.
///
/// 2. Availability truth. The dim, the tap gate, and the refusal toast all read Availability
///    below. Before, they didn't: the bar dimmed from one expression, the rules gated from a
///    local function inside TurnController.TryHeal that a UI tap never reached (it only ran on
///    the bot's FindFirst auto-pick), and the spent-wand refusal was a third check written
///    inline in GameController. The bar could therefore draw a slot dimmed with a countdown
///    and still drink the potion if you tapped it — a turn spent and a potion gone on an
///    action the UI had just told the player it would refuse.
///
/// No game rules are DECIDED here. Availability MIRRORS the rules' own gates — WandComponent
/// .HasCharges and the potion-cooldown rule — and the rules-side callers now read it back
/// rather than keeping private copies.
///
/// Lives in Logic.Content (not Presentation) so it can be tested without Godot, and so the
/// bot and TurnController can share it — same rationale as ItemDisplay, which it sits next to.
/// </summary>
public static class QuickSlotModel
{
    /// <summary>
    /// Can the player use this item right now, and if not, which rule says no?
    ///
    /// The single availability answer. One guard per source:
    ///   charges         — a wand with no charges left (WandComponent.HasCharges)
    ///   potion cooldown — a cooldown-participating consumable while the global timer runs
    ///
    /// Items that are neither (a plain potion, a scroll) are always available; whether the
    /// action then succeeds is the rules' business, not availability's.
    ///
    /// CooldownRemaining is only meaningful for PotionCooldown and is 0 otherwise — a spent
    /// wand has no countdown, and reading the potion timer for one would be a lie the bar
    /// would happily print.
    /// </summary>
    public static (bool Available, QuickSlotBlock Block, int CooldownRemaining)
        Availability(Entity item, Fighter fighter)
    {
        // Charges. Checked first: a spent wand is spent regardless of any potion timer.
        var wand = item.Get<WandComponent>();
        if (wand != null)
            return wand.HasCharges
                ? (true, QuickSlotBlock.None, 0)
                : (false, QuickSlotBlock.NoCharges, 0);

        // Potion cooldown. UseCooldownTurns == 0 means the item ignores the global timer.
        var consumable = item.Get<Consumable>();
        if (consumable != null
            && consumable.UseCooldownTurns > 0
            && fighter.PotionCooldownRemaining > 0)
            return (false, QuickSlotBlock.PotionCooldown, fighter.PotionCooldownRemaining);

        return (true, QuickSlotBlock.None, 0);
    }

    /// <summary>
    /// True when the player could drink this healing potion right now.
    ///
    /// The rules-side reading of <see cref="Availability"/>, kept as a named predicate because
    /// TurnController and BotBrain both want "is this a potion I can drink?" rather than the
    /// full triple. Same answer the bar dims from — that is the point.
    /// </summary>
    public static bool CanUseHealingPotion(Entity item, Fighter fighter)
    {
        var consumable = item.Get<Consumable>();
        if (consumable == null || !consumable.IsHealing) return false;
        return Availability(item, fighter).Available;
    }

    /// <summary>
    /// Snapshot every item the quick-slot bar would show, in inventory order.
    ///
    /// Membership matches QuickSlotBar.RefreshItemSlots: anything carrying a Consumable
    /// or a SpellEffect. Returns an empty list when the player has no inventory.
    /// </summary>
    public static List<QuickSlotEntry> Build(GameState state)
    {
        var entries = new List<QuickSlotEntry>();

        var inventory = state.PlayerInventory;
        if (inventory == null) return entries;

        var fighter = state.PlayerFighter;

        foreach (var item in inventory.Items)
        {
            var consumable = item.Get<Consumable>();
            var spell      = item.Get<SpellEffect>();
            if (consumable == null && spell == null) continue;

            var wand = item.Get<WandComponent>();

            int  badge    = wand != null ? wand.Charges : consumable?.StackSize ?? 0;
            bool infinite = wand?.Infinite ?? false;

            var (available, block, cooldown) = Availability(item, fighter);

            entries.Add(new QuickSlotEntry(
                ItemId:            item.Id,
                DisplayName:       ItemDisplay.GetDisplayName(item, state.IdentificationRegistry, state.AppearancePool),
                BadgeCount:        badge,
                Infinite:          infinite,
                Available:         available,
                CooldownRemaining: cooldown,
                Block:             block));
        }

        return entries;
    }

    /// <summary>
    /// The entity ID of the weapon in the main hand, or null for bare fists.
    /// Part of the bar's visible state, so it belongs in the change check.
    /// </summary>
    public static int? MainHandItemId(GameState state)
        => state.Player.Get<Equipment>()?.MainHand?.Id;
}
