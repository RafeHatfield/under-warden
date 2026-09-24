using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;


namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for the identification system: AppearancePool, IdentificationRegistry,
/// pre-identification, use/equip triggers, scroll of identify, wand charging gate,
/// stacking, and floor carry-forward.
/// </summary>
[TestFixture]
public class IdentificationTests
{
    // ─── Test data helpers ──────────────────────────────────────────────────────

    // A minimal set of identifiable item definitions for pool construction.
    private static readonly List<(string id, ItemCategory category)> SampleItems =
    [
        ("healing_potion",    ItemCategory.Potion),
        ("speed_potion",      ItemCategory.Potion),
        ("scroll_of_light",   ItemCategory.Scroll),
        ("scroll_of_fire",    ItemCategory.Scroll),
        ("wand_of_lightning", ItemCategory.Wand),
        ("ring_of_protection",ItemCategory.Ring),
    ];

    private static AppearancePool BuildPool(int seed) => new(SampleItems, seed);

    // ─── AppearancePool — determinism ───────────────────────────────────────────

    [Test]
    public void AppearancePool_SameSeed_SameAssignments()
    {
        var pool1 = BuildPool(1337);
        var pool2 = BuildPool(1337);

        foreach (var (id, _) in SampleItems)
        {
            Assert.That(pool1.GetDescriptor(id),    Is.EqualTo(pool2.GetDescriptor(id)),
                $"Descriptor for '{id}' should be deterministic");
            Assert.That(pool1.GetMysterySprite(id), Is.EqualTo(pool2.GetMysterySprite(id)),
                $"Mystery sprite for '{id}' should be deterministic");
            Assert.That(pool1.GetDisplayName(id),   Is.EqualTo(pool2.GetDisplayName(id)),
                $"Display name for '{id}' should be deterministic");
        }
    }

    [Test]
    public void AppearancePool_DifferentSeed_DifferentAssignments()
    {
        var pool1 = BuildPool(1337);
        var pool2 = BuildPool(9999);

        // It's probabilistically near-certain that at least one assignment differs.
        bool anyDifferent = SampleItems.Any(item =>
            pool1.GetDescriptor(item.id) != pool2.GetDescriptor(item.id));

        Assert.That(anyDifferent, Is.True,
            "Different seeds should produce different descriptor assignments");
    }

    [Test]
    public void AppearancePool_PotionSprites_CycleThroughAllThreeKeys()
    {
        // With many potion types, each mystery sprite key in the cycle should appear at least once.
        var manyPotions = Enumerable.Range(0, 9)
            .Select(i => ($"potion_{i}", ItemCategory.Potion))
            .ToList();

        var pool = new AppearancePool(manyPotions, seed: 42);

        var sprites = manyPotions.Select(p => pool.GetMysterySprite(p.Item1)).ToList();

        Assert.That(sprites, Does.Contain("unknown_potion_a"), "Should cycle to unknown_potion_a");
        Assert.That(sprites, Does.Contain("unknown_potion_b"), "Should cycle to unknown_potion_b");
        Assert.That(sprites, Does.Contain("unknown_potion_c"), "Should cycle to unknown_potion_c");
    }

    [Test]
    public void AppearancePool_DescriptorPoolExhausted_FallsBackGracefully()
    {
        // 16+ potions exceed the 15-descriptor pool.
        var tooManyPotions = Enumerable.Range(0, 16)
            .Select(i => ($"overflow_potion_{i}", ItemCategory.Potion))
            .ToList();

        // Should not throw — overflow types get fallback names.
        AppearancePool pool = null!;
        Assert.DoesNotThrow(() => pool = new AppearancePool(tooManyPotions, seed: 42));

        // All types should have a non-null descriptor.
        foreach (var (id, _) in tooManyPotions)
            Assert.That(pool.GetDescriptor(id), Is.Not.Null.And.Not.Empty,
                $"Type '{id}' should have a descriptor even if pool was exhausted");
    }

    [Test]
    public void AppearancePool_DisplayName_FormatsByCategory()
    {
        var pool = BuildPool(42);

        // Potion: "X Potion"
        var potionName = pool.GetDisplayName("healing_potion");
        Assert.That(potionName, Does.EndWith(" Potion"), $"Potion unidentified name should end with ' Potion': {potionName}");

        // Scroll: "Scroll labeled X"
        var scrollName = pool.GetDisplayName("scroll_of_light");
        Assert.That(scrollName, Does.StartWith("Scroll labeled "), $"Scroll unidentified name should start with 'Scroll labeled ': {scrollName}");

        // Wand: "X Wand"
        var wandName = pool.GetDisplayName("wand_of_lightning");
        Assert.That(wandName, Does.EndWith(" Wand"), $"Wand unidentified name should end with ' Wand': {wandName}");

        // Ring: "X Ring"
        var ringName = pool.GetDisplayName("ring_of_protection");
        Assert.That(ringName, Does.EndWith(" Ring"), $"Ring unidentified name should end with ' Ring': {ringName}");
    }

