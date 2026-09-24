using System.Text;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;

namespace UnderWarden.Logic.Balance.LlmPlayer;

public sealed record AvailableAction(string Label, PlayerAction Action);

public sealed record StateDescription(string Text, IReadOnlyList<AvailableAction> Actions);

/// <summary>Brain-held state the describer can't derive from GameState.</summary>
public sealed record DescriberContext(
    IReadOnlyList<string> RecentEventLines,
    string? PendingPromptBlock,
    string? TriggerContext = null);  // what woke the LLM from auto-explore; rendered prominently

public static class GameStateDescriber
{
    private const int NearbyTileRadius = 10; // TODO(deferred): replace with true FOV from GameState.Map

    // TODO(deferred): add Hollowmark CONTEXT block when menu gains possession entries
    // TODO(deferred): add Possess/ExitPossession/ThrowItem/CastSpell/RangedAttack menu entries
    private const int MuralMaxChars = 2000;

    public static StateDescription Describe(GameState state, LlmPersona persona, DescriberContext ctx)
    {
        var player = state.Player;
        var fighter = state.PlayerFighter;
        int hpPct = fighter.MaxHp > 0 ? (int)Math.Round(100.0 * fighter.Hp / fighter.MaxHp) : 0;

        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"=== TURN {state.TurnCount} | FLOOR {state.CurrentDepth} | {fighter.Hp}/{fighter.MaxHp} HP ({hpPct}%) ===");

        // Combat loadout — helps the LLM notice when it's fighting unarmed
        var equipment = player.Get<Equipment>();
        string weaponStr = equipment?.MainHand?.Name ?? "bare hands";
        string armourStr = equipment?.Chest?.Name ?? "none";
        sb.AppendLine($"Weapon: {weaponStr} | Armour: {armourStr}");
        sb.AppendLine();

        // Decision point trigger (why the LLM was woken from auto-explore)
        if (ctx.TriggerContext != null)
        {
            sb.AppendLine($"DECISION POINT: {ctx.TriggerContext}");
            sb.AppendLine();
        }

        // SITUATION
        var nearbyMonsters = state.AliveMonsters
            .Where(m => Chebyshev(player, m) <= NearbyTileRadius)
            .ToList();

        int stairDist = state.StairDown != null
            ? Chebyshev(player.X, player.Y, state.StairDown.X, state.StairDown.Y)
            : -1;

        sb.AppendLine("SITUATION");
        if (nearbyMonsters.Count == 0)
        {
            if (state.IsFloorClear)
                sb.Append("The floor is clear of enemies.");
            else
                sb.Append("No enemies are visible nearby.");
        }
        else
        {
            sb.Append($"{nearbyMonsters.Count} enemy{(nearbyMonsters.Count == 1 ? "" : " enemies")} visible.");
        }

        if (stairDist >= 0)
            sb.Append(state.PlayerOnStairDown
                ? " You are standing on the staircase."
                : $" The staircase is {stairDist} tile{(stairDist == 1 ? "" : "s")} away.");

        sb.AppendLine();
        sb.AppendLine();

        // THREATS
        var threats = nearbyMonsters
            .Select(m => (Monster: m, Dist: Chebyshev(player, m)))
            .OrderBy(t => t.Dist)
            .Take(5)
            .ToList();

        if (threats.Count > 0)
        {
            sb.AppendLine("THREATS  (nearest 5, sorted by distance)");
            foreach (var (m, dist) in threats)
            {
                int dx = m.X - player.X;
                int dy = m.Y - player.Y;
                string dir = Direction(dx, dy);
                string threat = dist <= 1 ? "high" : dist <= 4 ? "med" : "low";
                sb.AppendLine($"- {m.Name}: {dist} tile{(dist == 1 ? "" : "s")} {dir}, threat {threat}");
            }
            sb.AppendLine();
        }

