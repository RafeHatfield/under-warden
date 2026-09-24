namespace UnderWarden.Logic.ECS;

public sealed class MuralComponent : IComponent
{
    public Entity? Owner { get; set; }

    public string Text { get; init; } = "";

    /// <summary>Unique mural ID used by MuralTracker to prevent duplicates per floor.</summary>
    public string MuralId { get; init; } = "";

    /// <summary>Tile ID (5036-5038) for the visual variant chosen at placement.</summary>
    public int TileId { get; init; }

    public bool HasBeenExamined { get; set; }
}
