using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Per-run appearance assignment for identifiable items.
///
/// Assigns a descriptor and mystery sprite to every item type that can be unidentified.
/// Assignments are deterministic from run seed and shuffle each new game — same seed
/// always produces the same layout.
///
/// Category assignments:
///   Potions:  one of 15 texture/feel descriptors + one of 3 black bottle sprites (36/37/38), cycling
///   Scrolls:  one of 18 NetHack-style rune labels; sprite always "rune_scroll" ("45")
///   Wands:    one of 10 material/feel descriptors; sprite always "unknown_wand" ("50")
///   Rings:    one of 16 material names + sprite cycling through 76–80
///
/// Thread safety: read-only after construction.
/// TODO: wire to save/load system when that lands.
/// </summary>
public sealed class AppearancePool
{
    // ── Descriptor pools ────────────────────────────────────────────────────────

    // Texture/feel descriptors — never color-based so they don't imply visual information.
    private static readonly string[] PotionDescriptors =
    [
        "Fizzy", "Thick", "Runny", "Opaque", "Smoking", "Syrupy", "Slimy", "Chunky",
        "Warm", "Ice-Cold", "Sour-Smelling", "Sweet", "Acrid", "Effervescent", "Cloudy",
    ];

    // NetHack-style rune labels for scrolls.
    private static readonly string[] ScrollLabels =
    [
        "KIRJE XIXAXA", "ZELGO MER", "JUYED AWK YACC", "FOOBIE BLETCH", "ETAOIN SHRDLU",
        "FNORD", "HAPAX LEGOMENON", "EIRIS SAZUN", "GHOTI", "NR 9",
        "XIXAXA XOXAXA XUXAXA", "YUM YUM", "PRATYAVAYAH", "DAIYEN FOOELS", "THARR",
        "VERR YED HORRE", "VENZAR BORGAVVE", "PRIRUTSENIE",
    ];

    // Material/feel descriptors for wands.
    private static readonly string[] WandDescriptors =
    [
        "Gnarled Oak", "Smooth Birch", "Rough Pine", "Polished Ebony", "Cracked Ivory",
        "Cold Iron", "Humming", "Vibrating", "Warm Copper", "Heavy Lead",
    ];

    // Material names for rings (text is the primary differentiator; sprites cycle 76-80).
    private static readonly string[] RingMaterials =
    [
        "Wooden", "Iron", "Copper", "Bronze", "Silver", "Gold", "Platinum", "Jade",
        "Opal", "Pearl", "Ruby", "Sapphire", "Ivory", "Bone", "Obsidian", "Moonstone",
    ];

    // Mystery potion bottle tileset keys — must match items section in every tileset YAML.
    private static readonly string[] PotionMysterySprites = ["unknown_potion_a", "unknown_potion_b", "unknown_potion_c"];

    // Ring sprite keys (file numbers 76-80, cycling for 16 ring types).
    private static readonly string[] RingSprites = ["76", "77", "78", "79", "80"];

    // Shared mystery sprites for categories that use a single sprite.
    // Must match items section keys in every tileset YAML.
    public const string ScrollMysterySprite = "rune_scroll";
    public const string WandMysterySprite   = "unknown_wand";

    // ── Per-type assignments (populated at construction) ─────────────────────

    private readonly Dictionary<string, string> _descriptors  = new();
    private readonly Dictionary<string, string> _mysterySprites = new();

    /// <summary>
    /// Build the appearance pool for a run.
    ///
    /// items: all identifiable item definitions from the content bundle, keyed by YAML ID.
    /// seed: run seed — same seed always produces same assignments.
    /// </summary>
    // ── Mid-run serialization (M1.4) ──────────────────────────────────────────
    private AppearancePool() { }   // for Restore only — bypasses the RNG-consuming build

