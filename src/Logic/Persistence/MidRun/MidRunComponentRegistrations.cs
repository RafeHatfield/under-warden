using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;
using static UnderWarden.Logic.Persistence.MidRun.MidRunComponentRegistry;

namespace UnderWarden.Logic.Persistence.MidRun;

/// <summary>
/// The hand-written per-type registrations. Every concrete IComponent gets exactly one entry here;
/// the reflection completeness gate proves this list is exhaustive. Discriminators are the concrete
/// type's short name (stable across refactors of namespace/assembly and immune to enum-ordinal drift).
/// </summary>
internal static class MidRunComponentRegistrations
{
    private static readonly MidRunJsonContext C = MidRunJsonContext.Default;

    public static void RegisterAll()
    {
        // ── empty marker components ──────────────────────────────────────────
        RegisterEmpty<EngulfsOnHitTag>("EngulfsOnHitTag", () => new());
        RegisterEmpty<FreeActionTag>("FreeActionTag", () => new());
        RegisterEmpty<HasAttackedPlayerTag>("HasAttackedPlayerTag", () => new());
        RegisterEmpty<TransfersEffectsOnHitComponent>("TransfersEffectsOnHitComponent", () => new());
        RegisterEmpty<UnattendedBodyTag>("UnattendedBodyTag", () => new());

        // ── RemainingTurns-only effects ──────────────────────────────────────
        RegisterTurns<AcidEffect>("AcidEffect", () => new());
        RegisterTurns<DisarmedEffect>("DisarmedEffect", () => new());
        RegisterTurns<DisorientationEffect>("DisorientationEffect", () => new());
        RegisterTurns<EngulfedEffect>("EngulfedEffect", () => new());
        RegisterTurns<EntangledEffect>("EntangledEffect", () => new());
        RegisterTurns<FearEffect>("FearEffect", () => new());
        RegisterTurns<ImmobilizedEffect>("ImmobilizedEffect", () => new());
        RegisterTurns<InvisibilityEffect>("InvisibilityEffect", () => new());
        RegisterTurns<SilencedEffect>("SilencedEffect", () => new());
        RegisterTurns<SleepEffect>("SleepEffect", () => new());
        RegisterTurns<SlowedEffect>("SlowedEffect", () => new());

        // ChargingSoulBoltEffect is not an IStatusEffect but carries only RemainingTurns.
        Register<ChargingSoulBoltEffect, StatusTurnsDto>("ChargingSoulBoltEffect", C.StatusTurnsDto,
            c => new StatusTurnsDto(c.RemainingTurns),
            (d, _) => new ChargingSoulBoltEffect { RemainingTurns = d.RemainingTurns });

        // ── Balance ──────────────────────────────────────────────────────────
        Register<ThreatArchetypeTag, ThreatArchetypeTagDto>("ThreatArchetypeTag", C.ThreatArchetypeTagDto,
            c => new(c.Archetype),
            (d, _) => new ThreatArchetypeTag(d.Archetype));

        // ── Combat ───────────────────────────────────────────────────────────
        Register<Consumable, ConsumableDto>("Consumable", C.ConsumableDto,
            c => new(c.HealAmount, c.StackSize, c.IsPotion, c.UseCooldownTurns),
            (d, _) => new Consumable(d.HealAmount, d.IsPotion, d.UseCooldownTurns) { StackSize = d.StackSize });

        Register<DamageModifiers, DamageModifiersDto>("DamageModifiers", C.DamageModifiersDto,
            c => new(c.Resistance, c.Vulnerability),
            (d, _) => new DamageModifiers { Resistance = d.Resistance, Vulnerability = d.Vulnerability });

        Register<Equipment, EquipmentDto>("Equipment", C.EquipmentDto,
            c => new(c.MainHand?.Id, c.OffHand?.Id, c.Head?.Id, c.Chest?.Id, c.Feet?.Id,
                     c.LeftRing?.Id, c.RightRing?.Id, c.Neck?.Id, c.Quiver?.Id),
            (d, r) => new Equipment
            {
                MainHand = r.ResolveOptional(d.MainHand), OffHand = r.ResolveOptional(d.OffHand),
                Head = r.ResolveOptional(d.Head), Chest = r.ResolveOptional(d.Chest), Feet = r.ResolveOptional(d.Feet),
                LeftRing = r.ResolveOptional(d.LeftRing), RightRing = r.ResolveOptional(d.RightRing),
                Neck = r.ResolveOptional(d.Neck), Quiver = r.ResolveOptional(d.Quiver),
            },
            children: c => new[] { c.MainHand, c.OffHand, c.Head, c.Chest, c.Feet, c.LeftRing, c.RightRing, c.Neck, c.Quiver }
                .Where(e => e is not null)!);

        Register<Equippable, EquippableDto>("Equippable", C.EquippableDto,
            c => new(c.Slot, c.DamageMin, c.DamageMax, c.ToHitBonus, c.ArmorClassBonus, c.DamageType, c.ArmorType,
                     c.CritThreshold, c.Material, c.BaseDamageMax, c.IsRangedWeapon, c.TwoHanded, c.IsSpecialAmmo),
            (d, _) =>
            {
                var e = new Equippable(d.Slot)
                {
                    DamageMin = d.DamageMin, DamageMax = d.DamageMax, ToHitBonus = d.ToHitBonus,
                    ArmorClassBonus = d.ArmorClassBonus, DamageType = d.DamageType, ArmorType = d.ArmorType,
                    CritThreshold = d.CritThreshold, Material = d.Material,
                    IsRangedWeapon = d.IsRangedWeapon, TwoHanded = d.TwoHanded, IsSpecialAmmo = d.IsSpecialAmmo,
                };
                e.RestoreBaseDamageMax(d.BaseDamageMax);
                return e;
            });

        Register<Fighter, FighterDto>("Fighter", C.FighterDto,
            c => new(c.BaseMaxHp, c.Hp, c.Strength, c.Dexterity, c.Constitution, c.Accuracy, c.Evasion,
                     c.DamageMin, c.DamageMax, c.NaturalDamageType, c.BasePower, c.BaseDefense, c.Xp,
                     c.RingMaxHpBonus, c.BoonMaxHpBonus, c.PotionCooldownRemaining, c.CanOpenDoors, c.SurpriseAttackAvailable),
            (d, _) =>
            {
                var f = new Fighter(d.BaseMaxHp, d.BaseDefense, d.BasePower, d.Xp, d.DamageMin, d.DamageMax,
                                    d.Strength, d.Dexterity, d.Constitution, d.Accuracy, d.Evasion)
                {
                    Hp = d.Hp, NaturalDamageType = d.NaturalDamageType, RingMaxHpBonus = d.RingMaxHpBonus,
                    BoonMaxHpBonus = d.BoonMaxHpBonus, PotionCooldownRemaining = d.PotionCooldownRemaining,
                    CanOpenDoors = d.CanOpenDoors,
                };
                // SurpriseAttackAvailable has only a private setter (defaults true; flips false once consumed).
                if (!d.SurpriseAttackAvailable) f.ConsumeSurpriseAttack();
                return f;
            });

        Register<PortalComponent, PortalComponentDto>("PortalComponent", C.PortalComponentDto,
            c => new(c.Type, c.LinkedPortalId, c.UsedThisTurn),
            (d, _) => new PortalComponent { Type = d.Type, LinkedPortalId = d.LinkedPortalId, UsedThisTurn = d.UsedThisTurn });

        Register<SpeedBonusTracker, SpeedBonusTrackerDto>("SpeedBonusTracker", C.SpeedBonusTrackerDto,
            c => new(c.BaseRatio, c.EquipmentRatio, c.RingRatio, c.AttackCounter, c.LastTargetId),
            (d, _) =>
            {
                var t = new SpeedBonusTracker(d.BaseRatio) { EquipmentRatio = d.EquipmentRatio, RingRatio = d.RingRatio };
                t.RestoreMomentum(d.AttackCounter, d.LastTargetId);
                return t;
            });

        Register<SpellEffect, SpellEffectDto>("SpellEffect", C.SpellEffectDto,
            c => new(c.SpellId, c.Targeting, c.Damage, c.Radius, c.Range, c.Duration, c.MisfireChance, c.ThrowSpellId),
            (d, _) => new SpellEffect
            {
                SpellId = d.SpellId, Targeting = d.Targeting, Damage = d.Damage, Radius = d.Radius,
                Range = d.Range, Duration = d.Duration, MisfireChance = d.MisfireChance, ThrowSpellId = d.ThrowSpellId,
            });

        Register<WandComponent, WandComponentDto>("WandComponent", C.WandComponentDto,
            c => new(c.Charges, c.MaxCharges, c.RechargeScrollId, c.Infinite),
            (d, _) => new WandComponent { Charges = d.Charges, MaxCharges = d.MaxCharges, RechargeScrollId = d.RechargeScrollId, Infinite = d.Infinite });

        // ── Combat.StatusEffects (with data) ─────────────────────────────────
        Register<AggravatedEffect, AggravatedEffectDto>("AggravatedEffect", C.AggravatedEffectDto,
            c => new(c.RemainingTurns, c.TargetFaction),
            (d, _) => new AggravatedEffect { RemainingTurns = d.RemainingTurns, TargetFaction = d.TargetFaction });
        Register<BarkskinEffect, BarkskinEffectDto>("BarkskinEffect", C.BarkskinEffectDto,
            c => new(c.RemainingTurns, c.AcBonus),
            (d, _) => new BarkskinEffect { RemainingTurns = d.RemainingTurns, AcBonus = d.AcBonus });
        Register<BleedEffect, BleedEffectDto>("BleedEffect", C.BleedEffectDto,
            c => new(c.RemainingTurns, c.Severity),
            (d, _) => new BleedEffect { RemainingTurns = d.RemainingTurns, Severity = d.Severity });
        Register<BlindedEffect, BlindedEffectDto>("BlindedEffect", C.BlindedEffectDto,
            c => new(c.RemainingTurns, c.AccuracyPenalty),
            (d, _) => new BlindedEffect { RemainingTurns = d.RemainingTurns, AccuracyPenalty = d.AccuracyPenalty });
        Register<BurningEffect, BurningEffectDto>("BurningEffect", C.BurningEffectDto,
            c => new(c.RemainingTurns, c.DamagePerTurn),
            (d, _) => new BurningEffect { RemainingTurns = d.RemainingTurns, DamagePerTurn = d.DamagePerTurn });
        Register<CrippledEffect, CrippledEffectDto>("CrippledEffect", C.CrippledEffectDto,
            c => new(c.RemainingTurns, c.ToHitPenalty, c.AcPenalty),
            (d, _) => new CrippledEffect { RemainingTurns = d.RemainingTurns, ToHitPenalty = d.ToHitPenalty, AcPenalty = d.AcPenalty });
        Register<DissonantChantEffect, DissonantChantEffectDto>("DissonantChantEffect", C.DissonantChantEffectDto,
            c => new(c.RemainingTurns, c.MoveEnergyTax, c.ChantingShamanId),
            (d, _) => new DissonantChantEffect { RemainingTurns = d.RemainingTurns, MoveEnergyTax = d.MoveEnergyTax, ChantingShamanId = d.ChantingShamanId });
        Register<EnragedEffect, EnragedEffectDto>("EnragedEffect", C.EnragedEffectDto,
            c => new(c.RemainingTurns, c.DamageMultiplier, c.AccuracyMultiplier, c.HostileToAll),
            (d, _) => new EnragedEffect { RemainingTurns = d.RemainingTurns, DamageMultiplier = d.DamageMultiplier, AccuracyMultiplier = d.AccuracyMultiplier, HostileToAll = d.HostileToAll });
        Register<FocusedEffect, FocusedEffectDto>("FocusedEffect", C.FocusedEffectDto,
            c => new(c.RemainingTurns, c.AccuracyBonus),
            (d, _) => new FocusedEffect { RemainingTurns = d.RemainingTurns, AccuracyBonus = d.AccuracyBonus });
        Register<HeroismEffect, HeroismEffectDto>("HeroismEffect", C.HeroismEffectDto,
            c => new(c.RemainingTurns, c.AttackBonus, c.DamageBonus),
            (d, _) => new HeroismEffect { RemainingTurns = d.RemainingTurns, AttackBonus = d.AttackBonus, DamageBonus = d.DamageBonus });
        Register<PlagueEffect, PlagueEffectDto>("PlagueEffect", C.PlagueEffectDto,
            c => new(c.RemainingTurns, c.DamagePerTurn),
            (d, _) => new PlagueEffect { RemainingTurns = d.RemainingTurns, DamagePerTurn = d.DamagePerTurn });
        Register<PoisonEffect, PoisonEffectDto>("PoisonEffect", C.PoisonEffectDto,
            c => new(c.RemainingTurns, c.DamagePerTurn),
            (d, _) => new PoisonEffect { RemainingTurns = d.RemainingTurns, DamagePerTurn = d.DamagePerTurn });
        Register<PossessionEffect, PossessionEffectDto>("PossessionEffect", C.PossessionEffectDto,
            c => new(c.RemainingTurns, c.PossessorEntityId, c.OriginatorBodyId, c.DrainPerTurn, c.Source, c.EnteredTurn,
                     c.WandTileX, c.WandTileY, c.DrainWarning25Fired, c.DrainWarning50Fired, c.NearDeathWarningFired, c.HomeBodyThreatenedFired),
            (d, _) => new PossessionEffect
            {
                RemainingTurns = d.RemainingTurns, PossessorEntityId = d.PossessorEntityId, OriginatorBodyId = d.OriginatorBodyId,
                DrainPerTurn = d.DrainPerTurn, Source = d.Source, EnteredTurn = d.EnteredTurn, WandTileX = d.WandTileX,
                WandTileY = d.WandTileY, DrainWarning25Fired = d.DrainWarning25Fired, DrainWarning50Fired = d.DrainWarning50Fired,
                NearDeathWarningFired = d.NearDeathWarningFired, HomeBodyThreatenedFired = d.HomeBodyThreatenedFired,
            });
        Register<ProtectionEffect, ProtectionEffectDto>("ProtectionEffect", C.ProtectionEffectDto,
            c => new(c.RemainingTurns, c.AcBonus),
            (d, _) => new ProtectionEffect { RemainingTurns = d.RemainingTurns, AcBonus = d.AcBonus });
        Register<RallyEffect, RallyEffectDto>("RallyEffect", C.RallyEffectDto,
            c => new(c.RemainingTurns, c.ToHitBonus, c.DamageBonus, c.ChieftainId),
            (d, _) => new RallyEffect { RemainingTurns = d.RemainingTurns, ToHitBonus = d.ToHitBonus, DamageBonus = d.DamageBonus, ChieftainId = d.ChieftainId });
        Register<RegenerationEffect, RegenerationEffectDto>("RegenerationEffect", C.RegenerationEffectDto,
            c => new(c.RemainingTurns, c.HealPerTurn),
            (d, _) => new RegenerationEffect { RemainingTurns = d.RemainingTurns, HealPerTurn = d.HealPerTurn });
        Register<ShieldEffect, ShieldEffectDto>("ShieldEffect", C.ShieldEffectDto,
            c => new(c.RemainingTurns, c.AcBonus),
            (d, _) => new ShieldEffect { RemainingTurns = d.RemainingTurns, AcBonus = d.AcBonus });
        Register<SluggishEffect, SluggishEffectDto>("SluggishEffect", C.SluggishEffectDto,
            c => new(c.RemainingTurns, c.SpeedPenaltyRatio),
            (d, _) => new SluggishEffect { RemainingTurns = d.RemainingTurns, SpeedPenaltyRatio = d.SpeedPenaltyRatio });
        Register<SpeedEffect, SpeedEffectDto>("SpeedEffect", C.SpeedEffectDto,
            c => new(c.RemainingTurns, c.SpeedBonusRatio),
            (d, _) => new SpeedEffect { RemainingTurns = d.RemainingTurns, SpeedBonusRatio = d.SpeedBonusRatio });
        Register<TauntedEffect, TauntedEffectDto>("TauntedEffect", C.TauntedEffectDto,
            c => new(c.RemainingTurns, c.TauntTargetId),
            (d, _) => new TauntedEffect { RemainingTurns = d.RemainingTurns, TauntTargetId = d.TauntTargetId });
        Register<WeaknessEffect, WeaknessEffectDto>("WeaknessEffect", C.WeaknessEffectDto,
            c => new(c.RemainingTurns, c.DamagePenalty),
            (d, _) => new WeaknessEffect { RemainingTurns = d.RemainingTurns, DamagePenalty = d.DamagePenalty });

        // ── ECS ──────────────────────────────────────────────────────────────
        Register<AiComponent, AiComponentDto>("AiComponent", C.AiComponentDto,
            c => new(c.AiType, c.Faction, new List<string>(c.Tags), c.CanSeekItems, c.CanUseItems, c.SeekDistance, c.InventorySize),
            (d, _) => new AiComponent { AiType = d.AiType, Faction = d.Faction, Tags = new List<string>(d.Tags),
                CanSeekItems = d.CanSeekItems, CanUseItems = d.CanUseItems, SeekDistance = d.SeekDistance, InventorySize = d.InventorySize });

        Register<AlertedState, AlertedStateDto>("AlertedState", C.AlertedStateDto,
            c => new(c.LastKnownPlayerX, c.LastKnownPlayerY, c.TurnsUntilDeaggro),
            (d, _) => new AlertedState { LastKnownPlayerX = d.LastKnownPlayerX, LastKnownPlayerY = d.LastKnownPlayerY, TurnsUntilDeaggro = d.TurnsUntilDeaggro });

        Register<AutoExploreState, AutoExploreStateDto>("AutoExploreState", C.AutoExploreStateDto,
            c => new(c.IsActive,
                c.CurrentPath.Select(p => new PointDto(p.X, p.Y)).ToList(),
                c.ExploredSnapshot.OrderBy(p => p.X).ThenBy(p => p.Y).Select(p => new PointDto(p.X, p.Y)).ToArray(),
                c.KnownMonsterIds.OrderBy(i => i).ToArray(),
                c.KnownItemIds.OrderBy(i => i).ToArray(),
                c.KnownFeatureIds.OrderBy(i => i).ToArray(),
                c.KnownStairs.OrderBy(p => p.X).ThenBy(p => p.Y).Select(p => new PointDto(p.X, p.Y)).ToArray(),
                c.LastHp, c.StopReason, c.StuckCounter,
                c.LastExpectedPosition is { } lp ? new PointDto(lp.X, lp.Y) : null,
                c.PositionHistorySnapshot.Select(p => new PointDto(p.X, p.Y)).ToArray()),
            (d, _) =>
            {
                var state = new AutoExploreState
                {
                IsActive = d.IsActive,
                CurrentPath = d.CurrentPath.Select(p => (p.X, p.Y)).ToList(),
                ExploredSnapshot = new HashSet<(int, int)>(d.ExploredSnapshot.Select(p => (p.X, p.Y))),
                KnownMonsterIds = new HashSet<int>(d.KnownMonsterIds),
                KnownItemIds = new HashSet<int>(d.KnownItemIds),
                KnownFeatureIds = new HashSet<int>(d.KnownFeatureIds),
                KnownStairs = new HashSet<(int, int)>(d.KnownStairs.Select(p => (p.X, p.Y))),
                LastHp = d.LastHp, StopReason = d.StopReason, StuckCounter = d.StuckCounter,
                LastExpectedPosition = d.LastExpectedPosition is { } lp ? (lp.X, lp.Y) : null,
            };
            state.RestorePositionHistory(d.PositionHistory.Select(p => (p.X, p.Y)).ToArray());
            return state;
        });

        Register<ChestComponent, ChestComponentDto>("ChestComponent", C.ChestComponentDto,
            c => new(c.IsOpen, c.IsLooted, new List<string>(c.LootItemIds)),
            (d, _) => new ChestComponent { IsOpen = d.IsOpen, IsLooted = d.IsLooted, LootItemIds = new List<string>(d.LootItemIds) });

        Register<ChestLootStash, ChestLootStashDto>("ChestLootStash", C.ChestLootStashDto,
            c => new(c.Items.Select(e => e.Id).ToArray()),
            (d, r) => new ChestLootStash(d.ItemIds.Select(r.Resolve).ToList()),
            children: c => c.Items);

        Register<CorpseComponent, CorpseComponentDto>("CorpseComponent", C.CorpseComponentDto,
            c => new(c.OriginalMonsterId, c.OriginalName, c.DeathTurn, c.RaiseCount, c.MaxRaises, c.Consumed, c.RaisedByName,
                     c.State, c.CorpseId, c.BaseHp, c.BaseDamageMin, c.BaseDamageMax, c.BaseStrength, c.BaseDexterity,
                     c.BaseConstitution, c.BaseDefense, c.BaseAccuracy, c.BaseEvasion),
            (d, _) => new CorpseComponent
            {
                OriginalMonsterId = d.OriginalMonsterId, OriginalName = d.OriginalName, DeathTurn = d.DeathTurn,
                RaiseCount = d.RaiseCount, MaxRaises = d.MaxRaises, Consumed = d.Consumed, RaisedByName = d.RaisedByName,
                State = d.State, CorpseId = d.CorpseId, BaseHp = d.BaseHp, BaseDamageMin = d.BaseDamageMin,
                BaseDamageMax = d.BaseDamageMax, BaseStrength = d.BaseStrength, BaseDexterity = d.BaseDexterity,
                BaseConstitution = d.BaseConstitution, BaseDefense = d.BaseDefense, BaseAccuracy = d.BaseAccuracy, BaseEvasion = d.BaseEvasion,
            });

        Register<CorrosionComponent, CorrosionComponentDto>("CorrosionComponent", C.CorrosionComponentDto,
            c => new(c.Chance), (d, _) => new CorrosionComponent(d.Chance));

        Register<DestructiblePropComponent, DestructiblePropComponentDto>("DestructiblePropComponent", C.DestructiblePropComponentDto,
            c => new(c.PropKind, c.IsResolved, new List<int>(c.LootEntityIds),
                     c.TrapPayload is null ? null : new TrapPayloadDto(c.TrapPayload.Actions.Select(ToTrapActionDto).ToList()),
                     c.RouseAction is null ? null : ToTrapActionDto(c.RouseAction), c.ClosedTileId, c.OpenTileId),
            (d, _) => new DestructiblePropComponent
            {
                PropKind = d.PropKind ?? "", IsResolved = d.IsResolved, LootEntityIds = new List<int>(d.LootEntityIds),
                TrapPayload = d.TrapPayload is null ? null : new TrapPayloadComponent { Actions = d.TrapPayload.Actions.Select(FromTrapActionDto).ToList() },
                RouseAction = d.RouseAction is null ? null : FromTrapActionDto(d.RouseAction),
                ClosedTileId = d.ClosedTileId, OpenTileId = d.OpenTileId,
            });

        Register<FloorTrapComponent, FloorTrapComponentDto>("FloorTrapComponent", C.FloorTrapComponentDto,
            c => new(c.TrapType, c.IsSpent, c.IsDetected, c.IsDetectable, c.PassiveDetectChance,
                     c.Payload is null ? null : new TrapPayloadDto(c.Payload.Actions.Select(ToTrapActionDto).ToList()),
                     c.VisibleTileId, c.TileModulate),
            (d, _) => new FloorTrapComponent
            {
                TrapType = d.TrapType ?? "", IsSpent = d.IsSpent, IsDetected = d.IsDetected, IsDetectable = d.IsDetectable,
                PassiveDetectChance = d.PassiveDetectChance,
                Payload = d.Payload is null ? new TrapPayloadComponent() : new TrapPayloadComponent { Actions = d.Payload.Actions.Select(FromTrapActionDto).ToList() },
                VisibleTileId = d.VisibleTileId, TileModulate = d.TileModulate,
            });

        Register<HostAbilityComponent, HostAbilityComponentDto>("HostAbilityComponent", C.HostAbilityComponentDto,
            c => new(c.Abilities.Select(a => new MonsterAbilityDto(a.AbilityId, a.Name, a.Description, a.ActionType, a.Range)).ToArray()),
            (d, _) => new HostAbilityComponent { Abilities = d.Abilities.Select(a => new MonsterAbilityDefinition
                { AbilityId = a.AbilityId ?? "", Name = a.Name ?? "", Description = a.Description ?? "", ActionType = a.ActionType ?? "", Range = a.Range }).ToArray() });

        Register<IdentifiableItem, IdentifiableItemDto>("IdentifiableItem", C.IdentifiableItemDto,
            c => new(c.UnidentifiedName, c.IdentifiedName),
            (d, _) => new IdentifiableItem { UnidentifiedName = d.UnidentifiedName ?? "", IdentifiedName = d.IdentifiedName ?? "" });

        Register<InnateRegenComponent, InnateRegenComponentDto>("InnateRegenComponent", C.InnateRegenComponentDto,
            c => new(c.HealPerTurn), (d, _) => new InnateRegenComponent { HealPerTurn = d.HealPerTurn });

        Register<Inventory, InventoryDto>("Inventory", C.InventoryDto,
            c => new(c.Items.Select(e => e.Id).ToArray()),
            (d, r) =>
            {
                var inv = new Inventory();
                foreach (var id in d.ItemIds) inv.Add(r.Resolve(id));
                return inv;
            },
            children: c => c.Items);

        Register<ItemTag, ItemTagDto>("ItemTag", C.ItemTagDto,
            c => new(c.TypeId), (d, _) => new ItemTag(d.TypeId));

        Register<KeyItemComponent, KeyItemComponentDto>("KeyItemComponent", C.KeyItemComponentDto,
            c => new(c.LockColorId), (d, _) => new KeyItemComponent { LockColorId = d.LockColorId });

        Register<LichAiComponent, LichAiComponentDto>("LichAiComponent", C.LichAiComponentDto,
            c => new(c.SoulBoltRange, c.SoulBoltDamagePct, c.SoulBoltCooldownTurns, c.SoulBoltCooldownRemaining,
                     c.CommandTheDeadRadius, c.DeathSiphonRadius, c.SummonMonsterId),
            (d, _) => new LichAiComponent
            {
                SoulBoltRange = d.SoulBoltRange, SoulBoltDamagePct = d.SoulBoltDamagePct, SoulBoltCooldownTurns = d.SoulBoltCooldownTurns,
                SoulBoltCooldownRemaining = d.SoulBoltCooldownRemaining, CommandTheDeadRadius = d.CommandTheDeadRadius,
                DeathSiphonRadius = d.DeathSiphonRadius, SummonMonsterId = d.SummonMonsterId,
            });

        Register<LifeDrainComponent, LifeDrainComponentDto>("LifeDrainComponent", C.LifeDrainComponentDto,
            c => new(c.DrainPct), (d, _) => new LifeDrainComponent(d.DrainPct));

        Register<LockableComponent, LockableComponentDto>("LockableComponent", C.LockableComponentDto,
            c => new(c.LockColorId, c.IsLocked),
            (d, _) => new LockableComponent { LockColorId = d.LockColorId, IsLocked = d.IsLocked });

        Register<MuralComponent, MuralComponentDto>("MuralComponent", C.MuralComponentDto,
            c => new(c.Text, c.MuralId, c.TileId, c.HasBeenExamined),
            (d, _) => new MuralComponent { Text = d.Text ?? "", MuralId = d.MuralId ?? "", TileId = d.TileId, HasBeenExamined = d.HasBeenExamined });

        Register<NecromancerAiComponent, NecromancerAiComponentDto>("NecromancerAiComponent", C.NecromancerAiComponentDto,
            c => new(c.RaiseRange, c.RaiseCooldown, c.CooldownRemaining, c.DangerRadius, c.PreferredDistanceMin, c.PreferredDistanceMax),
            (d, _) => new NecromancerAiComponent
            {
                RaiseRange = d.RaiseRange, RaiseCooldown = d.RaiseCooldown, CooldownRemaining = d.CooldownRemaining,
                DangerRadius = d.DangerRadius, PreferredDistanceMin = d.PreferredDistanceMin, PreferredDistanceMax = d.PreferredDistanceMax,
            });

        Register<OnHitEffectComponent, OnHitEffectComponentDto>("OnHitEffectComponent", C.OnHitEffectComponentDto,
            c => new(c.EffectType, c.Duration), (d, _) => new OnHitEffectComponent(d.EffectType, d.Duration));

        Register<OrcChieftainComponent, OrcChieftainComponentDto>("OrcChieftainComponent", C.OrcChieftainComponentDto,
            c => new(c.RallyCried, c.BellowedAtLowHp, c.RallyRange, c.RallyMinAllies, c.BellowHpThreshold,
                     c.BellowDebuffDuration, c.PreferredDistanceMin, c.PreferredDistanceMax, c.DangerRadius),
            (d, _) => new OrcChieftainComponent
            {
                RallyCried = d.RallyCried, BellowedAtLowHp = d.BellowedAtLowHp, RallyRange = d.RallyRange,
                RallyMinAllies = d.RallyMinAllies, BellowHpThreshold = d.BellowHpThreshold, BellowDebuffDuration = d.BellowDebuffDuration,
                PreferredDistanceMin = d.PreferredDistanceMin, PreferredDistanceMax = d.PreferredDistanceMax, DangerRadius = d.DangerRadius,
            });

        Register<OrcShamanComponent, OrcShamanComponentDto>("OrcShamanComponent", C.OrcShamanComponentDto,
            c => new(c.HexCooldownRemaining, c.HexCooldownTurns, c.HexRange, c.HexDuration, c.IsChanneling, c.ChantTurnsRemaining,
                     c.ChantCooldownRemaining, c.ChantCooldownTurns, c.ChantRange, c.ChantDuration, c.ChantTargetEntityId,
                     c.PreferredDistanceMin, c.PreferredDistanceMax, c.DangerRadius),
            (d, _) => new OrcShamanComponent
            {
                HexCooldownRemaining = d.HexCooldownRemaining, HexCooldownTurns = d.HexCooldownTurns, HexRange = d.HexRange,
                HexDuration = d.HexDuration, IsChanneling = d.IsChanneling, ChantTurnsRemaining = d.ChantTurnsRemaining,
                ChantCooldownRemaining = d.ChantCooldownRemaining, ChantCooldownTurns = d.ChantCooldownTurns, ChantRange = d.ChantRange,
                ChantDuration = d.ChantDuration, ChantTargetEntityId = d.ChantTargetEntityId,
                PreferredDistanceMin = d.PreferredDistanceMin, PreferredDistanceMax = d.PreferredDistanceMax, DangerRadius = d.DangerRadius,
            });

        Register<PortalCastStateComponent, PortalCastStateComponentDto>("PortalCastStateComponent", C.PortalCastStateComponentDto,
            c => new(c.Step, c.PendingEntrance?.Id),
            (d, r) => new PortalCastStateComponent { Step = d.Step, PendingEntrance = r.ResolveOptional(d.PendingEntrance) },
            children: c => c.PendingEntrance is null ? Array.Empty<Entity>() : new[] { c.PendingEntrance });

        Register<RaisedFromCorpseTag, RaisedFromCorpseTagDto>("RaisedFromCorpseTag", C.RaisedFromCorpseTagDto,
            c => new(c.CorpseId), (d, _) => new RaisedFromCorpseTag { CorpseId = d.CorpseId ?? "" });

        Register<RingEffectComponent, RingEffectComponentDto>("RingEffectComponent", C.RingEffectComponentDto,
            c => new(c.Kind, c.Strength, c.SpeedRatio),
            (d, _) => new RingEffectComponent(d.Kind, d.Strength, d.SpeedRatio));

        Register<RunAggressionTally, RunAggressionTallyDto>("RunAggressionTally", C.RunAggressionTallyDto,
            c => new(c.UnprovokedKillsByFaction.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new KillEntryDto(kv.Key, kv.Value)).ToList()),
            (d, _) =>
            {
                var t = new RunAggressionTally();
                foreach (var e in d.Kills) t.UnprovokedKillsByFaction[e.Faction] = e.Count;
                return t;
            });

