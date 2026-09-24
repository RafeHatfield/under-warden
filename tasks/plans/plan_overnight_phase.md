# Overnight Build Phase: 5 Systems

## Current State
- Status: ✅ ALL PHASES COMPLETE
- Last updated: 2026-04-11
- 1040 tests total (1039 pass, 1 skip), 0 failures

## Status Summary

| Phase | System | Status |
|-------|--------|--------|
| 1 | Depth Boons (Player Progression) | ✅ complete (16 tests) |
| 2 | Wave 4 Monsters (wraith, lich, troll_ancient, greater_slime, plague_zombie) | ✅ complete (5 YAML defs, 8 MonsterDef fields) |
| 3 | Life Drain + Soul Bolt + Command the Dead + Death Siphon | ✅ complete (14 tests) |
| 4 | Lich AI | ✅ complete (built with Phase 3) |
| 5 | Identity Scenario Tests (wave 4 monsters) | ✅ complete (22 tests) |

## IMPORTANT: Pre-build Correction

The original request listed "Rings", "Monster Knowledge", and "Spell Phase 3" as unbuilt. These are **already complete**:
- **Rings**: 10 Phase 1 rings fully implemented, 29 tests in `tests/Core/RingTests.cs`, equip/unequip/floor-transition all wired
- **Monster Knowledge**: `src/Logic/Knowledge/` has MonsterKnowledgeSystem, MonsterKnowledgeEntry, MonsterInfoView, ItemInspectView. Wired into TurnController and GameState. Tests in `tests/Core/MonsterKnowledgeTests.cs` and `tests/Core/ItemInspectTests.cs`. Presentation layer has InspectPanel and LongPressDetector.
- **Spell Phase 3**: All single-target spells implemented in SpellResolver. Targeting UI exists (TargetingState, TargetingOverlay, InputHandler routing).

The 5 phases below cover what is genuinely unbuilt.

---

## Phase 1: Depth Boons (Player Progression)

### Overview
Fixed boon table, depths 1-5. One boon per depth, automatically applied on first arrival. No UI selection, no RNG. Exact port of PoC `balance/depth_boons.py`.

### Boon Table (from PoC -- DO NOT CHANGE)

| Depth | Boon ID | Display Name | Effect |
|-------|---------|--------------|--------|
| 1 | `fortitude_10` | Fortitude | +10 max HP; immediate heal for 10 HP |
| 2 | `accuracy_1` | Keen Eye | +2 accuracy |
| 3 | `defense_1` | Iron Skin | +1 base defense |
| 4 | `damage_1` | Cruel Blow | +1 minimum damage |
| 5 | `resilience_5` | Resilience | +10 max HP; immediate heal for 10 HP |

### CRITICAL: Fighter.BaseMaxHp is read-only

`Fighter.BaseMaxHp` is a get-only property (init in constructor). The boon system needs to add +10 to max HP for fortitude_10 and resilience_5. The pattern already exists: `Fighter.RingMaxHpBonus` is a mutable field added to MaxHp.

**Solution: Add `BoonMaxHpBonus` field to Fighter.**

```csharp
// In src/Logic/Combat/Fighter.cs
public int BoonMaxHpBonus { get; set; }

// Update MaxHp property:
public int MaxHp => BaseMaxHp + ConstitutionMod + RingMaxHpBonus + BoonMaxHpBonus;
```

### Files to Create

#### 1. `config/depth_boons.yaml`
```yaml
depth_boons:
  1: {id: fortitude_10, display_name: "Fortitude", description: "+10 max HP (heals 10 HP immediately)", hp_bonus: 10, immediate_heal: 10}
  2: {id: accuracy_1, display_name: "Keen Eye", description: "+2 accuracy (improves to-hit chance)", accuracy_bonus: 2}
  3: {id: defense_1, display_name: "Iron Skin", description: "+1 defense (reduces damage taken)", defense_bonus: 1}
  4: {id: damage_1, display_name: "Cruel Blow", description: "+1 minimum damage", min_damage_bonus: 1}
  5: {id: resilience_5, display_name: "Resilience", description: "+10 max HP (heals 10 HP immediately)", hp_bonus: 10, immediate_heal: 10}
```

#### 2. `src/Logic/Balance/BoonDefinition.cs`
```csharp
namespace UnderWarden.Logic.Balance;

/// <summary>
/// Immutable definition of a single depth boon. Loaded from config/depth_boons.yaml.
/// All numeric fields default to 0 (no effect). A boon with all zeros is valid (no-op).
/// </summary>
public sealed record BoonDefinition(
    string BoonId,
    string DisplayName,
    string Description,
    int HpBonus = 0,
    int ImmediateHeal = 0,
    int AccuracyBonus = 0,
    int DefenseBonus = 0,
    int MinDamageBonus = 0
);
```

