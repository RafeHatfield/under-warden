# HUD & Inventory Plan — COMPLETE (2026-03-25)

**All 8 tasks done. 369 tests passing.**

**Goal:** Replicate PoC inventory + HUD functionality in C#/Godot. Layout differs
(mobile portrait vs terminal sidebar) but all functional behaviour is identical.

**PoC reference files:**
- `~/development/rlike/components/inventory.py` — core logic
- `~/development/rlike/item_functions.py` — potion/spell effects
- `~/development/rlike/io_layer/sidebar_renderer.py` — display
- `~/development/rlike/config/game_constants.yaml` — constants

---

## What already works (don't touch)

- `Inventory.cs` — list-backed component, Add/Remove/FindFirst
- `Consumable.cs` + `ConsumableFactory.cs` — healing potions with correct heal_amount=40
- Auto-pickup on walk-over (`TurnController.TryPickUpItemsAt`)
- `TurnController.TryHeal()` — uses first healing potion from inventory
- `PickUpEvent` / `HealEvent` in `TurnEvent.cs`
- `PlayerAction.UseItem(Entity?)` — action type exists, wired to TurnController
- `CombatLog.cs` — displays pickup/heal messages

---

## What is missing

### Logic layer
1. **Inventory capacity** — no limit enforced (PoC: 25)
2. **Item stacking** — each picked-up potion is a separate entity; PoC stacks same-type consumables
3. **Drop action** — `PlayerAction.DropItem` doesn't exist
4. **Stack-aware drop** — PoC can drop partial stacks

### Presentation layer
5. **InventoryPanel** — doesn't exist; player can't see what they're carrying
6. **Item use via tap** — no UI path from tap → `PlayerAction.UseItem(item)`
7. **Equipment display** — weapon/armor are equipped but invisible in HUD
8. **HUD: floor depth** — not shown
9. **HUD: auto-use feedback** — log line when auto-pickup occurs is fine, but need
   item count visible so player knows they have potions

---

## Scope decisions (what we port now vs defer)

### Port now
- Capacity limit (25)
- Stacking (consumables only — simplest case, needed for display)
- InventoryPanel with tap-to-use
- Equipment summary row (weapon + armor icons, no full equip screen yet)
- HUD floor depth indicator
- Drop (single item / full stack only — partial stack drop deferred)

