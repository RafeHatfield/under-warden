using UnderWarden.Logic.Content;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Validates that AotObjectFactory can create all YAML-mapped types.
/// Catches missing factory registrations before they hit iOS at runtime.
/// Run this after adding any new YAML content type.
/// </summary>
[TestFixture]
public class AotObjectFactoryTests
{
    private AotObjectFactory _factory = null!;

    [OneTimeSetUp]
    public void Setup() => _factory = new AotObjectFactory();

    // Content types
    [TestCase(typeof(EntitiesFile))]
    [TestCase(typeof(MonsterDefinition))]
    [TestCase(typeof(MonsterStats))]
    [TestCase(typeof(MonsterEquipmentConfig))]
    [TestCase(typeof(WeightedItem))]
    [TestCase(typeof(ItemDefinition))]
    [TestCase(typeof(ConsumableDefinition))]
    [TestCase(typeof(LevelTemplatesFile))]

    // Balance types
    [TestCase(typeof(UnderWarden.Logic.Balance.LevelOverride))]
    [TestCase(typeof(UnderWarden.Logic.Balance.GenerationParameters))]
    [TestCase(typeof(UnderWarden.Logic.Balance.GuaranteedSpawns))]
    [TestCase(typeof(UnderWarden.Logic.Balance.SpawnEntry))]
    [TestCase(typeof(UnderWarden.Logic.Balance.StairRules))]
    [TestCase(typeof(UnderWarden.Logic.Balance.SpawnRules))]
    [TestCase(typeof(UnderWarden.Logic.Balance.EncounterBudget))]
    [TestCase(typeof(UnderWarden.Logic.Balance.SpecialRoomDef))]
    [TestCase(typeof(UnderWarden.Logic.Balance.ScenarioDefinition))]
    [TestCase(typeof(UnderWarden.Logic.Balance.ScenarioPlayer))]
    [TestCase(typeof(UnderWarden.Logic.Balance.ScenarioMonster))]
    [TestCase(typeof(UnderWarden.Logic.Balance.ScenarioItem))]

    // Collection types used in YAML deserialization
    [TestCase(typeof(Dictionary<string, MonsterDefinition>))]
    [TestCase(typeof(Dictionary<string, ItemDefinition>))]
    [TestCase(typeof(Dictionary<string, ConsumableDefinition>))]
    [TestCase(typeof(Dictionary<string, UnderWarden.Logic.Balance.LevelOverride>))]
    [TestCase(typeof(Dictionary<string, Dictionary<string, MonsterDefinition>>))]
    [TestCase(typeof(Dictionary<string, Dictionary<string, ItemDefinition>>))]
    [TestCase(typeof(Dictionary<string, Dictionary<string, ConsumableDefinition>>))]
    [TestCase(typeof(Dictionary<string, List<WeightedItem>>))]
    [TestCase(typeof(List<UnderWarden.Logic.Balance.SpawnEntry>))]
    [TestCase(typeof(List<UnderWarden.Logic.Balance.SpecialRoomDef>))]
    [TestCase(typeof(List<UnderWarden.Logic.Balance.ScenarioMonster>))]
    [TestCase(typeof(List<UnderWarden.Logic.Balance.ScenarioItem>))]
    [TestCase(typeof(List<WeightedItem>))]
    [TestCase(typeof(List<string>))]
    [Description("AotObjectFactory can create all registered YAML-mapped types")]
    public void Create_RegisteredType_Succeeds(Type type)
    {
        var instance = _factory.Create(type);
        Assert.That(instance, Is.Not.Null, $"Factory returned null for {type.Name}");
        Assert.That(instance.GetType(), Is.EqualTo(type), $"Factory returned wrong type for {type.Name}");
    }
}
