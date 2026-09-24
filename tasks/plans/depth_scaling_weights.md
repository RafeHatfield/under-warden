# Plan: Depth-Scaling Spawn Weights

**Status:** READY
**PoC reference:** `~/development/rlike/services/spawn_service.py`, `~/development/rlike/config/etp_config.yaml`, `~/development/rlike/random_utils.py`

---

## What this plan does

Makes monster mix vary by depth. Currently every depth rolls from the same static pool (orc@80, orc_grunt@80, orc_brute@15/depth≥3, zombie@20/depth≥10). The PoC uses a depth-threshold lookup function (`from_dungeon_level`) to make weights ramp up or down as the player descends. Also wires the already-parsed `EncounterBudget` from level templates into room spawning (currently discarded).

---

## PoC design reference

### `from_dungeon_level(table, depth)`
Lives in `~/development/rlike/random_utils.py`:
```python
def from_dungeon_level(table, dungeon_level):
    for value, level in reversed(table):
        if dungeon_level >= level:
            return value
    return 0
```
Table format: `[[weight_at_depth, min_depth], ...]` — returns the value whose `min_depth` is the highest still `<= depth`. Returns 0 if no entry applies (monster excluded from pool entirely).

### PoC spawn weight tables (from `spawn_service.py`)
```python
"orc":          80                                        # constant
"troll":        from_dungeon_level([[15,3],[30,5],[60,7]])
"zombie":       from_dungeon_level([[20,10],[40,13],[60,16]])
"slime":        from_dungeon_level([[20,2],[40,4],[60,6]])
"large_slime":  from_dungeon_level([[5,3],[15,5],[25,7]])
"giant_spider": from_dungeon_level([[15,8],[30,11],[45,14]])
```
Orc variants (brute, shaman, skirmisher) are resolved *after* picking "orc" via `_resolve_orc_variant()`. In C# we model these as separate pool entries — that's fine, just needs matching probability math (see entities.yaml section below).

### PoC ETP band room budgets (from `etp_config.yaml`)
| Band | Depths | room_etp.max |
|------|--------|--------------|
| B1   | 1–5    | 50           |
| B2   | 6–10   | 100          |
| B3   | 11–15  | 150          |
| B4   | 16–20  | 200          |
| B5   | 21–25  | 300          |

C# already has `DefaultRoomEtpMax = 50` which matches B1. But depths 6+ never escalate because `EncounterBudget` is parsed but never passed to `FillRooms`. This plan fixes that.

---

## Changes required

### 0. Make `SpawnWeight` nullable — prerequisite for orc_grunt

**File:** `src/Logic/Content/MonsterDefinition.cs`

Change:
```csharp
public int SpawnWeight { get; set; } = 0;
```
To:
```csharp
public int? SpawnWeight { get; set; }
```

**Why this is required:** `ContentLoader.Merge` uses `child.SpawnWeight != 0` as the sentinel for "was explicitly set." Because YAML defaults to 0 when the field is absent, there is no way to distinguish `spawn_weight: 0` (deliberately zeroed) from an omitted field — both produce `SpawnWeight = 0`. Without this fix, setting `spawn_weight: 0` on `orc_grunt` falls through to the parent value of 80 and orc_grunt continues to spawn procedurally.

**File:** `src/Logic/Content/ContentLoader.cs` — update `Merge`:
```csharp
SpawnWeight = child.SpawnWeight ?? parent.SpawnWeight,
```

**File:** `src/Logic/Core/EntityPlacer.cs` — update the `SpawnWeight > 0` guard:
```csharp
// Before: d!.SpawnWeight > 0
// After:
(d!.SpawnWeight ?? 0) > 0
```

And in the weighted pool fallback weight resolution:
```csharp
int weight = d!.DepthWeights != null
    ? SpawnUtils.FromDungeonLevel(d.DepthWeights, depth)
    : (d.SpawnWeight ?? 0);
```

---

### 1. `SpawnUtils.FromDungeonLevel()` — new file

