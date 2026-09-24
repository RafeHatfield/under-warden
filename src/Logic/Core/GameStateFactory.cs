using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Creates GameState instances from scenario definitions. Single source of truth
/// for entity setup — used by both the harness and the presentation layer.
/// </summary>
public static class GameStateFactory
{
    /// <summary>
    /// Create a GameState from a scenario definition with deterministic seed.
    /// </summary>
    public static GameState FromScenario(
        ScenarioDefinition scenario,
        int seed,
        MonsterFactory monsterFactory,
        ItemFactory? itemFactory = null,
        ConsumableFactory? consumableFactory = null,
        SpellItemFactory? spellItemFactory = null)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(scenario.MapWidth, scenario.MapHeight);
        map.RevealAll(); // Scenarios have no fog of war — all tiles visible from turn 0

        var player = CreatePlayer(scenario, itemFactory, consumableFactory, spellItemFactory, rng);
        player.X = Math.Clamp(scenario.PlayerStartX, 1, scenario.MapWidth - 2);
        player.Y = Math.Clamp(scenario.PlayerStartY, 1, scenario.MapHeight - 2);
        map.RegisterEntity(player);

        var monsters = CreateMonsters(scenario, map, rng, monsterFactory);

        return new GameState(player, monsters, map, rng, scenario.TurnLimit);
    }

    private static Entity CreatePlayer(
        ScenarioDefinition scenario,
        ItemFactory? itemFactory,
        ConsumableFactory? consumableFactory,
        SpellItemFactory? spellItemFactory = null,
        SeededRandom? rng = null)
    {
        var def = scenario.Player;
        var player = new Entity(0, "Player", 0, 0, blocksMovement: true);
        player.Add(new Fighter(
            hp: def.Hp,
            strength: def.Strength,
            dexterity: def.Dexterity,
            constitution: def.Constitution,
            accuracy: def.Accuracy,
            evasion: def.Evasion,
            damageMin: def.DamageMin,
            damageMax: def.DamageMax));

        Equipment? playerEquipment = null;
        if (itemFactory != null && (def.Weapon != null || def.Armor != null || def.Quiver != null))
        {
            playerEquipment = player.Add(new Equipment());
            if (def.Weapon != null)
            {
                var weapon = itemFactory.Create(def.Weapon);
                if (weapon != null) playerEquipment.MainHand = weapon;
            }
            if (def.Armor != null)
            {
                var armor = itemFactory.Create(def.Armor);
                if (armor != null) playerEquipment.Chest = armor;
            }
            // Quiver: special ammo pre-equipped at scenario start.
            // Direct slot assignment (same pattern as weapon/armor) — no inventory detour.
            if (def.Quiver != null)
            {
                var quiver = itemFactory.Create(def.Quiver);
                if (quiver?.Get<Equippable>()?.IsSpecialAmmo == true)
                    playerEquipment.Quiver = quiver;
            }
        }

        // Speed bonus for momentum system — PoC gives every scenario player a 0.25 base tracker
        // unconditionally (scenario_level_loader.py line 230). Equipment speed adds via EquipmentRatio.
        double weaponSpeed = playerEquipment?.MainHand?.Get<SpeedBonusTracker>()?.EquipmentRatio ?? 0;
        player.Add(new SpeedBonusTracker(baseRatio: def.SpeedBonus) { EquipmentRatio = weaponSpeed });

        if (scenario.Items.Count > 0)
        {
            var inventory = player.GetOrAdd<Inventory>();
            var itemRng = rng ?? new SeededRandom(0);
            foreach (var itemDef in scenario.Items)
            {
                for (int i = 0; i < itemDef.Count; i++)
                {
                    Entity? item = null;
                    // Resolution order: SpellItemFactory (scrolls/wands) → ConsumableFactory → ItemFactory.
                    // This allows scenario YAML to specify scrolls, wands, and potions in the same items list.
                    if (spellItemFactory != null)
                    {
                        // Try scroll first, then wand (wands need rng + depth)
                        item = spellItemFactory.CreateScroll(itemDef.Type)
                            ?? spellItemFactory.CreateWand(itemDef.Type, itemRng, depth: 1);
                    }
                    if (item == null && consumableFactory != null)
                        item = consumableFactory.Create(itemDef.Type);
                    if (item == null && itemFactory != null)
                        item = itemFactory.Create(itemDef.Type);

                    if (item != null) inventory.Add(item);
                }
            }
        }

        return player;
    }

    private static List<Entity> CreateMonsters(
        ScenarioDefinition scenario,
        GameMap map,
        SeededRandom rng,
        MonsterFactory monsterFactory)
    {
        var monsters = new List<Entity>();
        int idx = 0;
        foreach (var monsterDef in scenario.Monsters)
        {
            for (int i = 0; i < monsterDef.Count; i++)
            {
                var monster = monsterFactory.Create(monsterDef.Type, depth: scenario.Depth, rng: rng);
                if (monster != null)
                {
                    // Use explicit position from scenario YAML if provided.
                    // Null position → offset grid (existing behaviour, backward compatible).
                    if (monsterDef.Position != null && monsterDef.Position.Length == 2)
                    {
                        monster.X = monsterDef.Position[0];
                        monster.Y = monsterDef.Position[1];
                    }
                    else
                    {
                        monster.X = 8 + (idx % 3);
                        monster.Y = 4 + (idx / 3) * 2;
                    }
                    map.RegisterEntity(monster);
                    monsters.Add(monster);
                    idx++;
                }
            }
        }
        return monsters;
    }
}
