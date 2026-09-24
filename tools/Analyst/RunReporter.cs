using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnderWarden.Analyst;

/// <summary>
/// Produces a human-readable gameplay stats block for a single enriched JSONL run transcript.
///
/// Design notes:
/// - Uses System.Text.Json only — no external dependencies.
/// - Events lack actor_name/target_name fields; entity→monster mapping is built from
///   CorpseCreated events (which carry original_monster_id). Entities that were never
///   killed by the player get an "entity_{id}" label as fallback.
/// - hp_profile in the summary is a list of [floor, hp_pct] pairs (not dicts).
/// - LLM decisions are identified by a non-empty decision_context field on the turn record.
/// - All sections degrade gracefully on missing fields.
/// </summary>
public static class RunReporter
{
    public static string Render(string jsonlText)
    {
        var lines = jsonlText.Split('\n')
            .Where(l => l.Trim().Length > 0)
            .ToArray();

        if (lines.Length == 0)
            return "(empty transcript — nothing to report)";

        // ── Parse all records in one pass ─────────────────────────────────────
        JsonObject? header = null;
        JsonObject? summaryObj = null;
        var turnRecords = new List<JsonObject>();

        foreach (var line in lines)
        {
            JsonObject? obj;
            try { obj = JsonNode.Parse(line)?.AsObject(); }
            catch { continue; }
            if (obj == null) continue;

            string recordType = obj["record_type"]?.GetValue<string>() ?? "";
            switch (recordType)
            {
                case "header":  header = obj; break;
                case "summary": summaryObj = obj; break;
                case "turn":    turnRecords.Add(obj); break;
            }
        }

        if (header == null)
            return "(no header record found — transcript may be malformed)";

        // ── Header fields ──────────────────────────────────────────────────────
        int    seed        = header["seed"]?.GetValue<int>() ?? 0;
        string persona     = CapFirst(header["persona"]?.GetValue<string>() ?? "unknown");
        string playerType  = header["player_type"]?.GetValue<string>() ?? "unknown";
        int    floors      = header["floor_count"]?.GetValue<int>() ?? 0;
        string ending      = header["ending"]?.GetValue<string>()?.ToUpperInvariant() ?? "UNKNOWN";
        int    turnCount   = header["turn_count"]?.GetValue<int>() ?? turnRecords.Count;

        // ── Single-pass event scan ─────────────────────────────────────────────
        // Entity names come directly from actor_name/target_name fields on Attack and
        // Death events (added in TurnController). CorpseCreated is kept as a secondary
        // source for any entity not seen in combat (rare edge case).
        var entityLabel = new Dictionary<int, string>(); // entity_id -> display name

        // Combat keyed by *entity id* so we can merge hits/damage per individual monster,
        // then roll up to species level at render time.
        var playerHitsOnEntity   = new Dictionary<int, List<AttackEvent>>();
        var monsterHitsOnPlayer  = new Dictionary<int, List<AttackEvent>>();
        var playerKillsByEntity  = new Dictionary<int, int>(); // entity_id -> kill count

        var pickups   = new List<string>();              // item_name
        var equips    = new List<(string name, string slot)>();
        int healCount = 0;
        int healTotal = 0;

        int llmTurns  = 0;
        int autoTurns = 0;

        double minHpPct   = 1.0;
        int    minHpTurn  = 0;
        int    minHpFloor = 0;
        double finalHpPct = 1.0;

        int totalPlayerHits     = 0;
        int totalPlayerAttempts = 0;
        int totalPlayerDamage   = 0;
        int totalPlayerCrits    = 0;
        int totalMonsterHits    = 0;
        int totalMonsterDamage  = 0;

        foreach (var turn in turnRecords)
        {
            int turnNum  = turn["turn"]?.GetValue<int>()  ?? 0;
            int floor    = turn["floor"]?.GetValue<int>() ?? 0;
            double hpPct = turn["player_hp_pct"]?.GetValue<double>() ?? 1.0;

            // LLM vs auto-explore
            string? dc = turn["decision_context"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(dc))
                llmTurns++;
            else
                autoTurns++;

            // Track HP
            if (hpPct < minHpPct)
            {
                minHpPct   = hpPct;
                minHpTurn  = turnNum;
                minHpFloor = floor;
            }
            finalHpPct = hpPct;

            foreach (var evt in turn["events"]?.AsArray() ?? new JsonArray())
            {
                var e = evt?.AsObject();
                if (e == null) continue;
                string et = e["event_type"]?.GetValue<string>() ?? "";

                switch (et)
                {
                    case "Attack":
                    {
                        int actorId   = e["actor_id"]?.GetValue<int>()    ?? -1;
                        int targetId  = e["target_id"]?.GetValue<int>()   ?? -1;
                        bool hit      = e["hit"]?.GetValue<bool>()         ?? false;
                        int  dmg      = e["damage"]?.GetValue<int>()       ?? 0;
                        bool isCrit   = e["is_critical"]?.GetValue<bool>() ?? false;
                        string actorN  = e["actor_name"]?.GetValue<string>()  ?? "";
                        string targetN = e["target_name"]?.GetValue<string>() ?? "";

                        // Harvest names into the entity label map
                        if (actorId  > 0 && actorN.Length  > 0) entityLabel[actorId]  = actorN;
                        if (targetId > 0 && targetN.Length > 0) entityLabel[targetId] = targetN;

                        if (actorId == 0)
                        {
                            // Player attacking a monster
                            if (!playerHitsOnEntity.ContainsKey(targetId))
                                playerHitsOnEntity[targetId] = new List<AttackEvent>();
                            playerHitsOnEntity[targetId].Add(new AttackEvent(hit, dmg, isCrit));

                            totalPlayerAttempts++;
                            if (hit)
                            {
                                totalPlayerHits++;
                                totalPlayerDamage += dmg;
                                if (isCrit) totalPlayerCrits++;
                            }
                        }
                        else if (targetId == 0)
                        {
                            // Monster attacking player
                            if (!monsterHitsOnPlayer.ContainsKey(actorId))
                                monsterHitsOnPlayer[actorId] = new List<AttackEvent>();
                            monsterHitsOnPlayer[actorId].Add(new AttackEvent(hit, dmg, isCrit));

                            if (hit)
                            {
                                totalMonsterHits++;
                                totalMonsterDamage += dmg;
                            }
                        }
                        break;
                    }
                    case "Death":
                    {
                        int killerId = e["killer_id"]?.GetValue<int>() ?? -1;
                        int actorId  = e["actor_id"]?.GetValue<int>()  ?? -1;
                        string actorN  = e["actor_name"]?.GetValue<string>()  ?? "";
                        string killerN = e["killer_name"]?.GetValue<string>() ?? "";
                        if (actorId  > 0 && actorN.Length  > 0) entityLabel[actorId]  = actorN;
                        if (killerId > 0 && killerN.Length > 0) entityLabel[killerId] = killerN;
                        if (killerId == 0 && actorId != 0)
                        {
                            // Player killed this entity
                            playerKillsByEntity[actorId] = playerKillsByEntity.GetValueOrDefault(actorId, 0) + 1;
                        }
                        break;
                    }
                    case "CorpseCreated":
                    {
                        // Secondary name source: fills in any entity not seen in combat
                        int actorId = e["actor_id"]?.GetValue<int>() ?? -1;
                        string monsterId = e["original_monster_id"]?.GetValue<string>() ?? "";
                        if (actorId > 0 && monsterId.Length > 0 && !entityLabel.ContainsKey(actorId))
                            entityLabel[actorId] = ToDisplayName(monsterId);
                        break;
                    }
                    case "PickUp":
                        if ((e["actor_id"]?.GetValue<int>() ?? -1) == 0)
                        {
                            string itemName = e["item_name"]?.GetValue<string>() ?? "unknown";
                            pickups.Add(itemName);
                        }
                        break;
                    case "Equip":
                        if ((e["actor_id"]?.GetValue<int>() ?? -1) == 0)
                        {
                            string itemName = e["item_name"]?.GetValue<string>() ?? "unknown";
                            string slot     = e["slot"]?.GetValue<string>()      ?? "?";
                            equips.Add((itemName, slot));
                        }
                        break;
                    case "Heal":
                        if ((e["actor_id"]?.GetValue<int>() ?? -1) == 0)
                        {
                            healCount++;
                            healTotal += e["amount_healed"]?.GetValue<int>() ?? 0;
                        }
                        break;
                }
            }
        }

        // ── Build species-level combat rollup ──────────────────────────────────
        // Group player-attack data by monster species label.
        // Entities without a CorpseCreated (survived the run) use "entity_{id}" as label.
        var allEntityIds = playerHitsOnEntity.Keys
            .Concat(monsterHitsOnPlayer.Keys)
            .Concat(playerKillsByEntity.Keys)
            .Distinct()
            .Where(id => id > 0)  // skip player (id 0)
            .ToHashSet();

        // Map each entity to its display label
        string LabelFor(int id) => entityLabel.TryGetValue(id, out var lbl) ? lbl : $"entity_{id}";

        // Roll up by species
        var speciesStats = new Dictionary<string, SpeciesCombatStats>();

        foreach (int eid in allEntityIds)
        {
            string label = LabelFor(eid);
            if (!speciesStats.ContainsKey(label))
                speciesStats[label] = new SpeciesCombatStats(label);

            var stats = speciesStats[label];
            stats.Kills += playerKillsByEntity.GetValueOrDefault(eid, 0);

            if (playerHitsOnEntity.TryGetValue(eid, out var pAtks))
                stats.PlayerAttacks.AddRange(pAtks);

            if (monsterHitsOnPlayer.TryGetValue(eid, out var mAtks))
                stats.MonsterAttacks.AddRange(mAtks);
        }

        // ── Summary fields ─────────────────────────────────────────────────────
        JsonArray? hpProfile         = summaryObj?["hp_profile"]?.AsArray();
        JsonArray? structuralJudgArr = summaryObj?["structural_judgments"]?.AsArray();
        string?    runNarrative      = summaryObj?["run_narrative"]?.GetValue<string>();

        // Count structural judgment types
        var judgmentCounts = new Dictionary<string, int>();
        string? firstJudgmentNote = null;
        if (structuralJudgArr != null)
        {
            foreach (var jNode in structuralJudgArr)
            {
                var j = jNode?.AsObject();
                if (j == null) continue;
                string judgment = j["judgment"]?.GetValue<string>() ?? "unknown";
                judgmentCounts[judgment] = judgmentCounts.GetValueOrDefault(judgment, 0) + 1;
                if (firstJudgmentNote == null)
                    firstJudgmentNote = j["note"]?.GetValue<string>();
            }
        }

        // ── Render ─────────────────────────────────────────────────────────────
        var sb = new StringBuilder();

        // Title box
        string title = $"  Run Report  ·  seed {seed}  ·  {persona}  ·  {floors} floor(s)  ·  {ending}  ";
        int boxWidth = Math.Max(title.Length + 2, 60);
        sb.AppendLine("╔" + new string('═', boxWidth) + "╗");
        sb.AppendLine("║" + title.PadRight(boxWidth) + "║");
        sb.AppendLine("╚" + new string('═', boxWidth) + "╝");
        sb.AppendLine();

        // OVERVIEW
        sb.AppendLine("OVERVIEW");
        double llmPct  = turnCount > 0 ? llmTurns  * 100.0 / turnCount : 0;
        double autoPct = turnCount > 0 ? autoTurns * 100.0 / turnCount : 0;
        sb.AppendLine($"  Turns:      {turnCount} total  ·  {llmTurns} LLM decisions ({llmPct:0.#}%)  ·  {autoTurns} auto-explore ({autoPct:0.#}%)");
        sb.AppendLine($"  Outcome:    {ending.ToLowerInvariant()} ({floors}/{floors} floors)");
        sb.AppendLine();

        // COMBAT
        sb.AppendLine("COMBAT");
        int totalKills = playerKillsByEntity.Values.Sum();
        sb.AppendLine($"  Monsters killed:  {totalKills}");
        sb.AppendLine();

        if (speciesStats.Count == 0)
        {
            sb.AppendLine("  No combat this run.");
        }
        else
        {
            // Sort by total kill count desc, then by name
            foreach (var stats in speciesStats.Values.OrderByDescending(s => s.Kills).ThenBy(s => s.Label))
            {
                int pHits    = stats.PlayerAttacks.Count(a => a.Hit);
                int pAttempts = stats.PlayerAttacks.Count;
                int pDamage  = stats.PlayerAttacks.Sum(a => a.Damage);
                int pCrits   = stats.PlayerAttacks.Count(a => a.IsCrit);
                double pHitPct = pAttempts > 0 ? pHits * 100.0 / pAttempts : 0;
                double pAvgDmg = pHits > 0 ? (double)pDamage / pHits : 0;

                int mHits     = stats.MonsterAttacks.Count(a => a.Hit);
                int mAttempts = stats.MonsterAttacks.Count;
                int mDamage   = stats.MonsterAttacks.Sum(a => a.Damage);
                double mHitPct = mAttempts > 0 ? mHits * 100.0 / mAttempts : 0;
                double mAvgDmg = mHits > 0 ? (double)mDamage / mHits : 0;

                string killTag  = stats.Kills > 0 ? $"x{stats.Kills}  killed" : "x0  survived";
                string youLine  = pAttempts > 0
                    ? $"you: {pHits}/{pAttempts} ({pHitPct:0}%), avg {pAvgDmg:0.#} dmg dealt{(pCrits > 0 ? $", {pCrits} crit(s)" : "")}"
                    : "you: no attacks";
                string itLine   = mAttempts > 0
                    ? $"it: {mHits}/{mAttempts} ({mHitPct:0}%), avg {mAvgDmg:0.#} dmg/hit taken"
                    : "it: no attacks";

                sb.AppendLine($"  {stats.Label,-20} {killTag,-14} |  {youLine}  |  {itLine}");
            }

            sb.AppendLine();
            int totalMonsterAttempts = speciesStats.Values.Sum(s => s.MonsterAttacks.Count);
            double overallHitPct = totalPlayerAttempts > 0 ? totalPlayerHits * 100.0 / totalPlayerAttempts : 0;
            sb.AppendLine($"  TOTALS  you dealt: {totalPlayerDamage} dmg in {totalPlayerHits} hits ({overallHitPct:0}% hit rate, {totalPlayerCrits} crits)  ·  you took: {totalMonsterDamage} dmg in {totalMonsterHits} hits");
        }
        sb.AppendLine();

        // ITEMS
        sb.AppendLine("ITEMS");
        if (pickups.Count > 0)
        {
            var grouped = pickups
                .GroupBy(n => n)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key);
            sb.AppendLine($"  Picked up:");
            sb.AppendLine($"    {string.Join(", ", grouped)}");
        }
        else
        {
            sb.AppendLine("  Picked up:  (none)");
        }

