using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Tracks what an entity has equipped. Aggregates bonuses from all slots.
/// </summary>
public sealed class Equipment : IComponent
{
    public Entity? Owner { get; set; }

    public Entity? MainHand { get; set; }
    public Entity? OffHand { get; set; }
    public Entity? Head { get; set; }
    public Entity? Chest { get; set; }
    public Entity? Feet { get; set; }
    public Entity? LeftRing { get; set; }
    public Entity? RightRing { get; set; }
    public Entity? Neck { get; set; }
    /// <summary>Holds special ammo stacks (fire_arrow, net_arrow). See Equippable.IsSpecialAmmo.</summary>
    public Entity? Quiver { get; set; }

    /// <summary>Get the equipped item in a slot.</summary>
    public Entity? GetSlot(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.MainHand  => MainHand,
        EquipmentSlot.OffHand   => OffHand,
        EquipmentSlot.Head      => Head,
        EquipmentSlot.Chest     => Chest,
        EquipmentSlot.Feet      => Feet,
        EquipmentSlot.LeftRing  => LeftRing,
        EquipmentSlot.RightRing => RightRing,
        EquipmentSlot.Neck      => Neck,
        EquipmentSlot.Quiver    => Quiver,
        _ => null,
    };

    /// <summary>Set an item in a slot. Returns the previously equipped item (or null).</summary>
    public Entity? SetSlot(EquipmentSlot slot, Entity? item)
    {
        Entity? previous = GetSlot(slot);
        switch (slot)
        {
            case EquipmentSlot.MainHand:  MainHand  = item; break;
            case EquipmentSlot.OffHand:   OffHand   = item; break;
            case EquipmentSlot.Head:      Head      = item; break;
            case EquipmentSlot.Chest:     Chest     = item; break;
            case EquipmentSlot.Feet:      Feet      = item; break;
            case EquipmentSlot.LeftRing:  LeftRing  = item; break;
            case EquipmentSlot.RightRing: RightRing = item; break;
            case EquipmentSlot.Neck:      Neck      = item; break;
            case EquipmentSlot.Quiver:    Quiver    = item; break;
        }
        return previous;
    }

    /// <summary>Total AC bonus from all equipped armor.</summary>
    public int TotalArmorClassBonus => SumBonus(e => e.ArmorClassBonus);

    /// <summary>Total to-hit bonus from equipped weapon.</summary>
    public int TotalToHitBonus => SumBonus(e => e.ToHitBonus);

    /// <summary>Iterate all equipped items with their Equippable component.</summary>
    private int SumBonus(Func<Equippable, int> selector)
    {
        int total = 0;
        foreach (var item in AllEquipped())
        {
            var eq = item.Get<Equippable>();
            if (eq != null)
                total += selector(eq);
        }
        return total;
    }

    private IEnumerable<Entity> AllEquipped()
    {
        if (MainHand  != null) yield return MainHand;
        if (OffHand   != null) yield return OffHand;
        if (Head      != null) yield return Head;
        if (Chest     != null) yield return Chest;
        if (Feet      != null) yield return Feet;
        if (LeftRing  != null) yield return LeftRing;
        if (RightRing != null) yield return RightRing;
        if (Neck      != null) yield return Neck;
        if (Quiver    != null) yield return Quiver;
    }
}
