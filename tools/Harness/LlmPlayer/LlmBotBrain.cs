using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Balance.LlmPlayer;
using UnderWarden.Logic.Balance.Transcript;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;

namespace UnderWarden.Harness.LlmPlayer;

/// <summary>
/// LLM-driven player brain. Implements IPlayerBrain using the Anthropic API for turn
/// decisions, with a BotBrain fallback on API error, timeout, or parse failure.
///
/// Phase 2: core API integration, structured output parsing, and fallback handling.
/// Phase 4 will add full self-assessment hooks (near-death, first-orc, possession, mural).
/// The plumbing for hook detection is wired in Phase 2 so Phase 4 is additive only.
/// </summary>
public sealed class LlmBotBrain : IPlayerBrain, IDisposable
{
    private readonly AnthropicTurnClient _client;
    private readonly LlmPersona _persona;
    private readonly BotBrain _fallbackBrain;
    private readonly int _maxTurns;

    // Ring buffer for RECENT EVENTS: max 3 entries, oldest-first
    private readonly Queue<string> _recentEvents = new();

    // Per-turn hook state
    private string? _pendingPromptBlock;
    private string _pendingHookName = "";
    private int _currentDepth;
    private int _llmTurnsUsed;
    private bool _budgetExhausted;

    // Hybrid decision mode: LLM only called at decision points; bot-brain handles navigation.
    private int _turnsSinceLastDecision;
    private const int PeriodicCheckInterval = 25;

    // True when the player took damage (melee hit, ranged hit, or DoT) on the previous turn.
    // Forces a decision point so the LLM can react to remote threats and chip damage.
    private bool _tookDamageLastTurn;

    // Significant-event detector (Phase 4): extracted to the Logic layer for testability.
    private readonly SignificantEventDetector _detector;

    // System prompt — static, cacheable block (~2650 tokens).
    // Sent with cache_control:ephemeral on each call via AnthropicTurnClient.
    // Public so Program.cs can build the client before constructing the brain.
    public static string BuildSystemPrompt(LlmPersona persona)
    {
        string personaInstruction = persona switch
        {
            LlmPersona.Reader =>
                """
                PERSONA: The Reader

                Your priority is engaging with the game's narrative. Read signs, murals, and all
                Hollowmark lines. Honor relationships: if you made a commitment to the orcs, honor it.
                Decide based on story logic, not expected value. Play as if choices have consequences
                beyond the mechanical — because in this game, they do.

                You receive Hollowmark's actual words and actual mural/sign text verbatim. Read
                them. They are the primary signal for your structural assessment: when the
                writing is present and decision-relevant, your job is to notice whether it could
                actually influence a choice, or whether it arrived with no corresponding action
                surface.

                When making a structural assessment, focus on: was narrative information present
                but impossible to act on? Did a story beat occur without giving you a choice?
                """,
            LlmPersona.SystemExplorer =>
                """
                PERSONA: The System Explorer

                Your priority is triggering mechanics you haven't seen yet. Use possession when
                you haven't tried it recently. Engage the orc faction. Try throwing items. Use
                wands. Explore features. You are mapping the game's interaction surface, not
                optimizing survival. Survive long enough to see more, but don't play
                conservatively if it means missing a system.

                When making a structural assessment, focus on: was this mechanic accessible but
                opaque? Was the interaction surface clear or hidden? Did you find yourself
                reaching for a mechanic and unable to access it?
                """,
            _ => "",
        };

        // Build as a regular string to avoid escaping issues with curly braces in the JSON
        // example block. The persona instruction is appended at the end.
        const string basePrompt =
            "You are playing a roguelike dungeon game. Your goal is to survive as long as possible\n" +
            "while following your persona instructions below.\n\n" +
            "HOW YOU PLAY:\n" +
            "You do not control every step. The game's auto-explore system handles routine navigation\n" +
            "automatically — moving through corridors, searching rooms, walking toward unexplored areas.\n" +
            "You are only asked to decide when something meaningful is happening. The DECISION POINT\n" +
            "line at the top of each prompt tells you exactly what triggered this wake-up.\n" +
            "Focus your reasoning on that specific situation.\n\n" +
            "GAME RULES:\n" +
            "- Combat: attacking an adjacent monster is a melee strike\n" +
            "- Healing: drink potions from inventory when wounded\n" +
            "- Progression: descend the staircase when standing on it. You MAY descend with monsters\n" +
            "  still alive on the floor; clearing the floor first is usually safer but is a choice,\n" +
            "  not a rule.\n" +
            "- Death is permanent; no save/reload\n\n" +
            "STRUCTURAL JUDGMENT VOCABULARY:\n" +
            "dead_action_space: all available options had equivalent/arbitrary outcomes\n" +
            "forced_move: only one meaningful action existed\n" +
            "novel_encounter: first time interacting with this entity or mechanic\n" +
            "system_unreachable: mechanic was present but had no accessible entry point\n\n" +
            "OUTPUT FORMAT (JSON only, no other text):\n" +
            "{\n" +
            "  \"action_index\": <integer, 1-based index from AVAILABLE ACTIONS>,\n" +
            "  \"action_label\": <string, brief description of what you're doing>,\n" +
            "  \"reasoning\": <string, 1-2 sentences explaining your choice>,\n" +
            "  \"structural_assessment\": <null OR {\"judgment\": \"<vocabulary term>\", \"note\": \"<brief explanation>\"}>,\n" +
            "  \"floor_summary\": <null; when the prompt contains a FLOOR COMPLETE block, answer it here as a string>,\n" +
            "  \"reflection\": <null; when the prompt contains a REFLECTION block, answer it here as a string>\n" +
            "}\n\n" +
            "Produce structural_assessment only when:\n" +
            "- Only one action was available (forced_move)\n" +
            "- All available actions had equivalent expected outcomes (dead_action_space)\n" +
            "- You encountered an entity or mechanic for the first time (novel_encounter)\n" +
            "- A mechanic was present but you had no clear way to interact with it (system_unreachable)\n" +
            "Otherwise, structural_assessment is null.\n\n";

        return basePrompt + personaInstruction;
    }

