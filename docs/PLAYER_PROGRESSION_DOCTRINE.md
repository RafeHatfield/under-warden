# Player Progression Doctrine — Depth Boons

_Last verified: 2026-07-12 against commit 86b6f10_

## Status: Design Specification (No Implementation)

This document defines the player progression model for The Under-Warden.
It is doctrine only. No code, YAML, or numeric tuning is included.

---

## 1. Design Philosophy

### 1.1 Why Not XP Grinding

Traditional roguelike XP systems create a fundamental tension with
deterministic balance:

- **Grind incentive.** When power comes from kill count, the optimal strategy
  is to farm weak enemies on safe floors. This is boring and rewards patience
  over skill.
- **Variance amplification.** A lucky early floor that yields extra kills
  compounds into a snowball. A bad floor that forces retreat compounds into a
  death spiral. XP makes run variance worse, not better.
- **Untestable scaling.** If the player's power at depth 5 depends on how
  many enemies they killed on depths 1–4, the scenario harness cannot
  produce stable baselines. Every scenario would need to simulate a full
  run history, defeating the purpose of isolated depth probes.
- **Stair abuse.** Any system where revisiting content yields power creates
  an exploit: descend, kill, ascend, repeat. Preventing this requires
  either diminishing returns (complex, fragile) or hard caps (feels
  arbitrary).

YARL's core loop is: descend, face calibrated pressure, make tactical
decisions, survive or die. XP undermines every part of this.

### 1.2 Why Depth-Based Progression Fits YARL

YARL already uses depth as the primary difficulty axis:

- Monster stats scale by depth band.
- Scenarios are tagged by depth for pressure modeling.
- Encounter composition changes with depth (orcs -> mixed -> undead).
- The dungeon is the clock. Depth is the only axis that always advances.

Tying player progression to depth means:

- **Deterministic.** At depth N, the player has exactly the boons from
  depths 1 through N-1. No variance from kill count or loot RNG.
- **Testable.** A scenario at depth 5 can assume a known boon budget.
  The harness can grant a fixed boon set for isolated testing.
- **Non-farmable.** You get one boon per depth, once. There is nothing
  to grind.
- **Pacing-aligned.** The player gets stronger at the same rate the
  dungeon gets harder. The pressure model controls both curves.

### 1.3 Options Over Inflation

The primary goal of progression is to give the player **new options**, not
bigger numbers. A player at depth 7 should have more tactical verbs than a
player at depth 2 — not just +15 HP and +3 damage.

This is the same principle behind Oaths: the Oath of Embers
doesn't make you strictly stronger, it makes you play differently. Depth
Boons extend this philosophy across the full run.

Flat stat increases are not banned, but they are **rare, large, and
mathematically justified**. A +2 HP boon is meaningless noise. A +10 HP
boon shifts H_MP by a measurable amount and must be validated against the
pressure model. Small stat boons are forbidden because they are
untestable.

### 1.4 Connection to Existing Systems

| System | Relationship to Depth Boons |
|--------|-----------------------------|
| **Depth Scaling** | Monsters get harder by depth. Boons are the player-side answer. Both curves must be co-designed. |
| **Pressure Model** | H_PM and H_MP invariants constrain what boons are permissible at each depth band. |
| **Oaths** | Oaths define run identity at the start. Boons refine and specialize identity as the run progresses. |
| **Ranged Doctrine** | Boons may unlock ranged-specific options (e.g., extra ammo slot), but must not invalidate the range band system. |
| **Skirmisher AI** | Anti-kiting enemies exist to punish passive play. Boons must not trivialize this pressure. |

---

## 2. Core Mechanic: Depth Boons

### 2.1 Definition

A **Depth Boon** is a permanent, run-scoped enhancement granted to the
player upon first arrival at a new dungeon depth.

Properties:

- **One boon per depth, per run.** Arriving at depth 3 for the first time
  in a run triggers a boon selection. Returning to depth 3 later does not.
- **Permanent within the run.** Boons are never removed, even if the player
  ascends to a previous depth.
- **Choice-based.** The player is offered 3 boons from a curated pool and
  selects 1. The other 2 are discarded for that run.
