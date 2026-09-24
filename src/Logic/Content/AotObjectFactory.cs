using System.Collections;
using UnderWarden.Logic.Balance;
using UnderWarden.Logic.ECS;
using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Custom YamlDotNet object factory for NativeAOT compatibility.
///
/// NativeAOT trims Activator.CreateInstance(Type) calls that use reflection
/// to find parameterless constructors. YamlDotNet's DefaultObjectFactory
/// relies on this pattern, which fails on iOS (NativeAOT mandatory).
///
/// This factory explicitly maps every YAML-deserialized type to a direct
/// constructor call — no reflection needed, fully AOT-safe.
/// </summary>
public sealed class AotObjectFactory : IObjectFactory
{
    /// <summary>
    /// When true, throws immediately on any unregistered type instead of falling back to
    /// Activator.CreateInstance. Use this in tests to simulate NativeAOT constraints —
    /// if the test passes with strict=true, iOS will not hit a missing-factory crash.
    /// </summary>
    private readonly bool _strict;

    public AotObjectFactory(bool strict = false) { _strict = strict; }

    // Lookup from Type to factory function. Populated once, used on every deserialize.
    private static readonly Dictionary<Type, Func<object>> _factories = new()
    {
        // ECS components — not deserialized from YAML, but registered for NativeAOT safety
        // if any future serialization path needs to reconstruct them from saved state.
        [typeof(AiComponent)] = () => new AiComponent(),

        // Content layer
        [typeof(EntitiesFile)] = () => new EntitiesFile(),
        [typeof(MonsterDefinition)] = () => new MonsterDefinition(),
        [typeof(MonsterAbilityDefinition)] = () => new MonsterAbilityDefinition(),
        [typeof(List<MonsterAbilityDefinition>)] = () => new List<MonsterAbilityDefinition>(),
        [typeof(MonsterStats)] = () => new MonsterStats(),
        [typeof(MonsterEquipmentConfig)] = () => new MonsterEquipmentConfig(),
        [typeof(WeightedItem)] = () => new WeightedItem(),
        [typeof(ItemDefinition)] = () => new ItemDefinition(),
        [typeof(ConsumableDefinition)] = () => new ConsumableDefinition(),
        [typeof(SpellDefinition)] = () => new SpellDefinition(),
        [typeof(FloorItemPoolEntry)] = () => new FloorItemPoolEntry(),
        [typeof(FloorItemPoolFile)] = () => new FloorItemPoolFile(),
        [typeof(LevelTemplatesFile)] = () => new LevelTemplatesFile(),
        [typeof(DepthWeightEntry)] = () => new DepthWeightEntry(),

        // Depth boons
        [typeof(DepthBoonConfig)] = () => new DepthBoonConfig(),
        [typeof(DepthBoonYamlEntry)] = () => new DepthBoonYamlEntry(),
        [typeof(Dictionary<int, DepthBoonYamlEntry>)] = () => new Dictionary<int, DepthBoonYamlEntry>(),

        // Balance layer
        [typeof(LevelOverride)] = () => new LevelOverride(),
        [typeof(GenerationParameters)] = () => new GenerationParameters(),
        [typeof(GuaranteedSpawns)] = () => new GuaranteedSpawns(),
        [typeof(SpawnEntry)] = () => new SpawnEntry(),
        [typeof(StairRules)] = () => new StairRules(),
        [typeof(SpawnRules)] = () => new SpawnRules(),
        [typeof(EncounterBudget)] = () => new EncounterBudget(),
        [typeof(SpecialRoomDef)] = () => new SpecialRoomDef(),
        [typeof(ScenarioDefinition)] = () => new ScenarioDefinition(),
        [typeof(ScenarioPlayer)] = () => new ScenarioPlayer(),
        [typeof(ScenarioMonster)] = () => new ScenarioMonster(),
        [typeof(ScenarioItem)] = () => new ScenarioItem(),

        // Standard library types YamlDotNet needs to create
        [typeof(Dictionary<string, MonsterDefinition>)] = () => new Dictionary<string, MonsterDefinition>(),
        [typeof(Dictionary<string, ItemDefinition>)] = () => new Dictionary<string, ItemDefinition>(),
        [typeof(Dictionary<string, ConsumableDefinition>)] = () => new Dictionary<string, ConsumableDefinition>(),
        [typeof(Dictionary<string, SpellDefinition>)] = () => new Dictionary<string, SpellDefinition>(),
        [typeof(Dictionary<string, LevelOverride>)] = () => new Dictionary<string, LevelOverride>(),
        // Nested dictionary types for top-level YAML parsing (avoids EntitiesFile wrapper)
        [typeof(Dictionary<string, Dictionary<string, MonsterDefinition>>)] = () => new Dictionary<string, Dictionary<string, MonsterDefinition>>(),
        [typeof(Dictionary<string, Dictionary<string, ItemDefinition>>)] = () => new Dictionary<string, Dictionary<string, ItemDefinition>>(),
        [typeof(Dictionary<string, Dictionary<string, ConsumableDefinition>>)] = () => new Dictionary<string, Dictionary<string, ConsumableDefinition>>(),
        [typeof(Dictionary<string, Dictionary<string, SpellDefinition>>)] = () => new Dictionary<string, Dictionary<string, SpellDefinition>>(),
        [typeof(RingDefinition)] = () => new RingDefinition(),
        [typeof(Dictionary<string, RingDefinition>)] = () => new Dictionary<string, RingDefinition>(),
        [typeof(Dictionary<string, Dictionary<string, RingDefinition>>)] = () => new Dictionary<string, Dictionary<string, RingDefinition>>(),
        [typeof(Dictionary<string, string>)] = () => new Dictionary<string, string>(),
        [typeof(Dictionary<string, double>)] = () => new Dictionary<string, double>(),
        [typeof(List<string>)] = () => new List<string>(),
        [typeof(List<int>)] = () => new List<int>(),
        [typeof(List<SpawnEntry>)] = () => new List<SpawnEntry>(),
        [typeof(List<SpecialRoomDef>)] = () => new List<SpecialRoomDef>(),
        [typeof(List<ScenarioMonster>)] = () => new List<ScenarioMonster>(),
        [typeof(List<ScenarioItem>)] = () => new List<ScenarioItem>(),
        [typeof(List<WeightedItem>)] = () => new List<WeightedItem>(),
        [typeof(List<DepthWeightEntry>)] = () => new List<DepthWeightEntry>(),
        [typeof(List<FloorItemPoolEntry>)] = () => new List<FloorItemPoolEntry>(),
        [typeof(Dictionary<string, List<WeightedItem>>)] = () => new Dictionary<string, List<WeightedItem>>(),
        // Props
        [typeof(PropsFile)] = () => new PropsFile(),
        [typeof(PropDefinition)] = () => new PropDefinition(),
        [typeof(Dictionary<string, PropDefinition>)] = () => new Dictionary<string, PropDefinition>(),
        [typeof(List<List<int>>)] = () => new List<List<int>>(),
        [typeof(Dictionary<string, double>)] = () => new Dictionary<string, double>(),
        [typeof(Dictionary<int, LevelOverride>)] = () => new Dictionary<int, LevelOverride>(),
        [typeof(List<double>)] = () => new List<double>(),
        [typeof(List<float>)] = () => new List<float>(),
        [typeof(int[])] = () => new int[0],

        // Interactive props + floor traps (interactive_props.yaml + floor_traps.yaml)
        [typeof(InteractivePropsFile)] = () => new InteractivePropsFile(),
        [typeof(InteractivePropDefinition)] = () => new InteractivePropDefinition(),
        [typeof(PropLootConfig)] = () => new PropLootConfig(),
        [typeof(WeightedPayloadEntry)] = () => new WeightedPayloadEntry(),
        [typeof(TrapPayloadDefinition)] = () => new TrapPayloadDefinition(),
        [typeof(TrapActionDefinition)] = () => new TrapActionDefinition(),
        [typeof(FloorTrapsFile)] = () => new FloorTrapsFile(),
        [typeof(FloorTrapDefinition)] = () => new FloorTrapDefinition(),
        [typeof(Dictionary<string, InteractivePropDefinition>)] = () => new Dictionary<string, InteractivePropDefinition>(),
        [typeof(Dictionary<string, TrapPayloadDefinition>)] = () => new Dictionary<string, TrapPayloadDefinition>(),
        [typeof(Dictionary<string, FloorTrapDefinition>)] = () => new Dictionary<string, FloorTrapDefinition>(),
        [typeof(Dictionary<string, int>)] = () => new Dictionary<string, int>(),
        [typeof(List<TrapActionDefinition>)] = () => new List<TrapActionDefinition>(),
        [typeof(List<WeightedPayloadEntry>)] = () => new List<WeightedPayloadEntry>(),
        // ECS components registered for forward-safety (forward-compatibility with any future save state path)
        [typeof(DestructiblePropComponent)] = () => new DestructiblePropComponent(),
        [typeof(FloorTrapComponent)] = () => new FloorTrapComponent(),
        [typeof(TrapPayloadComponent)] = () => new TrapPayloadComponent(),
        [typeof(WeaponAcidCoatingComponent)] = () => new WeaponAcidCoatingComponent(),
        [typeof(KeyItemComponent)] = () => new KeyItemComponent(),
        [typeof(LockableComponent)] = () => new LockableComponent(),

        // Loot system (loot_tags.yaml + loot_policy.yaml)
        [typeof(LootTagsFile)] = () => new LootTagsFile(),
        [typeof(LootTag)] = () => new LootTag(),
        [typeof(List<LootTag>)] = () => new List<LootTag>(),
        [typeof(LootPolicyFile)] = () => new LootPolicyFile(),
        [typeof(LootPolicyBandConfig)] = () => new LootPolicyBandConfig(),
        [typeof(LootPityConfig)] = () => new LootPityConfig(),
        [typeof(PityBandThreshold)] = () => new PityBandThreshold(),
        [typeof(Dictionary<string, LootPolicyBandConfig>)] = () => new Dictionary<string, LootPolicyBandConfig>(),
        [typeof(Dictionary<string, PityBandThreshold>)] = () => new Dictionary<string, PityBandThreshold>(),

        // Voice line YAML pools: trigger_id → string[]
        [typeof(Dictionary<string, List<string>>)] = () => new Dictionary<string, List<string>>(),

        // Voice tier metadata (voice_tiers.yaml + the dev fixture): ambient_cutoff_tier + ordered
        // families. Missing these made VoiceTierMetadata.LoadFromYaml throw under NativeAOT (iOS), so
        // the voice scheduler was never built and the ribbon stayed silent — desktop JIT hid it.
        [typeof(Voice.VoiceTierMetadata.MetaFileDto)] = () => new Voice.VoiceTierMetadata.MetaFileDto(),
        [typeof(Voice.VoiceTierMetadata.FamilyDto)] = () => new Voice.VoiceTierMetadata.FamilyDto(),
        [typeof(List<Voice.VoiceTierMetadata.FamilyDto>)] = () => new List<Voice.VoiceTierMetadata.FamilyDto>(),

        // Under-Warden memos (memos.yaml): key → MemoDtoEntry
        [typeof(MemoRegistry.MemoDtoEntry)] = () => new MemoRegistry.MemoDtoEntry(),
        [typeof(Dictionary<string, MemoRegistry.MemoDtoEntry>)] = () => new Dictionary<string, MemoRegistry.MemoDtoEntry>(),
    };

