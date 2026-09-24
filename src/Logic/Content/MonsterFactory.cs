using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Creates Entity instances from resolved MonsterDefinitions.
/// Bridges the content layer (YAML) to the ECS layer (entities + components).
/// Applies depth scaling at spawn time when depth is specified.
/// </summary>
public sealed class MonsterFactory
{
    private readonly Dictionary<string, MonsterDefinition> _definitions;
    private readonly EntityFactory _entityFactory;
    private readonly MonsterEquipmentSpawner? _equipmentSpawner;

    public MonsterFactory(
        Dictionary<string, MonsterDefinition> definitions,
        EntityFactory entityFactory,
        ItemFactory? itemFactory = null)
    {
        _definitions = definitions;
        _entityFactory = entityFactory;
        _equipmentSpawner = itemFactory != null ? new MonsterEquipmentSpawner(itemFactory) : null;
    }

    /// <summary>Returns the resolved definition for the given type ID, or null if not found.</summary>
    public MonsterDefinition? GetDefinition(string typeId)
        => _definitions.TryGetValue(typeId, out var def) ? def : null;

    /// <summary>
    /// Create a monster entity from a definition ID.
    /// Depth > 0 applies depth scaling to stats. RNG used for equipment spawning.
    /// Returns null if the ID is not found.
    /// </summary>
    public Entity? Create(string monsterId, int x = 0, int y = 0, int depth = 0, Core.SeededRandom? rng = null)
    {
        if (!_definitions.TryGetValue(monsterId, out var def))
            return null;

        var entity = CreateFromDefinition(def, x, y, depth, rng);
        // Attach the YAML key so MonsterKnowledgeSystem can key on species without re-looking up the definition.
        entity.Add(new ECS.SpeciesTag(monsterId));
        // Attach the threat archetype (if classified) so the soak harness can attribute kills + scan for
        // live escalators directly off the entity. Unclassified monsters get no tag.
        if (Balance.ThreatArchetypeTag.Parse(def.ThreatArchetype) is { } archetype)
            entity.Add(new Balance.ThreatArchetypeTag(archetype));
        return entity;
    }