        if (equips.Count > 0)
        {
            sb.AppendLine("  Equipped:");
            foreach (var (name, slot) in equips)
                sb.AppendLine($"    {name}  [{slot}]");
        }
        else
        {
            sb.AppendLine("  Equipped:   (none)");
        }

        if (healCount > 0)
            sb.AppendLine($"  Potions used:  {healCount}  (healed {healTotal} HP total)");
        else
            sb.AppendLine("  Potions used:  0");
        sb.AppendLine();

        // HEALTH
        sb.AppendLine("HEALTH");
        if (hpProfile != null && hpProfile.Count > 0)
        {
            // hp_profile is [[floor, hp_pct], ...] — floor-entry snapshots
            sb.AppendLine("  Floor-entry HP:");
            foreach (var entry in hpProfile)
            {
                var arr = entry?.AsArray();
                if (arr == null || arr.Count < 2) continue;
                int    f  = arr[0]?.GetValue<int>()    ?? 0;
                double hp = arr[1]?.GetValue<double>() ?? 0;
                sb.AppendLine($"    Floor {f}: {hp * 100:0.#}%");
            }
        }

        if (minHpTurn > 0)
            sb.AppendLine($"  Min HP:     {minHpPct * 100:0.#}%  on turn {minHpTurn}  (floor {minHpFloor})");

