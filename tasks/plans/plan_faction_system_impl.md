# Faction System Implementation Plan

## Current State
- Status: ✅ ALL PHASES COMPLETE
- Last updated: 2026-04-11
- 1077 tests total, 0 failures

## Status Summary

| Phase | System | Status |
|-------|--------|--------|
| 1 | FactionRegistry + hostility matrix | ✅ complete |
| 2 | AI target selection (faction-aware) | ✅ complete |
| 3 | Monster-vs-monster combat | ✅ complete |
| 4 | Death Siphon + Command the Dead faction checks | ✅ verified (no changes needed) |
| 5 | Tests + harness verification | ✅ complete (19 tests) |

---

## Overview

Port the PoC faction system (~/development/rlike/components/faction.py). Monsters already have `Faction` strings on AiComponent. The missing piece is a hostility matrix + AI target selection that uses it. This enables orc-vs-undead emergent combat, makes deep dungeon floors dynamic, and is required for meaningful balance at depth 10+.

**Key design decision**: Reuse existing `AiComponent.Faction` string field (already populated from YAML). No new component needed for faction membership — just a static hostility resolver and changes to AI target selection.

---

## Phase 1: FactionRegistry + Hostility Matrix

### New File: `src/Logic/AI/FactionRegistry.cs`

Static hostility matrix matching PoC `faction.py`. Uses string-based faction IDs (already in YAML).

```csharp
namespace UnderWarden.Logic.AI;

/// <summary>
/// Static hostility matrix between factions. Matches PoC components/faction.py.
/// All faction relationships are symmetric: if A is hostile to B, B is hostile to A.
/// Same-faction entities are never hostile to each other.
/// </summary>
public static class FactionRegistry
{
    /// <summary>
    /// Returns true if entities of factionA would attack entities of factionB.
    /// Same faction → never hostile.
    /// Player → hostile to all monster factions.
    /// </summary>
    public static bool AreHostile(string factionA, string factionB)
    {
        if (factionA == factionB) return false;
        
        // Player is hostile to everything, everything is hostile to player
        if (factionA == "player" || factionB == "player") return true;
        
        // Look up in the matrix
        return IsHostile(factionA, factionB) || IsHostile(factionB, factionA);
    }
    
    private static bool IsHostile(string attacker, string target)
    {
        return (attacker, target) switch
        {
            // Orc faction: attacks undead, player, independent. Neutral with cultist.
            ("orc", "undead") => true,
            ("orc", "independent") => true,
            ("orc", "beast") => true,
            
            // Undead faction: attacks all living (orc, cultist, independent, beast)
            ("undead", "orc") => true,
            ("undead", "cultist") => true,
            ("undead", "independent") => true,
            ("undead", "beast") => true,
            
            // Cultist faction: territorial, attacks intruders (orc, undead, independent)
            ("cultist", "orc") => true,
            ("cultist", "undead") => true,
            ("cultist", "independent") => true,
            ("cultist", "beast") => true,
            
            // Independent/beast: attacks everything except same species (handled by same-faction check)
            ("independent", "orc") => true,
            ("independent", "undead") => true,
            ("independent", "cultist") => true,
            ("beast", "orc") => true,
            ("beast", "undead") => true,
            ("beast", "cultist") => true,
            
            // Neutral: only hostile to player (handled by player check above)
            _ => false,
        };
    }
    
    /// <summary>
    /// Target priority: higher = preferred target when multiple hostiles are equidistant.
    /// Player is always highest priority. Matches PoC TARGET_PRIORITY_MATRIX.
    /// </summary>
    public static int GetTargetPriority(string attackerFaction, string targetFaction)
    {
        if (targetFaction == "player") return 10;
        
        return (attackerFaction, targetFaction) switch
        {
            ("undead", "orc") => 7,
            ("undead", "cultist") => 7,
            ("undead", "independent") => 5,
            ("undead", "beast") => 5,
            
            ("orc", "undead") => 7,
            ("orc", "independent") => 5,
            ("orc", "beast") => 5,
            
            _ => 5, // default priority for hostile factions
        };
    }
}
```

### Acceptance Criteria
- [ ] `FactionRegistry.AreHostile("orc", "undead")` → true
- [ ] `FactionRegistry.AreHostile("orc", "orc")` → false
- [ ] `FactionRegistry.AreHostile("orc", "cultist")` → false (neutral)
- [ ] `FactionRegistry.AreHostile("undead", "orc")` → true
- [ ] `FactionRegistry.AreHostile("undead", "cultist")` → true
- [ ] `FactionRegistry.AreHostile("player", anything)` → true
- [ ] Player priority always 10

### Files to Create
- `src/Logic/AI/FactionRegistry.cs`
- `tests/Core/FactionRegistryTests.cs`

---

## Phase 2: Faction-Aware AI Target Selection

### Modify: `src/Logic/AI/BasicMonsterAI.cs`

Change `ChooseTarget()` to use faction hostility when selecting targets. Current flow:
1. TauntedEffect → forced target
2. EnragedEffect → nearest entity (any)
3. **Default → player**

