using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Content;

/// <summary>
/// Guards for the quick-slot bar's model.
///
/// The device bug these exist for: drinking a duration potion (invisibility, speed) resolves
/// through ResolveSpellAction, which consumes the stack but emits SpellEvent/StatusAppliedEvent.
/// GameController's redraw flag only listed HealEvent/PickUpEvent/etc., so the bar kept
/// rendering an item the player had already drunk until some unrelated later turn refreshed it.
///
/// These assert the property that fix depends on: the model reflects a consume the same turn
/// it happens, whatever event the rules chose to emit.
/// </summary>
[TestFixture]
public class QuickSlotModelTests
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

    private static Entity MakePotion(int id, string name, int healAmount = 15,
        int stackSize = 1, int useCooldownTurns = 0)
    {
        var potion = new Entity(id, name, 0, 0);
        potion.Add(new Consumable(healAmount, isPotion: true, useCooldownTurns)
        {
            StackSize = stackSize,
        });
        return potion;
    }

    // ── Consume → model updates the same turn ────────────────────────────────

    [Test]
    public void DurationPotion_LeavesModel_TheTurnItIsDrunk()
    {
        // A buff potion: SpellEffect + Consumable, no heal. This is the item class that
        // regressed — its consume path emits no event the old redraw list recognised.
        var player = MakePlayer();
        var potion = MakePotion(100, "Potion of Speed", healAmount: 0);
        potion.Add(new SpellEffect { SpellId = "haste", Targeting = TargetingMode.Self, Duration = 20 });
        player.Require<Inventory>().Add(potion);

        var state = MakeState(player);
        Assert.That(QuickSlotModel.Build(state).Select(e => e.ItemId), Does.Contain(100),
            "the potion must be on the bar before it is drunk.");

        TurnController.ProcessTurn(state, PlayerAction.CastSpell(potion), monsterFactory: null);

        Assert.That(QuickSlotModel.Build(state).Select(e => e.ItemId), Does.Not.Contain(100),
            "the last of a stack was drunk — the slot must be gone the same turn, not turns later.");
    }

    [Test]
    public void StackedDurationPotion_DecrementsBadge_TheTurnItIsDrunk()
    {
        var player = MakePlayer();
        var potion = MakePotion(100, "Potion of Speed", healAmount: 0, stackSize: 3);
        potion.Add(new SpellEffect { SpellId = "haste", Targeting = TargetingMode.Self, Duration = 20 });
        player.Require<Inventory>().Add(potion);

        var state = MakeState(player);
        Assert.That(QuickSlotModel.Build(state).Single().BadgeCount, Is.EqualTo(3));

        TurnController.ProcessTurn(state, PlayerAction.CastSpell(potion), monsterFactory: null);

        Assert.That(QuickSlotModel.Build(state).Single().BadgeCount, Is.EqualTo(2),
            "the count label must drop the same turn the potion is drunk.");
    }

    [Test]
    public void HealingPotion_DecrementsBadge_TheTurnItIsDrunk()
    {
        // The path that always worked — kept so a future refactor can't break it silently.
        var player = MakePlayer();
        player.Require<Fighter>().Hp = 10;
        var potion = MakePotion(100, "Healing Potion", healAmount: 15, stackSize: 2);
        player.Require<Inventory>().Add(potion);

        var state = MakeState(player);

        TurnController.ProcessTurn(state, PlayerAction.UseItem(potion), monsterFactory: null);

        Assert.That(QuickSlotModel.Build(state).Single().BadgeCount, Is.EqualTo(1));
    }

    // ── Cooldown → model reports unavailable ─────────────────────────────────

    [Test]
    public void CooldownGatedPotion_ReportsUnavailable_WhileCooldownPending()
    {
        var player = MakePlayer();
        var potion = MakePotion(100, "Healing Potion", healAmount: 20, stackSize: 5, useCooldownTurns: 10);
        player.Require<Inventory>().Add(potion);

        var state = MakeState(player);
        Assert.That(QuickSlotModel.Build(state).Single().Available, Is.True,
            "no cooldown pending — the slot is usable.");

        state.PlayerFighter.PotionCooldownRemaining = 6;

        var entry = QuickSlotModel.Build(state).Single();
        Assert.That(entry.Available, Is.False, "cooldown pending — the slot must report unavailable.");
        Assert.That(entry.CooldownRemaining, Is.EqualTo(6), "the slot must expose the turns left to show.");
    }

    [Test]
    public void PotionWithoutCooldown_StaysAvailable_EvenWhileACooldownIsPending()
    {
        // Mirrors CanUsePotion: an item with UseCooldownTurns == 0 is never gated.
        var player = MakePlayer();
        player.Require<Inventory>().Add(MakePotion(100, "Healing Potion", healAmount: 40));

        var state = MakeState(player);
        state.PlayerFighter.PotionCooldownRemaining = 6;

        var entry = QuickSlotModel.Build(state).Single();
        Assert.That(entry.Available, Is.True);
        Assert.That(entry.CooldownRemaining, Is.Zero, "an available slot reports no countdown.");
    }

    [Test]
    public void Wand_ReportsUnavailable_WhenOutOfCharges()
    {
        var player = MakePlayer();
        var wand = new Entity(100, "Wand of Fire", 0, 0);
        wand.Add(new SpellEffect { SpellId = "fireball", Targeting = TargetingMode.SingleTarget });
        wand.Add(new WandComponent { Charges = 0, MaxCharges = 5 });
        player.Require<Inventory>().Add(wand);

        var state = MakeState(player);

        var entry = QuickSlotModel.Build(state).Single();
        Assert.That(entry.Available, Is.False);
        Assert.That(entry.CooldownRemaining, Is.Zero, "a spent wand has no countdown — only an empty badge.");
    }

    [Test]
    public void InfiniteWand_IsAlwaysAvailable()
    {
        var player = MakePlayer();
        var wand = new Entity(100, "Wand of Portals", 0, 0);
        wand.Add(new SpellEffect { SpellId = "portal", Targeting = TargetingMode.Portal });
        wand.Add(new WandComponent { Charges = 0, Infinite = true });
        player.Require<Inventory>().Add(wand);

        var entry = QuickSlotModel.Build(MakeState(player)).Single();
        Assert.That(entry.Available, Is.True);
        Assert.That(entry.Infinite, Is.True);
    }

    // ── Change detection: the signal GameController redraws on ───────────────

    [Test]
    public void Model_DiffersAfterConsume_SoTheBarKnowsToRedraw()
    {
        // GameController compares before/after snapshots instead of enumerating event types.
        // This is the property that makes that comparison correct for the regressed path.
        var player = MakePlayer();
        var potion = MakePotion(100, "Potion of Speed", healAmount: 0, stackSize: 2);
        potion.Add(new SpellEffect { SpellId = "haste", Targeting = TargetingMode.Self, Duration = 20 });
        player.Require<Inventory>().Add(potion);

        var state  = MakeState(player);
        var before = QuickSlotModel.Build(state);

        TurnController.ProcessTurn(state, PlayerAction.CastSpell(potion), monsterFactory: null);

        Assert.That(before.SequenceEqual(QuickSlotModel.Build(state)), Is.False,
            "a consumed potion must change the model, or the bar has no reason to redraw.");
    }

    [Test]
    public void Model_IsUnchanged_ByATurnThatTouchesNothingOnTheBar()
    {
        // The other half: waiting must NOT look like a change, or the bar rebuilds every turn.
        var player = MakePlayer();
        player.Require<Inventory>().Add(MakePotion(100, "Healing Potion", healAmount: 40, stackSize: 2));

        var state  = MakeState(player);
        var before = QuickSlotModel.Build(state);

        TurnController.ProcessTurn(state, PlayerAction.Wait, monsterFactory: null);

        Assert.That(before.SequenceEqual(QuickSlotModel.Build(state)), Is.True,
            "an idle turn must leave the model identical — no needless rebuild.");
    }

    // ── Availability is one answer, and it names its reason ──────────────────

    [Test]
    public void Availability_NamesTheBlockingRule_ForEachSource()
    {
        var player = MakePlayer();
        var fighter = player.Require<Fighter>();

        var plain = MakePotion(100, "Healing Potion", healAmount: 40);
        Assert.That(QuickSlotModel.Availability(plain, fighter).Block, Is.EqualTo(QuickSlotBlock.None));

        var gated = MakePotion(101, "Healing Potion", healAmount: 20, useCooldownTurns: 10);
        Assert.That(QuickSlotModel.Availability(gated, fighter).Block, Is.EqualTo(QuickSlotBlock.None),
            "no timer running — a cooldown-gated potion is still available.");

        fighter.PotionCooldownRemaining = 4;
        var gatedNow = QuickSlotModel.Availability(gated, fighter);
        Assert.That(gatedNow.Block, Is.EqualTo(QuickSlotBlock.PotionCooldown));
        Assert.That(gatedNow.CooldownRemaining, Is.EqualTo(4));

        var spent = new Entity(102, "Wand of Fire", 0, 0);
        spent.Add(new WandComponent { Charges = 0, MaxCharges = 5 });
        Assert.That(QuickSlotModel.Availability(spent, fighter).Block, Is.EqualTo(QuickSlotBlock.NoCharges));
    }

    [Test]
    public void SpentWand_ReportsNoCountdown_EvenWhileAPotionCooldownRuns()
    {
        // The two sources are independent. CooldownRemaining used to be read off the potion
        // timer for ANY unavailable slot, so a spent wand would have printed a potion's
        // countdown over its own empty badge.
        var player = MakePlayer();
        player.Require<Fighter>().PotionCooldownRemaining = 7;

        var wand = new Entity(100, "Wand of Fire", 0, 0);
        wand.Add(new SpellEffect { SpellId = "fireball", Targeting = TargetingMode.SingleTarget });
        wand.Add(new WandComponent { Charges = 0, MaxCharges = 5 });
        player.Require<Inventory>().Add(wand);

        var entry = QuickSlotModel.Build(MakeState(player)).Single();
        Assert.That(entry.Block, Is.EqualTo(QuickSlotBlock.NoCharges));
        Assert.That(entry.CooldownRemaining, Is.Zero,
            "a spent wand must not borrow the potion timer's countdown.");
    }

    [Test]
    public void CanUseHealingPotion_AgreesWithWhatTheBarDims()
    {
        // The single-source property. The rules-side predicate (TurnController's auto-pick,
        // BotBrain's heal gate) and the bar's dim must never be able to disagree.
        var player = MakePlayer();
        var fighter = player.Require<Fighter>();
        var gated = MakePotion(100, "Healing Potion", healAmount: 20, useCooldownTurns: 10);
        player.Require<Inventory>().Add(gated);

        var state = MakeState(player);

        foreach (int pending in new[] { 0, 1, 5 })
        {
            fighter.PotionCooldownRemaining = pending;
            Assert.That(QuickSlotModel.CanUseHealingPotion(gated, fighter),
                Is.EqualTo(QuickSlotModel.Build(state).Single().Available),
                $"rules gate and bar dim disagreed with {pending} turns pending.");
        }
    }

    [Test]
    public void AutoPick_SkipsABlockedPotion_AndTakesAnUngatedOne()
    {
        // Behaviour the shared predicate has to preserve: the bot's FindFirst auto-pick
        // steps over a potion the timer is holding and drinks one that ignores the timer.
        var player = MakePlayer();
        player.Require<Fighter>().Hp = 10;
        player.Require<Fighter>().PotionCooldownRemaining = 5;

        var gated  = MakePotion(100, "Healing Potion", healAmount: 20, useCooldownTurns: 10);
        var ungated = MakePotion(101, "Healing Potion", healAmount: 40);
        player.Require<Inventory>().Add(gated);
        player.Require<Inventory>().Add(ungated);

        var state = MakeState(player);
        TurnController.ProcessTurn(state, PlayerAction.UseItem(), monsterFactory: null);

        Assert.That(player.Require<Inventory>().Items, Does.Contain(gated),
            "the blocked potion must be left alone.");
        Assert.That(player.Require<Inventory>().Items, Does.Not.Contain(ungated),
            "the ungated potion is the one the auto-pick should drink.");
    }
}
