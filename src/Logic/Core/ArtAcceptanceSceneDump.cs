using System.Text;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Core;

/// <summary>
/// Deterministic plain-text dump of a GameState's renderer-consumed floor model:
/// tile grid, props, features, floor items, monsters, player.
///
/// Used by ArtAcceptanceSceneBuilderTests (determinism regression test) and by
/// tools/Harness's --art-scene-dump mode (produces the committed evidence artifacts —
/// two cold-start dumps that must diff empty per docs/art_test_scene_spec_v2.md §4).
/// </summary>
public static class ArtAcceptanceSceneDump
{
    public static string Dump(GameState state)
    {
        var sb = new StringBuilder();
        var map = state.Map;

        sb.AppendLine($"MAP width={map.Width} height={map.Height}");
        sb.AppendLine("GRID (row=y, col=x; #=Wall .=Floor D=Door o=Door-open ?=other):");
        for (int y = 0; y < map.Height; y++)
        {
            var row = new StringBuilder();
            for (int x = 0; x < map.Width; x++)
            {
                row.Append(map.GetTileKind(x, y) switch
                {
                    TileKind.Wall => '#',
                    TileKind.Floor => '.',
                    TileKind.Corridor => '.',
                    TileKind.Door => 'D',
                    TileKind.DoorOpen => 'o',
                    _ => '?',
                });
            }
            sb.AppendLine(row.ToString());
        }

        sb.AppendLine($"PLAYER x={state.Player.X} y={state.Player.Y} hp={state.PlayerFighter.Hp}");

        sb.AppendLine("MONSTERS:");
        foreach (var m in state.Monsters.OrderBy(e => e.X).ThenBy(e => e.Y))
        {
            var species = m.Get<SpeciesTag>()?.TypeId ?? m.Name;
            var hp = m.Get<UnderWarden.Logic.Combat.Fighter>()?.Hp ?? -1;
            sb.AppendLine($"  {species} x={m.X} y={m.Y} hp={hp}");
        }

        sb.AppendLine("PROPS:");
        foreach (var p in state.Props.OrderBy(p => p.X).ThenBy(p => p.Y))
            sb.AppendLine($"  {p.PropId} tile={p.TileId} x={p.X} y={p.Y} blocks={p.BlocksMovement}");

        sb.AppendLine("FEATURES:");
        foreach (var f in state.Features.OrderBy(e => e.X).ThenBy(e => e.Y))
        {
            var chest = f.Get<ChestComponent>();
            var sign = f.Get<SignpostComponent>();
            var mural = f.Get<MuralComponent>();
            string desc = chest != null ? $"Chest isOpen={chest.IsOpen}"
                        : sign != null ? "Signpost"
                        : mural != null ? $"Mural tile={mural.TileId}"
                        : f.Name;
            sb.AppendLine($"  {desc} x={f.X} y={f.Y}");
        }

        sb.AppendLine("FLOORITEMS:");
        foreach (var i in state.FloorItems.OrderBy(e => e.X).ThenBy(e => e.Y))
        {
            var key = i.Get<KeyItemComponent>();
            var tag = i.Get<ItemTag>();
            string desc = key != null ? $"Key lockColor={key.LockColorId}"
                        : tag != null ? $"Item({tag.TypeId})"
                        : i.Name;
            sb.AppendLine($"  {desc} x={i.X} y={i.Y}");
        }

        return sb.ToString();
    }
}