New flow:
1. TauntedEffect → forced target (unchanged)
2. EnragedEffect → nearest entity (unchanged — aggravation overrides faction)
3. **Faction check → nearest hostile entity (player OR monster), player preferred on tie**
4. Fallback → player (if no visible hostiles found)

### ChooseTarget Changes

```csharp
internal static Entity ChooseTarget(Entity monster, Entity player, GameState state)
{
    // 1. Taunt overrides everything
    var taunt = monster.Get<TauntedEffect>();
    if (taunt != null) { ... unchanged ... }
    
    // 2. EnragedEffect: nearest anything
    if (monster.Has<EnragedEffect>()) { ... unchanged ... }
    
    // 3. Invisible player gate (moved up to inform faction targeting)
    bool playerInvisible = player.Has<InvisibilityEffect>();
    
    // 4. Faction-aware targeting
    string myFaction = monster.Get<AiComponent>()?.Faction ?? "neutral";
    Entity? bestTarget = null;
    int bestDist = int.MaxValue;
    int bestPriority = -1;
    
    // Consider player as target
    if (!playerInvisible && FactionRegistry.AreHostile(myFaction, "player"))
    {
        int d = monster.ChebyshevDistanceTo(player.X, player.Y);
        bestTarget = player;
        bestDist = d;
        bestPriority = FactionRegistry.GetTargetPriority(myFaction, "player"); // 10
    }
    
    // Consider other monsters
    foreach (var other in state.AliveMonsters)
    {
        if (other.Id == monster.Id) continue;
        string otherFaction = other.Get<AiComponent>()?.Faction ?? "neutral";
        if (!FactionRegistry.AreHostile(myFaction, otherFaction)) continue;
        
        int d = monster.ChebyshevDistanceTo(other.X, other.Y);
        int priority = FactionRegistry.GetTargetPriority(myFaction, otherFaction);
        
        // Prefer higher priority, then closer distance
        if (priority > bestPriority || (priority == bestPriority && d < bestDist))
        {
            bestTarget = other;
            bestDist = d;
            bestPriority = priority;
        }
    }
    
    return bestTarget ?? player; // fallback to player if no hostile found
}
```

### CRITICAL: Monster-vs-monster attack path

Currently `ResolveMonsterAttack` in TurnController assumes the target is always the player. When a monster attacks another monster:
- The `AttackEvent` is the same (it uses entity IDs, not player-specific data)
- Death handling needs to work for monster targets (drop loot? create corpse?)
- Death siphon should fire for any undead death, not just player-killed

This is Phase 3.

### Files to Modify
- `src/Logic/AI/BasicMonsterAI.cs` — ChooseTarget()
- Other AIs (SkirmisherAI, OrcShamanAI, etc.) call `BasicMonsterAI.ChooseTarget` → changes propagate

### Acceptance Criteria
- [ ] Orc chooses adjacent zombie over distant player
- [ ] Zombie chooses adjacent orc over distant player  
- [ ] Same-faction monsters never target each other
- [ ] Player is highest priority (chosen when equidistant with monster hostile)
- [ ] Invisible player → monster targets nearest hostile monster instead

---

## Phase 3: Monster-vs-Monster Combat Resolution

### Problem

`TurnController.ResolveMonsterAttack` (line ~1260) handles the monster→target attack. The target can be the player (current path) or another monster (new path). Key differences:

1. **Monster death from monster attack**: Currently only player kills trigger `DropMonsterLoot` + `TransformToCorpse` + `ResolveDeathSiphon`. Monster-vs-monster kills need the same.
2. **Knowledge tracking**: `MonsterKnowledgeSystem` shouldn't track monster-vs-monster kills.
3. **Ring of Teleportation**: Only applies when player is hit. Guard with `target.Id == state.Player.Id`.

### Changes to TurnController

The `ResolveMonsterAttack` method already receives a generic `Entity target`. The fix is:

```csharp
if (result.TargetKilled)
{
    events.Add(new DeathEvent { ActorId = target.Id, KillerId = monster.Id });
    
    if (target.Id == state.Player.Id)
    {
        // Player death — no loot/corpse needed
    }
    else
    {
        // Monster killed by another monster — same treatment as player kills
        DropMonsterLoot(state, target, events);
        TransformToCorpse(state, target, events, monsterFactory);
        ResolveDeathSiphon(state, target, events);
    }
}
```

Wait — re-reading the current code, `ResolveMonsterAttack` line 1291-1295:
```csharp
if (result.TargetKilled)
{
    events.Add(new DeathEvent { ActorId = target.Id, KillerId = monster.Id });
    // target here is the player — players don't have equipment to drop
}
```

The comment says "target here is the player" because that was the only case. Now it can be a monster. The `DropMonsterLoot` and `TransformToCorpse` methods are only called from the player attack path. We need to call them from the monster attack path too when target is a monster.

**BUT**: `DropMonsterLoot` and `TransformToCorpse` might require a `MonsterFactory` parameter that `ResolveMonsterAttack` doesn't have. Check the call chain.