        sb.AppendLine($"  Final HP:   {finalHpPct * 100:0.#}%");
        sb.AppendLine();

        // STRUCTURAL ASSESSMENTS
        sb.AppendLine("STRUCTURAL ASSESSMENTS  (from LLM decisions)");
        if (judgmentCounts.Count == 0)
        {
            sb.AppendLine("  (none fired)");
        }
        else
        {
            foreach (var (j, count) in judgmentCounts.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"  {j,-30} x{count}");

            if (firstJudgmentNote != null)
            {
                sb.AppendLine();
                sb.AppendLine("  Sample:");
                foreach (var chunk in WordWrap(firstJudgmentNote, 76))
                    sb.AppendLine($"    {chunk}");
            }
        }
        sb.AppendLine();

        // RUN NARRATIVE
        sb.AppendLine("RUN NARRATIVE");
        if (string.IsNullOrWhiteSpace(runNarrative))
        {
            sb.AppendLine("  (none recorded)");
        }
        else
        {
            foreach (var chunk in WordWrap(runNarrative, 78))
                sb.AppendLine($"  {chunk}");
        }

        return sb.ToString();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Convert a snake_case monster id to a Title Case display name.</summary>
    private static string ToDisplayName(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        return string.Join(" ", id.Split('_').Select(CapFirst));
    }

    private static string CapFirst(string s)
        => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    /// <summary>
    /// Wrap text at approximately maxWidth characters, breaking on word boundaries.
    /// Returns one string per visual line.
    /// </summary>
    private static IEnumerable<string> WordWrap(string text, int maxWidth)
    {
        // Normalise whitespace / newlines so we handle narrative blobs cleanly.
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var line  = new StringBuilder();

        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > maxWidth)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0)
            yield return line.ToString();
    }

    // ── Data types ─────────────────────────────────────────────────────────────

    private sealed record AttackEvent(bool Hit, int Damage, bool IsCrit);

    private sealed class SpeciesCombatStats(string label)
    {
        public string Label { get; } = label;
        public int Kills { get; set; }
        public List<AttackEvent> PlayerAttacks  { get; } = new();
        public List<AttackEvent> MonsterAttacks { get; } = new();
    }
}