#### 3. `src/Logic/Balance/DepthBoonConfig.cs`
```csharp
using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// YAML deserialization class for config/depth_boons.yaml.
/// Maps depth (int) to boon definition.
/// </summary>
public sealed class DepthBoonConfig
{
    [YamlMember(Alias = "depth_boons")]
    public Dictionary<int, DepthBoonYamlEntry> DepthBoons { get; set; } = new();
}

public sealed class DepthBoonYamlEntry
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = "";

    [YamlMember(Alias = "display_name")]
    public string DisplayName { get; set; } = "";

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";

    [YamlMember(Alias = "hp_bonus")]
    public int HpBonus { get; set; }

    [YamlMember(Alias = "immediate_heal")]
    public int ImmediateHeal { get; set; }

    [YamlMember(Alias = "accuracy_bonus")]
    public int AccuracyBonus { get; set; }

    [YamlMember(Alias = "defense_bonus")]
    public int DefenseBonus { get; set; }

    [YamlMember(Alias = "min_damage_bonus")]
    public int MinDamageBonus { get; set; }

    public BoonDefinition ToBoonDefinition() => new(
        BoonId: Id,
        DisplayName: DisplayName,
        Description: Description,
        HpBonus: HpBonus,
        ImmediateHeal: ImmediateHeal,
        AccuracyBonus: AccuracyBonus,
        DefenseBonus: DefenseBonus,
        MinDamageBonus: MinDamageBonus
    );
}
```

#### 4. `src/Logic/Balance/BoonSystem.cs`
```csharp
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Applies depth boons to the player. Stateless utility class.
/// Entry point: ApplyDepthBoonIfEligible().
/// </summary>
public static class BoonSystem
{
    /// <summary>
    /// Award a depth boon on first arrival at the given depth.
    /// Returns the boon applied, or null if none (already visited, no mapping, disabled).
    /// </summary>
    public static BoonDefinition? ApplyDepthBoonIfEligible(
        Entity player, int depth, BoonTracker tracker, IReadOnlyDictionary<int, BoonDefinition> boonTable)
    {
        if (tracker.DisableDepthBoons) return null;
        if (tracker.VisitedDepths.Contains(depth)) return null;

        tracker.VisitedDepths.Add(depth);

        if (!boonTable.TryGetValue(depth, out var boon)) return null;

        ApplyBoon(player, boon);
        tracker.BoonsApplied.Add(boon.BoonId);
        return boon;
    }

    /// <summary>
    /// Apply a boon to a player entity. Mutates Fighter fields.
    /// Raises InvalidOperationException if player has no Fighter.
    /// </summary>
    public static void ApplyBoon(Entity player, BoonDefinition boon)
    {
        var fighter = player.Require<Fighter>();

        if (boon.HpBonus > 0)
        {
            fighter.BoonMaxHpBonus += boon.HpBonus;
        }

        if (boon.ImmediateHeal > 0)
        {
            fighter.Hp = Math.Min(fighter.Hp + boon.ImmediateHeal, fighter.MaxHp);
        }

        if (boon.AccuracyBonus != 0)
            fighter.Accuracy += boon.AccuracyBonus;

        if (boon.DefenseBonus != 0)
            fighter.BaseDefense += boon.DefenseBonus;

        if (boon.MinDamageBonus != 0)
            fighter.DamageMin += boon.MinDamageBonus;
    }
}
```

#### 5. `src/Logic/ECS/BoonTracker.cs`
```csharp
namespace UnderWarden.Logic.ECS;

/// <summary>
/// Tracks which depths the player has visited and which boons have been applied.
/// Lives on GameState as a per-run singleton. Reset on new game.
/// </summary>
public sealed class BoonTracker
{
    public HashSet<int> VisitedDepths { get; } = new();
    public List<string> BoonsApplied { get; } = new();

    /// <summary>
    /// When true, no depth boons are awarded. Used by scenarios that need boon-free baselines.
    /// </summary>
    public bool DisableDepthBoons { get; set; }

    public void Reset()
    {
        VisitedDepths.Clear();
        BoonsApplied.Clear();
    }
}
```

#### 6. `tests/Core/DepthBoonTests.cs`

Test list (minimum):
```
- ApplyBoon_Fortitude_AddsMaxHp_And_Heals
- ApplyBoon_KeenEye_AddsAccuracy
- ApplyBoon_IronSkin_AddsDefense
- ApplyBoon_CruelBlow_AddsDamageMin
- ApplyBoon_Resilience_AddsMaxHp_And_Heals
- ApplyDepthBoon_FirstVisit_AppliesBoon
- ApplyDepthBoon_SecondVisit_NoBoon
- ApplyDepthBoon_Depth6_NoBoon (no mapping)
- ApplyDepthBoon_Disabled_NoBoon
- BoonTracker_VisitedDepths_Persists
- BoonTracker_Reset_ClearsEverything
- Fighter_BoonMaxHpBonus_AffectsMaxHp
- Fighter_BoonMaxHpBonus_StacksWithRingMaxHpBonus
- AllFiveBoons_AppliedSequentially_CorrectStats
- ContentLoader_LoadBoons_AllFivePresent
```

### Files to Modify

#### `src/Logic/Combat/Fighter.cs`
- Add `public int BoonMaxHpBonus { get; set; }` after `RingMaxHpBonus`
- Update `MaxHp` property: `BaseMaxHp + ConstitutionMod + RingMaxHpBonus + BoonMaxHpBonus`

#### `src/Logic/Core/GameState.cs`
- Add `public BoonTracker BoonTracker { get; } = new();`
- Add `public IReadOnlyDictionary<int, BoonDefinition>? BoonTable { get; set; }` (set by ContentLoader or DungeonFloorBuilder)

#### `src/Logic/Content/ContentLoader.cs`
- Add `LoadBoons(string yaml)` method that deserializes `config/depth_boons.yaml`
- Returns `Dictionary<int, BoonDefinition>`
- Wire into the content bundle or return separately (boons are not items)