**File:** `src/Logic/Balance/SpawnUtils.cs`

```csharp
namespace UnderWarden.Logic.Balance;

public static class SpawnUtils
{
    /// <summary>
    /// Port of Python prototype's from_dungeon_level() in random_utils.py.
    /// table: rows of {Weight, MinDepth} sorted ascending by MinDepth.
    /// Returns the Weight of the last row whose MinDepth &lt;= depth.
    /// Returns 0 if no row qualifies — monster is excluded from the pool at this depth.
    ///
    /// IMPORTANT: table must be sorted ascending by MinDepth. Unsorted tables
    /// produce silent wrong results. ContentLoader validates order at load time.
    /// </summary>
    public static int FromDungeonLevel(IReadOnlyList<DepthWeightEntry> table, int depth)
    {
        int result = 0;
        foreach (var entry in table)
        {
            if (depth >= entry.MinDepth)
                result = entry.Weight;
        }
        return result;
    }
}
```

**File:** `src/Logic/Content/ContentLoader.cs` — add validation in `LoadMonsters` (after deserialization, before returning):

```csharp
// Validate depth_weights tables are sorted ascending by min_depth
foreach (var (id, def) in resolved)
{
    if (def.DepthWeights == null) continue;
    for (int i = 1; i < def.DepthWeights.Count; i++)
    {
        if (def.DepthWeights[i].MinDepth <= def.DepthWeights[i - 1].MinDepth)
            throw new InvalidOperationException(
                $"Monster '{id}': depth_weights must be sorted ascending by min_depth. " +
                $"Entry {i} (min_depth={def.DepthWeights[i].MinDepth}) is not greater than " +
                $"entry {i-1} (min_depth={def.DepthWeights[i-1].MinDepth}).");
    }
}
```

---

### 2. `DepthWeightEntry` model + `DepthWeights` field on `MonsterDefinition`

**File:** `src/Logic/Content/MonsterDefinition.cs`

Add `DepthWeightEntry` class (alongside `WeightedItem`, `MonsterStats`, etc.):
```csharp
/// <summary>
/// One row of a depth-weight progression table.
/// YAML: depth_weights: [{weight: int, min_depth: int}]
/// Entries must be in ascending min_depth order — ContentLoader validates this.
/// </summary>
public sealed class DepthWeightEntry
{
    [YamlMember(Alias = "weight")]
    public int Weight { get; set; }

    [YamlMember(Alias = "min_depth")]
    public int MinDepth { get; set; } = 1;
}
```

Add field to `MonsterDefinition`:
```csharp
/// <summary>
/// Optional depth-progression table for spawn weight.
/// If set, overrides SpawnWeight — weight is resolved per depth via SpawnUtils.FromDungeonLevel.
/// Mirrors the from_dungeon_level pattern from PoC spawn_service.py.
///
/// Note on min_depth + depth_weights: both can coexist. min_depth on the definition is a hard
/// pre-filter gate (monster excluded from allDepthEligible before weight resolution).
/// depth_weights handles the ramp. For monsters using depth_weights, set the definition's
/// min_depth to match the first entry's min_depth — it's redundant but serves as documentation.
/// </summary>
[YamlMember(Alias = "depth_weights")]
public List<DepthWeightEntry>? DepthWeights { get; set; }
```

**File:** `src/Logic/Content/ContentLoader.cs` — add to `Merge`:
```csharp
DepthWeights = child.DepthWeights ?? parent.DepthWeights,
```

---

### 3. `EntityPlacer.FillRooms` — resolve depth-scaled weights

**File:** `src/Logic/Core/EntityPlacer.cs`

Change the weighted pool construction (currently lines ~179–182) to resolve per-depth weights:

```csharp
var weightedPool = allDepthEligible
    .Select(id =>
    {
        monsters.TryGetDefinition(id, out var d);
        int weight = d!.DepthWeights != null
            ? SpawnUtils.FromDungeonLevel(d.DepthWeights, depth)
            : (d.SpawnWeight ?? 0);
        return (id, weight);
    })
    .Where(p => p.weight > 0)
    .ToList();
```