    /// <summary>
    /// Create a monster entity directly from a definition.
    /// </summary>
    public Entity CreateFromDefinition(MonsterDefinition def, int x = 0, int y = 0, int depth = 0, Core.SeededRandom? rng = null)
    {
        var stats = def.Stats ?? new MonsterStats();
        string name = def.Name ?? FallbackName(def);

        var entity = _entityFactory.Create(name, x, y, blocksMovement: def.Blocks);

        int hp = stats.Hp;
        int damageMin = stats.DamageMin;
        int damageMax = stats.DamageMax;
        int accuracy = stats.Accuracy;

        // Apply depth scaling if depth specified
        if (depth > 0)
        {
            var mult = DepthScaling.GetForTags(depth, def.Tags);
            hp = DepthScaling.ScaleHp(hp, mult.Hp);
            damageMin = DepthScaling.ScaleStat(damageMin, mult.Damage);
            damageMax = DepthScaling.ScaleStat(damageMax, mult.Damage);
            accuracy = DepthScaling.ScaleStat(accuracy, mult.ToHit);
        }

        var fighter = new Fighter(
            hp: hp,
            defense: stats.Defense,
            power: stats.Power,
            xp: stats.Xp,
            damageMin: damageMin,
            damageMax: damageMax,
            strength: stats.Strength,
            dexterity: stats.Dexterity,
            constitution: stats.Constitution,
            accuracy: accuracy,
            evasion: stats.Evasion)
        {
            CanOpenDoors = def.CanOpenDoors,
            NaturalDamageType = def.NaturalDamageType,
        };
        entity.Add(fighter);

        // Damage resistance/vulnerability
        if (def.DamageResistance != null || def.DamageVulnerability != null)
        {
            entity.Add(new DamageModifiers
            {
                Resistance = def.DamageResistance,
                Vulnerability = def.DamageVulnerability,
            });
        }

        // Speed bonus for momentum system
        if (def.SpeedBonus > 0)
            entity.Add(new SpeedBonusTracker(baseRatio: def.SpeedBonus));

        // Equipment spawning (weapons, armor from weighted pools)
        if (_equipmentSpawner != null && def.Equipment != null && rng != null)
            _equipmentSpawner.SpawnEquipment(entity, def.Equipment, rng);

        // AI component — controls behavior dispatch and item-seeking
        entity.Add(new AiComponent
        {
            AiType       = def.AiType,
            Faction      = def.Faction,
            Tags         = def.Tags ?? [],
            CanSeekItems = def.CanSeekItems,
            SeekDistance = def.SeekDistance,
            InventorySize = def.InventorySize,
        });

        // Specialized AI state components — attached based on ai_type.
        switch (def.AiType)
        {
            case "skirmisher":
                entity.Add(new SkirmisherComponent());
                break;
            case "orc_shaman":
                entity.Add(new OrcShamanComponent());
                break;
            case "orc_chieftain":
                entity.Add(new OrcChieftainComponent());
                break;
            case "skeleton":
                entity.Add(new ShieldWallComponent());
                break;
            case "necromancer":
            case "plague_necromancer":
                entity.Add(new NecromancerAiComponent
                {
                    RaiseRange           = def.RaiseDeadRange,
                    RaiseCooldown        = def.RaiseDeadCooldownTurns,
                    DangerRadius         = def.DangerRadiusFromPlayer,
                    PreferredDistanceMin = def.PreferredDistanceMin,
                    PreferredDistanceMax = def.PreferredDistanceMax,
                });
                break;
            case "lich":
                // Lich gets both necromancer behavior AND lich-specific abilities
                entity.Add(new NecromancerAiComponent
                {
                    RaiseRange           = def.RaiseDeadRange,
                    RaiseCooldown        = def.RaiseDeadCooldownTurns,
                    DangerRadius         = def.DangerRadiusFromPlayer,
                    PreferredDistanceMin = def.PreferredDistanceMin,
                    PreferredDistanceMax = def.PreferredDistanceMax,
                });
                entity.Add(new LichAiComponent
                {
                    SoulBoltRange        = def.SoulBoltRange,
                    SoulBoltDamagePct    = def.SoulBoltDamagePct,
                    SoulBoltCooldownTurns = def.SoulBoltCooldownTurns,
                    CommandTheDeadRadius = def.CommandTheDeadRadius,
                    DeathSiphonRadius    = def.DeathSiphonRadius,
                    SummonMonsterId      = def.SummonMonsterId ?? "zombie",
                });
                break;
        }

        // Regeneration: monsters with regeneration_amount get InnateRegenComponent — a permanent,
        // non-duration component. This differs from RegenerationEffect (used for ring/potion regen),
        // which is a timed status effect. The separation allows AcidEffect to suppress only innate
        // regeneration without affecting player ring/potion regeneration.
        if (def.RegenerationAmount > 0)
        {
            entity.Add(new InnateRegenComponent { HealPerTurn = def.RegenerationAmount });
        }

        // On-hit effect: attach for monsters that apply status effects on hit (spiders, fire_beetle).
        if (def.OnHitEffect != null && def.OnHitEffectDuration > 0)
            entity.Add(new OnHitEffectComponent(def.OnHitEffect, def.OnHitEffectDuration));

        // Inventory for monsters that can seek and carry items
        if (def.CanSeekItems && def.InventorySize > 0)
            entity.Add(new Inventory());

        // Equipment for monsters that can seek items — ensures auto-equip works even when
        // the monster has no YAML equipment config (MonsterEquipmentSpawner creates it
        // conditionally, so we guarantee it here for all item-seekers).
        if (def.CanSeekItems && entity.Get<Equipment>() == null)
            entity.Add(new Equipment());

        // Split-under-pressure: attach tracker when split config is present
        if (def.SplitTriggerHpPct.HasValue && def.SplitChildType != null)
        {
            entity.Add(new SplitTracker(
                triggerHpPct: def.SplitTriggerHpPct.Value,
                childType: def.SplitChildType,
                minChildren: def.SplitMinChildren,
                maxChildren: def.SplitMaxChildren,
                weights: def.SplitWeights?.ToArray()));
        }

        // Corrosion: attach component for acid-bearing monsters
        if (def.CorrosionChance > 0)
            entity.Add(new CorrosionComponent(def.CorrosionChance));

        // Life drain: wraith heals on melee hit
        if (def.LifeDrainPct > 0)
            entity.Add(new LifeDrainComponent(def.LifeDrainPct));

        // Effect transfer on drain: wraith absorbs poison/bleed from target on melee hit
        if (def.TransfersEffectsOnHit)
            entity.Add(new TransfersEffectsOnHitComponent());

        // Engulf on hit: slimes apply EngulfedEffect to the player on any successful melee hit.
        if (def.EngulfsOnHit)
            entity.Add(new EngulfsOnHitTag());

        // Status immunities: wraith/lich immune to confusion/slow/fear/etc.
        if (def.StatusImmunities is { Count: > 0 })
            entity.Add(new StatusImmunityComponent(def.StatusImmunities));

        // Species abilities usable during possession (e.g. Hall Warden grapple).
        // Infrastructure-only until ability-bearing species ship.
        if (def.Abilities?.Count > 0)
            entity.Add(new HostAbilityComponent { Abilities = def.Abilities });

        return entity;
    }

    /// <summary>
    /// The EntityFactory shared by all content factories in this session.
    /// Exposed so callers (e.g. DungeonFloorBuilder) can read NextId to avoid ID collisions
    /// when allocating IDs for map-placed entities after starting gear has been created.
    /// </summary>
    public EntityFactory EntityFactory => _entityFactory;

    /// <summary>All available monster IDs.</summary>
    public IEnumerable<string> AvailableIds => _definitions.Keys;

    /// <summary>
    /// Try to get a monster definition by ID without creating an entity.
    /// Returns false if the ID is not registered.
    /// </summary>
    public bool TryGetDefinition(string monsterId, out MonsterDefinition? def)
        => _definitions.TryGetValue(monsterId, out def);

    private static string FallbackName(MonsterDefinition def) => def.Char ?? "?";
}
