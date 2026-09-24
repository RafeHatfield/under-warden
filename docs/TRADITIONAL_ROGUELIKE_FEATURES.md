# Traditional Roguelike Features - Design Document

_Last verified: 2026-07-12 against commit 86b6f10_

*Last Updated: October 2025 - Post Boss Fights v3.9.0*

> **Status reconciliation (2026-07-12):** This is an **aspirational design wishlist** with effort estimates and a priority matrix — it describes features to consider, not current implementation status. Since October 2025 several items on this list have shipped and the roadmap framing below is now partly stale. As of commit 86b6f10: **item stacking** (consumables + ammo), **damage-type resistance/vulnerability**, and the **trap system** (9 placed trap types) are **implemented** — do not treat their "future / N weeks" entries here as current. For authoritative current status, always defer to [systems/INDEX.md](systems/INDEX.md). Still not built: amulets, blessed/cursed items, gods, hunger/food, shops, rarity tiers.

## Executive Summary

This document outlines 35+ beloved features from classic roguelikes (NetHack, DCSS, Brogue, Caves of Qud, ADOM) that would elevate The Under-Warden from a solid roguelike to one of the best traditional roguelikes ever made.

**Current Strengths:**
- Excellent D&D-style combat system (d20, AC, dice notation)
- Strong equipment foundation (12 weapons, armor, 5 slots)
- Good spell variety (8 scrolls) with tactical depth
- Boss fights with dialogue and phases
- Loot quality system (4 rarity tiers)

**Critical Gaps:**
- **Discovery:** No item identification system (THE defining roguelike mechanic)
- **Resource Management:** No hunger/food system, limited charge-based items
- **Build Diversity:** Missing rings, amulets, resistances, blessed/cursed
- **Emergent Gameplay:** No item interactions, throwing, polymorphing
- **Meta-Progression:** No religion/god system, achievements

---

## TIER 1: Essential Roguelike Features (Quick Wins)

### 1. Item Identification System

**Impact:** CRITICAL - This is THE defining roguelike mechanic
**Effort:** 1-2 weeks
**Priority:** #1

#### Why This Matters
Every legendary roguelike has unidentified items. It creates:
- Exciting risk/reward decisions ("Should I drink this unknown potion?")
- Discovery moments that create player stories
- Replay value (potion colors randomize each game)
- Strategic resource management (identify scrolls are valuable)

#### Design
- **Scrolls:** Appear as "scroll labeled XYZZY" or "scroll labeled PRATYAVAYAH"
  - Random labels per game (20+ possible labels)
  - Once used/identified, all scrolls of that type are known
  - Identify scroll reveals one item type

- **Potions:** Appear as "blue potion" or "bubbling yellow potion"
  - 10+ colors/descriptions
  - Drinking identifies the potion type
  - Risky but necessary

- **Rings/Wands (future):** Similar system

#### Configuration Options
**IMPORTANT: Implement with DUAL toggle system for maximum flexibility**

```yaml
# MASTER TOGGLE - Can completely disable identification system
identification_system:
  enabled: true  # If false, ALL items always identified (no ID mechanic at all)

# DIFFICULTY INTEGRATION - When enabled, difficulty determines pre-ID percentages
difficulty:
  easy:
    item_identification:
      scrolls_pre_identified: 80%
      potions_pre_identified: 80%
      rings_pre_identified: 90%
      wands_pre_identified: 75%

  medium:
    item_identification:
      scrolls_pre_identified: 40%
      potions_pre_identified: 50%
      rings_pre_identified: 40%
      wands_pre_identified: 30%

  hard:
    item_identification:
      scrolls_pre_identified: 5%
      potions_pre_identified: 5%
      rings_pre_identified: 0%
      wands_pre_identified: 0%

# PROGRESSIVE LEARNING - QoL for veterans
meta_progression:
  auto_identify_after_first_win: true
  common_items_learned: true
```

#### Technical Notes
- Appearance mapping stored in game state
- Persists across save/load
- New game = new randomization (if enabled)
- Configuration file allows runtime toggle without code changes
- Easy to extend with difficulty modes later

---

### 2. Item Stacking

**Impact:** HIGH - Quality of life, standard in all roguelikes
**Effort:** 1 week
**Priority:** #2

#### Design
- Stackable: Potions, scrolls, food, ammunition (future)
- Non-stackable: Equipment, unique items
- Show count: "healing potion (x5)"
- Use one from stack
- Drop quantity selector

---

### 3. Throwing System

**Impact:** HIGH - Tactical depth, emergent gameplay
**Effort:** 1 week
**Priority:** #5

#### Why This Matters
Enables creative tactics:
- Throw healing potion at enemy (heals them, wastes potion BUT might pacify)
- Throw confusion potion to shatter on impact
- Throw daggers for ranged damage
- Throw items into lava/pits

#### Design
- Target with mouse or arrow keys
- Potions shatter on impact (apply effect to target tile)
- Weapons deal reduced damage
- Heavy items stun
- Items land on ground if missed

---

### 4. Resistance System