---

### 4. Wire `EncounterBudget` through `DungeonFloorBuilder` → `EntityPlacer.FillRooms`

**File:** `src/Logic/Core/DungeonFloorBuilder.cs`

In `Build()`, after resolving level override (line ~66), extract encounter budget:
```csharp
var encounterBudget = levelOverride?.EncounterBudget;
int roomEtpMax = encounterBudget?.EtpMax ?? DefaultRoomEtpMax;
bool allowSpike = encounterBudget?.AllowSpike ?? false;
```

Pass both to **both** `FillRooms` call sites (guaranteed+fill path and fill-only path):
```csharp
var filled = EntityPlacer.FillRooms(
    generatedMap, genParams, _monsterFactory, _consumableFactory,
    rng, depth, ids,
    roomEtpMax: roomEtpMax,
    allowSpike: allowSpike,
    items: _itemFactory,
    floorItemPool: _floorItemPool);
```

**File:** `src/Logic/Core/EntityPlacer.cs` — add `allowSpike` parameter (currently hardcoded `false` locally):
```csharp
public static IReadOnlyList<Entity> FillRooms(
    GeneratedMap map,
    GenerationParameters? genParams,
    MonsterFactory monsters,
    ConsumableFactory consumables,
    SeededRandom rng,
    int depth,
    EntityIdAllocator ids,
    int roomEtpMax = DefaultRoomEtpMax,
    bool allowSpike = false,
    ItemFactory? items = null,
    IReadOnlyList<FloorItemPoolEntry>? floorItemPool = null)
```

Remove the `bool allowSpike = false;` local variable that currently shadows this.

**File:** `src/Logic/Balance/LevelOverride.cs` — add a comment to `EncounterBudget.EtpMin`:
```csharp
/// <summary>
/// Minimum ETP target for a room. Parsed but not yet enforced — no mechanism
/// to keep adding monsters until a floor-level minimum is met. Tracked at floor
/// level in the PoC (balance/etp.py). Deferred to floor-ETP tracking plan.
/// </summary>
public int EtpMin { get; set; }
```

---

### 5. `entities.yaml` — update monster spawn configuration

**File:** `config/entities.yaml`

#### orc
No change — weight 80 is constant across all depths (matches PoC exactly).

#### orc_grunt
Set `spawn_weight: 0` (explicitly zeroed — requires nullable SpawnWeight from change 0 above). Remains available for guaranteed_spawns in scenarios; excluded from procedural fill. Matches PoC behavior where orc_grunt is a named variant, not a spawn pool entry.

#### orc_brute
Replace `spawn_weight: 15` with `depth_weights` ramp approximating PoC's `_resolve_orc_variant()` probabilities. Keep `min_depth: 3` on the definition as a documentation-level hard gate (consistent with first depth_weights entry):

```yaml
orc_brute:
  extends: orc
  min_depth: 3          # matches first depth_weights entry — documentation + hard gate
  depth_weights:
    - weight: 6
      min_depth: 3      # ~7% of orc pool at depth 3 (matches PoC 7.5% brute chance)
    - weight: 12
      min_depth: 4      # ~13% at depth 4 (PoC 10%)
    - weight: 20
      min_depth: 6      # ~20% at depth 6+ (PoC 10% but depth 6+ is deeper band)
  stats:
    hp: 42
    ...
```

#### zombie
Replace `spawn_weight: 20` with `depth_weights` — exact PoC table match. Keep `min_depth: 10` on definition:

```yaml
zombie:
  min_depth: 10         # matches first depth_weights entry
  depth_weights:
    - weight: 20
      min_depth: 10
    - weight: 40
      min_depth: 13
    - weight: 60
      min_depth: 16
```

---

### 6. `level_templates.yaml` — add `encounter_budget` per depth

**File:** `config/level_templates.yaml`

Add `encounter_budget` to depths 1–3. Values from PoC `etp_config.yaml` B1 band (room_etp.max = 50). `etp_min` is included as parsed-but-not-enforced (see note in LevelOverride.cs).

