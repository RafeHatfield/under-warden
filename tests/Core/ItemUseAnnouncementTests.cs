using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Every item use announces itself, from the use seam rather than from its effect.
///
/// The bug these exist for: the player's item use had no announcement at all. What the
/// player saw was whichever EFFECT event happened to own a ToastLog formatter. HealEvent
/// has one, so healing potions looked announced by accident. Every self-buff potion
/// resolves through SpellResolver.ResolveSelfStatusEffect, which returns only a SpellEvent
/// - and ToastLog has no SpellEvent case. Drinking invisibility, speed, protection,
/// heroism or shield produced no message of any kind.
///
/// The property asserted here is the one that makes the fix hold: the announcement does
/// not depend on the effect being visible, or on the effect emitting anything at all.
/// </summary>
[TestFixture]
public class ItemUseAnnouncementTests
{
    private static Entity MakePlayer()
    {
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        player.Add(new Inventory());
        return player;
    }

    private static GameState MakeState(Entity player)
        => new(player, new List<Entity>(), GameMap.CreateArena(20, 20), new SeededRandom(1337));

    private static PlayerItemUseEvent? Announcement(TurnResult result)
        => result.Events.OfType<PlayerItemUseEvent>().SingleOrDefault();

    // ── The case that had no message at all ──────────────────────────────────

    [Test]
    public void InvisibilityPotion_IsAnnounced_ThoughItsEffectEmitsOnlyASpellEvent()
    {
        var player = MakePlayer();
        var potion = new Entity(100, "Potion of Invisibility", 0, 0);
        potion.Add(new Consumable(healAmount: 0, isPotion: true));
        potion.Add(new SpellEffect
        {
            SpellId = "invisibility", Targeting = TargetingMode.Self, Duration = 30,
        });
        potion.Add(new IdentifiableItem { IdentifiedName = "Potion of Invisibility" });
        player.Require<Inventory>().Add(potion);

        var state  = MakeState(player);
        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(potion), monsterFactory: null);

