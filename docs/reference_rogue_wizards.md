# Reference: Rogue Wizards

_Last verified: 2026-07-12 against commit 86b6f10_

**Status:** Primary visual and UX reference for The Under-Warden. The closest existing game to the vision.

Screenshots: `docs/IMG_1444.PNG`, `docs/IMG_1445.PNG`

---

## Overview

Isometric turn-based roguelike dungeon crawler. Built by **Colin Day** (Spellbind Studios) — essentially a solo developer with 20 years AAA experience including **Diablo III**, Marvel Heroes, and Command & Conquer: Generals. The Diablo pedigree shows in the loot system.

- **Platforms:** iOS, Android, Steam, Switch, PlayStation, Xbox
- **Kickstarter funded:** $74,739 from 1,429 backers (2014)
- **Released:** Steam Sept 2016, iOS May 2017
- **Won "Best in Play 2015" at GDC**
- **Steam reception:** Mostly Positive (79% of 122 reviews), TechRaptor 8.5/10

---

## What It Got Right (learn from these)

### Loot System (strongest element)
- Diablo-style rarity tiers, six equipment slots, random enchantments, gem socketing
- **Items gain experience and rank up** with randomized bonuses — creates attachment and keep-vs-replace decisions
- Flexible playstyles based on gear drops (melee, ranged, spellcaster each run)
- The gear loop is the strongest system by a wide margin — directly relevant to our "gear > boons" philosophy

### Session-Friendly Pacing
- Works in short bursts — praised as very mobile-friendly
- Turn-based means "combat is as fast as you want it to be"
- Perfect for the mobile use case

### Dual Mode Structure
- Story mode (~10-30 hours) with no death penalty — extended tutorial/campaign
- Gauntlet mode with permadeath and endless depth — the "real" roguelike
- Lets casual players engage without permadeath while giving genre fans what they want

### Town Meta-Progression
- Wizard Tower sanctuary with upgradable shops, crafting, NPC recruitment
- Persistent across runs — gives a sense of progress even on failed runs

### Tactical Positioning
- One action per turn: move, attack, switch weapon, or cast spell
- Positioning genuinely matters — sometimes skipping a turn to reposition is optimal

---

## What It Got Wrong (avoid these)

### Controls / Targeting (biggest weakness)
- Tap-to-move on isometric grid causes **misclicks** — character models overlap with loot, shrines, enemies
- Wasting a turn on a misclick in a system where every turn matters is punishing
- "Never satisfactorily solves how to move and how to target" — most consistent criticism
- **Lesson for us:** Isometric touch targeting is a hard problem. We need to solve it better. Possible approaches: tile highlighting on hover/hold, undo-last-move, confirmation for ambiguous taps, smart target disambiguation.

### Turn Cost for Weapon/Spell Switching
- Changing weapons costs a full turn — combined with limited visibility, feels punishing
- Creates a "never switch weapons" incentive that undermines build flexibility
- **Lesson:** Don't make the interesting choice (adapting your approach) cost as much as the boring choice (keep doing the same thing)

### Limited Visibility
- Only 5-6 tiles visible. World animates into existence ahead, crumbles behind.
- Visually distinctive but gameplay-frustrating — constant ambushes
- **Lesson:** Visibility should serve gameplay. Fog of war is fine; claustrophobic visibility that prevents planning is not.

### Shallow Combat Depth
- "Too light and too simple, and a tad too slow for its scaled-down design"
- Mechanics don't evolve much past midgame
- Only 3-4 enemy types per world, some sharing sprites despite different abilities
- **Lesson:** This is exactly what our balance harness prevents. Measurable depth, not just surface variety.

### Monster Balance
- "Too many monsters have unavoidable abilities and/or able to stun-lock the player"
- Deaths that feel unfair — contradicts our "fair deaths" design principle
- **Lesson:** Every death should feel earned. This is already in PLAYER_PAIN_POINTS.md.

---

## UI/UX Analysis (from screenshots)

### What Works
- **Health/mana bars** at top center — always visible, clear status at a glance
- **Quick-use item bar** at bottom — large, colorful, thumb-reachable icons with quantity numbers
- **Minimap** (top-left green gem) — compact, unobtrusive
- **Location name** (top-right) — always know where you are
- **Enemy HP bar with name** appears on engagement — clear feedback
- **Colored tile highlights** — green for movement/walkable, blue/pink for spell/enemy zones
- **Fog of war** as black silhouette — clear boundary between known and unknown
- **Level Up** notification — prominent but not blocking

### What We'd Change
- **Landscape only** — we're portrait-first
- **Action buttons in corners** — some aren't thumb-reachable depending on hand/device
- **No visible grid** until highlighted — can be hard to judge distance

### Visual Design Notes
- Rich environmental detail: cave walls, torches, skulls, barrels, stalagmites
- Tiles have varied textures (stone patterns differ per tile) — avoids grid monotony
- Characters are charming, readable at small sizes — important for mobile
- Environmental objects (chests, barrels) are clearly interactive vs decorative
- Lighting effects (torch glow, spell particles) add atmosphere without clutter

---

## Relevance to The Under-Warden

This game validates that isometric turn-based roguelikes work on mobile. It also demonstrates the primary risk: touch targeting in iso view. The loot system philosophy aligns directly with our gear-over-boons design. The dual-mode structure (story + permadeath) is worth considering.

The key difference in our approach: measurable balance. Rogue Wizards' combat was criticized as shallow and its monster balance as unfair — exactly the problems our harness-driven methodology is designed to prevent.

**What to take:** Visual ambiance, session pacing, loot-forward design, iso viability proof
**What to improve on:** Touch controls, combat depth, enemy variety, death fairness, portrait orientation
