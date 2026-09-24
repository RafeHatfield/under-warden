using UnderWarden.Logic.AI;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Knowledge;
using UnderWarden.Logic.Persistence.Namespaces;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Processes one game turn. Stateless — all state lives in GameState.
/// This is the single source of truth for turn resolution.
/// Both the harness (via BotBrain → PlayerAction) and the UI call this.
/// </summary>
public static class TurnController
{
    /// <summary>
    /// Process one complete turn: player action + all monster responses.
    /// Mutates gameState. Returns events describing what happened.
    ///
    /// monsterFactory: required for split spawning. When null (most test environments),
    /// a split falls back to a kill (HP=0, DeathEvent) with no children spawned.
    /// All existing call sites that omit this parameter retain correct behavior.
    ///
    /// portalEntityFactory: required for portal placement (Wand of Portals). When null,
    /// portal cast actions silently do nothing. Tests that exercise portals must inject this.
    /// </summary>
    public static TurnResult ProcessTurn(GameState state, PlayerAction action,
        MonsterFactory? monsterFactory = null,
        EntityFactory? portalEntityFactory = null)
    {
        var events = new List<TurnEvent>();
        state.TurnCount++;

        // === PLAYER TURN ===
        // Process start-of-turn effects: DOT/HOT ticks, skip-turn determination.
        // If the player's turn is skipped (slowed, immobilized, asleep), skip the action.
        // ProcessTurnStart fires BEFORE the action; ProcessTurnEnd fires AFTER.
        // This means effects applied DURING a player action (e.g. a misfire applying
        // DisorientationEffect to the player) are decremented on the NEXT turn — not this one —
        // because ProcessTurnEnd has already run for this entity before the action.
        //
        // Effects applied to MONSTERS during the player's action (e.g. Fear scroll) ARE
        // decremented in the same round by the monster's ProcessTurnEnd at the end of its turn.
        // This is correct: the monster takes its full turn before the round ends.

        // freeAction: true when the player reads a signpost. Sign reads don't cost a turn —
        // TurnCount is decremented back, monster turns are skipped, status ticks are skipped.
        // Mirrors PoC behavior where reading a sign is "looking," not a game action.
        bool freeAction = false;

        bool playerSkipTurn = StatusEffectProcessor.ProcessTurnStart(state.Player, events, state.TurnCount, state);
        if (!playerSkipTurn)
            ResolvePlayerAction(state, action, events, monsterFactory, portalEntityFactory, out freeAction);

        // Sign reads are free actions: reverse the TurnCount increment so the game clock
        // does not advance. Status ticks are also skipped (no ProcessTurnEnd this cycle).
        if (freeAction)
        {
            state.TurnCount--;
        }
        else
        {
            StatusEffectProcessor.ProcessTurnEnd(state.Player, events);
            // Potion cooldown: tick down each real player turn (not free actions, not skipped turns).
            var pf = state.PlayerFighter;
            if (pf.PotionCooldownRemaining > 0)
                pf.PotionCooldownRemaining--;
        }

        // Ring of Regeneration: passive heal every 5 turns (not turn 0).
        // Checked after the player acts so the heal happens at the end of their action phase.
        // Using TurnCount % 5 == 0 matches PoC ring.py passive tick logic.
        // Two regen rings heal twice (we count both slots).
        if (state.TurnCount > 0 && state.TurnCount % 5 == 0)
        {
            int regenCount = CountRingEffect(state.Player.Get<Equipment>(), RingEffectKind.Regeneration);
            if (regenCount > 0)
            {
                var regenFighter = state.PlayerFighter;
                for (int r = 0; r < regenCount; r++)
                {
                    int healed = regenFighter.Heal(1);
                    if (healed > 0)
                        events.Add(new HotHealEvent
                        {
                            ActorId  = state.Player.Id,
                            EntityId = state.Player.Id,
                            EffectName = "ring_of_regeneration",
                            Amount   = healed,
                        });
                }
            }
        }

        state.RecomputeFov(); // update FOV after player moves (no-op in scenario mode)

        // Passive secret door detection: checked once per player turn after the action resolves.
        // Free actions (sign reads) and skipped turns do not count — same as other passive effects.
        if (!freeAction && !playerSkipTurn && state.PlayerFighter.IsAlive)
            CheckSecretDoorDetection(state, events);

        // === MONSTER TURNS ===
        // Skip monster turns on successful Descend — the floor transition is handled
        // by the presentation layer. Monsters should not get a free attack as the
        // player steps through the stair.
        bool descended = events.Any(e => e is DescendEvent);
        if (descended)
            state.Corpses.Clear(); // Corpses don't follow the player to the next floor
        // Skip monster turns for free actions (sign reads) — they don't advance game time.
        if (!descended && !freeAction && state.PlayerFighter.IsAlive)
        {
            ResolveMonsterTurns(state, events, monsterFactory, portalEntityFactory);
            TickEnvironment(state, events, monsterFactory);
            state.RecomputeFov(); // update FOV after monsters move (no-op in scenario mode)
        }

        // Clear portal UsedThisTurn flags at end of turn so portals can fire again next turn.
        // This is a no-op when no portals are active.
        PortalSystem.ClearPortalUsedFlags(state);

        // Possession visibility constraint (§4): check after all entities have moved.
        // If the host moved outside MAX_POSSESSION_DISTANCE or LOS was broken, forces exit.
        // No-op when no active possession.
        if (state.PlayerFighter.IsAlive)
            PossessionSystem.CheckVisibilityConstraint(state, events);

        // === KNOWLEDGE UPDATE ===
        // Update monster knowledge after all combat events are resolved.
        // Build a lookup of all monsters (including dead ones still in state.Monsters) so we
        // can resolve species IDs from entity IDs in AttackEvent and DeathEvent.
        UpdateKnowledge(state, events);

        // Weighing orchestration (TASK-009). On the first turn in the arena, begin the gauntlet
        // (score the audit, rise the first Guardian). Thereafter advance the phase after all combat
        // resolves. No-op off the Weighing floor (no arena). Runs even on player death to set the
        // distinct loss ending/cause.
        if (state.WeighingArena != null && state.Weighing == null)
            Endgame.WeighingOrchestrator.BeginFromPersistence(state, events);
        else if (state.Weighing != null)
            Endgame.WeighingOrchestrator.Advance(state, events);

        var aliveMonsters = state.AliveMonsters;
        return new TurnResult
        {
            TurnNumber = state.TurnCount,
            Events = events,
            GameOver = state.IsGameOver,
            PlayerDied = !state.PlayerFighter.IsAlive,
            AllMonstersDefeated = aliveMonsters.Count == 0,
        };
    }

    /// <summary>
    /// Update MonsterKnowledgeSystem from the turn's events and current FOV.
    ///
    /// Three update sources:
    ///   1. AttackEvent where actor or target is a monster → RecordEngaged
    ///   2. DeathEvent for a monster → RecordKilled
    ///   3. FOV scan: all currently visible alive monsters → RecordSeen
    ///
    /// Uses entity ID lookup to find the SpeciesTag on each monster.
    /// If a monster has no SpeciesTag (hand-constructed in tests), the update is skipped silently.
    /// </summary>
    private static void UpdateKnowledge(GameState state, List<TurnEvent> events)
    {
        var knowledge = state.Knowledge;

        // Build id→entity map once. Include all monsters (dead ones may appear in DeathEvent).
        // Use a manual loop rather than ToDictionary to safely handle rare duplicate IDs — these
        // can occur in split tests where hand-constructed entities share IDs with factory children.
        // Last-entry wins (children overwrite the dead original, which is fine for knowledge purposes).
        var monsterById = new Dictionary<int, Entity>(state.Monsters.Count);
        foreach (var m in state.Monsters)
            monsterById[m.Id] = m;
        int playerId = state.Player.Id;

        // Snapshot count before the loop so that instrumentation events appended inside the loop
        // (e.g. OrcRepChangedEvent) land in `events` for the caller without being re-processed and
        // without tripping List<T>'s modification-during-enumeration guard.
        int eventCount = events.Count;
        for (int evtIdx = 0; evtIdx < eventCount; evtIdx++)
        {
            var evt = events[evtIdx];
            switch (evt)
            {
                case AttackEvent atk:
                    // Player attacked a monster, or a monster attacked the player.
                    // Both directions count as an engagement for that species.
                    if (atk.ActorId != playerId && monsterById.TryGetValue(atk.ActorId, out var attacker))
                    {
                        var tag = attacker.Get<SpeciesTag>();
                        if (tag != null) knowledge.RecordEngaged(tag.TypeId);

                        // Aggression flag (TASK-003/007): a monster that attacks Sasha's side —
                        // the player OR a player-ally — hit OR miss, chose violence. Sticky, so a
                        // later kill of it reads as provoked even if it deaggros or flees first.
                        if (!attacker.Has<HasAttackedPlayerTag>())
                        {
                            bool attackedSashasSide = atk.TargetId == playerId
                                || (monsterById.TryGetValue(atk.TargetId, out var atkTarget)
                                    && FactionRegistry.IsPlayerSide(atkTarget.Get<AiComponent>()?.Faction ?? "neutral"));
                            if (attackedSashasSide)
                                attacker.Add(new HasAttackedPlayerTag());
                        }
                    }
                    if (atk.TargetId != playerId && monsterById.TryGetValue(atk.TargetId, out var target))
                    {
                        var tag = target.Get<SpeciesTag>();
                        if (tag != null) knowledge.RecordEngaged(tag.TypeId);
                    }
                    break;

                case DeathEvent death when !death.IsPossessionInduced:
                    // Only count monster deaths (not the player's own death).
                    // Possession-induced deaths (§8.2/§8.5) are excluded: holding a body ≠ killing it.
                    if (death.ActorId != playerId && monsterById.TryGetValue(death.ActorId, out var dead))
                    {
                        var tag = dead.Get<SpeciesTag>();
                        if (tag != null) knowledge.RecordKilled(tag.TypeId);

                        // Unprovoked-kill tally (TASK-003): Sasha killed a monster that never
                        // attacked him. Counts toward the excess metric (Auditor's Own) and, for
                        // orcs, the faction-rep Hostile transition. "Dealt by Sasha" includes kills
                        // through a host he is possessing — the metric must not be launderable via a
                        // proxy — but not kills by hosts possessed by anything else (see ExcessKillEvaluator).
                        Entity? killer = monsterById.TryGetValue(death.KillerId, out var k) ? k : null;
                        if (Endgame.ExcessKillEvaluator.DealtByPlayer(killer, death.KillerId, playerId)
                            && !dead.Has<HasAttackedPlayerTag>())
                        {
                            string victimFaction = dead.Get<AiComponent>()?.Faction ?? "neutral";
                            state.Player.GetOrAdd<RunAggressionTally>().AddUnprovokedKill(victimFaction);
                            // Note: OrcRepChangedEvent is emitted from ResolvePlayerAttack (at the kill
                            // site, before TransformToCorpse strips the AiComponent). The emit here would
                            // read a null faction because TransformToCorpse already ran.
                        }
                    }
                    break;
            }
        }

        // FOV: record seen for all monsters currently visible. In scenario mode (IsDungeonMode=false),
        // IsVisible returns true for all tiles, so all alive monsters get RecordSeen every turn.
        // This is intentional — scenario mode isn't bounded by FOV, and RecordSeen is idempotent
        // in effect (tier only advances, never regresses). In dungeon mode, only visible monsters count.
        foreach (var monster in state.AliveMonsters)
        {
            if (state.Map.IsVisible(monster.X, monster.Y))
            {
                var tag = monster.Get<SpeciesTag>();
                if (tag != null) knowledge.RecordSeen(tag.TypeId);
            }
        }
    }

    private static void ResolvePlayerAction(GameState state, PlayerAction action, List<TurnEvent> events,
        MonsterFactory? monsterFactory, EntityFactory? portalEntityFactory, out bool freeAction)
    {
        freeAction = false;
        var player = state.Player;
        bool isPossessing = !ReferenceEquals(state.ControlledEntity, player);

        switch (action.Kind)
        {
            // ── Possession actions ────────────────────────────────────────────────
            case PlayerAction.ActionKind.EnterPossessionTargeting:
                // Free action: UI opens targeting overlay; no game-state change here.
                freeAction = true;
                break;

            case PlayerAction.ActionKind.Possess:
                // Costs one turn: possess the target host.
                if (action.Target != null)
                    PossessionSystem.Enter(action.Target, state, events);
                // Drain ticks on the possessed turn (including this first one — §7).
                PossessionSystem.ApplyDrainTick(state, events);
                break;

            case PlayerAction.ActionKind.ExitPossession:
                // Free action: voluntarily leave the host body.
                PossessionSystem.ExitVoluntary(state, events);
                freeAction = true;
                break;

            // ── Blocked during possession ─────────────────────────────────────────
            case PlayerAction.ActionKind.EquipItem:
                if (isPossessing) { events.Add(new WaitEvent { ActorId = player.Id }); break; }
                ResolveEquip(state, action.Item!, events);
                player.Get<SpeedBonusTracker>()?.ResetMomentum();
                break;

            case PlayerAction.ActionKind.UnequipItem:
                if (isPossessing) { events.Add(new WaitEvent { ActorId = player.Id }); break; }
                ResolveUnequip(state, action.Slot!.Value, events);
                player.Get<SpeedBonusTracker>()?.ResetMomentum();
                break;

            case PlayerAction.ActionKind.Descend:
                if (isPossessing) { events.Add(new WaitEvent { ActorId = player.Id }); break; }
                ResolveDescend(state, events);
                break;

            // ── Normal actions (routed to ControlledEntity for Move/Attack in future phases) ──
            case PlayerAction.ActionKind.Attack:
                ResolvePlayerAttack(state, action.Target!, events, isBonusAttack: false, monsterFactory);
                if (isPossessing) PossessionSystem.ApplyDrainTick(state, events);
                break;

            case PlayerAction.ActionKind.UseItem:
                TryHeal(state, action.Item, events);
                player.Get<SpeedBonusTracker>()?.ResetMomentum();
                if (isPossessing) PossessionSystem.ApplyDrainTick(state, events);
                break;

            case PlayerAction.ActionKind.Move:
                ResolvePlayerMove(state, action, events, out freeAction);
                if (!freeAction)
                {
                    player.Get<SpeedBonusTracker>()?.ResetMomentum();
                    if (isPossessing) PossessionSystem.ApplyDrainTick(state, events);
                }
                break;

            case PlayerAction.ActionKind.Wait:
                events.Add(new WaitEvent { ActorId = player.Id });
                player.Get<SpeedBonusTracker>()?.ResetMomentum();
                if (isPossessing) PossessionSystem.ApplyDrainTick(state, events);
                break;

            case PlayerAction.ActionKind.DropItem:
                ResolveDrop(state, action.Item!, events);
                if (isPossessing) PossessionSystem.ApplyDrainTick(state, events);
                break;

            case PlayerAction.ActionKind.CastSpell:
                ResolveSpellAction(state, action, events, portalEntityFactory);
                player.Get<SpeedBonusTracker>()?.ResetMomentum();
                if (isPossessing) PossessionSystem.ApplyDrainTick(state, events);
                break;

            case PlayerAction.ActionKind.ThrowItem:
                ResolveThrowItem(state, action, events);
                if (isPossessing) PossessionSystem.ApplyDrainTick(state, events);
                break;

            case PlayerAction.ActionKind.UseMonsterAbility:
                // Infrastructure stub — no species abilities are implemented yet.
                // Future phases will route action.AbilityId to per-ability resolvers
                // (e.g. "grapple" → Hall Warden grapple handler in Phase 8+).
                // Graceful degradation: unknown ability IDs resolve as Wait.
                events.Add(new WaitEvent { ActorId = player.Id });
                if (isPossessing) PossessionSystem.ApplyDrainTick(state, events);
                break;

            case PlayerAction.ActionKind.RangedAttack:
                ResolveRangedAttack(state, action.Target!, events);
                if (isPossessing) PossessionSystem.ApplyDrainTick(state, events);
                break;
        }
    }

