using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Root YAML structure for entities file.
/// </summary>
public sealed class EntitiesFile
{
    [YamlMember(Alias = "monsters")]
    public Dictionary<string, MonsterDefinition> Monsters { get; set; } = new();

    [YamlMember(Alias = "weapons")]
    public Dictionary<string, ItemDefinition> Weapons { get; set; } = new();

    [YamlMember(Alias = "armor")]
    public Dictionary<string, ItemDefinition> Armor { get; set; } = new();

    [YamlMember(Alias = "consumables")]
    public Dictionary<string, ConsumableDefinition> Consumables { get; set; } = new();

    [YamlMember(Alias = "scrolls")]
    public Dictionary<string, SpellDefinition> Scrolls { get; set; } = new();

    [YamlMember(Alias = "wands")]
    public Dictionary<string, SpellDefinition> Wands { get; set; } = new();
}

/// <summary>
/// Partial wrapper to extract just the floor_item_pool list from the entities YAML.
/// IgnoreUnmatchedProperties allows this to be deserialized from the full entities file.
/// </summary>
internal sealed class FloorItemPoolFile
{
    [YamlMember(Alias = "floor_item_pool")]
    public List<FloorItemPoolEntry> FloorItemPool { get; set; } = new();
}

/// <summary>
/// Loads YAML content files and resolves inheritance.
/// Single source of truth for content loading — all YAML goes through here.
/// </summary>
public sealed class ContentLoader
{
    private readonly IDeserializer _deserializer;

    public ContentLoader() : this(new AotObjectFactory()) { }

