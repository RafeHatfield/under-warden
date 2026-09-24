namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marks a monster as applying a status effect on each successful melee hit.
/// TurnController reads this after a hit lands and calls the appropriate
/// StatusEffectProcessor.ApplyEffect&lt;T&gt; on the target.
///
/// Supported effect types: "poison" (cave_spider), "slowed" (web_spider), "burning" (fire_beetle).
/// Duration is the number of turns the effect lasts (uses no-stack/refresh rule on re-application).
/// </summary>
public sealed class OnHitEffectComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>Status effect type string. Must match a case in TurnController.ResolveOnHitEffect.</summary>
    public string EffectType { get; init; } = "";

    /// <summary>Duration in turns passed to ApplyEffect when the effect lands.</summary>
    public int Duration { get; init; }

    public OnHitEffectComponent(string effectType, int duration)
    {
        EffectType = effectType;
        Duration = duration;
    }
}
