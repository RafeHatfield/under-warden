using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Charge-tracking component for wand items.
/// Wands pair with SpellEffect (what spell to cast) and WandComponent (how many uses remain).
///
/// Infinite wands (Wand of Portals) always return true from TryConsume and never run out.
/// Normal wands are destroyed from inventory when Charges hits 0.
///
/// Recharging: picking up a matching scroll automatically recharges the wand by 1 charge
/// instead of adding the scroll to inventory. The recharge_scroll_id identifies which
/// scroll ID triggers recharge.
/// </summary>
public sealed class WandComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>Current charges remaining. Ignored when Infinite=true.</summary>
    public int Charges { get; set; }

    /// <summary>Maximum charges this wand can hold (cap for recharging).</summary>
    public int MaxCharges { get; set; } = 10;

    /// <summary>
    /// Scroll ID that recharges this wand on pickup.
    /// When null, the wand cannot be recharged via scroll.
    /// </summary>
    public string? RechargeScrollId { get; set; }

    /// <summary>When true the wand has unlimited uses (Wand of Portals).</summary>
    public bool Infinite { get; set; }

    /// <summary>True if this wand can be fired right now.</summary>
    public bool HasCharges => Infinite || Charges > 0;

    /// <summary>
    /// Attempt to consume one charge. Returns true on success.
    /// Infinite wands always return true. Normal wands decrement charges.
    /// Returns false if out of charges.
    /// </summary>
    public bool TryConsume()
    {
        if (Infinite) return true;
        if (Charges <= 0) return false;
        Charges--;
        return true;
    }
}
