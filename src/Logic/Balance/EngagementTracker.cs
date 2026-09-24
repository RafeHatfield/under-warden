using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Accumulates per-engagement combat into the six bounded per-death lever signals (0c).
/// Extracted from DungeonRunHarness.FloorCombatTracker so both the scenario harness (Layer-1
/// controlled engagements) and the dungeon soak (Layer-2 economy) share a single source of truth
/// for per-death capture. In a controlled scenario DistinctAttackers equals the actual composition
/// size, making the lever attribution trustworthy — the whole reason this path exists for Layer-1.
///
/// Same API as the dungeon harness's private FloorCombatTracker; DungeonRunHarness delegates to
/// this class. Pure bookkeeping over the turn-event stream; no balance opinions.
/// </summary>
internal sealed class EngagementTracker
{
    private readonly int _playerId;

    private readonly Dictionary<int, int> _monsterHits        = new(); // killer's LANDED hits on player
    private readonly Dictionary<int, int> _monsterSwings      = new(); // killer's ATTEMPTED swings on player
    private readonly Dictionary<int, int> _monsterDamage      = new(); // total landed damage to player per attacker
    private readonly Dictionary<int, int> _monsterFirstHitTurn = new(); // turn of killer's first landed hit
    private readonly Dictionary<int, int> _playerHitsOnMonster = new(); // player's LANDED hits per monster
    private readonly HashSet<int> _distinctAttackers           = new(); // monsters that dealt the player any damage
    private readonly List<int> _killHitCounts                  = new(); // player hits per monster killed (ttk)

    public int DamageTaken { get; private set; }
    public int CombatTurns { get; private set; }
    public bool EscalatorPresent { get; private set; }
    public bool SpikePresent { get; private set; }
    public int? EscalatorNeutralizedAtTurn { get; private set; }

    public EngagementTracker(int playerId, GameState state)
    {
        _playerId = playerId;
        foreach (var m in state.Monsters)
        {
            var a = m.Get<ThreatArchetypeTag>()?.Archetype;
            if (a is ThreatArchetype.Escalator or ThreatArchetype.Fused) EscalatorPresent = true;
            if (a is ThreatArchetype.Spike or ThreatArchetype.Fused) SpikePresent = true;
        }
    }

    public double AvgHitsToKill => _killHitCounts.Count > 0 ? _killHitCounts.Average() : 0.0;

    public void IngestTurn(IEnumerable<TurnEvent> events, int turnNumber, GameState state)
    {
        bool playerInCombat = false;

        foreach (var evt in events)
        {
            switch (evt)
            {
                // Monster melee against the player — misses still emit, so this is the hit-rate denominator.
                case AttackEvent atk when atk.TargetId == _playerId:
                    playerInCombat = true;
                    Bump(_monsterSwings, atk.ActorId);
                    if (atk.Hit)
                        RecordLandedHitOnPlayer(atk.ActorId, atk.Damage, turnNumber);
                    break;

                // Player's own blow — landed hits feed ttk + the counterattack (weapon-speed) signal.
                case AttackEvent atk when atk.ActorId == _playerId:
                    playerInCombat = true;
                    if (atk.Hit) Bump(_playerHitsOnMonster, atk.TargetId);
                    break;

                // Soul Bolt: the lich's spike blow. Count as a landed hit so a soul-bolt kill
                // attributes hits-to-down correctly even though it's not a melee AttackEvent.
                case SoulBoltEvent sb when sb.TargetId == _playerId:
                    playerInCombat = true;
                    Bump(_monsterSwings, sb.ActorId);
                    RecordLandedHitOnPlayer(sb.ActorId, sb.Damage, turnNumber);
                    break;

                // DOT / bleed: status ticks count toward HP lost (floor vitals) but NOT toward
                // hits-to-down, which measures the killer's actual blows.
                case DotDamageEvent dot when dot.EntityId == _playerId:
                    DamageTaken += dot.Damage;
                    break;
                case BleedTickEvent bleed when bleed.ActorId == _playerId:
                    DamageTaken += bleed.Damage;
                    break;

                // A monster died: record the player's hits-to-kill it and the escalator clock.
                case DeathEvent d when d.ActorId != _playerId:
                    _killHitCounts.Add(_playerHitsOnMonster.GetValueOrDefault(d.ActorId));
                    if (IsEscalatorId(d.ActorId, state))
                    {
                        EscalatorPresent = true;
                        EscalatorNeutralizedAtTurn ??= turnNumber;
                    }
                    break;
            }
        }

        if (playerInCombat) CombatTurns++;
    }

    /// <summary>
    /// Build the per-death diagnostic record. Call once when the player dies. The six lever signals
    /// are populated from accumulated per-monster tracking; in a controlled scenario the composition
    /// is fixed so DistinctAttackers reflects the actual encounter density, not bot-pulled chaos.
    /// </summary>
    public PlayerDeathRecord BuildDeathRecord(int depth, int killerId, int turnAtDeath, GameState state)
    {
        int hits    = _monsterHits.GetValueOrDefault(killerId);
        int swings  = _monsterSwings.GetValueOrDefault(killerId);
        int damage  = _monsterDamage.GetValueOrDefault(killerId);
        int firstHit = _monsterFirstHitTurn.TryGetValue(killerId, out var f) ? f : turnAtDeath;

        var killer = killerId == -1 ? null : state.Monsters.FirstOrDefault(m => m.Id == killerId);

        return new PlayerDeathRecord
        {
            Depth                = depth,
            KillerId             = killerId,
            KillerTypeId         = killer?.Get<SpeciesTag>()?.TypeId,
            KillerArchetype      = killer?.Get<ThreatArchetypeTag>()?.Archetype,
            HitsToDown           = hits,
            DamagePerHit         = hits > 0 ? (double)damage / hits : 0.0,
            KillerHitRate        = swings > 0 ? (double)hits / swings : 0.0,
            CounterattacksLanded = _playerHitsOnMonster.GetValueOrDefault(killerId),
            DistinctAttackers    = _distinctAttackers.Count,
            // Inclusive span: 1 hit over 1 turn → frequency 1.0 (attack every turn).
            EngagementTurns      = Math.Max(1, turnAtDeath - firstHit + 1),
        };
    }

    private void RecordLandedHitOnPlayer(int attackerId, int damage, int turnNumber)
    {
        Bump(_monsterHits, attackerId);
        _monsterDamage[attackerId] = _monsterDamage.GetValueOrDefault(attackerId) + damage;
        if (!_monsterFirstHitTurn.ContainsKey(attackerId))
            _monsterFirstHitTurn[attackerId] = turnNumber;
        if (damage > 0)
        {
            DamageTaken += damage;
            _distinctAttackers.Add(attackerId);
        }
    }

    private static void Bump(Dictionary<int, int> d, int key) => d[key] = d.GetValueOrDefault(key) + 1;

    private static bool IsEscalator(Entity m)
        => m.Get<ThreatArchetypeTag>()?.Archetype is ThreatArchetype.Escalator or ThreatArchetype.Fused;

    private static bool IsEscalatorId(int id, GameState state)
    {
        var m = state.Monsters.FirstOrDefault(e => e.Id == id);
        return m != null && IsEscalator(m);
    }
}