        Register<ShieldWallComponent, ShieldWallComponentDto>("ShieldWallComponent", C.ShieldWallComponentDto,
            c => new(c.AcBonusPerAlly, c.CurrentAcBonus),
            (d, _) => new ShieldWallComponent { AcBonusPerAlly = d.AcBonusPerAlly, CurrentAcBonus = d.CurrentAcBonus });

        Register<SignpostComponent, SignpostComponentDto>("SignpostComponent", C.SignpostComponentDto,
            c => new(c.Message, c.SignType, c.HasBeenRead),
            (d, _) => new SignpostComponent { Message = d.Message ?? "", SignType = d.SignType ?? "", HasBeenRead = d.HasBeenRead });

        Register<SkirmisherComponent, SkirmisherComponentDto>("SkirmisherComponent", C.SkirmisherComponentDto,
            c => new(c.LeapCooldownRemaining, c.LeapCooldownTurns, c.LeapRangeMin, c.LeapRangeMax),
            (d, _) => new SkirmisherComponent
            {
                LeapCooldownRemaining = d.LeapCooldownRemaining, LeapCooldownTurns = d.LeapCooldownTurns,
                LeapRangeMin = d.LeapRangeMin, LeapRangeMax = d.LeapRangeMax,
            });

        Register<SpeciesTag, SpeciesTagDto>("SpeciesTag", C.SpeciesTagDto,
            c => new(c.TypeId), (d, _) => new SpeciesTag(d.TypeId));

