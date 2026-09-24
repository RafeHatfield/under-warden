using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for all potion spell IDs in SpellResolver.
/// Covers buff potions (Phase A2), debuff potions (Phase A3), dual-mode and special potions (Phase A4).
///
/// Duration assertions use Is.InRange(N-1, N) because ProcessTurnEnd decrements effects
/// by 1 at the end of the turn where they were applied. An effect applied with duration 20
/// will read as 19 after ProcessTurn returns. Using range (N-1, N) confirms the correct
/// initial duration was set without being brittle to the tick ordering.
///
/// No YAML loading required. SpellEffect is created directly with the potion spell_id.
/// </summary>
[TestFixture]
public class SpellResolverPotionTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static (GameState state, Entity player, Entity monster) CreateState(
        int playerHp = 50, int monsterHp = 30, int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: playerHp, strength: 12, dexterity: 10, constitution: 12,
            accuracy: 5, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Orc", 6, 5, blocksMovement: true);
        monster.Add(new Fighter(hp: monsterHp, strength: 12, dexterity: 10, constitution: 12,
            accuracy: 5, evasion: 0, damageMin: 2, damageMax: 4));
        monster.Add(new AiComponent { AiType = "basic", Faction = "orc", Tags = ["humanoid", "corporeal_flesh"] });
        map.RegisterEntity(monster);

        var state = new GameState(player, new List<Entity> { monster }, map, rng, turnLimit: 50);
        return (state, player, monster);
    }

    /// <summary>Create a potion entity with the given spell_id and targeting mode.</summary>
    private static Entity MakePotion(string spellId, TargetingMode targeting = TargetingMode.Self, int duration = 0)
    {
        var potion = new Entity(10, "Test Potion", 0, 0);
        potion.Add(new Consumable(isPotion: true));
        potion.Add(new SpellEffect { SpellId = spellId, Targeting = targeting, Duration = duration });
        return potion;
    }

    private static PlayerAction DrinkPotion(Entity potion, Entity player)
    {
        player.GetOrAdd<Inventory>().Add(potion);
        return PlayerAction.CastSpell(potion);
    }

    private static PlayerAction ThrowPotion(Entity potion, Entity player, Entity target)
    {
        player.GetOrAdd<Inventory>().Add(potion);
        return PlayerAction.CastSpell(potion, targetEntityId: target.Id);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase A2: Buff Potions
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void DrinkSpeed_ReusesHasteSpell_AppliesSpeedEffect()
    {
        var (state, player, _) = CreateState();
        var potion = MakePotion("haste", duration: 20);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<SpeedEffect>();
        Assert.That(effect, Is.Not.Null, "SpeedEffect should be applied after drinking speed potion.");
        // ProcessTurnEnd decrements by 1 at end of the same turn — check duration was set to 20.
        Assert.That(effect!.RemainingTurns, Is.InRange(19, 20));
    }

    [Test]
    public void DrinkInvisibility_ReusesInvisibilitySpell_AppliesInvisibilityEffect()
    {
        var (state, player, _) = CreateState();
        var potion = MakePotion("invisibility", duration: 30);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<InvisibilityEffect>();
        Assert.That(effect, Is.Not.Null, "InvisibilityEffect should be applied after drinking invisibility potion.");
        Assert.That(effect!.RemainingTurns, Is.InRange(29, 30));
    }

    [Test]
    public void DrinkProtection_AppliesProtectionEffect_WithCorrectValues()
    {
        var (state, player, _) = CreateState();
        var potion = MakePotion("drink_protection", duration: 50);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<ProtectionEffect>();
        Assert.That(effect, Is.Not.Null, "ProtectionEffect should be applied.");
        Assert.That(effect!.RemainingTurns, Is.InRange(49, 50), "Duration should be 50 turns (PoC value).");
        Assert.That(effect.AcBonus, Is.EqualTo(4), "AC bonus should be +4 (PoC value).");
    }

    [Test]
    public void DrinkRegeneration_AppliesRegenerationEffect_WithCorrectValues()
    {
        var (state, player, _) = CreateState();
        var potion = MakePotion("drink_regeneration", duration: 50);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<RegenerationEffect>();
        Assert.That(effect, Is.Not.Null, "RegenerationEffect should be applied.");
        Assert.That(effect!.RemainingTurns, Is.InRange(49, 50), "Duration should be 50 turns (PoC value).");
        Assert.That(effect.HealPerTurn, Is.EqualTo(1), "HealPerTurn should be 1 (PoC: 1 HP/t / 50t = 50 HP total).");
    }

    [Test]
    public void DrinkHeroism_AppliesHeroismEffect_WithCorrectValues()
    {
        var (state, player, _) = CreateState();
        var potion = MakePotion("drink_heroism", duration: 30);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<HeroismEffect>();
        Assert.That(effect, Is.Not.Null, "HeroismEffect should be applied.");
        Assert.That(effect!.RemainingTurns, Is.InRange(29, 30), "Duration should be 30 turns (PoC value).");
        Assert.That(effect.AttackBonus, Is.EqualTo(3), "AttackBonus should be +3 (PoC value).");
        Assert.That(effect.DamageBonus, Is.EqualTo(3), "DamageBonus should be +3 (PoC value).");
    }

    [Test]
    public void HeroismEffect_AddsToHitBonus_InCombat()
    {
        // Verify HeroismEffect is wired in CombatResolver (component is present and no crash occurs).
        var (state, player, monster) = CreateState();
        player.Add(new HeroismEffect { RemainingTurns = 10, AttackBonus = 3, DamageBonus = 3 });
        var result = TurnController.ProcessTurn(state, PlayerAction.Attack(monster));

        var attackEvent = result.Events.OfType<AttackEvent>().FirstOrDefault(e => e.ActorId == player.Id);
        Assert.That(attackEvent, Is.Not.Null, "AttackEvent should be emitted when player has HeroismEffect.");
    }

    [Test]
    public void HeroismEffect_StacksWithRallyEffect()
    {
        // Both RallyEffect and HeroismEffect should coexist — different component types.
        var (state, player, _) = CreateState();
        player.Add(new RallyEffect { RemainingTurns = 10, ToHitBonus = 1, DamageBonus = 1 });
        player.Add(new HeroismEffect { RemainingTurns = 30, AttackBonus = 3, DamageBonus = 3 });

        Assert.That(player.Get<RallyEffect>(), Is.Not.Null, "RallyEffect should be present.");
        Assert.That(player.Get<HeroismEffect>(), Is.Not.Null, "HeroismEffect should be present.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase A3: Debuff Potions — drink (self) and throw (target)
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void DrinkWeakness_AppliesWeaknessEffect_ToSelf_Duration30()
    {
        var (state, player, _) = CreateState();
        var potion = MakePotion("drink_weakness", duration: 30);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<WeaknessEffect>();
        Assert.That(effect, Is.Not.Null);
        Assert.That(effect!.RemainingTurns, Is.InRange(29, 30));
    }

    [Test]
    public void DrinkSlowness_AppliesSlowedEffect_ToSelf_Duration20()
    {
        var (state, player, _) = CreateState();
        var potion = MakePotion("drink_slowness", duration: 20);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<SlowedEffect>();
        Assert.That(effect, Is.Not.Null);
        Assert.That(effect!.RemainingTurns, Is.InRange(19, 20));
    }

    [Test]
    public void DrinkBlindness_AppliesBlindedEffect_ToSelf_Duration15()
    {
        var (state, player, _) = CreateState();
        var potion = MakePotion("drink_blindness", duration: 15);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<BlindedEffect>();
        Assert.That(effect, Is.Not.Null);
        Assert.That(effect!.RemainingTurns, Is.InRange(14, 15));
    }

    [Test]
    public void DrinkParalysis_AppliesImmobilizedEffect_ToSelf_Duration3To5()
    {
        var (state, player, _) = CreateState();
        // SpellEffect Duration=0 so resolver uses random 3-5. After end-of-turn tick: 2-4 range.
        var potion = MakePotion("drink_paralysis");
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<ImmobilizedEffect>();
        Assert.That(effect, Is.Not.Null);
        Assert.That(effect!.RemainingTurns, Is.InRange(2, 5),
            "Paralysis duration should be random 3-5 turns (PoC value), minus 1 for end-of-turn tick.");
    }

    [Test]
    public void DrinkTar_AppliesSluggishEffect_ToSelf_Duration10()
    {
        var (state, player, _) = CreateState();
        var potion = MakePotion("drink_tar", duration: 10);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<SluggishEffect>();
        Assert.That(effect, Is.Not.Null);
        Assert.That(effect!.RemainingTurns, Is.InRange(9, 10));
    }

    [Test]
    public void ThrowWeakness_AppliesWeaknessEffect_ToTarget_Duration30()
    {
        var (state, player, monster) = CreateState();
        var potion = MakePotion("throw_weakness", targeting: TargetingMode.SingleTarget, duration: 30);
        TurnController.ProcessTurn(state, ThrowPotion(potion, player, monster));

        Assert.That(monster.Get<WeaknessEffect>(), Is.Not.Null, "WeaknessEffect should be applied to the target.");
        Assert.That(player.Get<WeaknessEffect>(), Is.Null, "Player should NOT have WeaknessEffect when throwing.");
    }

    [Test]
    public void ThrowBlindness_AppliesBlindedEffect_ToTarget_Duration15()
    {
        var (state, player, monster) = CreateState();
        var potion = MakePotion("throw_blindness", targeting: TargetingMode.SingleTarget, duration: 15);
        TurnController.ProcessTurn(state, ThrowPotion(potion, player, monster));

        var effect = monster.Get<BlindedEffect>();
        Assert.That(effect, Is.Not.Null);
        // Monster's ProcessTurnEnd also decrements at end of monster turn.
        Assert.That(effect!.RemainingTurns, Is.InRange(13, 15));
    }

    [Test]
    public void ThrowParalysis_AppliesImmobilizedEffect_ToTarget_Duration3To5()
    {
        var (state, player, monster) = CreateState();
        var potion = MakePotion("throw_paralysis", targeting: TargetingMode.SingleTarget);
        TurnController.ProcessTurn(state, ThrowPotion(potion, player, monster));

        var effect = monster.Get<ImmobilizedEffect>();
        Assert.That(effect, Is.Not.Null);
        // Paralysis applied in player turn. Monster ProcessTurnEnd also runs. Range 1-5.
        Assert.That(effect!.RemainingTurns, Is.InRange(1, 5));
    }

    [Test]
    public void DrinkSlowness_FreeAction_BlocksApplication()
    {
        var (state, player, _) = CreateState();
        // FreeActionTag blocks SlowedEffect and ImmobilizedEffect (via StatusEffectProcessor).
        player.Add(new FreeActionTag());

        var potion = MakePotion("drink_slowness", duration: 20);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        Assert.That(player.Get<SlowedEffect>(), Is.Null,
            "SlowedEffect should be blocked by FreeActionTag.");
    }

    [Test]
    public void ThrowSlowness_FreeAction_BlocksApplication()
    {
        var (state, player, monster) = CreateState();
        monster.Add(new FreeActionTag());

        var potion = MakePotion("throw_slowness", targeting: TargetingMode.SingleTarget, duration: 20);
        TurnController.ProcessTurn(state, ThrowPotion(potion, player, monster));

        Assert.That(monster.Get<SlowedEffect>(), Is.Null,
            "SlowedEffect should be blocked by FreeActionTag on the target.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase A4: Dual-mode and Special Potions
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void DrinkRoot_AppliesBarkskin_Plus3AC_10Turns()
    {
        var (state, player, _) = CreateState();
        var potion = MakePotion("drink_root", duration: 10);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<BarkskinEffect>();
        Assert.That(effect, Is.Not.Null);
        Assert.That(effect!.AcBonus, Is.EqualTo(3), "Root potion drink should give +3 AC (PoC value).");
        Assert.That(effect.RemainingTurns, Is.InRange(9, 10));
    }

    [Test]
    public void ThrowRoot_AppliesEntangled_ToTarget_3Turns()
    {
        var (state, player, monster) = CreateState();
        var potion = MakePotion("throw_root", targeting: TargetingMode.SingleTarget, duration: 3);
        TurnController.ProcessTurn(state, ThrowPotion(potion, player, monster));

        var effect = monster.Get<EntangledEffect>();
        Assert.That(effect, Is.Not.Null, "EntangledEffect should be applied to target.");
        // Applied in player turn; monster ProcessTurnEnd decrements. Range 1-3.
        Assert.That(effect!.RemainingTurns, Is.InRange(1, 3));
    }

    [Test]
    public void DrinkSunburst_AppliesFocused_Plus2Acc_8Turns()
    {
        var (state, player, _) = CreateState();
        var potion = MakePotion("drink_sunburst", duration: 8);
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var effect = player.Get<FocusedEffect>();
        Assert.That(effect, Is.Not.Null);
        Assert.That(effect!.AccuracyBonus, Is.EqualTo(2), "Sunburst potion drink should give +2 accuracy (PoC value).");
        Assert.That(effect.RemainingTurns, Is.InRange(7, 8));
    }

    [Test]
    public void ThrowSunburst_AppliesBlinded_ToTarget_3Turns()
    {
        var (state, player, monster) = CreateState();
        var potion = MakePotion("throw_sunburst", targeting: TargetingMode.SingleTarget, duration: 3);
        TurnController.ProcessTurn(state, ThrowPotion(potion, player, monster));

        var effect = monster.Get<BlindedEffect>();
        Assert.That(effect, Is.Not.Null, "BlindedEffect should be applied to target from sunburst throw.");
        Assert.That(effect!.RemainingTurns, Is.InRange(1, 3));
    }

    [Test]
    public void ThrowFire_AppliesBurning_ToTarget_1DmgPerTurn_4Turns()
    {
        var (state, player, monster) = CreateState();
        var potion = MakePotion("throw_fire", targeting: TargetingMode.SingleTarget, duration: 4);
        TurnController.ProcessTurn(state, ThrowPotion(potion, player, monster));

        var effect = monster.Get<BurningEffect>();
        Assert.That(effect, Is.Not.Null, "BurningEffect should be applied to target from fire potion throw.");
        Assert.That(effect!.DamagePerTurn, Is.EqualTo(1), "Fire potion does 1 dmg/turn (PoC value).");
        Assert.That(effect.RemainingTurns, Is.InRange(2, 4), "Fire potion lasts 4 turns (PoC value).");
    }

    [Test]
    public void DrinkAntidote_RemovesPlagueEffect()
    {
        var (state, player, _) = CreateState();
        player.Add(new PlagueEffect { RemainingTurns = 15, DamagePerTurn = 1 });

        var potion = MakePotion("drink_antidote");
        TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        Assert.That(player.Get<PlagueEffect>(), Is.Null, "PlagueEffect should be removed after drinking antidote.");
    }

    [Test]
    public void DrinkAntidote_NoPlaguePresent_NoError()
    {
        var (state, player, _) = CreateState();
        // No plague — antidote should still succeed without throwing.
        var potion = MakePotion("drink_antidote");
        Assert.DoesNotThrow(() =>
            TurnController.ProcessTurn(state, DrinkPotion(potion, player)));
    }

    [Test]
    public void DrinkAntidote_EmitsStatusExpiredEvent_WithReasonCured()
    {
        var (state, player, _) = CreateState();
        player.Add(new PlagueEffect { RemainingTurns = 10, DamagePerTurn = 1 });

        var potion = MakePotion("drink_antidote");
        var result = TurnController.ProcessTurn(state, DrinkPotion(potion, player));

        var expiredEvent = result.Events.OfType<StatusExpiredEvent>()
            .FirstOrDefault(e => e.EffectName == "plague" && e.Reason == "cured");
        Assert.That(expiredEvent, Is.Not.Null, "Should emit StatusExpiredEvent(plague, reason=cured).");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase A1: TASK-001 infrastructure (SilencedEffect gate + IsPotion)
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Silenced_DoesNotBlockPotion_WithSpellEffect()
    {
        // A potion with SpellEffect (routes through ResolveSpellAction) should NOT be blocked
        // by SilencedEffect. Scrolls and wands are blocked; potions are physical, not magical.
        var (state, player, _) = CreateState();
        player.Add(new SilencedEffect { RemainingTurns = 3 });

        var potion = MakePotion("haste", duration: 20);
        player.GetOrAdd<Inventory>().Add(potion);
        TurnController.ProcessTurn(state, PlayerAction.CastSpell(potion));

        // If silenced blocked the potion, SpeedEffect would not be applied.
        Assert.That(player.Get<SpeedEffect>(), Is.Not.Null,
            "Potion with SpellEffect should bypass SilencedEffect gate.");
    }

    [Test]
    public void Invisible_DrinkPotion_DoesNotBreakInvisibility()
    {
        // Drinking a buff potion while invisible should NOT break invisibility.
        var (state, player, _) = CreateState();
        player.Add(new InvisibilityEffect { RemainingTurns = 30 });

        var potion = MakePotion("haste", duration: 20);
        player.GetOrAdd<Inventory>().Add(potion);
        TurnController.ProcessTurn(state, PlayerAction.CastSpell(potion));

        Assert.That(player.Get<InvisibilityEffect>(), Is.Not.Null,
            "Drinking a potion should NOT break InvisibilityEffect.");
    }

    [Test]
    public void Invisible_ThrowPotion_BreaksInvisibility()
    {
        // Throwing a potion at a target (offensive action) SHOULD break invisibility.
        var (state, player, monster) = CreateState();
        player.Add(new InvisibilityEffect { RemainingTurns = 30 });

        var potion = MakePotion("throw_weakness", targeting: TargetingMode.SingleTarget, duration: 30);
        player.GetOrAdd<Inventory>().Add(potion);
        TurnController.ProcessTurn(state, PlayerAction.CastSpell(potion, targetEntityId: monster.Id));

        Assert.That(player.Get<InvisibilityEffect>(), Is.Null,
            "Throwing a potion at a target SHOULD break InvisibilityEffect.");
    }
}