#### `src/Logic/Core/DungeonFloorBuilder.cs`
- After `TurnController.ReapplyRingEffects(player)` (line ~170), add:
  ```csharp
  // Apply depth boon if this is first visit to this depth
  var boonResult = BoonSystem.ApplyDepthBoonIfEligible(player, depth, state.BoonTracker, boonTable);
  if (boonResult != null)
      // Emit or log — presentation layer will show toast
  ```
- Accept `IReadOnlyDictionary<int, BoonDefinition>?` parameter or read from state
- BoonTracker must be carried forward across floors (same instance, not recreated)

#### `src/Logic/Core/PlayerCarryForward.cs`
- After copying Fighter, copy BoonMaxHpBonus: The new Fighter constructor doesn't accept BoonMaxHpBonus, so after `newPlayer.Add(newFighter)`, set `newFighter.BoonMaxHpBonus = oldFighter.BoonMaxHpBonus`

### Acceptance Criteria
- [ ] All 5 boons apply correct stat mutations matching PoC exactly
- [ ] First visit to depth awards boon; return visit does not
- [ ] BoonMaxHpBonus persists across floor transitions via PlayerCarryForward
- [ ] BoonTracker.VisitedDepths persists across floors (same instance)
- [ ] Depth 6+ silently returns null (no crash)
- [ ] DisableDepthBoons = true prevents all boons
- [ ] `dotnet test --filter "Category!=Slow"` passes with all new tests

### Dependencies
- None. This is the first phase.

---

## Phase 2: Wave 4 Monsters (YAML + MonsterDefinition Extensions)

### Overview
Add 5 deep-dungeon monsters to entities.yaml: wraith, lich, troll_ancient, greater_slime, plague_zombie. All stats from PoC. Wraith and lich need new MonsterDefinition fields for their special abilities.

### New MonsterDefinition Fields Required

Add these YAML-mappable fields to `src/Logic/Content/MonsterDefinition.cs`:

```csharp
// ── Life Drain (wraith) ──────────────────────────────────────────────
[YamlMember(Alias = "life_drain_pct")]
public double LifeDrainPct { get; set; }

// ── Soul Bolt (lich) ─────────────────────────────────────────────────
[YamlMember(Alias = "soul_bolt_range")]
public int SoulBoltRange { get; set; }

[YamlMember(Alias = "soul_bolt_damage_pct")]
public double SoulBoltDamagePct { get; set; }

[YamlMember(Alias = "soul_bolt_cooldown_turns")]
public int SoulBoltCooldownTurns { get; set; }

// ── Command the Dead (lich) ──────────────────────────────────────────
[YamlMember(Alias = "command_the_dead_radius")]
public int CommandTheDeadRadius { get; set; }

// ── Death Siphon (lich) ──────────────────────────────────────────────
[YamlMember(Alias = "death_siphon_radius")]
public int DeathSiphonRadius { get; set; }

// ── Summon override (lich raises zombies, not the original corpse type) ──
[YamlMember(Alias = "summon_monster_id")]
public string? SummonMonsterId { get; set; }

// ── Status immunities (wraith, lich) ─────────────────────────────────
[YamlMember(Alias = "status_immunities")]
public List<string>? StatusImmunities { get; set; }
```

### YAML Definitions (from PoC -- copy exactly)

Add to `config/entities.yaml` in the monsters section, after `cultist_blademaster`:

```yaml
  # ── Wave 4: Deep Dungeon (depths 13+) ─────────────────────────────────

  plague_zombie:
    extends: zombie
    name: "Plague Zombie"
    stats:
      hp: 30
      damage_min: 4
      damage_max: 7
      xp: 45
    char: "Z"
    color: [100, 180, 80]
    tags: ["corporeal_flesh", "undead", "mindless", "low_undead", "plague_carrier", "zombie"]
    on_hit_effect: "plague"
    on_hit_effect_duration: 20
    etp_base: 40
    min_depth: 13
    depth_weights:
      - weight: 10
        min_depth: 13
      - weight: 20
        min_depth: 16
      - weight: 35
        min_depth: 19

  troll_ancient:
    extends: troll
    name: "Ancient Troll"
    stats:
      hp: 50
      power: 3
      defense: 3
      xp: 200
    char: "A"
    color: [0, 200, 0]
    inventory_size: 12
    seek_distance: 10
    speed_bonus: 0.15
    regeneration_amount: 3
    etp_base: 95
    min_depth: 15
    depth_weights:
      - weight: 5
        min_depth: 15
      - weight: 10
        min_depth: 18
      - weight: 20
        min_depth: 21

  greater_slime:
    extends: large_slime
    name: "Greater Slime"
    stats:
      hp: 80
      defense: 2
      xp: 150
      damage_min: 3
      damage_max: 7
      strength: 14
      dexterity: 6
      constitution: 16
    char: "S"
    color: [0, 150, 0]
    corrosion_chance: 0.15
    split_trigger_hp_pct: 0.35
    split_child_type: "large_slime"
    split_min_children: 2
    split_max_children: 2
    split_weights: [100]
    etp_base: 75
    min_depth: 12
    depth_weights:
      - weight: 5
        min_depth: 12
      - weight: 10
        min_depth: 15
      - weight: 20
        min_depth: 18

  wraith:
    name: "Wraith"
    stats:
      hp: 20
      power: 3
      defense: 4
      xp: 100
      damage_min: 5
      damage_max: 9
      strength: 10
      dexterity: 18
      constitution: 10
      accuracy: 3
      evasion: 4
    char: "W"
    color: [180, 180, 220]
    ai_type: "basic"
    faction: "undead"
    blocks: true
    tags: ["incorporeal", "undead", "high_undead", "no_flesh"]
    can_seek_items: false
    inventory_size: 0
    speed_bonus: 2.0
    life_drain_pct: 0.50
    leaves_corpse: false
    status_immunities: ["confusion", "slow", "fear"]
    etp_base: 65
    min_depth: 15
    depth_weights:
      - weight: 5
        min_depth: 15
      - weight: 15
        min_depth: 18
      - weight: 25
        min_depth: 21

  lich:
    name: "Lich"
    stats:
      hp: 60
      power: 2
      defense: 2
      xp: 150
      damage_min: 3
      damage_max: 6
      strength: 10
      dexterity: 14
      constitution: 14
      accuracy: 5
      evasion: 3
    char: "L"
    color: [140, 80, 180]
    ai_type: "lich"
    faction: "undead"
    blocks: true
    tags: ["undead", "high_undead", "caster", "no_flesh"]
    can_seek_items: false
    inventory_size: 0
    leaves_corpse: false
    status_immunities: ["confusion", "slow", "fear", "poison", "bleed"]
    soul_bolt_range: 7
    soul_bolt_damage_pct: 0.18
    soul_bolt_cooldown_turns: 8
    command_the_dead_radius: 6
    death_siphon_radius: 6
    raise_dead_range: 5
    raise_dead_cooldown_turns: 4
    danger_radius_from_player: 2
    preferred_distance_min: 4
    preferred_distance_max: 7
    summon_monster_id: "zombie"
    etp_base: 131
    min_depth: 18
    depth_weights:
      - weight: 3
        min_depth: 18
      - weight: 8
        min_depth: 21
      - weight: 15
        min_depth: 24
```

