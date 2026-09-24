using System.Reflection;
using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Knowledge;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Parallel tripwire for the mid-run SUBSYSTEMS (the component audit did not cover these). Each
/// subsystem the save serializes is listed here with its private instance fields; a new private field
/// fails the test, forcing a serialization decision instead of a silent divergence on load. Weighing*
/// is intentionally absent — it is not serialized yet (4a.3b-3) and is fenced by the SaveMidRun guard.
/// </summary>
[TestFixture]
public class MidRunSubsystemPrivateFieldTripwireTests
{
    // The subsystem types the mid-run save reconstructs.
    private static readonly Type[] SerializedSubsystems =
    {
        typeof(GameMap), typeof(MonsterKnowledgeSystem), typeof(GroundHazardManager),
        typeof(IdentificationRegistry), typeof(AppearancePool), typeof(MuralTracker),
        typeof(PityTracker), typeof(BoonTracker),
        typeof(UnderWarden.Logic.Endgame.WeighingState),
        typeof(UnderWarden.Logic.Endgame.WeighingArena),
        typeof(WeighingAuditRegistry),
        typeof(UnderWarden.Logic.Voice.VoiceScheduler),
    };

    // Every private instance field of the above, "Type.field" — all COVERED by the serializer.
    private static readonly HashSet<string> AuditedSubsystemFields = new(StringComparer.Ordinal)
    {
        "GameMap._walkable", "GameMap._tiles", "GameMap._visible", "GameMap._explored",
        "GameMap._theme", "GameMap._entities", "GameMap._propCells",
        "MonsterKnowledgeSystem._entries",
        "GroundHazardManager._hazards",
        "IdentificationRegistry._identified", "IdentificationRegistry._decidedUnidentified",
        "AppearancePool._descriptors", "AppearancePool._mysterySprites",
        "AppearancePool._potionTypes", "AppearancePool._scrollTypes",
        "AppearancePool._wandTypes", "AppearancePool._ringTypes",
        "MuralTracker._usedThisFloor", "MuralTracker._usedThisRun",
        "PityTracker._counters", "PityTracker._pendingHardInjects",
        "PityTracker._lootItemCounts", "PityTracker._hardPityFireCount",
        // WeighingState is all public props (no private fields).
        "WeighingArena._anchors",
        "WeighingAuditRegistry._sequences",
        // VoiceScheduler run state — all SERIALIZE-class except the two RECONSTRUCT deps.
        "VoiceScheduler._registry",             // RECONSTRUCT — caller-provided VoiceLineRegistry, never serialized
        "VoiceScheduler._meta",                 // RECONSTRUCT — caller-provided VoiceTierMetadata, never serialized
        "VoiceScheduler._rng",                  // COVERED via (RngSeed, RngCallCount)
        "VoiceScheduler._bags",                 // COVERED via VoiceBagDto[] (remaining draw order per family)
        "VoiceScheduler._fired",                // COVERED via Fired[]
        "VoiceScheduler._history",              // COVERED via VoiceHistoryDto[]
        "VoiceScheduler._currentFloorSilenced", // COVERED via CurrentFloorSilenced
        "VoiceScheduler._lastDeliveredTurn",    // COVERED via LastDeliveredTurn
    };

    [Test]
    public void SerializedSubsystems_HaveOnlyAuditedPrivateFields()
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in SerializedSubsystems)
            foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.Name.Contains("k__BackingField")) continue;
                found.Add($"{t.Name}.{f.Name}");
            }

        var unaudited = found.Except(AuditedSubsystemFields).OrderBy(s => s).ToList();
        var vanished = AuditedSubsystemFields.Except(found).OrderBy(s => s).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(unaudited, Is.Empty,
                "New private subsystem field(s) not in the mid-run audit — decide serialization in " +
                $"MidRunSerializer, then add here: {string.Join(", ", unaudited)}");
            Assert.That(vanished, Is.Empty,
                $"Audited subsystem field(s) no longer exist — remove from the allowlist: {string.Join(", ", vanished)}");
        });
    }
}
