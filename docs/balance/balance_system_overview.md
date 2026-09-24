# Balance System Overview

_Last verified: 2026-07-12 against commit 86b6f10_

This document describes the combat and loot balancing systems in The Under-Warden. It covers the Effective Threat Points (ETP) encounter budgeting, banded loot distribution, and the pity system that prevents bad-luck streaks.

## Overview

The balance system has three main components:

1. **ETP (Effective Threat Points)** - Controls encounter difficulty via per-room monster budgets
2. **Banded Loot System** - Controls item availability and density per dungeon band (B1-B5)
3. **Pity System** - Safety net that guarantees critical items after unlucky streaks

All three systems work together to create a consistent difficulty curve while allowing for exciting variance.

---

## Effective Threat Points (ETP)

### What is ETP?

ETP measures monster threat level relative to player power at a given depth. Higher ETP = harder fight.

### Where ETP is Defined

- **Base ETP per monster:** `config/entities.yaml` -> each monster has a `base_etp` value
- **Band budgets:** `config/etp_config.yaml` -> defines per-band room budgets
- **Logic:** Balance module -> applies multipliers and computes final ETP

### ETP Modifiers

| Modifier | Description | Example |
|----------|-------------|---------|
| **Band Multiplier** | Scales ETP by depth band | B1=1.0x, B3=1.2x, B5=1.5x |
| **Elite Multiplier** | (Elite) variants get bonus ETP | +50% for elite monsters |
| **Behavior Modifiers** | Optional AI trait bonuses | +10% for "aggressive" |

### Room Budgets

Each band defines a min/max ETP budget for rooms:

| Band | Depths | Room ETP Budget |
|------|--------|-----------------|
| B1 | 1-5 | 0-60 |
| B2 | 6-10 | 20-100 |
| B3 | 11-15 | 30-150 |
| B4 | 16-20 | 40-200 |
| B5 | 21-25 | 50-300 |

### ETP Status Codes

| Status | Meaning |
|--------|---------|
| **OK** | Within budget |
| **UNDER** | Below minimum (easy room) |
| **OVER** | Exceeds maximum (violation in normal rooms) |
| **SPIKE** | Special room that's allowed to exceed budget |
| **BOSS** | Boss/miniboss room (budget exempt) |
| **EMPTY** | No monsters |
| **EXEMPT** | Explicitly exempt from budget |

---

## Room Metadata & Roles

### Room Roles

| Role | Description | ETP Handling | Pity |
|------|-------------|--------------|------|
| `normal` | Standard room | Must stay within budget | Active |
| `miniboss` | Contains miniboss | Can exceed budget | Skipped |
| `boss` | Contains boss | Can exceed budget | Skipped |
| `end_boss` | Final boss room | Can exceed budget | Skipped |
| `treasure` | Vault/treasure room | Can spike (allow_spike=True) | Skipped |
| `optional` | Side content | Normal budget | Active |

### Effects on Systems

Room metadata affects:

1. **ETP Budgeting:** Boss/treasure rooms can exceed budgets
2. **Monster Spawning:** Minibosses/bosses only spawn in appropriate rooms
3. **Pity System:** Special rooms skip pity entirely (they're meant to be swingy)

---

## Loot System

### Loot Tags

Each item has:
- **categories:** e.g., ["healing"], ["panic", "utility"]
- **band_min/band_max:** Minimum/maximum band (1-5) where item appears
- **weight:** Base spawn weight

### Item Categories

| Category | Description | Examples |
|----------|-------------|----------|
| `healing` | HP restoration | healing_potion, regeneration_potion |
| `panic` | Emergency escape | teleport_scroll, haste_scroll, blink_scroll |
| `offensive` | Direct damage | fireball_scroll, lightning_scroll, wands |
| `defensive` | Protection/buffs | shield_scroll, protection_potion |
| `utility` | Tactical/situational | confusion_scroll, identify_scroll |
| `upgrade_weapon` | Weapon improvements | enhance_weapon_scroll, swords |
| `upgrade_armor` | Armor improvements | enhance_armor_scroll, armor pieces |
| `rare` | Valuable items | All rings |
| `key` | Progression keys | bronze_key, silver_key, gold_key |

### Band Multipliers

Three multipliers control loot distribution by band:

**Item density (items per room):**
| Band | Multiplier |
|------|-----------|
| B1 | 0.35 |
| B2 | 0.45 |
| B3-B5 | 1.0 |

**Healing spawn weight:**
| Band | Multiplier |
|------|-----------|
| B1 | 0.25 |
| B2 | 0.35 |
| B3 | 1.0 |
| B4-B5 | 1.1 |

**Rare item (rings) spawn weight:**
| Band | Multiplier |
|------|-----------|
| B1 | 0.05 |
| B2 | 0.15 |
| B3 | 0.5 |
| B4 | 0.8 |
| B5 | 1.0 |

---

## Pity System

### Purpose

The pity system ensures players don't go too long without receiving critical items. It acts as a safety net — it should rarely trigger if base spawn rates are healthy.

### PityState Tracking

Counters tracked per run:
- `rooms_since_healing_drop`
- `rooms_since_panic_item`
- `rooms_since_weapon_upgrade`
- `rooms_since_armor_upgrade`

### Band-Based Thresholds

| Band | Healing | Panic | Weapon | Armor |
|------|---------|-------|--------|-------|
| B1 | 6 rooms | 7 rooms | 8 rooms | 8 rooms |
| B2 | 5 rooms | 6 rooms | 7 rooms | 7 rooms |
| B3+ | 4 rooms | 5 rooms | 6 rooms | 6 rooms |

When a counter exceeds the threshold, pity triggers and guarantees that item type.

### Pity Rules

1. **Skip special rooms:** Boss, miniboss, treasure rooms don't increment counters or trigger pity
2. **At most one pity item per room:** Priority order is healing -> panic -> weapon -> armor
3. **Counter reset:** When an item of a category spawns (naturally or via pity), that counter resets
4. **Counter increment:** When a normal room lacks a category, that counter increments

### Interpreting Pity Trigger Rates

- **5-15%** = Healthy (pity is a rare safety net)
- **0%** = Base rates generous enough pity never triggers
- **30%+** = Base rates too low; pity is doing heavy lifting
- **50%+** = Problem — pity shouldn't be main progression driver

---

## Tuning Workflow

### Step 1: Encounter Difficulty (ETP)

1. Adjust band budgets in ETP config
2. Adjust monster `base_etp` values
3. Run ETP sanity harness and verify no violations

### Step 2: Item Availability (Loot Tags)

1. Adjust `band_min`/`band_max` for when items appear
2. Adjust `weight` for spawn frequency
3. Tune band multipliers for density/healing/rares
4. Run loot sanity harness
5. Check items/room targets: B1=5-8, B2=6-9, B3-B5=8-10

### Step 3: Pity Thresholds

1. Adjust thresholds per category per band
2. Run loot sanity harness
3. Check pity trigger rates (target: 5-15%)

### Step 4: Iterate

- If pity triggers too often -> increase base spawn rates
- If pity triggers too rarely -> thresholds may be too generous
- Testing mode templates have heavy guaranteed spawns; test normal mode for balance

---

## Quick Reference

### Design Intent

- **ETP** ensures encounters are appropriately challenging per band
- **Loot tags** ensure items appear when they're useful
- **Pity** ensures bad luck never softlocks progression
- **Room metadata** allows special rooms to break the rules intentionally

The systems are designed to be tweaked via config, not code. When in doubt, run the harnesses and let the numbers guide you.