    public IReadOnlyDictionary<string, string> Descriptors => _descriptors;
    public IReadOnlyDictionary<string, string> MysterySprites => _mysterySprites;
    public IReadOnlyCollection<string> PotionTypes => _potionTypes;
    public IReadOnlyCollection<string> ScrollTypes => _scrollTypes;
    public IReadOnlyCollection<string> WandTypes => _wandTypes;
    public IReadOnlyCollection<string> RingTypes => _ringTypes;

    /// <summary>Rebuild an appearance pool from a mid-run save — sets the per-run assignments directly,
    /// without re-running the RNG-consuming build. Serializer-only.</summary>
    public static AppearancePool Restore(
        IEnumerable<KeyValuePair<string, string>> descriptors,
        IEnumerable<KeyValuePair<string, string>> mysterySprites,
        IEnumerable<string> potionTypes, IEnumerable<string> scrollTypes,
        IEnumerable<string> wandTypes, IEnumerable<string> ringTypes)
    {
        var p = new AppearancePool();
        foreach (var kv in descriptors) p._descriptors[kv.Key] = kv.Value;
        foreach (var kv in mysterySprites) p._mysterySprites[kv.Key] = kv.Value;
        foreach (var t in potionTypes) p._potionTypes.Add(t);
        foreach (var t in scrollTypes) p._scrollTypes.Add(t);
        foreach (var t in wandTypes) p._wandTypes.Add(t);
        foreach (var t in ringTypes) p._ringTypes.Add(t);
        return p;
    }

    public AppearancePool(IEnumerable<(string id, ItemCategory category)> items, int seed)
    {
        // Separate items by category so we can shuffle each pool independently.
        var potionIds  = new List<string>();
        var scrollIds  = new List<string>();
        var wandIds    = new List<string>();
        var ringIds    = new List<string>();

        foreach (var (id, cat) in items)
        {
            switch (cat)
            {
                case ItemCategory.Potion: potionIds.Add(id);  break;
                case ItemCategory.Scroll: scrollIds.Add(id);  break;
                case ItemCategory.Wand:   wandIds.Add(id);    break;
                case ItemCategory.Ring:   ringIds.Add(id);    break;
                // Other: always identified, no appearance needed
            }
        }

        // Each category gets its own shuffled descriptor pool, seeded deterministically.
        // We derive per-category seeds from the run seed to keep them independent.
        AssignDescriptors(potionIds,  PotionDescriptors,  seed ^ 0x1337,  fallbackPrefix: "Unknown Potion");
        AssignDescriptors(scrollIds,  ScrollLabels,       seed ^ 0x2468,  fallbackPrefix: "Unknown Scroll");
        AssignDescriptors(wandIds,    WandDescriptors,    seed ^ 0x3579,  fallbackPrefix: "Unknown Wand");
        AssignDescriptors(ringIds,    RingMaterials,      seed ^ 0x4680,  fallbackPrefix: "Unknown Ring");

        // Assign mystery sprites — potion types cycle through 36/37/38, rings cycle 76-80.
        AssignPotionSprites(potionIds);
        AssignRingSprites(ringIds);
        // Scrolls and wands share one mystery sprite each (set inline in GetMysterySprite).
    }

    /// <summary>
    /// Get the descriptor for a type, e.g. "Fizzy", "ZELGO MER", "Gnarled Oak", "Jade".
    /// Returns empty string for types that aren't in the pool (weapons/armor — always identified).
    /// </summary>
    public string GetDescriptor(string typeId) =>
        _descriptors.TryGetValue(typeId, out var d) ? d : "";

    /// <summary>
    /// Get the mystery sprite key (file number as string) for a type.
    /// Returns the appropriate shared sprite for scrolls/wands, or the assigned sprite for potions/rings.
    /// Returns empty string if the type has no mystery sprite (weapons/armor).
    /// </summary>
    public string GetMysterySprite(string typeId)
    {
        if (_mysterySprites.TryGetValue(typeId, out var s)) return s;
        // Scrolls and wands use shared sprites — but we only know category from context,
        // so callers for scrolls/wands should use the public constants directly.
        return "";
    }