        var announced = Announcement(result);
        Assert.That(announced, Is.Not.Null,
            "an invisible effect still has to announce the action that caused it.");
        Assert.That(announced!.Verb, Is.EqualTo("drink"));
        Assert.That(result.Events.OfType<StatusAppliedEvent>(), Is.Empty,
            "guard on the premise: this path emits no StatusAppliedEvent to fall back on.");
    }

    // ── One announcement per use, whatever the item class ────────────────────

    [Test]
    public void HealingPotion_IsAnnouncedOnce_AndDrinks()
    {
        var player = MakePlayer();
        player.Require<Fighter>().Hp = 10;
        var potion = new Entity(100, "Healing Potion", 0, 0);
        potion.Add(new Consumable(healAmount: 40));
        potion.Add(new IdentifiableItem { IdentifiedName = "Healing Potion" });
        player.Require<Inventory>().Add(potion);

        var state  = MakeState(player);
        var result = TurnController.ProcessTurn(state, PlayerAction.UseItem(potion), monsterFactory: null);

        var announced = Announcement(result);
        Assert.That(announced, Is.Not.Null, "exactly one announcement, not zero and not one per effect.");
        Assert.That(announced!.Verb, Is.EqualTo("drink"));
        Assert.That(announced.ItemName, Is.EqualTo("Healing Potion"));
    }

    [Test]
    public void Scroll_IsAnnouncedAsRead()
    {
        // SpellItemFactory builds scrolls as Consumable(healAmount: 0) + SpellEffect —
        // neither potion nor healing, which is what makes them read rather than drunk.
        var player = MakePlayer();
        var scroll = new Entity(100, "Scroll of Shield", 0, 0);
        scroll.Add(new Consumable(healAmount: 0));
        scroll.Add(new SpellEffect { SpellId = "shield", Targeting = TargetingMode.Self, Duration = 10 });
        scroll.Add(new IdentifiableItem { IdentifiedName = "Scroll of Shield" });
        player.Require<Inventory>().Add(scroll);

        var state  = MakeState(player);
        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(scroll), monsterFactory: null);

        Assert.That(Announcement(result)?.Verb, Is.EqualTo("read"));
    }

    [Test]
    public void Wand_IsAnnouncedAsPointed()
    {
        var player = MakePlayer();
        var wand = new Entity(100, "Wand of Shield", 0, 0);
        wand.Add(new WandComponent { Charges = 3, MaxCharges = 5 });
        wand.Add(new SpellEffect { SpellId = "shield", Targeting = TargetingMode.Self, Duration = 10 });
        wand.Add(new IdentifiableItem { IdentifiedName = "Wand of Shield" });
        player.Require<Inventory>().Add(wand);

        var state  = MakeState(player);
        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand), monsterFactory: null);

        Assert.That(Announcement(result)?.Verb, Is.EqualTo("point"));
    }

    // ── Never announce a use the rules refused ───────────────────────────────

    [Test]
    public void SpentWand_IsNotAnnounced()
    {
        var player = MakePlayer();
        var wand = new Entity(100, "Wand of Shield", 0, 0);
        wand.Add(new WandComponent { Charges = 0, MaxCharges = 5 });
        wand.Add(new SpellEffect { SpellId = "shield", Targeting = TargetingMode.Self, Duration = 10 });
        player.Require<Inventory>().Add(wand);

        var state  = MakeState(player);
        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(wand), monsterFactory: null);

        Assert.That(Announcement(result), Is.Null,
            "the charge check refused the use — announcing it would describe something that did not happen.");
    }

    [Test]
    public void SilencedScroll_IsNotAnnounced()
    {
        var player = MakePlayer();
        player.Add(new SilencedEffect { RemainingTurns = 3 });
        var scroll = new Entity(100, "Scroll of Shield", 0, 0);
        scroll.Add(new Consumable(healAmount: 0));
        scroll.Add(new SpellEffect { SpellId = "shield", Targeting = TargetingMode.Self, Duration = 10 });
        player.Require<Inventory>().Add(scroll);

        var state  = MakeState(player);
        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(scroll), monsterFactory: null);

        Assert.That(Announcement(result), Is.Null, "silence blocked the read before it committed.");
    }

    [Test]
    public void SilencedPotion_IsStillAnnounced()
    {
        // Silence blocks speech, not swallowing — the potion path is exempt, so the
        // announcement has to follow the rule rather than the item's shape.
        var player = MakePlayer();
        player.Add(new SilencedEffect { RemainingTurns = 3 });
        var potion = new Entity(100, "Potion of Speed", 0, 0);
        potion.Add(new Consumable(healAmount: 0, isPotion: true));
        potion.Add(new SpellEffect { SpellId = "haste", Targeting = TargetingMode.Self, Duration = 20 });
        potion.Add(new IdentifiableItem { IdentifiedName = "Potion of Speed" });
        player.Require<Inventory>().Add(potion);

        var state  = MakeState(player);
        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(potion), monsterFactory: null);

        Assert.That(Announcement(result)?.Verb, Is.EqualTo("drink"));
    }

    // ── Ordering: the action, then what it did ───────────────────────────────

    [Test]
    public void Announcement_PrecedesTheEffectEvent()
    {
        var player = MakePlayer();
        player.Require<Fighter>().Hp = 10;
        var potion = new Entity(100, "Healing Potion", 0, 0);
        potion.Add(new Consumable(healAmount: 40));
        player.Require<Inventory>().Add(potion);

        var state  = MakeState(player);
        var result = TurnController.ProcessTurn(state, PlayerAction.UseItem(potion), monsterFactory: null);

        int announcedAt = result.Events.FindIndex(e => e is PlayerItemUseEvent);
        int healedAt    = result.Events.FindIndex(e => e is HealEvent);

        Assert.That(announcedAt, Is.GreaterThanOrEqualTo(0));
        Assert.That(healedAt,    Is.GreaterThanOrEqualTo(0));
        Assert.That(announcedAt, Is.LessThan(healedAt),
            "the log has to read in the order it happened: you drink, then you heal.");
    }
}
