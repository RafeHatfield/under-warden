# Plan: Throw-Anything System

## Status: needs-review

## Overview

Replace the current throw-only-potions-via-tap system with a universal throw-anything system. Any item can be thrown: weapons deal throw damage, potions apply their throw effect, other items (scrolls, rings, armor) are wasted. Throwing is initiated from a long-press action sheet on inventory items, not from tap.

This is the foundation for the full item interaction model: long-press any inventory item to get an action sheet (Use / Throw / Drop / Inspect), while tap remains the "obvious" action (drink potion, equip weapon, use scroll).

## PoC Reference Summary

Source: `~/development/rlike/throwing.py` (333 lines), `~/development/rlike/game_actions.py` (_handle_throw_action, _handle_inventory_selection_throw), `~/development/rlike/game_states.py` (THROW_SELECT_ITEM, THROW_TARGETING)

### PoC Throw Behavior (answers to all research questions)

1. **Weapon throw damage**: Roll the weapon's damage dice, then apply a -2 penalty (min 1). No accuracy roll -- if the projectile path reaches the target tile and a monster is there, it hits. No STR mod on throw damage.

2. **Non-potion, non-weapon items** (rings, scrolls, armor, wands): Land on the ground at the target tile. No damage, no effect. Message: "You throw the {name}. It lands at (x, y)." Item is retrievable.

3. **Equipped weapons**: The PoC removes the item from inventory before throwing. If the weapon is equipped, it must be unequipped first (the PoC flow goes through inventory selection, not equipment slots). Our action sheet must handle this: if weapon is equipped, auto-unequip before throwing.

4. **Miss vs hit**: No accuracy roll. The projectile traces a Bresenham line from thrower to target. If a monster occupies the final tile, it's a hit. If the tile is empty (monster moved, or targeting empty ground), it's a miss. Weapons and non-consumables land on the ground regardless. Potions shatter regardless (hit = apply effect to monster, miss = wasted on ground).

5. **Range**: 10 tiles for all throws (weapons, potions, everything). PoC comment: "could be modified by STR in future" but never was.

6. **Thrown weapons**: Always land on the ground at the target position, retrievable. Both on hit and miss.

7. **Monster killed by thrown weapon**: Weapon lands at the target position (floor item). PoC sets item.x/y to final_x/final_y and adds to entities list.

8. **Throw effects by item type**:
   - **Potions with use_function**: Shatter on impact. Hit = apply potion effect to target monster. Miss = wasted ("shatters on the ground"). Consumed either way.
   - **Weapons (Equippable with damage)**: Deal weapon dice - 2 damage (min 1). Land on ground (retrievable).
   - **Everything else** (rings, scrolls, armor, wands, misc): Land on ground at target position. No damage, no effect. Retrievable.

## Architecture Decision: New ActionKind vs. Routing Through CastSpell

**Decision: Add `PlayerAction.ActionKind.ThrowItem`.**

CastSpell routing would work for potions (they already have ThrowSpellId), but weapon throws and junk throws have no SpellEffect component. A new ActionKind is cleaner and avoids overloading CastSpell semantics. The ThrowResolver handles all three paths (potion, weapon, junk) in one place.

## Current State Audit

### What exists
- `SpellEffect.ThrowSpellId` on potions -- dual-mode potions already work via CastSpell
- `TargetingState.IsThrowPotion` / `ThrowSpellId` / `DrinkSpellId` -- throw-potion targeting
- `HandleThrowablePotion()` in GameController -- enters targeting for throw potions on tap
- `LongPressDetector` -- exists, fires on map tiles for monster/item inspect
- `InventoryPanel` -- has `ItemTapped` and `ItemDropRequested` events, manual hit-testing
- `EquipmentPanel` -- has `EquipRequested`, `UnequipRequested`, `ItemDropRequested` events

### What changes
- **Tap on throwable potion**: Currently enters throw targeting. Must change to drink immediately (tap = obvious action). Throw moves to action sheet.
- **HandleThrowablePotion**: Removed. Throwable potions tap-to-drink like any potion.
- **TargetingState.IsThrowPotion / ThrowSpellId / DrinkSpellId**: Removed. No longer needed. Throw targeting is entered from the action sheet, not from tap.
- **DrinkSelfRequested event**: Removed from InputHandler. Self-tap in targeting = cancel (universal).
- **Long-press on inventory**: Currently no behavior. Must detect long-press on inventory slots and show action sheet.

---

## Tasks