        // ITEMS ON FLOOR
        var nearbyItems = state.FloorItems
            .Select(i => (Item: i, Dist: Chebyshev(player, i)))
            .Where(t => t.Dist <= NearbyTileRadius)
            .OrderBy(t => t.Dist)
            .Take(3)
            .ToList();

        if (nearbyItems.Count > 0)
        {
            sb.AppendLine("ITEMS ON FLOOR  (nearest 3)");
            foreach (var (item, dist) in nearbyItems)
            {
                int dx = item.X - player.X;
                int dy = item.Y - player.Y;
                string dir = Direction(dx, dy);
                sb.AppendLine($"- {item.Name} at {dist} tile{(dist == 1 ? "" : "s")} {dir}");
            }
            sb.AppendLine();
        }

        // FEATURES
        var nearbyFeatures = state.Features
            .Select(f => (Feature: f, Dist: Chebyshev(player, f)))
            .Where(t => t.Dist <= NearbyTileRadius)
            .OrderBy(t => t.Dist)
            .ToList();

        if (nearbyFeatures.Count > 0)
        {
            sb.AppendLine("FEATURES");
            foreach (var (feature, dist) in nearbyFeatures)
            {
                int dx = feature.X - player.X;
                int dy = feature.Y - player.Y;
                string dir = Direction(dx, dy);

                if (persona == LlmPersona.Reader)
                {
                    var mural = feature.Get<MuralComponent>();
                    if (mural != null)
                    {
                        string text = mural.Text.Length > MuralMaxChars
                            ? mural.Text[..MuralMaxChars] + "[...]"
                            : mural.Text;
                        sb.AppendLine($"MURAL ({dir} wall): \"{text}\"");
                        continue;
                    }

                    var sign = feature.Get<SignpostComponent>();
                    if (sign != null)
                    {
                        string text = sign.Message.Length > MuralMaxChars
                            ? sign.Message[..MuralMaxChars] + "[...]"
                            : sign.Message;
                        sb.AppendLine($"SIGNPOST: \"{text}\"");
                        continue;
                    }

                    var chest = feature.Get<ChestComponent>();
                    if (chest != null)
                    {
                        string state2 = chest.IsLooted ? "looted" : chest.IsOpen ? "open" : "unopened";
                        sb.AppendLine($"- Chest ({state2}) at {dist} tile{(dist == 1 ? "" : "s")} {dir}");
                        continue;
                    }

                    sb.AppendLine($"- {feature.Name} at {dist} tile{(dist == 1 ? "" : "s")} {dir}");
                }
                else
                {
                    // SystemExplorer: compressed form
                    var mural = feature.Get<MuralComponent>();
                    if (mural != null)
                    {
                        sb.AppendLine($"- Mural: Ancient inscription (readable) at {dist} tile{(dist == 1 ? "" : "s")} {dir}");
                        continue;
                    }

                    var sign = feature.Get<SignpostComponent>();
                    if (sign != null)
                    {
                        sb.AppendLine($"- Signpost (readable) at {dist} tile{(dist == 1 ? "" : "s")} {dir}");
                        continue;
                    }

                    var chest = feature.Get<ChestComponent>();
                    if (chest != null)
                    {
                        string state2 = chest.IsLooted ? "looted" : chest.IsOpen ? "open" : "unopened";
                        sb.AppendLine($"- Chest ({state2}) at {dist} tile{(dist == 1 ? "" : "s")} {dir}");
                        continue;
                    }

                    sb.AppendLine($"- {feature.Name} at {dist} tile{(dist == 1 ? "" : "s")} {dir}");
                }
            }
            sb.AppendLine();
        }

        // RECENT EVENTS
        if (ctx.RecentEventLines.Count > 0)
        {
            sb.AppendLine("RECENT EVENTS (last 3 turns)");
            // RecentEventLines are oldest-first; turn numbering is approximate
            int baseTurn = state.TurnCount - ctx.RecentEventLines.Count;
            for (int i = 0; i < ctx.RecentEventLines.Count; i++)
                sb.AppendLine($"- T{baseTurn + i + 1}: {ctx.RecentEventLines[i]}");
            sb.AppendLine();
        }

