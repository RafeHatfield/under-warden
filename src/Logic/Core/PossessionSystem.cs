using UnderWarden.Logic.AI;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Handles possession state transitions, visibility constraint enforcement, drain clock,
/// and all exit pipelines.
///
/// Sibling of PortalSystem. All methods are static; all mutable state lives in entity
/// components (PossessionEffect, UnattendedBodyTag) and GameState.
///
/// The cross-system contract: one primitive (PossessionEffect) composes all three
/// possession sources — player Hollowmark-channel, Under-Warden bureaucratic possession,
/// and Variant 3 dispel. See §1 of plan_possession_system.md.
/// </summary>
public static class PossessionSystem
{
    // ── Targeting ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the entity is a valid possession target.
    /// Checked before the player confirms the Possess action.
    /// </summary>
    public static bool IsValidTarget(Entity target, Entity possessor, GameState state)
    {
        if (target.Get<Fighter>() == null) return false;
        if (target.Get<AiComponent>() == null) return false;
        if (target.Get<Fighter>()!.IsAlive == false) return false;
        // Can't possess Sasha's own side — the player or a player-ally (TASK-006).
        if (FactionRegistry.IsPlayerSide(target.Get<AiComponent>()!.Faction)) return false;
        if (target.Get<StatusImmunityComponent>()?.IsImmuneTo("possessed") == true) return false;
        if (target.Has<CorpseComponent>()) return false;
        if (target.Has<PossessionEffect>()) return false;
        if (ChebyshevDistance(possessor.X, possessor.Y, target.X, target.Y) > PossessionConfig.MaxPossessionDistance) return false;
        if (!state.Map.HasLineOfSight(possessor.X, possessor.Y, target.X, target.Y)) return false;
        return true;
    }

    // ── Enter ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Player enters possession of the given host entity.
    /// Applies PossessionEffect to host; applies UnattendedBodyTag to home body.
    /// Returns false if the target is immune to possession.
    /// Costs one player turn — the caller (TurnController) has already consumed the turn.
    /// </summary>
    public static bool Enter(Entity host, GameState state, List<TurnEvent> events)
    {
        var effect = StatusEffectProcessor.ApplyEffect<PossessionEffect>(host, int.MaxValue);
        if (effect == null) return false;

        effect.PossessorEntityId = state.Player.Id;
        effect.OriginatorBodyId = state.Player.Id;
        effect.DrainPerTurn = PossessionConfig.DrainPerTurnByDepth(state.CurrentDepth);
        effect.Source = PossessionSource.PlayerInitiated;
        effect.EnteredTurn = state.TurnCount;
        effect.WandTileX = state.Player.X;
        effect.WandTileY = state.Player.Y;

        state.Player.Add(new UnattendedBodyTag());

        var speciesId = host.Get<SpeciesTag>()?.TypeId ?? "";
        events.Add(new PossessionEnteredEvent
        {
            ActorId = state.Player.Id,
            HostEntityId = host.Id,
            HostSpecies = speciesId,
            OriginatorBodyId = state.Player.Id,
        });

        // Occasional enter commentary (~25% chance). Event pools fire reliably; only enter is gated.
        if (state.Rng.Next(PossessionConfig.PossessionEnterVoiceChanceDenominator) == 0)
        {
            events.Add(new VoiceLineEvent
            {
                ActorId = state.Player.Id,
                TriggerId = PossessionEnterTriggerId(speciesId),
            });
        }

        return true;
    }

    // ── Exit: Voluntary (§8.1) ────────────────────────────────────────────────

    /// <summary>
    /// Player voluntarily exits possession. Free action.
    /// Records species-knowledge unlock if held long enough (§11).
    /// </summary>
    public static void ExitVoluntary(GameState state, List<TurnEvent> events)
    {
        var (host, effect) = FindActivePossession(state);
        if (host == null || effect == null) return;

        int turnsHeld = state.TurnCount - effect.EnteredTurn;
        string? speciesId = host.Get<SpeciesTag>()?.TypeId;

        host.Remove<PossessionEffect>();
        state.Player.Remove<UnattendedBodyTag>();

        if (speciesId != null)
        {
            if (turnsHeld >= PossessionConfig.KnowledgeUnlockTurnThreshold)
                state.Knowledge.RecordTrait(speciesId, "possessed_by_player");
            else
                state.Knowledge.RecordEngaged(speciesId);
        }

        events.Add(new PossessionExitedEvent
        {
            ActorId = state.Player.Id,
            Reason = "voluntary",
            HostEntityId = host.Id,
            HostSpecies = speciesId ?? "",
        });
        events.Add(new VoiceLineEvent { ActorId = state.Player.Id, TriggerId = "possession_exit_voluntary" });
    }

