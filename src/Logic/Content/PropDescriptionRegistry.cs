namespace UnderWarden.Logic.Content;

/// <summary>
/// Provides human-readable names and atmospheric descriptions for inspectable world features.
///
/// Two data sources:
///   1. config/props.yaml and config/interactive_props.yaml — entries parsed from
///      `display_name:` and `description:` fields under each prop key.
///   2. Hard-coded entries for tile-based features (doors, stairs, traps, portals, etc.)
///      that don't have a YAML prop definition but still need inspect text.
///
/// Keys for tile-based features use the `__xxx` prefix to avoid collisions with prop IDs.
/// Keys for prop-based features are the prop ID string (e.g. "barrel", "bone_pile").
///
/// Parsing uses simple line-by-line YAML scanning — no YamlDotNet dependency here.
/// This keeps the registry iOS NativeAOT-safe (no reflection at load time).
/// </summary>
public static class PropDescriptionRegistry
{
    private static readonly Dictionary<string, (string Name, string Description)> Entries = new();

    // Hard-coded tile-based feature entries loaded once at class init.
    // These cover TileKind values that have semantic inspect content but no props.yaml entry.
    static PropDescriptionRegistry()
    {
        LoadTileFeatureEntries();
    }

    /// <summary>
    /// Load display_name and description fields from props.yaml and interactive_props.yaml.
    /// Call once at startup after reading both YAML files via Godot FileAccess.
    /// Safe to call multiple times — later calls overwrite earlier entries for the same key.
    /// </summary>
    public static void Load(string propsYaml, string interactivePropsYaml)
    {
        ParsePropSection(propsYaml, topLevelKey: "props");
        ParsePropSection(interactivePropsYaml, topLevelKey: "interactive_props");
    }

    /// <summary>
    /// Look up the display name and description for a given prop or tile feature key.
    /// Returns null if the key is not registered.
    /// </summary>
    public static (string Name, string Description)? Get(string propId)
        => Entries.TryGetValue(propId, out var entry) ? entry : null;

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a top-level YAML section looking for prop entries that contain
    /// `display_name:` and `description:` fields.
    ///
    /// Format parsed:
    ///   topLevelKey:
    ///     some_prop_id:
    ///       display_name: "Name Here"
    ///       description: "One-liner here."
    ///       other_fields: ...
    ///
    /// The parser is deliberately minimal: it tracks indentation levels to identify
    /// which prop block it is inside and extracts the two fields it cares about.
    /// All other YAML content is ignored. This is intentional — it avoids pulling
    /// YamlDotNet into any path that might run in a NativeAOT context.
    /// </summary>
    private static void ParsePropSection(string yaml, string topLevelKey)
    {
        var lines = yaml.Split('\n');

        bool inTargetSection = false; // inside the topLevelKey block
        string? currentPropId = null;
        string? currentName = null;
        string? currentDescription = null;

        foreach (var rawLine in lines)
        {
            // Strip trailing whitespace / carriage returns
            var line = rawLine.TrimEnd();

            // Skip blank lines and comments
            if (line.Length == 0 || line.TrimStart().StartsWith('#')) continue;

            int indent = CountLeadingSpaces(line);
            var trimmed = line.TrimStart();

            // Detect top-level section header (indent=0, matches "topLevelKey:")
            if (indent == 0)
            {
                // Flush any in-progress prop when leaving a section
                FlushProp(ref currentPropId, ref currentName, ref currentDescription);

                inTargetSection = trimmed.StartsWith(topLevelKey + ":", StringComparison.Ordinal);
                currentPropId = null;
                continue;
            }

            if (!inTargetSection) continue;

            // Prop ID lines: indent=2, value is the prop key (ends with ':')
            if (indent == 2 && trimmed.EndsWith(':'))
            {
                // Flush the previous prop before starting a new one
                FlushProp(ref currentPropId, ref currentName, ref currentDescription);

                currentPropId = trimmed[..^1].Trim(); // strip trailing ':'
                currentName = null;
                currentDescription = null;
                continue;
            }

            // Field lines inside a prop block: indent=4
            if (indent == 4 && currentPropId != null)
            {
                if (trimmed.StartsWith("display_name:", StringComparison.Ordinal))
                {
                    currentName = ExtractStringValue(trimmed, "display_name:");
                }
                else if (trimmed.StartsWith("description:", StringComparison.Ordinal))
                {
                    currentDescription = ExtractStringValue(trimmed, "description:");
                }
            }
        }

        // Flush the last prop in the section
        FlushProp(ref currentPropId, ref currentName, ref currentDescription);
    }

