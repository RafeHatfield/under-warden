using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Knowledge;

/// <summary>
/// Tracks and exposes player knowledge about monster species for the inspect system.
///
/// Knowledge is keyed by species ID (the YAML monster type key, e.g. "orc", "plague_zombie").
/// Each encounter increments the appropriate counter, which advances the knowledge tier.
/// The system is read-only from a gameplay perspective — it observes, not modifies.
///
/// Lives on GameState as a single instance per run; reset on new game via Reset().
/// </summary>
public sealed class MonsterKnowledgeSystem
{
    private readonly Dictionary<string, MonsterKnowledgeEntry> _entries = new();

    /// <summary>Per-species knowledge entries, exposed read-only for the mid-run serializer.</summary>
    public IReadOnlyDictionary<string, MonsterKnowledgeEntry> Entries => _entries;

    /// <summary>Restore one species entry after a mid-run load. Serializer-only — gameplay records
    /// knowledge through the Record* methods.</summary>
    public void RestoreEntry(string speciesId, int seenCount, int engagedCount, int killedCount, IEnumerable<string> traitsDiscovered)
    {
        var entry = new MonsterKnowledgeEntry
        {
            SeenCount = seenCount,
            EngagedCount = engagedCount,
            KilledCount = killedCount,
        };
        foreach (var t in traitsDiscovered) entry.TraitsDiscovered.Add(t);
        _entries[speciesId] = entry;
    }

    // ── Write API (called by TurnController) ───────────────────────────────────

    /// <summary>
    /// Record that a monster of this species entered the player's FOV.
    /// Increments SeenCount once per call — callers should guard against calling this
    /// multiple times per turn for the same entity (TurnController scans AliveMonsters once).
    /// </summary>
    public void RecordSeen(string speciesId)
    {
        GetOrCreate(speciesId).SeenCount++;
    }

    /// <summary>
    /// Record a combat engagement (player attacked this species OR this species attacked player).
    /// Also ensures SeenCount >= 1 since combat implies the player has seen the monster.
    /// </summary>
    public void RecordEngaged(string speciesId)
    {
        var entry = GetOrCreate(speciesId);
        entry.EngagedCount++;
        // Implied: if you're fighting it, you've seen it.
        if (entry.SeenCount == 0)
            entry.SeenCount = 1;
    }

    /// <summary>
    /// Record that the player killed a monster of this species.
    /// Also ensures SeenCount and EngagedCount are at least 1.
    /// </summary>
    public void RecordKilled(string speciesId)
    {
        var entry = GetOrCreate(speciesId);
        entry.KilledCount++;
        if (entry.SeenCount == 0) entry.SeenCount = 1;
        if (entry.EngagedCount == 0) entry.EngagedCount = 1;
    }

    /// <summary>
    /// Record that the player experienced a notable trait from this species
    /// (e.g., "plague_carrier" when plagued, "swarm_ai" when the swarm retargets).
    /// Major traits can unlock Tier 3 immediately — see MonsterKnowledgeEntry.Tier.
    /// </summary>
    public void RecordTrait(string speciesId, string trait)
    {
        GetOrCreate(speciesId).TraitsDiscovered.Add(trait);
    }

    /// <summary>Reset all knowledge. Call on new game start.</summary>
    public void Reset() => _entries.Clear();

    // ── Read API ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Get the knowledge entry for a species, or a default empty entry if never seen.
    /// </summary>
    public MonsterKnowledgeEntry GetEntry(string speciesId)
        => _entries.TryGetValue(speciesId, out var e) ? e : new MonsterKnowledgeEntry();