    // ── Exit: Visibility break (§8.3) ─────────────────────────────────────────

    /// <summary>
    /// End-of-turn visibility constraint check. Fires forced exit if host is outside
    /// MAX_POSSESSION_DISTANCE or has no LOS to home body.
    /// No-op if no active player-initiated possession.
    /// </summary>
    public static void CheckVisibilityConstraint(GameState state, List<TurnEvent> events)
    {
        var (host, _) = FindActivePossession(state);
        if (host == null) return;

        var home = state.Player;
        if (!home.Get<Fighter>()?.IsAlive == true) return; // home body died — OnHomeBodyKilled handles it

        bool distanceBroken = ChebyshevDistance(host.X, host.Y, home.X, home.Y) > PossessionConfig.MaxPossessionDistance;
        bool losBroken = !state.Map.HasLineOfSight(host.X, host.Y, home.X, home.Y);
        if (!distanceBroken && !losBroken) return;

        string? speciesId = host.Get<SpeciesTag>()?.TypeId;
        int hostId = host.Id;

        host.Remove<PossessionEffect>();
        state.Player.Remove<UnattendedBodyTag>();

        if (speciesId != null)
            state.Knowledge.RecordEngaged(speciesId);

        events.Add(new PossessionExitedEvent
        {
            ActorId = state.Player.Id,
            Reason = "visibility_broken",
            HostEntityId = hostId,
            HostSpecies = speciesId ?? "",
        });
        events.Add(new VoiceLineEvent { ActorId = state.Player.Id, TriggerId = "possession_exit_out_of_range" });
    }

    // ── ApplyDrainTick (§7) ───────────────────────────────────────────────────

    /// <summary>
    /// Drain one tick from the home body. Called at end of the player's possessed turn.
    /// Safety rail: drain alone cannot kill the home body (clamp to 1 HP, force voluntary exit).
    /// Monster damage can still kill the home body — that's handled by OnHomeBodyKilled.
    /// </summary>
    public static void ApplyDrainTick(GameState state, List<TurnEvent> events)
    {
        var (_, effect) = FindActivePossession(state);
        if (effect == null || effect.DrainPerTurn <= 0) return;

        var homeFighter = state.PlayerFighter;

        if (homeFighter.Hp - effect.DrainPerTurn <= 0)
        {
            // Safety rail: clamp to 1, force exit
            int damage = homeFighter.Hp - 1;
            if (damage > 0)
            {
                homeFighter.Hp = 1;
                events.Add(new PossessionDrainEvent { ActorId = state.Player.Id, TargetEntityId = state.Player.Id, Damage = damage });
            }
            ExitVoluntary(state, events);
            return;
        }

        homeFighter.Hp -= effect.DrainPerTurn;
        events.Add(new PossessionDrainEvent { ActorId = state.Player.Id, TargetEntityId = state.Player.Id, Damage = effect.DrainPerTurn });

        // Drain progress voice warnings — three tiers, each fires once per session.
        // 25% drained: home body at ≤75% MaxHp.
        if (!effect.DrainWarning25Fired && homeFighter.MaxHp > 0
            && homeFighter.Hp <= homeFighter.MaxHp * 3 / 4)
        {
            effect.DrainWarning25Fired = true;
            events.Add(new VoiceLineEvent { ActorId = state.Player.Id, TriggerId = "possession_drain_warning_25" });
        }
        // 50% drained: home body at ≤50% MaxHp.
        if (!effect.DrainWarning50Fired && homeFighter.MaxHp > 0
            && homeFighter.Hp <= homeFighter.MaxHp / 2)
        {
            effect.DrainWarning50Fired = true;
            events.Add(new VoiceLineEvent { ActorId = state.Player.Id, TriggerId = "possession_drain_warning_50" });
        }
        // 75% drained: home body at ≤25% MaxHp — also fires the UI near-death alert.
        if (!effect.NearDeathWarningFired && homeFighter.MaxHp > 0
            && homeFighter.Hp <= homeFighter.MaxHp / 4)
        {
            effect.NearDeathWarningFired = true;
            events.Add(new PossessionNearDeathWarningEvent
            {
                ActorId = state.Player.Id,
                HomeBodyEntityId = state.Player.Id,
                CurrentHp = homeFighter.Hp,
                MaxHp = homeFighter.MaxHp,
            });
            events.Add(new VoiceLineEvent { ActorId = state.Player.Id, TriggerId = "possession_drain_warning_75" });
        }
    }

    // ── OnPossessionInducedHostDeath (§8.2 + §8.5 WardenInitiated) ────────────

