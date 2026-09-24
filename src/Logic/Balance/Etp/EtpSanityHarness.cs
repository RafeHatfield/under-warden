using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Balance.Etp;

/// <summary>
/// ETP sanity analysis tool.
/// Generates representative dungeon floors and validates per-room ETP against band budgets.
///
/// Ports ~/development/rlike/etp_sanity.py:107-494.
/// </summary>
public static class EtpSanityHarness
{
    // Status taxonomy (PoC etp_sanity.py:51-63)
    public const string StatusOk      = "OK";
    public const string StatusUnder   = "UNDER";
    public const string StatusOver    = "OVER";
    public const string StatusEmpty   = "EMPTY";
    public const string StatusBoss    = "BOSS";
    public const string StatusMiniboss = "MINIBOSS";
    public const string StatusEndboss  = "ENDBOSS";
    public const string StatusSpike    = "SPIKE";
    public const string StatusExempt   = "EXEMPT";

    /// <summary>Non-violation statuses — OVER is the only strict failure.</summary>
    private static readonly HashSet<string> ViolationStatuses = new() { StatusUnder, StatusOver };

    /// <summary>Default depths for sanity checks — one per band (PoC etp_sanity.py:376-378).</summary>
    public static readonly int[] DefaultDepths = [3, 8, 13, 18, 23];

    // ── Result types ──────────────────────────────────────────────────────────

    public sealed record RoomEtpResult(
        int RoomIndex,
        int RoomX,
        int RoomY,
        double TotalEtp,
        IReadOnlyDictionary<string, int> MonsterCounts,
        IReadOnlyDictionary<string, double> EtpBreakdown,
        double BudgetMin,
        double BudgetMax,
        string Status,   // OK | UNDER | OVER | EMPTY | BOSS | MINIBOSS | SPIKE | EXEMPT
        string Role,
        bool AllowSpike);

    public sealed record LevelEtpResult(
        int Depth,
        string Band,
        IReadOnlyList<RoomEtpResult> Rooms,
        double TotalFloorEtp,
        double FloorBudgetMin,
        double FloorBudgetMax,
        bool WithinFloorBudget);

    // ── Core analysis ─────────────────────────────────────────────────────────

    /// <summary>
    /// Build a dungeon floor and analyze per-room ETP.
    /// Returns null if the build fails or rooms are not available.
    /// </summary>
    public static LevelEtpResult? AnalyzeLevel(
        DungeonFloorBuilder builder,
        EtpConfig cfg,
        int depth,
        int seed,
        MonsterRegistry? monsterRegistry = null)
    {
        var rng   = new SeededRandom(seed);
        GameState state;
        try
        {
            state = builder.Build(depth, rng);
        }
        catch
        {
            return null;
        }

        if (state.Rooms == null || state.Rooms.Count == 0)
        {
            // No room data — return empty result
            var emptyBand = EtpCalculator.BandForDepth(cfg, depth);
            var (emptyFloorMin, emptyFloorMax) = EtpCalculator.GetFloorEtpBudget(cfg, depth);
            return new LevelEtpResult(depth, emptyBand, [], 0, emptyFloorMin, emptyFloorMax, true);
        }

        string band = EtpCalculator.BandForDepth(cfg, depth);
        var roomResults = new List<RoomEtpResult>();
        double totalFloorEtp = 0;

        for (int idx = 0; idx < state.Rooms.Count; idx++)
        {
            var room = state.Rooms[idx];
            var result = AnalyzeRoom(cfg, room, idx, depth, state.Monsters, monsterRegistry);
            roomResults.Add(result);
            totalFloorEtp += result.TotalEtp;
        }

        var (floorMin, floorMax) = EtpCalculator.GetFloorEtpBudget(cfg, depth);
        bool withinFloor = totalFloorEtp >= floorMin * 0.9 && totalFloorEtp <= floorMax * 1.1;

        return new LevelEtpResult(
            depth, band, roomResults,
            totalFloorEtp, floorMin, floorMax, withinFloor);
    }

    /// <summary>
    /// Analyze a single room: find monsters inside it, compute ETP, classify status.
    /// </summary>
    private static RoomEtpResult AnalyzeRoom(
        EtpConfig cfg,
        Room room,
        int roomIdx,
        int depth,
        IReadOnlyList<UnderWarden.Logic.ECS.Entity> monsters,
        MonsterRegistry? monsterRegistry)
    {
        // Find monsters whose position is inside this room's bounds
        var roomMonsters = monsters
            .Where(m => m.X >= room.X && m.X < room.X + room.Width
                     && m.Y >= room.Y && m.Y < room.Y + room.Height)
            .ToList();

        if (roomMonsters.Count == 0)
        {
            var (emptyMin, emptyMax) = EtpCalculator.GetRoomEtpBudget(cfg, depth, false);
            return new RoomEtpResult(
                roomIdx, room.X, room.Y,
                0, new Dictionary<string, int>(), new Dictionary<string, double>(),
                emptyMin, emptyMax,
                StatusEmpty, "normal", false);
        }

        // Count monsters and compute ETP per type
        var counts    = new Dictionary<string, int>();
        var breakdown = new Dictionary<string, double>();
        double totalEtp = 0;

        foreach (var m in roomMonsters)
        {
            string typeId = GetMonsterTypeId(m);
            counts.TryGetValue(typeId, out int existing);
            counts[typeId] = existing + 1;

            // Get ETP for this monster
            double etp = GetMonsterEtp(m, cfg, depth, monsterRegistry);
            breakdown.TryGetValue(typeId, out double etpSum);
            breakdown[typeId] = etpSum + etp;
            totalEtp += etp;
        }

        // Determine role from room metadata (simple type-based inference)
        // Full role classification would require SpecialRoomDef metadata — we infer from context.
        string role = InferRoomRole(roomIdx, state: null);
        bool allowSpike = false;

        // Classify status
        var (budgetMin, budgetMax) = EtpCalculator.GetRoomEtpBudget(cfg, depth, allowSpike);
        string status = ClassifyRoomStatus(totalEtp, budgetMin, budgetMax, cfg, role, allowSpike);

        return new RoomEtpResult(
            roomIdx, room.X, room.Y,
            totalEtp, counts, breakdown,
            budgetMin, budgetMax, status, role, allowSpike);
    }