- **Identity-shaping.** Each boon should create or reinforce a playstyle,
  not just add raw power.
- **Non-farmable.** No mechanism exists to trigger a boon more than once
  per depth per run.

### 2.2 Trigger Rule

The boon trigger is a simple state check:

```
On entering depth D:
  If visited_depths[D] is FALSE for this run:
    Set visited_depths[D] = TRUE
    Offer boon selection (3 choices from pool for depth band D)
    Player selects 1
    Apply selected boon permanently
  Else:
    No boon offered
```

The `visited_depths` flag is a **run-state boolean array**, not a counter.
It is set once and never cleared. This is the complete anti-abuse mechanism.

### 2.3 Boon Pool Curation

Boons are drawn from a **depth-band pool**, not a global pool:

| Depth Band | Pool Character |
|-----------|---------------|
| 1–2 | Foundational: basic tactical options, small resource boons |
| 3–4 | Specialization: combat modifiers that reward specific playstyles |
| 5–6 | Midgame power: meaningful stat boons become available, advanced verbs |
| 7–8 | Late-game depth: rare powerful options, synergy with existing boons |
| 9+ | Capstone: high-impact boons, potential build-defining choices |

The 3 offered boons are drawn from the pool for the entered depth's band.
The draw may be seeded from the run's RNG for determinism.

### 2.4 Why 3-Choose-1

- **Agency.** The player always has a meaningful choice.
- **Build diversity.** Different selections on the same depth create
  different runs.
- **Scenario testability.** A test can force a specific boon selection
  to validate its pressure impact in isolation.
- **Anti-optimization.** With only 3 options (not a full tree), there is
  no "solved" optimal path — the best choice depends on what you've
  already selected and what you expect to face.

---

## 3. Boon Categories

Boons fall into four categories. This section defines the categories and
their design constraints. Exact boons and numeric values are not specified
here — they require pressure model validation before implementation.

### 3.A Tactical Boons (New Verbs)

**Purpose:** Give the player a new action they did not have before.

**Design intent:** These are the most valuable boons because they expand
the decision space without inflating stats. A player with Dash can
disengage from a bad position. A player without Dash cannot. This is a
qualitative difference, not a quantitative one.

**Examples (conceptual, not final):**

- **Dash.** Once per N turns, move 2 tiles without provoking. Creates
  repositioning options against swarms.
- **Brace.** Sacrifice movement this turn to gain damage reduction until
  next turn. Rewards reading the battlefield.
- **Riposte.** After dodging an attack, automatically counter-attack.
  Rewards high-evasion builds.
- **Tactical Swap.** Switch positions with an adjacent ally (summon, etc.)
  or push an enemy 1 tile. Micro-positioning verb.

**Constraints:**

- Must not grant free damage (no "deal 5 damage as a free action").
- Must require a decision (use Dash now or save it?).
- Must be observable in scenario metrics (e.g., dash_uses_per_run).
- Cooldowns must be turn-based, not kill-based.

### 3.B Combat Rule Modifiers

**Purpose:** Change how existing combat rules work in a small, consistent
way.

**Design intent:** These modify the texture of every fight without adding
new UI elements. They reward players who understand the combat system.

**Examples (conceptual, not final):**

- **Opening Strike.** The first melee hit against any uninjured enemy
  staggers it (skip its next turn). Rewards target prioritization.
- **Extended Knockback.** Knockback effects push 1 additional tile.
  Amplifies positioning gameplay.
- **Momentum.** After killing an enemy, the next attack has advantage
  (roll 2d20, take higher). Rewards aggressive play.
- **Steady Aim.** Ranged attacks at optimal range gain +1 to hit.
  Reinforces range band discipline.

**Constraints:**

- Must not stack multiplicatively with themselves.
- Must not bypass core combat resolution (d20 roll is always required).
- Effects must be expressible as simple combat-phase modifiers.
- Must be measurable: a scenario can detect whether the modifier fired.

### 3.C Resource Boons

**Purpose:** Expand the player's resource budget in a controlled way.

**Design intent:** Resources (potions, ammo, HP) are the attrition budget.
Resource boons extend the player's runway, allowing them to survive longer
encounter sequences. They are the primary defense against attrition drift.

