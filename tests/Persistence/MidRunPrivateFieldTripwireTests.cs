using System.Reflection;
using UnderWarden.Logic.Persistence.MidRun;
using NUnit.Framework;

namespace UnderWarden.Tests.Persistence;

/// <summary>
/// Tripwire for the private-field audit (docs/systems/midrun_private_field_audit.md). The
/// completeness gate round-trips PUBLIC component state but cannot see PRIVATE fields; the audit
/// classified every private field once, by hand. This test locks that in: it reflects over every
/// registered component and asserts the set of non-public, non-backing instance fields is EXACTLY
/// the audited allowlist. A newly-added private field fails here — forcing a re-audit + a
/// serialization decision — instead of silently diverging on load.
/// </summary>
[TestFixture]
public class MidRunPrivateFieldTripwireTests
{
    // The audited allowlist, "Type.field" — every entry is COVERED per the audit doc. Keep this
    // adjacent to the assertion; update it (and the audit doc) only after re-classifying a new field.
    private static readonly HashSet<string> AuditedPrivateFields = new(StringComparer.Ordinal)
    {
        "Inventory._items",                         // COVERED via Items → InventoryDto.ItemIds
        "SpeedBonusTracker._attackCounter",         // COVERED via AttackCounter / RestoreMomentum
        "SpeedBonusTracker._lastTargetId",          // COVERED via LastTargetId / RestoreMomentum
        "StatusImmunityComponent._immunities",      // COVERED via Immunities / ctor
        "AutoExploreState._positionHistory",        // COVERED via PositionHistorySnapshot / RestorePositionHistory
        "AutoExploreState._positionCount",          // COVERED (implicit in serialized PositionHistory length)
    };

    [Test]
    public void RegisteredComponents_HaveOnlyAuditedPrivateFields()
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var codec in MidRunComponentRegistry.All)
        {
            foreach (var f in codec.ComponentType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.Name.Contains("k__BackingField")) continue;   // auto-property backing — covered by the gate
                found.Add($"{codec.ComponentType.Name}.{f.Name}");
            }
        }

        var unaudited = found.Except(AuditedPrivateFields).OrderBy(s => s).ToList();
        var vanished = AuditedPrivateFields.Except(found).OrderBy(s => s).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(unaudited, Is.Empty,
                "New private component field(s) not in the audit. Re-audit + decide serialization " +
                "(see docs/systems/midrun_private_field_audit.md and MidRunComponentRegistrations), then " +
                $"add to AuditedPrivateFields: {string.Join(", ", unaudited)}");
            Assert.That(vanished, Is.Empty,
                $"Audited private field(s) no longer exist — remove from the allowlist + audit doc: {string.Join(", ", vanished)}");
        });
    }
}