    public object Create(Type type)
    {
        if (_factories.TryGetValue(type, out var factory))
            return factory();

        // Strict mode: no fallback — simulate NativeAOT behaviour for testing.
        if (_strict)
            throw new InvalidOperationException(
                $"AotObjectFactory: no factory registered for {type.FullName}. " +
                $"Add it to AotObjectFactory._factories for NativeAOT compatibility.");

        // Dynamic fallback for generic collection types we didn't pre-register.
        // NativeAOT can construct generic types if the concrete type arguments are preserved.
        // This handles Dictionary<string, List<T>>, List<T>, etc. without manual registration.
        try
        {
            return Activator.CreateInstance(type)!;
        }
        catch
        {
            // Log the missing type so we can add it to the factory for next time
            System.Diagnostics.Debug.WriteLine(
                $"AotObjectFactory: Activator.CreateInstance failed for {type.FullName}");
            throw new InvalidOperationException(
                $"AotObjectFactory: no factory registered for {type.FullName}. " +
                $"Add it to AotObjectFactory._factories for NativeAOT compatibility.");
        }
    }

    public object CreatePrimitive(Type type) => Create(type);

    public bool GetDictionary(IObjectDescriptor descriptor, out IDictionary? dictionary, out Type[]? genericArguments)
    {
        dictionary = null;
        genericArguments = null;
        return false;
    }

    public Type GetValueType(Type type) => typeof(object);

    public void ExecuteOnDeserialized(object value) { }
    public void ExecuteOnDeserializing(object value) { }
    public void ExecuteOnSerializing(object value) { }
    public void ExecuteOnSerialized(object value) { }
}