### Files to Create
- None (all YAML goes in existing entities.yaml)

### Files to Modify
- `src/Logic/Content/MonsterDefinition.cs` -- add life_drain_pct, soul_bolt_range, soul_bolt_damage_pct, soul_bolt_cooldown_turns, command_the_dead_radius, death_siphon_radius, summon_monster_id, status_immunities fields
- `config/entities.yaml` -- add 5 monster definitions after cultist_blademaster

### Acceptance Criteria
- [ ] `ContentLoader.LoadAll()` parses all 5 new monsters without error
- [ ] `MonsterFactory.Create("wraith")` returns entity with correct Fighter stats
- [ ] `MonsterFactory.Create("lich")` returns entity with correct Fighter stats
- [ ] `MonsterFactory.Create("plague_zombie")` works (extends zombie)
- [ ] `MonsterFactory.Create("troll_ancient")` works (extends troll, regen=3)
- [ ] `MonsterFactory.Create("greater_slime")` works (extends large_slime, split into large_slime)
- [ ] `dotnet test --filter "Category!=Slow"` passes

### Dependencies
- None (YAML definitions and field additions are independent)

---

## Phase 3: Wraith Life Drain + Lich Soul Bolt Systems

### Overview
Implement the two signature abilities: wraith life drain (heals on hit) and lich Soul Bolt (2-turn telegraph, % max HP damage). These are combat resolution hooks, not AI changes.

### 3A: Wraith Life Drain

**Mechanic (from PoC):**
- On each successful melee hit by wraith, heal wraith for `ceil(life_drain_pct * damage_dealt)`
- Healing capped at missing HP (no overheal)
- Only triggers when damage > 0
- Ward Against Drain effect blocks drain completely (deferred -- not building the ward scroll now, but leave the check hook)

**Implementation location:** `src/Logic/Core/TurnController.cs`, in `ResolveMonsterAttack` (or wherever monster melee damage is finalized).

After a monster deals damage to any target:
```csharp
// Check for life drain (component-based, no ContentBundle dependency)
if (result.Hit && result.Damage > 0)
{
    var drain = attacker.Get<LifeDrainComponent>();
    if (drain != null && drain.DrainPct > 0)
    {
        var attackerFighter = attacker.Require<Fighter>();
        int drainAmount = (int)Math.Ceiling(drain.DrainPct * result.Damage);
        int healed = attackerFighter.Heal(drainAmount);
        if (healed > 0)
            events.Add(new LifeDrainEvent { ActorId = attacker.Id, TargetId = target.Id, Amount = healed });
    }
}
```

**New event:** `LifeDrainEvent` in `src/Logic/Core/TurnEvent.cs`
```csharp
public sealed class LifeDrainEvent : TurnEvent
{
    public int Amount { get; init; }
    public int TargetId { get; init; }
}
```

### 3B: Lich Soul Bolt System

**Mechanic (from PoC lich_ai.py):**
1. **Charge turn**: Lich starts channeling. Apply `ChargingSoulBoltEffect` (1-turn duration marker). Message: "The Lich channels dark energy..."
2. **Resolve turn**: If player still in LOS and within `soul_bolt_range` (7), fire Soul Bolt for `ceil(soul_bolt_damage_pct * target.MaxHp)` damage. Set cooldown to `soul_bolt_cooldown_turns` (8).
3. If LOS is broken or player moves out of range during charge turn, bolt fizzles (charge wasted).
4. Soul Ward (deferred): if player has SoulWardEffect, reduce damage by 70% and convert remainder to SoulBurnEffect DOT.

**New files:**

