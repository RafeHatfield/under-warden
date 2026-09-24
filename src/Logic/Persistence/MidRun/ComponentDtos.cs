using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Persistence.MidRun;

// DTOs for the mid-run entity/component serializer (M1.4 §Component registry).
// One record per concrete IComponent, capturing STATE only:
//   - Owner is RECONSTRUCT-class (rebuilt on Entity.Add) — never here.
//   - Computed/derived properties (EffectName, IsPermanent, MaxHp, IsWeapon, ...) are omitted;
//     they recompute from the state that IS captured.
//   - Entity references become int Ids (nullable for optional, arrays for lists).
// Records give free structural equality, which the completeness gate leans on.

// ── shared value DTOs ────────────────────────────────────────────────────────
public sealed record PointDto(int X, int Y);
public sealed record MonsterAbilityDto(string? AbilityId, string? Name, string? Description, string? ActionType, int Range);
public sealed record TrapActionDto(string? Kind, int Amount, int Duration, int Radius, string? Target);
public sealed record TrapPayloadDto(List<TrapActionDto> Actions);

// ── empty marker components ──────────────────────────────────────────────────
public sealed record EmptyDto;   // shared by all data-less marker tags

// ── Balance ──────────────────────────────────────────────────────────────────
public sealed record ThreatArchetypeTagDto(ThreatArchetype Archetype);

// ── Combat ───────────────────────────────────────────────────────────────────
public sealed record ConsumableDto(int HealAmount, int StackSize, bool IsPotion, int UseCooldownTurns);
public sealed record DamageModifiersDto(string? Resistance, string? Vulnerability);
public sealed record EquipmentDto(int? MainHand, int? OffHand, int? Head, int? Chest, int? Feet,
    int? LeftRing, int? RightRing, int? Neck, int? Quiver);
public sealed record EquippableDto(EquipmentSlot Slot, int DamageMin, int DamageMax, int ToHitBonus,
    int ArmorClassBonus, string? DamageType, string? ArmorType, int CritThreshold, string? Material,
    int BaseDamageMax, bool IsRangedWeapon, bool TwoHanded, bool IsSpecialAmmo);
public sealed record FighterDto(int BaseMaxHp, int Hp, int Strength, int Dexterity, int Constitution,
    int Accuracy, int Evasion, int DamageMin, int DamageMax, string? NaturalDamageType, int BasePower,
    int BaseDefense, int Xp, int RingMaxHpBonus, int BoonMaxHpBonus, int PotionCooldownRemaining,
    bool CanOpenDoors, bool SurpriseAttackAvailable);
public sealed record PortalComponentDto(PortalType Type, int LinkedPortalId, bool UsedThisTurn);
public sealed record SpeedBonusTrackerDto(double BaseRatio, double EquipmentRatio, double RingRatio,
    int AttackCounter, int LastTargetId);
public sealed record SpellEffectDto(string? SpellId, TargetingMode Targeting, int Damage, int Radius,
    int Range, int Duration, double MisfireChance, string? ThrowSpellId);
public sealed record WandComponentDto(int Charges, int MaxCharges, string? RechargeScrollId, bool Infinite);

// ── Combat.StatusEffects ─────────────────────────────────────────────────────
// All status effects carry RemainingTurns; EffectName/IsPermanent are computed and omitted.
public sealed record StatusTurnsDto(int RemainingTurns);                            // RemainingTurns only
public sealed record AggravatedEffectDto(int RemainingTurns, string? TargetFaction);
public sealed record BarkskinEffectDto(int RemainingTurns, int AcBonus);
public sealed record BleedEffectDto(int RemainingTurns, int Severity);
public sealed record BlindedEffectDto(int RemainingTurns, int AccuracyPenalty);
public sealed record BurningEffectDto(int RemainingTurns, int DamagePerTurn);
public sealed record CrippledEffectDto(int RemainingTurns, int ToHitPenalty, int AcPenalty);
public sealed record DissonantChantEffectDto(int RemainingTurns, int MoveEnergyTax, int ChantingShamanId);
public sealed record EnragedEffectDto(int RemainingTurns, double DamageMultiplier, double AccuracyMultiplier, bool HostileToAll);
public sealed record FocusedEffectDto(int RemainingTurns, int AccuracyBonus);
public sealed record HeroismEffectDto(int RemainingTurns, int AttackBonus, int DamageBonus);
public sealed record PlagueEffectDto(int RemainingTurns, int DamagePerTurn);
public sealed record PoisonEffectDto(int RemainingTurns, int DamagePerTurn);
public sealed record PossessionEffectDto(int RemainingTurns, int PossessorEntityId, int? OriginatorBodyId,
    int DrainPerTurn, PossessionSource Source, int EnteredTurn, int WandTileX, int WandTileY,
    bool DrainWarning25Fired, bool DrainWarning50Fired, bool NearDeathWarningFired, bool HomeBodyThreatenedFired);
public sealed record ProtectionEffectDto(int RemainingTurns, int AcBonus);
public sealed record RallyEffectDto(int RemainingTurns, int ToHitBonus, int DamageBonus, int ChieftainId);
public sealed record RegenerationEffectDto(int RemainingTurns, int HealPerTurn);
public sealed record ShieldEffectDto(int RemainingTurns, int AcBonus);
public sealed record SluggishEffectDto(int RemainingTurns, float SpeedPenaltyRatio);
public sealed record SpeedEffectDto(int RemainingTurns, float SpeedBonusRatio);
public sealed record TauntedEffectDto(int RemainingTurns, int TauntTargetId);
public sealed record WeaknessEffectDto(int RemainingTurns, int DamagePenalty);