    /// <summary>
    /// Build a tier-gated MonsterInfoView from the current knowledge entry and the
    /// species' MonsterDefinition (for stat label computation).
    ///
    /// Fields are null when not yet unlocked at the current tier.
    /// </summary>
    public MonsterInfoView GetInfoView(string speciesId, MonsterDefinition def)
    {
        var entry = GetEntry(speciesId);
        var tier = entry.Tier;

        // Always shown: name + tier
        if (tier == KnowledgeTier.Unknown)
        {
            return new MonsterInfoView(
                Name: def.Name ?? speciesId,
                Tier: KnowledgeTier.Unknown,
                FactionLabel: null,
                RoleLabel: null,
                SpeedLabel: null,
                DurabilityLabel: null,
                DamageLabel: null,
                AccuracyLabel: null,
                EvasionLabel: null,
                SpecialWarnings: Array.Empty<string>(),
                AdviceLine: null);
        }

        // Tier 1+ (Observed): faction, role, speed
        string? factionLabel = tier >= KnowledgeTier.Observed ? GetFactionLabel(def) : null;
        string? roleLabel    = tier >= KnowledgeTier.Observed ? GetRoleLabel(def) : null;
        string? speedLabel   = tier >= KnowledgeTier.Observed ? GetSpeedLabel(def) : null;

        // Tier 2+ (Battled): durability, damage, accuracy, evasion
        string? durabilityLabel = tier >= KnowledgeTier.Battled ? GetDurabilityLabel(def) : null;
        string? damageLabel     = tier >= KnowledgeTier.Battled ? GetDamageLabel(def) : null;
        string? accuracyLabel   = tier >= KnowledgeTier.Battled ? GetAccuracyLabel(def) : null;
        string? evasionLabel    = tier >= KnowledgeTier.Battled ? GetEvasionLabel(def) : null;

        // Tier 3+ (Understood): warnings, advice
        IReadOnlyList<string> warnings = tier >= KnowledgeTier.Understood
            ? GetSpecialWarnings(def, entry)
            : Array.Empty<string>();
        string? adviceLine = tier >= KnowledgeTier.Understood ? GetAdviceLine(def, entry) : null;

        return new MonsterInfoView(
            Name: def.Name ?? speciesId,
            Tier: tier,
            FactionLabel: factionLabel,
            RoleLabel: roleLabel,
            SpeedLabel: speedLabel,
            DurabilityLabel: durabilityLabel,
            DamageLabel: damageLabel,
            AccuracyLabel: accuracyLabel,
            EvasionLabel: evasionLabel,
            SpecialWarnings: warnings,
            AdviceLine: adviceLine);
    }

    // ── Internal helpers ───────────────────────────────────────────────────────

    private MonsterKnowledgeEntry GetOrCreate(string speciesId)
    {
        if (!_entries.TryGetValue(speciesId, out var entry))
        {
            entry = new MonsterKnowledgeEntry();
            _entries[speciesId] = entry;
        }
        return entry;
    }

    // ── Stat label computation (thresholds match balance/knowledge_config.py exactly) ──

    /// <summary>
    /// Durability = maxHp + armorClass * 5.
    /// Thresholds: fragile < 20, sturdy < 40, very tough < 70, monstrous >= 70.
    /// </summary>
    private static string? GetDurabilityLabel(MonsterDefinition def)
    {
        var stats = def.Stats;
        if (stats == null) return null;

        // Defense in YAML maps to BaseDefense on Fighter. Use it directly for the label calculation
        // rather than instantiating a Fighter — the definition stats are the canonical source here.
        int durability = stats.Hp + stats.Defense * 5;

        return durability switch
        {
            < 20 => "Fragile",
            < 40 => "Sturdy",
            < 70 => "Very Tough",
            _    => "Monstrous",
        };
    }

    /// <summary>
    /// Damage = average DPR using midpoint of damage range.
    /// Thresholds: light < 4, moderate < 8, heavy < 14, brutal >= 14.
    /// </summary>
    private static string? GetDamageLabel(MonsterDefinition def)
    {
        var stats = def.Stats;
        if (stats == null) return null;

        // PoC: avg_damage = ((damage_min + damage_max) / 2) + power
        double avgDamage = (stats.DamageMin + stats.DamageMax) / 2.0 + stats.Power;

        return avgDamage switch
        {
            < 4  => "Light",
            < 8  => "Moderate",
            < 14 => "Heavy",
            _    => "Brutal",
        };
    }

    /// <summary>
    /// Speed relative to base (1.0). Uses MonsterDefinition.SpeedBonus directly.
    /// Thresholds: sluggish < 0.6, normal < 1.2, fast < 1.8, lightning fast >= 1.8.
    /// 0.0 (default / unset) = 1.0x speed (Normal).
    /// </summary>
    private static string? GetSpeedLabel(MonsterDefinition def)
    {
        // SpeedBonus == 0 means default speed (1.0x) in the YAML schema.
        // We treat 0 as 1.0 to match PoC behavior.
        double speed = def.SpeedBonus > 0 ? def.SpeedBonus : 1.0;

        return speed switch
        {
            < 0.6 => "Sluggish",
            < 1.2 => "Normal",
            < 1.8 => "Fast",
            _     => "Lightning Fast",
        };
    }