    /// <summary>
    /// Injectable constructor — primarily for testing with a strict AotObjectFactory.
    /// Pass <c>new AotObjectFactory(strict: true)</c> to simulate NativeAOT behaviour.
    /// </summary>
    public ContentLoader(IObjectFactory factory)
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithObjectFactory(factory)
            .Build();
    }

    /// <summary>
    /// Load monster definitions from a YAML string. Resolves inheritance.
    /// Returns a dictionary of monster ID → resolved MonsterDefinition.
    /// </summary>
    public Dictionary<string, MonsterDefinition> LoadMonsters(string yaml)
    {
        // Deserialize to a generic dictionary tree instead of EntitiesFile.
        // NativeAOT trims PropertyInfo.SetValue which prevents YamlDotNet from
        // populating typed wrapper classes. Deserializing directly to the section
        // we need avoids the property-setter reflection entirely.
        var root = _deserializer.Deserialize<Dictionary<string, Dictionary<string, MonsterDefinition>>>(yaml);
        if (root == null || !root.TryGetValue("monsters", out var monsters) || monsters.Count == 0)
            return new Dictionary<string, MonsterDefinition>();

        var resolved = ResolveInheritance(monsters);

        // Validate depth_weights tables are sorted ascending by min_depth
        foreach (var (id, def) in resolved)
        {
            if (def.DepthWeights == null) continue;
            for (int i = 1; i < def.DepthWeights.Count; i++)
            {
                if (def.DepthWeights[i].MinDepth <= def.DepthWeights[i - 1].MinDepth)
                    throw new InvalidOperationException(
                        $"Monster '{id}': depth_weights must be sorted ascending by min_depth. " +
                        $"Entry {i} (min_depth={def.DepthWeights[i].MinDepth}) is not greater than " +
                        $"entry {i-1} (min_depth={def.DepthWeights[i-1].MinDepth}).");
            }
        }

        return resolved;
    }

    /// <summary>
    /// Load monster definitions from a YAML file path.
    /// </summary>
    public Dictionary<string, MonsterDefinition> LoadMonstersFromFile(string path)
    {
        string yaml = File.ReadAllText(path);
        return LoadMonsters(yaml);
    }

    /// <summary>
    /// Load item definitions (weapons + armor) from a YAML string.
    /// Returns a combined dictionary of item ID to ItemDefinition.
    /// </summary>
    public Dictionary<string, ItemDefinition> LoadItems(string yaml)
    {
        var root = _deserializer.Deserialize<Dictionary<string, Dictionary<string, ItemDefinition>>>(yaml);
        var items = new Dictionary<string, ItemDefinition>();

        if (root != null && root.TryGetValue("weapons", out var weapons))
        {
            foreach (var (id, def) in weapons)
            {
                def.Name ??= TitleCase(id);
                items[id] = def;
            }
        }

        if (root != null && root.TryGetValue("armor", out var armor))
        {
            foreach (var (id, def) in armor)
            {
                def.Name ??= TitleCase(id);
                items[id] = def;
            }
        }

        return items;
    }

    /// <summary>
    /// Load consumable definitions from a YAML string.
    /// </summary>
    public Dictionary<string, ConsumableDefinition> LoadConsumables(string yaml)
    {
        var root = _deserializer.Deserialize<Dictionary<string, Dictionary<string, ConsumableDefinition>>>(yaml);
        var consumables = new Dictionary<string, ConsumableDefinition>();

        if (root != null && root.TryGetValue("consumables", out var section))
        {
            foreach (var (id, def) in section)
            {
                def.Name ??= TitleCase(id);
                def.Id = id;
                // Consumables default to Potion; override via YAML category: field if needed.
                if (def.Category == ItemCategory.Other)
                    def.Category = ItemCategory.Potion;
                consumables[id] = def;
            }
        }

        return consumables;
    }

    /// <summary>
    /// Load a scenario definition from a YAML string.
    /// </summary>
    public Balance.ScenarioDefinition LoadScenario(string yaml)
    {
        return _deserializer.Deserialize<Balance.ScenarioDefinition>(yaml);
    }

    /// <summary>
    /// Load a scenario definition from a YAML file path.
    /// </summary>
    public Balance.ScenarioDefinition LoadScenarioFromFile(string path)
    {
        return LoadScenario(File.ReadAllText(path));
    }

    /// <summary>
    /// Load floor item pool entries from a YAML string.
    /// Returns an empty list if the floor_item_pool section is missing.
    /// </summary>
    public List<FloorItemPoolEntry> LoadFloorItemPool(string yaml)
    {
        var wrapper = _deserializer.Deserialize<FloorItemPoolFile>(yaml);
        return wrapper?.FloorItemPool ?? new List<FloorItemPoolEntry>();
    }

    /// <summary>
    /// Load spell item definitions (scrolls and wands) from a YAML string.
    /// Returns a combined dictionary of item ID → SpellDefinition.
    /// Both scrolls (is_wand=false) and wands (is_wand=true) are returned together.
    /// </summary>
    public Dictionary<string, SpellDefinition> LoadSpellItems(string yaml)
    {
        var root = _deserializer.Deserialize<Dictionary<string, Dictionary<string, SpellDefinition>>>(yaml);
        var spellItems = new Dictionary<string, SpellDefinition>();

        if (root != null && root.TryGetValue("scrolls", out var scrolls))
        {
            foreach (var (id, def) in scrolls)
            {
                def.Name ??= TitleCase(id);
                def.IsWand = false; // explicit: scrolls are never wands
                def.Id = id;
                def.Category = ItemCategory.Scroll;
                spellItems[id] = def;
            }
        }

        if (root != null && root.TryGetValue("wands", out var wands))
        {
            foreach (var (id, def) in wands)
            {
                def.Name ??= TitleCase(id);
                def.IsWand = true; // explicit: wands section always produces wands
                def.Id = id;
                def.Category = ItemCategory.Wand;
                spellItems[id] = def;
            }
        }

        return spellItems;
    }

    /// <summary>
    /// Load ring definitions from a YAML string.
    /// Converts each RingDefinition into an ItemDefinition with category=Ring,
    /// slot=left_ring, and ring-specific fields populated.
    /// Returns a dictionary of ring ID → ItemDefinition.
    /// </summary>
    public Dictionary<string, ItemDefinition> LoadRings(string yaml)
    {
        var root = _deserializer.Deserialize<Dictionary<string, Dictionary<string, RingDefinition>>>(yaml);
        var rings = new Dictionary<string, ItemDefinition>();

        if (root == null || !root.TryGetValue("rings", out var section))
            return rings;

        foreach (var (id, def) in section)
        {
            var itemDef = new ItemDefinition
            {
                Name = TitleCase(id),
                Slot = "left_ring",   // All rings start as left_ring; ResolveEquip auto-redirects to right
                Category = ItemCategory.Ring,
                Char = def.Char,
                Color = def.Color,
                RingEffect = def.RingEffect,
                EffectStrength = def.EffectStrength,
                // Speed ring ratio: convert from integer percentage to double ratio
                RingSpeedRatio = def.RingEffect switch
                {
                    "speed"       => 0.10,
                    "hummingbird" => 0.25,
                    _             => 0.0,
                },
            };
            rings[id] = itemDef;
        }

        return rings;
    }

    /// <summary>
    /// Load depth boon definitions from a YAML string (config/depth_boons.yaml).
    /// Returns a dictionary of depth → BoonDefinition.
    /// </summary>
    public Dictionary<int, Balance.BoonDefinition> LoadBoons(string yaml)
    {
        var config = _deserializer.Deserialize<Balance.DepthBoonConfig>(yaml);
        if (config?.DepthBoons == null || config.DepthBoons.Count == 0)
            return new Dictionary<int, Balance.BoonDefinition>();

        return config.DepthBoons.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToBoonDefinition());
    }

    /// <summary>
    /// Load depth boon definitions from a YAML file path.
    /// </summary>
    public Dictionary<int, Balance.BoonDefinition> LoadBoonsFromFile(string path)
    {
        return LoadBoons(File.ReadAllText(path));
    }

    /// <summary>
    /// Load prop definitions from a YAML string (config/props.yaml).
    /// Returns a PropRegistry keyed by prop ID.
    /// </summary>
    public PropRegistry LoadProps(string yaml)
    {
        var file = _deserializer.Deserialize<PropsFile>(yaml);
        return new PropRegistry(file?.Props ?? new Dictionary<string, PropDefinition>());
    }

    /// <summary>
    /// Load prop definitions from a YAML file path.
    /// </summary>
    public PropRegistry LoadPropsFromFile(string path)
    {
        return LoadProps(File.ReadAllText(path));
    }

    /// <summary>
    /// Load interactive prop definitions from a YAML string (config/interactive_props.yaml).
    /// Returns an InteractivePropsRegistry containing prop definitions + named trap payloads.
    /// </summary>
    public InteractivePropsRegistry LoadInteractiveProps(string yaml)
    {
        var file = _deserializer.Deserialize<InteractivePropsFile>(yaml);
        return new InteractivePropsRegistry(
            file?.Props ?? new Dictionary<string, InteractivePropDefinition>(),
            file?.TrapPayloads ?? new Dictionary<string, TrapPayloadDefinition>());
    }

    /// <summary>
    /// Load interactive prop definitions from a YAML file path.
    /// </summary>
    public InteractivePropsRegistry LoadInteractivePropsFromFile(string path)
    {
        return LoadInteractiveProps(File.ReadAllText(path));
    }

    /// <summary>
    /// Load floor trap definitions from a YAML string (config/floor_traps.yaml).
    /// Returns a FloorTrapRegistry keyed by trap type ID.
    /// </summary>
    public FloorTrapRegistry LoadFloorTraps(string yaml)
    {
        var file = _deserializer.Deserialize<FloorTrapsFile>(yaml);
        return new FloorTrapRegistry(file?.Traps ?? new Dictionary<string, FloorTrapDefinition>());
    }

    /// <summary>
    /// Load floor trap definitions from a YAML file path.
    /// </summary>
    public FloorTrapRegistry LoadFloorTrapsFromFile(string path)
    {
        return LoadFloorTraps(File.ReadAllText(path));
    }

    /// <summary>
    /// Load all content from a single entities YAML string.
    /// Returns monsters, items, consumables, spell items, and floor item pool in one call.
    /// </summary>
    public ContentBundle LoadAll(string yaml)
    {
        // Parse the floor item pool first (uses a typed wrapper that handles sequences).
        // Then strip that key from the yaml before passing to section-specific loaders —
        // those loaders deserialize to Dictionary<string, Dictionary<string, T>> which
        // chokes on sequence values like floor_item_pool's list.
        var floorPool = LoadFloorItemPool(yaml);
        var cleanYaml = StripTopLevelKey(yaml, "floor_item_pool");

        var items = LoadItems(cleanYaml);

        // Rings are loaded separately and merged into the items dictionary.
        // This keeps ItemFactory as the single entity creation path.
        var rings = LoadRings(cleanYaml);
        foreach (var (id, def) in rings)
            items[id] = def;

        return new ContentBundle
        {
            Monsters = LoadMonsters(cleanYaml),
            Items = items,
            Consumables = LoadConsumables(cleanYaml),
            SpellItems = LoadSpellItems(cleanYaml),
            FloorItemPool = floorPool,
        };
    }

    /// <summary>
    /// Remove a top-level YAML key (and all its indented content) from a YAML string.
    /// Top-level keys are lines that start at column 0 (no leading whitespace).
    /// </summary>
    private static string StripTopLevelKey(string yaml, string key)
    {
        var lines = yaml.Split('\n');
        var result = new System.Text.StringBuilder();
        bool skipping = false;
        string prefix = key + ":";

        foreach (var line in lines)
        {
            // A top-level key: non-empty, starts at column 0, not a comment, not a list item
            bool isTopLevel = line.Length > 0
                && !char.IsWhiteSpace(line[0])
                && !line.StartsWith('#')
                && !line.StartsWith('-');

            if (isTopLevel && line.StartsWith(prefix))
            {
                skipping = true;
                continue;
            }

            if (skipping && isTopLevel)
                skipping = false;

            if (!skipping)
                result.AppendLine(line);
        }

        return result.ToString();
    }

    /// <summary>
    /// Load all content from a YAML file path.
    /// </summary>
    public ContentBundle LoadAllFromFile(string path)
    {
        return LoadAll(File.ReadAllText(path));
    }

    /// <summary>
    /// Resolve `extends` inheritance. Parent fields are deep-merged into children.
    /// Child values override parent values. Stats are merged field-by-field.
    /// </summary>
    private static Dictionary<string, MonsterDefinition> ResolveInheritance(
        Dictionary<string, MonsterDefinition> raw)
    {
        var resolved = new Dictionary<string, MonsterDefinition>();
        var resolving = new HashSet<string>();

        void Resolve(string id)
        {
            if (resolved.ContainsKey(id)) return;
            if (!raw.TryGetValue(id, out var def))
                throw new InvalidOperationException($"Unknown monster '{id}' referenced in inheritance.");
            if (resolving.Contains(id))
                throw new InvalidOperationException($"Circular inheritance detected for monster '{id}'.");

            if (string.IsNullOrEmpty(def.Extends))
            {
                resolved[id] = def;
                return;
            }

            resolving.Add(id);
            string parentId = def.Extends;
            Resolve(parentId);
            var parent = resolved[parentId];

            resolved[id] = Merge(parent, def, id);
            resolving.Remove(id);
        }

        foreach (var id in raw.Keys)
            Resolve(id);

        return resolved;
    }

    /// <summary>
    /// Merge parent definition into child. Child values win.
    /// Stats are merged field-by-field (child stats override individual fields, not the whole block).
    /// </summary>
    private static MonsterDefinition Merge(MonsterDefinition parent, MonsterDefinition child, string id)
    {
        var mergedStats = MergeStats(parent.Stats, child.Stats);

        return new MonsterDefinition
        {
            Name = child.Name ?? parent.Name ?? TitleCase(id),
            Extends = null, // resolved
            Stats = mergedStats,
            Char = child.Char != "?" ? child.Char : parent.Char,
            Color = child.Color is [255, 255, 255] ? parent.Color : child.Color,
            AiType = child.AiType != "basic" || parent.AiType == "basic" ? child.AiType : parent.AiType,
            RenderOrder = child.RenderOrder,
            Blocks = child.Blocks,
            Faction = child.Faction != "neutral" ? child.Faction : parent.Faction,
            Tags = child.Tags ?? parent.Tags,
            // Threat archetype: child wins if set, else inherit parent (orc variants inherit orc's).
            ThreatArchetype = child.ThreatArchetype ?? parent.ThreatArchetype,
            EtpBase = child.EtpBase != 0 ? child.EtpBase : parent.EtpBase,
            MinDepth = child.MinDepth != 1 ? child.MinDepth : parent.MinDepth,
            SpeedBonus = child.SpeedBonus != 0 ? child.SpeedBonus : parent.SpeedBonus,
            DamageResistance = child.DamageResistance ?? parent.DamageResistance,
            DamageVulnerability = child.DamageVulnerability ?? parent.DamageVulnerability,
            Equipment = child.Equipment ?? parent.Equipment,
            // Item-seeking behavior: child's true always wins; inherit parent's true if child didn't set it.
            // This matches PoC convention where non-seeking monsters explicitly set can_seek_items: false.
            CanSeekItems = child.CanSeekItems || parent.CanSeekItems,
            // Door-opening: same pattern — true propagates from parent to children.
            CanOpenDoors = child.CanOpenDoors || parent.CanOpenDoors,
            SeekDistance = child.SeekDistance != 5 ? child.SeekDistance : parent.SeekDistance,
            InventorySize = child.InventorySize != 0 ? child.InventorySize : parent.InventorySize,
            SpawnWeight = child.SpawnWeight ?? parent.SpawnWeight,
            DepthWeights = child.DepthWeights ?? parent.DepthWeights,
            // Corrosion: child wins if non-zero, else inherit parent
            CorrosionChance = child.CorrosionChance != 0 ? child.CorrosionChance : parent.CorrosionChance,
            // Split: child wins if explicitly set, else inherit parent
            SplitTriggerHpPct = child.SplitTriggerHpPct ?? parent.SplitTriggerHpPct,
            SplitChildType = child.SplitChildType ?? parent.SplitChildType,
            SplitMinChildren = child.SplitMinChildren != 2 ? child.SplitMinChildren : parent.SplitMinChildren,
            SplitMaxChildren = child.SplitMaxChildren != 3 ? child.SplitMaxChildren : parent.SplitMaxChildren,
            SplitWeights = child.SplitWeights ?? parent.SplitWeights,
            // LeavesCorpse: false wins — if either parent or child says no corpse, result is no corpse.
            LeavesCorpse = child.LeavesCorpse && parent.LeavesCorpse,
            // Regeneration: child wins if non-zero, else inherit parent
            RegenerationAmount = child.RegenerationAmount != 0 ? child.RegenerationAmount : parent.RegenerationAmount,
            // On-hit effect: child wins if set (allows child to override parent's effect)
            OnHitEffect = child.OnHitEffect ?? parent.OnHitEffect,
            OnHitEffectDuration = child.OnHitEffectDuration != 0 ? child.OnHitEffectDuration : parent.OnHitEffectDuration,
            // Necromancer AI params: child wins if non-default, else inherit parent
            RaiseDeadRange           = child.RaiseDeadRange != 5           ? child.RaiseDeadRange           : parent.RaiseDeadRange,
            RaiseDeadCooldownTurns   = child.RaiseDeadCooldownTurns != 4   ? child.RaiseDeadCooldownTurns   : parent.RaiseDeadCooldownTurns,
            DangerRadiusFromPlayer   = child.DangerRadiusFromPlayer != 2   ? child.DangerRadiusFromPlayer   : parent.DangerRadiusFromPlayer,
            PreferredDistanceMin     = child.PreferredDistanceMin != 4     ? child.PreferredDistanceMin     : parent.PreferredDistanceMin,
            PreferredDistanceMax     = child.PreferredDistanceMax != 7     ? child.PreferredDistanceMax     : parent.PreferredDistanceMax,
            // Life drain: child wins if non-zero
            LifeDrainPct = child.LifeDrainPct != 0 ? child.LifeDrainPct : parent.LifeDrainPct,
            // Soul Bolt params: child wins if non-zero/default
            SoulBoltRange = child.SoulBoltRange != 0 ? child.SoulBoltRange : parent.SoulBoltRange,
            SoulBoltDamagePct = child.SoulBoltDamagePct != 0 ? child.SoulBoltDamagePct : parent.SoulBoltDamagePct,
            SoulBoltCooldownTurns = child.SoulBoltCooldownTurns != 0 ? child.SoulBoltCooldownTurns : parent.SoulBoltCooldownTurns,
            // Command the Dead / Death Siphon: child wins if non-zero
            CommandTheDeadRadius = child.CommandTheDeadRadius != 0 ? child.CommandTheDeadRadius : parent.CommandTheDeadRadius,
            DeathSiphonRadius = child.DeathSiphonRadius != 0 ? child.DeathSiphonRadius : parent.DeathSiphonRadius,
            // Summon override: child wins if set
            SummonMonsterId = child.SummonMonsterId ?? parent.SummonMonsterId,
            // Status immunities: child wins if set (full override, not merge)
            StatusImmunities = child.StatusImmunities ?? parent.StatusImmunities,
            // Engulf: true propagates from parent to children (slime hierarchy inherits it)
            EngulfsOnHit = child.EngulfsOnHit || parent.EngulfsOnHit,
        };
    }

    private static MonsterStats MergeStats(MonsterStats? parent, MonsterStats? child)
    {
        if (parent == null) return child ?? new MonsterStats();
        if (child == null) return parent;

        return new MonsterStats
        {
            Hp = child.Hp != 0 ? child.Hp : parent.Hp,
            Power = child.Power != 0 ? child.Power : parent.Power,
            Defense = child.Defense != 0 ? child.Defense : parent.Defense,
            Xp = child.Xp != 0 ? child.Xp : parent.Xp,
            DamageMin = child.DamageMin != 0 ? child.DamageMin : parent.DamageMin,
            DamageMax = child.DamageMax != 0 ? child.DamageMax : parent.DamageMax,
            Strength = child.Strength != 10 ? child.Strength : parent.Strength,
            Dexterity = child.Dexterity != 10 ? child.Dexterity : parent.Dexterity,
            Constitution = child.Constitution != 10 ? child.Constitution : parent.Constitution,
            Accuracy = child.Accuracy != Combat.HitModel.DefaultAccuracy ? child.Accuracy : parent.Accuracy,
            Evasion = child.Evasion != Combat.HitModel.DefaultEvasion ? child.Evasion : parent.Evasion,
        };
    }

    private static string TitleCase(string id)
    {
        return string.Join(' ', id.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
    }
}
