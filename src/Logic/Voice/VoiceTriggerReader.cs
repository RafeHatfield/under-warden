using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Voice;

/// <summary>
/// Derives voice trigger keys from what is observable at a turn commit (M1.5b trigger bus). Pure logic —
/// no Godot — so the derivation is headlessly testable; the presentation layer only calls this each
/// <c>TurnCompleted</c> and fires <see cref="VoiceScheduler.TryDeliver"/>.
///
/// Emits SPECIFIC, authored pool keys (Option A, ruled 2026-08-22): "species_first_sight.&lt;typeId&gt;",
/// "hp_threshold.25|10|1", "trap_first.&lt;trapType&gt;", "long_idle". The scheduler resolves each key's
/// tier by family prefix. Edge-triggered where a sustained condition would otherwise spam. Never draws
/// randomness and never touches the gameplay Rng.
///
/// Coverage note: this pass derives the families that fire in normal play from existing signals. Other
/// authored families (on_death, kill_streak_clean, region_first_entry, item_identified,
/// overnight_identified, spell_break_used, between_runs, past_sasha_encounter) have tiers + pools but no
/// derivation yet — future work; the tiers↔pools cross-validation still guards them.
/// </summary>
public sealed class VoiceTriggerReader
{
    // HP fraction bands → the authored hp_threshold.<band> pool keys. Most-severe first.
    private static readonly (float Frac, int Band)[] HpBands = { (0.01f, 1), (0.10f, 10), (0.25f, 25) };

    private int? _lastHpBand;                                             // most-severe band already announced; null = re-armed
    private readonly HashSet<string> _seenSpecies = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenTrapTypes = new(StringComparer.Ordinal);

    /// <summary>Reset all edge state — called at the start of a new run (depth 1).</summary>
    public void Reset()
    {
        _lastHpBand = null;
        _seenSpecies.Clear();
        _seenTrapTypes.Clear();
    }

    /// <summary>
    /// The specific trigger keys raised by this committed turn, in a stable order. May be empty. The
    /// scheduler resolves priority/cooldown/bags across whatever is returned.
    /// </summary>
    public IReadOnlyList<string> Read(TurnResult result, GameState state)
    {
        var keys = new List<string>();

        // hp_threshold.<band> — edge-triggered on the home body entering a MORE-severe band (alive).
        var body = state.PlayerFighter;
        int? band = null;
        if (body.Hp > 0 && body.MaxHp > 0)
        {
            float frac = (float)body.Hp / body.MaxHp;
            foreach (var (f, b) in HpBands) if (frac <= f) { band = b; break; }   // most-severe wins
        }
        if (band != null && (_lastHpBand == null || band < _lastHpBand))
            keys.Add($"hp_threshold.{band}");
        _lastHpBand = band;   // null when recovered above the top band → re-arms

        // trap_first.<type> — first time the player's own body triggers each trap type this run.
        foreach (var e in result.Events)
            if (e is TrapTriggeredEvent t && t.TargetId == state.Player.Id
                && !string.IsNullOrEmpty(t.Source) && _seenTrapTypes.Add(t.Source))
                keys.Add($"trap_first.{t.Source}");

        // long_idle — the player waited/skipped this turn.
        if (result.Events.Any(e => e is WaitEvent or SkipTurnEvent))
            keys.Add("long_idle");

        // species_first_sight.<typeId> — a species entering the FOV for the first time this run.
        foreach (var m in state.Monsters)
        {
            if (!state.Map.IsVisible(m.X, m.Y)) continue;
            var species = m.Get<SpeciesTag>()?.TypeId;
            if (species != null && _seenSpecies.Add(species))
                keys.Add($"species_first_sight.{species}");
        }

        return keys;
    }
}