    public LlmBotBrain(
        AnthropicTurnClient client,
        LlmPersona persona,
        string fallbackPersonaName,
        int maxTurns)
    {
        _client = client;
        _persona = persona;
        _fallbackBrain = new BotBrain(BotPersonaRegistry.Get(fallbackPersonaName));
        _maxTurns = maxTurns;
        _detector = new SignificantEventDetector(persona);
    }

    // ── IPlayerBrain: Decide ──────────────────────────────────────────────────

    public PlayerDecision Decide(GameState state)
    {
        // Pull the pending block (if any) and clear it for the next turn
        string? pendingBlock = _pendingPromptBlock;
        string hookName = _pendingHookName;
        _pendingPromptBlock = null;
        _pendingHookName = "";

        // Budget exhausted — permanent fallback for this run
        if (_budgetExhausted)
            return MakeFallbackDecision(state, "api_budget_exhausted");

        // Hybrid mode: only call the API at decision points. Between them, the bot-brain
        // handles navigation silently. A pending hook block (near-death warning, floor
        // summary, etc.) always forces an API call regardless of decision-point status.
        _turnsSinceLastDecision++;
        var (isDecision, triggerContext) = CheckDecisionPoint(state);
        bool needsApi = isDecision || pendingBlock != null;
        if (!needsApi)
            return MakeFallbackDecision(state, "auto_explore");

        _turnsSinceLastDecision = 0;

        var ctx = new DescriberContext(_recentEvents.ToList(), pendingBlock, triggerContext);
        var desc = GameStateDescriber.Describe(state, _persona, ctx);

        // Call the API
        var (text, apiError) = _client.CallSync(desc.Text);
        if (text == null)
            return MakeFallbackDecision(state, $"api_error: {apiError}");

        // Parse the response
        if (!LlmResponseParser.TryParse(text, desc.Actions.Count, out var output, out var parseError))
            return MakeFallbackDecision(state, $"parse_error: {parseError}");

        // Increment turn counter and check budget
        _llmTurnsUsed++;
        if (_llmTurnsUsed >= _maxTurns && !_budgetExhausted)
        {
            _budgetExhausted = true;
            Console.Error.WriteLine(
                $"[LlmBotBrain] API budget exhausted ({_llmTurnsUsed} turns). " +
                "Switching to bot fallback for the rest of this run.");
        }

        // Build structural assessment from parsed output
        StructuralAssessment? assessment = null;
        if (output.StructuralAssessment != null)
        {
            assessment = new StructuralAssessment
            {
                Judgment = output.StructuralAssessment.Judgment,
                Note = output.StructuralAssessment.Note,
            };
        }

        // Build extra judgments (floor_summary and reflection carriers)
        var extra = new List<StructuralAssessment>();

        if (output.FloorSummary != null)
        {
            extra.Add(new StructuralAssessment
            {
                Judgment = "floor_summary",
                Note = output.FloorSummary,
            });
        }

        if (output.Reflection != null)
        {
            // Hook name recorded when the block was queued (Phase 4 populates this).
            // In Phase 2 the hook name is always "" but the field is here for continuity.
            string reflectionLabel = string.IsNullOrEmpty(hookName)
                ? "reflection"
                : $"reflection:{hookName}";

            extra.Add(new StructuralAssessment
            {
                Judgment = reflectionLabel,
                Note = output.Reflection,
            });
        }

        var action = desc.Actions[output.ActionIndex - 1].Action;
        return new PlayerDecision(action, output.Reasoning, assessment, extra);
    }

