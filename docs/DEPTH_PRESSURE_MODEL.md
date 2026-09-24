# Depth Pressure Model

_Last verified: 2026-07-12 against commit 86b6f10_

## Purpose

This document describes the **Formal Pressure Model Derivation Layer** for
The Under-Warden. It explains why depth scaling needs mathematical grounding,
defines the invariants used, documents observed curves, and proposes target
curves with derived multiplier adjustments.

**No gameplay behavior is changed by this document.** This is instrumentation,
analysis, and documentation only.

---

## Problem: Why HP-Only Scaling Creates Attrition Drift

The depth scaling system primarily scales monster HP and to-hit. Damage scaling
is minimal:

| Depth | HP Mult | ToHit Mult | DMG Mult |
|-------|---------|------------|----------|
| 1-2   | 1.00    | 1.00       | 1.00     |
| 3-4   | 1.08    | 1.06       | 1.00     |
| 5-6   | 1.25    | 1.12       | 1.05     |
| 7-8   | 1.35    | 1.17       | 1.10     |
| 9+    | 1.45    | 1.22       | 1.15     |

The consequence: at deeper depths, monsters take **more hits to kill** but
don't deal proportionally **more damage per round**. This means:

- Fights get **longer** (more turns per monster kill)
- Each individual hit is not meaningfully more dangerous
- Player attrition (HP/healing resource drain) becomes the primary killer
- Tactical play is devalued: you die the same way whether you play well or poorly

This is **attrition drift** — the game becomes a resource management problem
rather than a tactical combat problem.

---

## Mathematical Framework

### Definitions

For a given scenario run with seeded RNG:

| Symbol | Definition |
|--------|-----------|
| `E[DMG_P]` | Average player damage per **landed** hit |
| `P(hit_P)` | Player hit rate (hits / total attacks) |
| `E[DMG_M]` | Average monster damage per **landed** hit |
| `P(hit_M)` | Monster hit rate (hits / total attacks) |
| `HP_P` | Player starting HP (currently 54) |
| `HP_M` | Average HP of spawned monster type (at depth, post-scaling) |

### Derived Invariants

| Invariant | Formula | Meaning |
|-----------|---------|---------|
| `DPR_P` | `E[DMG_P] * P(hit_P)` | Player **damage per round** |
| `DPR_M` | `E[DMG_M] * P(hit_M)` | Monster **damage per round** |
| `H_PM` | `HP_M / DPR_P` | **Player hits-to-kill** — how many effective rounds to kill one monster |
| `H_MP` | `HP_P / DPR_M` | **Monster hits-to-kill** — how many effective monster rounds to kill the player |
| DMG/encounter | `total_monster_dmg / kills` | Expected damage taken per encounter |
| T/kill | `total_turns / kills` | Average turns to kill one enemy |

### Key Relationships

- **H_PM rising with depth, H_MP flat** = HP-heavy scaling (attrition)
- **H_PM rising, H_MP falling** = balanced scaling
- **H_PM flat, H_MP falling** = spike lethality (dangerous)
- **H_PM/H_MP ratio** indicates attrition (> 0.6) vs lethality (< 0.3)

### Action Economy Context

When facing N monsters simultaneously:
- Effective incoming DPR = `N * DPR_M`
- Survival turns = `H_MP / N`
- At depth 2 with 3 orcs: survival ~ `H_MP / 3` turns
- At depth 5 with 8 zombies: survival ~ `H_MP / 8` turns

This is why H_MP must account for encounter size, and why pure HP scaling
(which only increases H_PM) fails to create appropriate pressure.

---

## Target Curve Proposals

### Player Hits-to-Kill (H_PM)

How many effective rounds the player needs to kill one monster.

| Depth Band | Target Range | Rationale |
|-----------|-------------|-----------|
| 1-2 | 3.5 – 4.5 | Early game: quick kills, player is learning mechanics |
| 3-4 | 4.0 – 5.0 | Minor increase: fights require some positioning |
| 5-6 | 4.5 – 5.5 | Midgame: tactical engagement required |
| 7-8 | 5.0 – 6.0 | Late: sustained pressure, resource management matters |
| 9+  | 6.0 – 7.0 | Endgame: monsters are serious threats |