### Defer
- Item identification system (PoC's dual-toggle pre-ID by difficulty)
- Partial stack drops
- Throw item
- Wand charge system (no wands in game yet)
- Scroll targeting (no scrolls yet)
- Character sheet (XP, level stats)
- Full equipment screen (equip/unequip interaction)

---

## Layout (mobile portrait, bottom HUD)

```
┌──────────────────────────────────┐
│                                  │
│         DUNGEON VIEW             │  ← ~70% of screen height
│         (isometric map)          │
│                                  │
├──────────────────────────────────┤
│  HP ████████░░  54/56  Depth: 2  │  ← HUD strip (top of bottom panel)
├──────────────────────────────────┤
│ INVENTORY           [26/25]      │  ← Header with count
│ [🧪 ×3] [⚔ Dagger] [🛡 Leather] │  ← Item row (tap to use/select)
│  ...more items scroll right...   │
├──────────────────────────────────┤
│  Combat log (3 lines)            │  ← Existing CombatLog
└──────────────────────────────────┘
```

**Item strip behaviour:**
- Items shown as icon sprite + name abbreviation + count (if > 1)
- Tap → immediately use (consumable) or show equip prompt (equippable)
- Consumable items show count badge if stacked
- Strip scrolls horizontally if more items than fit
- Equipped weapon/armor shown with a highlight border

---

## Task breakdown

### Task 1 — Logic: Inventory capacity + stacking [COMPLETE]
**Files:** `src/Logic/ECS/Inventory.cs`

Status: complete
Files changed:
- `src/Logic/ECS/Inventory.cs` — added Capacity=25 const, IsFull, changed Add() to return bool with stacking logic
- `src/Logic/Core/TurnController.cs` — TryPickUpItemsAt respects Add() bool; TryHeal stack-aware (decrement, remove on 0)

Notes:
- Stacking uses name equality: same Name + has Consumable = stack instead of add new slot
- Equippables with same name each get their own slot (no stack match since no Consumable)
- TryHeal now decrements StackSize; removes entity from inventory only when StackSize reaches 0
- Existing TurnControllerTests.ProcessTurn_HealConsumesPotion test still passes (StackSize=1 → removes on use)

---

### Task 2 — Logic: Drop action [COMPLETE]
**Files:** `src/Logic/Core/PlayerAction.cs`, `src/Logic/Core/TurnController.cs`, `src/Logic/Core/TurnEvent.cs`

Changes:
- Add `PlayerAction.DropItem(Entity item)` action type ✓ (`PlayerAction.Drop(item)`)
- Add `DropEvent : TurnEvent` with `ItemId`, `ItemName` ✓
- In `TurnController.ResolvePlayerAction`: handle `DropItem` →
  remove from inventory, place on floor at player position, add to `state.FloorItems` ✓
- Re-register entity in `state.Map` ✓
- Emit `DropEvent` ✓

Status: complete
Files changed:
- `src/Logic/Core/TurnEvent.cs` — added DropEvent
- `src/Logic/Core/PlayerAction.cs` — added DropItem to ActionKind enum, added Drop() factory
- `src/Logic/Core/TurnController.cs` — added DropItem case + ResolveDrop method
- `src/Logic/ECS/GameMap.cs` — added public Entities property (needed for map registration assertion)
- `tests/Core/DropItemTests.cs` — new, 5 tests
- `tests/Core/GameStateFactoryTests.cs` — updated FromScenario_WithPotions_AddsToInventory to match stacking behavior from Task 1
Notes:
- Factory method named Drop() not DropItem() to read cleanly: PlayerAction.Drop(item)
- ResolveDrop silently no-ops if item not found in inventory (safe guard)
- Drop drops the full stack entity (partial stack drop is deferred per plan)
- GameMap.Entities property added as IReadOnlyList<Entity> — presentation layer needs this to render floor items

Tests to write:
- `DropItem_RemovesFromInventory`
- `DropItem_AppearsOnFloor`
- `DropItem_EmitsDropEvent`
- `DropItem_CanBePickedUpAgain`

---

### Task 3 — Logic: Inventory tests (catch-up) [COMPLETE]
**File:** `tests/Core/InventoryTests.cs` (new)

Status: complete
Files changed:
- `tests/Core/InventoryTests.cs` — new file, 9 tests

Tests written:
- `Add_ItemToEmptyInventory_ReturnsTrue`
- `Add_BeyondCapacity_ReturnsFalse_ItemNotAdded`
- `Add_SameNameConsumable_StacksInsteadOfAdding`
- `Add_DifferentConsumables_DoNotStack`
- `Add_EquippableItems_DoNotStack`
- `IsFull_WhenAtCapacity_ReturnsTrue`
- `IsFull_BelowCapacity_ReturnsFalse`
- `TryHeal_StackedPotion_DecrementsStackSize`
- `TryHeal_LastPotion_RemovesFromInventory`

Notes:
- MakeEquippable uses Equippable(EquipmentSlot.MainHand) — there is no separate Weapon class
- TryHeal tests drive through TurnController.ProcessTurn (same pattern as TurnControllerTests)
- Drop places item at player position (after Task 2)

---

### Task 4 — Presentation: Consumable.StackSize + YAML [COMPLETE]
**Files:** `src/Logic/Combat/Consumable.cs`, `src/Logic/Content/ConsumableDefinition.cs`

Status: complete
Files changed:
- `src/Logic/Combat/Consumable.cs` — added `public int StackSize { get; set; } = 1`

Notes:
- Runtime-only property, no YAML changes needed
- Default 1 means all existing code that removes the item after use continues to work (StackSize=1 → remove)

---

### Task 5 — Presentation: InventoryPanel UI
**File:** `src/Presentation/UI/InventoryPanel.cs` (new)

A Godot `Control` node rendered in C#. Responsibilities:
- Display inventory contents from `GameState.Player`
- Show each unique item (by name) with: sprite icon, short name, stack count if > 1
- Tap handler per item → fires `ItemTapped(Entity item)` signal
- Refresh method called after each turn
- No items = show "(empty)" text

Implementation notes:
- Use `HBoxContainer` for item strip (horizontal scroll)
- Each item slot: `TextureRect` (icon) + `Label` (×N count) in a `VBoxContainer`
- Load item sprite from `SpriteMapping.GetItemSpritePath(item.Name)`
- Show equipped items with a different border colour (gold tint on TextureRect)
- Capacity indicator: "N/25" in panel header

Status: complete
Files changed:
- `src/Presentation/UI/InventoryPanel.cs` — new file

Notes:
- Uses C# `event Action<int>? ItemTapped` (not Godot `[Signal]`) — consistent with HUD.ExploreRequested pattern
- Slot layout: Button (tap target) → ColorRect highlight (if equipped) → VBoxContainer → icon + name label + optional ×N count badge
- Icon loading: tries SpriteMapping.GetItemSpritePath → GD.Load<Texture2D> → falls back to ColorRect with category colour (green=consumable, gold=weapon, blue=armor, grey=unknown)
- Name truncated to 8 chars with ellipsis to prevent slot overflow
- Equipped detection checks both MainHand and Chest slots via Equipment component
- Empty inventory shows "(empty)" text and hides the ScrollContainer
- ScrollContainer uses ShowNever for scrollbar — mobile-friendly drag/swipe scrolling
- Build: 0 errors, 2 pre-existing warnings in other files (not introduced by this change)

---

### Task 6 — Presentation: HUD depth + equipment summary [COMPLETE]
**File:** `src/Presentation/UI/HUD.cs`

Status: complete
Files changed:
- `src/Presentation/UI/HUD.cs` — replaced turn counter with depth label, added equipment summary row

Changes:
- Removed `_turnLabel` (low value, takes space — combat log already surfaces action feedback)
- Added `_depthLabel` ("Depth: N") right-aligned in the top row, same slot as the old turn label
- Added `_equipLabel` below the HP bar: "Wpn: Dagger   Arm: Leather Armor"
  - Reads `Equipment.MainHand?.Name` and `Equipment.GetSlot(EquipmentSlot.Chest)?.Name`
  - Shows "—" for empty slots; `Equipment` component missing → both show "—"
  - Names truncated to 12 chars with "…" suffix via static `Truncate()` helper
- `CustomMinimumSize` height bumped 120→145px to accommodate the new row
- `SetState()` and `Refresh()` signatures unchanged — GameController not affected

Notes:
- `EquipmentSlot` is in `UnderWarden.Logic.Combat` — already imported, no new using needed
- The two pre-existing nullable warnings in `Main.cs` / `GameController.cs` are unchanged

---

### Task 7 — Wiring: GameController + Main + input [COMPLETE]
**Files:** `src/Presentation/GameController.cs`, `src/Presentation/Main.cs`

Status: complete
Files changed:
- `src/Presentation/GameController.cs` — added `InventoryPanel? _inventoryPanel` field; added `inventoryPanel` param to `Initialize`; added `HandleInventoryTap(int itemId)` method; added DropEvent handling (re-adds floor sprite via `_itemSprites.CreateSprite`); added `_inventoryPanel?.Refresh(_state!)` call in `ExecuteTurn`
- `src/Presentation/Main.cs` — added `InventoryPanel? _inventoryPanel` field; creates panel in `SetupPresentation`, adds to UILayer, calls `Initialize(state)`; wires `ItemTapped` → `OnInventoryItemTapped` → `GameController.HandleInventoryTap`; passes panel to `GameController.Initialize`
- `src/Presentation/Entities/ItemSpriteManager.cs` — made `CreateSprite` public (needed by GameController for drop re-render)

Notes:
- `HandleInventoryTap` looks up item by ID from `PlayerInventory.FindFirst`, then fires `PlayerAction.UseItem(item)` via `OnActionChosen` (respects WaitingForInput guard)
- InventoryPanel is recreated on each floor transition (parallel to HUD pattern)
- Build: 0 errors, 2 pre-existing warnings (unchanged from before Task 7)

---

### Task 8 — Tests: full integration [COMPLETE]
**File:** `tests/Core/InventoryIntegrationTests.cs` (new)

Status: complete
Files changed:
- `tests/Core/InventoryIntegrationTests.cs` — 4 tests

Tests written:
- `UseItem_Potion_ConsumesAndHeals`
- `DropItem_ThenWalkOver_PicksUpAgain`
- `InventoryFull_PickupIgnored`
- `StackedPotions_UseReducesCount`

Notes:
- All drive through `TurnController.ProcessTurn` — no Godot dependencies
- `InventoryFull` test uses 25 uniquely-named daggers (non-stackable) to hit capacity, then places a potion on the floor at player position and walks onto it — verifies it stays on the floor
- `StackedPotions` pre-builds a StackSize=2 potion and verifies slot survives after UseItem (StackSize becomes 1)

---

## Build order and dependencies

```
Task 1 (Inventory capacity + stacking)
  └─ Task 3 (Inventory logic tests)        ← can run in parallel with Task 2
  └─ Task 4 (Consumable.StackSize)
       └─ Task 5 (InventoryPanel UI)
            └─ Task 6 (HUD depth)          ← can run in parallel with Task 5
                 └─ Task 7 (Wiring)
                      └─ Task 8 (Integration tests)

Task 2 (Drop action) ← parallel with Task 1
  └─ feeds into Task 7 (drop → floor re-render)
```

---

## Out of scope for this milestone

- Item identification
- Wand charges
- Scroll targeting
- Character sheet / XP display
- Full equip/unequip screen
- Multi-item drag or sorting
- Partial stack drops
- Throw item

These will be planned separately when wands, scrolls, and the leveling system land.
