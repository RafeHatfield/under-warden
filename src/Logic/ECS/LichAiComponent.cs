namespace UnderWarden.Logic.ECS;

/// <summary>
/// Lich-specific AI state. Extends necromancer behavior with Soul Bolt.
/// Attached by MonsterFactory when ai_type is "lich".
/// </summary>
public sealed class LichAiComponent : IComponent
{
    public Entity? Owner { get; set; }

    // Soul Bolt
    public int SoulBoltRange { get; set; } = 7;
    public double SoulBoltDamagePct { get; set; } = 0.18;
    public int SoulBoltCooldownTurns { get; set; } = 8;
    public int SoulBoltCooldownRemaining { get; set; }

    // Command the Dead
    public int CommandTheDeadRadius { get; set; } = 6;

    // Death Siphon
    public int DeathSiphonRadius { get; set; } = 6;

    // Summon override
    public string SummonMonsterId { get; set; } = "zombie";
}
