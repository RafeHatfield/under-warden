namespace UnderWarden.Logic.Core;

/// <summary>
/// Type of ground hazard. Determines damage element, VFX colour, and toast wording.
/// </summary>
public enum HazardType { Fire, PoisonGas }

/// <summary>
/// A single ground-tile hazard. Persists on the map for MaxDuration turns, dealing
/// damage that decays linearly each tick: floor(BaseDamage × RemainingTurns / MaxDuration).
///
/// Fireball:     base=3, duration=3 → deals 3 / 2 / 1 on turns 1/2/3 AFTER the spell lands.
/// Dragon Fart:  base=6, duration=5 → deals 6 / 4 / 3 / 2 / 1 on turns 1–5 AFTER the spell lands.
///
/// JustPlaced: set when the hazard is created by a spell. TickEnvironment skips damage and
/// aging for JustPlaced hazards and clears the flag. This prevents the hazard from dealing
/// damage on the same turn the spell lands (the spell already dealt its own direct damage).
/// </summary>
public sealed class GroundHazard
{
    public HazardType Type           { get; }
    public int        X              { get; }
    public int        Y              { get; }
    public int        BaseDamage     { get; }
    public int        MaxDuration    { get; }
    public int        RemainingTurns { get; set; }

    /// <summary>
    /// True on the turn the hazard was created. TickEnvironment skips this hazard (no damage,
    /// no aging) and sets it to false. Prevents double-damage on the spell-cast turn.
    /// </summary>
    public bool JustPlaced { get; set; }

    public GroundHazard(HazardType type, int x, int y, int baseDamage, int maxDuration,
        bool justPlaced = true)
    {
        Type           = type;
        X              = x;
        Y              = y;
        BaseDamage     = baseDamage;
        MaxDuration    = maxDuration;
        RemainingTurns = maxDuration;
        JustPlaced     = justPlaced;
    }

    /// <summary>Damage dealt this tick. Linear decay: full on turn 1, zero after expiry.</summary>
    public int CurrentDamage => (int)(BaseDamage * RemainingTurns / (float)MaxDuration);
}

/// <summary>
/// Tracks all active ground hazards on the current floor.
/// Stored on GameState. Cleared automatically on floor transition (new GameState per floor).
///
/// One hazard per tile — adding a new hazard at an occupied tile replaces the old one.
/// </summary>
public sealed class GroundHazardManager
{
    private readonly Dictionary<(int X, int Y), GroundHazard> _hazards = new();

    /// <summary>Read-only view of all active hazards, keyed by tile position.</summary>
    public IReadOnlyDictionary<(int X, int Y), GroundHazard> Hazards => _hazards;

    /// <summary>
    /// Add or replace a hazard at (x, y). A fresh hazard always starts at full duration.
    /// Replaces any existing hazard at the same tile — no stacking.
    /// Pass justPlaced=false in tests that want immediate ticking.
    /// </summary>
    public void AddHazard(HazardType type, int x, int y, int baseDamage, int maxDuration,
        bool justPlaced = true)
    {
        _hazards[(x, y)] = new GroundHazard(type, x, y, baseDamage, maxDuration, justPlaced);
    }

    /// <summary>Remove all hazards with RemainingTurns ≤ 0.</summary>
    public void RemoveExpired()
    {
        foreach (var key in _hazards.Keys.Where(k => _hazards[k].RemainingTurns <= 0).ToList())
            _hazards.Remove(key);
    }

    /// <summary>Remove all hazards immediately. Called on floor transition (harness/testing).</summary>
    public void Clear() => _hazards.Clear();
}