Looking at the existing code:
- `ResolvePlayerAttack` has `MonsterFactory? monsterFactory` parameter
- `ResolveMonsterAttack` does NOT — it's called from `ResolveMonsterTurns` which also doesn't have it

**Solution**: Add optional `MonsterFactory? monsterFactory` parameter to `ResolveMonsterAttack` (default null), and pass it from `ResolveMonsterTurns` which gets it from `ProcessTurn`.

### Files to Modify
- `src/Logic/Core/TurnController.cs` — ResolveMonsterAttack target handling, ResolveMonsterTurns monsterFactory threading

### Acceptance Criteria
- [ ] Monster killing monster emits DeathEvent
- [ ] Monster killing monster drops loot
- [ ] Monster killing monster creates corpse (if leaves_corpse=true)
- [ ] Death siphon fires for monster-vs-monster kills
- [ ] Ring of Teleportation only fires when player is hit (not monsters)

---

## Phase 4: Death Siphon + Command the Dead Faction Checks

### Current Issue

Death Siphon checks `ai.Tags.Contains("undead")` on the dead monster. This is correct — any undead death triggers siphon. No faction change needed here.

Command the Dead checks `ai.Tags.Contains("undead")` on the attacker. This is correct — the to-hit bonus applies to undead monsters near a lich. No change needed.

**However**, both should respect faction for one edge case: if a lich raises a corpse, the raised entity should be undead faction (already handled by NecromancerAI raise logic which sets faction to raiser's faction). Verified this works.

Phase 4 is primarily about **verifying** the faction integration works end-to-end, not adding new code. It's a test phase.

---

## Phase 5: Tests + Harness Verification

### Test File: `tests/Core/FactionSystemTests.cs`

```
# FactionRegistry tests
- AreHostile_OrcVsUndead_True
- AreHostile_OrcVsOrc_False
- AreHostile_OrcVsCultist_False
- AreHostile_UndeadVsCultist_True
- AreHostile_PlayerVsAnything_True
- AreHostile_NeutralVsNeutral_False
- GetTargetPriority_PlayerAlways10
- GetTargetPriority_UndeadVsOrc_7

# AI targeting tests
- ChooseTarget_AdjacentHostileMonster_OverDistantPlayer
- ChooseTarget_SameFaction_NeverTargeted
- ChooseTarget_PlayerPreferred_WhenEquidistant
- ChooseTarget_InvisiblePlayer_TargetsHostileMonster
- ChooseTarget_NoHostiles_FallsToPlayer
- ChooseTarget_EnragedOverridesFaction

# Monster-vs-monster combat
- MonsterKillsMonster_EmitsDeathEvent
- MonsterKillsMonster_DropsLoot
- MonsterKillsMonster_CreatesCorpse
- OrcAttacksZombie_CombatResolves
- ZombieAttacksOrc_CombatResolves

# Integration
- MixedFactionRoom_MonstersAttackEachOther
- LichWithZombies_AllAttackPlayer (same faction, no infighting)
```

### Files to Create
- `tests/Core/FactionSystemTests.cs`

### Acceptance Criteria
- [ ] All tests pass
- [ ] `dotnet test --filter "Category!=Slow"` passes with all new tests
- [ ] No regressions in existing monster AI tests

---

## Build Order

```
Phase 1: FactionRegistry          (independent, no prerequisites)
Phase 2: AI Target Selection      (requires Phase 1)
Phase 3: Monster-vs-Monster Combat (requires Phase 2)
Phase 4: Verify Siphon/Command    (requires Phase 3)
Phase 5: Tests                    (requires all above)
```

Phases should be tested incrementally — run tests after each phase.

## Risks

### R1: Performance — scanning AliveMonsters for faction targets
ChooseTarget now iterates AliveMonsters every monster turn. For N monsters, this is O(N²) per turn. At 30 monsters (typical dungeon floor), this is 900 iterations — trivial. At 150 (stress test), 22,500 — still under 1ms. Not a concern.

### R2: AI chaos — too many monsters fighting each other
If every orc fights every zombie and vice versa, the player might walk through empty rooms. **Mitigation**: Player priority is 10 vs 5-7 for monster targets. Monsters will attack the player when closer or equidistant. Only when a monster-faction hostile is significantly closer will they redirect.

### R3: Orc-cultist relationship
PoC has orcs neutral with cultists (don't attack each other). This is intentional — cultists are a third faction that fights undead but ignores orcs, creating interesting tactical situations. The plan preserves this.

### R4: ResolveMonsterAttack signature change
Adding `monsterFactory` parameter requires threading it through `ResolveMonsterTurns`. Both are private static methods so no external callers break, but the change touches a hot path. Test thoroughly.

### R5: Bonus attack chain for monster-vs-monster
The player attack path has bonus attack chains (momentum). Monster attacks do not — `ResolveMonsterAttack` does not recurse. This is correct and should stay unchanged. Monsters don't get momentum bonus attacks.