    // ─── IdentificationRegistry ─────────────────────────────────────────────────

    [Test]
    public void Registry_Identify_MarksAsIdentified()
    {
        var registry = new IdentificationRegistry();
        registry.Identify("healing_potion");
        Assert.That(registry.IsIdentified("healing_potion"), Is.True);
    }

    [Test]
    public void Registry_Identify_ReturnsTrue_OnlyFirstTime()
    {
        var registry = new IdentificationRegistry();
        bool first  = registry.Identify("healing_potion");
        bool second = registry.Identify("healing_potion");
        Assert.That(first,  Is.True,  "First identification should return true");
        Assert.That(second, Is.False, "Second identification should return false (already known)");
    }

    [Test]
    public void Registry_MarkUnidentified_PreventsFutureRoll()
    {
        var registry = new IdentificationRegistry();
        registry.MarkUnidentified("healing_potion");
        Assert.That(registry.IsIdentified("healing_potion"), Is.False);
        Assert.That(registry.HasDecision("healing_potion"), Is.True);
    }

    [Test]
    public void Registry_HasDecision_TrueAfterEitherCall()
    {
        var registry = new IdentificationRegistry();

        Assert.That(registry.HasDecision("a"), Is.False);

        registry.Identify("a");
        Assert.That(registry.HasDecision("a"), Is.True);

        var registry2 = new IdentificationRegistry();
        registry2.MarkUnidentified("b");
        Assert.That(registry2.HasDecision("b"), Is.True);
    }

    [Test]
    public void Registry_Identify_OverridesMarkUnidentified()
    {
        // Pre-identified types can still be identified later (e.g. via use).
        var registry = new IdentificationRegistry();
        registry.MarkUnidentified("healing_potion");
        Assert.That(registry.IsIdentified("healing_potion"), Is.False);

        registry.Identify("healing_potion");
        Assert.That(registry.IsIdentified("healing_potion"), Is.True);
    }

    // ─── Pre-identification ──────────────────────────────────────────────────────

    [Test]
    public void PreIdentification_DecisionCachedPerType_NotRerolled()
    {
        var registry = new IdentificationRegistry();
        var pool     = BuildPool(42);
        var rng      = new SeededRandom(1337);

        // Apply 10 times for the same type — only the first roll matters.
        var itemIds = Enumerable.Range(0, 10)
            .Select(_ =>
            {
                var entity = new Entity(0, "Healing Potion");
                entity.Add(new ItemTag("healing_potion"));
                entity.Add(new IdentifiableItem { IdentifiedName = "Healing Potion" });
                PreIdentification.Apply(entity, "healing_potion", ItemCategory.Potion,
                    registry, pool, rng, Difficulty.Medium);
                return entity;
            }).ToList();

        // After 10 items, the registry should still have exactly one decision for this type.
        bool identified = registry.IsIdentified("healing_potion");
        bool hasDecision = registry.HasDecision("healing_potion");
        Assert.That(hasDecision, Is.True);

        // All subsequent calls must not flip the decision.
        for (int i = 0; i < 5; i++)
        {
            var entity = new Entity(0, "Healing Potion");
            entity.Add(new ItemTag("healing_potion"));
            entity.Add(new IdentifiableItem { IdentifiedName = "Healing Potion" });
            PreIdentification.Apply(entity, "healing_potion", ItemCategory.Potion,
                registry, pool, rng, Difficulty.Medium);
            // Decision must remain stable
            Assert.That(registry.IsIdentified("healing_potion"), Is.EqualTo(identified),
                "Pre-ID decision must be stable across multiple instances of same type");
        }
    }

    [Test]
    public void PreIdentification_MediumDifficulty_ConvergesTo50Pct()
    {
        // Statistical test: over 500 independent runs (each fresh registry), potion pre-ID
        // at medium difficulty should converge to ~50%. Accept 35-65% as passing range.
        int identifiedCount = 0;
        int totalRuns = 500;

        for (int seed = 0; seed < totalRuns; seed++)
        {
            var registry = new IdentificationRegistry();
            var pool     = BuildPool(seed);
            var rng      = new SeededRandom(seed);

            var entity = new Entity(0, "Healing Potion");
            entity.Add(new ItemTag("healing_potion"));
            entity.Add(new IdentifiableItem { IdentifiedName = "Healing Potion" });
            PreIdentification.Apply(entity, "healing_potion", ItemCategory.Potion,
                registry, pool, rng, Difficulty.Medium);

            if (registry.IsIdentified("healing_potion"))
                identifiedCount++;
        }

        double rate = (double)identifiedCount / totalRuns;
        Assert.That(rate, Is.InRange(0.35, 0.65),
            $"Expected ~50% pre-ID rate for potions at medium difficulty; got {rate:P1}");
    }