### TASK-001: ThrowEvent and PlayerAction.ThrowItem (Logic Layer)

- Status: complete
- Files changed:
  - `src/Logic/Core/PlayerAction.cs` — added ThrowItem ActionKind and factory method
  - `src/Logic/Core/TurnEvent.cs` — added ThrowEvent with ThrowResultType enum, ActorX/Y fields
- Notes: ThrowEvent includes ActorX/Y (thrower position) for animation path length calculation
- Layer: logic
- Type: system
- Dependencies: none
- Files to modify:
  - `src/Logic/Core/PlayerAction.cs` -- add `ThrowItem` ActionKind and factory method
  - `src/Logic/Core/TurnEvent.cs` -- add `ThrowEvent`
- Acceptance criteria:
  - `PlayerAction.ActionKind.ThrowItem` exists
  - `PlayerAction.ThrowItem(Entity item, int targetX, int targetY)` factory method exists
  - `ThrowEvent` has: ActorId, ItemId, ItemName, TargetX, TargetY, Hit (bool), Damage (int), TargetKilled (bool), TargetEntityId (int?, null on miss), ItemLandsOnGround (bool), ResultType (enum: PotionShatter, WeaponHit, WeaponMiss, JunkLand)
  - Compiles with `dotnet build`

### TASK-002: ThrowResolver (Logic Layer)

- Status: complete
- Files changed:
  - `src/Logic/Combat/ThrowResolver.cs` — new file; Bresenham path, three resolution paths
  - `src/Logic/Core/TurnController.cs` — added ThrowItem case → ResolveThrowItem → ThrowResolver.Resolve
- Notes: Throw kills follow spell-kill pattern (DeathEvent only, no loot drop/corpse transform from ThrowResolver — TurnController handles those via UpdateKnowledge). This matches existing SpellResolver behavior and keeps ThrowResolver stateless.
- Layer: logic
- Type: system
- Dependencies: TASK-001
- Files to create:
  - `src/Logic/Combat/ThrowResolver.cs`
- Files to modify:
  - `src/Logic/Core/TurnController.cs` -- add `case ActionKind.ThrowItem` dispatching to ThrowResolver
