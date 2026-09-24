# Balance Strategy — The Under-Warden

_Last verified: 2026-07-12 against commit 86b6f10_

**Status:** DRAFT for Rafe's review. No tuning happens until this is reviewed and aligned.
**Date:** 2026-06-08
**Purpose:** restore the strategic seat. After reading this you should know what *balanced* means for
this game, the sequence of work and where we are in it, how you'd *observe* progress, and what artifact
you'd open to see — at a glance — whether a change made the whole game better.

> **Framing, named honestly (the three reframes baked in):**
> 1. **Floors 1–24 first; the Weighing is parked.** We opened at the endgame — the part almost no one
>    reaches. The framework/metric-fix/findings are durable and waiting; the descent is the priority.
> 2. **This is the FIRST real balancing of ~19 of the 25 floors, not a re-tune.** The PoC balanced
>    floors 1–6. We carried those numbers forward when we left the PoC and have done almost no balancing
>    since, while the game grew massively. Scope is *establishing* balance, not restoring it.
> 3. **The PoC is the seed, not the standard.** Its approach worked for a 5–6-floor game. It was a
>    prototype too; it has to grow like everything else. We re-anchor to *what it did and why that
>    worked*, then evolve that thinking for the full current game.

---

# Terms — defined in our terms

The vocabulary the rest of this doc uses, defined as the code *actually* computes them (sources noted),
with how to read each one. If a term below feels fuzzy, that's a gap to close before we trust a number.

## Combat metrics (per fight, from `RunMetrics.FromRuns`)

- **ttk — time-to-kill.** Plain meaning: how many landed hits the player needs to kill a *common* monster.
  Our metric for it is **H_PM**. Lower = snappier fights; higher = grindier.
- **ttd — time-to-die.** Plain meaning: how many landed hits a monster needs to kill the player *at full HP*.
  Our metric for it is **H_MP**. Lower = lethal/fragile; higher = durable/tanky.
- **H_PM** *(hits-to-kill-monster)* — **= avg monster max HP ÷ avg player damage per landed hit.** This is
  ttk in hits. Note: it's per *landed* hit (damage-per-swing), so it does **not** include misses — a
  separate thing from how long a fight takes in turns. Read: "how many clean swings to drop one enemy."
- **H_MP** *(hits-to-kill-player)* — **= avg player max HP ÷ avg monster damage per landed hit.** This is
  ttd in hits. Also per landed hit. Read: "how many clean enemy swings to drop the player." The "monster
  damage per hit" here is the **post-reduction landed damage** (a monster's raw 4–6 is reduced before it
  lands; the exact reduction path — defense/armor in the defender's damage-taking — is one we have **not
  fully pinned down yet**, and that's flagged as a thing to verify, not asserted).
- **Death%** — fraction of runs in which the player died in that scenario/floor. The headline outcome.
- **DPR_P / DPR_M** — **damage per round**, player / monster. Throughput *over time* — it includes misses
  and rounds spent moving, so it is **not** the same as per-hit damage (H_PM/H_MP). Read DPR as "pace of
  damage flowing"; read H_PM/H_MP as "weight of a single blow."
- **Pressure index** — **= avg monster attacks per run − avg player attacks per run.** Positive = monsters
  are getting more swings in than the player (player on the back foot / being swarmed); near-zero = trading
  evenly. A swarm/attrition signal.

## Encounter & threat budgeting

- **ETP — Effective Threat Points.** The currency for "how much threat is in this room/floor." Each monster
  has an **`etp_base`** (orc 27, slime 10, troll 50…); the full formula (`etp_config.yaml`) is
  `ETP = (DPS×6) × Durability × Behavior × Synergy × Elite × Speed`. Rooms and floors have **ETP budgets**
  per band (e.g. B2 room max ≈ 100 ≈ "3 orcs"). Read: the size of an encounter in threat, independent of
  which specific monsters fill it.
- **`target_ttk_hits` / `target_ttd_hits`** — the **intended** ttk/ttd per band, written in `etp_config.yaml`
  (B1: 3/5, B2: 4/4…). These are the design *intent*. **As of 2026-06-07 these are canonical** — the
  yardstick observed H_PM/H_MP are checked against. (They currently disagree with the `PressureModel` bands;
  reconciling that into one target table is Step 0.)

## Structure