    [Test]
    public void PreIdentification_HardDifficulty_VeryRareWands()
    {
        // Wands at Hard: 0% pre-ID. Over 100 runs, none should be identified.
        int identifiedCount = 0;
        for (int seed = 0; seed < 100; seed++)
        {
            var registry = new IdentificationRegistry();
            var pool     = BuildPool(seed);
            var rng      = new SeededRandom(seed);

            var entity = new Entity(0, "Wand of Lightning");
            entity.Add(new ItemTag("wand_of_lightning"));
            entity.Add(new IdentifiableItem { IdentifiedName = "Wand of Lightning" });
            PreIdentification.Apply(entity, "wand_of_lightning", ItemCategory.Wand,
                registry, pool, rng, Difficulty.Hard);

            if (registry.IsIdentified("wand_of_lightning"))
                identifiedCount++;
        }
        Assert.That(identifiedCount, Is.EqualTo(0), "Hard difficulty wands should never be pre-identified (0% rate)");
    }

    // ─── Factory integration ─────────────────────────────────────────────────────

    private const string ConsumableYaml = """
        consumables:
          healing_potion:
            name: "Healing Potion"
            heal_amount: 40
            char: "!"
            color: [127, 0, 255]
        """;

    private const string ScrollWandYaml = """
        scrolls:
          scroll_of_lightning:
            name: "Scroll of Lightning"
            spell_id: "lightning"
            targeting: "auto_closest"
            damage: 40
            range: 5
            char: "~"
            color: [255, 255, 100]
        wands:
          wand_of_lightning:
            name: "Wand of Lightning"
            spell_id: "lightning"
            targeting: "auto_closest"
            damage: 40
            range: 5
            is_wand: true
            min_charges: 3
            max_charges: 6
            charge_cap: 10
            recharge_scroll: "lightning"
            char: "/"
            color: [255, 255, 100]
        """;

    private static (ConsumableFactory consumable, SpellItemFactory spell) BuildFactories()
    {
        var loader = new ContentLoader();
        var consumableDefs = loader.LoadConsumables(ConsumableYaml);
        var spellDefs      = loader.LoadSpellItems(ScrollWandYaml);
        var ef = new EntityFactory();
        return (new ConsumableFactory(consumableDefs, ef), new SpellItemFactory(spellDefs, ef));
    }

    [Test]
    public void ConsumableFactory_AttachesItemTag()
    {
        var (factory, _) = BuildFactories();
        var entity = factory.Create("healing_potion")!;
        Assert.That(entity.Get<ItemTag>(), Is.Not.Null, "ConsumableFactory must attach ItemTag");
        Assert.That(entity.Get<ItemTag>()!.TypeId, Is.EqualTo("healing_potion"));
    }

    [Test]
    public void ConsumableFactory_AttachesIdentifiableItem()
    {
        var (factory, _) = BuildFactories();
        var entity = factory.Create("healing_potion")!;
        var idComp = entity.Get<IdentifiableItem>();
        Assert.That(idComp, Is.Not.Null, "ConsumableFactory must attach IdentifiableItem");
        Assert.That(idComp!.IdentifiedName, Is.EqualTo("Healing Potion"));
    }

    [Test]
    public void SpellItemFactory_AttachesItemTag_Scroll()
    {
        var (_, factory) = BuildFactories();
        var entity = factory.CreateScroll("scroll_of_lightning")!;
        Assert.That(entity.Get<ItemTag>(), Is.Not.Null);
        Assert.That(entity.Get<ItemTag>()!.TypeId, Is.EqualTo("scroll_of_lightning"));
    }

    [Test]
    public void SpellItemFactory_AttachesItemTag_Wand()
    {
        var (_, factory) = BuildFactories();
        var rng = new SeededRandom(42);
        var entity = factory.CreateWand("wand_of_lightning", rng)!;
        Assert.That(entity.Get<ItemTag>(), Is.Not.Null);
        Assert.That(entity.Get<ItemTag>()!.TypeId, Is.EqualTo("wand_of_lightning"));
    }

    // ─── Identification on use/equip ─────────────────────────────────────────────

