namespace UnderWarden.Logic.ECS;

/// <summary>
/// Tracks state for the Orc Chieftain's Rally Cry and Sonic Bellow abilities.
/// Attached by MonsterFactory when ai_type is "orc_chieftain".
/// Rally Cry fires once (on first contact with 2+ orc allies nearby) then cannot fire again.
/// Sonic Bellow fires once at &lt;50% HP then cannot fire again.
/// </summary>
public sealed class OrcChieftainComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>True once Rally Cry has fired. Prevents re-activation.</summary>
    public bool RallyCried { get; set; } = false;

    /// <summary>True once Sonic Bellow has fired. Prevents re-activation at low HP.</summary>
    public bool BellowedAtLowHp { get; set; } = false;

    /// <summary>Range (Chebyshev) to check for orc allies for Rally Cry. PoC value: 5.</summary>
    public int RallyRange { get; set; } = 5;

    /// <summary>Minimum ally count required for Rally Cry to activate. PoC value: 2.</summary>
    public int RallyMinAllies { get; set; } = 2;

    /// <summary>HP fraction below which Sonic Bellow fires. PoC value: 0.5 (50%).</summary>
    public double BellowHpThreshold { get; set; } = 0.5;

    /// <summary>Duration of the CrippledEffect applied to the player by Sonic Bellow. PoC value: 2 turns.</summary>
    public int BellowDebuffDuration { get; set; } = 2;

    /// <summary>Minimum preferred distance from player. PoC value: 3.</summary>
    public int PreferredDistanceMin { get; set; } = 3;

    /// <summary>Maximum preferred distance from player. PoC value: 6.</summary>
    public int PreferredDistanceMax { get; set; } = 6;

    /// <summary>Panic radius: if player is this close, always retreat. PoC value: 2.</summary>
    public int DangerRadius { get; set; } = 2;
}