    /// <summary>
    /// Resolve a throw action. Delegates to ThrowResolver which handles all three paths
    /// (potion, weapon, junk). The monsterFactory is not needed for throws — death from
    /// thrown weapons follows the spell-kill pattern (DeathEvent only, no loot/corpse here).
    /// </summary>
    private static void ResolveThrowItem(GameState state, PlayerAction action, List<TurnEvent> events)
    {
        var item = action.Item;
        if (item == null) return;
        if (!action.TargetX.HasValue || !action.TargetY.HasValue) return;

        var throwEvents = ThrowResolver.Resolve(
            state.Player,
            item,
            action.TargetX.Value,
            action.TargetY.Value,
            state);

        events.AddRange(throwEvents);
    }

    /// <summary>
    /// Resolve a scroll or wand use action.
    ///
    /// Flow:
    ///   1. Locate the SpellEffect component on the item — bail if missing.
    ///   2. If item has WandComponent: consume a charge. If out of charges, emit WandUseEvent(Success=false) and stop.
    ///   3. If item has Consumable (scroll): decrement stack, remove entity from inventory when depleted.
    ///   4. Delegate to SpellResolver.Resolve with the target info from the action.
    ///      Exception: Portal targeting mode is handled here via PortalSystem.PlacePortals —
    ///      SpellResolver does not have an EntityFactory, so portal placement is TurnController's job.
    ///   5. If wand, emit WandUseEvent with remaining charges (including destroyed=true if charges hit 0).
    ///   6. Also handle wand auto-recharge on scroll pickup (called from TryPickUpItemsAt).
    /// </summary>
    private static void ResolveSpellAction(GameState state, PlayerAction action, List<TurnEvent> events,
        EntityFactory? portalEntityFactory = null)
    {
        var item = action.Item;
        if (item == null) return;

        var spell = item.Get<SpellEffect>();
        if (spell == null) return;

        // SilencedEffect: blocks scroll AND wand use. Does NOT block potions or melee.
        // Potions are physical (swallowed), not magical (spoken) — silence doesn't stop drinking.
        // Gate here before charges or consumables are spent, so the player isn't charged for a blocked action.
        bool isPotion = item.Get<Consumable>()?.IsPotion == true;
        if (state.Player.Has<SilencedEffect>() && !isPotion)
            return;

        var wand = item.Get<WandComponent>();
        var consumable = item.Get<Consumable>();

        // ── Wand: check + consume charge ─────────────────────────────────────
        if (wand != null)
        {
            if (!wand.HasCharges)
            {
                events.Add(new WandUseEvent
                {
                    ActorId = state.Player.Id,
                    WandName = item.Name,
                    RemainingCharges = 0,
                    Success = false,
                });
                return;
            }

            wand.TryConsume();
        }

        // The use has committed: silence didn't block it and the wand had a charge.
        // Announcing here rather than at the top means a silenced scroll or a spent wand
        // is never announced as used.
        AnnounceItemUse(state, item, events);

        // ── Scroll: consume one stack ─────────────────────────────────────────
        if (consumable != null)
        {
            var inventory = state.PlayerInventory;
            if (inventory != null)
            {
                consumable.StackSize--;
                if (consumable.StackSize <= 0)
                    inventory.Remove(item);
            }
        }

        // ── InvisibilityEffect break on spell cast ────────────────────────────
        // InvisibilityEffect breaks when the player casts a spell (scroll or wand).
        // Does NOT break on drinking a potion — potions are physical, not magical.
        // DOES break on throwing a potion — throwing at a target is an offensive action.
        // PoC-verified: invisibility persists through potion drinks, ends on offensive actions.
        bool throwingPotion = isPotion && action.TargetEntityId.HasValue;
        bool breaksInvisibility = !isPotion || throwingPotion;
        if (state.Player.Has<InvisibilityEffect>() && breaksInvisibility)
        {
            state.Player.Remove<InvisibilityEffect>();
            events.Add(new StatusExpiredEvent
            {
                ActorId = state.Player.Id,
                EntityId = state.Player.Id,
                EffectName = "invisibility",
                Reason = throwingPotion ? "threw_potion" : "cast_spell",
            });
        }

        // ── Resolve spell ─────────────────────────────────────────────────────
        // Portal casting is handled here (not SpellResolver) because it needs an EntityFactory.
        // All other targeting modes delegate to SpellResolver.
        if (spell.SpellId == "portal")
        {
            if (portalEntityFactory != null)
            {
                var portalEvents = PortalSystem.HandlePortalCast(
                    state.Player,
                    state,
                    item,
                    targetX: action.TargetX,
                    targetY: action.TargetY,
                    entityFactory: portalEntityFactory);

                if (portalEvents != null)
                    events.AddRange(portalEvents);
            }
            // If no factory: silently no-op (tests that don't exercise portals).
        }
        else
        {
            // Throwable potions: the item's primary SpellId is the drink spell (e.g., "drink_weakness").
            // When the player throws at a target (TargetEntityId is set), use ThrowSpellId instead
            // (e.g., "throw_weakness") so SpellResolver applies the effect to the target, not the caster.
            // Drink path (no TargetEntityId): overrideSpellId is null, SpellResolver uses spell.SpellId.
            string? overrideSpellId = null;
            if (action.TargetEntityId.HasValue && !string.IsNullOrEmpty(spell.ThrowSpellId))
                overrideSpellId = spell.ThrowSpellId;

            var spellEvents = SpellResolver.Resolve(
                state.Player,
                spell,
                state,
                targetEntityId: action.TargetEntityId,
                targetX: action.TargetX,
                targetY: action.TargetY,
                overrideSpellId: overrideSpellId);

            events.AddRange(spellEvents);
        }

        // ── Wand post-use: emit charge event ──────────────────────────────────
        if (wand != null)
        {
            bool destroyed = !wand.Infinite && wand.Charges <= 0;

            events.Add(new WandUseEvent
            {
                ActorId = state.Player.Id,
                WandName = item.Name,
                RemainingCharges = wand.Infinite ? int.MaxValue : wand.Charges,
                Success = true,
                WandDestroyed = destroyed,
            });

            // Remove depleted wand from inventory
            if (destroyed)
            {
                state.PlayerInventory?.Remove(item);
            }
        }

        // Identification on use: scrolls and wands are identified when used.
        // Spell effects fire first, then identification check, then toast if newly identified.
        TryIdentifyOnUse(state, item, events, trigger: "used");
    }

    private static void ResolvePlayerAttack(GameState state, Entity target, List<TurnEvent> events,
        bool isBonusAttack, MonsterFactory? monsterFactory)
    {
        var player = state.Player;

        // ImmobilizedEffect: cannot attack (or move, or cast — all actions blocked).
        // ProcessTurnStart already returns skipTurn=true for this, so in normal flow this
        // branch should not fire. Kept as an explicit guard for safety.
        if (player.Has<ImmobilizedEffect>())
            return;

        // DisarmedEffect: weapon attack is cancelled. Emit a failed attack event.
        // Does not prevent unarmed attacks if the player has no weapon equipped.
        var equipment = player.Get<Equipment>();
        bool hasWeaponEquipped = equipment?.MainHand != null;
        if (player.Has<DisarmedEffect>() && hasWeaponEquipped)
        {
            events.Add(new AttackEvent
            {
                ActorId = player.Id,
                TargetId = target.Id,
                Hit = false,
                Damage = 0,
                IsCritical = false,
                IsFumble = false,
                TargetKilled = false,
                IsBonusAttack = isBonusAttack,
                FailReason = "disarmed",
                ActorName = player.Name,
                TargetName = target.Name,
            });
            return;
        }

        // InvisibilityEffect breaks when the player makes any attack (PoC-verified).
        // NOT broken by taking damage or using items. Breaks before the attack resolves
        // so monsters can respond after the reveal.
        if (player.Has<InvisibilityEffect>())
        {
            player.Remove<InvisibilityEffect>();
            events.Add(new StatusExpiredEvent
            {
                ActorId = player.Id,
                EntityId = player.Id,
                EffectName = "invisibility",
                Reason = "attacked",
            });
        }

        // Surprise attack — guaranteed hit on first player attack against any monster.
        // PoC: is_monster_aware starts False even in "aware" scenario state; first attack auto-hits.
        var targetFighter = target.Get<Fighter>();
        bool isSurprise = targetFighter?.SurpriseAttackAvailable == true;
        if (isSurprise)
            targetFighter!.ConsumeSurpriseAttack();

        var result = CombatResolver.ResolveAttack(player, target, state.Rng, forceHit: isSurprise);

        events.Add(new AttackEvent
        {
            ActorId = player.Id,
            TargetId = target.Id,
            Hit = result.Hit,
            Damage = result.Damage,
            IsCritical = result.IsCritical,
            IsFumble = result.IsFumble,
            TargetKilled = result.TargetKilled,
            IsBonusAttack = isBonusAttack,
            ActorName = player.Name,
            TargetName = target.Name,
        });

        if (result.Hit)
        {
            // Wake sleeping targets on attack damage (NOT DOT — PoC-verified).
            StatusEffectProcessor.OnDamageTaken(target, events);

            // Acid weapon coating: coated main-hand weapon applies AcidEffect to the target on hit
            // and decrements the coating's hit counter. Removed when exhausted.
            var playerEquip = player.Get<Equipment>();
            var coatedWeapon = playerEquip?.MainHand?.Get<WeaponAcidCoatingComponent>();
            if (coatedWeapon != null)
            {
                var acidEffect = StatusEffectProcessor.ApplyEffect<AcidEffect>(target, coatedWeapon.EffectDuration);
                if (acidEffect != null)
                {
                    events.Add(new StatusAppliedEvent
                    {
                        ActorId    = player.Id,
                        TargetId   = target.Id,
                        EffectName = "acid",
                        Duration   = acidEffect.RemainingTurns,
                    });
                }

                coatedWeapon.HitsRemaining--;
                if (coatedWeapon.HitsRemaining <= 0)
                    playerEquip!.MainHand!.Remove<WeaponAcidCoatingComponent>();
            }

            // Attack damage interrupt hooks — rally end, chant interrupt.
            // Called after acid coating so all on-hit effects fire before the interrupt check.
            // DOT damage does NOT call this path (PoC-verified).
            if (result.Damage > 0)
                OnAttackDamageTaken(state, player, target, events);

            // Check split-under-pressure BEFORE death check — split wins over kill.
            // Only check on a hit because the HP hasn't changed on a miss.
            var splitTracker = target.Get<SplitTracker>();
            if (splitTracker != null && !splitTracker.HasSplit)
            {
                var tFighter = target.Require<Fighter>();
                double hpPct = tFighter.MaxHp > 0 ? (double)tFighter.Hp / tFighter.MaxHp : 0;
                if (hpPct < splitTracker.TriggerHpPct)
                {
                    splitTracker.HasSplit = true;
                    ResolveSplit(state, target, splitTracker, monsterFactory, events);
                    return; // original is gone — skip death processing
                }
            }
        }

        if (result.TargetKilled)
        {
            events.Add(new DeathEvent { ActorId = target.Id, KillerId = player.Id, ActorName = target.Name, KillerName = player.Name });
            DropMonsterLoot(state, target, events);

            // Instrumentation: emit OrcRepChangedEvent at the exact turn where the orc tally
            // crosses the Hostile threshold. Must happen BEFORE TransformToCorpse (which strips
            // AiComponent from the target) and BEFORE UpdateKnowledge (which calls AddUnprovokedKill
            // but reads a null faction from the already-stripped entity). Reading faction here, at
            // the kill site, is the only point where the faction is still present on the entity.
            // The event predicts the run-end mutation that fires in the presentation layer (Main.cs).
            EmitOrcRepChangedIfThresholdCrossed(state, target, events);

            TransformToCorpse(state, target, events, monsterFactory);
            ResolveDeathSiphon(state, target, events);

            // Shaman death cleanup: if the killed monster was channeling, remove
            // DissonantChantEffect from the player so they aren't permanently slowed.
            ResolveChannelCleanupOnDeath(state, target, events);
        }

        // Bonus attack chain — recurse if triggered and target still alive
        if (result.BonusAttackTriggered && !result.TargetKilled && target.Require<Fighter>().IsAlive)
        {
            ResolvePlayerAttack(state, target, events, isBonusAttack: true, monsterFactory);
        }
    }

    /// <summary>
    /// Resolve a ranged attack action. Delegates entirely to RangedCombatService.
    /// Validates that the player has a ranged weapon equipped — falls back to Wait if not.
    /// Also handles loot drop and corpse creation when the target is killed.
    /// </summary>
    private static void ResolveRangedAttack(GameState state, Entity target, List<TurnEvent> events)
    {
        var player = state.Player;

        // Guard: must have ranged weapon equipped (bow/crossbow with IsRangedWeapon=true).
        var equip = player.Get<Equipment>();
        var weapon = equip?.MainHand?.Get<Equippable>();
        if (weapon == null || !weapon.IsRangedWeapon)
        {
            // No ranged weapon — silently do nothing (treat as Wait).
            events.Add(new WaitEvent { ActorId = player.Id });
            return;
        }

        // InvisibilityEffect breaks on any attack.
        if (player.Has<InvisibilityEffect>())
        {
            player.Remove<InvisibilityEffect>();
            events.Add(new StatusExpiredEvent
            {
                ActorId    = player.Id,
                EntityId   = player.Id,
                EffectName = "invisibility",
                Reason     = "attacked",
            });
        }

        bool targetWasAlive = target.Get<Fighter>()?.IsAlive == true;

        RangedCombatService.AttemptRangedAttack(player, target, state, events);

        // If target was alive and is now dead, handle loot drop and corpse transformation.
        // RangedCombatService emits DeathEvent; TurnController owns the loot/corpse side effect.
        if (targetWasAlive && target.Get<Fighter>()?.IsAlive == false)
        {
            DropMonsterLoot(state, target, events);
            TransformToCorpse(state, target, events, monsterFactory: null);
            ResolveDeathSiphon(state, target, events);
        }

        // Ranged attacks reset momentum (no chaining from ranged).
        player.Get<SpeedBonusTracker>()?.ResetMomentum();
    }

    /// <summary>
    /// Resolve a split-under-pressure event. Original monster is removed and children are spawned.
    ///
    /// Null-factory fallback: test environments that don't inject a factory get a kill instead of
    /// a spawn — HP=0, emit DeathEvent. No crash, just no children.
    ///
    /// Children act on the spawn turn because they are added to state.Monsters before
    /// ResolveMonsterTurns completes. The AliveMonsters cache rebuilds on the next access.
    /// This is intentional: creates a dramatic "suddenly surrounded" moment matching PoC behavior.
    /// </summary>
    private static void ResolveSplit(GameState state, Entity original, SplitTracker split,
        MonsterFactory? monsterFactory, List<TurnEvent> events)
    {
        if (monsterFactory == null)
        {
            // No factory available — treat as a kill so tests that don't inject a factory
            // still see deterministic behavior (original dies, no children).
            original.Require<Fighter>().Hp = 0;
            events.Add(new DeathEvent { ActorId = original.Id, KillerId = state.Player.Id, ActorName = original.Name, KillerName = state.Player.Name });
            TransformToCorpse(state, original, events, monsterFactory: null);
            return;
        }

        int numChildren = RollSplitChildren(split, state.Rng);
        var positions = FindSplitPositions(state.Map, original.X, original.Y, numChildren, state);

        // Remove original from the map (HP=0 marks it dead for AliveMonsters cache).
        // No XP event — split is not a kill.
        original.Require<Fighter>().Hp = 0;
        state.Map.UnregisterEntity(original);

        var childIds = new List<int>();
        for (int i = 0; i < Math.Min(numChildren, positions.Count); i++)
        {
            var child = monsterFactory.Create(
                split.ChildType,
                positions[i].X, positions[i].Y,
                state.CurrentDepth,
                state.Rng);
            if (child == null) continue;

            state.Monsters.Add(child);
            state.Map.RegisterEntity(child);
            childIds.Add(child.Id);
        }

        events.Add(new SplitEvent
        {
            ActorId = original.Id,
            OriginalId = original.Id,
            ChildIds = childIds,
        });
    }