    private static GameState CreateStateWithRegistry(bool registerPotionAsUnidentified = true)
    {
        var rng     = new SeededRandom(1337);
        var map     = GameMap.CreateArena(15, 15);
        var player  = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        player.Add(new Inventory());
        map.RegisterEntity(player);

        var registry = new IdentificationRegistry();
        if (registerPotionAsUnidentified)
            registry.MarkUnidentified("healing_potion");

        var pool = BuildPool(1337);

        return new GameState(player, [], map, rng)
        {
            IdentificationRegistry = registry,
            AppearancePool         = pool,
        };
    }

    [Test]
    public void UsePotion_UnknownType_IdentifiesType_ToastShown()
    {
        var state = CreateStateWithRegistry(registerPotionAsUnidentified: true);

        var potion = new Entity(1, "Healing Potion");
        potion.Add(new Consumable(healAmount: 40));
        potion.Add(new ItemTag("healing_potion"));
        potion.Add(new IdentifiableItem { IdentifiedName = "Healing Potion", UnidentifiedName = "Fizzy Potion" });
        state.PlayerInventory!.Add(potion);

        var result = TurnController.ProcessTurn(state, PlayerAction.UseItem(potion));

        var identEvent = result.Events.OfType<IdentificationEvent>().FirstOrDefault();
        Assert.That(identEvent, Is.Not.Null, "Using an unidentified potion should emit IdentificationEvent");
        Assert.That(identEvent!.IdentifiedName, Is.EqualTo("Healing Potion"));
        Assert.That(state.IdentificationRegistry!.IsIdentified("healing_potion"), Is.True);
    }

    [Test]
    public void UsePotion_AlreadyIdentified_NoToast()
    {
        var state = CreateStateWithRegistry(registerPotionAsUnidentified: false);
        // Mark as already identified before use
        state.IdentificationRegistry!.Identify("healing_potion");

        var potion = new Entity(1, "Healing Potion");
        potion.Add(new Consumable(healAmount: 40));
        potion.Add(new ItemTag("healing_potion"));
        potion.Add(new IdentifiableItem { IdentifiedName = "Healing Potion", UnidentifiedName = "Fizzy Potion" });
        state.PlayerInventory!.Add(potion);

        var result = TurnController.ProcessTurn(state, PlayerAction.UseItem(potion));

        var identEvents = result.Events.OfType<IdentificationEvent>().ToList();
        Assert.That(identEvents, Is.Empty, "Using an already-identified potion should NOT emit IdentificationEvent");
    }

    [Test]
    public void EquipRing_UnknownType_IdentifiesType()
    {
        var state = CreateStateWithRegistry();
        state.IdentificationRegistry!.MarkUnidentified("ring_of_protection");

        var ring = new Entity(2, "Ring of Protection");
        ring.Add(new Equippable(EquipmentSlot.LeftRing));
        ring.Add(new ItemTag("ring_of_protection"));
        ring.Add(new IdentifiableItem { IdentifiedName = "Ring of Protection", UnidentifiedName = "Jade Ring" });
        state.PlayerInventory!.Add(ring);

        var result = TurnController.ProcessTurn(state, PlayerAction.Equip(ring));

        var identEvent = result.Events.OfType<IdentificationEvent>().FirstOrDefault();
        Assert.That(identEvent, Is.Not.Null, "Equipping an unidentified ring should emit IdentificationEvent");
        Assert.That(identEvent!.Trigger, Is.EqualTo("equipped"));
        Assert.That(state.IdentificationRegistry!.IsIdentified("ring_of_protection"), Is.True);
    }

    // ─── Scroll of Identify effect ───────────────────────────────────────────────

    private static GameState CreateScrollOfIdentifyState()
    {
        var rng    = new SeededRandom(42);
        var map    = GameMap.CreateArena(15, 15);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        player.Add(new Inventory());
        map.RegisterEntity(player);

        var registry = new IdentificationRegistry();
        var pool     = BuildPool(42);

        return new GameState(player, [], map, rng)
        {
            IdentificationRegistry = registry,
            AppearancePool         = pool,
        };
    }

    [Test]
    public void ScrollOfIdentify_1to3RandomTypes_Identified()
    {
        var state = CreateScrollOfIdentifyState();
        var registry = state.IdentificationRegistry!;

        // Add 5 unidentified item types to inventory.
        for (int i = 0; i < 5; i++)
        {
            string typeId = $"item_{i}";
            registry.MarkUnidentified(typeId);
            var item = new Entity(i + 1, $"Unknown Item {i}");
            item.Add(new Consumable(healAmount: 0));
            item.Add(new ItemTag(typeId));
            item.Add(new IdentifiableItem { IdentifiedName = $"True Item {i}", UnidentifiedName = $"Rune {i}" });
            state.PlayerInventory!.Add(item);
        }

        // Create and add scroll of identify.
        var scroll = new Entity(10, "Scroll of Identify");
        scroll.Add(new Consumable(healAmount: 0));
        scroll.Add(new SpellEffect { SpellId = "identify", Targeting = TargetingMode.Self });
        scroll.Add(new ItemTag("scroll_of_identify"));
        scroll.Add(new IdentifiableItem { IdentifiedName = "Scroll of Identify", UnidentifiedName = "Scroll labeled FNORD" });
        registry.MarkUnidentified("scroll_of_identify");
        state.PlayerInventory!.Add(scroll);

        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(scroll));