**Examples (conceptual, not final):**

- **Deep Pockets.** +1 maximum potion carrying capacity. More healing
  options across floors.
- **Scavenger.** Regain 1 HP per kill, capped at N per floor. Soft
  sustain that rewards engagement without being farmable.
- **Expanded Quiver.** +1 special ammo slot. More ranged tactical options.
- **Second Wind.** Once per floor, automatically heal N HP when dropping
  below 25% HP. Emergency sustain with a clear, testable trigger.

**Constraints:**

- HP regeneration must be capped per floor or per encounter to prevent
  farming via stair loops.
- Potion/ammo capacity increases must be small (+1, not +5).
- No boon should fully negate attrition — the player must still manage
  resources.
- Recovery effects must not trigger on the same floor twice (floor-scoped
  flags).

### 3.D Stat Boons (Rare and Significant)

**Purpose:** Directly increase a core stat (HP, damage, accuracy, defense).

**Design intent:** These are the most dangerous boon type because they
directly shift pressure invariants. They exist because some depth bands
require the player to have more raw survivability to face scaled monsters.
They are rare, large, and mathematically constrained.

**Examples (conceptual, not final):**

- **Fortitude.** +8 to +12 max HP. Shifts H_MP upward by a measurable
  amount.
- **Battle Hardened.** +1 base accuracy. Shifts player hit rate and
  therefore DPR_P.
- **Thick Skin.** +1 base defense. Reduces incoming damage per hit.
- **Brutal Force.** +1 to minimum weapon damage. Raises damage floor.

**Constraints:**

- **Must be large enough to measure.** A +1 HP boon is noise. A +10 HP
  boon shifts H_MP by approximately `10 / DPR_M` turns — this is
  testable.
- **Must be validated against pressure model.** Before any stat boon is
  implemented, it must be run through the depth pressure model to confirm:
  - H_MP remains within the target band for the relevant depth.
  - H_PM does not drop below the early-depth floor.
  - The midgame pressure probe scenario still passes expectations.
- **Rare.** At most 1 stat boon should appear in a pool of 3 at any
  depth. Most pools should offer 0 stat boons.
- **No micro-increments.** +1 HP, +0.5 damage, +1% hit chance — these
  are all forbidden. If a stat boon is worth offering, it is worth
  making large enough to notice and test.

---

## 4. Mathematical Constraints

This is the load-bearing section. Without these constraints, boons are
just another power creep vector.

### 4.1 Pressure Invariant Bands

The pressure model defines target bands for two key invariants:

**Player Hits-to-Kill (H_PM):** How many effective rounds the player needs
to kill one monster.

| Depth Band | Target H_PM |
|-----------|-------------|
| 1–2 | 3.5 – 4.5 |
| 3–4 | 4.0 – 5.0 |
| 5–6 | 4.5 – 5.5 |
| 7–8 | 5.0 – 6.0 |
| 9+ | 6.0 – 7.0 |

**Monster Hits-to-Kill (H_MP):** How many effective monster rounds to kill
the player.

| Depth Band | Target H_MP |
|-----------|-------------|
| 1–2 | 10 – 14 |
| 3–4 | 9 – 12 |
| 5–6 | 8 – 10 |
| 7–8 | 7 – 9 |
| 9+ | 6 – 8 |

### 4.2 Boon Constraint Rules

**Rule 1: No H_MP floor collapse.**
A boon must not reduce H_MP below the lower bound of the current depth
band's target. If a defensive boon (e.g., +HP) raises H_MP, that is
acceptable as long as it does not push H_MP above the *previous* band's
upper bound (which would make the depth feel too safe).

**Rule 2: No H_PM collapse.**
An offensive boon (e.g., +accuracy, +damage) that increases DPR_P must
not reduce H_PM below the current band's lower bound. Monsters must
still require meaningful engagement to kill.

**Rule 3: Midgame probe stability.**
The midgame pressure probe scenario (depth 4) must still pass its
expectations with any combination of boons that the player could have
accumulated by depth 4 (typically boons from depths 1, 2, and 3).