- **Bands B1–B5 / regions.** The game is five 5-floor difficulty bands, which *are* the named regions:
  B1 Reven Crypt (1–5), B2 The Boundary (6–10), B3 The Dimhalls (11–15), B4/B5 The Inner Court (16–24),
  then The Weighing (25). Each band has its own targets and ETP budgets.
- **Target band.** The acceptable range for a metric; a value inside it is healthy ("in band"). The two
  band systems we have today — `PressureModel` (death/H_PM/H_MP ranges) and ETP `ttk/ttd` — currently
  conflict; collapsing them to one canonical per-region target table is the foundational Step-0 task.

## Method & tooling

- **Representative vs stress scenario.** *Representative* = ETP-budgeted to a band's room budget; measures
  whether **intended** difficulty hits target. *Stress* = deliberately **over-budget** (e.g. the 5-orc
  siege at ~50% death); measures "is the wall beatable" and serves as a regression anchor. We use both, but
  they answer different questions and are read against different targets.
- **The net / baseline.** The 15 committed acceptance scenarios + their stored metrics
  (`reports/baselines/balance_suite_baseline.json`). A change is re-run against them; CI fails on a FAIL
  verdict. It catches *regressions in fixed fights* — it does not tell you the whole game is balanced.
- **Personas.** The 5 bot AIs (cautious, balanced, aggressive, greedy, speedrunner). Used as a difficulty
  **band**: "beatable-but-hard" = the cautious/balanced personas survive at intended rates while the reckless
  ones die more. A healthy game shows skill mattering across personas.
- **Damage = base + StrengthMod.** A landed hit deals the weapon/innate roll **plus** the attacker's STR
  modifier (STR 14 → +2), ×2 on a crit, then status/resistance modifiers, min 1 (`CombatResolver`). This is
  why the player's fine-longsword (1–8) at STR 14 averages ≈ 6.5/hit — matching the ETP formula's `1d8+2`
  baseline.
- **Seed 1337.** The fixed RNG seed for all balance runs. Same seed + same code = identical results, so any
  metric change between runs is a real change, never variance.

---

# Part 1 — Re-anchor to the PoC (the foundation)

## What the PoC's balance approach was

A measured loop, not a feel:

```
scenario YAML  →  C# harness (N runs, fixed seed 1337)  →  JSON metrics
      →  compare against target bands  →  change ONE variable  →  re-run same seed  →  compare
```

Four pillars held it up:

1. **A fixed-player methodology.** Hold the player's survivability stats constant (hp 54, STR/DEX/CON
   14, leather armor across the mid-game scenarios) and vary the *threat* (composition, depth) and the
   *gear* (weapon). This isolates "how hard is this encounter / how much does this weapon help" from
   player progression. Sound, and we keep it.
2. **A metrics vocabulary.**
   - **H_PM** = monster HP ÷ player damage-per-hit = *hits to kill a monster* (time-to-kill).
   - **H_MP** = player HP ÷ monster damage-per-hit = *hits for a monster to kill the player* (durability).
   - **Death%**, **DPR_P / DPR_M** (damage per round), **pressure index** (monster attacks − player attacks).
3. **Target bands** (`PressureModel`) — a per-metric acceptable range; a scenario PASSes if it lands in band.
4. **An acceptance matrix as a regression gate** — 15 fixed scenarios (depths 2/3/5, orc + zombie + four
   gear tiers each), re-run on every change; a committed baseline; CI fails on a FAIL verdict.

## What it covered

- **Floors 1–6**, really sampled at depths 2, 3, 5, 6. **Orc and zombie melee.** Gear-tier progression.
- That's it. ~6 of an eventual 25 floors; one combat archetype (melee); no systems beyond weapons.

## What numbers we carried forward (and are now treating as "given")

- Monster base stats (e.g. orc: hp 28, dmg 4–6, STR 14) and the **scaling curves** (DEFAULT_CURVE,
  ZOMBIE_CURVE) that grow them with depth.
- The **`PressureModel` target bands** (death 35–65%, H_PM 7–24, H_MP 20–48, …).
- The **ETP system** — per-monster threat values (orc 27, slime 10, troll 50), per-band room/floor ETP
  budgets, and per-band **`ttk`/`ttd` targets** (B1: 3/5, B2: 4/4, …).
- The **15-scenario baseline** (re-locked 2026-06-07).

## Why those numbers can't simply scale up to 25 floors and a much bigger game

This is the crux of reframe #2 — and it's four independent reasons, any one of which breaks "just scale it":