### Monster Hits-to-Kill (H_MP)

How many effective monster rounds to kill the player. Lower = more dangerous.

| Depth Band | Target Range | Rationale |
|-----------|-------------|-----------|
| 1-2 | 10 – 14 | Forgiving: player can recover from several mistakes |
| 3-4 | 9 – 12  | Tightening: mistakes cost health, healing matters |
| 5-6 | 8 – 10  | Midgame: genuine threat, positioning critical |
| 7-8 | 7 – 9   | Late: every hit counts, retreat is valid |
| 9+  | 6 – 8   | Endgame: lethal, avoidance and resource planning key |

### Anti-Attrition Design Principle

Target curves are designed so that:

1. **H_PM grows gradually** (monsters get tougher, fights get slightly longer)
2. **H_MP decreases** (monsters get deadlier, pressure increases)
3. The **ratio H_PM/H_MP stays in 0.3–0.6** (balanced zone)

If H_PM grows without H_MP decreasing, the game trends toward attrition.
If H_MP drops without H_PM growing, the game trends toward spike lethality.

---

## Damage Multiplier Derivation

The `derive_required_damage_multiplier()` function computes what damage
multiplier would be needed to bring the observed H_MP into the target range.

### Mathematical Derivation

Given:
- `H_MP_observed = HP_P / DPR_M_observed`
- `H_MP_target` (midpoint of target band)

Required:
```
DPR_M_required = HP_P / H_MP_target
E[DMG_M]_required = DPR_M_required / P(hit_M)
multiplier = E[DMG_M]_required / E[DMG_M]_observed
```

The multiplier tells us: "To achieve the target H_MP at this depth, monster
damage would need to be multiplied by X."

**This multiplier is NOT applied.** It is reported for decision-making.

---

## How to Repeat This Analysis

### Step 1: Run Depth Scenarios

Run balance suite scenarios to generate metrics with deterministic seeds.

### Step 2: Extract Metrics

Use the aggregated metrics from the harness output (JSON or in-memory).

### Step 3: Compute Pressure Model

Feed scenario results into the pressure model analysis to compute H_PM, H_MP,
and damage-per-encounter with the boon active.

### Step 4: Interpret Report

The report outputs:
1. **Observed Curve Table** — raw derived metrics by depth
2. **Target Comparison** — observed vs target ranges with OK/LOW/HIGH status
3. **Multiplier Recommendations** — derived damage multipliers (not applied)
4. **Scaling Diagnosis** — whether current system is HP-heavy, balanced, or spike

---

## Scenario HP Budgets (Reference)

| Scenario | Depth | Monsters | Scaling | HP Budget/Run |
|----------|-------|----------|---------|--------------|
| depth2_orc_baseline | 2 | 3x orc_grunt (28 HP) | 1.0x | 84 |
| depth3_orc_brutal | 3 | 4x orc_grunt (28 HP) | 1.08x -> 31 HP | 124 |
| midgame_pressure_probe_orcs | 4 | 1x grunt + 1x brute + 1x shaman + 1x skirmisher | 1.08x | 127 |
| depth5_zombie | 5 | 8x zombie (24 HP) | 1.0x (zombie curve) | 192 |

---

## Boon State in Run Exports

Every `RunMetrics` result contains a `boons_applied` key listing the boon IDs
that were active during the run:

```json
{
  "turns_taken": 35,
  "player_damage_dealt": 120,
  "monster_damage_dealt": 55,
  "boons_applied": ["fortitude_10", "accuracy_1"]
}
```

`boons_applied` is always present (empty list `[]` when no boons were applied).

### Running "with boons" vs "without boons" comparisons

Use scenario YAML to control boon state for controlled A/B analysis:

**Without boons (control):**
```yaml
player:
  boons: []
  disable_depth_boons: true
```

**With explicit boons (treatment):**
```yaml
player:
  boons: ["fortitude_10"]
  disable_depth_boons: true
```

**With auto depth boons (campaign-like):**
```yaml
# omit both keys — depth boons fire automatically on arrival
```

Collect runs for each variant, then compare `player_damage_dealt` /
`monster_damage_dealt` ratios and death rates to measure the pressure shift
each boon introduces.