**Rule 4: Encounter damage budget.**
The average damage taken per encounter (total monster damage / kills)
must remain within a testable range. Defensive boons that reduce incoming
damage must not make encounters trivial. Offensive boons that shorten
fights must not reduce the encounter damage budget below a meaningful
floor.

### 4.3 Validation Protocol for New Boons

Before any specific boon is implemented:

1. **Define the boon's expected pressure impact.**
   - Does it affect DPR_P? DPR_M? HP_P? Hit rates?
   - Estimate the invariant shift mathematically.

2. **Create a temporary scenario.**
   - Clone the relevant depth probe scenario.
   - Grant the boon to the player in the scenario setup.
   - Run under `seed_base=1337`.

3. **Compute pressure metrics.**
   - Compute H_PM, H_MP, and damage-per-encounter with the boon active.

4. **Confirm invariants remain within band.**
   - Compare against the target bands in Section 4.1.
   - If any invariant exits its band, the boon must be revised (smaller
     magnitude, different depth band, or rejected).

5. **Test boon combinations.**
   - Simulate the worst-case synergy: what if the player selected the
     most aggressive boon at every prior depth?
   - Confirm the midgame probe still passes.

This protocol is non-negotiable. No boon ships without passing it.

---

## 5. Anti-Abuse Rules

### 5.1 Stair Abuse Prevention

**The visited_depths flag is a boolean, not a counter.**

Once set for depth D, it is never cleared, decremented, or re-checked.
There is no mechanism to trigger a boon at the same depth twice. This is
not enforced by complex logic — it is enforced by the absence of any
mechanism to reset the flag.

Ascending to depth D-1 and descending back to depth D does nothing.
The flag is already set.

### 5.2 No Kill-Based Progression

No boon, boon effect, or boon selection criterion is tied to kill count.
Kill count is a consequence of encounter design, not a measure of player
skill. Tying anything to kill count creates a farming incentive.

The one exception: resource recovery effects (e.g., "heal 1 HP per kill")
are acceptable **only** with a hard per-floor cap. The cap ensures that
whether you kill 3 enemies or 30, the maximum recovery is identical.

### 5.3 No Exponential Stacking

Boons do not multiply each other's effects. If Boon A grants +1 damage
and Boon B grants "double bonus damage," Boon B is illegal. All boon
effects are additive or conditional, never multiplicative with other boons.

**Additive stacking (allowed):**
- Boon A: +1 accuracy -> Boon B: +1 damage -> total: +1 accuracy AND +1 damage.

**Multiplicative stacking (forbidden):**
- Boon A: +20% damage -> Boon B: +20% damage -> total: 1.44x damage.

Percentage-based boons are inherently suspect and should be avoided. Flat
bonuses with hard caps are preferred.

### 5.4 No Power Loss on Ascent

If the player ascends (returns to a previous depth), all boons remain
active. This is a hard rule. Removing boons on ascent would:

- Create a "descend as fast as possible" incentive (skip content).
- Make ascending for strategic reasons (retreating to heal, exploring a
  missed branch) feel punishing.
- Complicate scenario testing (boon state depends on pathing history).

### 5.5 Summary of Forbidden Patterns

| Pattern | Why Forbidden |
|---------|--------------|
| XP from kills | Farming incentive, untestable variance |
| Boon re-acquisition via stairs | Exploit loop, violates one-per-depth rule |
| Kill-count thresholds | Farming incentive, variance amplification |
| Percentage multiplier boons | Exponential stacking risk |
| Boon removal on ascent | Punishes strategic retreat |
| Tiny stat increments (+1 HP) | Untestable, meaningless noise |
| Boons tied to item count | Hoarding incentive, inventory abuse |

---

## 6. Interaction With Existing Systems

### 6.1 Oaths

Oaths define the player's **starting identity** — the strategic posture
for the run. Boons **refine and extend** that identity as the run
progresses.

- An Oath of Embers player might select fire-synergy boons.
- An Oath of Chains player might select positioning boons.
- An Oath of Venom player might select duration boons.

Boons should **not** be Oath-locked. Any player can select any boon. But
the 3-choose-1 system naturally creates synergy pressure: a boon that
complements your Oath is usually better than a generic one.

The interaction is emergent, not enforced.

### 6.2 Special Ammo