// ── ECS ──────────────────────────────────────────────────────────────────────
public sealed record AiComponentDto(string? AiType, string? Faction, List<string> Tags,
    bool CanSeekItems, bool CanUseItems, int SeekDistance, int InventorySize);
public sealed record AlertedStateDto(int LastKnownPlayerX, int LastKnownPlayerY, int TurnsUntilDeaggro);
public sealed record AutoExploreStateDto(bool IsActive, List<PointDto> CurrentPath, PointDto[] ExploredSnapshot,
    int[] KnownMonsterIds, int[] KnownItemIds, int[] KnownFeatureIds, PointDto[] KnownStairs,
    int LastHp, string? StopReason, int StuckCounter, PointDto? LastExpectedPosition,
    PointDto[] PositionHistory);
public sealed record ChestComponentDto(bool IsOpen, bool IsLooted, List<string> LootItemIds);
public sealed record ChestLootStashDto(int[] ItemIds);
public sealed record CorpseComponentDto(string? OriginalMonsterId, string? OriginalName, int DeathTurn,
    int RaiseCount, int MaxRaises, bool Consumed, string? RaisedByName, CorpseState State, string? CorpseId,
    int BaseHp, int BaseDamageMin, int BaseDamageMax, int BaseStrength, int BaseDexterity, int BaseConstitution,
    int BaseDefense, int BaseAccuracy, int BaseEvasion);
public sealed record CorrosionComponentDto(double Chance);
public sealed record DestructiblePropComponentDto(string? PropKind, bool IsResolved, List<int> LootEntityIds,
    TrapPayloadDto? TrapPayload, TrapActionDto? RouseAction, int ClosedTileId, int OpenTileId);
public sealed record FloorTrapComponentDto(string? TrapType, bool IsSpent, bool IsDetected, bool IsDetectable,
    double PassiveDetectChance, TrapPayloadDto? Payload, int VisibleTileId, float[]? TileModulate);
public sealed record HostAbilityComponentDto(MonsterAbilityDto[] Abilities);
public sealed record IdentifiableItemDto(string? UnidentifiedName, string? IdentifiedName);
public sealed record InnateRegenComponentDto(int HealPerTurn);
public sealed record InventoryDto(int[] ItemIds);
public sealed record ItemTagDto(string TypeId);
public sealed record KeyItemComponentDto(int LockColorId);
public sealed record LichAiComponentDto(int SoulBoltRange, double SoulBoltDamagePct, int SoulBoltCooldownTurns,
    int SoulBoltCooldownRemaining, int CommandTheDeadRadius, int DeathSiphonRadius, string? SummonMonsterId);
public sealed record LifeDrainComponentDto(double DrainPct);
public sealed record LockableComponentDto(int LockColorId, bool IsLocked);
public sealed record MuralComponentDto(string? Text, string? MuralId, int TileId, bool HasBeenExamined);
public sealed record NecromancerAiComponentDto(int RaiseRange, int RaiseCooldown, int CooldownRemaining,
    int DangerRadius, int PreferredDistanceMin, int PreferredDistanceMax);
public sealed record OnHitEffectComponentDto(string EffectType, int Duration);
public sealed record OrcChieftainComponentDto(bool RallyCried, bool BellowedAtLowHp, int RallyRange,
    int RallyMinAllies, double BellowHpThreshold, int BellowDebuffDuration, int PreferredDistanceMin,
    int PreferredDistanceMax, int DangerRadius);
public sealed record OrcShamanComponentDto(int HexCooldownRemaining, int HexCooldownTurns, int HexRange,
    int HexDuration, bool IsChanneling, int ChantTurnsRemaining, int ChantCooldownRemaining, int ChantCooldownTurns,
    int ChantRange, int ChantDuration, int? ChantTargetEntityId, int PreferredDistanceMin, int PreferredDistanceMax,
    int DangerRadius);
public sealed record PortalCastStateComponentDto(PortalCastStep Step, int? PendingEntrance);
public sealed record RaisedFromCorpseTagDto(string? CorpseId);
public sealed record RingEffectComponentDto(RingEffectKind Kind, int Strength, double SpeedRatio);
public sealed record RunAggressionTallyDto(List<KillEntryDto> Kills);
public sealed record KillEntryDto(string Faction, int Count);
public sealed record ShieldWallComponentDto(int AcBonusPerAlly, int CurrentAcBonus);
public sealed record SignpostComponentDto(string? Message, string? SignType, bool HasBeenRead);
public sealed record SkirmisherComponentDto(int LeapCooldownRemaining, int LeapCooldownTurns, int LeapRangeMin, int LeapRangeMax);
public sealed record SpeciesTagDto(string TypeId);
public sealed record SplitTrackerDto(double TriggerHpPct, string? ChildType, int MinChildren, int MaxChildren,
    int[]? Weights, bool HasSplit);
public sealed record StairDto(bool IsDown, int TargetDepth);
public sealed record StatusImmunityComponentDto(string[] Immunities);
public sealed record TrapPayloadComponentDto(List<TrapActionDto> Actions);
public sealed record WeaponAcidCoatingComponentDto(int HitsRemaining, int EffectDuration);