    /// <summary>
    /// Roll the number of children to spawn, using the split tracker's weight table if present.
    /// </summary>
    private static int RollSplitChildren(SplitTracker split, SeededRandom rng)
    {
        if (split.Weights == null || split.Weights.Length == 0)
            return rng.Next(split.MinChildren, split.MaxChildren + 1);

        int total = split.Weights.Sum();
        int roll = rng.Next(total);
        int running = 0;
        for (int i = 0; i < split.Weights.Length; i++)
        {
            running += split.Weights[i];
            if (roll < running)
                return split.MinChildren + i;
        }
        return split.MaxChildren;
    }

    /// <summary>
    /// Find valid spawn positions for split children using an expanding ring search.
    /// Mirrors PoC's _get_valid_spawn_positions. Fallback: if no positions found,
    /// returns origin position (children stack on top — unusual but not a crash).
    /// </summary>
    private static List<(int X, int Y)> FindSplitPositions(
        GameMap map, int cx, int cy, int count, GameState state)
    {
        var found = new List<(int X, int Y)>();
        var occupied = new HashSet<(int, int)>(
            state.Monsters
                .Where(m => m.Require<Fighter>().IsAlive)
                .Select(m => (m.X, m.Y)));
        occupied.Add((state.Player.X, state.Player.Y));

        for (int radius = 1; radius <= 3 && found.Count < count; radius++)
        {
            for (int dx = -radius; dx <= radius && found.Count < count; dx++)
            for (int dy = -radius; dy <= radius && found.Count < count; dy++)
            {
                // Only walk the ring perimeter, not the interior
                if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;
                int nx = cx + dx, ny = cy + dy;
                if (map.IsWalkable(nx, ny) && !occupied.Contains((nx, ny)))
                {
                    found.Add((nx, ny));
                    occupied.Add((nx, ny)); // reserve so next child doesn't pick the same spot
                }
            }
        }

        if (found.Count == 0) found.Add((cx, cy)); // fallback: stack on origin
        return found;
    }

    private static void ResolvePlayerMove(GameState state, PlayerAction action, List<TurnEvent> events,
        out bool freeAction)
    {
        freeAction = false;
        var player = state.Player;
        int fromX = player.X, fromY = player.Y;

        // EntangledEffect: player cannot move. CAN still attack (that's in ResolvePlayerAttack).
        if (player.Has<EntangledEffect>())
        {
            // Emit wait-equivalent (no movement), consume the turn.
            events.Add(new WaitEvent { ActorId = player.Id });
            // Record that movement was blocked by entangle — used by ranged combat metrics.
            events.Add(new EntangleMoveBlockedEvent
            {
                ActorId           = player.Id,
                EntityId          = player.Id,
                BlockedActionType = "move",
            });
            return;
        }

        // DisorientationEffect: player's intended direction is replaced with a random direction.
        // The move still consumes a turn — player taps, the character stumbles elsewhere.
        // If the random direction hits a wall, no movement occurs (same as normal wall bump).
        bool moved;
        if (player.Has<DisorientationEffect>())
        {
            // Determine the intended destination so we can pick a random direction instead.
            int destX = action.TargetX ?? (action.Target?.X ?? player.X);
            int destY = action.TargetY ?? (action.Target?.Y ?? player.Y);

            // Pick a random cardinal/diagonal direction — using game RNG for determinism.
            var directions = new (int dx, int dy)[]
            {
                (-1, -1), (0, -1), (1, -1),
                (-1,  0),          (1,  0),
                (-1,  1), (0,  1), (1,  1),
            };
            int idx = state.Rng.Next(0, directions.Length);
            var (rdx, rdy) = directions[idx];
            int randomDestX = player.X + rdx;
            int randomDestY = player.Y + rdy;

            // Disoriented moves: no feature interaction (player doesn't know where they're going)
            // Door check for disoriented move (player always can open doors)
            if (TryOpenDoor(state.Map, player, randomDestX, randomDestY, events))
                return;
            moved = state.Map.MoveToward(player, randomDestX, randomDestY);
        }
        else if (action.Target != null)
        {
            // Feature interaction: check before door/move. Returns true if handled (caller skips move).
            // consumesTurn=false → sign read → freeAction=true (no turn cost).
            if (TryInteractFeature(state, action.Target.X, action.Target.Y, events, out bool consumesTurnA))
            {
                freeAction = !consumesTurnA;
                return;
            }
            // Locked door bump: check target tile and each step on the path.
            // Returns true (as free action) when locked door blocked movement.
            if (TryHandleLockedDoorBump(state, player, action.Target.X, action.Target.Y, events, out bool lockedDoorFreeA))
            {
                freeAction = lockedDoorFreeA;
                return;
            }
            if (TryOpenDoorOnPath(state.Map, player, action.Target.X, action.Target.Y, events))
                return;
            moved = state.Map.MoveToward(player, action.Target.X, action.Target.Y);
        }
        else if (action.TargetX.HasValue && action.TargetY.HasValue)
        {
            // Feature interaction: check before door/move. Returns true if handled (caller skips move).
            // consumesTurn=false → sign read → freeAction=true (no turn cost).
            if (TryInteractFeature(state, action.TargetX.Value, action.TargetY.Value, events, out bool consumesTurnB))
            {
                freeAction = !consumesTurnB;
                return;
            }
            // Locked door bump: free action when blocked by a locked door.
            if (TryHandleLockedDoorBump(state, player, action.TargetX.Value, action.TargetY.Value, events, out bool lockedDoorFreeB))
            {
                freeAction = lockedDoorFreeB;
                return;
            }
            if (TryOpenDoor(state.Map, player, action.TargetX.Value, action.TargetY.Value, events))
                return;
            moved = state.Map.MoveToward(player, action.TargetX.Value, action.TargetY.Value);
        }
        else
            moved = false;

        if (moved)
        {
            events.Add(new MoveEvent
            {
                ActorId = player.Id,
                FromX = fromX, FromY = fromY,
                ToX = player.X, ToY = player.Y,
            });

            // Portal collision: check if player stepped onto a portal and teleport if so.
            // Runs before stair check so portals near stairs don't cause confusion.
            var portalTeleport = PortalSystem.CheckPortalCollision(player, state);
            if (portalTeleport != null)
                events.Add(portalTeleport);

            // Stair reached: stop auto-explore so the player must consciously tap to descend.
            // Auto-descend would swallow floors unintentionally during explore mode.
            if (state.IsDungeonMode && state.PlayerOnStairDown)
            {
                var ae = state.Player.Get<AutoExploreState>();
                if (ae != null && ae.IsActive)
                    AutoExploreSystem.Stop(ae, "Reached stairs");
            }

            // Floor trap check: test whether the player just stepped onto a trap.
            HandleFloorTrapEntry(state, player, player.X, player.Y, events, skipPassiveDetect: false);

            // Walk-over pickup: auto-collect any floor item at the new position
            TryPickUpItemsAt(state, player.X, player.Y, events);
        }
    }

    /// <summary>
    /// Check whether a floor trap is at (x, y) and process entry logic.
    ///
    /// Detection model (PoC-exact):
    ///   1. Find FloorTrapComponent at (x, y) that is not yet spent.
    ///   2. If already detected → emit TrapAvoidedEvent, skip trigger.
    ///   3. If skipPassiveDetect=true (monsters) → trigger immediately.
    ///   4. Else roll PassiveDetectChance:
    ///      - Success → mark IsDetected=true, emit TrapDetectedEvent, skip trigger.
    ///      - Failure → call TrapActionResolver.Resolve, mark IsSpent=true.
    /// </summary>
    private static void HandleFloorTrapEntry(GameState state, Entity mover, int x, int y,
        List<TurnEvent> events, bool skipPassiveDetect,
        MonsterFactory? monsterFactory = null)
    {
        // Find the first non-spent floor trap at this tile.
        // Floor traps do NOT block movement so they don't appear in Features.BlocksMovement path.
        // They are stored in Features (same list as chests/signs) but with BlocksMovement=false.
        var trapFeature = state.Features.FirstOrDefault(f =>
            f.X == x && f.Y == y && f.Get<FloorTrapComponent>() != null);
        if (trapFeature == null) return;

        var trap = trapFeature.Require<FloorTrapComponent>();
        if (trap.IsSpent) return;

        // Auto-avoid detected traps.
        if (trap.IsDetected)
        {
            events.Add(new TrapAvoidedEvent
            {
                ActorId  = mover.Id,
                X        = x,
                Y        = y,
                TrapType = trap.TrapType,
            });
            return;
        }

        // Monsters skip passive detection — they always trigger.
        if (!skipPassiveDetect && trap.IsDetectable)
        {
            double roll = state.Rng.NextDouble();
            if (roll < trap.PassiveDetectChance)
            {
                // Detected: mark and emit, do NOT trigger.
                trap.IsDetected = true;
                events.Add(new TrapDetectedEvent
                {
                    ActorId  = mover.Id,
                    X        = x,
                    Y        = y,
                    TrapType = trap.TrapType,
                });

                // Stop auto-explore on trap detection (same pattern as chest discovery).
                var ae = mover.Get<AutoExploreState>();
                if (ae != null && ae.IsActive)
                    AutoExploreSystem.Stop(ae, "Detected trap");

                return;
            }
        }

        // Trigger: mark spent BEFORE resolving to prevent recursion if a spawned monster
        // immediately walks onto the same tile.
        trap.IsSpent = true;

        var idAlloc = GetOrCreateIdAllocator(state);
        TrapActionResolver.Resolve(mover, trap.Payload,
            trap.TrapType, (x, y),
            state, state.Rng, events,
            monsterFactory: monsterFactory,
            idAllocator: idAlloc);

        // Acid trap weapon coating: if the player survives an acid_trap trigger, their
        // equipped main-hand weapon gains WeaponAcidCoatingComponent (4 hits, 6-turn AcidEffect).
        // Only applies to the player (not monsters), only when alive, only for acid_trap.
        if (trap.TrapType == "acid_trap"
            && mover.Id == state.Player.Id
            && mover.Get<Fighter>() is { } movedFighter && movedFighter.IsAlive)
        {
            var equip = mover.Get<Equipment>();
            var weapon = equip?.MainHand;
            if (weapon != null)
            {
                var existing = weapon.Get<WeaponAcidCoatingComponent>();
                if (existing != null)
                {
                    // Already coated — take the higher HitsRemaining to avoid downgrade.
                    if (4 > existing.HitsRemaining)
                        existing.HitsRemaining = 4;
                }
                else
                {
                    weapon.Add(new WeaponAcidCoatingComponent { HitsRemaining = 4, EffectDuration = 6 });
                }

                events.Add(new WeaponAcidCoatedEvent
                {
                    WeaponId       = weapon.Id,
                    HitsRemaining  = weapon.Require<WeaponAcidCoatingComponent>().HitsRemaining,
                });
            }
        }
    }