Special ammo (net arrows, fire arrows, etc.) is a resource-limited tactical
system. Boons may interact with it:

- Resource boons might grant an additional ammo slot.
- Combat modifier boons might enhance specific ammo effects.

Boons must **not** grant infinite ammo or bypass the quiver system. The
ammo economy is a core tactical constraint.

### 6.3 Depth Scaling

Depth scaling makes monsters harder. Depth Boons make the player more
capable. These two curves must be co-designed:

- If scaling raises monster HP by 25% at depth 5, the player's accumulated
  boons by depth 5 should provide roughly equivalent capability growth —
  but through options, not raw stat inflation.
- The pressure model is the arbiter. Both curves feed into
  H_PM and H_MP, and both must stay within target bands.

### 6.4 Pressure Model

The pressure model is the **validation layer** for boons. It provides:

- **Invariant targets** that boons must not violate.
- **Measurement tools** to quantify a boon's impact before implementation.
- **Regression detection** to catch boon combinations that break balance.

Every boon is, mathematically, a perturbation to one or more pressure
invariants. The model tells us whether that perturbation is acceptable.

### 6.5 Scenario Harness

Boons must be expressible as scenario setup parameters. For any scenario
test:

```yaml
player:
  boons:
    - "dash"
    - "opening_strike"
    - "fortitude_10hp"
```

The harness must be able to grant a specific boon set to the player at
scenario start, bypassing the normal depth-trigger mechanism. This enables:

- Isolated boon testing (one boon, measure its impact).
- Combination testing (worst-case synergy stacks).
- Regression testing (does this boon break the midgame probe?).

---

## 7. Future Extensions

This section is speculative. Nothing here is committed.

### 7.1 XP Integration (If Ever)

If XP were introduced, it should function as a **boon unlock accelerator**,
not a parallel power track:

- XP could allow the player to see 4 boon choices instead of 3 at the
  next depth.
- XP could unlock a "rare" boon tier that is otherwise unavailable.
- XP must **never** directly increase stats. It only widens boon access.

### 7.2 Milestone Boons

At certain fixed depths (e.g., depth 5, depth 9), a special **milestone
boon** could be offered in addition to the normal boon. Milestone boons
would be stronger and more build-defining.

### 7.3 Lore Connection: The Heart

The dungeon's lore centers on "The Heart" — a source of power deep
within the catacombs. Depth Boons could be narratively framed as the
Heart's influence growing stronger as the player descends.

---

## 8. Prerequisites for Implementation

Before the first boon is implemented in code, the following must exist:

1. **Real pressure model data.** Actual observed H_PM and H_MP values
   from scenario runs with `seed_base=1337`, not synthetic estimates.

2. **Boon-aware scenario harness.** The harness must support granting
   specific boons to the player at scenario start via YAML configuration.

3. **At least 3 concrete boon designs** with:
   - Exact mechanical definition.
   - Predicted pressure invariant impact.
   - Validation scenario results.

4. **Boon pool design for depths 1–4.** The first implementation should
   cover early game only, where the pressure model has the most data
   and the balance risk is lowest.

5. **Run state persistence for visited_depths.** The flag array must be
   added to the run state and serialized correctly for save/load.

---

## Summary

### Why Depth Boons Over XP

XP creates grind incentives, variance amplification, and untestable
scaling. Depth Boons are deterministic (one per depth, no farming),
choice-driven (3-choose-1), and testable (scenario harness can grant
specific boons). The player gets stronger because they went deeper,
not because they killed more.

### How This Preserves Deterministic Pressure Modeling

Every boon is a measurable perturbation to pressure invariants. The
validation protocol requires that any boon be run through the pressure
model before shipping. The midgame probe scenario serves as a regression
gate. Because boon acquisition is deterministic (one per depth, not
kill-dependent), scenario harness tests can assume a known boon budget
at any depth.

### What Must Happen Before First Implementation

1. Collect real pressure model data from depth scenarios.
2. Add boon-grant capability to the scenario harness.
3. Design 3+ concrete boons with pressure validation.
4. Build the depth 1–4 boon pool.
5. Add visited_depths run-state tracking.

No boon ships without passing the pressure model validation protocol.
No exceptions.