**Impact:** HIGH - Build diversity, equipment strategy
**Effort:** 1-2 weeks
**Priority:** #4

#### Design
- Resistance types: Fire, Cold, Poison, Electric, Acid
- Levels: None (100% damage), Resistant (50%), Immune (0%)
- Sources: Equipment, rings, potions (temporary)
- Monster resistances too

---

### 5. Scroll/Potion Variety

**Impact:** HIGH - Replay value, discovery
**Effort:** 1-2 weeks
**Priority:** #3

#### Expansion Plan
**New Scrolls (12 total, 20 target):**
- Identify, Remove Curse, Magic Mapping, Create Monster
- Enchant Weapon, Enchant Armor, Summon Demon, Genocide
- Charging, Light, Darkness, Amnesia

**New Potions (15 target):**
- Healing, Speed, Poison, Confusion, Levitation
- Strength, Detect Objects, Detect Monsters, ESP
- Blindness, Paralysis, Gain Level, Holy Water
- Unholy Water, Restore Ability

---

### 6. Vault System

**Impact:** HIGH - Memorable moments, risk/reward
**Effort:** 1-2 weeks
**Priority:** #9

#### Design
- 10-15 vault templates
- 5-10% chance per level
- Types: Treasure Vault, Monster Zoo, Trap Maze, Shrine, Armory, Library, Potion Shop

---

### 7. Secret Doors

**Impact:** MEDIUM-HIGH - Exploration rewards
**Effort:** 1 week
**Priority:** #10

#### Design
- Appear as walls until discovered
- 10% chance to find when adjacent
- Active search reveals in radius (100%)
- Lead to bonus rooms, shortcuts, vaults

---

### 8. Corpse System

**Impact:** MEDIUM - Resource management, flavor
**Effort:** 1-2 weeks
**Priority:** #15

#### Design
- Dead monsters leave corpses
- Eat corpse for effects: nutrition, temporary ability, sickness
- Corpses decay over 100-500 turns

---

## TIER 2: Major Systems (Core Depth)

### 9. Hunger/Food System - OPTIONAL - CONTROVERSIAL

**Impact:** DEBATED - Was removed from DCSS in v0.26 (2020)
**Effort:** 2-3 weeks
**Priority:** #6 (SKIP OR MAKE OPTIONAL)

**WARNING:** This is the MOST CONTROVERSIAL roguelike mechanic. DCSS removed it entirely after finding it added "tedium without depth."

**Implementation Options:**
- **Option 1: SKIP (Recommended)** - Design against grinding instead
- **Option 2: Make OPTIONAL** - Difficulty setting toggle
- **Option 3: "Buff-Only" System** - Eating gives buffs, NOT eating has NO penalty

---

### 10. Blessed/Cursed Items

**Impact:** CRITICAL - Equipment puzzle, depth
**Effort:** 2-3 weeks
**Priority:** #11

