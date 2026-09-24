using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnderWarden.Analyst;

/// <summary>
/// Renders an enriched JSONL transcript as a compact human-readable turn-by-turn trace.
/// Purpose: eyeball verification that a recorded run contains coherent gameplay — real
/// combat, floor transitions, escalating pressure, a death or ending. Proves the harness
/// ran real game logic, not a replay of canned states.
///
/// Output format: one line per "interesting" turn plus condensed quiet-turn summaries.
/// Noisy structural fields (pos, entity IDs) are suppressed; meaningful game beats
/// (combat hits/misses, damage, kills, heals, loot, descents, deaths) are surfaced.
/// </summary>
public static class TraceRenderer
{
    public static string Render(string jsonl, TraceOptions opts)
    {
        var lines = jsonl.Split('\n').Where(l => l.Trim().Length > 0).ToArray();
        if (lines.Length == 0) return "(empty transcript)";

        var sb = new StringBuilder();
        var header = JsonNode.Parse(lines[0])?.AsObject();
        var summary = lines.Length > 1 ? JsonNode.Parse(lines[^1])?.AsObject() : null;

        // Header block
        string runId = header?["run_id"]?.GetValue<string>() ?? "?";
        int seed = header?["seed"]?.GetValue<int>() ?? 0;
        int turns = header?["turn_count"]?.GetValue<int>() ?? 0;
        int floors = header?["floor_count"]?.GetValue<int>() ?? 0;
        string ending = header?["ending"]?.GetValue<string>() ?? "?";
        sb.AppendLine($"╔══ RUN {runId[..Math.Min(16, runId.Length)]}… seed={seed} ══╗");
        sb.AppendLine($"║  {turns} turns   {floors} floor(s)   ending={ending}");
        var hpProfile = summary?["hp_profile"]?.AsArray();
        if (hpProfile != null)
        {
            var hpStr = string.Join("  ", hpProfile.Select(p =>
            {
                var arr = p?.AsArray();
                if (arr == null) return "?";
                return $"F{arr[0]}:{arr[1]?.GetValue<double>() * 100:0}%HP";
            }));
            sb.AppendLine($"║  floor-entry HP: {hpStr}");
        }
        sb.AppendLine("╠══════════════════════════════════════════╣");

        int currentFloor = 0;
        int quietStreak = 0;     // consecutive Move-only turns
        int moveCount = 0;
        int quietTurnStart = 0;

        for (int i = 1; i < lines.Length - 1; i++)
        {
            JsonObject? turn;
            try { turn = JsonNode.Parse(lines[i])?.AsObject(); }
            catch { continue; }
            if (turn == null) continue;
            if (turn["record_type"]?.GetValue<string>() != "turn") continue;

            int t = turn["turn"]?.GetValue<int>() ?? 0;
            int floor = turn["floor"]?.GetValue<int>() ?? 0;
            double hp = turn["player_hp_pct"]?.GetValue<double>() ?? 1.0;
            var action = turn["action_taken"]?.AsObject();
            string kind = action?["kind"]?.GetValue<string>() ?? "?";
            var events = turn["events"]?.AsArray() ?? new JsonArray();

            // Floor transition header
            if (floor != currentFloor)
            {
                if (quietStreak > 0) FlushQuiet(sb, quietStreak, moveCount, quietTurnStart, t - 1);
                quietStreak = 0; moveCount = 0;
                currentFloor = floor;
                sb.AppendLine();
                sb.AppendLine($"══ FLOOR {floor} ══════════════════════════════════════");
            }

            // Classify the turn
            var eventTypes = events.Select(e => e?.AsObject()?["event_type"]?.GetValue<string>() ?? "").ToList();
            bool isCombat = eventTypes.Any(e => e is "Attack" or "Death" or "Heal" or "DotDamage" or
                "SoulBolt" or "LifeDrain" or "RangedAttack" or "BleedTick" or "Spell");
            bool isInteresting = isCombat
                || eventTypes.Any(e => e is "Descend" or "PickUp" or "Equip" or "Unequip"
                    or "ChestOpened" or "ChestLooted" or "TrapTriggered" or "MuralExamined"
                    or "SignpostRead" or "PossessionEntered" or "PossessionExited"
                    or "WandUse" or "Throw" or "GuardianRose" or "WeighingResolved"
                    or "OrcRepChanged" or "VoiceLine")
                || kind is "UseItem" or "CastSpell" or "ThrowItem"
                || opts.Verbose;

            if (!isInteresting && kind is "Move" or "Wait")
            {
                if (quietStreak == 0) quietTurnStart = t;
                quietStreak++;
                if (kind == "Move") moveCount++;
                continue;
            }

            // Flush quiet streak before an interesting turn
            if (quietStreak > 0)
            {
                FlushQuiet(sb, quietStreak, moveCount, quietTurnStart, t - 1);
                quietStreak = 0; moveCount = 0;
            }

            // Render this turn
            string hpBar = HpBar(hp);
            var line = new StringBuilder($"  T{t,4} {hpBar} [{kind,-12}]");

            // Add combat detail
            foreach (var evt in events)
            {
                var e = evt?.AsObject();
                if (e == null) continue;
                string et = e["event_type"]?.GetValue<string>() ?? "";
                switch (et)
                {
                    case "Attack":
                    {
                        bool hit = e["hit"]?.GetValue<bool>() ?? false;
                        int dmg = e["damage"]?.GetValue<int>() ?? 0;
                        bool kill = e["target_killed"]?.GetValue<bool>() ?? false;
                        int actor = e["actor_id"]?.GetValue<int>() ?? -1;
                        bool playerAttacking = actor == 0;
                        if (playerAttacking)
                            line.Append(hit ? $" → hit {dmg}dmg{(kill ? " 💀" : "")}" : " → MISS");
                        else
                            line.Append(hit ? $" ← {dmg}dmg" : " ← miss");
                        break;
                    }
                    case "RangedAttack":
                    {
                        bool hit = e["hit"]?.GetValue<bool>() ?? false;
                        int dmg = e["damage"]?.GetValue<int>() ?? 0;
                        bool kill = e["target_killed"]?.GetValue<bool>() ?? false;
                        line.Append(hit ? $" →ranged {dmg}dmg{(kill ? " 💀" : "")}" : " →ranged MISS");
                        break;
                    }
                    case "Death":
                    {
                        int actorId = e["actor_id"]?.GetValue<int>() ?? -1;
                        if (actorId == 0) line.Append(" *** PLAYER DIED ***");
                        break;
                    }
                    case "Heal":
                    {
                        int amt = e["amount_healed"]?.GetValue<int>() ?? 0;
                        string item = e["item_name"]?.GetValue<string>() ?? "potion";
                        line.Append($" ♥+{amt} ({item})");
                        break;
                    }
                    case "PickUp":
                        line.Append($" +{e["item_name"]?.GetValue<string>() ?? "item"}");
                        break;
                    case "Equip":
                        line.Append($" equip:{e["item_name"]?.GetValue<string>() ?? "?"}");
                        break;
                    case "Descend":
                        line.Append($" ↓ floor {e["new_depth"]?.GetValue<int>() ?? 0}");
                        break;
                    case "TrapTriggered":
                        line.Append($" TRAP:{e["source"]?.GetValue<string>() ?? "?"}");
                        break;
                    case "DotDamage":
                    {
                        int dmg = e["damage"]?.GetValue<int>() ?? 0;
                        string effect = e["effect_name"]?.GetValue<string>() ?? "dot";
                        line.Append($" DOT:{effect} -{dmg}");
                        break;
                    }
                    case "ChestOpened":
                        line.Append(" chest opened");
                        break;
                    case "WandUse":
                        line.Append($" wand:{e["wand_name"]?.GetValue<string>() ?? "?"} ({e["remaining_charges"]}ch)");
                        break;
                    case "Spell":
                    {
                        string spell = e["spell_name"]?.GetValue<string>() ?? "?";
                        bool success = e["success"]?.GetValue<bool>() ?? false;
                        line.Append($" spell:{spell}{(success ? "" : "(fail)")}");
                        break;
                    }
                    case "VoiceLine":
                    {
                        string resolved = e["resolved_text"]?.GetValue<string>() ?? e["trigger_id"]?.GetValue<string>() ?? "?";
                        // Trim long lines
                        if (resolved.Length > 60) resolved = resolved[..57] + "…";
                        line.Append($" 💬 \"{resolved}\"");
                        break;
                    }
                    case "OrcRepChanged":
                        line.Append($" ⚑ orc-rep→{e["to_state"]?.GetValue<string>()}");
                        break;
                    case "GuardianRose":
                        line.Append($" ⚔ Guardian:{e["guardian"]} tier:{e["tier"]}");
                        break;
                    case "WeighingResolved":
                        line.Append($" ⚖ WEIGHING:{e["ending"]}");
                        break;
                    case "PossessionEntered":
                        line.Append($" 👁 POSSESS:{e["host_species"]?.GetValue<string>()}");
                        break;
                    case "PossessionExited":
                        line.Append($" 👁 EXIT({e["reason"]?.GetValue<string>()})");
                        break;
                }
            }

            sb.AppendLine(line.ToString());
        }

        // Flush final quiet streak
        if (quietStreak > 0)
            FlushQuiet(sb, quietStreak, moveCount, quietTurnStart, turns);

        // Summary block
        sb.AppendLine();
        sb.AppendLine("══ END ═════════════════════════════════════════");
        if (summary != null)
        {
            var triggers = summary["system_triggers"]?.AsObject();
            if (triggers != null)
            {
                var fired = triggers.Where(kv =>
                    kv.Value is JsonValue v && v.TryGetValue<bool>(out var b) && b)
                    .Select(kv => kv.Key).ToList();
                if (fired.Count > 0)
                    sb.AppendLine($"  systems touched: {string.Join(", ", fired)}");
            }
            var memos = summary["memos_delivered"]?.AsArray();
            if (memos?.Count > 0)
                sb.AppendLine($"  memos: {string.Join(", ", memos.Select(m => m?.AsObject()?["key"]?.GetValue<string>() ?? "?"))}");
        }
        sb.AppendLine($"  {turns} turns  ending={ending}");
        return sb.ToString();
    }

    private static void FlushQuiet(StringBuilder sb, int streak, int moves, int start, int end)
    {
        string range = start == end ? $"T{start}" : $"T{start}-{end}";
        sb.AppendLine($"  {range,12}  … {streak} quiet turn(s), {moves} move(s)");
    }

    private static string HpBar(double pct)
    {
        int filled = (int)Math.Round(pct * 5);
        filled = Math.Clamp(filled, 0, 5);
        string color = pct > 0.6 ? "█" : pct > 0.3 ? "▓" : "░";
        return $"[{new string(color[0], filled)}{new string('·', 5 - filled)}]";
    }
}

public sealed class TraceOptions
{
    /// <summary>Show all turns, not just interesting ones.</summary>
    public bool Verbose { get; init; }
}
