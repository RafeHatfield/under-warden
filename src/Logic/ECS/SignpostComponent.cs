namespace UnderWarden.Logic.ECS;

public sealed class SignpostComponent : IComponent
{
    public Entity? Owner { get; set; }

    public string Message { get; init; } = "";

    /// <summary>"lore" | "warning" | "humor" | "hint" | "directional"</summary>
    public string SignType { get; init; } = "lore";

    public bool HasBeenRead { get; set; }
}
