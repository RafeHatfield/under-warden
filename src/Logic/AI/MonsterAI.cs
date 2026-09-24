using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.AI;

/// <summary>
/// Dispatches monster AI decisions based on the monster's AiComponent.AiType.
/// Returns a MonsterAction that TurnController resolves.
/// Adding a new AI strategy: add the AiType string here and implement a static Decide method.
/// </summary>
public static class MonsterAI
{
    public static MonsterAction Decide(Entity monster, GameState state)
    {
        // Default to "basic" when the component is absent — harness-created monsters
        // may not have AiComponent attached yet, so the fallback keeps them functional.
        var aiType = monster.Get<AiComponent>()?.AiType ?? "basic";

        return aiType switch
        {
            "basic"          => BasicMonsterAI.Decide(monster, state),
            "skirmisher"     => SkirmisherAI.Decide(monster, state),
            "orc_shaman"     => OrcShamanAI.Decide(monster, state),
            "orc_chieftain"  => OrcChieftainAI.Decide(monster, state),
            "skeleton"          => SkeletonAI.Decide(monster, state),
            "necromancer"       => NecromancerAI.Decide(monster, state),
            "plague_necromancer"=> NecromancerAI.Decide(monster, state),
            "lich"             => LichAI.Decide(monster, state),
            _ => BasicMonsterAI.Decide(monster, state), // unknown type → safe fallback
        };
    }
}
