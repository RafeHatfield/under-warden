using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Resolves Soul Bolt damage. Called by LichAI/TurnController when the bolt resolves.
/// Damage = ceil(damagePct * target.MaxHp). PoC: soul_bolt_damage_pct = 0.18.
/// </summary>
public static class SoulBoltResolver
{
    public static int Resolve(Entity lich, Entity target, double damagePct, List<TurnEvent> events)
    {
        var targetFighter = target.Get<Fighter>();
        if (targetFighter == null) return 0;

        int baseDamage = (int)Math.Ceiling(damagePct * targetFighter.MaxHp);

        // TODO: Soul Ward check (deferred)
        // if (target.Has<SoulWardEffect>()) { upfront = ceil(baseDamage * 0.30); ... }

        targetFighter.TakeDamage(baseDamage);

        events.Add(new SoulBoltEvent
        {
            ActorId = lich.Id,
            TargetId = target.Id,
            Damage = baseDamage,
        });

        if (!targetFighter.IsAlive)
        {
            events.Add(new DeathEvent { ActorId = target.Id, KillerId = lich.Id });
        }

        return baseDamage;
    }
}