#### `src/Logic/Combat/StatusEffects/ChargingSoulBoltEffect.cs`
```csharp
namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Marker effect on lich: "charging Soul Bolt". Duration = 1 turn.
/// On the lich's NEXT turn, if still present, Soul Bolt resolves.
/// Removed after resolution or fizzle.
/// </summary>
public sealed class ChargingSoulBoltEffect : IComponent
{
    public Entity? Owner { get; set; }
    public int RemainingTurns { get; set; } = 1;
}
```

#### `src/Logic/ECS/LichAiComponent.cs`
```csharp
namespace UnderWarden.Logic.ECS;

/// <summary>
/// Lich-specific AI state. Extends necromancer behavior with Soul Bolt.
/// Attached by MonsterFactory when ai_type is "lich".
/// </summary>
public sealed class LichAiComponent : IComponent
{
    public Entity? Owner { get; set; }

    // Soul Bolt
    public int SoulBoltRange { get; set; } = 7;
    public double SoulBoltDamagePct { get; set; } = 0.18;
    public int SoulBoltCooldownTurns { get; set; } = 8;
    public int SoulBoltCooldownRemaining { get; set; }

    // Command the Dead
    public int CommandTheDeadRadius { get; set; } = 6;

    // Death Siphon
    public int DeathSiphonRadius { get; set; } = 6;

    // Summon override
    public string SummonMonsterId { get; set; } = "zombie";
}
```

#### `src/Logic/Combat/SoulBoltResolver.cs`
```csharp
namespace UnderWarden.Logic.Combat;

/// <summary>
/// Resolves Soul Bolt damage. Called by LichAI when the bolt resolves.
/// </summary>
public static class SoulBoltResolver
{
    /// <summary>
    /// Calculate and apply Soul Bolt damage.
    /// Returns damage dealt (0 if target has no Fighter).
    /// </summary>
    public static int Resolve(Entity lich, Entity target, double damagePct, GameState state, List<TurnEvent> events)
    {
        var targetFighter = target.Get<Fighter>();
        if (targetFighter == null) return 0;

        int baseDamage = (int)Math.Ceiling(damagePct * targetFighter.MaxHp);

        // TODO: Soul Ward check (deferred)
        // if (target.Has<SoulWardEffect>()) { upfront = ceil(baseDamage * 0.30); ... }

        targetFighter.TakeDamage(baseDamage);

        events.Add(new SoulBoltEvent
        {
            ActorId = lich.Id,
            TargetId = target.Id,
            Damage = baseDamage,
        });

        if (!targetFighter.IsAlive)
        {
            events.Add(new DeathEvent { ActorId = target.Id, KillerId = lich.Id });
        }

        return baseDamage;
    }
}
```

**New event:** `SoulBoltEvent` in `src/Logic/Core/TurnEvent.cs`
```csharp
public sealed class SoulBoltEvent : TurnEvent
{
    public int TargetId { get; init; }
    public int Damage { get; init; }
}
```

### 3C: Command the Dead (Passive Aura)

**Mechanic:** Allied undead within `command_the_dead_radius` (6) of the lich get +1 to-hit.

**Implementation:** In `CombatResolver` or `HitModel`, when calculating attack roll for a monster:
- If the attacking monster has faction "undead", check if any alive lich exists within radius 6
- If yes, add +1 to the attack roll

This should be a lightweight check in the hit calculation, not a persistent buff.

```csharp
// In HitModel or CombatResolver, when calculating monster attack bonus:
static int GetCommandTheDeadBonus(Entity attacker, GameState state)
{
    if (attacker.Get<SpeciesTag>()?.TypeId == "lich") return 0; // Lich doesn't buff itself
    var tags = state.ContentBundle?.Monsters.GetValueOrDefault(attacker.Get<SpeciesTag>()?.TypeId ?? "")?.Tags;
    if (tags == null || !tags.Contains("undead")) return 0;

    foreach (var monster in state.Monsters)
    {
        if (!monster.Require<Fighter>().IsAlive) continue;
        var lichComp = monster.Get<LichAiComponent>();
        if (lichComp == null) continue;

        double dist = Math.Sqrt(Math.Pow(attacker.X - monster.X, 2) + Math.Pow(attacker.Y - monster.Y, 2));
        if (dist <= lichComp.CommandTheDeadRadius)
            return 1; // +1 to-hit
    }
    return 0;
}
```

### 3D: Death Siphon (Passive Heal)

**Mechanic:** When an allied undead dies within `death_siphon_radius` (6) of the lich, lich heals 2 HP.

**Implementation:** In `TurnController`, after a monster death is finalized:
```csharp
// Check if dead monster was undead and any lich is in range
if (deadMonster has "undead" tag)
{
    foreach alive lich within death_siphon_radius:
        lich.Fighter.Heal(2)
        events.Add(new DeathSiphonEvent { LichId = lich.Id, Amount = 2 });
}
```

### Files to Create
- `src/Logic/ECS/LifeDrainComponent.cs` -- simple component with `DrainPct` double
- `src/Logic/Combat/StatusEffects/ChargingSoulBoltEffect.cs`
- `src/Logic/ECS/LichAiComponent.cs`
- `src/Logic/Combat/SoulBoltResolver.cs`
- `tests/Core/LifeDrainTests.cs`
- `tests/Core/SoulBoltTests.cs`