    private static void FlushProp(ref string? propId, ref string? name, ref string? description)
    {
        if (propId != null && name != null && description != null)
            Entries[propId] = (name, description);

        propId = null;
        name = null;
        description = null;
    }

    /// <summary>
    /// Extract a YAML string scalar value from a "key: value" line.
    /// Strips surrounding quotes if present.
    /// </summary>
    private static string? ExtractStringValue(string trimmedLine, string keyPrefix)
    {
        var value = trimmedLine[keyPrefix.Length..].Trim();

        // Strip surrounding double quotes
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];

        return value.Length > 0 ? value : null;
    }

    private static int CountLeadingSpaces(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ') count++;
            else break;
        }
        return count;
    }

    /// <summary>
    /// Populate hard-coded entries for tile-based features (TileKind values, traps, portals).
    /// These use the "__xxx" naming convention to distinguish them from prop IDs.
    /// Called once from the static constructor.
    /// </summary>
    private static void LoadTileFeatureEntries()
    {
        // Chests (handled as features, not tiles)
        Entries["__chest_closed"]  = ("Chest",        "A wooden chest bound with iron straps — could hold anything.");
        Entries["__chest_open"]    = ("Open Chest",   "Already opened — nothing left inside.");
        Entries["__chest_locked"]  = ("Locked Chest", "This chest is locked. Find the matching key.");

        // Doors
        Entries["__door"]          = ("Door",         "A wooden door, worn smooth from years of hands pushing it open.");
        Entries["__door_locked"]   = ("Locked Door",  "Locked tight. The right key will open it.");
        Entries["__secret_door"]   = ("Secret Door",  "A hidden passage, now revealed in the stonework.");

        // Navigation
        Entries["__stair_down"]    = ("Stairs Down",  "Stone steps descending into deeper darkness.");
        Entries["__stair_up"]      = ("Stairs Up",    "The stairs you came down. There is no going back.");

        // Signs and murals
        Entries["__sign"]          = ("Sign",         "There is writing here — tap to read it.");
        Entries["__mural"]         = ("Mural",        "A carved relief on the wall — old, and deeply strange.");

        // Portals
        Entries["__portal"]        = ("Portal",       "A shimmering portal, humming with barely-contained energy.");

        // Traps — generic descriptions that work whether detected or not
        Entries["__trap_spike"]    = ("Spike Trap",   "Metal spikes concealed beneath a pressure plate.");
        Entries["__trap_web"]      = ("Web Trap",     "Thick strands of enchanted webbing, nearly invisible underfoot.");
        Entries["__trap_gas"]      = ("Gas Trap",     "A pressure plate connected to a hidden gas vent.");
        Entries["__trap_fire"]     = ("Fire Trap",    "A flame jet rigged to trigger on contact.");
        Entries["__trap_alarm"]    = ("Alarm Trap",   "A noisemaker — it will alert everything nearby if triggered.");
        Entries["__trap_hole"]     = ("Pit Trap",     "A false floor that drops away without warning.");
        Entries["__trap_acid"]     = ("Acid Trap",    "Corrosive fluid sprayed by a hidden mechanism.");
        Entries["__trap_root"]     = ("Root Trap",    "Enchanted tendrils that snap upward to bind the unwary.");
        Entries["__trap_teleport"] = ("Teleport Trap","A runic circle that whisks you somewhere else entirely.");
    }
}