    /// <summary>
    /// Check if a feature entity (chest, signpost, mural) is at the given tile and interact with it.
    ///
    /// Called before door/move logic in ResolvePlayerMove. Returns true when the bump is fully
    /// consumed by the feature (movement should not also apply this turn).
    ///
    /// consumesTurn (out): true when the interaction costs a game turn; false for free actions.
    ///   - Chest (closed): costs a turn (loot drop, game time advances)
    ///   - Chest (open): costs no turn (blocked bump, but nothing to do)
    ///   - Signpost: free action (no turn consumed — clock does not advance)
    ///   - Mural: costs a turn (reading a mural is a deliberate action)
    ///
    /// The caller uses consumesTurn via the freeAction flag to decide whether TurnCount
    /// is decremented and monster turns are skipped.
    /// </summary>
    private static bool TryInteractFeature(GameState state, int x, int y, List<TurnEvent> events,
        out bool consumesTurn)
    {
        consumesTurn = true; // default: costs a turn (safe for unhandled cases)

        // Find the first BLOCKING feature entity at this tile.
        // Floor traps (BlocksMovement=false) are intentionally excluded — they are handled
        // after movement by HandleFloorTrapEntry, not here at the bump point.
        var feature = state.Features.FirstOrDefault(f => f.X == x && f.Y == y && f.BlocksMovement);
        if (feature == null)
        {
            consumesTurn = true;
            return false; // no blocking feature here — let normal door/move logic proceed
        }

        var chest = feature.Get<ChestComponent>();
        if (chest != null)
        {
            if (chest.IsOpen && !chest.IsLooted)
            {
                // Second tap on an open chest: collect items sitting on the chest tile into inventory.
                var atChest = state.FloorItems.Where(i => i.X == feature.X && i.Y == feature.Y).ToList();
                foreach (var lootItem in atChest)
                {
                    lootItem.X = state.Player.X;
                    lootItem.Y = state.Player.Y;
                }
                TryPickUpItemsAt(state, state.Player.X, state.Player.Y, events);
                chest.IsLooted = true;
                events.Add(new ChestLootedEvent { ActorId = state.Player.Id, X = feature.X, Y = feature.Y });
                consumesTurn = true;
                return true;
            }

            if (chest.IsOpen && chest.IsLooted)
            {
                // Already looted: bump is consumed (chest still blocks movement) but no turn spent.
                consumesTurn = false;
                return true;
            }

            // Locked chest check: if the chest has a LockableComponent and is still locked,
            // require a matching colored key in the player's inventory.
            var lockable = feature.Get<LockableComponent>();
            if (lockable != null && lockable.IsLocked)
            {
                var inventory = state.Player.GetOrAdd<Inventory>();
                var matchingKey = inventory.FindFirst(item =>
                {
                    var key = item.Get<KeyItemComponent>();
                    return key != null && key.LockColorId == lockable.LockColorId;
                });

                if (matchingKey == null)
                {
                    // No matching key — emit feedback event, consume a turn, do not open.
                    events.Add(new ChestLockedEvent
                    {
                        ActorId      = state.Player.Id,
                        ChestId      = feature.Id,
                        LockColorId  = lockable.LockColorId,
                        X            = feature.X,
                        Y            = feature.Y,
                    });
                    consumesTurn = true;
                    return true;
                }

                // Found matching key — consume it and unlock the chest.
                int keyId = matchingKey.Id;
                inventory.Remove(matchingKey);
                lockable.IsLocked = false;

                events.Add(new KeyConsumedEvent
                {
                    ActorId     = state.Player.Id,
                    KeyId       = keyId,
                    LockColorId = lockable.LockColorId,
                });
                events.Add(new ChestUnlockedEvent
                {
                    ActorId     = state.Player.Id,
                    ChestId     = feature.Id,
                    KeyId       = keyId,
                    LockColorId = lockable.LockColorId,
                    X           = feature.X,
                    Y           = feature.Y,
                });
                // Fall through to normal chest-open logic below.
            }

            // Open the chest (first tap): place loot at the CHEST tile so they are visible.
            // Items stay there until the player bumps again (second tap) to collect them.
            chest.IsOpen = true;

            var lootStash = feature.Get<ChestLootStash>();
            var droppedIds = new List<int>();
            var player = state.Player;

            if (lootStash != null)
            {
                foreach (var item in lootStash.Items)
                {
                    item.X = feature.X;
                    item.Y = feature.Y;
                    state.FloorItems.Add(item);
                    state.Map.RegisterEntity(item);
                    droppedIds.Add(item.Id);
                }
                lootStash.Items.Clear();
            }

            events.Add(new ChestOpenedEvent
            {
                ActorId = player.Id,
                X = feature.X,
                Y = feature.Y,
                DroppedItemIds = droppedIds,
            });

            // No auto-pickup here — items remain on the chest tile until the player bumps again.
            consumesTurn = true;
            return true;
        }

        var sign = feature.Get<SignpostComponent>();
        if (sign != null)
        {
            // Reading a sign is a free action — emits event even if already read before.
            // PoC: re-reading a sign is allowed (same message each time).
            sign.HasBeenRead = true;

            events.Add(new SignpostReadEvent
            {
                X = feature.X,
                Y = feature.Y,
                Message = sign.Message,
                SignType = sign.SignType,
            });

            consumesTurn = false; // sign read is a free action — TurnCount will be reversed
            return true;
        }

        var mural = feature.Get<MuralComponent>();
        if (mural != null)
        {
            mural.HasBeenExamined = true;

            events.Add(new MuralExaminedEvent
            {
                X = feature.X,
                Y = feature.Y,
                Text = mural.Text,
                MuralId = mural.MuralId,
            });

            consumesTurn = true; // reading a mural costs a turn
            return true;
        }

        var prop = feature.Get<DestructiblePropComponent>();
        if (prop != null)
        {
            if (prop.IsResolved)
            {
                // Already resolved: bump is consumed (entity still blocks movement)
                // but no turn is spent and nothing happens. Player just bumps.
                consumesTurn = false;
                return true;
            }

            prop.IsResolved = true;
            var player = state.Player;
            bool trapFired = false;
            bool monsterRoused = false;

            // Drop pre-staged loot to player tile and auto-pick up.
            var lootStash = feature.Get<ChestLootStash>(); // reuse ChestLootStash for prop loot
            var droppedIds = new List<int>();
            if (lootStash != null)
            {
                foreach (var item in lootStash.Items)
                {
                    item.X = player.X;
                    item.Y = player.Y;
                    state.FloorItems.Add(item);
                    state.Map.RegisterEntity(item);
                    droppedIds.Add(item.Id);
                }
                lootStash.Items.Clear();
            }

            events.Add(new PropDestroyedEvent
            {
                ActorId       = player.Id,
                X             = feature.X,
                Y             = feature.Y,
                PropKind      = prop.PropKind,
                DroppedItemIds = droppedIds,
                TrapFired      = false,   // updated below if trap fires
                MonsterRoused  = false,
            });

            TryPickUpItemsAt(state, player.X, player.Y, events);

            // Fire trap payload if present.
            if (prop.TrapPayload != null)
            {
                var idAlloc = GetOrCreateIdAllocator(state);
                TrapActionResolver.Resolve(player, prop.TrapPayload,
                    $"{prop.PropKind}_trap", (feature.X, feature.Y),
                    state, state.Rng, events,
                    monsterFactory: null, idAllocator: idAlloc);
                trapFired = true;
            }

            // Fire rouse action if present.
            if (prop.RouseAction != null)
            {
                var rousePayload = new TrapPayloadComponent();
                rousePayload.Actions.Add(prop.RouseAction);
                var idAlloc = GetOrCreateIdAllocator(state);

                // We need monsterFactory for spawn_monster — not available here.
                // The rouse fires as a no-op when no factory is injected (tests).
                // In dungeon mode, ProcessTurn passes monsterFactory; but TryInteractFeature
                // doesn't receive it. For now: rouse fires without factory (no-op in non-dungeon).
                // Full wiring deferred to Phase 4 when EntityPlacer places bone piles in dungeon mode.
                bool rouseResult = TrapActionResolver.Resolve(player, rousePayload,
                    "bone_pile_rouse", (feature.X, feature.Y),
                    state, state.Rng, events,
                    monsterFactory: null, idAllocator: idAlloc);

                if (rouseResult)
                    monsterRoused = true;
            }

            // Update the PropDestroyedEvent with actual trap/rouse results.
            // TurnEvent is init-only so we need to replace the event.
            var existingPropEvt = events.OfType<PropDestroyedEvent>()
                .LastOrDefault(e => e.X == feature.X && e.Y == feature.Y);
            if (existingPropEvt != null && (trapFired || monsterRoused))
            {
                int idx = events.LastIndexOf(existingPropEvt);
                events[idx] = new PropDestroyedEvent
                {
                    ActorId        = existingPropEvt.ActorId,
                    X              = existingPropEvt.X,
                    Y              = existingPropEvt.Y,
                    PropKind       = existingPropEvt.PropKind,
                    DroppedItemIds = existingPropEvt.DroppedItemIds,
                    TrapFired      = trapFired,
                    MonsterRoused  = monsterRoused,
                };
            }

            consumesTurn = true;
            return true;
        }

        // Feature exists but has no recognized component — treat as a blocking bump.
        consumesTurn = false;
        return true;
    }

    /// <summary>
    /// Get the floor's EntityIdAllocator from state, or create a fallback one based on max existing ID.
    /// The fallback is used in tests and scenario mode where no allocator was set at floor-build time.
    /// </summary>
    private static EntityIdAllocator GetOrCreateIdAllocator(GameState state)
    {
        if (state.IdAllocator != null)
            return state.IdAllocator;

        // Fallback: compute max ID across all entities and start from there.
        int maxId = 0;
        if (state.Player.Id > maxId) maxId = state.Player.Id;
        foreach (var m in state.Monsters) if (m.Id > maxId) maxId = m.Id;
        foreach (var f in state.Features) if (f.Id > maxId) maxId = f.Id;
        foreach (var item in state.FloorItems) if (item.Id > maxId) maxId = item.Id;
        return new EntityIdAllocator(startFrom: maxId + 1);
    }

    /// <summary>
    /// Pick up all floor items at the given position. Each picked-up item is added to
    /// the player's inventory (creating one if needed), removed from FloorItems, and
    /// reported as a PickUpEvent.
    /// </summary>
    private static void TryPickUpItemsAt(GameState state, int x, int y, List<TurnEvent> events)
    {
        var toPickUp = state.FloorItems.Where(item => item.X == x && item.Y == y).ToList();
        if (toPickUp.Count == 0) return;

        var inventory = state.Player.GetOrAdd<Inventory>();

        foreach (var item in toPickUp)
        {
            // Wand auto-recharge: if the item is a scroll with a SpellEffect, check whether
            // the player holds a wand whose RechargeScrollId matches. If so, consume the scroll
            // to add one charge instead of placing it in inventory.
            var spellEffect = item.Get<SpellEffect>();
            if (spellEffect != null && TryRechargeWand(state, item, spellEffect, events))
            {
                // Scroll was consumed for recharge — remove from floor, do NOT add to inventory.
                state.FloorItems.Remove(item);
                state.Map.UnregisterEntity(item);
                continue;
            }

            // Add returns false when inventory is full and the item could not be stacked.
            // In that case leave the item on the floor — do NOT remove it or emit an event.
            if (!inventory.Add(item))
                continue;

            state.FloorItems.Remove(item);
            state.Map.UnregisterEntity(item);

            events.Add(new PickUpEvent
            {
                ActorId = state.Player.Id,
                ItemId = item.Id,
                ItemName = ItemDisplay.GetDisplayName(item, state.IdentificationRegistry, state.AppearancePool),
            });
        }
    }

    /// <summary>
    /// Check whether the item's type needs to be identified after a use/equip action.
    /// If the type is newly identified, emits an IdentificationEvent.
    ///
    /// No-op when the state has no identification registry (scenario/harness mode)
    /// or when the item has no ItemTag (weapons/armor — always identified).
    /// </summary>
    private static void TryIdentifyOnUse(GameState state, Entity item, List<TurnEvent> events, string trigger)
    {
        var registry = state.IdentificationRegistry;
        if (registry == null) return;

        var tag = item.Get<ECS.ItemTag>();
        if (tag == null) return;

        var idComp = item.Get<ECS.IdentifiableItem>();
        if (idComp == null) return;

        // Identify returns true only the FIRST time this type is identified this run.
        bool newlyIdentified = registry.Identify(tag.TypeId);
        if (newlyIdentified)
        {
            events.Add(new IdentificationEvent
            {
                ActorId        = state.Player.Id,
                TypeId         = tag.TypeId,
                IdentifiedName = idComp.IdentifiedName,
                Trigger        = trigger,
            });
        }
    }

    /// <summary>
    /// Check if a picked-up scroll should auto-recharge a wand the player is carrying.
    /// Returns true if the scroll was consumed for recharge (caller should skip normal pickup).
    ///
    /// Auto-recharge rule: if the player's inventory contains a wand whose RechargeScrollId
    /// matches the picked-up scroll's SpellId, and that wand is below MaxCharges, consume
    /// the scroll to add one charge instead of adding it to inventory.
    ///
    /// Gate: both scroll and wand must be identified. An unidentified scroll cannot be
    /// absorbed by an unidentified wand — the magical concealment prevents it.
    /// </summary>
    private static bool TryRechargeWand(GameState state, Entity scroll, SpellEffect scrollSpell,
        List<TurnEvent> events)
    {
        var inventory = state.PlayerInventory;
        if (inventory == null) return false;

        // Identification gate: an unidentified scroll cannot auto-recharge an unidentified wand.
        // The player doesn't know what either item is, so the wand doesn't "know" to accept it.
        // When the registry is null (scenario mode), identification is off and recharge always fires.
        var registry = state.IdentificationRegistry;
        if (registry != null)
        {
            var scrollTag = scroll.Get<ECS.ItemTag>();
            if (scrollTag != null && !registry.IsIdentified(scrollTag.TypeId))
                return false; // Unidentified scroll goes to inventory normally
        }

        // Find a wand in inventory that lists this scroll's spell_id as its recharge source
        var wand = inventory.Items.FirstOrDefault(item =>
        {
            var w = item.Get<WandComponent>();
            if (w == null || w.Infinite || w.RechargeScrollId != scrollSpell.SpellId || w.Charges >= w.MaxCharges)
                return false;

            // Identification gate: wand must also be identified for auto-recharge to fire.
            if (registry != null)
            {
                var wandTag = item.Get<ECS.ItemTag>();
                if (wandTag != null && !registry.IsIdentified(wandTag.TypeId))
                    return false;
            }

            return true;
        });

        if (wand == null) return false;

        var wandComp = wand.Require<WandComponent>();
        wandComp.Charges++;

        events.Add(new WandRechargeEvent
        {
            ActorId = state.Player.Id,
            WandName = wand.Name,
            ScrollName = scroll.Name,
            NewCharges = wandComp.Charges,
        });

        return true;
    }

    /// <summary>
    /// Resolve a Descend action.
    ///
    /// Guards (any failing → treat as Wait, emit WaitEvent):
    ///   - Must be dungeon mode
    ///   - Player must be standing on the stair
    ///
    /// On success: emit DescendEvent. Monsters do NOT act this turn (handled in ProcessTurn).
    /// The actual floor transition (building next GameState) is handled by the presentation layer
    /// on receipt of DescendEvent.
    /// </summary>
    private static void ResolveDescend(GameState state, List<TurnEvent> events)
    {
        var player = state.Player;

        if (!state.IsDungeonMode || !state.PlayerOnStairDown)
        {
            // Guard failed — treat as wait, do NOT reset momentum (stair tap is like waiting)
            events.Add(new WaitEvent { ActorId = player.Id });
            return;
        }

        events.Add(new DescendEvent
        {
            ActorId = player.Id,
            NewDepth = state.CurrentDepth + 1,
        });
    }

    /// <summary>
    /// The verb for using this item, read off the components the factories already give it:
    ///   WandComponent                       -> "point"   (SpellItemFactory.CreateWand)
    ///   Consumable, potion or healing       -> "drink"   (ConsumableFactory)
    ///   Consumable, neither                 -> "read"    (SpellItemFactory.CreateScroll,
    ///                                                     which builds scrolls as
    ///                                                     Consumable(healAmount: 0))
    ///
    /// Derived rather than stored: no new component, no mid-run DTO, no AotObjectFactory
    /// registration. The composition above is exhaustive for every usable item in content.
    /// </summary>
    private static string UseVerb(Entity item)
    {
        if (item.Get<WandComponent>() != null) return "point";

        var consumable = item.Get<Consumable>();
        if (consumable == null) return "use";

        return consumable.IsPotion || consumable.IsHealing ? "drink" : "read";
    }

    /// <summary>
    /// Announce an item use. ONE call per resolver arm, at the point the use commits -
    /// never per-effect, so it fires even when the effect is invisible. Drinking a potion
    /// of invisibility is the case that proves it: it emits a SpellEvent and nothing else,
    /// and every message the player used to see came from an effect event.
    ///
    /// Emitted before the effect resolves so the log reads in the order it happened:
    /// "You drink the Healing Potion." then "+40 HP from Healing Potion."
    /// </summary>
    private static void AnnounceItemUse(GameState state, Entity item, List<TurnEvent> events)
    {
        events.Add(new PlayerItemUseEvent
        {
            ActorId  = state.Player.Id,
            ItemName = ItemDisplay.GetDisplayName(item, state.IdentificationRegistry, state.AppearancePool),
            Verb     = UseVerb(item),
        });
    }

    private static void TryHeal(GameState state, Entity? specificItem, List<TurnEvent> events)
    {
        var fighter = state.PlayerFighter;
        var inventory = state.PlayerInventory;
        if (inventory == null) return;

        // Use specific item if provided (UI), otherwise find first healing potion (bot).
        // For cooldown-gated potions: only pick one whose cooldown allows use right now.
        //
        // QuickSlotModel.CanUseHealingPotion is the single availability answer — the same one
        // the quick-slot bar dims from. It used to be a private copy of that expression here,
        // which is how the bar and the rules were free to disagree.
        //
        // Note this gates the AUTO-PICK only: a caller that names an item (a UI tap) still
        // reaches the drink below. The refusal for a named item lives at the tap seam in
        // GameController, matching the spent-wand precedent — refuse before the turn is
        // spent rather than half-resolving one inside ProcessTurn.
        var potion = specificItem
            ?? inventory.FindFirst(item => QuickSlotModel.CanUseHealingPotion(item, fighter));

        if (potion == null) return;

        var consumable = potion.Get<Consumable>();
        if (consumable == null) return;

        // The use has committed — announce it. Everything below this point consumes.
        AnnounceItemUse(state, potion, events);

        // Apply cooldown before healing so the set happens even if heal returns 0 (already at max HP).
        if (consumable.UseCooldownTurns > 0)
            fighter.PotionCooldownRemaining = consumable.UseCooldownTurns;

        int healed = fighter.Heal(consumable.HealAmount);

        // Stack-aware consumption: decrement stack count; only remove the entity when
        // the last charge is used. This keeps the inventory slot alive for stacked potions.
        consumable.StackSize--;
        if (consumable.StackSize <= 0)
            inventory.Remove(potion);

        // Healing potion clears severity-1 bleed (minor wound stanched by bandaging / magical healing).
        // Severity-2 bleed (deep wound) requires a dedicated remedy — not cleared here.
        var bleed = state.Player.Get<BleedEffect>();
        if (bleed != null && bleed.Severity == 1)
        {
            state.Player.Remove<BleedEffect>();
            events.Add(new StatusExpiredEvent
            {
                ActorId    = state.Player.Id,
                EntityId   = state.Player.Id,
                EffectName = "bleed",
                Reason     = "healed",
            });
        }

        events.Add(new HealEvent
        {
            ActorId = state.Player.Id,
            AmountHealed = healed,
            ItemId = potion.Id,
            ItemName = ItemDisplay.GetDisplayName(potion, state.IdentificationRegistry, state.AppearancePool),
        });

        // Identification on use: potions are identified when consumed.
        // Effect fires first (heal), then identification check, then toast if newly identified.
        TryIdentifyOnUse(state, potion, events, trigger: "used");
    }