### Files to Modify
- `src/Logic/Core/TurnEvent.cs` -- add LifeDrainEvent, SoulBoltEvent, DeathSiphonEvent
- `src/Logic/Core/TurnController.cs` -- add life drain hook in monster attack resolution
- `src/Logic/Combat/HitModel.cs` or `CombatResolver.cs` -- add Command the Dead bonus
- `src/Logic/Content/MonsterFactory.cs` -- attach LifeDrainComponent when `def.LifeDrainPct > 0`, attach LichAiComponent for ai_type "lich"

### Test List

**`tests/Core/LifeDrainTests.cs`:**
```
- LifeDrain_HealsWraithFor50PercentOfDamage
- LifeDrain_CapsAtMissingHp
- LifeDrain_NoDamage_NoHeal
- LifeDrain_ZeroPct_NoHeal
- LifeDrain_EmitsLifeDrainEvent
```

**`tests/Core/SoulBoltTests.cs`:**
```
- SoulBolt_DamageIs18PercentOfMaxHp
- SoulBolt_CeilRounding
- SoulBolt_EmitsSoulBoltEvent
- SoulBolt_KillsTarget_EmitsDeathEvent
- CommandTheDead_UndeadAllyGetsPlus1ToHit
- CommandTheDead_LichDoesNotBuffSelf
- CommandTheDead_OutOfRadius_NoBonus
- DeathSiphon_LichHeals2WhenAllyDies
- DeathSiphon_OutOfRadius_NoHeal
```

### Acceptance Criteria
- [ ] Wraith heals for ceil(50% * damage) on each hit
- [ ] Life drain does not overheal
- [ ] Soul Bolt deals ceil(18% * target.MaxHp) damage
- [ ] Command the Dead gives +1 to-hit to undead allies in radius
- [ ] Death Siphon heals lich 2 HP when undead ally dies in radius
- [ ] All new events are emitted correctly
- [ ] `dotnet test --filter "Category!=Slow"` passes

### Dependencies
- Phase 2 (MonsterDefinition fields and YAML entries must exist)

---

## Phase 4: Lich AI

### Overview
LichAI extends NecromancerAI with Soul Bolt charge/resolve cycle. Priority order:
1. If charging Soul Bolt: try to resolve
2. If Soul Bolt off cooldown + player in range/LOS: start charge
3. Fall through to NecromancerAI behavior (raise dead, corpse seeking, hang-back)

### New File: `src/Logic/AI/LichAI.cs`

```csharp
namespace UnderWarden.Logic.AI;

/// <summary>
/// Lich AI: Soul Bolt (2-turn telegraph) + necromancer corpse economy.
///
/// Priority (from PoC lich_ai.py):
///   1. If charging (has ChargingSoulBoltEffect): resolve if target still in LOS+range, else fizzle
///   2. If Soul Bolt off cooldown: start charge if player in LOS+range
///   3. Fallback to NecromancerAI (raise dead, seek corpse, retreat, approach)
/// </summary>
public static class LichAI
{
    public static MonsterAction Decide(Entity lich, GameState state)
    {
        var lichComp = lich.Get<LichAiComponent>();
        if (lichComp == null) return NecromancerAI.Decide(lich, state);

        // Tick Soul Bolt cooldown
        if (lichComp.SoulBoltCooldownRemaining > 0)
            lichComp.SoulBoltCooldownRemaining--;

        BasicMonsterAI.UpdateAwareness(lich, state.Player, state);
        var alerted = lich.Get<AlertedState>();
        if (alerted == null) return MonsterAction.Wait();

        var isCharging = lich.Has<ChargingSoulBoltEffect>();
        var target = state.Player;
        double dist = /* euclidean distance lich to player */;
        bool hasLos = state.Map.HasLineOfSight(lich.X, lich.Y, target.X, target.Y);

        // Priority 1: Resolve charging Soul Bolt
        if (isCharging)
        {
            if (hasLos && dist <= lichComp.SoulBoltRange)
            {
                lich.Remove<ChargingSoulBoltEffect>();
                lichComp.SoulBoltCooldownRemaining = lichComp.SoulBoltCooldownTurns;
                return MonsterAction.SoulBolt(target.Id);
            }
            else
            {
                // Fizzle: remove charge, continue to other actions
                lich.Remove<ChargingSoulBoltEffect>();
            }
        }

        // Priority 2: Start Soul Bolt charge
        if (lichComp.SoulBoltCooldownRemaining == 0 && !isCharging)
        {
            // Check silence (if SilencedEffect exists, skip)
            if (!lich.Has<SilencedEffect>() && hasLos && dist <= lichComp.SoulBoltRange)
            {
                lich.Add(new ChargingSoulBoltEffect());
                return MonsterAction.Channel("Soul Bolt");
            }
        }

        // Priority 3: Fall through to necromancer behavior
        return NecromancerAI.Decide(lich, state);
    }
}
```

### New MonsterAction Variants

Add to `src/Logic/AI/MonsterAction.cs`:
```csharp
public static MonsterAction SoulBolt(int targetId) => new(ActionKind.SoulBolt, targetId: targetId);
public static MonsterAction Channel(string abilityName) => new(ActionKind.Channel, abilityName: abilityName);
```

Add to `ActionKind` enum:
```csharp
SoulBolt,
Channel,
```

### TurnController Resolution

In `TurnController.ResolveMonsterAction` (the method that dispatches on MonsterAction.Kind):

```csharp
case ActionKind.SoulBolt:
    SoulBoltResolver.Resolve(monster, state.Player, lichComp.SoulBoltDamagePct, state, events);
    break;

case ActionKind.Channel:
    events.Add(new ChannelEvent { ActorId = monster.Id, AbilityName = action.AbilityName });
    break;
```