- Implementation details:
  - `ThrowResolver.Resolve(Entity thrower, Entity item, int targetX, int targetY, GameState state)` returns `List<TurnEvent>`
  - **Projectile path**: Bresenham line from thrower position to target, stopping at walls or max range (10). Use `UnderWarden.Logic.Map.GameMap.IsWalkable()` for wall detection. Implement Bresenham in a static utility method `ThrowResolver.CalculatePath()` (no tcod dependency in C#).
  - **Target detection**: Check if an alive monster occupies the final path tile.
  - **Three resolution paths**:
    1. **Potion** (item has `SpellEffect` with `ThrowSpellId`): Delegate to `SpellResolver.Resolve()` with `overrideSpellId = spell.ThrowSpellId` and `targetEntityId`. Consume the potion (decrement Consumable.StackSize, remove if depleted). On miss (no monster at target), still consume but emit ThrowEvent with Hit=false. Do NOT call SpellResolver on miss -- potion shatters on ground with no effect.
    2. **Weapon** (item has `Equippable` with `IsWeapon`): Roll weapon damage dice via `equippable.RollDamage(state.Rng)`, subtract 2 (min 1). If monster at target: apply damage via `fighter.TakeDamage()`, emit ThrowEvent and possibly DeathEvent. If no monster: emit ThrowEvent with Hit=false. Either way: remove from inventory, place as floor item at final path position.
    3. **Junk** (everything else): Remove from inventory, place as floor item at final path position. Emit ThrowEvent with ResultType=JunkLand.
  - **Auto-unequip**: If the thrown item is currently equipped (check all Equipment slots), unequip it first (emit UnequipEvent) before removing from inventory.
  - **Invisibility break**: Throwing always breaks invisibility (offensive action). Same pattern as CastSpell.
  - **Momentum reset**: `player.Get<SpeedBonusTracker>()?.ResetMomentum()` after throw.
  - **Floor item placement**: Same pattern as `ResolveDrop` -- set item.X/Y, add to `state.FloorItems`, call `state.Map.RegisterEntity(item)`.
- Acceptance criteria:
  - Throwing a weapon at a monster deals weapon dice - 2 damage (min 1)
  - Throwing a weapon at empty ground places it on the floor
  - Throwing a potion at a monster applies the throw spell effect and consumes the potion
  - Throwing a potion at empty ground consumes the potion with no effect
  - Throwing a ring/scroll/armor at any target places it on the floor with no effect
  - Throwing an equipped weapon auto-unequips it first
  - Throwing breaks invisibility
  - Throwing resets momentum
  - All paths emit appropriate ThrowEvent
  - Monster death from thrown weapon emits DeathEvent
  - `dotnet test --filter "Category!=Slow"` passes

### TASK-003: ThrowResolver Unit Tests (Logic Layer)

- Status: complete
- Files changed:
  - `tests/Combat/ThrowResolverTests.cs` — 17 tests covering all paths
- Notes: All 17 tests pass. Plan called for 16 tests; added separate BresenhamPath_Straight_Horizontal and BresenhamPath_Straight_Vertical (both listed in plan as "BresenhamPath_Straight") for better coverage.
- Layer: logic
- Type: test
- Dependencies: TASK-002
- Files to create:
  - `tests/Combat/ThrowResolverTests.cs`
- Test cases:
  - `ThrowWeapon_HitsMonster_DealsDamageMinusTwo` -- weapon 1d6, verify damage is dice-2, min 1
  - `ThrowWeapon_MissesEmpty_LandsOnGround` -- no monster at target, weapon becomes floor item
  - `ThrowWeapon_KillsMonster_EmitsDeathEvent` -- monster HP low enough to die
  - `ThrowWeapon_Equipped_AutoUnequips` -- weapon in MainHand, thrown, slot cleared
  - `ThrowPotion_HitsMonster_AppliesEffect` -- weakness potion thrown, target gets WeaknessEffect
  - `ThrowPotion_MissesEmpty_Consumed` -- potion consumed, no effect applied
  - `ThrowPotion_Stacked_DecrementsStack` -- stack of 3, throw one, stack becomes 2
  - `ThrowJunk_Ring_LandsOnGround` -- ring thrown, lands at target, no damage
  - `ThrowJunk_Scroll_LandsOnGround` -- scroll thrown, lands at target, no damage
  - `ThrowArmor_LandsOnGround` -- armor thrown, retrievable
  - `Throw_BreaksInvisibility` -- player invisible, throws, invisibility removed
  - `Throw_ResetsMomentum` -- player has momentum, throws, momentum cleared
  - `Throw_PathStopsAtWall` -- target behind wall, item stops at wall
  - `Throw_MaxRange10` -- target 15 tiles away, item stops at 10
  - `BresenhamPath_Straight` -- verify correct path for horizontal/vertical/diagonal
  - `BresenhamPath_Diagonal` -- verify correct path for non-axis-aligned throw
- Acceptance criteria:
  - All tests pass with `dotnet test --filter "Category!=Slow"`
  - Deterministic seeds used (1337 default)

### TASK-004: Action Sheet UI (Presentation Layer)

- Status: complete
- Files changed:
  - `src/Presentation/UI/ActionSheet.cs` — new file; centered popup with action buttons
- Notes: ActionSheetAction enum lives at the bottom of the file. Button hit-rects computed via CallDeferred(_ComputeButtonRects) after sheet is in tree. Uses same manual hit-test pattern as InventoryPanel/EquipmentPanel.
- Layer: presentation
- Type: system
- Dependencies: none (can be built in parallel with TASK-001/002)
- Files to create:
  - `src/Presentation/UI/ActionSheet.cs`
- Implementation details:
  - A modal overlay panel (like EquipmentPanel) that shows contextual actions for an item.
  - Appears on long-press of an inventory slot or equipment slot.
  - Layout: centered popup, dark semi-transparent background, vertically stacked action buttons.
  - Each action button: full-width, large touch target (min 48px height), icon + label.
  - Actions by item type:

    | Item Type | Actions |
    |-----------|---------|
    | Healing potion | Drink / Throw / Drop |
    | Buff potion (no ThrowSpellId) | Drink / Throw / Drop |
    | Debuff potion (has ThrowSpellId) | Drink / Throw / Drop |
    | Scroll | Use / Throw / Drop |
    | Wand | Use / Throw / Drop |
    | Weapon (unequipped) | Equip / Throw / Drop |
    | Weapon (equipped) | Unequip / Throw / Drop |
    | Armor (unequipped) | Equip / Throw / Drop |
    | Armor (equipped) | Unequip / Throw / Drop |
    | Ring (unequipped) | Equip / Throw / Drop |
    | Ring (equipped) | Unequip / Throw / Drop |

  - Note: "Inspect" action deferred. The action sheet itself shows the item name and basic stats at the top (name, damage/AC/effect). Full inspect panel integration is a follow-up.
  - Events: `ActionSelected(int itemId, ActionSheetAction action)` where `ActionSheetAction` is an enum: `Use, Throw, Drop, Equip, Unequip`.
  - Dismissal: tap outside the sheet, or tap an action button. No explicit cancel button needed.
  - Manual hit-testing pattern (same as InventoryPanel/EquipmentPanel) for reliable touch targets.
- Acceptance criteria:
  - ActionSheet shows correct actions for each item type
  - Tapping an action fires `ActionSelected` with correct parameters
  - Tapping outside dismisses the sheet
  - Sheet appears centered on screen with semi-transparent backdrop
  - Touch targets are at least 48px tall
  - Panel consumes all input while visible (MouseFilter.Stop + backdrop)

### TASK-005: Inventory Long-Press Detection (Presentation Layer)

- Status: complete
- Files changed:
  - `src/Presentation/UI/InventoryPanel.cs` — added long-press timer (_Process) + ItemLongPressed event; refactored _GuiInput to handle mouse-down/up separately
  - `src/Presentation/UI/EquipmentPanel.cs` — added long-press detection for equipped slots and pack items; EquippedItemLongPressed + PackItemLongPressed events; _currentState tracking for slot→itemId lookup
  - `src/Presentation/GameController.cs` — ActionSheet field, HandleInventoryLongPress, HandleEquippedSlotLongPress, HandlePackItemLongPress, OnActionSheetSelected, EnterThrowTargeting, FindItemById, IsItemEquipped; _pendingThrowItem to route LocationChosen to ThrowItem
- Notes: EnterThrowTargeting uses a synthetic SpellEffect when the item has none (weapons, junk) — required because TargetingState.Spell is non-nullable. The placeholder spell has SpellId="_throw_placeholder" and will never reach SpellResolver since OnLocationChosen routes to ThrowItem when _pendingThrowItem is set.
- Layer: presentation
- Type: system
- Dependencies: TASK-004
- Files to modify:
  - `src/Presentation/UI/InventoryPanel.cs` -- add long-press detection on slots
  - `src/Presentation/UI/EquipmentPanel.cs` -- add long-press detection on slots
  - `src/Presentation/GameController.cs` -- wire long-press to ActionSheet, wire ActionSheet actions to PlayerAction dispatch
  - `src/Presentation/Main.cs` -- add ActionSheet to scene tree, connect events
- Implementation details:
  - **InventoryPanel**: Track touch-down time per slot. On `_GuiInput`, if `InputEventMouseButton.Pressed` on a slot, start a 0.4s timer. On release before timer: fire existing `ItemTapped`. On timer expiry: fire new `ItemLongPressed(int itemId)` event and cancel the tap. On drag (mouse motion > 8px from press point): cancel both.
  - **EquipmentPanel**: Same pattern for pack item slots and equipped slots. Long-press on equipped slot fires `EquippedItemLongPressed(EquipmentSlot slot, int itemId)`. Long-press on pack item fires `PackItemLongPressed(int itemId)`.
  - **GameController**: New methods:
    - `HandleInventoryLongPress(int itemId)` -- look up item, determine type, create and show ActionSheet with appropriate actions.
    - `HandleActionSheetSelection(int itemId, ActionSheetAction action)` -- dispatch:
      - `Use` -> existing `HandleInventoryTap` logic (but see TASK-006 for tap behavior change)
      - `Throw` -> enter throw targeting mode (new targeting state, NOT IsThrowPotion)
      - `Drop` -> existing `HandleDropRequest`
      - `Equip` -> existing `HandleEquipRequest`
      - `Unequip` -> existing `HandleUnequipRequest`
    - Throw targeting from action sheet: Enter `GamePhase.Targeting` with a new `TargetingState` where `Mode = TargetingMode.Location` (any tile, not just monsters -- you can throw at empty ground). On location chosen: fire `PlayerAction.ThrowItem(item, x, y)`. On targeting cancelled: no action.
  - **Main.cs**: Create `ActionSheet` instance, add to UI layer, connect `ActionSelected` to `GameController.HandleActionSheetSelection`.
- Acceptance criteria:
  - Long-press (0.4s) on inventory slot shows action sheet
  - Short tap still fires the existing tap behavior
  - Long-press on equipment panel slot shows action sheet
  - Action sheet "Throw" enters targeting mode
  - Targeting tap on any tile (walkable or with monster) fires ThrowItem action
  - Self-tap during throw targeting cancels
  - Action sheet "Drop" drops item (same as current drop button)
  - Action sheet "Equip" equips item
  - Action sheet "Unequip" unequips item
  - Action sheet "Use" uses item (drink/cast)

### TASK-006: Refactor Throwable Potion Tap Behavior (Presentation Layer)

- Status: complete
- Files changed:
  - `src/Presentation/GameController.cs` — removed HandleThrowablePotion, OnDrinkSelfRequested; tap on throwable potion now calls HandleScrollOrWandUse (drinks immediately)
  - `src/Presentation/Input/InputHandler.cs` — removed DrinkSelfRequested event; self-tap during targeting always cancels
  - `src/Presentation/Input/TargetingState.cs` — removed IsThrowPotion, ThrowSpellId, DrinkSpellId properties
- Notes: Throwable potions (SpellEffect.ThrowSpellId != null) now tap-to-drink like non-throwable potions. Throw is accessed via long-press → action sheet only. The TurnController CastSpell path already handles throwable potions correctly when drinking (no TargetEntityId → uses primary SpellId = drink spell).
- Layer: presentation
- Type: system
- Dependencies: TASK-002, TASK-005
- Files to modify:
  - `src/Presentation/GameController.cs` -- remove `HandleThrowablePotion`, change `HandleInventoryTap` to treat throwable potions same as non-throwable
  - `src/Presentation/Input/InputHandler.cs` -- remove `DrinkSelfRequested` event
  - `src/Presentation/Input/TargetingState.cs` -- remove `IsThrowPotion`, `ThrowSpellId`, `DrinkSpellId` properties
- Implementation details:
  - **HandleInventoryTap**: Remove the `if (spellEffect.ThrowSpellId != null)` branch that calls `HandleThrowablePotion`. Throwable potions now fall through to the same path as non-throwable potions.
  - For potions with `HealAmount > 0` (including debuff potions that self-apply): tap = `UseItem` (drink).
  - For potions with `SpellEffect` and `Consumable.IsPotion` and no `HealAmount`: currently these route through HandleScrollOrWandUse which enters targeting. After this change, tap on a debuff potion should still drink it (self-target). The SpellEffect.Targeting is `Self`, so `HandleScrollOrWandUse` already fires `CastSpell` immediately for Self-targeting. This is correct -- drinking a weakness potion applies weakness to self.
  - **Remove dead code**: `HandleThrowablePotion`, `OnDrinkSelfRequested`, `DrinkSelfRequested` event, `IsThrowPotion`/`ThrowSpellId`/`DrinkSpellId` on TargetingState.
  - **TargetingState simplification**: Only `Item`, `Spell`, `Mode`, `Range`, `Radius` remain. Clean and minimal.
- Acceptance criteria:
  - Tapping a weakness potion drinks it (applies weakness to self) -- NOT enters throw targeting
  - Tapping a healing potion drinks it (same as before)
  - Tapping a scroll uses it (same as before)
  - Long-press on weakness potion shows action sheet with Drink / Throw / Drop
  - Selecting "Throw" from action sheet enters targeting, tapping monster throws potion
  - `HandleThrowablePotion` method no longer exists
  - `TargetingState.IsThrowPotion` no longer exists
  - `InputHandler.DrinkSelfRequested` no longer exists
  - `dotnet build` succeeds with no warnings from removed references

### TASK-007: Throw Animation (Presentation Layer)

- Status: complete
- Files changed:
  - `src/Presentation/Animation/TurnAnimator.cs` — added ThrowEvent to animatable check and AppendEventAnimation switch; new AnimateThrow method
- Notes: Projectile label ("/", "*", "o") travels from thrower to landing tile at 50ms/tile. Potion hits get a blue flash on target. Label SafeFree'd after travel via TweenCallback. Used Tween.EaseType.InOut + Tween.TransitionType.Linear for constant-speed travel.
- Layer: presentation
- Type: system
- Dependencies: TASK-002, TASK-006
- Files to modify:
  - `src/Presentation/Animation/TurnAnimator.cs` -- add ThrowEvent handling with projectile animation
- Implementation details:
  - When TurnAnimator encounters a `ThrowEvent`, animate a projectile sprite along the Bresenham path from actor to target tile.
  - Projectile: small item icon (or a generic '/' for weapons, '*' for potions, 'o' for junk) moving tile-by-tile. 50ms per tile (PoC timing).
  - On arrival: if potion, flash effect at impact tile. If weapon, show weapon sprite landing on ground. If junk, minimal landing effect.
  - Pair with existing hit-flash for damage (reuse AttackEvent animation pattern).
  - If ThrowEvent.TargetKilled, chain into DeathEvent animation as normal.
- Acceptance criteria:
  - Thrown item animates from player to target tile
  - Animation timing matches PoC (50ms per tile)
  - Potion throw shows shatter effect
  - Weapon throw shows weapon landing on ground
  - Kill-on-throw chains into death animation

### TASK-008: Bot ThrowItem Support (Logic Layer)

- Status: pending
- Layer: logic
- Type: system
- Dependencies: TASK-002
- Files to modify:
  - `src/Logic/AI/BotBrain.cs` -- add throw consideration to bot decision-making
- Implementation details:
  - Bot should consider throwing debuff potions at enemies when they have throwable potions and a target in range (10 tiles).
  - Priority: throw debuff potion > melee attack (if enemy is far) but melee > throw (if adjacent).
  - Bot should NOT throw weapons (too valuable) or junk (no benefit).
  - Bot should NOT throw healing potions (always drink those).
  - Decision: if bot has a throwable potion (ThrowSpellId set) and closest enemy is 2-10 tiles away, throw it.
- Acceptance criteria:
  - Bot throws debuff potions at distant enemies
  - Bot does not throw weapons or healing potions
  - Bot prefers melee when adjacent
  - Harness runs with throw-capable items produce ThrowEvents in the event log

### TASK-009: Integration Tests and Harness Verification

- Status: complete
- Files changed:
  - `tests/Core/ThrowSystemIntegrationTests.cs` — 5 integration tests covering all paths through TurnController
- Notes: Tests placed in tests/Core/ (not tests/Integration/) since they are logic-only (no Godot). All 5 pass. Total test count: 966 (up from 944 before this feature).
- Layer: logic
- Type: test
- Dependencies: TASK-002, TASK-003, TASK-008
- Files to create:
  - `tests/Integration/ThrowSystemIntegrationTests.cs`
- Test cases:
  - Full turn cycle: player throws weapon at monster, monster takes damage, weapon on floor, monster gets turn
  - Full turn cycle: player throws potion at monster, effect applied, potion consumed, no floor item
  - Full turn cycle: player throws ring, ring on floor, no damage
  - Throw kills monster: death event emitted, weapon on floor at monster position
  - Throw from action sheet flow: simulate the full action dispatch (ActionKind.ThrowItem through TurnController)
- Acceptance criteria:
  - All integration tests pass
  - `dotnet test --filter "Category!=Slow"` green
  - No regressions in existing test suite

---

## Deferred

- **Swipe-to-throw**: Fast path for experienced players. Long-press shows sheet, but swipe-from-item could initiate throw directly. Deferred to after the core system is stable.
- **Inspect action on action sheet**: Full item inspect panel from action sheet. Current inspect works via long-press on map tiles. Adding it to the action sheet is trivial once the sheet exists but is not in the PoC throw flow.
- **STR-based throw range**: PoC comments suggest it but never implemented. Range is fixed at 10.
- **Splash damage for potions**: PoC has a TODO for AoE splash on miss. Not implemented in PoC.
- **Throw accuracy roll**: PoC has none. Throws always hit if a monster is on the final tile. Could add a d20 roll later if throws feel too reliable.
- **Monster throwing**: Monsters throwing items at the player. Not in PoC scope for this phase.

## Risks

1. **InventoryPanel long-press timing**: The 0.4s threshold must feel distinct from a tap (which should be instant). If touch-down-to-tap timing on mobile is already ~200ms, 400ms should be safe. Test on device.

2. **Equipped weapon throw**: Auto-unequip adds complexity. If the player throws their only weapon, they're unarmed. This is correct (PoC behavior) but may surprise players. The action sheet should show the weapon name clearly so they know what they're throwing.

3. **TargetingState removal fields**: TASK-006 removes IsThrowPotion/ThrowSpellId/DrinkSpellId from TargetingState. Any code that references these will break at compile time (good -- compiler catches it). But verify no string-based references in Godot signals or reflection.

4. **Bresenham implementation**: Need a standalone C# Bresenham that matches the PoC's tcod.los.bresenham output. The algorithm is simple but edge cases on steep diagonals matter. Write a few path-equality tests against known inputs.