    /// <summary>
    /// Drop an item from the player's inventory onto the floor at the player's position.
    /// Dropping costs a turn — monsters will still act afterward.
    /// </summary>
    private static void ResolveDrop(GameState state, Entity item, List<TurnEvent> events)
    {
        var inventory = state.PlayerInventory;
        if (inventory == null) return;

        // Item must actually be in the player's inventory
        if (!inventory.Remove(item)) return;

        // Place the item at the player's current position
        item.X = state.Player.X;
        item.Y = state.Player.Y;

        state.FloorItems.Add(item);
        state.Map.RegisterEntity(item);

        events.Add(new DropEvent
        {
            ActorId = state.Player.Id,
            ItemId = item.Id,
            ItemName = item.Name,
        });
    }

    /// <summary>
    /// Equip an item from the player's inventory into its designated slot.
    /// If the slot is already occupied, the displaced item is returned to inventory.
    /// Costs a turn. Guard: item must be in inventory and have an Equippable component.
    /// </summary>
    private static void ResolveEquip(GameState state, Entity item, List<TurnEvent> events)
    {
        var inventory = state.PlayerInventory;
        if (inventory == null) return;

        var equippable = item.Get<Equippable>();
        if (equippable == null) return;

        // Item must be in the player's inventory.
        if (!inventory.Remove(item)) return;

        // Quiver slot guard: only IsSpecialAmmo items can be equipped in the Quiver.
        // Non-ammo items with slot=Quiver would be a YAML authoring mistake — silently abort.
        if (equippable.Slot == EquipmentSlot.Quiver && !equippable.IsSpecialAmmo)
        {
            inventory.Add(item); // return to inventory
            return;
        }

        var equipment = state.Player.GetOrAdd<Equipment>();

        // Ring slot auto-assignment: all rings are created with slot=LeftRing.
        // If LeftRing is occupied but RightRing is free, auto-redirect to RightRing.
        // This prevents forcing the player to manually juggle left vs. right.
        var targetSlot = equippable.Slot;
        if (targetSlot == EquipmentSlot.LeftRing && equipment.LeftRing != null && equipment.RightRing == null)
            targetSlot = EquipmentSlot.RightRing;

        // Two-handed weapon: clear OffHand slot BEFORE placing the weapon in MainHand.
        // This prevents bow + shield stacking. The displaced off-hand item is returned to
        // inventory (or dropped if inventory is full) before the weapon equip event fires.
        if (equippable.TwoHanded && targetSlot == EquipmentSlot.MainHand && equipment.OffHand != null)
        {
            var offHandItem = equipment.SetSlot(EquipmentSlot.OffHand, null);
            if (offHandItem != null)
            {
                if (!inventory.Add(offHandItem))
                {
                    // Inventory full — drop the off-hand item at the player's feet.
                    offHandItem.X = state.Player.X;
                    offHandItem.Y = state.Player.Y;
                    state.FloorItems.Add(offHandItem);
                    state.Map.RegisterEntity(offHandItem);
                    events.Add(new DropEvent
                    {
                        ActorId  = state.Player.Id,
                        ItemId   = offHandItem.Id,
                        ItemName = offHandItem.Name,
                    });
                }
            }
        }

        var displaced = equipment.SetSlot(targetSlot, item);

        // Attempt to return the displaced item to inventory. If inventory is full and
        // the displaced item can't be stacked, drop it at the player's feet instead.
        int? displacedId = null;
        string? displacedName = null;
        if (displaced != null)
        {
            displacedId   = displaced.Id;
            displacedName = displaced.Name;
            if (!inventory.Add(displaced))
            {
                // Inventory full — drop displaced item on floor.
                displaced.X = state.Player.X;
                displaced.Y = state.Player.Y;
                state.FloorItems.Add(displaced);
                state.Map.RegisterEntity(displaced);
                events.Add(new DropEvent
                {
                    ActorId  = state.Player.Id,
                    ItemId   = displaced.Id,
                    ItemName = displaced.Name,
                });
            }
        }

        // Propagate weapon speed_bonus to player's SpeedBonusTracker so momentum
        // system activates when equipping a weapon that has a speed bonus.
        if (targetSlot == EquipmentSlot.MainHand)
        {
            double weaponSpeed = item.Get<SpeedBonusTracker>()?.EquipmentRatio ?? 0;
            var tracker = state.Player.Get<SpeedBonusTracker>();
            if (tracker == null && weaponSpeed > 0)
            {
                tracker = new SpeedBonusTracker();
                state.Player.Add(tracker);
            }
            if (tracker != null) tracker.EquipmentRatio = weaponSpeed;
        }

        // If a ring was displaced, reverse its effect before applying the new ring's effect.
        // This ensures stat accounting stays correct when swapping rings.
        if (displaced != null && (targetSlot == EquipmentSlot.LeftRing || targetSlot == EquipmentSlot.RightRing))
        {
            var displacedEffect = displaced.Get<RingEffectComponent>();
            if (displacedEffect != null)
                ApplyRingEffect(state.Player, displacedEffect, equip: false, events);
        }

        // Apply equip effect for rings
        if (targetSlot == EquipmentSlot.LeftRing || targetSlot == EquipmentSlot.RightRing)
        {
            var ringEffect = item.Get<RingEffectComponent>();
            if (ringEffect != null)
                ApplyRingEffect(state.Player, ringEffect, equip: true, events);
        }

        events.Add(new EquipEvent
        {
            ActorId          = state.Player.Id,
            ItemId           = item.Id,
            ItemName         = item.Name,
            Slot             = targetSlot,
            DisplacedItemId  = displacedId,
            DisplacedItemName = displacedName,
        });

        // Identification on equip: rings are identified when equipped.
        // EquipEvent fires first, then identification check, then toast if newly identified.
        TryIdentifyOnUse(state, item, events, trigger: "equipped");
    }

    /// <summary>
    /// Unequip the item in the given slot and return it to the player's inventory.
    /// Guard: slot must be occupied; inventory must not be full (unless the item can stack).
    /// If inventory is full, the action is silently ignored (no event emitted).
    /// </summary>
    private static void ResolveUnequip(GameState state, EquipmentSlot slot, List<TurnEvent> events)
    {
        var equipment = state.Player.Get<Equipment>();
        if (equipment == null) return;

        var item = equipment.GetSlot(slot);
        if (item == null) return;

        var inventory = state.Player.GetOrAdd<Inventory>();
        if (!inventory.Add(item)) return; // Inventory full — block the action

        equipment.SetSlot(slot, null);

        // Clear weapon speed contribution when unequipping from main hand.
        if (slot == EquipmentSlot.MainHand)
        {
            var tracker = state.Player.Get<SpeedBonusTracker>();
            if (tracker != null) tracker.EquipmentRatio = 0;
        }

        // Reverse ring effects on unequip
        if (slot == EquipmentSlot.LeftRing || slot == EquipmentSlot.RightRing)
        {
            var ringEffect = item.Get<RingEffectComponent>();
            if (ringEffect != null)
                ApplyRingEffect(state.Player, ringEffect, equip: false, events);
        }

        events.Add(new UnequipEvent
        {
            ActorId  = state.Player.Id,
            ItemId   = item.Id,
            ItemName = item.Name,
            Slot     = slot,
        });
    }

    /// <summary>
    /// Environment phase: tick all active ground hazards, apply damage to any entity
    /// standing on a hazard tile, then age and expire the hazards.
    ///
    /// Runs after monster turns, before FOV recompute. Applies to player AND monsters.
    /// Damage decays linearly each tick: floor(base × remaining / max).
    /// Monster deaths from hazards are fully resolved (loot drop, corpse transform).
    /// </summary>
    private static void TickEnvironment(GameState state, List<TurnEvent> events,
        MonsterFactory? monsterFactory)
    {
        var manager = state.GroundHazards;
        if (manager.Hazards.Count == 0) return;

        // Snapshot so we don't mutate while iterating.
        var activeHazards = manager.Hazards.Values.ToList();

        // Clear JustPlaced flag on newly-placed hazards before damage/aging so they are
        // ready to tick from next turn. Don't damage or age them this turn.
        var toTickNow = new List<GroundHazard>(activeHazards.Count);
        foreach (var hazard in activeHazards)
        {
            if (hazard.JustPlaced) { hazard.JustPlaced = false; continue; }
            toTickNow.Add(hazard);
        }

        foreach (var hazard in toTickNow)
        {
            int dmg = hazard.CurrentDamage;
            if (dmg <= 0) continue;

            string effectName = hazard.Type == HazardType.Fire ? "fire" : "poison gas";

            // Player on this tile?
            var pf = state.PlayerFighter;
            if (pf.IsAlive && state.Player.X == hazard.X && state.Player.Y == hazard.Y)
            {
                pf.TakeDamage(dmg);
                events.Add(new DotDamageEvent
                {
                    ActorId    = state.Player.Id,
                    EntityId   = state.Player.Id,
                    EffectName = effectName,
                    Damage     = dmg,
                });
                if (!pf.IsAlive)
                {
                    events.Add(new DeathEvent { ActorId = state.Player.Id, KillerId = -1, ActorName = state.Player.Name });
                    state.PlayerDeathCause = "hazard";
                }
            }

            // Monsters on this tile?
            foreach (var monster in state.AliveMonsters.ToList())
            {
                if (monster.X != hazard.X || monster.Y != hazard.Y) continue;
                var mf = monster.Require<Fighter>();
                if (!mf.IsAlive) continue;

                mf.TakeDamage(dmg);
                events.Add(new DotDamageEvent
                {
                    ActorId    = monster.Id,
                    EntityId   = monster.Id,
                    EffectName = effectName,
                    Damage     = dmg,
                });
                if (!mf.IsAlive)
                {
                    events.Add(new DeathEvent { ActorId = monster.Id, KillerId = -1, ActorName = monster.Name });
                    DropMonsterLoot(state, monster, events);
                    TransformToCorpse(state, monster, events, monsterFactory);
                }
            }
        }

        // Age only hazards that ticked this turn; newly-placed ones are untouched.
        foreach (var hazard in toTickNow)
            hazard.RemainingTurns--;
        manager.RemoveExpired();
    }

    private static void ResolveMonsterTurns(GameState state, List<TurnEvent> events,
        MonsterFactory? monsterFactory = null, EntityFactory? portalEntityFactory = null)
    {
        // Snapshot the list — monsters may die during resolution (e.g. player bonus attacks
        // killing a monster before its turn, or future reflect damage). Iterating a snapshot
        // prevents collection-modified exceptions and mirrors the Python prototype's behavior.
        var monsters = state.AliveMonsters.ToList();

        foreach (var monster in monsters)
        {
            if (!monster.Require<Fighter>().IsAlive) continue;
            if (!state.PlayerFighter.IsAlive) break; // player died mid-turn — stop processing

            // Process start-of-turn effects: DOT/HOT ticks, skip-turn determination.
            bool monsterSkipTurn = StatusEffectProcessor.ProcessTurnStart(monster, events, state.TurnCount, state);
            if (monsterSkipTurn)
            {
                // Still decrement durations even on a skipped turn — time passes for sleeping/immobilized entities.
                StatusEffectProcessor.ProcessTurnEnd(monster, events);
                continue;
            }

            // Wand-kick: monster adjacent to the unattended home body during player-initiated possession
            // gets a 1-in-N chance to kick the phantom wand. Does not consume the monster's action.
            TryKickWand(monster, state, events);

            var action = MonsterAI.Decide(monster, state);

            // EntangleMoveBlockedEvent for skirmisher leap: if the skirmisher is entangled,
            // in leap range, and the action resolved to Wait (because EntangledEffect prevented the leap),
            // emit the event. TurnController owns this cross-cutting detection because MonsterAction
            // has no event list — SkirmisherAI communicates via the returned action kind only.
            if (action.Kind == MonsterAction.ActionKind.Wait && monster.Has<EntangledEffect>())
            {
                var skirmComp = monster.Get<SkirmisherComponent>();
                if (skirmComp != null)
                {
                    int dist = monster.ChebyshevDistanceTo(state.Player.X, state.Player.Y);
                    if (dist >= skirmComp.LeapRangeMin && dist <= skirmComp.LeapRangeMax)
                    {
                        events.Add(new EntangleMoveBlockedEvent
                        {
                            ActorId           = monster.Id,
                            EntityId          = monster.Id,
                            BlockedActionType = "leap",
                        });
                    }
                }
            }

            switch (action.Kind)
            {
                case MonsterAction.ActionKind.Attack:
                    ResolveMonsterAttack(state, monster, action.Target!, events, isBonusAttack: false,
                        monsterFactory: monsterFactory);
                    break;

                case MonsterAction.ActionKind.MoveTo:
                case MonsterAction.ActionKind.SeekItem:
                    // A* already computed the exact next step (adjacent tile), so MoveToward
                    // resolves directly without extra greedy computation.
                    int fromX = monster.X, fromY = monster.Y;
                    // Door check — intelligent monsters open doors before stepping through.
                    if (TryOpenDoor(state.Map, monster, action.TargetX, action.TargetY, events))
                        break;
                    bool moved = state.Map.MoveToward(monster, action.TargetX, action.TargetY);
                    if (moved)
                    {
                        events.Add(new MoveEvent
                        {
                            ActorId = monster.Id,
                            FromX = fromX, FromY = fromY,
                            ToX = monster.X, ToY = monster.Y,
                        });

                        // Portal collision: monsters can also use portals.
                        var monsterPortalTeleport = PortalSystem.CheckPortalCollision(monster, state);
                        if (monsterPortalTeleport != null)
                            events.Add(monsterPortalTeleport);

                        // Floor trap: monsters skip passive detection and always trigger.
                        // Only check if the monster is still alive after the portal (teleport might have moved it).
                        if (monster.Require<Fighter>().IsAlive)
                        {
                            HandleFloorTrapEntry(state, monster, monster.X, monster.Y, events,
                                skipPassiveDetect: true, monsterFactory: monsterFactory);
                        }
                    }
                    break;

                case MonsterAction.ActionKind.PickUp:
                    ResolveMonsterPickUp(state, monster, action.Target!, events);
                    break;

                case MonsterAction.ActionKind.UseItem:
                    ResolveMonsterItemUse(state, monster, action.Target!, events);
                    break;

                case MonsterAction.ActionKind.RaiseDead:
                    ResolveNecromancerRaise(state, monster, action.Target!, events);
                    break;

                case MonsterAction.ActionKind.SoulBolt:
                    var lichComp = monster.Get<LichAiComponent>();
                    if (lichComp != null)
                        SoulBoltResolver.Resolve(monster, action.Target!, lichComp.SoulBoltDamagePct, events);
                    break;

                case MonsterAction.ActionKind.Channel:
                    events.Add(new ChannelEvent { ActorId = monster.Id, AbilityName = action.AbilityName ?? "" });
                    break;

                case MonsterAction.ActionKind.Wait:
                    break;
            }

            // Decrement duration for all monster effects after it acts.
            StatusEffectProcessor.ProcessTurnEnd(monster, events);
        }
    }

    /// <summary>
    /// Necromancer raises a fresh corpse in-place. Sets the raise cooldown and emits RaiseDeadEvent.
    /// For plague_necromancer, adds plague_carrier tag and a small stat boost to the raised entity.
    /// </summary>
    private static void ResolveNecromancerRaise(
        GameState state, Entity necromancer, Entity corpse, List<TurnEvent> events)
    {
        var necComp = necromancer.Get<NecromancerAiComponent>();
        var corpseComp = corpse.Get<CorpseComponent>();
        if (necComp == null || corpseComp == null || !corpseComp.CanBeRaised) return;

        string corpseId = corpseComp.CorpseId;
        string raisedFaction = necromancer.Get<AiComponent>()?.Faction ?? "neutral";

        RaiseDeadResolver.Raise(corpse, raisedFaction, state);

        // Plague necromancer: add plague_carrier tag + small stat boost to raised entity
        if (string.Equals(necromancer.Get<AiComponent>()?.AiType, "plague_necromancer",
                StringComparison.OrdinalIgnoreCase))
        {
            var raisedFighter = corpse.Get<Fighter>();
            if (raisedFighter != null)
            {
                // PoC plague_necromancer_ai.py: +25% HP, add plague_carrier to tags
                raisedFighter.Hp = (int)Math.Round(raisedFighter.Hp * 1.25);
            }
            var raisedAi = corpse.Get<AiComponent>();
            if (raisedAi != null && !raisedAi.Tags.Contains("plague_carrier"))
                raisedAi.Tags.Add("plague_carrier");
        }

        necComp.CooldownRemaining = necComp.RaiseCooldown;

        events.Add(new RaiseDeadEvent
        {
            ActorId = necromancer.Id,
            RaisedEntityId = corpse.Id,
            CorpseId = corpseId,
            AssignedFaction = raisedFaction,
        });
    }