### MonsterFactory Wiring

In `src/Logic/Content/MonsterFactory.cs`, when processing ai_type:
```csharp
case "lich":
    entity.Add(new NecromancerAiComponent
    {
        RaiseRange = def.RaiseDeadRange,
        RaiseCooldown = def.RaiseDeadCooldownTurns,
        DangerRadius = def.DangerRadiusFromPlayer,
        PreferredDistanceMin = def.PreferredDistanceMin,
        PreferredDistanceMax = def.PreferredDistanceMax,
    });
    entity.Add(new LichAiComponent
    {
        SoulBoltRange = def.SoulBoltRange,
        SoulBoltDamagePct = def.SoulBoltDamagePct,
        SoulBoltCooldownTurns = def.SoulBoltCooldownTurns,
        CommandTheDeadRadius = def.CommandTheDeadRadius,
        DeathSiphonRadius = def.DeathSiphonRadius,
        SummonMonsterId = def.SummonMonsterId ?? "zombie",
    });
    break;
```

### Files to Create
- `src/Logic/AI/LichAI.cs`
- `tests/Core/LichAITests.cs`

### Files to Modify
- `src/Logic/AI/MonsterAction.cs` -- add SoulBolt, Channel action kinds + fields
- `src/Logic/AI/MonsterAI.cs` -- add case for ai_type "lich" dispatching to LichAI.Decide
- `src/Logic/Core/TurnController.cs` -- add SoulBolt and Channel resolution
- `src/Logic/Content/MonsterFactory.cs` -- attach LichAiComponent + NecromancerAiComponent for "lich"
- `src/Logic/Core/TurnEvent.cs` -- add ChannelEvent

### Test List

**`tests/Core/LichAITests.cs`:**
```
- LichAI_OffCooldown_PlayerInRange_StartsCharge
- LichAI_Charging_PlayerInRange_ResolvesSoulBolt
- LichAI_Charging_PlayerOutOfRange_Fizzles
- LichAI_Charging_NoLOS_Fizzles
- LichAI_OnCooldown_FallsToNecromancer
- LichAI_Silenced_CannotStartCharge
- LichAI_NoPlayerAwareness_Waits
- MonsterFactory_Lich_HasBothComponents
```

### Acceptance Criteria
- [ ] Lich starts charging when player in range + LOS + off cooldown
- [ ] Lich resolves bolt next turn if conditions still met
- [ ] Bolt fizzles if player breaks LOS or moves out of range
- [ ] Lich falls through to NecromancerAI after bolt or when on cooldown
- [ ] Silence blocks charge start
- [ ] MonsterFactory creates lich with both NecromancerAiComponent and LichAiComponent
- [ ] `dotnet test --filter "Category!=Slow"` passes

### Dependencies
- Phase 2 (YAML + MonsterDefinition)
- Phase 3 (SoulBoltResolver, LichAiComponent, ChargingSoulBoltEffect)

---

## Phase 5: Wave 4 Identity Scenario Tests

### Overview
Create scenario YAML files and C# test classes that verify each wave 4 monster's identity mechanics work correctly. These are functional integration tests, not harness runs.

### Scenario Files to Create

#### `config/testing/test_wraith_identity.yaml`
```yaml
scenario_id: wraith_identity
name: "Wraith Identity Kit"
description: "Validate wraith life drain and speed mechanics."
depth: 3
mode: arena
room:
  width: 13
  height: 13
player:
  position: [3, 6]
  hp: 60
  defense: 2
  power: 0
  damage_min: 8
  damage_max: 12
  accuracy: 5
monsters:
  - type: wraith
    position: [9, 6]
items: []
turn_limit: 100
```

#### `config/testing/test_lich_identity.yaml`
```yaml
scenario_id: lich_identity
name: "Lich Identity Kit"
description: "Validate lich Soul Bolt, Command the Dead, Death Siphon."
depth: 5
mode: arena
room:
  width: 17
  height: 17
player:
  position: [3, 8]
  hp: 60
  defense: 2
  power: 0
  damage_min: 8
  damage_max: 12
  accuracy: 5
monsters:
  - type: lich
    position: [13, 8]
  - type: skeleton
    position: [11, 8]
items: []
turn_limit: 200
```

#### `config/testing/test_greater_slime_identity.yaml`
```yaml
scenario_id: greater_slime_identity
name: "Greater Slime Identity Kit"
description: "Validate greater slime splits into large_slimes."
depth: 3
mode: arena
room:
  width: 13
  height: 13
player:
  position: [3, 6]
  hp: 80
  defense: 2
  power: 0
  damage_min: 10
  damage_max: 15
  accuracy: 5
monsters:
  - type: greater_slime
    position: [9, 6]
items: []
turn_limit: 100
```

### Test File: `tests/Core/Wave4IdentityTests.cs`

```
- Wraith_LifeDrain_HealsOnHit
- Wraith_Speed2x_GetsBonusAttacks
- Wraith_LeavesNoCorpse
- Wraith_StatusImmunities_BlockConfusionSlowFear (if status_immunities wired)
- Lich_SoulBolt_DealsDamageBasedOnMaxHp
- Lich_SoulBolt_ChargeAndResolve_TwoTurns
- Lich_CommandTheDead_AlliedUndeadGetBonus
- Lich_DeathSiphon_HealsOnAllyDeath
- Lich_FallsToNecromancerBehavior
- Lich_LeavesNoCorpse
- PlagueZombie_AppliesPlagueOnHit
- PlagueZombie_ExtendsZombie
- TrollAncient_Regen3PerTurn
- TrollAncient_ExtendsTroll
- GreaterSlime_SplitsIntoLargeSlimes
- GreaterSlime_SplitAt35PercentHp
- ContentLoader_AllWave4MonstersLoad
```