    /// <summary>
    /// Accuracy from fighter stat.
    /// Thresholds (from PoC): <= 1 often misses, <= 3 usually hits, > 3 rarely misses.
    /// </summary>
    private static string? GetAccuracyLabel(MonsterDefinition def)
    {
        var stats = def.Stats;
        if (stats == null) return null;

        return stats.Accuracy switch
        {
            <= 1 => "Often Misses",
            <= 3 => "Usually Hits",
            _    => "Rarely Misses",
        };
    }

    /// <summary>
    /// Evasion from fighter stat.
    /// Thresholds (from PoC): <= 1 easy to hit, <= 2 average evasion, > 2 hard to hit.
    /// </summary>
    private static string? GetEvasionLabel(MonsterDefinition def)
    {
        var stats = def.Stats;
        if (stats == null) return null;

        return stats.Evasion switch
        {
            <= 1 => "Easy to Hit",
            <= 2 => "Average Evasion",
            _    => "Hard to Hit",
        };
    }

    /// <summary>
    /// Faction label from the definition's faction field.
    /// Capitalizes the faction name. "neutral" → null (no faction worth displaying).
    /// </summary>
    private static string? GetFactionLabel(MonsterDefinition def)
    {
        var faction = def.Faction;
        if (string.IsNullOrEmpty(faction) || faction == "neutral") return null;
        // Capitalize first letter for display
        return char.ToUpper(faction[0]) + faction[1..];
    }

    /// <summary>
    /// Role label derived from monster tags. Checks common archetypes in priority order.
    /// Mirrors PoC's _get_role_label which reads tags and special_abilities.
    /// </summary>
    private static string? GetRoleLabel(MonsterDefinition def)
    {
        var tags = def.Tags;
        if (tags == null) return null;

        if (tags.Contains("boss"))       return "Boss";
        if (tags.Contains("swarm"))      return "Swarm";
        if (tags.Contains("mindless"))   return "Mindless";
        if (tags.Contains("venomous"))   return "Venomous";
        if (tags.Contains("elite"))      return "Elite";

        // Infer from name patterns — matches PoC fallback behavior
        var nameLower = (def.Name ?? "").ToLowerInvariant();
        if (nameLower.Contains("brute"))      return "Brute";
        if (nameLower.Contains("chieftain"))  return "Leader";
        if (nameLower.Contains("scout"))      return "Scout";
        if (nameLower.Contains("veteran"))    return "Elite";

        return null;
    }

    /// <summary>
    /// Tier 3 warning strings based on known traits and monster definition.
    /// </summary>
    private static IReadOnlyList<string> GetSpecialWarnings(MonsterDefinition def, MonsterKnowledgeEntry entry)
    {
        var warnings = new List<string>();

        if (entry.TraitsDiscovered.Contains("plague_carrier"))
            warnings.Add("Carries the Plague of Restless Death");

        if (entry.TraitsDiscovered.Contains("swarm_ai"))
            warnings.Add("Swarm behavior: retargets when adjacent to multiple foes");

        // Always-visible warnings based on definition tags (at tier 3 they're confirmed, not suspected)
        var tags = def.Tags;
        if (tags != null && tags.Contains("plague_carrier") && !entry.TraitsDiscovered.Contains("plague_carrier"))
            warnings.Add("Suspected plague carrier");

        return warnings;
    }

    /// <summary>
    /// Short tactical advice derived from monster traits and definition.
    /// </summary>
    private static string? GetAdviceLine(MonsterDefinition def, MonsterKnowledgeEntry entry)
    {
        if (entry.TraitsDiscovered.Contains("plague_carrier") ||
            (def.Tags != null && def.Tags.Contains("plague_carrier")))
            return "Avoid getting hit. Cure plague immediately with antidotes.";

        if (entry.TraitsDiscovered.Contains("swarm_ai") ||
            (def.Tags != null && def.Tags.Contains("swarm")))
            return "Avoid being adjacent alongside other enemies; retargets chaotically.";

        // Speed-based advice
        double speed = def.SpeedBonus > 0 ? def.SpeedBonus : 1.0;
        if (speed >= 1.8)
            return "Very fast enemy. Build momentum slowly or use crowd control.";

        if (def.Tags != null && def.Tags.Contains("regenerating"))
            return "Kill quickly before it regenerates. Focus fire is effective.";

        return null;
    }
}