    /// <summary>
    /// Dedicated death pipeline for possession-induced host deaths.
    /// Bypasses the standard OnDeath flow — explicitly skips RecordKilled, XP, and
    /// faction-reputation kill triggers. Emits DeathEvent for VFX.
    ///
    /// Entry point for both §8.2 (host died during player possession) and §8.5
    /// WardenInitiated path (spell-break collapses a past-Sasha corpse).
    ///
    /// reason: "host_died" | "warden_dispelled"
    /// </summary>
    public static void OnPossessionInducedHostDeath(Entity host, GameState state, List<TurnEvent> events, string reason = "host_died")
    {
        var effect = host.Get<PossessionEffect>();
        if (effect == null) return;

        string? speciesId = host.Get<SpeciesTag>()?.TypeId;
        int hostId = host.Id;

        events.Add(new DeathEvent { ActorId = hostId, KillerId = state.Player.Id, IsPossessionInduced = true });

        host.Remove<PossessionEffect>();
        state.Player.Remove<UnattendedBodyTag>();

        // RecordEngaged only — no kill credit (per OQ-9)
        if (speciesId != null)
            state.Knowledge.RecordEngaged(speciesId);

        events.Add(new PossessionExitedEvent
        {
            ActorId = state.Player.Id,
            Reason = reason,
            HostEntityId = hostId,
            HostSpecies = speciesId ?? "",
        });

        if (reason == "host_died")
            events.Add(new VoiceLineEvent { ActorId = state.Player.Id, TriggerId = "possession_exit_host_death" });
    }

    // ── OnPossessionDispelled (§8.5) ──────────────────────────────────────────

    /// <summary>
    /// Called by the Dispel spell handler when a PossessionEffect is removed from a target.
    /// Branches on PossessionEffect.Source:
    ///   PlayerInitiated → player exits, DisorientationEffect applied to home body.
    ///   WardenInitiated  → past-Sasha corpse collapses (Variant 3 beat).
    /// </summary>
    public static void OnPossessionDispelled(Entity host, PossessionEffect effect, GameState state, List<TurnEvent> events)
    {
        if (effect.Source == PossessionSource.PlayerInitiated)
        {
            host.Remove<PossessionEffect>();
            state.Player.Remove<UnattendedBodyTag>();

            StatusEffectProcessor.ApplyEffect<DisorientationEffect>(state.Player, 3);

            string? speciesId = host.Get<SpeciesTag>()?.TypeId;
            if (speciesId != null)
                state.Knowledge.RecordEngaged(speciesId);

            events.Add(new PossessionExitedEvent
            {
                ActorId = state.Player.Id,
                Reason = "dispelled",
                HostEntityId = host.Id,
                HostSpecies = speciesId ?? "",
            });
        }
        else // WardenInitiated — Variant 3
        {
            // Collapse the corpse-host inert (sets HP to 0 before routing through the death pipeline)
            var hostFighter = host.Get<Fighter>();
            if (hostFighter != null) hostFighter.Hp = 0;

            // Bypass standard death pipeline — no kill credit for the Hall Warden species
            OnPossessionInducedHostDeath(host, state, events, reason: "warden_dispelled");

            // Override: record past-self freed instead of Hall Warden engagement
            state.Knowledge.RecordTrait("past_self", "freed");

            // Hollowmark voice trigger — post spell-break beat
            events.Add(new VoiceLineEvent
            {
                ActorId = state.Player.Id,
                TriggerId = "past_sasha_encounter.possessed_corpse.post_spell_break",
            });
        }
    }

    // ── OnHomeBodyKilled (§8.4) ───────────────────────────────────────────────

