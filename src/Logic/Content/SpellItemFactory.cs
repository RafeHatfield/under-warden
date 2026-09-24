using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Creates scroll and wand entities from SpellDefinitions.
///
/// Scrolls:  Consumable (stack=1, healAmount=0) + SpellEffect
/// Wands:    WandComponent (random charges from def range) + SpellEffect
///
/// Entity names match definition names (e.g., "Scroll of Lightning").
/// </summary>
public sealed class SpellItemFactory
{
    private readonly Dictionary<string, SpellDefinition> _definitions;
    private readonly EntityFactory _entityFactory;

    public SpellItemFactory(Dictionary<string, SpellDefinition> definitions, EntityFactory entityFactory)
    {
        _definitions = definitions;
        _entityFactory = entityFactory;
    }

    /// <summary>All spell/wand definition IDs available in this factory.</summary>
    public IEnumerable<string> AvailableIds => _definitions.Keys;

    /// <summary>Look up a spell definition by ID. Returns null if not found.</summary>
    public SpellDefinition? GetDefinition(string spellId) =>
        _definitions.TryGetValue(spellId, out var def) ? def : null;

    /// <summary>
    /// Create a scroll entity from a definition ID.
    ///
    /// registry/pool/rng/difficulty: optional identification system parameters.
    /// When provided, pre-identification is applied per the run's per-type decision.
    /// When null (scenario harness, tests), identification is skipped and items appear identified.
    ///
    /// Returns null if the ID is unknown or the definition is marked is_wand=true.
    /// </summary>
    public Entity? CreateScroll(string spellId,
        IdentificationRegistry? registry = null,
        AppearancePool? pool = null,
        SeededRandom? identRng = null,
        Difficulty difficulty = Difficulty.Medium)
    {
        if (!_definitions.TryGetValue(spellId, out var def))
            return null;

        if (def.IsWand)
            return null; // Use CreateWand for wand definitions

        var entity = _entityFactory.Create(def.Name ?? spellId);

        // Consumable with HealAmount=0 — scrolls are consumed on use but don't heal.
        entity.Add(new Consumable(healAmount: 0));

        entity.Add(new SpellEffect
        {
            SpellId      = def.SpellId,
            Targeting    = def.ParseTargetingMode(),
            Damage       = def.Damage,
            Radius       = def.Radius,
            Range        = def.Range,
            Duration     = def.Duration,
            MisfireChance = def.MisfireChance,
        });

        // ItemTag carries the canonical YAML type ID — required for identification and stacking.
        entity.Add(new ItemTag(spellId));

        // IdentifiableItem holds the two possible display names.
        entity.Add(new IdentifiableItem
        {
            IdentifiedName   = def.DisplayName,
            UnidentifiedName = "",
        });

        // Apply pre-identification decision. No-op if registry/pool/identRng are null.
        if (registry != null && pool != null && identRng != null)
            PreIdentification.Apply(entity, spellId, def.Category, registry, pool, identRng, difficulty);

        return entity;
    }

    /// <summary>
    /// Create a wand entity from a definition ID.
    /// Charges are randomly selected from [def.MinCharges, def.MaxCharges] using the provided rng,
    /// then scaled by depth: charges += (depth - 1), capped at def.ChargeCap.
    /// PoC formula: rand(min_charges, max_charges) + (depth - 1), capped at charge_cap.
    ///
    /// registry/pool/identRng/difficulty: optional identification system parameters.
    /// Returns null if the ID is unknown or the definition is NOT marked is_wand=true.
    /// </summary>
    public Entity? CreateWand(string spellId, SeededRandom rng, int depth = 1,
        IdentificationRegistry? registry = null,
        AppearancePool? pool = null,
        SeededRandom? identRng = null,
        Difficulty difficulty = Difficulty.Medium)
    {
        if (!_definitions.TryGetValue(spellId, out var def))
            return null;

        if (!def.IsWand)
            return null; // Use CreateScroll for scroll definitions

        var entity = _entityFactory.Create(def.Name ?? spellId);

        int charges = def.Infinite ? 0
            : Math.Min(rng.Next(def.MinCharges, def.MaxCharges + 1) + (depth - 1), def.ChargeCap);

        entity.Add(new WandComponent
        {
            Charges         = charges,
            MaxCharges      = def.ChargeCap,
            Infinite        = def.Infinite,
            RechargeScrollId = def.RechargeScroll,
        });

        entity.Add(new SpellEffect
        {
            SpellId      = def.SpellId,
            Targeting    = def.ParseTargetingMode(),
            Damage       = def.Damage,
            Radius       = def.Radius,
            Range        = def.Range,
            Duration     = def.Duration,
            MisfireChance = def.MisfireChance,
        });

        // ItemTag carries the canonical YAML type ID — required for identification and stacking.
        entity.Add(new ItemTag(spellId));

        // IdentifiableItem holds the two possible display names.
        entity.Add(new IdentifiableItem
        {
            IdentifiedName   = def.DisplayName,
            UnidentifiedName = "",
        });

        // Apply pre-identification decision. No-op if registry/pool/identRng are null.
        if (registry != null && pool != null && identRng != null)
            PreIdentification.Apply(entity, spellId, def.Category, registry, pool, identRng, difficulty);

        return entity;
    }
}