    private static void ResolveMonsterAttack(GameState state, Entity monster, Entity target, List<TurnEvent> events, bool isBonusAttack,
        MonsterFactory? monsterFactory = null)
    {
        // DisarmedEffect: weapon attack cancelled — emit failure event, do not resolve.
        // Does not prevent unarmed attacks (if no weapon equipped).
        var monEquip = monster.Get<Equipment>();
        bool monHasWeapon = monEquip?.MainHand != null;
        if (monster.Has<DisarmedEffect>() && monHasWeapon)
        {
            events.Add(new AttackEvent
            {
                ActorId = monster.Id,
                TargetId = target.Id,
                Hit = false,
                Damage = 0,
                IsCritical = false,
                IsFumble = false,
                TargetKilled = false,
                IsBonusAttack = isBonusAttack,
                FailReason = "disarmed",
                ActorName = monster.Name,
                TargetName = target.Name,
            });
            return;
        }

        // Command the Dead: undead allies near a lich get +1 to-hit
        int commandBonus = GetCommandTheDeadBonus(monster, state);
        var result = CombatResolver.ResolveAttack(monster, target, state.Rng, extraToHitBonus: commandBonus);

        events.Add(new AttackEvent
        {
            ActorId = monster.Id,
            TargetId = target.Id,
            Hit = result.Hit,
            Damage = result.Damage,
            IsCritical = result.IsCritical,
            IsFumble = result.IsFumble,
            TargetKilled = result.TargetKilled,
            IsBonusAttack = isBonusAttack,
            ActorName = monster.Name,
            TargetName = target.Name,
        });

        if (result.Hit)
        {
            // Wake sleeping target on attack damage (NOT DOT — PoC-verified).
            StatusEffectProcessor.OnDamageTaken(target, events);

            // Home body threatened: alert Hollowmark once if the catatonic home body is hit.
            if (target.Id == state.Player.Id)
                PossessionSystem.OnHomeBodyHit(state, events);

            // Engulf: slimes apply EngulfedEffect to the player on any successful hit.
            // Only applies to the player (not monster-vs-monster); deterministic (no RNG).
            if (result.Damage > 0 && monster.Has<EngulfsOnHitTag>() && target.Id == state.Player.Id)
            {
                var engulfed = StatusEffectProcessor.ApplyEffect<EngulfedEffect>(target, 3);
                if (engulfed != null)
                {
                    events.Add(new StatusAppliedEvent
                    {
                        ActorId    = monster.Id,
                        TargetId   = target.Id,
                        EffectName = "engulfed",
                        Duration   = engulfed.RemainingTurns,
                    });
                }
            }

            // Attack damage interrupt hooks — rally end, chant interrupt.
            // DOT damage does NOT call this path (PoC-verified).
            if (result.Damage > 0)
                OnAttackDamageTaken(state, monster, target, events);
        }

        if (result.TargetKilled)
        {
            // Possession death router (§8.2): host killed during active player possession.
            // Bypasses RecordKilled / XP / faction-rep triggers — holding a body ≠ killing it.
            if (target.Get<Combat.StatusEffects.PossessionEffect>() is { Source: Combat.StatusEffects.PossessionSource.PlayerInitiated } possEff
                && possEff.PossessorEntityId == state.Player.Id)
            {
                DropMonsterLoot(state, target, events); // gear hits the floor for recovery
                PossessionSystem.OnPossessionInducedHostDeath(target, state, events, "host_died");
                return;
            }

            events.Add(new DeathEvent { ActorId = target.Id, KillerId = monster.Id, ActorName = target.Name, KillerName = monster.Name });

            if (target.Id == state.Player.Id)
            {
                // Record killer for past-Sasha snapshot at game-over.
                state.PlayerDeathKillerSpecies = monster.Get<SpeciesTag>()?.TypeId;
                state.PlayerDeathCause = "monster";
            }
            else
            {
                // Monster-vs-monster kill: drop loot, create corpse, check death siphon.
                DropMonsterLoot(state, target, events);
                TransformToCorpse(state, target, events, monsterFactory);
                ResolveDeathSiphon(state, target, events);
                // Shaman death cleanup for monster-vs-monster kills.
                ResolveChannelCleanupOnDeath(state, target, events);
            }
        }
        else if (result.Hit)
        {
            // Corrosion check — acidic monsters (slimes) degrade metal weapons on hit
            var corrosion = monster.Get<CorrosionComponent>();
            if (corrosion != null)
                ResolveCorrosion(state, monster, corrosion.Chance, state.Rng, events);

            // On-hit status effect (cave_spider=poison, web_spider=slowed, fire_beetle=burning)
            var onHit = monster.Get<OnHitEffectComponent>();
            if (onHit != null)
                ResolveOnHitEffect(target, onHit, events);

            // Life drain: wraith heals for ceil(DrainPct * damage) on each successful hit
            var drain = monster.Get<LifeDrainComponent>();
            if (drain != null && result.Damage > 0)
            {
                int drainAmount = (int)Math.Ceiling(drain.DrainPct * result.Damage);
                int healed = monster.Require<Fighter>().Heal(drainAmount);
                if (healed > 0)
                    events.Add(new LifeDrainEvent { ActorId = monster.Id, TargetId = target.Id, Amount = healed });
            }

            // Effect transfer: wraith absorbs poison/bleed from the target onto itself on each hit.
            // Clone semantics — original stays on target, both share the effect afterward.
            if (monster.Has<TransfersEffectsOnHitComponent>())
                ResolveEffectTransfer(monster, target, events);
        }

        // Ring of Teleportation: 20% on-hit, player teleports to a random open tile.
        // Cancels the bonus attack chain — returning here means no recursion.
        // Two teleportation rings each get an independent roll (36% effective chance).
        if (result.Hit && !result.TargetKilled && target.Id == state.Player.Id)
        {
            int teleportCount = CountRingEffect(state.Player.Get<Equipment>(), RingEffectKind.Teleportation);
            for (int t = 0; t < teleportCount; t++)
            {
                if (state.Rng.Next(0, 100) < 20)
                {
                    int fromX = target.X, fromY = target.Y;
                    var dest = FindRandomOpenTile(state, target);
                    if (dest.HasValue)
                    {
                        state.Map.UnregisterEntity(target);
                        target.X = dest.Value.x;
                        target.Y = dest.Value.y;
                        state.Map.RegisterEntity(target);
                        events.Add(new TeleportEvent
                        {
                            ActorId  = target.Id,
                            EntityId = target.Id,
                            FromX    = fromX,
                            FromY    = fromY,
                            ToX      = dest.Value.x,
                            ToY      = dest.Value.y,
                            Misfire  = false,
                            Reason   = "ring_of_teleportation",
                        });
                        return; // teleport cancels this monster's bonus attack chain
                    }
                    break; // no open tile found — skip remaining rolls
                }
            }
        }

        // Monster bonus attacks — recurse if triggered and target still alive
        if (result.BonusAttackTriggered && target.Require<Fighter>().IsAlive)
        {
            ResolveMonsterAttack(state, monster, target, events, isBonusAttack: true,
                monsterFactory: monsterFactory);
        }
    }

    /// <summary>
    /// Attempt to corrode the player's equipped metal main-hand weapon.
    /// Rolls against chance; if triggered, reduces DamageMax by 1, floored at BaseDamageMax/2.
    /// Emits CorrosionEvent for the presentation layer to display a toast.
    /// </summary>
    private static void ResolveCorrosion(GameState state, Entity attacker, double chance,
        SeededRandom rng, List<TurnEvent> events)
    {
        if (rng.NextDouble() >= chance) return;

        var equipment = state.Player.Get<Equipment>();
        var mainHandItem = equipment?.MainHand;
        if (mainHandItem == null) return;

        var equippable = mainHandItem.Get<Equippable>();
        if (equippable == null) return;

        // Only metal weapons corrode
        if (!string.Equals(equippable.Material, "metal", StringComparison.OrdinalIgnoreCase)) return;

        // Floor: weapon cannot be degraded below 50% of its base damage max
        int floor = Math.Max(1, equippable.BaseDamageMax / 2);
        if (equippable.DamageMax <= floor) return;

        equippable.DamageMax--;

        events.Add(new CorrosionEvent
        {
            ActorId = attacker.Id,
            WeaponId = mainHandItem.Id,
            WeaponName = mainHandItem.Name,
            NewDamageMax = equippable.DamageMax,
            BaseDamageMax = equippable.BaseDamageMax,
            MonsterName = attacker.Name,
        });
    }

    /// <summary>
    /// Apply the monster's on-hit status effect to the target.
    /// Uses the no-stack/refresh rule from StatusEffectProcessor.ApplyEffect.
    /// Emits StatusAppliedEvent for the presentation layer.
    /// </summary>
    private static void ResolveOnHitEffect(Entity target, OnHitEffectComponent onHit, List<TurnEvent> events)
    {
        switch (onHit.EffectType)
        {
            case "poison":
                StatusEffectProcessor.ApplyEffect<PoisonEffect>(target, onHit.Duration);
                break;
            case "slowed":
                StatusEffectProcessor.ApplyEffect<SlowedEffect>(target, onHit.Duration);
                break;
            case "burning":
                StatusEffectProcessor.ApplyEffect<BurningEffect>(target, onHit.Duration);
                break;
            case "plague":
                StatusEffectProcessor.ApplyEffect<PlagueEffect>(target, onHit.Duration);
                break;
            default:
                return; // unknown effect type — no-op, no event
        }

        events.Add(new StatusAppliedEvent
        {
            ActorId = target.Id,
            TargetId = target.Id,
            EffectName = onHit.EffectType,
            Duration = onHit.Duration,
        });
    }

    /// <summary>
    /// Effect transfer: clones active PoisonEffect and/or BleedEffect from target onto attacker.
    /// Clone semantics — both entities end up with the effect; original is NOT removed from target.
    /// Guard: skips if attacker already has the effect (prevents re-applying / extending via loop).
    /// Immunity check is handled inside ApplyEffect — so an immune attacker silently skips transfer.
    ///
    /// Bleed cloning preserves the source severity (wraith that drains a deep-wound player gets
    /// the same severity, not a flat sev-1).
    /// </summary>
    private static void ResolveEffectTransfer(Entity attacker, Entity target, List<TurnEvent> events)
    {
        // Poison transfer
        var targetPoison = target.Get<PoisonEffect>();
        if (targetPoison != null && !attacker.Has<PoisonEffect>())
        {
            var cloned = StatusEffectProcessor.ApplyEffect<PoisonEffect>(attacker, targetPoison.RemainingTurns);
            if (cloned != null)
            {
                cloned.DamagePerTurn = targetPoison.DamagePerTurn;
                events.Add(new StatusTransferredEvent
                {
                    ActorId  = attacker.Id,
                    SourceId = target.Id,
                    TargetId = attacker.Id,
                    EffectKind = "poison",
                });
            }
        }

        // Bleed transfer
        var targetBleed = target.Get<BleedEffect>();
        if (targetBleed != null && !attacker.Has<BleedEffect>())
        {
            var cloned = StatusEffectProcessor.ApplyEffect<BleedEffect>(attacker, targetBleed.RemainingTurns);
            if (cloned != null)
            {
                cloned.Severity = targetBleed.Severity;
                events.Add(new StatusTransferredEvent
                {
                    ActorId  = attacker.Id,
                    SourceId = target.Id,
                    TargetId = attacker.Id,
                    EffectKind = "bleed",
                });
            }
        }
    }

    /// <summary>
    /// Command the Dead: undead allies within a lich's command_the_dead_radius get +1 to-hit.
    /// Lich does not buff itself. Returns 0 if no lich in range or attacker is not undead.
    /// </summary>
    private static int GetCommandTheDeadBonus(Entity attacker, GameState state)
    {
        // Skip if attacker is a lich (does not buff self)
        if (attacker.Get<LichAiComponent>() != null) return 0;

        var ai = attacker.Get<AiComponent>();
        if (ai == null || !ai.Tags.Contains("undead")) return 0;

        foreach (var monster in state.AliveMonsters)
        {
            var lichComp = monster.Get<LichAiComponent>();
            if (lichComp == null) continue;

            double dx = attacker.X - monster.X;
            double dy = attacker.Y - monster.Y;
            if (Math.Sqrt(dx * dx + dy * dy) <= lichComp.CommandTheDeadRadius)
                return 1; // +1 to-hit
        }
        return 0;
    }

