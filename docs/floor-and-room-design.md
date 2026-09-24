# Procedural room generation spec for The Under-Warden

_Last verified: 2026-07-12 against commit 86b6f10_

**Every room in a dungeon tells a micro-story.** The difference between a dungeon that feels designed and one that feels random comes down to a single principle: rooms need *purpose*, not just geometry. Research across dozens of shipped roguelikes — Brogue, DCSS, Shattered Pixel Dungeon, Cogmind, Dead Cells, Hades — reveals that the most praised procedural dungeons all use constrained, multi-pass generation where each layer of detail builds on decisions made by the layer above. The spec below encodes these principles into concrete, implementable rules for a mobile-first roguelike using 24×24px tiles on maps of roughly **36×36 to 48×48 tiles**, targeting **6–12 rooms per floor** with generation under 200ms.

Kate Compton (Spore, UC Santa Cruz) framed the core challenge as the "10,000 Bowls of Oatmeal" problem: mathematically unique outputs that players perceive as identical. Perceptual uniqueness — not mathematical uniqueness — is the metric that matters. A library with bookshelves on the walls, a reading table in the center, and a candle on the desk reads as "designed" even when every element was placed algorithmically. A room with the same objects scattered uniformly reads as noise.

---

## The five-layer generation pipeline

The system uses a **top-down, abstract-to-concrete pipeline** where each layer constrains the next. This architecture is drawn from Unexplored (Joris Dormans' cyclic dungeon generation), Shattered Pixel Dungeon's Initialize→Build→Paint system, and pvigier's constraint satisfaction room generator. Every successful dungeon generator discovered in this research uses some form of multi-pass refinement.

**Layer 1 — Mission graph.** Generate an abstract graph of rooms with semantic types, lock-and-key relationships, and a guaranteed path from entrance to exit. This layer controls *pacing and purpose* — it decides that floor 4 needs a library containing a key that unlocks the vault two rooms away.

**Layer 2 — Spatial layout.** Map the mission graph onto physical space. Place rooms, determine adjacency, carve corridors, and add loops. This layer controls *topology* — whether the level feels labyrinthine or open, linear or branching.

**Layer 3 — Room geometry.** Generate the shape of each room — not just rectangles, but L-shapes, cave blobs, circular chambers, rooms with alcoves. This layer controls *spatial identity*.

**Layer 4 — Floor composition and dressing.** Paint floor tiles with patterns, place props according to room archetype rules, apply environmental details. This layer controls *visual richness and semantic meaning*.

**Layer 5 — Beauty pass.** Smooth edges, add scattered debris, apply theme blending, validate connectivity. This layer controls *polish and cohesion*.

The critical rule: **make the most important decisions first, then get finer with each pass, using the state of previous passes to constrain what you can do.** Archmage Rises' developer Thomas Henshell reports this as the single most important insight from 17 months building a room decoration system.

---

## Layer 2: spatial layout and connectivity

### The loop builder as primary algorithm

Shattered Pixel Dungeon's **LoopBuilder** is the most proven mobile-optimized layout algorithm. It places rooms along a main loop (oval or circle, optionally warped/skewed) with the entrance and exit on opposite ends, then attaches branch rooms off the loop. This guarantees no dead-end frustration while creating interesting topology. The FigureEightBuilder variant uses two intersecting loops for more complex levels.

For levels that should feel more organic, use the **Delaunay triangulation + minimum spanning tree** approach (popularized by TinyKeep). Generate rooms with normally distributed sizes, separate them using physics simulation or iterative pushing, compute a Delaunay triangulation of room centers, extract the MST for guaranteed connectivity, then re-add **10–15%** of remaining triangulation edges to create loops. This produces natural-feeling layouts with good variety.

### Adding loops post-generation

Brogue's loop addition algorithm dramatically improves any tree-structured dungeon: after initial generation, scan every wall cell. If both sides are floor, belong to different rooms, and are more than **20 cells apart** via A* pathfinding, place a door. Cap at **30 loops** per level after up to **500 attempts**. This single technique converts frustrating backtracking into tactical choice.

### Room connectivity rules

Corridors should connect room centers with **L-shaped paths** (one horizontal segment, one vertical). Corridor width defaults to **1 tile** but widens to **2–3 tiles** at intersections. Bob Nystrom's "rooms and mazes" technique fills remaining space with a growing-tree maze, connects rooms to adjacent maze regions, adds occasional extra connections (1-in-N chance), then iteratively removes dead ends — guaranteeing every corridor leads somewhere. For Yarl, a simpler approach works: connect rooms via L-shaped corridors, then remove any corridor tile that would create a dead end longer than 3 tiles.

### Layout variety through level feelings

Borrow Shattered Pixel Dungeon's **level feelings** system. Each floor has a ~50% chance of being "normal" and a ~50% chance of receiving one modifier from: WATER (30% of floor tiles become shallow water), GRASS (organic patches via cellular automata), DARK (reduced vision radius), LARGE (+50% rooms), TRAPS (triple trap density), CHASM (gaps requiring bridging), SECRETS (hidden rooms). This generates massive variety at zero room-design cost.

### Recommended layout parameters

| Parameter | Value |
|---|---|
| Map size | 36×36 to 48×48 tiles |
| Rooms per floor | 6–12 (sewers: 6–8, halls: 8–12) |
| Room size: small | 3×3 to 6×6 (40% of rooms) |
| Room size: medium | 7×7 to 12×12 (45% of rooms) |
| Room size: large | 13×13 to 15×20 (15% of rooms) |
| Corridor width | 1–2 tiles |
| Loop re-addition rate | 12% of non-MST edges |
| Max dead-end length | 3 tiles before pruning |

---

## Layer 3: room shape generation

Rectangular rooms are the most common mistake in procedural dungeon generation. Brogue's developer Brian Walker found plain rectangles "too boring" and made **every room** use the cross-room template (two overlapping rectangles). The shape system should produce rectangles only **30%** of the time, with the remaining 70% distributed across more interesting forms.

### The two-rectangle union method

The simplest and most versatile technique, used extensively in Angband variants with 23+ room types. Generate two random rectangles within a bounding box and take their union. Depending on overlap position, this naturally produces simple rectangles (when one encloses the other), **L-shapes** (corner overlap), **T-shapes** (edge-to-middle overlap), and **cross/plus shapes** (centered overlap). Bias toward symmetry in the random parameters encourages more interesting shapes. This runs in O(w×h) time per room — ideal for mobile.

```
1. Define bounding box matching BSP leaf or allocated space
2. Generate rect_A: random position and size within bounds
3. Generate rect_B: random position and size within bounds
4. Room floor = rect_A ∪ rect_B
5. Surround union with walls
```

### Cellular automata caves

For organic cave rooms, use the canonical **4-5 rule**: fill a grid randomly (each cell is wall with **45% probability**), then iterate 4–5 times. Each iteration: a wall cell stays wall if ≥4 of its 8 neighbors are walls; an open cell becomes wall if ≥5 neighbors are walls. Post-process by flood-filling to find the largest connected region and discarding isolated pockets. At 24px tiles, a 15×20 bounding box gives 300 cells — plenty for interesting cave shapes.

**The isolation problem and its solution:** Pin certain cells as "definitively alive" (floor, cannot change) before running CA — specifically room cores and corridor connection points. This guarantees connectivity while letting CA create organic edges. pvigier calls this the "four-state CA": definitively dead, dead, alive, definitively alive. It is the gold standard for mixing caves with constructed rooms.

### Rooms with alcoves and niches

After generating a base room shape, scan wall segments longer than 3 tiles. Each segment has a **15–30% chance** of receiving a 1–2 tile deep niche, 1–3 tiles wide. Niches serve as treasure spots, ambush points, or decorative alcoves. The additive variant extrudes small rectangles outward from walls, creating closets and storage bays. Only apply to rooms 8×8 or larger — small rooms don't benefit.

### Circular and oval rooms

Use `(x-cx)² + (y-cy)² ≤ R²` for circles and `(x-cx)²/a² + (y-cy)²/b²  ≤ 1` for ovals. At 24px tile resolution, circles below radius 3 (diameter 7) look too blocky. Radius **4–8** produces good results. Optionally run 1–2 CA smoothing passes to soften staircase edges for a more natural stone-carved appearance.

### Pillar and column formations

For rooms 8×8 and larger, place internal structural elements using formation types: **circle formation** (pillar ring via midpoint circle), **cross formation** (vertical + horizontal lines at center), **grid formation** (regular pillar grid every N tiles), **perimeter formation** (pillars along an inner ring offset 2 tiles from walls). Always flood-fill from the entrance after placement to verify all floor tiles remain reachable.

### Shape selection weights

| Shape Type | Weight | Min Room Size | Best For |
|---|---|---|---|
| Simple rectangle | 30% | 3×3 | Any |
| Two-rectangle union (L/T/cross) | 30% | 5×5 | Constructed areas |
| Cellular automata blob | 15% | 7×7 | Cave sections |
| Rectangle with alcoves | 10% | 8×8 | Storage, crypts |
| Circle/oval | 8% | 7×7 | Shrines, boss arenas |
| Long thin corridor-room | 7% | 3×8 | Connectors, hallways |

---

## Layer 4a: floor composition and tile patterns

Floor tiles are where "designed" feeling lives or dies. A room with uniform floor tiles feels like a placeholder. A room where the floor darkens near walls, shows a worn path between doors, and has a faint checkered pattern in the center reads as intentional. The Oryx 16-Bit Fantasy tileset provides enough tile variants to support this — the system needs **3–4 floor variants per theme** plus transition tiles.

### The seven-pass floor decoration pipeline

**Pass 1 — Base fill.** Lay the primary floor tile for the room's theme across all floor cells.

**Pass 2 — Edge darkening.** Compute `distanceFromWall` (Manhattan distance to nearest wall) for each tile. Distance 0: use the darkest floor variant (shadow from walls). Distance 1: use a slightly darker variant. Distance 2+: keep the standard tile. This single technique creates an enormous amount of visual depth with zero design effort.

**Pass 3 — Floor pattern.** Based on room archetype, optionally apply a geometric pattern. Throne rooms and temples get **checkered floors** (`(x+y) % 2` alternation between two tile variants). Libraries and treasuries get **bordered floors** (a 1-tile-wide darker border inset from walls, lighter center). Ritual rooms get **centered medallions** (circular accent using distance-from-center). Not every room needs a pattern — apply to roughly **25%** of medium and large rooms.

**Pass 4 — Noise-driven variation.** Sample 2D simplex noise at each tile position with **frequency 0.2–0.3** and use thresholds to select from floor variants: values below −0.3 get the worn variant (~20%), values above 0.6 get the accent variant (~10%), everything between gets the standard variant. Use different seeds for each detail layer so they don't correlate. This creates natural-looking clusters of variation.

**Pass 5 — Worn paths.** For each pair of doors in a room, compute an A* path between them. Mark tiles on the path as "high traffic" with an 80–100% chance of receiving worn/polished stone variants. Tiles 1 cell adjacent to the path get a 40–60% chance. Add ±1 tile jitter via noise to avoid perfectly straight wear lines.

**Pass 6 — Environmental details.** Place puddles, cracks, rubble, and moss using independent noise layers. Puddles appear where `water_noise > 0.7` AND `distFromWall ≤ 2`. Cracks appear where `crack_noise > 0.65`, weighted toward walls. Rubble clusters near walls and corners with probability `max(0, 0.4 − 0.1 × distFromWall)`. Never place environmental details on worn paths or in doorway zones.

**Pass 7 — Scatter decoration.** Place purely visual, non-blocking overlay decorations (dust, small bones, fallen leaves) on **2–5%** of remaining empty floor tiles. These occupy the visual layer but do not block pathfinding.

### Noise parameter reference

| Detail Type | Frequency | Threshold | Coverage |
|---|---|---|---|
| Floor variant selection | 0.2–0.3 | −0.3 / 0.6 | ~20% worn, ~10% accent |
| Moss/growth patches | 0.1–0.15 | 0.6 | ~15% where eligible |
| Crack distribution | 0.3–0.4 | 0.65 | ~8% near walls |
| Puddle placement | 0.1–0.15 | 0.7 | ~5% near walls |
| Rubble clustering | 0.2 | 0.5 | ~10% near walls/corners |

---

## Layer 4b: room dressing and prop placement

This is the system that transforms empty geometry into meaningful spaces. The research converges on a **recipe + constraint satisfaction** model: each room archetype defines required and optional props, and a CSP solver places them while maintaining navigability.

### Twelve room archetypes with prop rules

Every room generated by the mission graph receives an archetype tag. Each archetype defines anchor props (must place, or regenerate), optional props (attempt to place), density targets, and symmetry preferences. Below are the core archetypes with their defining characteristics.

**Library** — density 40–60%, size 5×5+. Required: bookshelves (wall-adjacent, 2–6), reading table (center). Optional: chairs adjacent to table, candelabra, globe (corner), desk+chair cluster. Bookshelves line walls contiguously.

**Armory** — density 30–45%, size 5×5+. Required: weapon racks (wall-adjacent, 2–4), armor stand (free-standing, 1–2). Optional: training dummy (center, needs 2-tile clear radius), workbench, chest (corner). Weapon racks on the longest walls.

**Kitchen/Dining** — density 35–50%, size 6×6+. Required: hearth (wall, opposite entrance), table (center). Optional: chairs around table (2–6), barrel cluster in corner (2–4), shelving near hearth. Barrels always cluster in groups.

**Throne room** — density 15–25%, size 8×8+. Required: throne (center of wall farthest from entrance). Optional: carpet runner (door to throne), pillars (symmetrical pairs), braziers (flanking throne). **Bilateral symmetry mandatory.** Deliberately sparse for grandeur.

**Prison** — density 45–60%, size 5×5+. Required: cell bars/iron door (internal partition, 1–4), chains (wall). Optional: cage, shackles, straw pile, bucket, skeleton. Subdivide room into 2×2 or 3×3 cells with a narrow corridor between them.

**Laboratory** — density 40–55%, size 5×5+. Required: alchemy table (center or wall), cauldron (center). Optional: shelves with bottles, bookshelf, brazier near cauldron. Central cauldron as focal point.

**Shrine/Temple** — density 20–35%, size 5×5+. Required: altar (center-back or center). Optional: candles flanking altar in pairs (2–8), statue, prayer mat, pews facing altar. **Symmetry preferred.** Clear approach from entrance to altar.

**Storage** — density 55–75%, size 3×3+. Required: crate cluster (3–8), barrel cluster (2–6). Optional: shelving (wall), sacks, chest (corner). Crates form rectangular clusters (2×2, 2×3, L-shapes). One aisle must always connect door to back wall.

**Bedroom** — density 30–45%, size 3×3+. Required: bed (wall-adjacent, head against wall). Optional: chest (foot of bed), nightstand (adjacent to bed head), desk+chair cluster, wardrobe (wall perpendicular to bed wall). Bed never blocks door.

**Crypt** — density 25–40%, size 5×5+. Required: sarcophagus (center, 1–3) OR tombstones (rows, 2–6). Optional: urns, candelabra flanking sarcophagus, cobwebs (corners). Sarcophagi in center row with aisles between. Symmetry preferred.

**Fountain room** — density 15–25%, size 6×6+. Required: fountain (exact center). Optional: benches at cardinal points, pillars at corners, planters along walls. Radial symmetry.

**Forge** — density 35–50%, size 5×5+. Required: forge (wall-adjacent), anvil (1–2 tiles from forge). Optional: water barrel (within 2 tiles of forge), weapon rack, tool rack, coal pile. Clear workspace in front of anvil.

### The seven-pass placement algorithm

**Pass 0 — Initialize grid.** Mark wall cells as FULL, door-adjacent cells (1 tile each direction from every door) as MARGIN (keep clear), everything else as EMPTY.

**Pass 1 — Focal/center props.** Place required center props sorted by size descending. Constrain to the center 60% of room area. For symmetrical archetypes, snap to center axis. After each placement, mark occupied cells as FULL and surrounding cells as MARGIN. **Run connectivity validation** — flood-fill from every door must reach every other door.

**Pass 2 — Wall-adjacent props.** Find all valid wall segments (contiguous EMPTY cells touching wall cells). Filter segments shorter than the prop width and segments adjacent to doors. Sort by length descending, fill longest walls first. Mark occupied cells FULL plus 1-tile MARGIN in front. Validate connectivity.

**Pass 3 — Clustered props.** Place composite groups: table surrounded by chairs, barrel triangle, bed+nightstand+chest. Treat each cluster as a single unit with a composite collision box. Place the anchor prop first, then satellites at defined offsets with **60–90%** individual probability and ±1 pixel jitter for organic feel.

**Pass 4 — Optional props.** For each optional prop, roll against its chance value. On success, attempt placement at a random valid position matching the prop's constraint (wall, corner, free, etc.). Maximum **10 attempts** per prop. Skip on failure rather than backtracking.

**Pass 5 — Fillable props.** For placed props marked as fillable (bookshelves, tables, shelves), populate their anchor points with small items matching the archetype's fillable tags (books, bottles, candles, plates) at a rate controlled by the room's clutter density.

**Pass 6 — Scatter decoration.** Place non-blocking visual decorations (bones, rubble, cobwebs, dust) on **2–10%** of remaining empty floor tiles, avoiding door adjacency zones.

**Pass 7 — Best-of-N selection.** Generate the room **10–15 times**, score each by `(required_props_placed × 10) + (optional_props_placed × 3) + (symmetry_score × 5) − (blocked_path_penalty × 100)`, and keep the highest-scoring result. Archmage Rises explicitly uses this technique for quality assurance.

### Density and spacing constraints

**Minimum 40% of walkable area must remain empty** in every room type — this preserves space for combat, movement, and tactical play. At least one 2×2 clear area must exist for player maneuvering. Door adjacency zones (1 tile in each direction from every door) must always remain clear. The system must run A* pathfinding from every door to every other door after all placement to confirm navigability.

The clutter state modifier adjusts base density: well-maintained rooms get **×0.8** (organized, aligned), normal rooms get **×1.0**, neglected rooms get **×1.1** with ±15% position jitter, abandoned rooms get **×1.2** with increased scatter, and ruined rooms get **×0.7** for intact props (fewer survive) plus **×2.0** scatter budget (rubble, debris everywhere).

### Small room special handling

Rooms with walkable area ≤9 tiles (3×3): maximum 1 prop, 1×1 only. Walkable area ≤16 (4×4): maximum 2 props, up to 1×2. Walkable area ≤25 (5×5): maximum 4 props, up to 2×2. Skip cluster placement, skip symmetry, ensure at least 50% of walkable tiles remain empty.

---

## Layer 5: theme blending across dungeon floors

Theme transitions should feel like geological strata shifting — gradual, organic, and inevitable. The system defines each theme as a data structure containing floor tile variants, wall tile variants, accent tiles, detail objects, ambient color, and an enemy pool. A **theme progression table** maps floor numbers to theme weights.

### The progression table

```
Floors 1–2:   Stone Catacombs (100%)
Floor 3:      Stone 80% / Dirt 20%
Floor 4:      Stone 50% / Dirt 50%
Floors 5–6:   Dirt Caverns (100%)
Floor 7:      Dirt 80% / Moss 20%
Floor 8:      Dirt 50% / Moss 50%
Floors 9–10:  Mossy Depths (100%)
Floor 11:     Moss 70% / Ice 30%
...continue for Ice, Lava, Crystal
```

### Per-room assignment with noise-blended transitions

The recommended approach combines room-level theme assignment with tile-level noise blending at boundaries. On pure floors (100% one theme), every room uses that theme. On transition floors, each room rolls against the weight: `if random() < primary_weight, use primary theme; else use secondary`. Rooms adjacent to a differently-themed room become **transition rooms** where per-tile noise blending activates.

Within transition rooms, each tile independently decides its theme:

```
theme_weight = get_depth_weight(floor_number)
noise = simplex_noise(x × 0.07, y × 0.07, floor_seed)
blended = clamp(theme_weight + noise × 0.3, 0.0, 1.0)
if random() < blended: use primary theme tile
else: use secondary theme tile
```

The **noise frequency of 0.05–0.1** creates large organic patches of 5–15 tiles, preventing a salt-and-pepper appearance. The noise amplitude of **±0.3** allows gentle variation around the threshold. After placement, run a cleanup pass: any isolated single tile of one theme surrounded entirely by another theme gets replaced with the majority — eliminating visual noise.

### Transition tiles and autotiling

Where two theme tiles meet, use a **15-piece transition tileset** per adjacent theme pair (4 edges + 4 inner corners + 4 outer corners + 2 diagonal pieces + 1 isolated). With 5 dungeon themes and transitions only between adjacent pairs in the progression (stone↔dirt, dirt↔moss, moss↔ice, ice↔lava), that is **4 transition tilesets × 15 tiles = 60 transition tiles** total. Non-adjacent theme pairs (stone meeting moss) route through intermediate themes using BFS on a material compatibility graph — the system automatically inserts a 1-tile dirt buffer zone between stone and moss.

### Environmental storytelling shifts

As themes progress deeper, environmental details evolve. Stone floors have torch sconces, clean walls, and cobwebs. Dirt floors shift to bioluminescent mushrooms, crumbling supports, and root systems. Moss floors introduce puddles, overgrown roots, and heavy vegetation patches. Ice floors bring frozen puddles, frost crystals, and stalactites. Detail density should **increase by ~10% per floor** — deeper dungeons feel more alive and dangerous.

---

## Lessons encoded from the best roguelikes

Eight games provided the most actionable insights for this system. **Brogue** proved that avoiding plain rectangles entirely (every room uses two overlapping rectangles) and adding post-generation loops transforms dungeon quality. Its flood-fill passability check for water and terrain features is essential — never place anything that breaks connectivity. **DCSS** demonstrated that even 20–30 hand-designed "vault" rooms tagged with depth and biome metadata, mixed into a procedural framework, dramatically elevate perceived quality. Its "ruin" tag — programmatically damaging vault edges for organic integration — is a low-cost high-return technique.

**Shattered Pixel Dungeon** is the most directly relevant reference as a mobile roguelike. Its three-phase Initialize→Build→Paint architecture cleanly separates room selection from spatial layout from visual rendering. The LoopBuilder placing rooms along a main loop with branches is proven for mobile screens and should be adopted directly. Its **100+ room types** across Standard, Special, and Hidden categories on a **32×32 tile grid** show what's achievable.

**Cogmind** contributed the insight that measuring dungeon metrics (connectivity ratio, dead-end count, room-to-corridor ratio) and auto-rejecting maps below quality thresholds is essential for consistent output. **Dead Cells** showed that defining abstract "concept graphs" per biome — specifying path length, branching factor, and special room count independent of geometry — creates strong area identity cheaply. **Hades** proved that rooms escalating in size within a biome create natural difficulty curves, and that dynamic enemy spawning within fixed room geometry makes even repeated layouts feel fresh.

**Spelunky's** template grid with guaranteed critical path and randomizable chunks within templates remains one of the most elegant solutions for ensuring solvability. For Yarl, this concept applies at the room level: design rooms with fixed structural elements and variable decoration zones.

---

## Concrete implementation architecture

### Data structures

```
RoomArchetype {
  name: string
  size_range: {min: (w,h), max: (w,h)}
  density: (min_float, max_float)
  symmetry: NONE | BILATERAL | RADIAL
  shape_preference: [RECTANGLE, UNION, CAVE, CIRCLE, ALCOVE]
  required_props: [{tags, placement, count_range, priority}]
  optional_props: [{tags, placement, count_range, chance}]
  clusters: [{anchor_tags, satellite_tags, pattern, density}]
  floor_pattern: NONE | CHECKERED | BORDERED | MEDALLION
  fillable_tags: [string]
  scatter_tags: [string]
  scatter_density: float
}

Theme {
  name: string
  floor_tiles: [tile_id × 4 variants]
  wall_tiles: [tile_id × 3 variants]
  accent_tiles: [tile_id]
  detail_objects: [tile_id]
  transition_sets: {adjacent_theme: tileset_15}
  environmental_details: {type: probability}
}

DungeonFloor {
  theme_primary: Theme
  theme_secondary: Theme | null
  theme_weight: float
  feeling: NORMAL | WATER | GRASS | DARK | LARGE | TRAPS | SECRETS
  room_count_range: (min, max)
  layout_type: LOOP | FIGURE_EIGHT | GRAPH
}
```

### Generation sequence

The complete generation sequence for one dungeon floor executes in order: select biome parameters and level feeling from the progression table; initialize the room list with entrance, exit, 4–8 standard rooms (archetype assigned by depth-weighted random selection), 1–2 special rooms, and 0–1 secret rooms; build the spatial layout using the LoopBuilder with entrance and exit on opposite ends; generate room shapes using the weighted shape selection table; run constrained cellular automata for any cave-themed rooms, pinning corridor connection points; paint floors using the seven-pass decoration pipeline; dress rooms using the seven-pass prop placement algorithm with best-of-10 selection; apply theme blending on transition floors using per-tile noise; add loops between distant rooms using Brogue's wall-scanning technique; validate with flood-fill connectivity and dungeon metrics; and finally retry the entire floor if metrics fall below thresholds (max 5 retries).

### Quality validation metrics

Reject and regenerate any floor where: fewer than 90% of floor tiles are reachable from the entrance via flood fill; any door is unreachable from any other door; the ratio of corridor tiles to room tiles exceeds 2:1 (too much hallway) or falls below 0.3:1 (rooms too packed together); more than 2 dead ends exist longer than 3 tiles; or the longest path between entrance and exit is less than 1.5× the Manhattan distance (level is too direct and boring).

---

## Conclusion: the three rules that matter most

All of this research — across dozens of games, papers, and developer postmortems — distills to three non-negotiable principles. First, **every room needs a purpose** that drives its shape, contents, and floor treatment; the archetype system ensures this by making "library" or "forge" the first decision, not the last decoration. Second, **constrain before randomizing** — the more rules a room must satisfy (walls have shelves, center has a focal point, paths between doors stay clear, props cluster logically), the more "designed" the output reads to players, because real designers impose exactly these kinds of constraints. Third, **multi-pass refinement from abstract to concrete** prevents the combinatorial explosion that makes purely bottom-up generation produce noise; deciding "this is a moss-themed shrine floor with a checkered pattern" before placing any individual tile means every tile reinforces the same intention.

The system as specified produces rooms across a rich continuum: 3×3 closets with a single chest, L-shaped libraries with contiguous bookshelves and a reading nook, circular shrines with medallion floors and candle-flanked altars, organic cave chambers where cellular automata sculpted the walls, and 15×20 throne rooms with bilateral symmetry and carpet runners. Each room participates in a floor-wide theme that gradually shifts through geological strata across the dungeon's depth. The result should be rooms that players look at and think "someone designed this" — which is the highest compliment a procedural system can receive.