#### Design
- States: Blessed (+1 bonus), Uncursed (normal), Cursed (-1, can't unequip)
- Visual indicators: blessed (glowing), cursed (dark)
- Cursed items have UPSIDES (cursed sword: -1 AC, +3 damage)
- Can always remove (costs scroll or gold)

---

### 11. Religion/God System

**Impact:** CRITICAL - Meta-progression, replay value
**Effort:** 3-4 weeks
**Priority:** #12

#### Design
**Gods (3-5 initial):**
1. **Tyr (Law)** - Justice, blessed weapons, smite evil
2. **Loki (Chaos)** - Trickster, random polymorphs, chaos bolt
3. **Gaia (Nature)** - Earth mother, natural resistances, summon animals

---

### 12. Shop System

**Impact:** HIGH - Economy, resource conversion
**Effort:** 2-3 weeks
**Priority:** #13

#### Design
- Shops spawn on ~20% of levels
- Types: General, Weapon, Armor, Scroll, Potion, Magic
- Buy/sell economy, shopkeeper identifies items
- Shopkeepers are TOUGH if attacked

---

### 13. Wand System

**Impact:** HIGH - Reusable magic, resource management
**Effort:** 2 weeks
**Priority:** #7

#### Design
**15 Wand Types:** Fire, Cold, Lightning, Death, Polymorph, Teleportation, Slow, Speed, Digging, Striking, Wishing, Sleep, Cancellation, Create Monster, Charging

#### Mechanics
- Start with 3-10 charges
- Recharging risky (might explode)
- Some affect user if misused

---

### 14. Ring System

**Impact:** HIGH - Build customization
**Effort:** 2-3 weeks
**Priority:** #8

#### Design
**Ring Types (15 total):**
- Beneficial: Regeneration, Protection, Strength, Dexterity, See Invisible, Teleport Control, Free Action, Fire/Cold/Poison Resistance, Searching
- Mixed/Cursed: Hunger, Aggravate Monster, Random Teleportation

#### Mechanics
- 2 ring slots (left/right hand)
- Passive effects always active
- Cursed rings can't be removed

---

### 15. Amulet System

**Impact:** MEDIUM-HIGH - Power spikes
**Effort:** 2 weeks
**Priority:** #14

#### Design
**Amulet Types (10 total):**
Life Saving, Reflection, ESP, Strangulation, Unchanging, Magical Breathing, Flying, Versus Poison, Guarding, Restful Sleep

---

### 16. Trap System - NO INSTANT DEATH

**Impact:** HIGH - Danger, rewards caution
**Effort:** 2-3 weeks
**Priority:** #10

**CRITICAL:** NO instant death traps. Player death should feel earned, never random/unfair.

**Trap Types (10 total) - ALL SURVIVABLE:**
- Dart Trap, Pit Trap, Teleport Trap, Polymorph Trap, Fire Trap
- Sleeping Gas, Arrow Trap, Bear Trap, Rust Trap, Magic Trap

**Safety Rules:**
- Warning System ("You feel a draft" before pit trap)
- Passive Discovery (10% when adjacent)
- Search Command reveals all traps in radius
- No Instant Death: Even stacking traps is survivable

---

### 17. More Status Effects

**Impact:** MEDIUM-HIGH - Tactical depth
**Effort:** 2-3 weeks
**Priority:** #16

**New Effects:** Poison, Disease, Paralysis, Blindness, Levitation, Haste, Slow, Regeneration, Detect Monsters, Berserk

---

### 18. Polymorph System - NO INSTANT DEATH

**Impact:** HIGH - Unique mechanic, power fantasy
**Effort:** 3-4 weeks
**Priority:** #17

**Safety Features:**
- NO DEATH WHILE POLYMORPHED: If HP hits 0, revert to normal form
- Minimum HP: Weak forms have minimum 10 HP (never 1 HP)
- Cancel Option for risky polymorphs
- Good Forms More Common (70% neutral-to-good)

---

## TIER 3: Depth Systems (Advanced)

### 19. Wish/Genie System
Ultra-rare, player types what they want, interpretation system.

### 20. Item Interaction System
Dipping, mixing, applying. Emergent gameplay.

### 21. Unique Artifacts
10 legendary items (Excalibur, Mjolnir, etc.). Found in vaults, boss drops, quest rewards.

### 22. Digging/Tunneling
Pickaxe/spell creates passages. Escape, shortcut, bypass.

### 23. Fountain Effects
Random effects on drink. 1% wish chance. Risk/reward.

### 24. Monster Special Abilities
Nymphs steal, rust monsters corrode, liches summon, mimics disguise.

### 25. Victory Condition/Ascension
Retrieve Amulet of Yendor, return to surface. Victory screen, hall of fame, score.

### 26. Morgue Files
Character dump on death. Stats, equipment, timeline. Shareable.

### 27. Swarm Mechanics
Rats, bats, insects. Groups of 5-15, individually weak, dangerous in numbers.

---

## Implementation Priority Matrix

### Phase 1: Core Identification & QoL (Weeks 1-6)
1. Item Identification System (2 weeks)
2. Item Stacking (1 week)
3. Scroll/Potion Variety (2 weeks)
4. Resistance System (1 week)

### Phase 2: Resource Management (Weeks 7-13)
5. Throwing System (1 week)
6. Hunger/Food System (3 weeks)
7. Wand System (2 weeks)
8. Corpse System (1 week)

### Phase 3: Build Diversity (Weeks 14-20)
9. Ring System (3 weeks)
10. Amulet System (2 weeks)
11. Blessed/Cursed Items (2 weeks)

### Phase 4: World Depth (Weeks 21-30)
12. Trap System (3 weeks)
13. Vaults & Secret Doors (2 weeks)
14. Shop System (3 weeks)
15. Religion/God System (4 weeks)

### Phase 5: Advanced Mechanics (Weeks 31-45)
16. More Status Effects (3 weeks)
17. Polymorph System (4 weeks)
18. Item Interaction System (4 weeks)
19. Unique Artifacts (3 weeks)
20. Monster Special Abilities (3 weeks)

### Phase 6: Completion Systems (Weeks 46-52)
21. Victory Condition/Ascension (3 weeks)
22. Fountain Effects (1 week)
23. Digging/Tunneling (2 weeks)
24. Wish/Genie System (2 weeks)
25. Morgue Files (1 week)

---

## Success Metrics

### Depth Score Target
| System | Current | Target | Impact |
|--------|---------|--------|--------|
| Discovery | 2/10 | 10/10 | +Item ID, unknown effects, secret areas |
| Resource Management | 3/10 | 9/10 | +Hunger, wand charges, identify scrolls |
| Build Diversity | 5/10 | 9/10 | +Rings, resistances, blessed/cursed, gods |
| Emergent Gameplay | 4/10 | 9/10 | +Throwing, item interactions, polymorph |
| Memorable Moments | 6/10 | 10/10 | +Wishes, polymorph, vaults, divine intervention |

---

## Conclusion

These 35+ features represent what makes traditional roguelikes beloved by players worldwide. Implementing them in order of Impact vs Effort will transform The Under-Warden from a solid roguelike into one of the best traditional roguelikes ever made.

**Next Immediate Action:** Start with Item Identification System

The journey to becoming a legendary roguelike begins with the question: "I wonder what this blue potion does?"