    // ── IPlayerBrain: OnTurnResolved ──────────────────────────────────────────

    public void OnTurnResolved(int turn, IReadOnlyList<TurnEvent> events, GameState state)
    {
        // Summarize this turn's events and push into the ring buffer (max 3)
        string summary = GameStateDescriber.SummarizeEvents(events);
        _recentEvents.Enqueue(summary);
        if (_recentEvents.Count > 3)
            _recentEvents.Dequeue();

        // Hook detection — delegated to SignificantEventDetector (Logic layer, testable).
        // The detector owns all state (near-death arm/disarm, first-X flags).

        double hpPct = state.PlayerFighter.MaxHp > 0
            ? (double)state.PlayerFighter.Hp / state.PlayerFighter.MaxHp
            : 0;

        var hooks = _detector.ProcessTurn(turn, events, hpPct, id => IsOrc(id, state));

        // Queue the first hook; additional hooks in the same turn are dropped (v1: one per turn).
        // In practice, multiple simultaneous hooks are rare and the next turn's queue is almost
        // always empty when the second hook would have landed.
        if (hooks.Count > 0)
        {
            QueueHookPrompt(hooks[0].HookName, hooks[0].PromptBlock);
        }
    }

    // ── IPlayerBrain: OnFloorEnter ────────────────────────────────────────────

    public void OnFloorEnter(int depth)
    {
        // First floor — no previous floor to summarize
        if (_currentDepth == 0)
        {
            _currentDepth = depth;
            return;
        }

        // Descending to a new floor: queue a floor-summary block for the NEXT Decide
        if (depth > _currentDepth)
        {
            int completedFloor = _currentDepth;
            QueueHookPrompt("floor_summary",
                $"""
                FLOOR {completedFloor} COMPLETE — briefly answer before acting:
                - Primary interaction type this floor (move / fight / explore / use-system):
                - Interesting decisions made (count + 1-sentence description, or "none"):
                - Systems encountered for the first time:
                """);
        }

        _currentDepth = depth;
    }

    // ── IPlayerBrain: OnRunEnd ────────────────────────────────────────────────