1. **The curve is extrapolated, not measured, past depth 6.** The PoC never ran a balanced scenario
   deeper than 6. Floors 7–24 use the same scaling curve, but *no one has ever checked that the curve
   produces fair fights there.* It's a formula running unobserved for 19 floors.
2. **The game grew a second and third dimension.** The PoC balanced melee. The current game has factions
   (emergent monster-vs-monster), possession (drain economy, host abilities), spells/wands/scrolls, rings,
   potions/throw, 28 monsters across 4 waves (incl. casters like the lich), and the Weighing. None of these
   were in the PoC's balance picture. They interact with combat in ways a melee curve can't predict.
3. **The inherited bands are internally contradictory** (FIND-001…004). The `PressureModel` bands were
   calibrated to the PoC's deliberately *over-budget stress scenarios* (≈50% death by design). The ETP
   `ttk/ttd` targets describe a different, faster game. They disagree by ~2× on kill-speed and ~10× on
   durability. We can't "carry forward the targets" when the targets fight each other. (You ruled ETP
   ttk/ttd canonical on 2026-06-07 — but that ruling exposed that the *shipped game doesn't meet it*,
   which is itself the headline finding, not a tuning detail.)
4. **Composition/encounter budgets for deep floors are untested.** ETP room/floor budgets exist for every
   band, but only B1's were ever validated against real fights. The deep-floor budgets are guesses.

**Conclusion:** the PoC gave us a *method that works* and a *validated first region*. It did not give us a
balanced 25-floor game, and it can't, by scaling. What we inherit is the loop and the foundation — not the answer.

---

# Part 2 — The balance strategy (the vision)

## What "balanced" MEANS for *this* game

Catacombs is a permadeath roguelike with **no XP leveling** (progression is gear + boons), procedural
floors, and a **durable-but-swarmable** combat model (FIND-004: per-hit lethality is low, players are hard
to kill 1-on-1, death comes from attrition and numbers). For this game, "balanced" is five concrete properties:

1. **The difficulty curve is intentional and legible.** Each region is harder than the last in a way the
   player can feel and trust. No **spikes** (a floor that kills far above its neighbors) and no **troughs**
   (a floor that's a trivial cakewalk). The curve rises; it doesn't jump or sag.
2. **Death is fair and diagnosable.** Players die to things they could have understood — bad composition
   matchups, resource mismanagement, a wrong gear choice — not to unobservable stat walls or pure RNG. A
   balanced death makes the player say "I see what I did," not "that was noise."
3. **The intended power curve holds.** At each region, a player with region-appropriate gear+boons, played
   competently (the *cautious/balanced* bot personas), survives at the intended rate — while the *reckless*
   personas die more. Difficulty is a **band across personas**, not a single point. "Beatable-but-hard."
4. **Choices measurably matter.** Gear, consumables, possession, faction stance change outcomes by amounts
   the harness can see. (FIND-001 — the keen dagger being a 94%-death trap — is a *balance failure of this
   property*: a weapon choice that's strictly dominated isn't a choice.)
5. **Combat feel matches a declared intent.** This is the upstream decision everything else hangs on (below).

## The upstream decision that gates everything: durable vs lethal

> **SUPERSEDED (2026-06-08).** This binary was a category error. Feel is not a global durable-vs-lethal
> point — it's a **durable baseline with deliberate lethal exceptions**, decided per threat-ROLE, with
> **composition** as the unit of balance. See `docs/balance/threat_archetypes.md`. The text below is kept
> for history.

FIND-004 surfaced that the game's actual combat (low per-hit lethality, H_MP ≈ 40 vs an ETP intent of 4) is
a *different combat philosophy* than the ETP targets describe. **Before any per-floor tuning, we decide the
intended feel**, because it defines what every downstream number aims at:

- **(A) Keep the durable/swarm game** — attrition-driven, you survive many hits, threat comes from numbers
  and resource drain. A valid, distinctive roguelike feel. → We revise the ETP `ttd` target toward reality
  and tune *composition/density* as the primary lever.
- **(B) Shift toward the lethal ETP intent** — fast, punishing, four-hits-and-you're-out. → A large change
  to the damage/soak model, not just monster HP, with heavy playtest.

This is **your call**, and it's the first pin on the map. It's not in this doc to decide — it's here to be
named as the gate. Everything in Part 2's sequence assumes we answer it first and write it down as the
**canonical per-region target table** (death%, ttk, ttd, encounter ETP per region) — the single source of
truth that replaces the two contradictory band systems we have today.

## The sequence (floors first; endgame parked)

**Proposed order: ascending by region.** The ETP config already bands the game into five regions of five
floors; those bands *are* the natural difficulty units. Tackle them low → high:

| Step | Region | Floors | Why here |
|---|---|---|---|
| 0 | **Threat-archetype model + role-aware target table** | — | Upstream gate. See `threat_archetypes.md` — 0a (feel model) + 0b (target table) done; 0c (report) next. Supersedes the durable-vs-lethal binary below. |
| 1 | **B1 — Reven Crypt** (re-validate) | 1–5 | The PoC "did" this, but against the contradictory old bands. Re-validate against the *new* canonical target + chosen feel. It's the foundation every deeper measurement stands on — we must trust it first. |
| 2 | **B2 — The Boundary** | 6–10 | Where the descent population actually dies (mid-game). Highest player-impact per floor. First genuinely-new territory. |
| 3 | **B3 — The Dimhalls** | 11–15 | Casters/undead wave (lich, wraith) come online — the never-measured dangerous monsters live here. |
| 4 | **B4 / B5 — Inner Court** | 16–24 | Late descent; fewest reach it, but it must hold before the Weighing means anything. |
| 5 | **The Weighing** (un-park) | 25 | Already frameworked + headless-runnable. Tune last, against a descent that delivers a correctly-powered player to its door. |

**Why ascending, not "hardest-first" or "most-played-first":**
- **Foundation-first and population-first coincide in the early regions** — the floors most players see are
  also the floors everything else builds on. No conflict; both point low.
- **Each region is validated on validated ground.** A player arrives at region N in a state (gear, HP,
  resources) shaped by region N−1. If N−1 is unbalanced, region N's measurements are polluted. Ascending
  guarantees the input to each region's tuning is itself trustworthy.
- **The deep-floor data-quality problem forces it anyway** (Part 3, gap #5): you can't soak-measure floor 18
  if the bot dies on floor 9. The descent has to be survivable up to region N before you can observe region N.

## Where we are right now (the pin on the map)

- **Step 0: not done.** The combat-feel decision is open; the canonical target table doesn't exist yet. *This
  is the immediate next strategic step.*
- **Step 1 (B1): partially measured, against the wrong yardstick.** PoC scenarios exist and pass, but against
  the old contradictory bands — not re-validated against canonical targets or the chosen feel.
- **Steps 2–5: unmeasured.** Floors 6–24 + the Weighing have no validated balance. (The Weighing has a
  *framework* and is headless-runnable — Step 5 is ready when we get there.)
- **Instrumentation: partial** (Part 3). We can measure single fixed fights and run a whole-descent soak, but
  we cannot yet see "the whole game vs intent" or "did this change help the whole game" in one place.

So: **we are at Step 0**, with a method, a foundation, and a half-built observability layer. The strategy is
to decide feel, write the target table, build the whole-game view (Part 4), then walk the regions upward.

---

# Part 3 — Observability: current vs needed (the instrumentation)

This is the part that determines whether you can *hold* the strategy, so it's the most honest section. The
governing question, stated plainly and answered at the end:

> **When we change a number, how does Rafe see the effect on the whole game and know it improved?**

## What exists TODAY, and what each thing actually tells you

| Tool / artifact | What it measures | What it tells you | Honest limit |
|---|---|---|---|
| **Scenario harness, single** (`--scenario`) | One fixed fight, N runs | Per-fight H_PM, H_MP, Death%, DPR, pressure vs bands | One *authored* fight; floors 1–6 only; bands are the contradictory ones |
| **Acceptance suite + baseline net** (`--suite --baseline`) | 15 fixed fights vs committed baseline | Regression gate: PASS/WARN/FAIL + deltas; CI-wired | Floors 2/3/5 only; fixed compositions; "did these 15 fights move," not "is the game balanced" |
| **Dungeon soak** (`--dungeon --report`) | Real *procedural* descent, bot-played, N runs | **Survival curve, death-rate-by-floor**, floor efficiency, death classification, killer counts, bot telemetry, anomalies | Deep-floor data is thin if the bot dies early; measures procedural reality, **not adherence to per-floor targets**; no before/after delta |
| **Bot survivability report** (`--bot-report`) | 5 personas × scenarios | The difficulty *band* — who survives, who doesn't | Scenario-bound (floors 1–6); not whole-descent |
| **Depth-pressure report** (`--depth-report`) | Pressure index across depths | Coarse "where is pressure rising" | Single axis; not tied to targets |
| **ETP sanity tool** (`--etp-sanity`) | ETP budget math consistency | Catches internally-inconsistent budgets | Checks the formula, not whether the fights it implies are fair |
| **Findings log / coverage map** (manual docs) | Human-tracked decisions & gaps | The narrative state of balance | Manual; not generated; can go stale |

**The good news buried here:** the dungeon soak already produces a **per-floor death curve across the whole
descent**. That's the seed of the whole-game view — it's the closest thing we have to the artifact you want.
It's just not tied to targets, not delta-framed, and starves on deep floors.

## The GAP — what's missing to answer "is it balanced" and "did my change help"

1. **No canonical per-floor/region target table.** We have an ETP config and a PressureModel, and they
   *disagree*. There is no single "intended death%, ttk, ttd, encounter ETP per region" that observed data
   is checked against. **Until this exists, "balanced" is undefined and nothing can be green or red.** (Step 0.)
2. **No whole-game observed-vs-target view.** The soak shows observed per-floor death; the suite shows
   target-adherence for 6 floors. Nothing shows *all 25 floors'* observed-vs-intended on the key axes at once.
3. **No change-delta on the whole game.** When you move a number, no artifact says "here's the whole descent
   before vs after, here's what moved, toward or away from target." The baseline net does this for 15 fixed
   fights — not the descent the player actually walks.
4. **No legible health indicator.** Nothing says, at a glance, "floor 12 is a spike" or "the durability curve
   sags at B3." You must read raw numbers and know the bands by heart — which is exactly how you lost altitude.
5. **Deep-floor data-quality problem.** The soak can't measure floor 18 if the bot dies on floor 9. We need
   **staged starts** — drop a region-appropriately-geared bot at floor N — to measure deep regions in isolation.
   (Good news: the headless levers we built for the Weighing — inject state, drive the run without UI —
   generalize directly into this. The endgame work pays off here.)
6. **No tie between observed combat and the feel intent** (ttk/ttd) per floor — so we can't see whether the
   *feel* (not just the death rate) is consistent down the descent.

## The answer to the governing question — today vs what you need

**Today:** you can't, cleanly. You'd run the suite (6 fixed fights, regression-only) and/or a soak (whole
descent but thin deep data, no target framing, no delta), then eyeball raw numbers against bands you'd have to
remember. There is no single glance that says "the change pulled floors 6–9 toward target and broke nothing else."

**What you need (and what Part 4 mocks):** one **whole-game balance report** that, for every floor/region,
shows **observed vs canonical target** on the agreed axes, a **health flag**, and a **delta vs the previous
run** — so a single read tells you the whole-game effect of a change. That artifact is the prerequisite for you
to hold the strategy. Building it (and the target table in gap #1) is the real Step 0 deliverable, ahead of any
tuning.

---

# Part 4 — The artifact: whole-game balance report (mockup)

This is what you'd open after any change. Generated by extending the dungeon-soak (it already has the per-floor
curve) with: the canonical target table, staged-start deep-floor measurement, health flags, and a delta column.

## Header — "what changed and did the whole game improve?"

```
══════════════════════════════════════════════════════════════════════════════
 CATACOMBS BALANCE REPORT          run: 2026-06-12 14:02   seed 1337   feel: DURABLE
 change since last: orc_grunt base HP 28 → 24  (B2 representative tuning)
 ──────────────────────────────────────────────────────────────────────────────
 WHOLE-GAME VERDICT:  ▲ IMPROVED   (3 floors → toward target, 0 → away, 0 new spikes)
 descent survival (bot, balanced persona):  31%  (prev 28%,  target 25–35%)  ✅
══════════════════════════════════════════════════════════════════════════════
```

## The descent table — observed vs target, per floor, with health + delta

```
 Region / Floor   Death%   (target)   ttk   (tgt)   ttd   (tgt)   ETP   (budget)  Health   Δ vs prev
 ───────────────────────────────────────────────────────────────────────────────────────────────
 B1 Reven Crypt
   1               4%       (3–8)      3.1   (3)     6.0   (5)      28   (20–50)    ✅       ·
   2               6%       (3–8)      3.4   (3)     5.8   (5)      44   (20–50)    ✅       ·
   3               9%       (5–12)     3.6   (3)     5.2   (5)      61   (20–60)    ✅       ▼ -2%
   4               11%      (5–12)     3.8   (4)     5.0   (5)      55   (20–60)    ✅       ·
   5               14%      (8–16)     4.0   (4)     4.8   (5)      72   (20–60)    ✅       ·
 B2 The Boundary
   6               18%      (12–22)    4.4   (4)     4.6   (4)      90   (20–100)   ✅       ▼ -6%  ← change landed
   7               21%      (12–22)    5.0   (4)     4.4   (4)      96   (20–100)   ⚠▲      ▼ -9%
   8               26%      (12–22)    5.6   (4)     4.1   (4)     104   (20–100)   ⚠▲      ·
   9               24%      (12–22)    6.1   (4)     3.9   (4)      98   (20–100)   ⚠▲      ·
   10              22%      (12–22)    6.6   (4)     3.7   (4)     101   (20–100)   ✅       ·
 B3 The Dimhalls
   11              19%      (15–28)    6.9   (5)     3.6   (5)     128   (40–150)   ⚠▲      ·
   12              41%      (15–28)    7.4   (5)     2.1   (5)     147   (40–150)   🔴▲      ·   ← SPIKE
   13              23%      (15–28)    7.0   (5)     3.4   (5)     119   (40–150)   ✅       ·
   ...
```

Legend: `✅` in band · `⚠` near/over edge · `🔴` spike/trough · `▲` too hard · `▼` too easy · `Δ` change vs last run.

## Reading it at a glance

- **A healthy region** (B1 above): Death% rises smoothly inside its target, ttk/ttd hug their targets, no
  flags. The curve *climbs*; nothing jumps.
- **A drifting region** (B2): ttk is creeping above target (4 → 6.6 as you descend) — the `⚠▲` flags say
  "monsters are getting too tanky relative to gear faster than intended." The change we made (grunt HP 28→24)
  pulled floor 6 down 6% — visible in the Δ column — confirming the lever works, and the report shows it
  *didn't break neighbors*.
- **A spike** (floor 12, `🔴▲`): Death% 41% vs target 15–28%, and crucially **ttd collapses to 2.1** — players
  are dying *fast* there, not grinding out. One glance says "floor 12 kills too hard and too suddenly." The
  anomaly callout (below) tells you why.

## Anomaly callout — the "why" behind a flag

```
 🔴 FLOOR 12 SPIKE
    Death% 41% (target 15–28).  ttd 2.1 (target 5) — deaths are FAST, not attrition.
    Top killer: lich  — 71% of floor-12 deaths.  Soul Bolt accounts for 64% of lich damage.
    First-encounter deaths: 83%  → players die before they can learn the threat (fairness fail).
    Likely lever: lich spawn depth too shallow, or Soul Bolt damage unbanded at B3.
```

## Persona band — "beatable-but-hard?"

```
 Descent survival by persona (to floor 24):
   cautious   38%   balanced   31%   aggressive   22%   greedy   17%   speedrunner   12%
   reading: skilled play is rewarded, reckless play is punished — the band is intact. ✅
   (a problem reading would be all personas clustered — difficulty that ignores skill — or
    cautious ≈ 0% — a wall no amount of care survives.)
```

## What "working" looks like vs "a problem"

- **Working:** whole-game verdict ▲ IMPROVED or ✅ STABLE; the descent table is mostly ✅ with Death% rising
  monotonically inside targets; ttk/ttd track their per-region targets; the persona band shows skill mattering;
  the Δ column shows your change moved the intended floors and left the rest alone.
- **A problem you'd spot instantly:** a `🔴` in the table (spike/trough), a Δ column lighting up on floors you
  *didn't* mean to touch (a change with unintended whole-game reach), a ttk column drifting away from target
  down a region (scaling-curve wrong), or the persona band collapsing (difficulty disconnected from skill).

---

# What this doc commits us to (before any number moves)

1. **You make the combat-feel call** (durable vs lethal) — the upstream gate.
2. **We write the canonical per-region target table** (death%, ttk, ttd, ETP) — one source of truth, replacing
   the two contradictory band systems.
3. **We build the whole-game balance report** (Part 4) — extending the soak with the target table, staged
   deep-floor starts (reusing the Weighing headless levers), health flags, and deltas.
4. **Then, and only then, we tune** — region by region, ascending from B1, every tweak legible against the
   whole game in the report you hold.

Strategy first. When you've reviewed this and we've aligned on the sequence and the artifact, we go tactical —
and you keep the map.
```