    /// <summary>
    /// Called when the home body's HP reaches 0 from an external source (monster, trap, DOT).
    /// Removes the possession effect from the host; game-over fires through the normal
    /// PlayerFighter.IsAlive == false path after this returns.
    /// </summary>
    public static void OnHomeBodyKilled(GameState state, List<TurnEvent> events)
    {
        var (host, _) = FindActivePossession(state);
        if (host == null) return;

        string? speciesId = host.Get<SpeciesTag>()?.TypeId;
        int hostId = host.Id;

        host.Remove<PossessionEffect>();
        // UnattendedBodyTag stays on the (now dead) home body — moot since run is over

        events.Add(new PossessionExitedEvent
        {
            ActorId = state.Player.Id,
            Reason = "home_body_died",
            HostEntityId = hostId,
            HostSpecies = speciesId ?? "",
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static int ChebyshevDistance(int x1, int y1, int x2, int y2)
        => Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    public static (Entity? host, PossessionEffect? effect) FindActivePossession(GameState state)
    {
        foreach (var monster in state.Monsters)
        {
            var eff = monster.Get<PossessionEffect>();
            if (eff?.Source == PossessionSource.PlayerInitiated && eff.PossessorEntityId == state.Player.Id)
                return (monster, eff);
        }
        return (null, null);
    }

    // ── Wand-kick mechanic (§5 / OQ-2 resolution: Option A) ──────────────────

    /// <summary>
    /// True when the phantom wand position has drifted beyond MaxWandDistance from the host.
    /// When suppressed, Hollowmark-mediated abilities (portals, spell-break) are blocked.
    /// Returns false when the wand position is uninitialised (WandTileX &lt; 0).
    /// </summary>
    public static bool IsWandAbilitySuppressed(PossessionEffect effect, Entity host)
    {
        if (effect.WandTileX < 0) return false; // uninitialised wand position
        return ChebyshevDistance(effect.WandTileX, effect.WandTileY, host.X, host.Y) > PossessionConfig.MaxWandDistance;
    }

    /// <summary>
    /// Kick the phantom wand 1–3 tiles in a random direction.
    /// Emits WandKickedEvent. Also emits VoiceLineEvent when the wand drifts beyond
    /// MaxWandDistance from the host (abilities suppressed).
    /// Does not consume the kicker's action — this is a side effect of adjacency.
    /// </summary>
    public static void KickWand(GameState state, PossessionEffect effect, Entity kicker, List<TurnEvent> events)
    {
        int distance = state.Rng.Next(1, 4); // 1–3 tiles

        // Eight possible kick directions: cardinal + diagonal
        int[][] dirs = [[1,0],[-1,0],[0,1],[0,-1],[1,1],[1,-1],[-1,1],[-1,-1]];
        var dir = dirs[state.Rng.Next(dirs.Length)];

        int newX = effect.WandTileX + dir[0] * distance;
        int newY = effect.WandTileY + dir[1] * distance;

        // Clamp to map bounds — wand cannot go off-map.
        newX = Math.Clamp(newX, 0, state.Map.Width - 1);
        newY = Math.Clamp(newY, 0, state.Map.Height - 1);

        effect.WandTileX = newX;
        effect.WandTileY = newY;

        events.Add(new WandKickedEvent
        {
            ActorId           = kicker.Id,
            KickerEntityId    = kicker.Id,
            NewWandPositionX  = newX,
            NewWandPositionY  = newY,
        });

        // Emit voice trigger once when wand drifts out of usable range.
        // IsWandAbilitySuppressed compares to the host, not the home body.
        var (host, _) = FindActivePossession(state);
        if (host != null && IsWandAbilitySuppressed(effect, host))
        {
            events.Add(new VoiceLineEvent
            {
                ActorId   = state.Player.Id,
                TriggerId = "possession_wand_kicked",
            });
        }
    }

    // ── OnHomeBodyHit ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called when a monster lands a hit on the player's home body while the player
    /// is inhabiting a host. Fires the home-body-threatened voice line once per possession.
    /// No-op when not possessing or when already fired this possession.
    /// </summary>
    public static void OnHomeBodyHit(GameState state, List<TurnEvent> events)
    {
        if (!state.Player.Has<UnattendedBodyTag>()) return;

        var (_, effect) = FindActivePossession(state);
        if (effect == null || effect.HomeBodyThreatenedFired) return;

        effect.HomeBodyThreatenedFired = true;
        events.Add(new VoiceLineEvent { ActorId = state.Player.Id, TriggerId = "possession_home_body_threatened" });
    }

    // ── Species-to-trigger mapping ────────────────────────────────────────────

    /// <summary>
    /// Maps a species ID to the correct possession_enter trigger for compound-key lookup.
    /// Six bespoke hosts get their own pools; others resolve via orc/undead category or generic.
    /// </summary>
    private static string PossessionEnterTriggerId(string speciesId)
    {
        // Six bespoke hosts with dedicated voice pools.
        if (speciesId is "hall_warden" or "orc_shaman" or "king_bat" or "troll" or "bone_orc" or "hollow_orc")
            return $"possession_enter.{speciesId}";

        // Orc-family: any entity whose ID contains "orc" (covers grunt, brute, scout, veteran, etc.)
        if (speciesId.Contains("orc", StringComparison.OrdinalIgnoreCase))
            return "possession_enter.orc";

        // Undead-family: canonical undead species.
        if (speciesId is "zombie" or "plague_zombie" or "skeleton" or "wraith" or "lich")
            return "possession_enter.undead";

        return "possession_enter";
    }
}