    /// <summary>
    /// Get the full unidentified display name for a type.
    /// Format depends on category:
    ///   Potion: "Fizzy Potion"
    ///   Scroll: "Scroll labeled ZELGO MER"
    ///   Wand:   "Gnarled Oak Wand"
    ///   Ring:   "Jade Ring"
    ///   Other:  "" (weapons/armor — always identified, caller should use Entity.Name)
    /// </summary>
    public string GetDisplayName(string typeId)
    {
        if (!_descriptors.TryGetValue(typeId, out var descriptor) || string.IsNullOrEmpty(descriptor))
            return "";

        // Detect the category from the descriptor content — descriptors are unique per pool.
        // We use a separate lookup for this rather than re-storing category.
        if (_potionTypes.Contains(typeId))
            return $"{descriptor} Potion";
        if (_scrollTypes.Contains(typeId))
            return $"Scroll labeled {descriptor}";
        if (_wandTypes.Contains(typeId))
            return $"{descriptor} Wand";
        if (_ringTypes.Contains(typeId))
            return $"{descriptor} Ring";

        return "";
    }

    // Category membership sets — needed for GetDisplayName formatting.
    private readonly HashSet<string> _potionTypes = new();
    private readonly HashSet<string> _scrollTypes = new();
    private readonly HashSet<string> _wandTypes   = new();
    private readonly HashSet<string> _ringTypes   = new();

    // ── Private helpers ─────────────────────────────────────────────────────────

    private void AssignDescriptors(List<string> typeIds, string[] pool, int seed, string fallbackPrefix)
    {
        if (typeIds.Count == 0) return;

        // Log warning if pool is exhausted — fallback uses numbered names.
        if (typeIds.Count > pool.Length)
        {
            // We still need to assign something to every type — use fallback names for overflow.
            // In practice this means someone added too many items of this category to entities.yaml.
            Console.Error.WriteLine(
                $"[AppearancePool] Warning: {typeIds.Count} types exceed pool size {pool.Length} for {fallbackPrefix}. " +
                $"Overflow types will use numbered fallback names.");
        }

        // Shuffle the descriptor pool using Fisher-Yates with deterministic seed.
        var shuffled = ShufflePool(pool, seed);

        for (int i = 0; i < typeIds.Count; i++)
        {
            string descriptor = i < shuffled.Length
                ? shuffled[i]
                : $"{fallbackPrefix} #{i - shuffled.Length + 1}";
            _descriptors[typeIds[i]] = descriptor;

            // Track category membership for GetDisplayName.
            if (fallbackPrefix.Contains("Potion"))       _potionTypes.Add(typeIds[i]);
            else if (fallbackPrefix.Contains("Scroll"))  _scrollTypes.Add(typeIds[i]);
            else if (fallbackPrefix.Contains("Wand"))    _wandTypes.Add(typeIds[i]);
            else if (fallbackPrefix.Contains("Ring"))    _ringTypes.Add(typeIds[i]);
        }
    }

    private void AssignPotionSprites(List<string> potionIds)
    {
        // Cycle through the 3 black bottle sprites deterministically.
        // Types get the same sprite if count > 3, which is fine — the text descriptor differentiates them.
        for (int i = 0; i < potionIds.Count; i++)
            _mysterySprites[potionIds[i]] = PotionMysterySprites[i % PotionMysterySprites.Length];
    }

    private void AssignRingSprites(List<string> ringIds)
    {
        // Cycle through the 5 ring material sprites deterministically.
        // 16 ring types → 5 sprites → each sprite covers ~3 types.
        for (int i = 0; i < ringIds.Count; i++)
            _mysterySprites[ringIds[i]] = RingSprites[i % RingSprites.Length];
    }

    /// <summary>
    /// Fisher-Yates shuffle on a copy of the array using a deterministic seed.
    /// Returns the shuffled copy without modifying the original.
    /// </summary>
    private static string[] ShufflePool(string[] pool, int seed)
    {
        var copy = (string[])pool.Clone();
        var rng  = new System.Random(seed);

        for (int i = copy.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }
}