### Files to Create
- `config/testing/test_wraith_identity.yaml`
- `config/testing/test_lich_identity.yaml`
- `config/testing/test_greater_slime_identity.yaml`
- `tests/Core/Wave4IdentityTests.cs`

### Acceptance Criteria
- [ ] All scenario YAML files parse without error
- [ ] All identity tests pass
- [ ] Tests verify core mechanics (life drain, soul bolt, split, plague on-hit, regen)
- [ ] `dotnet test --filter "Category!=Slow"` passes with all new tests

### Dependencies
- Phase 2 (monster definitions)
- Phase 3 (life drain, soul bolt)
- Phase 4 (lich AI)

---

## Build Order (Critical)

```
Phase 1: Depth Boons          (independent, no prerequisites)
Phase 2: Wave 4 YAML + Fields (independent, no prerequisites)
   -- Phases 1 and 2 can run in parallel --
Phase 3: Life Drain + Soul Bolt (requires Phase 2)
Phase 4: Lich AI               (requires Phase 2 + 3)
Phase 5: Identity Tests        (requires Phase 2 + 3 + 4)
```

## Risks and Decisions

### R1: Fighter.BaseMaxHp is read-only
**Decision:** Add `BoonMaxHpBonus` (same pattern as `RingMaxHpBonus`). This keeps the constructor clean and makes boon HP bonuses explicitly reversible if needed.

### R2: HasLineOfSight for Soul Bolt
`GameMap.IsVisible(x,y)` only checks player FOV, not arbitrary LOS. Need a `HasLineOfSight(x1,y1,x2,y2)` check. `SpellResolver.BresenhamLine()` exists but is `private static`. Options:
- (A) Extract BresenhamLine to a utility class (e.g. `src/Logic/Map/LineOfSight.cs`) and add `HasLineOfSight` that walks the line checking for opaque tiles
- (B) Add `HasLineOfSight` directly to GameMap using Bresenham
- **Recommended: (A)** -- new `LineOfSight.HasClearPath(GameMap, x1, y1, x2, y2)` utility. SpellResolver can be refactored to use it too, but that's optional cleanup.
- For the lich, a simpler heuristic is acceptable for Phase 4: use player FOV visibility as a proxy (if lich is in player's FOV, player is in lich's LOS). This is what the PoC does (`map_is_in_fov`). Check `state.Map.IsVisible(lich.X, lich.Y)` -- if the lich tile is visible to the player, lich has LOS to the player.

### R3: Status Immunities
The `status_immunities` field on MonsterDefinition needs wiring into the status effect application path. This is a cross-cutting concern. For the overnight build, implement a simple check: before applying any status effect to a monster, check its definition's `status_immunities` list. If the effect name is in the list, skip application.

### R4: MonsterAction needs additional fields
`MonsterAction` may need new fields: `TargetId` (for SoulBolt target), `AbilityName` (for Channel). Check the existing `MonsterAction` struct before adding. It may already have a `TargetId` field. Use the pattern that exists.

### R5: ContentBundle is NOT on GameState
`GameState` does NOT have a ContentBundle accessor. Life drain cannot use runtime definition lookup.

**Required approach for life drain:** Add a `LifeDrainComponent` with a single `DrainPct` field, attached by MonsterFactory when `def.LifeDrainPct > 0`. Then TurnController checks `var drain = attacker.Get<LifeDrainComponent>()`. This is the same pattern used for other per-entity config (NecromancerAiComponent, SkirmisherComponent, etc.).

```csharp
// src/Logic/ECS/LifeDrainComponent.cs
public sealed class LifeDrainComponent : IComponent
{
    public Entity? Owner { get; set; }
    public double DrainPct { get; set; }
}
```

MonsterFactory attachment:
```csharp
if (def.LifeDrainPct > 0)
    entity.Add(new LifeDrainComponent { DrainPct = def.LifeDrainPct });
```

### R6: BoonTracker must survive floor transitions
`GameState` is recreated per floor (DungeonFloorBuilder creates new GameState). BoonTracker must be either:
- (A) Passed as a parameter to Build() and set on the new GameState, or
- (B) Extracted from the old GameState before transition and injected into the new one

Same pattern as IdentificationRegistry and AppearancePool (already handled this way in DungeonFloorBuilder.Build). **Follow the same pattern.**

### R7: Plague on-hit case missing from ResolveOnHitEffect

`TurnController.ResolveOnHitEffect` (around line 1394) has a switch on `onHit.EffectType` with cases for "poison", "slowed", "burning". **"plague" is not wired.** `PlagueEffect` exists in `src/Logic/Combat/StatusEffects/PlagueEffect.cs` and is processed by `StatusEffectProcessor`. The builder must add:

```csharp
case "plague":
    StatusEffectProcessor.ApplyEffect<PlagueEffect>(target, onHit.Duration);
    break;
```

This is required for `plague_zombie` to function. Add it in Phase 2 alongside the YAML definition, before the identity tests in Phase 5.