    public string? OnRunEnd(string endingLabel)
    {
        bool isDeath = endingLabel.Contains("death", StringComparison.OrdinalIgnoreCase)
                    || endingLabel.Contains("died", StringComparison.OrdinalIgnoreCase);

        var sb = new System.Text.StringBuilder();

        // Brief factual context — the LLM has no conversation history, so we must
        // supply what happened or it will confabulate "I haven't played yet".
        sb.AppendLine($"RUN COMPLETE — ending: {endingLabel.ToUpperInvariant()}");
        sb.AppendLine($"Floors reached: {_currentDepth}  |  Your decisions this run: {_llmTurnsUsed}  |  Auto-explored turns: {_turnsSinceLastDecision + (_llmTurnsUsed > 0 ? 0 : 0)}");
        if (_recentEvents.Count > 0)
        {
            sb.AppendLine("Last events in the run:");
            foreach (var ev in _recentEvents)
                sb.AppendLine($"  - {ev}");
        }
        sb.AppendLine();

        // Death reflection — folded into the run-end call (no Decide call exists after death)
        if (isDeath)
        {
            sb.AppendLine(
                """
                REFLECTION (death):
                Can you trace this death to a specific earlier decision you could have
                played differently? Or did it arrive with no decision point you could
                have acted on?

                If traceable: which turn and action?
                If not traceable: describe the moment it became unavoidable.

                (traceable-to-decision | arrived-without-decision-point)
                """);
            sb.AppendLine();
        }

        // End-of-run summary
        sb.AppendLine(
            $"""
            Based on the decisions you made this run, in 3–5 sentences:
            1. What was the structural character of this run? (Were decisions alive or
               mostly forced?)
            2. What systems were present but had no accessible entry point?
            3. Did your choices accumulate into a coherent story?

            Use the structural_judgment vocabulary: dead_action_space, forced_move,
            novel_encounter, system_unreachable.
            Do not use the word "fair" or describe outcomes as frustrating, satisfying,
            or surprising. Structural observations only.
            """);

        var (text, _) = _client.CallSync(sb.ToString(), maxTokens: 800);
        if (text == null) return null;

        // The system prompt instructs JSON-only output; strip any JSON wrapper if present
        // so run_narrative is plain prose rather than a JSON-encoded string.
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            // Strip fences and try to extract a "reflection" or "reasoning" string from the JSON
            var inner = trimmed.TrimStart('`');
            var firstBrace = inner.IndexOf('{');
            var lastBrace  = inner.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(inner[firstBrace..(lastBrace + 1)]);
                    // Look for a prose value in priority order
                    foreach (var key in new[] { "reflection", "reasoning", "action_label" })
                    {
                        if (doc.RootElement.TryGetProperty(key, out var val) &&
                            val.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var s = val.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) return s;
                        }
                    }
                }
                catch { /* fall through to return raw text */ }
            }
        }

        return text;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Queue a hook prompt block for the next Decide call.
    /// Only one block can be queued at a time; if one is already pending it is
    /// overwritten (the most recent event wins — they rarely overlap in practice).
    /// </summary>
    // ── Decision-point detection ──────────────────────────────────────────────

    private (bool IsDecision, string? TriggerContext) CheckDecisionPoint(GameState state)
    {
        var player = state.Player;

        // Monster adjacent — combat choice
        var adjacent = state.AliveMonsters
            .FirstOrDefault(m => Chebyshev(player.X, player.Y, m.X, m.Y) <= 1);
        if (adjacent != null)
            return (true, $"A {adjacent.Name} is adjacent and ready to fight.");

        // Item at player's tile — pick up or leave
        var atFeet = state.FloorItems
            .FirstOrDefault(i => i.X == player.X && i.Y == player.Y);
        if (atFeet != null)
            return (true, $"You are standing on {atFeet.Name}.");

        // Feature adjacent — chest/mural/signpost interaction
        var adjFeature = state.Features
            .FirstOrDefault(f => Chebyshev(player.X, player.Y, f.X, f.Y) <= 1);
        if (adjFeature != null)
        {
            string label = adjFeature.Get<ChestComponent>() != null ? "a chest"
                : adjFeature.Get<MuralComponent>() != null ? "a mural"
                : adjFeature.Get<SignpostComponent>() != null ? "a signpost"
                : adjFeature.Name;
            return (true, $"You are adjacent to {label}.");
        }

        // Standing on staircase — descend decision
        if (state.PlayerOnStairDown)
            return (true, "You are standing on the staircase. You may descend now.");

        // Wounded with potions — healing decision
        var fighter = state.PlayerFighter;
        double hpPct = fighter.MaxHp > 0 ? (double)fighter.Hp / fighter.MaxHp : 1.0;
        if (hpPct < 0.4 && HasHealingPotion(state.PlayerInventory))
            return (true, $"You are wounded ({(int)(hpPct * 100)}% HP) and carrying a healing potion.");

        // Periodic check — keep the LLM aware of the run
        if (_turnsSinceLastDecision >= PeriodicCheckInterval)
            return (true, "Auto-exploring. Any observations or decisions to make?");

        return (false, null);
    }

    private static int Chebyshev(int ax, int ay, int bx, int by)
        => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

    private static bool HasHealingPotion(UnderWarden.Logic.ECS.Inventory? inventory)
    {
        if (inventory == null) return false;
        foreach (var item in inventory.Items)
        {
            var consumable = item.Get<UnderWarden.Logic.Combat.Consumable>();
            if (consumable?.IsHealing == true) return true;
        }
        return false;
    }

    private void QueueHookPrompt(string hookName, string promptText)
    {
        _pendingPromptBlock = promptText;
        _pendingHookName = hookName;
    }

    /// <inheritdoc/>
    public void InjectPromptBlock(string block)
    {
        _pendingPromptBlock = _pendingPromptBlock == null
            ? block
            : _pendingPromptBlock + "\n\n" + block;
    }

    /// <summary>
    /// Build a fallback decision using the internal BotBrain instance.
    /// Handles ForceDescend by routing toward the stair; handles AbortRun by waiting.
    /// The LLM path never aborts a run.
    /// </summary>
    private PlayerDecision MakeFallbackDecision(GameState state, string reason)
    {
        var botAction = _fallbackBrain.Decide(
            state.Player,
            state.PlayerFighter,
            state.PlayerInventory,
            state.Monsters,
            state.Map,
            floorItems: state.FloorItems);

        PlayerAction action;

        if (botAction.Type == BotAction.ActionType.ForceDescend)
        {
            // Navigate toward the staircase using A*; fallback to Wait if unreachable
            if (state.StairDown != null)
            {
                var path = Pathfinder.AStar(
                    state.Map,
                    state.Player.X, state.Player.Y,
                    state.StairDown.X, state.StairDown.Y,
                    state.Player,
                    canPassDoors: true);

                action = path != null && path.Count > 0
                    ? PlayerAction.MoveTo(path[0].X, path[0].Y)
                    : PlayerAction.Wait;
            }
            else
            {
                action = PlayerAction.Wait;
            }
        }
        else if (botAction.Type == BotAction.ActionType.AbortRun)
        {
            // LLM path never aborts — Wait is the safe sentinel
            action = PlayerAction.Wait;
        }
        else
        {
            action = BotActionConverter.ToPlayerActionWithPathing(botAction, state);
        }

        return new PlayerDecision(action, null, null, Array.Empty<StructuralAssessment>(),
            UsedFallback: true, FallbackReason: reason);
    }

    /// <summary>
    /// Check whether an entity (by ID) has an orc SpeciesTag.TypeId starting with "orc".
    /// Checks both alive monsters and the player (entity id 0).
    /// </summary>
    private static bool IsOrc(int entityId, GameState state)
    {
        if (entityId == 0) return false; // player is never an orc

        // Search alive monsters (most common case)
        foreach (var m in state.AliveMonsters)
        {
            if (m.Id == entityId)
            {
                var tag = m.Get<SpeciesTag>();
                return tag != null && tag.TypeId.StartsWith("orc", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Create an LlmBotBrain from config. Reads ANTHROPIC_API_KEY from the environment.
    /// Throws InvalidOperationException if the key is missing.
    /// </summary>
    public static LlmBotBrain Create(LlmPlayerConfig config)
    {
        string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set");

        var persona = config.Persona.ToLowerInvariant() switch
        {
            "system_explorer" => LlmPersona.SystemExplorer,
            _ => LlmPersona.Reader,
        };

        string systemPrompt = BuildSystemPrompt(persona);

        var client = new AnthropicTurnClient(apiKey, config.Model, systemPrompt, timeoutMs: 10_000);

        return new LlmBotBrain(client, persona, config.FallbackPersona, config.MaxTurns);
    }

    public void Dispose() => _client.Dispose();
}