    /// <summary>
    /// Death Siphon: when an undead monster dies within a lich's death_siphon_radius,
    /// the lich heals 2 HP. PoC lich_ai.py: death_siphon.
    /// </summary>
    private static void ResolveDeathSiphon(GameState state, Entity deadMonster, List<TurnEvent> events)
    {
        // Check if dead monster has "undead" tag
        var ai = deadMonster.Get<AiComponent>();
        if (ai == null || !ai.Tags.Contains("undead")) return;

        foreach (var monster in state.AliveMonsters)
        {
            var lichComp = monster.Get<LichAiComponent>();
            if (lichComp == null || monster.Id == deadMonster.Id) continue;

            double dx = monster.X - deadMonster.X;
            double dy = monster.Y - deadMonster.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist <= lichComp.DeathSiphonRadius)
            {
                int healed = monster.Require<Fighter>().Heal(2);
                if (healed > 0)
                    events.Add(new DeathSiphonEvent { ActorId = monster.Id, DeadMonsterId = deadMonster.Id, Amount = healed });
            }
        }
    }

    /// <summary>
    /// Called after any successful ATTACK hit that deals damage > 0.
    /// Handles cross-cutting interrupt rules that depend on the attacker/defender pair:
    ///   - Rally ends when the chieftain (defender) takes attack damage.
    ///   - Chant of Dissonance is interrupted when the shaman (defender) takes attack damage.
    ///
    /// DOT damage does NOT call this (PoC-verified: only attack damage interrupts these).
    /// Called from both ResolvePlayerAttack and ResolveMonsterAttack.
    /// </summary>
    private static void OnAttackDamageTaken(GameState state, Entity attacker, Entity defender,
        List<TurnEvent> events)
    {
        // Rally ends when the chieftain takes any attack damage > 0.
        // Remove RallyEffect from the chieftain itself and every monster whose
        // RallyEffect.ChieftainId matches this chieftain.
        //
        // Use state.Monsters directly (not state.AliveMonsters) to avoid caching
        // a stale AliveMonsters list during the player turn phase. The filter
        // Get<Fighter>()?.IsAlive == true ensures we only process living monsters.
        if (defender.Has<OrcChieftainComponent>())
        {
            foreach (var entity in state.Monsters.Where(m => m.Get<Fighter>()?.IsAlive == true))
            {
                var rally = entity.Get<RallyEffect>();
                if (rally != null && rally.ChieftainId == defender.Id)
                {
                    entity.Remove<RallyEffect>();
                    events.Add(new StatusExpiredEvent
                    {
                        ActorId    = entity.Id,
                        EntityId   = entity.Id,
                        EffectName = "rallied",
                        Reason     = "rally_broken",
                    });
                }
            }
            // Also remove from the chieftain itself.
            var chieftainRally = defender.Get<RallyEffect>();
            if (chieftainRally != null)
            {
                defender.Remove<RallyEffect>();
                events.Add(new StatusExpiredEvent
                {
                    ActorId    = defender.Id,
                    EntityId   = defender.Id,
                    EffectName = "rallied",
                    Reason     = "rally_broken",
                });
            }
        }

        // Chant interrupted when the shaman (defender) takes any attack damage > 0.
        var shamanComp = defender.Get<OrcShamanComponent>();
        if (shamanComp != null && shamanComp.IsChanneling)
        {
            shamanComp.IsChanneling = false;
            shamanComp.ChantTurnsRemaining = 0;
            shamanComp.ChantCooldownRemaining = shamanComp.ChantCooldownTurns;

            // Remove DissonantChantEffect from the player if it was started by this shaman.
            var chantEffect = state.Player.Get<DissonantChantEffect>();
            if (chantEffect != null && chantEffect.ChantingShamanId == defender.Id)
            {
                StatusEffectProcessor.RemoveEffect<DissonantChantEffect>(state.Player);
                events.Add(new StatusExpiredEvent
                {
                    ActorId    = state.Player.Id,
                    EntityId   = state.Player.Id,
                    EffectName = "dissonant_chant",
                    Reason     = "chant_interrupted",
                });
            }
        }
    }

    /// <summary>
    /// Handle a monster picking up a floor item.
    ///
    /// Priority: auto-equip if the slot is empty and the item fits (weapon→MainHand, armor→Chest).
    /// If neither condition is met, add to the monster's inventory.
    /// Items that can't be stored (no equipment slot, inventory full) are left on the floor.
    /// </summary>
    private static void ResolveMonsterPickUp(GameState state, Entity monster, Entity item, List<TurnEvent> events)
    {
        // Verify the item is still on the floor (it may have been picked up in the same turn
        // by another monster in a future iteration — shouldn't happen with snapshot iteration
        // but guard defensively).
        if (!state.FloorItems.Contains(item)) return;

        var equippable = item.Get<Equippable>();
        var equipment = monster.Get<Equipment>();
        var inventory = monster.Get<Inventory>();

        bool picked = false;

        // Auto-equip weapons and armor into empty slots — this is what makes item-seekers
        // meaningful in combat: they arm themselves, not just hoard.
        if (equippable != null && equipment != null)
        {
            if (equippable.IsWeapon && equipment.MainHand == null)
            {
                equipment.SetSlot(EquipmentSlot.MainHand, item);
                picked = true;
            }
            else if (!equippable.IsWeapon && equippable.Slot == EquipmentSlot.Chest && equipment.Chest == null)
            {
                equipment.SetSlot(EquipmentSlot.Chest, item);
                picked = true;
            }
        }

        // Fall back to inventory for consumables, extras, or when equipment slot is occupied.
        if (!picked && inventory != null)
        {
            picked = inventory.Add(item);
        }

        if (!picked) return; // inventory full and no slot available — leave on floor

        state.FloorItems.Remove(item);
        state.Map.UnregisterEntity(item);

        events.Add(new PickUpEvent
        {
            ActorId = monster.Id,
            ItemId = item.Id,
            ItemName = item.Name,
        });
    }

    /// <summary>
    /// Resolve a monster's attempt to use an item from its inventory.
    ///
    /// Failure rates reflect monster intelligence — orcs fumble potions often,
    /// scrolls are nearly impossible without arcane training.
    /// Three failure modes keep even fumbles interesting:
    ///   fizzle          — wastes the turn and item, nothing happens
    ///   wrong_target    — healing hits the player instead (dangerous backfire)
    ///   equipment_damage — monster's weapon loses 1 DamageMax (weakens it mid-fight)
    ///
    /// Item is consumed regardless of success or failure — mirrors PoC item use logic.
    /// </summary>
    private static void ResolveMonsterItemUse(GameState state, Entity monster, Entity item, List<TurnEvent> events)
    {
        // Scrolls require a spell system — keep failure rate high so the framework
        // is wired but scrolls aren't meaningful until the spell system lands.
        const int ScrollFailurePercent = 75;
        const int PotionFailurePercent = 20;

        var consumable = item.Get<Consumable>();
        if (consumable == null) return;

        int failurePercent = consumable.IsHealing ? PotionFailurePercent : ScrollFailurePercent;
        bool success = state.Rng.Next(0, 100) >= failurePercent;

        string failureMode = "";
        int effectAmount   = 0;

        if (success)
        {
            if (consumable.IsHealing)
                effectAmount = monster.Require<Fighter>().Heal(consumable.HealAmount);
        }
        else
        {
            // Three equally probable failure modes — roll once into [0, 3).
            switch (state.Rng.Next(0, 3))
            {
                case 0:
                    // Fizzle — nothing happens, item still consumed.
                    failureMode = "fizzle";
                    break;

                case 1:
                    // Wrong target — healing applies to the player.
                    // Intentionally punishing: the monster heals its enemy.
                    failureMode = "wrong_target";
                    if (consumable.IsHealing)
                        effectAmount = state.PlayerFighter.Heal(consumable.HealAmount);
                    break;

                default:
                    // Equipment damage — main weapon loses 1 DamageMax.
                    // Guard: DamageMax must exceed DamageMin to preserve a valid damage range.
                    failureMode = "equipment_damage";
                    var equipment = monster.Get<Equipment>();
                    if (equipment?.MainHand != null)
                    {
                        var equippable = equipment.MainHand.Get<Equippable>();
                        if (equippable != null && equippable.DamageMax > equippable.DamageMin)
                        {
                            equippable.DamageMax--;
                            effectAmount = 1;
                        }
                    }
                    break;
            }
        }

        // Consume regardless of outcome — matches player potion consumption.
        consumable.StackSize--;
        if (consumable.StackSize <= 0)
            monster.Get<Inventory>()?.Remove(item);

        events.Add(new ItemUseEvent
        {
            ActorId      = monster.Id,
            ItemName     = item.Name,
            Success      = success,
            FailureMode  = failureMode,
            EffectAmount = effectAmount,
        });
    }

    /// <summary>
    /// Drop all equipped and carried items from a monster that just died.
    /// Items are placed at the monster's current position — floor items may stack,
    /// which is intentional (the PoC allows this; player picks up all on the tile).
    /// The monster entity itself is kept in state.Monsters so the death animation
    /// can still reference it — we only release its items.
    /// </summary>
    private static void DropMonsterLoot(GameState state, Entity monster, List<TurnEvent> events)
    {
        var equipment = monster.Get<Equipment>();
        var inventory = monster.Get<Inventory>();

        if (equipment == null && inventory == null) return;

        var itemsToDrop = new List<Entity>();

        if (equipment != null)
        {
            // Collect and clear all equipped slots
            foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
            {
                var item = equipment.GetSlot(slot);
                if (item != null)
                {
                    itemsToDrop.Add(item);
                    equipment.SetSlot(slot, null);
                }
            }
        }

        if (inventory != null)
        {
            foreach (var item in inventory.Items.ToList())
            {
                itemsToDrop.Add(item);
                inventory.Remove(item);
            }
        }

        foreach (var item in itemsToDrop)
        {
            item.X = monster.X;
            item.Y = monster.Y;
            state.FloorItems.Add(item);
            state.Map.RegisterEntity(item);

            events.Add(new DropEvent
            {
                ActorId = monster.Id,
                ItemId = item.Id,
                ItemName = item.Name,
            });
        }
    }

    /// <summary>
    /// Transform a freshly-killed monster entity into a corpse in-place.
    /// Called after DropMonsterLoot at every monster DeathEvent site.
    ///
    /// If the monster definition has LeavesCorpse=false (e.g., slimes), the call is a no-op.
    /// When no factory is available the check is skipped and the corpse IS created — this is
    /// the safe default for test environments (no slimes in unit tests without a factory).
    ///
    /// Components stripped: Fighter, AiComponent, AlertedState, SplitTracker, CorrosionComponent,
    /// SpeedBonusTracker, DamageModifiers, and all IStatusEffect implementors.
    /// SpeciesTag is preserved for knowledge-system and lineage tracking.
    /// </summary>
    private static void TransformToCorpse(GameState state, Entity entity, List<TurnEvent> events,
        MonsterFactory? monsterFactory)
    {
        // Guard: already a corpse (defensive — shouldn't happen in normal flow)
        if (entity.Has<CorpseComponent>()) return;

        // Corpses require a SpeciesTag — anonymous entities (hand-crafted test fixtures
        // without a YAML origin) are not transformed.
        var speciesTag = entity.Get<SpeciesTag>();
        if (speciesTag == null) return;

        // Check leaves_corpse flag via factory definition lookup.
        if (monsterFactory != null)
        {
            var def = monsterFactory.GetDefinition(speciesTag.TypeId);
            if (def != null && !def.LeavesCorpse) return;
        }

        string originalMonsterId = speciesTag?.TypeId ?? "";
        string originalName = entity.Name;

        // Snapshot Fighter stats before stripping — used by RaiseDeadResolver
        var fighter = entity.Get<Fighter>();
        int snapHp         = fighter?.BaseMaxHp ?? 0;
        int snapDmgMin     = fighter?.DamageMin  ?? 0;
        int snapDmgMax     = fighter?.DamageMax  ?? 0;
        int snapStr        = fighter?.Strength   ?? 10;
        int snapDex        = fighter?.Dexterity  ?? 10;
        int snapCon        = fighter?.Constitution ?? 10;
        int snapDef        = fighter?.BaseDefense ?? 0;
        int snapAccuracy   = fighter?.Accuracy   ?? Combat.HitModel.DefaultAccuracy;
        int snapEvasion    = fighter?.Evasion    ?? Combat.HitModel.DefaultEvasion;

        // Strip combat/AI components
        entity.Remove<Fighter>();
        entity.Remove<AiComponent>();
        entity.Remove<AlertedState>();
        entity.Remove<SplitTracker>();
        entity.Remove<CorrosionComponent>();

        // Strip all status effects (any IStatusEffect implementation)
        foreach (var effect in entity.GetAllComponents().OfType<IStatusEffect>().ToList())
            entity.RemoveByType(effect.GetType());

        // SpeciesTag intentionally NOT stripped — preserved for knowledge system and lineage

        // Add corpse component
        string corpseId = $"corpse_{entity.X}_{entity.Y}_{state.TurnCount}";
        bool wasRaised = entity.Has<RaisedFromCorpseTag>();

        var corpse = new CorpseComponent
        {
            OriginalMonsterId = originalMonsterId,
            OriginalName = originalName,
            DeathTurn = state.TurnCount,
            State = wasRaised ? CorpseState.Spent : CorpseState.Fresh,
            CorpseId = corpseId,
            // Snapshot for RaiseDeadResolver
            BaseHp           = snapHp,
            BaseDamageMin    = snapDmgMin,
            BaseDamageMax    = snapDmgMax,
            BaseStrength     = snapStr,
            BaseDexterity    = snapDex,
            BaseConstitution = snapCon,
            BaseDefense      = snapDef,
            BaseAccuracy     = snapAccuracy,
            BaseEvasion      = snapEvasion,
        };
        entity.Add(corpse);
        entity.BlocksMovement = false;

        // Dual membership: entity stays in state.Monsters AND is added to state.Corpses
        state.Corpses.Add(entity);

        events.Add(new CorpseCreatedEvent
        {
            ActorId = entity.Id,
            CorpseEntityId = entity.Id,
            CorpseId = corpseId,
            OriginalMonsterId = originalMonsterId,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Ring system
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply or reverse a ring's stat effect on the player entity.
    /// equip=true: apply the bonus. equip=false: remove the bonus.
    ///
    /// Phase 2 ring kinds (Resistance, Clarity, Invisibility, Searching, Wizardry, Luck)
    /// do nothing here — their parent systems are not yet implemented. They are safely inert.
    /// </summary>
    private static void ApplyRingEffect(Entity player, RingEffectComponent ring, bool equip, List<TurnEvent> events)
    {
        int delta = equip ? ring.Strength : -ring.Strength;
        var fighter = player.Get<Fighter>();

        switch (ring.Kind)
        {
            case RingEffectKind.Protection:
                if (fighter != null) fighter.BaseDefense += delta;
                break;

            case RingEffectKind.Strength:
                if (fighter != null) fighter.Strength += delta;
                break;

            case RingEffectKind.Dexterity:
                if (fighter != null) fighter.Dexterity += delta;
                break;

            case RingEffectKind.Constitution:
                if (fighter != null)
                {
                    fighter.Constitution += delta;
                    // +20 HP bonus tracked separately so it cleanly reverses on unequip.
                    // The +2 CON from the delta above gives +1 MaxHp via ConstitutionMod.
                    // The +20 RingMaxHpBonus is the additional explicit HP the ring grants.
                    if (equip)
                    {
                        fighter.RingMaxHpBonus += 20;
                        fighter.Hp += 20; // immediate current HP boost on equip
                    }
                    else
                    {
                        fighter.RingMaxHpBonus -= 20;
                        // Clamp current HP to new (lower) MaxHp
                        fighter.Hp = Math.Min(fighter.Hp, fighter.MaxHp);
                    }
                }
                break;

            case RingEffectKind.Might:
                if (fighter != null)
                {
                    // Strength is effect_strength=4 → +1 DamageMin, +4 DamageMax (PoC: might ring adds both)
                    // Convention: DamageMin gets +1, DamageMax gets +strength
                    int minDelta = equip ? 1 : -1;
                    fighter.DamageMin += minDelta;
                    fighter.DamageMax += delta;
                }
                break;

            case RingEffectKind.Regeneration:
                // Passive tick — no stat mutation on equip/unequip.
                // The tick fires in ProcessTurn via CountRingEffect check.
                break;

            case RingEffectKind.Speed:
                // Adjust player's SpeedBonusTracker.RingRatio.
                // Ring of Speed = 0.10, Ring of Hummingbird = 0.25 (stored in SpeedRatio).
                {
                    var tracker = player.Get<SpeedBonusTracker>();
                    if (tracker == null && equip)
                    {
                        tracker = new SpeedBonusTracker();
                        player.Add(tracker);
                    }
                    if (tracker != null)
                        tracker.RingRatio += equip ? ring.SpeedRatio : -ring.SpeedRatio;
                }
                break;

            case RingEffectKind.FreeAction:
                if (equip)
                    player.Add(new FreeActionTag());
                else
                    player.Remove<FreeActionTag>();
                break;

            case RingEffectKind.Teleportation:
                // On-hit passive — no stat mutation. Checked in ResolveMonsterAttack.
                break;

            // Phase 2 stubs: Resistance, Clarity, Invisibility, Searching, Wizardry, Luck
            // No-op until parent systems land.
            default:
                break;
        }
    }

    /// <summary>
    /// Count how many of a given ring effect kind are equipped across both ring slots.
    /// Returns 0 if equipment is null or no ring slots are occupied with that effect.
    /// </summary>
    private static int CountRingEffect(Equipment? equipment, RingEffectKind kind)
    {
        if (equipment == null) return 0;
        int count = 0;
        if (equipment.LeftRing?.Get<RingEffectComponent>()?.Kind == kind) count++;
        if (equipment.RightRing?.Get<RingEffectComponent>()?.Kind == kind) count++;
        return count;
    }

    /// <summary>
    /// Returns true if the player has at least one ring of the given kind equipped.
    /// </summary>
    private static bool HasRingEffect(Equipment? equipment, RingEffectKind kind)
        => CountRingEffect(equipment, kind) > 0;

    /// <summary>
    /// Find a random walkable, unoccupied tile for teleportation.
    /// Returns null if no open tile is found within a reasonable scan.
    /// Excludes the entity's current position.
    /// </summary>
    private static (int x, int y)? FindRandomOpenTile(GameState state, Entity entity)
    {
        var map = state.Map;
        // Collect all walkable, unblocked tiles excluding the entity's current position.
        // In scenario arenas (20x20 = 400 tiles) this is cheap. In dungeons (120x80 = 9600)
        // it's still fast. We snapshot once rather than retrying random positions.
        var candidates = new List<(int x, int y)>();
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                if (x == entity.X && y == entity.Y) continue;
                if (map.CanMoveTo(x, y))
                    candidates.Add((x, y));
            }
        }
        if (candidates.Count == 0) return null;
        return candidates[state.Rng.Next(candidates.Count)];
    }

    /// <summary>
    /// Re-apply all equipped ring stat effects to the player after a floor transition.
    ///
    /// PlayerCarryForward.Apply() creates a new Fighter with base stats only —
    /// ring bonuses (BaseDefense, Strength, etc.) are not in the Fighter constructor.
    /// This method must be called after Apply() to restore ring effects.
    ///
    /// Called from DungeonFloorBuilder (or wherever floor transitions happen) after
    /// PlayerCarryForward.Apply() returns the new player entity.
    ///
    /// Note: For carry-forward we create a new Fighter from the OLD fighter's base stats,
    /// but the OLD fighter already had ring bonuses applied. So if we just copy the stat
    /// values, the bonus is already baked in — we would double-apply if we called this.
    ///
    /// IMPORTANT: PlayerCarryForward.Apply() copies Strength, Dexterity, Constitution etc.
    /// from the live fighter (which already has ring bonuses). This means ring effects
    /// are already embedded in the carried-forward stats. This method should only be called
    /// when the fighter stats were reset to BASE values (not carried forward).
    ///
    /// For the current carry-forward strategy (copy live stats), call sites should NOT
    /// call this — the ring bonuses survive in the copied stats. However, RingMaxHpBonus,
    /// RingRatio, and FreeActionTag MUST be re-applied because they are not carried forward.
    /// </summary>
    public static void ReapplyRingEffects(Entity player)
    {
        var equipment = player.Get<Equipment>();
        if (equipment == null) return;

        var fighter = player.Get<Fighter>();

        // Re-apply ring effects that are NOT captured in Fighter stat fields:
        // - RingMaxHpBonus: new Fighter starts with 0, must be restored
        // - SpeedBonusTracker.RingRatio: tracker is not carried forward
        // - FreeActionTag: marker component, not carried forward

        foreach (var ringSlot in new[] { equipment.LeftRing, equipment.RightRing })
        {
            var ring = ringSlot?.Get<RingEffectComponent>();
            if (ring == null) continue;

            switch (ring.Kind)
            {
                case RingEffectKind.Constitution:
                    if (fighter != null)
                    {
                        // Only restore RingMaxHpBonus — Constitution stat was already carried forward
                        fighter.RingMaxHpBonus += 20;
                    }
                    break;

                case RingEffectKind.Speed:
                    {
                        var tracker = player.Get<SpeedBonusTracker>();
                        if (tracker == null)
                        {
                            tracker = new SpeedBonusTracker();
                            player.Add(tracker);
                        }
                        tracker.RingRatio += ring.SpeedRatio;
                    }
                    break;

                case RingEffectKind.FreeAction:
                    if (!player.Has<FreeActionTag>())
                        player.Add(new FreeActionTag());
                    break;
            }
        }
    }

    /// <summary>
    /// Check whether moving toward (targetX, targetY) would bump into a locked door.
    /// Checks the direct target tile and, for multi-step paths, the first greedy step.
    ///
    /// Two outcomes when a locked door is found:
    ///   - Player has matching key: consume the key, open the door (LockedDoor → DoorOpen),
    ///     emit DoorUnlockedEvent + KeyConsumedEvent. Returns true, isFreeAction=false (costs a turn).
    ///   - No matching key: emit LockedDoorBumpedEvent. Returns true, isFreeAction=true (no turn cost).
    ///
    /// Returns false when no locked door is on the path — caller proceeds normally.
    /// LockedDoor tiles are NEVER passable to the pathfinder (no canPassDoors bypass).
    /// </summary>
    private static bool TryHandleLockedDoorBump(GameState state, Entity player,
        int targetX, int targetY, List<TurnEvent> events, out bool isFreeAction)
    {
        isFreeAction = false;

        // Determine the actual tile being bumped — the direct target, or the first step
        // toward the target (mirrors TryOpenDoorOnPath's greedy logic).
        int bumpX = targetX, bumpY = targetY;

        var directKind = state.Map.GetTileKind(targetX, targetY);
        if (directKind != TileKind.LockedDoor)
        {
            // Check first greedy step toward target (same priority as MoveToward: diagonal first).
            int dx = Math.Sign(targetX - player.X);
            int dy = Math.Sign(targetY - player.Y);

            if (dx != 0 && dy != 0 && state.Map.GetTileKind(player.X + dx, player.Y + dy) == TileKind.LockedDoor)
            { bumpX = player.X + dx; bumpY = player.Y + dy; }
            else if (dx != 0 && state.Map.GetTileKind(player.X + dx, player.Y) == TileKind.LockedDoor)
            { bumpX = player.X + dx; bumpY = player.Y; }
            else if (dy != 0 && state.Map.GetTileKind(player.X, player.Y + dy) == TileKind.LockedDoor)
            { bumpX = player.X; bumpY = player.Y + dy; }
            else
                return false; // no locked door on path
        }

        // Locked door found at (bumpX, bumpY).
        if (!state.LockedDoors.TryGetValue((bumpX, bumpY), out int lockColorId))
        {
            // Door exists in map but not in registry — structural error. Treat as no-key (free action).
            events.Add(new LockedDoorBumpedEvent { ActorId = player.Id, X = bumpX, Y = bumpY, LockColorId = 0 });
            isFreeAction = true;
            return true;
        }

        // Find matching key in player inventory.
        var inventory = player.GetOrAdd<Inventory>();
        var matchingKey = inventory.FindFirst(item =>
        {
            var key = item.Get<KeyItemComponent>();
            return key != null && key.LockColorId == lockColorId;
        });

        if (matchingKey == null)
        {
            // No key — feedback event, no turn consumed (free action, same as bumping a wall).
            events.Add(new LockedDoorBumpedEvent { ActorId = player.Id, X = bumpX, Y = bumpY, LockColorId = lockColorId });
            isFreeAction = true;
            return true;
        }

        // Key found — consume it, open the door, emit events.
        int keyId = matchingKey.Id;
        inventory.Remove(matchingKey);
        state.LockedDoors.Remove((bumpX, bumpY));

        // Change tile from LockedDoor to DoorOpen (walkable, transparent).
        state.Map.SetTile(bumpX, bumpY, TileKind.DoorOpen);

        events.Add(new KeyConsumedEvent
        {
            ActorId     = player.Id,
            KeyId       = keyId,
            LockColorId = lockColorId,
        });
        events.Add(new DoorUnlockedEvent
        {
            ActorId     = player.Id,
            X           = bumpX,
            Y           = bumpY,
            KeyId       = keyId,
            LockColorId = lockColorId,
        });

        isFreeAction = false; // unlocking a door costs a turn
        return true;
    }

    /// <summary>
    /// If the target tile is a closed door and the entity can open doors, open it and emit
    /// DoorOpenedEvent, consuming the turn. Returns true if a door was opened (caller should
    /// not also apply movement this turn).
    /// </summary>
    private static bool TryOpenDoor(GameMap map, Entity entity, int x, int y, List<TurnEvent> events)
    {
        if (!map.InBounds(x, y)) return false;
        if (map.GetTileKind(x, y) != TileKind.Door) return false;

        // Player always can open doors; monsters check CanOpenDoors flag.
        bool canOpen = entity.Get<Fighter>()?.CanOpenDoors ?? false;
        if (!canOpen) return false;

        map.OpenDoor(x, y);
        events.Add(new DoorOpenedEvent { X = x, Y = y, OpenedById = entity.Id });
        return true;
    }

    /// <summary>
    /// Like TryOpenDoor but for MoveToward (entity target): checks the first greedy step
    /// toward (targetX, targetY) rather than the target tile itself. Handles the case where
    /// the path to the enemy passes through a door before reaching the enemy's position.
    /// </summary>
    // ─────────────────────────────────────────────────────────────────────────
    // Secret door detection
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Flavor hints shown to the player when a secret door is passively discovered.
    /// One is chosen at random using the game's seeded RNG so discovery text is deterministic.
    /// </summary>
    private static readonly string[] SecretDoorHints =
    [
        "You notice a faint draft from the wall.",
        "The wall sounds hollow when you tap it.",
        "There are scratch marks on the floor here.",
        "The stones here are slightly worn.",
        "A subtle misalignment in the stonework catches your eye.",
    ];

    /// <summary>
    /// Passive secret door detection. Called once per player turn, after the player acts.
    /// Checks all 8 Chebyshev-adjacent tiles for SecretDoor. Each adjacent SecretDoor has
    /// a 25% chance to be revealed as a normal Door this turn.
    ///
    /// When revealed: tile changes to TileKind.Door and SecretDoorFoundEvent is emitted.
    /// The event carries a random flavor hint from SecretDoorHints.
    /// Multiple adjacent secret doors can all reveal in the same turn (independent rolls).
    ///
    /// Skip-turn and free-action cases are handled by the caller — this method always checks.
    /// </summary>
    private static void CheckSecretDoorDetection(GameState state, List<TurnEvent> events)
    {
        int px = state.Player.X, py = state.Player.Y;

        // Chebyshev radius 1 — all 8 adjacent tiles (diagonals included)
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            int nx = px + dx, ny = py + dy;
            if (!state.Map.InBounds(nx, ny)) continue;
            if (state.Map.GetTileKind(nx, ny) != TileKind.SecretDoor) continue;

            // 25% base chance per adjacent secret door per turn
            if (state.Rng.NextDouble() < 0.25)
            {
                // Reveal: change tile to normal closed door before emitting the event
                // so the presentation layer sees the updated tile kind when it processes events.
                state.Map.SetTile(nx, ny, TileKind.Door);

                string hint = SecretDoorHints[state.Rng.Next(SecretDoorHints.Length)];
                events.Add(new SecretDoorFoundEvent
                {
                    ActorId = state.Player.Id,
                    X = nx,
                    Y = ny,
                    Hint = hint,
                });

                // Stop auto-explore on secret door discovery so the player can assess the
                // new passage. Same pattern as trap detection and chest discovery.
                var ae = state.Player.Get<AutoExploreState>();
                if (ae != null && ae.IsActive)
                    AutoExploreSystem.Stop(ae, "Found secret door");
            }
        }
    }

    /// <summary>
    /// When a monster dies, if it was channeling a Chant of Dissonance, remove the
    /// DissonantChantEffect from the player so they are not permanently slowed.
    /// Called from both ResolvePlayerAttack and ResolveMonsterAttack death paths.
    /// Risk R4 mitigation — easy to miss in edge cases, so it lives here in one place.
    /// </summary>
    /// <summary>
    /// Emit <see cref="OrcRepChangedEvent"/> if this kill crosses the orc-hostile threshold.
    /// Called at the kill site (before TransformToCorpse strips AiComponent) for unprovoked
    /// orc kills dealt directly by the player. Mirrors the unprovoked-kill conditions in
    /// UpdateKnowledge but reads faction BEFORE the component is stripped. Fires at most once
    /// per run (threshold can only be crossed once; existing-hostile rep is also guarded).
    /// </summary>
    private static void EmitOrcRepChangedIfThresholdCrossed(GameState state, Entity killed,
        List<TurnEvent> events)
    {
        // Only for unprovoked orc kills dealt directly by the player (same predicate as UpdateKnowledge).
        if (killed.Has<HasAttackedPlayerTag>()) return;
        string faction = killed.Get<AiComponent>()?.Faction ?? "";
        if (faction != FactionsData.OrcFactionId) return;

        // Count the kill (AddUnprovokedKill will be called by UpdateKnowledge; add it now to get
        // the post-kill count for the threshold check, but do NOT double-add — we use a peek approach).
        // Peek: what would the count be after UpdateKnowledge adds this kill?
        int currentCount = state.Player.Get<RunAggressionTally>()?.UnprovokedKillsFor(FactionsData.OrcFactionId) ?? 0;
        int postKillCount = currentCount + 1; // UpdateKnowledge will add exactly one
        string currentRep = state.PersistentState?.Factions.GetState(FactionsData.OrcFactionId) ?? "neutral";

        if (postKillCount == FactionsData.HostileThreshold && currentRep != "hostile")
            events.Add(new OrcRepChangedEvent
            {
                ActorId      = state.Player.Id,
                FactionId    = FactionsData.OrcFactionId,
                ToState      = "hostile",
                KillsThisRun = postKillCount,
            });
    }

    /// <summary>
    /// When a monster dies, if it was channeling a Chant of Dissonance, remove the
    /// DissonantChantEffect from the player so they are not permanently slowed.
    /// Called from both ResolvePlayerAttack and ResolveMonsterAttack death paths.
    /// Risk R4 mitigation — easy to miss in edge cases, so it lives here in one place.
    /// </summary>
    private static void ResolveChannelCleanupOnDeath(GameState state, Entity deadMonster,
        List<TurnEvent> events)
    {
        var shaman = deadMonster.Get<OrcShamanComponent>();
        if (shaman == null || !shaman.IsChanneling) return;

        // The shaman was channeling when it died — clean up the effect on the player.
        shaman.IsChanneling = false;
        shaman.ChantTurnsRemaining = 0;

        var chantEffect = state.Player.Get<DissonantChantEffect>();
        if (chantEffect != null && chantEffect.ChantingShamanId == deadMonster.Id)
        {
            StatusEffectProcessor.RemoveEffect<DissonantChantEffect>(state.Player);
            events.Add(new StatusExpiredEvent
            {
                ActorId    = state.Player.Id,
                EntityId   = state.Player.Id,
                EffectName = "dissonant_chant",
                Reason     = "shaman_died",
            });
        }
    }

    private static bool TryOpenDoorOnPath(GameMap map, Entity entity, int targetX, int targetY, List<TurnEvent> events)
    {
        int dx = Math.Sign(targetX - entity.X);
        int dy = Math.Sign(targetY - entity.Y);

        // Mirror MoveToward's priority: diagonal first, then horizontal, then vertical.
        if (dx != 0 && dy != 0 && TryOpenDoor(map, entity, entity.X + dx, entity.Y + dy, events))
            return true;
        if (dx != 0 && TryOpenDoor(map, entity, entity.X + dx, entity.Y, events))
            return true;
        if (dy != 0 && TryOpenDoor(map, entity, entity.X, entity.Y + dy, events))
            return true;
        return false;
    }

    /// <summary>
    /// If the monster is adjacent to the unattended home body during player-initiated possession,
    /// applies a 1-in-N chance to kick the phantom wand as a side effect.
    /// Does not consume the monster's action — checked before MonsterAI.Decide.
    /// The possessed host itself is excluded — only free-roaming monsters can kick the wand.
    /// </summary>
    private static void TryKickWand(Entity monster, GameState state, List<TurnEvent> events)
    {
        // Only fires during active player-initiated possession with an unattended home body.
        if (!state.Player.Has<UnattendedBodyTag>()) return;

        // Monster must be adjacent (Chebyshev distance 1) to the home body to kick the wand.
        if (PossessionSystem.ChebyshevDistance(monster.X, monster.Y, state.Player.X, state.Player.Y) > 1) return;

        var (host, effect) = PossessionSystem.FindActivePossession(state);
        if (effect == null || effect.Source != PossessionSource.PlayerInitiated) return;

        // The possessed host is controlled by the player — it does not kick its own wand.
        if (host != null && monster.Id == host.Id) return;

        // 1-in-N chance per monster per turn.
        if (state.Rng.Next(PossessionConfig.WandKickChanceDenominator) != 0) return;

        PossessionSystem.KickWand(state, effect, monster, events);
    }
}