        // Pending block (FLOOR COMPLETE / REFLECTION)
        if (ctx.PendingPromptBlock != null)
        {
            sb.AppendLine(ctx.PendingPromptBlock);
            sb.AppendLine();
        }

        // Build the action menu
        var actions = BuildActions(state);

        sb.AppendLine("AVAILABLE ACTIONS");
        for (int i = 0; i < actions.Count; i++)
            sb.AppendLine($"{i + 1}. {actions[i].Label}");
        sb.AppendLine();

        // Persona instruction lives in the system prompt (cached block) — not repeated here.

        return new StateDescription(sb.ToString(), actions);
    }

    private static IReadOnlyList<AvailableAction> BuildActions(GameState state)
    {
        var actions = new List<AvailableAction>();
        var player = state.Player;
        var fighter = state.PlayerFighter;

        // 1. Attack adjacent living monsters (Chebyshev ≤ 1)
        var adjacent = state.AliveMonsters
            .Where(m => Chebyshev(player, m) <= 1)
            .ToList();

        foreach (var m in adjacent)
        {
            var mf = m.Get<Fighter>();
            string hpState = "unknown";
            if (mf != null)
            {
                double frac = (double)mf.Hp / mf.MaxHp;
                hpState = frac >= 0.66 ? "healthy" : frac >= 0.33 ? "wounded" : "critical";
            }
            actions.Add(new AvailableAction($"Attack {m.Name} ({hpState})", PlayerAction.Attack(m)));
        }

        // 2. Semantic move entries — skip if A* is null (unreachable)

        // 2a. Move toward nearest monster (when alive monsters exist and none adjacent)
        var nonAdjacent = state.AliveMonsters
            .Where(m => Chebyshev(player, m) > 1)
            .OrderBy(m => Chebyshev(player, m))
            .FirstOrDefault();

        if (nonAdjacent != null)
        {
            var step = OneStep(state, player, nonAdjacent.X, nonAdjacent.Y);
            if (step.HasValue)
                actions.Add(new AvailableAction($"Move toward {nonAdjacent.Name}", PlayerAction.MoveTo(step.Value.X, step.Value.Y)));
        }

        // 2b. Move toward staircase
        if (state.StairDown != null && !state.PlayerOnStairDown)
        {
            var step = OneStep(state, player, state.StairDown.X, state.StairDown.Y);
            if (step.HasValue)
                actions.Add(new AvailableAction("Move toward the staircase", PlayerAction.MoveTo(step.Value.X, step.Value.Y)));
        }

        // 2c. Move toward nearest floor item (within 10 tiles)
        var nearestItem = state.FloorItems
            .Where(i => Chebyshev(player, i) <= NearbyTileRadius)
            .OrderBy(i => Chebyshev(player, i))
            .FirstOrDefault();

        if (nearestItem != null)
        {
            var step = OneStep(state, player, nearestItem.X, nearestItem.Y);
            if (step.HasValue)
                actions.Add(new AvailableAction($"Move toward {nearestItem.Name}", PlayerAction.MoveTo(step.Value.X, step.Value.Y)));
        }

        // 2d. Move toward / examine nearest unexamined feature (within 10 tiles)
        var nearestFeature = state.Features
            .Where(f => Chebyshev(player, f) <= NearbyTileRadius && !IsExamined(f))
            .OrderBy(f => Chebyshev(player, f))
            .FirstOrDefault();

        if (nearestFeature != null)
        {
            string featureLabel = FeatureLabel(nearestFeature);
            bool isAdjacent = Chebyshev(player, nearestFeature) <= 1;
            if (isAdjacent)
            {
                // Bump-interaction: move onto the feature tile (which triggers examine)
                actions.Add(new AvailableAction($"Examine {featureLabel}", PlayerAction.MoveTo(nearestFeature.X, nearestFeature.Y)));
            }
            else
            {
                var step = OneStep(state, player, nearestFeature.X, nearestFeature.Y);
                if (step.HasValue)
                    actions.Add(new AvailableAction($"Move toward the {featureLabel}", PlayerAction.MoveTo(step.Value.X, step.Value.Y)));
            }
        }

        // 3. Use healing potion
        var inv = state.PlayerInventory;
        if (inv != null)
        {
            int potionCount = CountHealingPotions(inv);
            if (potionCount > 0)
            {
                int hpPct = fighter.MaxHp > 0 ? (int)Math.Round(100.0 * fighter.Hp / fighter.MaxHp) : 0;
                actions.Add(new AvailableAction($"Drink a healing potion ({potionCount} left, you are at {hpPct}% HP)", PlayerAction.UseItem()));
            }
        }

        // 3b. Equip items from inventory — only offer if not already equipped.
        // Helps the LLM notice weapons/armour it picked up but hasn't equipped.
        if (inv != null)
        {
            var equip = player.Get<Equipment>();
            foreach (var item in inv.Items)
            {
                var eq = item.Get<Equippable>();
                if (eq == null) continue;

                // Determine whether this item is already in the matching slot
                bool alreadyEquipped = eq.Slot switch
                {
                    EquipmentSlot.MainHand => equip?.MainHand?.Id == item.Id,
                    EquipmentSlot.OffHand  => equip?.OffHand?.Id  == item.Id,
                    EquipmentSlot.Head     => equip?.Head?.Id      == item.Id,
                    EquipmentSlot.Chest    => equip?.Chest?.Id     == item.Id,
                    EquipmentSlot.Feet     => equip?.Feet?.Id      == item.Id,
                    EquipmentSlot.LeftRing => equip?.LeftRing?.Id  == item.Id,
                    EquipmentSlot.RightRing => equip?.RightRing?.Id == item.Id,
                    EquipmentSlot.Neck     => equip?.Neck?.Id      == item.Id,
                    EquipmentSlot.Quiver   => equip?.Quiver?.Id    == item.Id,
                    _ => false,
                };
                if (alreadyEquipped) continue;

                string slotLabel = eq.Slot switch
                {
                    EquipmentSlot.MainHand => "weapon",
                    EquipmentSlot.OffHand  => "off-hand",
                    EquipmentSlot.Head     => "helmet",
                    EquipmentSlot.Chest    => "armour",
                    EquipmentSlot.Feet     => "boots",
                    EquipmentSlot.LeftRing or EquipmentSlot.RightRing => "ring",
                    EquipmentSlot.Neck     => "amulet",
                    EquipmentSlot.Quiver   => "quiver",
                    _ => "gear",
                };
                actions.Add(new AvailableAction($"Equip {item.Name} ({slotLabel})", PlayerAction.Equip(item)));
            }
        }

        // 3c. Drink unidentified consumables (non-healing potions the LLM hasn't tried yet).
        // Limited to 3 entries to keep the menu concise.
        if (inv != null)
        {
            int unknownCount = 0;
            foreach (var item in inv.Items)
            {
                if (unknownCount >= 3) break;

                var c = item.Get<Consumable>();
                if (c == null) continue;
                if (c.IsHealing) continue;  // already handled by healing potion entry

                // Skip if definitively identified (we know what it is — don't label it "unknown")
                var tag = item.Get<ItemTag>();
                bool identified = tag != null
                    ? (state.IdentificationRegistry?.IsIdentified(tag.TypeId) ?? false)
                    : false;
                if (identified) continue;

                actions.Add(new AvailableAction(
                    $"Drink {item.Name} (unidentified — unknown effect)",
                    PlayerAction.UseItem(item)));
                unknownCount++;
            }
        }

        // 4. Descend staircase (only when standing on it)
        if (state.PlayerOnStairDown)
        {
            string label = state.IsFloorClear
                ? "Descend the staircase"
                : "Descend the staircase (monsters remain on floor)";
            actions.Add(new AvailableAction(label, PlayerAction.Descend));
        }

        // 5. Wait — always last
        actions.Add(new AvailableAction("Wait (do nothing)", PlayerAction.Wait));

        return actions;
    }

    /// <summary>Generates a one-line summary from a list of TurnEvents for use in RECENT EVENTS.</summary>
    public static string SummarizeEvents(IReadOnlyList<TurnEvent> events)
    {
        if (events.Count == 0) return "Nothing happened";

        var parts = new List<string>();
        foreach (var e in events)
        {
            string? summary = SummarizeOne(e);
            if (summary != null)
                parts.Add(summary);
        }

        return parts.Count > 0 ? string.Join("; ", parts) : "Nothing happened";
    }

    private static string? SummarizeOne(TurnEvent e) => e switch
    {
        AttackEvent a when a.Hit => a.ActorId == 0
            ? $"You hit for {a.Damage}{(a.TargetKilled ? " (killed)" : "")}"
            : $"Enemy hit you for {a.Damage}{(a.TargetKilled ? " (you died)" : "")}",
        AttackEvent a => a.ActorId == 0 ? "You missed" : "Enemy missed you",
        DeathEvent d when d.ActorId != 0 => "An enemy died",
        HealEvent h => $"You healed {h.AmountHealed} HP",
        DescendEvent d => $"You descended to floor {d.NewDepth}",
        MoveEvent m when m.ActorId == 0 => $"You moved {Direction(m.ToX - m.FromX, m.ToY - m.FromY)}",
        WaitEvent => "You waited",
        MuralExaminedEvent => "You examined a mural",
        SignpostReadEvent => "You read a signpost",
        DotDamageEvent d when d.EntityId == 0 => $"You took {d.Damage} {d.EffectName} damage",
        ChestOpenedEvent => "You opened a chest",
        PickUpEvent p => $"You picked up {p.ItemName}",
        DoorOpenedEvent => "You opened a door",
        _ => null,
    };

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static (int X, int Y)? OneStep(GameState state, Entity player, int toX, int toY)
    {
        var path = Pathfinder.AStar(state.Map, player.X, player.Y, toX, toY, player, canPassDoors: true);
        if (path == null || path.Count == 0) return null;
        return path[0];
    }

    private static int Chebyshev(Entity a, Entity b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static int Chebyshev(int ax, int ay, int bx, int by) =>
        Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

    private static string Direction(int dx, int dy) => (Math.Sign(dx), Math.Sign(dy)) switch
    {
        (0, -1) => "north",
        (1, -1) => "northeast",
        (1, 0) => "east",
        (1, 1) => "southeast",
        (0, 1) => "south",
        (-1, 1) => "southwest",
        (-1, 0) => "west",
        (-1, -1) => "northwest",
        _ => "nearby",
    };

    private static bool IsExamined(Entity feature)
    {
        var mural = feature.Get<MuralComponent>();
        if (mural != null) return mural.HasBeenExamined;
        var sign = feature.Get<SignpostComponent>();
        if (sign != null) return sign.HasBeenRead;
        var chest = feature.Get<ChestComponent>();
        if (chest != null) return chest.IsLooted;
        return false;
    }

    private static string FeatureLabel(Entity feature)
    {
        if (feature.Get<MuralComponent>() != null) return "mural";
        if (feature.Get<SignpostComponent>() != null) return "signpost";
        if (feature.Get<ChestComponent>() != null) return "chest";
        return feature.Name.ToLowerInvariant();
    }

    private static int CountHealingPotions(Inventory inv)
    {
        int count = 0;
        foreach (var item in inv.Items)
        {
            var c = item.Get<Consumable>();
            if (c?.IsHealing == true)
                count += c.StackSize;
        }
        return count;
    }

}
