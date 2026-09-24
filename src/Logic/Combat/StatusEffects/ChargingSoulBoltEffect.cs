using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Marker effect on lich: "charging Soul Bolt". Duration = 1 turn.
/// On the lich's NEXT turn, if still present, Soul Bolt resolves.
/// Removed after resolution or fizzle.
/// </summary>
public sealed class ChargingSoulBoltEffect : IComponent
{
    public Entity? Owner { get; set; }
    public int RemainingTurns { get; set; } = 1;
}