        var identEvents = result.Events.OfType<IdentificationEvent>().ToList();

        // Scroll identifies itself (trigger: "used") + 1-3 inventory types.
        // The scroll self-identification fires first via TryIdentifyOnUse in TurnController.
        var selfIdent = identEvents.FirstOrDefault(e => e.TypeId == "scroll_of_identify");
        Assert.That(selfIdent, Is.Not.Null, "Scroll of Identify should identify itself when used");

        var otherIdents = identEvents.Where(e => e.TypeId != "scroll_of_identify").ToList();
        Assert.That(otherIdents.Count, Is.InRange(1, 3),
            $"Scroll of Identify should identify 1-3 other types; got {otherIdents.Count}");
    }

    [Test]
    public void ScrollOfIdentify_EmptyUnidentifiedInventory_StillConsumed()
    {
        var state = CreateScrollOfIdentifyState();

        // Nothing unidentified in inventory.
        var scroll = new Entity(10, "Scroll of Identify");
        scroll.Add(new Consumable(healAmount: 0));
        scroll.Add(new SpellEffect { SpellId = "identify", Targeting = TargetingMode.Self });
        scroll.Add(new ItemTag("scroll_of_identify"));
        scroll.Add(new IdentifiableItem { IdentifiedName = "Scroll of Identify" });
        state.PlayerInventory!.Add(scroll);

        var result = TurnController.ProcessTurn(state, PlayerAction.CastSpell(scroll));

        // Scroll should be consumed (removed from inventory).
        Assert.That(state.PlayerInventory.Items.Contains(scroll), Is.False,
            "Scroll should be consumed even with empty unidentified inventory");

        // No secondary identification events (other than the scroll itself).
        var identEvents = result.Events.OfType<IdentificationEvent>().ToList();
        Assert.That(identEvents.Count, Is.LessThanOrEqualTo(1),
            "Only the scroll itself (or zero) IdentificationEvents should fire");
    }

    // ─── Wand charging gate ──────────────────────────────────────────────────────

    private static GameState CreateWandRechargeState(
        bool scrollIdentified = false, bool wandIdentified = false)
    {
        var rng    = new SeededRandom(1337);
        var map    = GameMap.CreateArena(15, 15);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        player.Add(new Inventory());
        map.RegisterEntity(player);

        var registry = new IdentificationRegistry();
        if (scrollIdentified) registry.Identify("scroll_of_lightning");
        else                  registry.MarkUnidentified("scroll_of_lightning");
        if (wandIdentified)   registry.Identify("wand_of_lightning");
        else                  registry.MarkUnidentified("wand_of_lightning");

        return new GameState(player, [], map, rng)
        {
            IdentificationRegistry = registry,
            AppearancePool         = BuildPool(1337),
        };
    }

    [Test]
    public void WandCharging_ScrollUnidentified_GoesToInventory()
    {
        var state = CreateWandRechargeState(scrollIdentified: false, wandIdentified: true);

        // Wand (identified) in inventory.
        var wand = new Entity(10, "Wand of Lightning");
        wand.Add(new WandComponent { Charges = 3, MaxCharges = 10, RechargeScrollId = "lightning" });
        wand.Add(new SpellEffect { SpellId = "lightning" });
        wand.Add(new ItemTag("wand_of_lightning"));
        state.PlayerInventory!.Add(wand);

        // Scroll on floor one step south.
        var scroll = new Entity(20, "Scroll of Lightning");
        scroll.Add(new Consumable(healAmount: 0) { StackSize = 1 });
        scroll.Add(new SpellEffect { SpellId = "lightning" });
        scroll.Add(new ItemTag("scroll_of_lightning"));
        scroll.X = 5; scroll.Y = 6;
        state.FloorItems.Add(scroll);
        state.Map.RegisterEntity(scroll);

        TurnController.ProcessTurn(state, PlayerAction.MoveTo(5, 6));

        // Unidentified scroll must NOT recharge the wand; it goes to inventory instead.
        Assert.That(wand.Require<WandComponent>().Charges, Is.EqualTo(3),
            "Unidentified scroll should not recharge the wand");
        Assert.That(state.PlayerInventory.Items, Does.Contain(scroll),
            "Unidentified scroll should be in inventory after pickup");
    }

    [Test]
    public void WandCharging_WandUnidentified_ScrollGoesToInventory()
    {
        var state = CreateWandRechargeState(scrollIdentified: true, wandIdentified: false);

        var wand = new Entity(10, "Wand of Lightning");
        wand.Add(new WandComponent { Charges = 3, MaxCharges = 10, RechargeScrollId = "lightning" });
        wand.Add(new SpellEffect { SpellId = "lightning" });
        wand.Add(new ItemTag("wand_of_lightning"));
        state.PlayerInventory!.Add(wand);

        var scroll = new Entity(20, "Scroll of Lightning");
        scroll.Add(new Consumable(healAmount: 0) { StackSize = 1 });
        scroll.Add(new SpellEffect { SpellId = "lightning" });
        scroll.Add(new ItemTag("scroll_of_lightning"));
        scroll.X = 5; scroll.Y = 6;
        state.FloorItems.Add(scroll);
        state.Map.RegisterEntity(scroll);

        TurnController.ProcessTurn(state, PlayerAction.MoveTo(5, 6));

        Assert.That(wand.Require<WandComponent>().Charges, Is.EqualTo(3),
            "Identified scroll should not recharge an unidentified wand");
        Assert.That(state.PlayerInventory.Items, Does.Contain(scroll),
            "Scroll should go to inventory when wand is unidentified");
    }

    [Test]
    public void WandCharging_BothIdentified_Recharges()
    {
        var state = CreateWandRechargeState(scrollIdentified: true, wandIdentified: true);

        var wand = new Entity(10, "Wand of Lightning");
        wand.Add(new WandComponent { Charges = 3, MaxCharges = 10, RechargeScrollId = "lightning" });
        wand.Add(new SpellEffect { SpellId = "lightning" });
        wand.Add(new ItemTag("wand_of_lightning"));
        state.PlayerInventory!.Add(wand);

        var scroll = new Entity(20, "Scroll of Lightning");
        scroll.Add(new Consumable(healAmount: 0) { StackSize = 1 });
        scroll.Add(new SpellEffect { SpellId = "lightning" });
        scroll.Add(new ItemTag("scroll_of_lightning"));
        scroll.X = 5; scroll.Y = 6;
        state.FloorItems.Add(scroll);
        state.Map.RegisterEntity(scroll);

        TurnController.ProcessTurn(state, PlayerAction.MoveTo(5, 6));

        Assert.That(wand.Require<WandComponent>().Charges, Is.EqualTo(4),
            "Both identified: scroll should recharge the wand");
        Assert.That(state.PlayerInventory.Items, Does.Not.Contain(scroll),
            "Scroll consumed for recharge should not be in inventory");
    }

    // ─── Stacking ────────────────────────────────────────────────────────────────

    [Test]
    public void Stacking_SameTypeUnidentified_Stacks()
    {
        var inventory = new Inventory();

        // Two "Fizzy Potion" entities with same TypeId — should stack.
        var p1 = new Entity(1, "Healing Potion");
        p1.Add(new Consumable(healAmount: 40));
        p1.Add(new ItemTag("healing_potion"));
        p1.Add(new IdentifiableItem { IdentifiedName = "Healing Potion", UnidentifiedName = "Fizzy Potion" });

        var p2 = new Entity(2, "Healing Potion");
        p2.Add(new Consumable(healAmount: 40));
        p2.Add(new ItemTag("healing_potion"));
        p2.Add(new IdentifiableItem { IdentifiedName = "Healing Potion", UnidentifiedName = "Fizzy Potion" });

        inventory.Add(p1);
        inventory.Add(p2);

        Assert.That(inventory.Count, Is.EqualTo(1), "Same-type consumables should stack");
        Assert.That(inventory.Items[0].Require<Consumable>().StackSize, Is.EqualTo(2));
    }

    [Test]
    public void Stacking_IdentifyType_StackIntact()
    {
        var registry = new IdentificationRegistry();
        var inventory = new Inventory();

        var p1 = new Entity(1, "Healing Potion");
        p1.Add(new Consumable(healAmount: 40));
        p1.Add(new ItemTag("healing_potion"));
        p1.Add(new IdentifiableItem { IdentifiedName = "Healing Potion", UnidentifiedName = "Fizzy Potion" });

        var p2 = new Entity(2, "Healing Potion");
        p2.Add(new Consumable(healAmount: 40));
        p2.Add(new ItemTag("healing_potion"));
        p2.Add(new IdentifiableItem { IdentifiedName = "Healing Potion", UnidentifiedName = "Fizzy Potion" });

        inventory.Add(p1);
        inventory.Add(p2);

        // Identify the type — should not break the stack (stack is TypeId-based, not name-based).
        registry.Identify("healing_potion");

        Assert.That(inventory.Count, Is.EqualTo(1), "Identifying a type should not break the stack");
        Assert.That(inventory.Items[0].Require<Consumable>().StackSize, Is.EqualTo(2));
    }

    // ─── Floor carry-forward ─────────────────────────────────────────────────────

    [Test]
    public void FloorCarryForward_RegistryAndPoolPreserved()
    {
        // Simulate a floor transition: registry and pool from floor 1 must survive to floor 2.
        // The test creates a registry with an identification decision, then checks that a new
        // GameState preserves those decisions.
        var registry = new IdentificationRegistry();
        registry.Identify("healing_potion");
        registry.MarkUnidentified("scroll_of_lightning");

        var pool = BuildPool(1337);

        var rng    = new SeededRandom(2000);
        var map    = GameMap.CreateArena(10, 10);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 30, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 1, damageMax: 3));
        map.RegisterEntity(player);

        // Simulating passing registry/pool through to a new floor state.
        var newFloorState = new GameState(player, [], map, rng)
        {
            IdentificationRegistry = registry,
            AppearancePool         = pool,
        };

        Assert.That(newFloorState.IdentificationRegistry!.IsIdentified("healing_potion"), Is.True,
            "Registry should preserve identified types across floors");
        Assert.That(newFloorState.IdentificationRegistry!.IsIdentified("scroll_of_lightning"), Is.False,
            "Registry should preserve unidentified decisions across floors");
        Assert.That(newFloorState.IdentificationRegistry!.HasDecision("scroll_of_lightning"), Is.True,
            "Registry should have the unidentified decision from previous floor");

        // Pool assignments must be stable (same descriptor for same seed).
        Assert.That(newFloorState.AppearancePool!.GetDescriptor("healing_potion"),
            Is.EqualTo(pool.GetDescriptor("healing_potion")),
            "Pool should be the same object — descriptors are preserved across floors");
    }

    [Test]
    public void FloorCarryForward_NewGame_RegistryReset()
    {
        // A new game creates a fresh registry with no identifications.
        var freshRegistry = new IdentificationRegistry();

        Assert.That(freshRegistry.IsIdentified("healing_potion"), Is.False,
            "Fresh registry should have no identifications");
        Assert.That(freshRegistry.HasDecision("healing_potion"), Is.False,
            "Fresh registry should have no decisions");
        Assert.That(freshRegistry.IdentifiedCount, Is.EqualTo(0));
    }

    // ─── ItemDisplay.GetSpriteKey ────────────────────────────────────────────────
    // These tests cover the "green square" regression: InventoryPanel must call
    // GetSpriteKey (not tag.TypeId directly) so unidentified potions show the
    // mystery bottle sprite rather than the unresolved type key.

    private static Entity MakePotionEntity(string typeId = "healing_potion")
    {
        var e = new Entity(1, "Healing Potion");
        e.Add(new ItemTag(typeId));
        e.Add(new Consumable(healAmount: 40) { IsPotion = true });
        e.Add(new IdentifiableItem { IdentifiedName = "Healing Potion", UnidentifiedName = "Fizzy Potion" });
        return e;
    }

    private static Entity MakeScrollEntity(string typeId = "scroll_of_fire")
    {
        var e = new Entity(2, "Scroll of Fire");
        e.Add(new ItemTag(typeId));
        e.Add(new Consumable(healAmount: 0));
        e.Add(new SpellEffect { SpellId = "fire", Targeting = TargetingMode.AutoClosest });
        e.Add(new IdentifiableItem { IdentifiedName = "Scroll of Fire" });
        return e;
    }

    private static Entity MakeWandEntity(string typeId = "wand_of_lightning")
    {
        var e = new Entity(3, "Wand of Lightning");
        e.Add(new ItemTag(typeId));
        e.Add(new WandComponent { Charges = 3, MaxCharges = 6, RechargeScrollId = "lightning" });
        e.Add(new SpellEffect { SpellId = "lightning", Targeting = TargetingMode.AutoClosest });
        e.Add(new IdentifiableItem { IdentifiedName = "Wand of Lightning" });
        return e;
    }

    private static Entity MakeWeaponEntity()
    {
        var e = new Entity(4, "Iron Sword");
        e.Add(new ItemTag("iron_sword"));
        e.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 3, DamageMax = 8 });
        // No IdentifiableItem — weapons are always identified
        return e;
    }

    [Test]
    public void GetSpriteKey_UnidentifiedPotion_ReturnsMysterySprite_NotTypeId()
    {
        // This is the exact bug that produced green squares: GetSpriteKey must return
        // the mystery bottle key (e.g. "36") for an unidentified potion, not "healing_potion".
        var items = new List<(string, ItemCategory)> { ("healing_potion", ItemCategory.Potion) };
        var pool  = new AppearancePool(items, seed: 42);
        var reg   = new IdentificationRegistry();
        reg.MarkUnidentified("healing_potion");

        var potion = MakePotionEntity();
        var key    = ItemDisplay.GetSpriteKey(potion, reg, pool);

        Assert.That(key, Is.Not.EqualTo("healing_potion"),
            "Unidentified potion must not return its TypeId as the sprite key");
        Assert.That(new[] { "unknown_potion_a", "unknown_potion_b", "unknown_potion_c" }, Does.Contain(key),
            "Unidentified potion sprite key must be one of the mystery bottle tileset keys");
    }

    [Test]
    public void GetSpriteKey_IdentifiedPotion_ReturnsTypeId()
    {
        var items = new List<(string, ItemCategory)> { ("healing_potion", ItemCategory.Potion) };
        var pool  = new AppearancePool(items, seed: 42);
        var reg   = new IdentificationRegistry();
        reg.Identify("healing_potion");

        var potion = MakePotionEntity();
        var key    = ItemDisplay.GetSpriteKey(potion, reg, pool);

        Assert.That(key, Is.EqualTo("healing_potion"),
            "Identified potion must return its TypeId so the tileset can look up the true sprite");
    }

    [Test]
    public void GetSpriteKey_NullRegistry_ReturnsTypeId()
    {
        // Null registry = scenario/harness mode = always show true sprite
        var potion = MakePotionEntity();
        var key    = ItemDisplay.GetSpriteKey(potion, registry: null, pool: null);

        Assert.That(key, Is.EqualTo("healing_potion"));
    }

    [Test]
    public void GetSpriteKey_UnidentifiedScroll_ReturnsScrollMysterySprite()
    {
        var items = new List<(string, ItemCategory)> { ("scroll_of_fire", ItemCategory.Scroll) };
        var pool  = new AppearancePool(items, seed: 42);
        var reg   = new IdentificationRegistry();
        reg.MarkUnidentified("scroll_of_fire");

        var scroll = MakeScrollEntity();
        var key    = ItemDisplay.GetSpriteKey(scroll, reg, pool);

        Assert.That(key, Is.EqualTo(AppearancePool.ScrollMysterySprite),
            "Unidentified scroll must return the shared scroll mystery sprite key");
    }

    [Test]
    public void GetSpriteKey_UnidentifiedWand_ReturnsWandMysterySprite()
    {
        var items = new List<(string, ItemCategory)> { ("wand_of_lightning", ItemCategory.Wand) };
        var pool  = new AppearancePool(items, seed: 42);
        var reg   = new IdentificationRegistry();
        reg.MarkUnidentified("wand_of_lightning");

        var wand = MakeWandEntity();
        var key  = ItemDisplay.GetSpriteKey(wand, reg, pool);

        Assert.That(key, Is.EqualTo(AppearancePool.WandMysterySprite),
            "Unidentified wand must return the shared wand mystery sprite key");
    }

    [Test]
    public void GetSpriteKey_Weapon_AlwaysReturnsTypeId()
    {
        // Weapons have no IdentifiableItem — always show true sprite regardless of registry.
        var reg    = new IdentificationRegistry();
        var weapon = MakeWeaponEntity();
        var key    = ItemDisplay.GetSpriteKey(weapon, reg, pool: null);

        Assert.That(key, Is.EqualTo("iron_sword"),
            "Weapons have no IdentifiableItem and must always return their TypeId");
    }

    [Test]
    public void GetSpriteKey_PotionBecomesIdentified_SpriteFlipsToTypeId()
    {
        // When a type goes from unidentified → identified mid-run, the sprite must update.
        var items = new List<(string, ItemCategory)> { ("healing_potion", ItemCategory.Potion) };
        var pool  = new AppearancePool(items, seed: 42);
        var reg   = new IdentificationRegistry();
        reg.MarkUnidentified("healing_potion");

        var potion     = MakePotionEntity();
        var keyBefore  = ItemDisplay.GetSpriteKey(potion, reg, pool);
        reg.Identify("healing_potion");
        var keyAfter   = ItemDisplay.GetSpriteKey(potion, reg, pool);

        Assert.That(keyBefore, Is.Not.EqualTo("healing_potion"), "Before: mystery sprite");
        Assert.That(keyAfter,  Is.EqualTo("healing_potion"),     "After: true sprite");
    }
}