Depths 4–5 not yet in level_templates: `DefaultRoomEtpMax = 50` applies via fallback — correct for B1, no entry needed until depth 4–5 content is added.

```yaml
levels:
  1:
    parameters:
      max_rooms: 150
      max_monsters_per_room: 2
      max_items_per_room: 2
    encounter_budget:
      etp_min: 0        # parsed, not yet enforced — see LevelOverride.cs
      etp_max: 50       # B1 band, from PoC etp_config.yaml
      allow_spike: false
    guaranteed_spawns:
      mode: "additional"
      items:
        - type: "healing_potion"
          count: "2-3"

  2:
    parameters:
      max_rooms: 150
      max_monsters_per_room: 2
      max_items_per_room: 2
    encounter_budget:
      etp_min: 0
      etp_max: 50
      allow_spike: false
    guaranteed_spawns:
      mode: "additional"
      items:
        - type: "healing_potion"
          count: "1-2"

  3:
    parameters:
      max_rooms: 150
      max_monsters_per_room: 3
      max_items_per_room: 2
    encounter_budget:
      etp_min: 0
      etp_max: 50
      allow_spike: false
    guaranteed_spawns:
      mode: "additional"
      items:
        - type: "healing_potion"
          count: "1-2"
```

---

## Files changed summary

| File | Change |
|------|--------|
| `src/Logic/Balance/SpawnUtils.cs` | NEW — `FromDungeonLevel()` |
| `src/Logic/Content/MonsterDefinition.cs` | `SpawnWeight` → `int?`; add `DepthWeightEntry` class + `DepthWeights` field |
| `src/Logic/Content/ContentLoader.cs` | Merge `SpawnWeight` with `??`; inherit `DepthWeights`; validate depth_weights sort order |
| `src/Logic/Core/EntityPlacer.cs` | Nullable SpawnWeight guard; depth-weight resolution in pool build; `allowSpike` param |
| `src/Logic/Core/DungeonFloorBuilder.cs` | Extract + pass `roomEtpMax` + `allowSpike` from `EncounterBudget` |
| `src/Logic/Balance/LevelOverride.cs` | Comment on `EtpMin` deferred status |
| `config/entities.yaml` | `orc_grunt` spawn_weight:0; `orc_brute` + `zombie` → `depth_weights` |
| `config/level_templates.yaml` | Add `encounter_budget` to depths 1–3 |

---

## Out of scope (separate plans)

- **Stat scaling curves** (DEFAULT_CURVE / ZOMBIE_CURVE) — HP/accuracy/damage multipliers by depth band. Separate `depth_stat_scaling` plan.
- **New monsters** (slime, large_slime, giant_spider) — separate task; depth_weights pattern is now established as the template.
- **Orc shaman / orc skirmisher** — need monster definitions first.
- **Floor-level ETP tracking** — per-floor budget accumulator across all rooms. Separate system (required before `etp_min` can be enforced).
- **Band density scaling for items** — `scale_max_items_per_room()` multiplier. Separate.

---

## Validation

### Automated tests (add to existing EntityPlacer test class)

Three targeted depth-gate tests, each spawning a large sample via `FillRooms` with a fixed seed:

- `ZombieDoesNotSpawnBeforeDepth10` — run 500-room generation at depth 9, assert zero zombies placed
- `ZombieSpawnsAtDepth10` — run 500-room generation at depth 10, assert zombie count > 0
- `OrcBruteDoesNotSpawnBeforeDepth3` — run 500-room generation at depth 2, assert zero orc_brutes

These run in ~10ms total (pure logic layer). Use seed 1337.

### Regression
- `dotnet test --filter "Category!=Slow"` must pass (all 454 + new tests)

### Manual smoke check
- Depth 1: only base orcs (no brutes, no zombies)
- Depth 3: occasional orc brutes visible (~1 in 15 orc-faction spawns)
- Depth 10: zombies start appearing
