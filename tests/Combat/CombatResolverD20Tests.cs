using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Combat;

/// <summary>
/// D20 combat mechanics. Ported from PoC test_d20_combat.py.
///
/// Coverage:
///   - Natural 20 always hits and doubles damage
///   - Natural 1 always misses
///   - Armor class calculation (base + DEX mod + armor equipment bonus)
///   - STR modifier applies to damage
///   - DEX modifier applies to to-hit roll
///   - Weapon damage used when weapon equipped (not natural attack)
///   - Weapon to-hit bonus applies to attack roll
///   - AC equipment bonus reduces attacker's hit rate
/// </summary>
[TestFixture]
public class CombatResolverD20Tests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Entity MakeFighter(int id, string name, int hp = 50,
        int str = 10, int dex = 10,
        int dmgMin = 1, int dmgMax = 4)
    {
        var e = new Entity(id, name);
        e.Add(new Fighter(hp: hp, strength: str, dexterity: dex,
            damageMin: dmgMin, damageMax: dmgMax));
        return e;
    }

    private static void EquipWeapon(Entity entity, int dmgMin, int dmgMax,
        int toHitBonus = 0, string? dmgType = null)
    {
        var weapon = new Entity(99, "Weapon");
        weapon.Add(new Equippable(EquipmentSlot.MainHand)
        {
            DamageMin = dmgMin, DamageMax = dmgMax,
            ToHitBonus = toHitBonus, DamageType = dmgType,
        });
        var equip = entity.Get<Equipment>() ?? entity.Add(new Equipment());
        equip.MainHand = weapon;
    }

    private static void EquipArmor(Entity entity, int acBonus)
    {
        var armor = new Entity(98, "Armor");
        armor.Add(new Equippable(EquipmentSlot.Chest) { ArmorClassBonus = acBonus });
        var equip = entity.Get<Equipment>() ?? entity.Add(new Equipment());
        equip.SetSlot(EquipmentSlot.Chest, armor);
    }

    // -------------------------------------------------------------------------
    // Crit / Fumble
    // -------------------------------------------------------------------------

    [Test]
    public void CriticalHit_Nat20_AlwaysHits_EvenAgainstUnreachableAC()
    {
        // DEX 1 → mod -5, weapon 0 to-hit. Needs 26+ to hit AC25... impossible except nat 20.
        var attacker = MakeFighter(1, "Attacker", dex: 1);
        EquipWeapon(attacker, dmgMin: 5, dmgMax: 5);

        // Defender: DEX 10 (mod 0) + plate armor (+10) = AC 20
        var defender = MakeFighter(2, "Defender", hp: 1000);
        EquipArmor(defender, 10);

        var rng = new SeededRandom(1337);
        bool foundCrit = false;

        for (int i = 0; i < 2000; i++)
        {
            defender.Require<Fighter>().Hp = 1000;
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.D20Roll == 20)
            {
                Assert.That(result.IsCritical, Is.True, "D20Roll=20 should set IsCritical");
                Assert.That(result.Hit, Is.True, "Nat 20 must always hit regardless of AC");
                foundCrit = true;
                break;
            }
        }

        Assert.That(foundCrit, Is.True, "Should roll nat 20 within 2000 attempts");
    }

    [Test]
    public void CriticalHit_Nat20_DoublesDamage()
    {
        // Fixed damage weapon: 5-5. STR 10 (mod 0). Normal hit = 5, crit = 10.
        var attacker = MakeFighter(1, "Attacker");
        EquipWeapon(attacker, dmgMin: 5, dmgMax: 5);

        var defender = MakeFighter(2, "Defender", hp: 10000);
        var rng = new SeededRandom(42);

        for (int i = 0; i < 2000; i++)
        {
            defender.Require<Fighter>().Hp = 10000;
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.IsCritical)
            {
                Assert.That(result.Damage, Is.EqualTo(10),
                    "Crit with fixed 5 damage and STR mod 0 should deal 10 (2×5)");
                return;
            }
        }

        Assert.Fail("Should produce a crit within 2000 attacks");
    }

    [Test]
    public void CriticalHit_Nat20_IncludesStrModInDoubling()
    {
        // STR 14 → mod +2. Weapon 5-5. Normal: 5+2=7. Crit: (5+2)*2=14.
        var attacker = MakeFighter(1, "Attacker", str: 14);
        EquipWeapon(attacker, dmgMin: 5, dmgMax: 5);

        var defender = MakeFighter(2, "Defender", hp: 10000);
        var rng = new SeededRandom(42);

        for (int i = 0; i < 2000; i++)
        {
            defender.Require<Fighter>().Hp = 10000;
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.IsCritical)
            {
                // (baseDmg + strMod) * 2 = (5 + 2) * 2 = 14
                Assert.That(result.Damage, Is.EqualTo(14),
                    "Crit doubles (baseDmg + STR mod) per PoC attack_d20: damage *= 2");
                return;
            }
        }

        Assert.Fail("Should produce a crit within 2000 attacks");
    }

    [Test]
    public void Fumble_Nat1_AlwaysMisses_EvenAgainstAC1()
    {
        // Attacker: DEX 20 (mod +5), weapon +10 to-hit. Always hits on 2+. Nat 1 = fumble.
        var attacker = MakeFighter(1, "Attacker", dex: 20);
        EquipWeapon(attacker, dmgMin: 5, dmgMax: 5, toHitBonus: 10);

        var defender = MakeFighter(2, "Defender", hp: 1000, dex: 1); // AC 8 (10 - 1)

        var rng = new SeededRandom(1337);
        bool foundFumble = false;

        for (int i = 0; i < 2000; i++)
        {
            defender.Require<Fighter>().Hp = 1000;
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.D20Roll == 1)
            {
                Assert.That(result.IsFumble, Is.True, "D20Roll=1 should set IsFumble");
                Assert.That(result.Hit, Is.False, "Nat 1 must always miss regardless of to-hit bonus");
                Assert.That(result.Damage, Is.EqualTo(0), "Fumble deals no damage");
                foundFumble = true;
                break;
            }
        }

        Assert.That(foundFumble, Is.True, "Should roll nat 1 within 2000 attempts");
    }

    [Test]
    public void CriticalHitRate_NoKeen_IsApproximately5Percent()
    {
        var attacker = MakeFighter(1, "Attacker");
        EquipWeapon(attacker, dmgMin: 1, dmgMax: 1);
        var defender = MakeFighter(2, "Defender", hp: 1000000);
        var rng = new SeededRandom(1337);

        int crits = 0;
        int total = 2000;
        for (int i = 0; i < total; i++)
        {
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.IsCritical) crits++;
        }

        double critRate = (double)crits / total;
        Assert.That(critRate, Is.InRange(0.03, 0.07),
            $"Base crit rate {critRate:P1} should be ~5% (only nat 20)");
    }

    // -------------------------------------------------------------------------
    // Armor Class
    // -------------------------------------------------------------------------

    [Test]
    public void BaseArmorClass_Is10PlusDexMod()
    {
        // No equipment: AC = 10 + DEX mod
        var defender = MakeFighter(1, "Defender", dex: 12); // mod +1
        var fighter = defender.Require<Fighter>();
        Assert.That(fighter.BaseArmorClass, Is.EqualTo(11)); // 10 + 1
    }

    [Test]
    public void ArmorEquipment_IncreasesEffectiveAC()
    {
        // Attacker: DEX 10 (mod 0). Defender: DEX 10 (mod 0). Base AC = 10.
        // Leather armor adds +2 AC → effective AC 12.
        // Unarmored: hit on 10+ = 55% (rolls 10-20 = 11 values / 20 = 55%)
        // Armored: hit on 12+ = 45% (rolls 12-20 = 9 values / 20 = 45%)
        var attacker = MakeFighter(1, "Attacker");
        EquipWeapon(attacker, dmgMin: 1, dmgMax: 1);

        var unarmored = MakeFighter(2, "Unarmored", hp: 1000000);
        var armored = MakeFighter(3, "Armored", hp: 1000000);
        EquipArmor(armored, acBonus: 2); // leather armor

        var rng1 = new SeededRandom(1337);
        var rng2 = new SeededRandom(1337);

        int unarmoredHits = 0, armoredHits = 0;
        int total = 2000;
        for (int i = 0; i < total; i++)
        {
            if (CombatResolver.ResolveAttack(attacker, unarmored, rng1).Hit) unarmoredHits++;
            if (CombatResolver.ResolveAttack(attacker, armored, rng2).Hit) armoredHits++;
        }

        Assert.That(unarmoredHits, Is.GreaterThan(armoredHits),
            "Armor should reduce hit count");

        double unarmoredRate = (double)unarmoredHits / total;
        double armoredRate = (double)armoredHits / total;
        Assert.That(unarmoredRate, Is.InRange(0.50, 0.60),
            $"Unarmored AC 10 hit rate {unarmoredRate:P1} should be ~55%");
        Assert.That(armoredRate, Is.InRange(0.40, 0.50),
            $"Armored AC 12 hit rate {armoredRate:P1} should be ~45%");
    }

    [Test]
    public void HighDex_IncreasesArmorClass()
    {
        // DEX 18 → mod +4. AC = 14.
        // Attacker: DEX 10 (mod 0). Hits on 14+ = 35%.
        var attacker = MakeFighter(1, "Attacker");
        EquipWeapon(attacker, dmgMin: 1, dmgMax: 1);
        var defender = MakeFighter(2, "HighDex", hp: 1000000, dex: 18); // AC 14

        var rng1 = new SeededRandom(1337);
        var rng2 = new SeededRandom(1337);
        var lowDexDefender = MakeFighter(3, "LowDex", hp: 1000000, dex: 10); // AC 10

        int highDexHits = 0, lowDexHits = 0;
        int total = 2000;
        for (int i = 0; i < total; i++)
        {
            if (CombatResolver.ResolveAttack(attacker, defender, rng1).Hit) highDexHits++;
            if (CombatResolver.ResolveAttack(attacker, lowDexDefender, rng2).Hit) lowDexHits++;
        }

        Assert.That(highDexHits, Is.LessThan(lowDexHits),
            "High DEX defender should be hit less often");
    }

    // -------------------------------------------------------------------------
    // To-Hit Modifiers
    // -------------------------------------------------------------------------

    [Test]
    public void DexModifier_IncreasesToHitRoll()
    {
        // DEX 14 → mod +2. Should hit more often than DEX 10 (mod 0).
        var highDexAttacker = MakeFighter(1, "HighDex", dex: 14);
        EquipWeapon(highDexAttacker, dmgMin: 1, dmgMax: 1);
        var lowDexAttacker = MakeFighter(2, "LowDex", dex: 10);
        EquipWeapon(lowDexAttacker, dmgMin: 1, dmgMax: 1);

        var defender = MakeFighter(3, "Defender", hp: 1000000);

        var rng1 = new SeededRandom(1337);
        var rng2 = new SeededRandom(1337);

        int highDexHits = 0, lowDexHits = 0;
        int total = 2000;
        for (int i = 0; i < total; i++)
        {
            if (CombatResolver.ResolveAttack(highDexAttacker, defender, rng1).Hit) highDexHits++;
            if (CombatResolver.ResolveAttack(lowDexAttacker, defender, rng2).Hit) lowDexHits++;
        }

        Assert.That(highDexHits, Is.GreaterThan(lowDexHits),
            "Higher DEX attacker should hit more often");

        double highDexRate = (double)highDexHits / total;
        double lowDexRate = (double)lowDexHits / total;
        Assert.That(highDexRate, Is.InRange(0.60, 0.72),
            $"DEX 14 hit rate {highDexRate:P1} should be ~65% (d20+2 vs AC 10)");
        Assert.That(lowDexRate, Is.InRange(0.50, 0.60),
            $"DEX 10 hit rate {lowDexRate:P1} should be ~55% (d20+0 vs AC 10)");
    }

    [Test]
    public void WeaponToHitBonus_IncreasesHitRate()
    {
        // Same attacker, weapon with +3 to-hit vs weapon with +0 to-hit.
        var attacker1 = MakeFighter(1, "With bonus");
        EquipWeapon(attacker1, dmgMin: 1, dmgMax: 1, toHitBonus: 3);
        var attacker2 = MakeFighter(2, "No bonus");
        EquipWeapon(attacker2, dmgMin: 1, dmgMax: 1, toHitBonus: 0);

        var defender = MakeFighter(3, "Defender", hp: 1000000);

        var rng1 = new SeededRandom(1337);
        var rng2 = new SeededRandom(1337);

        int hits1 = 0, hits2 = 0;
        int total = 2000;
        for (int i = 0; i < total; i++)
        {
            if (CombatResolver.ResolveAttack(attacker1, defender, rng1).Hit) hits1++;
            if (CombatResolver.ResolveAttack(attacker2, defender, rng2).Hit) hits2++;
        }

        Assert.That(hits1, Is.GreaterThan(hits2),
            "Weapon with +3 to-hit should produce more hits");
    }

    // -------------------------------------------------------------------------
    // Damage Calculation
    // -------------------------------------------------------------------------

    [Test]
    public void StrengthModifier_AppliedToDamage()
    {
        // STR 14 → mod +2. Weapon damage fixed at 3-3. Every hit should deal 5 (3+2).
        var attacker = MakeFighter(1, "Attacker", hp: 100, str: 14);
        EquipWeapon(attacker, dmgMin: 3, dmgMax: 3);

        var defender = MakeFighter(2, "Defender", hp: 100000);
        var rng = new SeededRandom(1337);

        bool foundHit = false;
        for (int i = 0; i < 100; i++)
        {
            defender.Require<Fighter>().Hp = 100000;
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.Hit && !result.IsCritical)
            {
                Assert.That(result.Damage, Is.EqualTo(5),
                    "Normal hit: weapon 3 + STR mod 2 = 5 (matches PoC attack_d20 line 1191)");
                foundHit = true;
                break;
            }
        }

        Assert.That(foundHit, Is.True, "Should get a normal hit within 100 attempts");
    }

    [Test]
    public void HighStrength_DealMoreDamageThanLowStrength()
    {
        // Both use weapon 1-1. STR 18 (mod+4) vs STR 6 (mod-2). Many hits, compare totals.
        var highStr = MakeFighter(1, "HighStr", str: 18);
        EquipWeapon(highStr, dmgMin: 1, dmgMax: 1);
        var lowStr = MakeFighter(2, "LowStr", str: 6);
        EquipWeapon(lowStr, dmgMin: 1, dmgMax: 1);

        var defender = MakeFighter(3, "Defender", hp: 100000);

        var rng1 = new SeededRandom(1337);
        var rng2 = new SeededRandom(1337);

        int dmg1 = 0, dmg2 = 0;
        for (int i = 0; i < 500; i++)
        {
            defender.Require<Fighter>().Hp = 100000;
            dmg1 += CombatResolver.ResolveAttack(highStr, defender, rng1).Damage;
            dmg2 += CombatResolver.ResolveAttack(lowStr, defender, rng2).Damage;
        }

        Assert.That(dmg1, Is.GreaterThan(dmg2),
            "STR 18 (+4) should out-damage STR 6 (-2) significantly");
    }

    [Test]
    public void WeaponDamage_UsedWhenEquipped_NotNaturalAttack()
    {
        // Natural attack 1-1 (very weak), weapon 10-10 (very strong).
        // When weapon equipped, should use weapon damage.
        var attacker = MakeFighter(1, "Attacker", dmgMin: 1, dmgMax: 1, str: 10);
        EquipWeapon(attacker, dmgMin: 10, dmgMax: 10);

        var defender = MakeFighter(2, "Defender", hp: 100000);
        var rng = new SeededRandom(1337);

        bool foundHit = false;
        for (int i = 0; i < 100; i++)
        {
            defender.Require<Fighter>().Hp = 100000;
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.Hit && !result.IsCritical)
            {
                // Weapon 10 + STR mod 0 = 10. Natural 1 + 0 = 1. Should be 10.
                Assert.That(result.Damage, Is.EqualTo(10),
                    "With weapon equipped, weapon damage used (not natural attack)");
                foundHit = true;
                break;
            }
        }

        Assert.That(foundHit, Is.True);
    }

    [Test]
    public void NaturalAttack_UsedWhenNoWeaponEquipped()
    {
        // No weapon: uses DamageMin/DamageMax. Fixed at 5-5 for predictability.
        var attacker = MakeFighter(1, "Attacker", dmgMin: 5, dmgMax: 5, str: 10);
        // No equipment added

        var defender = MakeFighter(2, "Defender", hp: 100000);
        var rng = new SeededRandom(1337);

        bool foundHit = false;
        for (int i = 0; i < 100; i++)
        {
            defender.Require<Fighter>().Hp = 100000;
            var result = CombatResolver.ResolveAttack(attacker, defender, rng);
            if (result.Hit && !result.IsCritical)
            {
                Assert.That(result.Damage, Is.EqualTo(5),
                    "Natural attack 5-5 + STR mod 0 = 5 when no weapon equipped");
                foundHit = true;
                break;
            }
        }

        Assert.That(foundHit, Is.True);
    }

    // -------------------------------------------------------------------------
    // Typical combat scenario (integration-level sanity check)
    // -------------------------------------------------------------------------

    [Test]
    public void PlayerVsOrc_TypicalCombat_Sanity()
    {
        // Player: STR 14, DEX 14, HP 56 (54 base + CON mod 2), dagger (1-4)
        // Orc: STR 14, DEX 10, HP 29 (28 base + CON mod 1), natural 4-6
        // Player hits on d20+2 >= 10 (orc AC) → 65%
        // Orc hits on d20+0 >= 12 (player AC 14 without armor) → 45%
        var player = new Entity(0, "Player");
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            damageMin: 1, damageMax: 2));
        var dagger = new Entity(10, "Dagger");
        dagger.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 1, DamageMax = 4 });
        var playerEquip = player.Add(new Equipment());
        playerEquip.MainHand = dagger;

        // Leather armor: +2 AC (player AC = 10 + 2 + 2 = 14)
        var leatherArmor = new Entity(11, "Leather Armor");
        leatherArmor.Add(new Equippable(EquipmentSlot.Chest) { ArmorClassBonus = 2 });
        playerEquip.SetSlot(EquipmentSlot.Chest, leatherArmor);

        var orc = new Entity(1, "Orc");
        orc.Add(new Fighter(hp: 28, strength: 14, dexterity: 10, constitution: 12,
            damageMin: 4, damageMax: 6));

        var rng = new SeededRandom(1337);
        int playerDeaths = 0;
        int orcDeaths = 0;
        int simulations = 200;

        for (int sim = 0; sim < simulations; sim++)
        {
            var pFighter = player.Require<Fighter>();
            var oFighter = orc.Require<Fighter>();
            pFighter.Hp = pFighter.MaxHp;
            oFighter.Hp = oFighter.MaxHp;

            int turns = 0;
            while (pFighter.IsAlive && oFighter.IsAlive && turns < 100)
            {
                CombatResolver.ResolveAttack(player, orc, rng);
                if (oFighter.IsAlive)
                    CombatResolver.ResolveAttack(orc, player, rng);
                turns++;
            }

            if (!pFighter.IsAlive) playerDeaths++;
            if (!oFighter.IsAlive) orcDeaths++;
        }

        // Player should win the majority of 1v1 fights with starting gear
        double playerWinRate = (double)orcDeaths / simulations;
        Assert.That(playerWinRate, Is.GreaterThan(0.55),
            $"Player should win >55% of 1v1 fights vs orc. Win rate: {playerWinRate:P1}");

        // But player shouldn't be invincible — some deaths expected
        Assert.That(playerDeaths, Is.GreaterThan(0),
            "Player should die occasionally (confirms orc has real threat potential)");
    }
}