        Register<SplitTracker, SplitTrackerDto>("SplitTracker", C.SplitTrackerDto,
            c => new(c.TriggerHpPct, c.ChildType, c.MinChildren, c.MaxChildren, c.Weights, c.HasSplit),
            (d, _) => new SplitTracker(d.TriggerHpPct, d.ChildType ?? "", d.MinChildren, d.MaxChildren, d.Weights) { HasSplit = d.HasSplit });

        Register<Stair, StairDto>("Stair", C.StairDto,
            c => new(c.IsDown, c.TargetDepth), (d, _) => new Stair(d.IsDown, d.TargetDepth));

        Register<StatusImmunityComponent, StatusImmunityComponentDto>("StatusImmunityComponent", C.StatusImmunityComponentDto,
            c => new(c.Immunities.OrderBy(s => s, StringComparer.Ordinal).ToArray()),
            (d, _) => new StatusImmunityComponent(d.Immunities));

        Register<TrapPayloadComponent, TrapPayloadComponentDto>("TrapPayloadComponent", C.TrapPayloadComponentDto,
            c => new(c.Actions.Select(ToTrapActionDto).ToList()),
            (d, _) => new TrapPayloadComponent { Actions = d.Actions.Select(FromTrapActionDto).ToList() });

        Register<WeaponAcidCoatingComponent, WeaponAcidCoatingComponentDto>("WeaponAcidCoatingComponent", C.WeaponAcidCoatingComponentDto,
            c => new(c.HitsRemaining, c.EffectDuration),
            (d, _) => new WeaponAcidCoatingComponent { HitsRemaining = d.HitsRemaining, EffectDuration = d.EffectDuration });
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static void RegisterEmpty<TComponent>(string name, Func<TComponent> ctor)
        where TComponent : class, IComponent =>
        Register<TComponent, EmptyDto>(name, MidRunJsonContext.Default.EmptyDto, _ => new EmptyDto(), (_, _) => ctor());

    private static void RegisterTurns<TComponent>(string name, Func<TComponent> ctor)
        where TComponent : class, IStatusEffect =>
        Register<TComponent, StatusTurnsDto>(name, MidRunJsonContext.Default.StatusTurnsDto,
            c => new StatusTurnsDto(c.RemainingTurns),
            (d, _) => { var e = ctor(); e.RemainingTurns = d.RemainingTurns; return e; });

    private static TrapActionDto ToTrapActionDto(TrapAction a) => new(a.Kind, a.Amount, a.Duration, a.Radius, a.Target);
    private static TrapAction FromTrapActionDto(TrapActionDto d) =>
        new() { Kind = d.Kind ?? "", Amount = d.Amount, Duration = d.Duration, Radius = d.Radius, Target = d.Target ?? "" };
}