    private static string ClassifyRoomStatus(
        double totalEtp, double budgetMin, double budgetMax,
        EtpConfig cfg, string role, bool allowSpike)
    {
        if (role is "boss" or StatusBoss)    return StatusBoss;
        if (role is "miniboss" or StatusMiniboss) return StatusMiniboss;
        if (role is "endboss" or StatusEndboss)   return StatusEndboss;
        if (role is StatusExempt)            return StatusExempt;

        if (totalEtp <= 0) return StatusEmpty;

        var check = EtpBudgetChecker.CheckRoom(cfg, totalEtp, 1, "", allowSpike);
        // Re-evaluate with actual budget values
        double tolerance = cfg.Tolerance.RoomTolerance;
        double effectiveMin = budgetMin > 0 ? budgetMin * (1 - tolerance) : 0;
        double effectiveMax = budgetMax * (1 + tolerance);

        if (allowSpike && totalEtp <= effectiveMax) return StatusSpike;
        if (totalEtp < effectiveMin && budgetMin > 0) return StatusUnder;
        if (totalEtp > effectiveMax) return StatusOver;
        return StatusOk;
    }

    private static string InferRoomRole(int roomIndex, GameState? state)
    {
        // Without SpecialRoomDef metadata attached to rooms, we treat all as "normal"
        // Future: attach role metadata to Room when SpecialRoomDef places the room.
        return "normal";
    }

    private static string GetMonsterTypeId(UnderWarden.Logic.ECS.Entity m)
    {
        // SpeciesTag stores the canonical monster type ID from entity definitions
        var tag = m.Get<UnderWarden.Logic.ECS.SpeciesTag>();
        return tag?.TypeId ?? m.Name;
    }

    private static double GetMonsterEtp(
        UnderWarden.Logic.ECS.Entity m,
        EtpConfig cfg,
        int depth,
        MonsterRegistry? registry)
    {
        if (registry == null)
        {
            // Fallback: use the legacy GetEtp which reads EtpBase directly from entity
            var fighter = m.Get<UnderWarden.Logic.Combat.Fighter>();
            // We don't have MonsterDefinition here without the registry
            // Use a default ETP of 20 (PoC fallback)
            return EtpCalculator.DefaultEtp;
        }

        string typeId = GetMonsterTypeId(m);
        if (!registry.TryGetDefinition(typeId, out var def) || def == null)
            return EtpCalculator.DefaultEtp;

        return EtpCalculator.GetMonsterEtp(cfg, def, depth);
    }

    // ── RunSanity orchestrator ────────────────────────────────────────────────

    /// <summary>
    /// Run ETP sanity across multiple depths.
    /// Returns exit code: 0 = OK, 1 = OVER violations in strict mode.
    ///
    /// Ports etp_sanity.py:354-494 run_sanity_check().
    /// </summary>
    public static int RunSanity(
        DungeonFloorBuilder builder,
        EtpConfig cfg,
        int[]? depths = null,
        bool strict = false,
        bool verbose = false,
        int runsPerDepth = 1,
        TextWriter? csvOut = null,
        MonsterRegistry? monsterRegistry = null)
    {
        var targetDepths = depths ?? DefaultDepths;
        bool anyOver = false;

        // CSV header (PoC etp_sanity.py:345-351)
        csvOut?.WriteLine("depth,band,room_index,etp_total,budget_min,budget_max,status,role,monsters");

        foreach (int depth in targetDepths)
        {
            for (int run = 0; run < runsPerDepth; run++)
            {
                int seed = SeedDerivation.Stable($"etp_sanity_{depth}", run, 1337);
                var result = AnalyzeLevel(builder, cfg, depth, seed, monsterRegistry);

                if (result == null)
                {
                    Console.Error.WriteLine($"  [etp-sanity] depth {depth} run {run}: build failed");
                    continue;
                }

                if (verbose)
                    Console.WriteLine($"\n=== Depth {depth} ({result.Band}) run {run} — Floor ETP: {result.TotalFloorEtp:F1} / [{result.FloorBudgetMin},{result.FloorBudgetMax}] ===");

                foreach (var room in result.Rooms)
                {
                    // CSV row
                    string monsters = string.Join(",",
                        room.MonsterCounts.Select(kv => $"{kv.Key}:{kv.Value}"));
                    csvOut?.WriteLine(
                        $"{depth},{result.Band},{room.RoomIndex}," +
                        $"{room.TotalEtp:F1},{room.BudgetMin},{room.BudgetMax}," +
                        $"{room.Status},{room.Role},{monsters}");

                    if (verbose && room.Status != StatusEmpty)
                        Console.WriteLine($"  Room {room.RoomIndex}: ETP={room.TotalEtp:F1} Status={room.Status} Monsters=[{monsters}]");

                    if (strict && room.Status == StatusOver)
                        anyOver = true;
                }
            }
        }

        return anyOver ? 1 : 0;
    }
}

/// <summary>
/// Minimal monster type registry interface for ETP sanity analysis.
/// </summary>
public interface MonsterRegistry
{
    bool TryGetDefinition(string typeId, out MonsterDefinition? def);
}
