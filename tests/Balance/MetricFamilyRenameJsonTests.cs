using System.Text.Json;
using System.Text.Json.Nodes;
using UnderWarden.Logic.Balance;
using NUnit.Framework;

namespace UnderWarden.Tests.Balance;

/// <summary>
/// FIND-005 rename guard: the hits-based family was renamed
/// <c>AggregatedMetrics.H_PM/H_MP → TtkHits/TtdHits</c>, but the ON-DISK JSON keys
/// must stay <c>h_pm</c>/<c>h_mp</c> so the committed balance baseline and the
/// DepthReportLoader read/write path keep round-tripping. These tests fail loudly if a
/// future edit drops the <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/>
/// and silently mangles the key (the "MISFED trap").
/// </summary>
[TestFixture]
public class MetricFamilyRenameJsonTests
{
    // Mirror of DepthReportLoader.JsonOptions — the real read path.
    private static readonly JsonSerializerOptions LoaderOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
    };

    [Test]
    public void TtkHits_TtdHits_SerializeToStable_h_pm_h_mp_Keys()
    {
        var agg = new AggregatedMetrics
        {
            ScenarioId = "roundtrip",
            Depth      = 3,
            TtkHits    = 9.3,
            TtdHits    = 9.7,
        };

        string json = JsonSerializer.Serialize(agg, LoaderOptions);
        var node = JsonNode.Parse(json)!.AsObject();

        // On-disk keys must remain h_pm/h_mp (NOT ttk_hits/ttd_hits) for baseline stability.
        Assert.That(node.ContainsKey("h_pm"), Is.True, "hits-based TtkHits must serialize to on-disk key 'h_pm'");
        Assert.That(node.ContainsKey("h_mp"), Is.True, "hits-based TtdHits must serialize to on-disk key 'h_mp'");
        Assert.That(node.ContainsKey("ttk_hits"), Is.False, "the renamed C# symbol must not leak a new on-disk key");
        Assert.That((double)node["h_pm"]!, Is.EqualTo(9.3).Within(1e-9));
        Assert.That((double)node["h_mp"]!, Is.EqualTo(9.7).Within(1e-9));
    }

    [Test]
    public void OnDisk_h_pm_h_mp_DeserializeBackInto_TtkHits_TtdHits_NoSilentNull()
    {
        // Simulate a committed/legacy raw metrics file keyed by h_pm/h_mp.
        const string onDisk = """
        { "scenario_id": "roundtrip", "depth": 3, "h_pm": 9.3, "h_mp": 9.7 }
        """;

        var agg = JsonSerializer.Deserialize<AggregatedMetrics>(onDisk, LoaderOptions);

        Assert.That(agg, Is.Not.Null);
        // The MISFED guard: h_pm/h_mp must land in TtkHits/TtdHits, not silently drop to 0.
        Assert.That(agg!.TtkHits, Is.EqualTo(9.3).Within(1e-9), "on-disk 'h_pm' must map back into TtkHits");
        Assert.That(agg.TtdHits, Is.EqualTo(9.7).Within(1e-9), "on-disk 'h_mp' must map back into TtdHits");
    }
}
