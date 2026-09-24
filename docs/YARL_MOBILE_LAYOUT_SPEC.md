# YARL mobile portrait layout spec

_Last verified: 2026-07-12 against commit 86b6f10_

This document describes the target UI layout for The Under-Warden (YARL) running in portrait mode on mobile (primarily iPhone). The game is a tap-to-move isometric roguelike. The layout is designed for one-handed play with the thumb naturally resting in the lower third of the screen.

## Design principles

- One-handed portrait play: all frequent actions must be reachable by thumb
- Minimal chrome: the viewport dominates; UI stays out of the way until needed
- Tap-to-interact: no dedicated buttons for contextual actions (stairs, wait, pick up) — tap the object or tap yourself to wait
- Touch targets: minimum 44pt for all interactive elements (Apple HIG)
- Information hierarchy: show only what's needed for moment-to-moment play; everything else behind a panel

## Screen zones (top to bottom)

### 1. Status bar (~7% of screen height)

A single compact row at the very top containing:

- **HP bar**: a green fill bar spanning most of the row width, with current/max numbers overlaid (e.g. "64/66")
- **Depth indicator**: small text to the right of the HP bar (e.g. "D:1")

That's it. No buttons in this row — it's pure status. Keep it as slim as possible.

### 2. Game viewport (~63% of screen height)

The isometric game world. This is the largest zone and where the player's eyes spend most of their time. The viewport has four overlay elements:

#### Minimap (top-right corner)
- Semi-transparent background (rgba with ~0.8 alpha)
- ~48x48px, rounded corners
- Shows dungeon layout with a yellow dot for player position
- Tap to expand to a full map view
- Bordered with a subtle 0.5px border

#### Message button (bottom-left corner)
- Same visual style as the minimap: semi-transparent background, same size (~32x32px), rounded corners, 0.5px border
- Icon: a small "lines of text" icon suggesting a message log
- Tap to expand the full message history
- Visually mirrors the minimap — they're a matched pair of viewport overlays

#### Toast messages (bottom-left of viewport, above the Msg button)
- Appear when events happen (potion use, identification, combat, etc.)
- Semi-transparent dark background with rounded corners
- Colored left border to indicate message type (green for positive, red for important/danger)
- Fade out after a few seconds
- Anchored to bottom-left so they don't obscure the center of the action
- Tapping the Msg button brings back the full log

#### Enemy HP bars (floating above enemy sprites)
- Small red bars floating directly above each visible enemy sprite
- Only appear when an enemy has taken damage (don't clutter peaceful rooms)
- Proportional fill showing remaining HP
- No text — just the bar. Keep it minimal.
- This replaces any need for a separate enemy status HUD element

### 3. Quick-slot bar (~12% of screen height)

A single row at the top of the thumb zone. This is the player's primary action bar for consumables.

#### Weapon indicator (far left, separated)
- ~40x42px slot with a subtle visual separator (1px line) between it and the consumable slots
- Shows an icon indicating current weapon type (sword for melee, bow for ranged)
- Small label below the icon ("melee" or "ranged")
- **Tap**: toggle between equipped melee and ranged weapon
- **Long-press**: open weapon detail popup
- This is NOT an equipment display — it's a quick toggle. Full equipment lives behind the Gear panel.

#### Consumable slots (5 slots, filling the remaining width)
- Each slot ~42x42px with rounded corners (6px radius)
- Subtle colored background tint matching the item type (red-ish for health potions, blue for mana, etc.)
- Item icon centered in the slot
- Quantity badge in bottom-right corner (e.g. "x3")
- **Tap**: use the item
- **Long-press**: show item details
- Empty slots show a dashed border with a "+" icon, indicating the player can assign an item from inventory
- Items are assigned via the Gear/inventory panel; the quick-slots are just shortcuts to use them

### 4. Menu buttons (~10% of screen height)

Two wide buttons spanning the full width, side by side:

- **Gear**: opens the equipment/inventory panel (full-screen overlay)
- **Explore**: opens the exploration/autoexplore options

Each button is ~34px tall with rounded corners, centered text labels. They should be wide enough to comfortably tap with a thumb — the two-button layout ensures each gets roughly half the screen width.

### 5. Bottom safe area (~4%)

Padding for iPhone home indicator / gesture bar. No interactive elements here.

## Panel overlays

When the player taps Gear, Explore, or Msg, these open as **full-screen overlays** on top of the game viewport. Not floating modals, not partial sheets — full screen. This avoids awkward dead zones on mobile and gives maximum space for inventory management, equipment slots, message history, etc.

The existing equipment panel design (with the body-slot layout showing Head, Neck, R. Ring, L. Ring, Main Hand, Body, Off Hand, Feet) is good. When making it full-screen:
- Keep the stat summary at top (HP, ATK, HIT, AC)
- Body-slot grid in the middle
- "IN PACK" inventory list below
- Close button (X) in top-right corner, large enough for touch (44pt minimum)

## What we're NOT doing

- No virtual d-pad or joystick — movement is tap-to-move on the viewport
- No dedicated stairs/descend button — tap stairs once to walk to them, tap again to descend
- No dedicated wait button — tap on the player character to wait
- No permanent equipment display on the main screen — that's what the Gear panel is for
- No separate enemy status panel — HP bars float on the sprites themselves
- No debug overlay in the production layout (dev builds can toggle it)

## Visual style notes

- Dark theme throughout (dark navy/charcoal backgrounds)
- Semi-transparent overlays for viewport elements (minimap, msg button)
- Colored borders/tints to distinguish item types in the quick-bar
- Monospace font for numerical values (HP, depth, quantities)
- Toast messages use a left-border color accent rather than full background color

## Implementation notes

This layout is a UI/chrome change — it does not require changes to the game logic, ECS, rendering pipeline, or input handling beyond the existing tap-to-move system. The main work is:

1. Restructuring the HUD layout to match these zones
2. Moving the quick-bar from a small corner element to a full-width row
3. Adding the weapon toggle indicator
4. Implementing floating enemy HP bars above sprites
5. Restyling toast messages with the left-border accent
6. Making the Msg button a viewport overlay (bottom-left)
7. Ensuring all panels (Gear, Explore, Msg log) open as full-screen overlays
8. Respecting 44pt minimum touch targets everywhere

The viewport size reduction (from ~80% to ~63% of screen) is the single biggest change and the one that makes everything else possible. The player character should remain centered in the viewport — the reduced height just means less empty floor visible above and below.

## Reference

- Rogue Wizards: landscape mobile roguelike with a similar action bar at the bottom (skills/items as circular icons). Good reference for quick-slot sizing and spacing.
- Shattered Pixel Dungeon: portrait mobile roguelike. Good reference for how much viewport you actually need and how inventory panels work as overlays.
