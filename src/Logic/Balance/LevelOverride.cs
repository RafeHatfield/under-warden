using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Per-floor procedural override loaded from level_templates.yaml.
/// Active types are fully typed. Deferred types (doors, traps, secret rooms, connectivity)
/// are stubbed as Dictionary&lt;string, object&gt; so they parse without error but are not processed.
/// </summary>
public sealed class LevelOverride
{
    [YamlMember(Alias = "parameters")]
    public GenerationParameters? Parameters { get; set; }

    [YamlMember(Alias = "guaranteed_spawns")]
    public GuaranteedSpawns? GuaranteedSpawns { get; set; }

    [YamlMember(Alias = "special_rooms")]
    public List<SpecialRoomDef> SpecialRooms { get; set; } = new();

    [YamlMember(Alias = "stairs")]
    public StairRules? Stairs { get; set; }

    [YamlMember(Alias = "encounter_budget")]
    public EncounterBudget? EncounterBudget { get; set; }

    // Deferred — parsed but not processed this milestone
    [YamlMember(Alias = "door_rules")]
    public Dictionary<string, string>? DoorRules { get; set; }

    [YamlMember(Alias = "trap_rules")]
    public Dictionary<string, string>? TrapRules { get; set; }

    [YamlMember(Alias = "secret_rooms")]
    public Dictionary<string, string>? SecretRooms { get; set; }

    [YamlMember(Alias = "connectivity")]
    public Dictionary<string, string>? Connectivity { get; set; }
}

public sealed class GenerationParameters
{
    [YamlMember(Alias = "max_rooms")]
    public int? MaxRooms { get; set; }

    [YamlMember(Alias = "min_room_size")]
    public int? MinRoomSize { get; set; } = null;

    [YamlMember(Alias = "max_room_size")]
    public int? MaxRoomSize { get; set; } = null;

    [YamlMember(Alias = "max_monsters_per_room")]
    public int MaxMonstersPerRoom { get; set; } = 3;

    [YamlMember(Alias = "max_items_per_room")]
    public int MaxItemsPerRoom { get; set; } = 2;

    [YamlMember(Alias = "map_width")]
    public int? MapWidth { get; set; }

    [YamlMember(Alias = "map_height")]
    public int? MapHeight { get; set; }
}

public sealed class GuaranteedSpawns
{
    [YamlMember(Alias = "mode")]
    public string Mode { get; set; } = "additional";

    [YamlMember(Alias = "monsters")]
    public List<SpawnEntry> Monsters { get; set; } = new();

    [YamlMember(Alias = "items")]
    public List<SpawnEntry> Items { get; set; } = new();

    [YamlMember(Alias = "equipment")]
    public List<SpawnEntry> Equipment { get; set; } = new();
}

/// <summary>
/// A single guaranteed spawn entry. Count may be an integer (e.g. 2) or a range string
/// (e.g. "2-5"). A custom IYamlTypeConverter handles both forms.
/// </summary>
public sealed class SpawnEntry
{
    public string Type { get; set; } = "";
    public int CountMin { get; set; } = 1;
    public int CountMax { get; set; } = 1;
}

public sealed class StairRules
{
    [YamlMember(Alias = "up")]
    public bool Up { get; set; } = true;

    [YamlMember(Alias = "down")]
    public bool Down { get; set; } = true;

    [YamlMember(Alias = "restrict_return_levels")]
    public int RestrictReturnLevels { get; set; }

    [YamlMember(Alias = "spawn_rules")]
    public SpawnRules? SpawnRules { get; set; }
}

public sealed class SpawnRules
{
    [YamlMember(Alias = "near_start_bias")]
    public float NearStartBias { get; set; } = 0.5f;
}

public sealed class EncounterBudget
{
    /// <summary>
    /// Minimum ETP target for a room. Parsed but not yet enforced — no mechanism
    /// to keep adding monsters until a floor-level minimum is met. Tracked at floor
    /// level in the PoC (balance/etp.py). Deferred to floor-ETP tracking plan.
    /// </summary>
    [YamlMember(Alias = "etp_min")]
    public int EtpMin { get; set; }

    [YamlMember(Alias = "etp_max")]
    public int EtpMax { get; set; }

    [YamlMember(Alias = "allow_spike")]
    public bool AllowSpike { get; set; }
}

public sealed class SpecialRoomDef
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "";

    [YamlMember(Alias = "count")]
    public int Count { get; set; } = 1;

    [YamlMember(Alias = "placement")]
    public string Placement { get; set; } = "random";

    [YamlMember(Alias = "guaranteed_spawns")]
    public GuaranteedSpawns? GuaranteedSpawns { get; set; }

    [YamlMember(Alias = "encounter_budget")]
    public EncounterBudget? EncounterBudget { get; set; }

    // Deferred
    [YamlMember(Alias = "metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    [YamlMember(Alias = "faction")]
    public Dictionary<string, string>? Faction { get; set; }
}

/// <summary>
/// Custom YAML type converter for SpawnEntry. Handles both:
///   count: 2        → CountMin=2, CountMax=2
///   count: "2-5"    → CountMin=2, CountMax=5
/// Uses YamlDotNet 16+ interface signatures (ObjectDeserializer / ObjectSerializer).
/// </summary>
public sealed class SpawnEntryConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(SpawnEntry);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var entry = new SpawnEntry();
        parser.Consume<MappingStart>();

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;

            if (key == "type")
            {
                entry.Type = parser.Consume<Scalar>().Value;
            }
            else if (key == "count")
            {
                var scalar = parser.Consume<Scalar>();
                var value = scalar.Value;

                if (value.Contains('-'))
                {
                    // Range format: "2-5"
                    var parts = value.Split('-');
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), out int min)
                        && int.TryParse(parts[1].Trim(), out int max))
                    {
                        entry.CountMin = min;
                        entry.CountMax = max;
                    }
                    else
                    {
                        // Malformed range — treat as 1
                        entry.CountMin = 1;
                        entry.CountMax = 1;
                    }
                }
                else if (int.TryParse(value, out int count))
                {
                    entry.CountMin = count;
                    entry.CountMax = count;
                }
            }
            else
            {
                // Unknown key — skip the value
                parser.SkipThisAndNestedEvents();
            }
        }

        return entry;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        // Write-back not needed for this pipeline
        throw new NotSupportedException("SpawnEntry YAML write is not supported.");
    }
}
