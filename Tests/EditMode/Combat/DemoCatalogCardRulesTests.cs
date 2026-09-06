using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 데모 카탈로그(<see cref="DemoCardCatalog"/>)로 카드 규칙을 굴리는 테스트. 옛 <c>ApprovedCardCatalogFactoryTests</c>에서
    /// 팩토리 구성(Draft·SourceId·effectRef 목록·엔트리 모양)을 재던 7건은 P3-b에서 팩토리와 함께 은퇴했고, 카드 규칙 테스트만 남겼다.
    /// 출하 CSV 배선은 <see cref="ShippingCardCsvEffectTests"/>가 따로 본다.
    /// </summary>
    public sealed class DemoCatalogCardRulesTests
    {
        [Test]
        public void CardDeckSurfaceAndGameplayTypeAreSeparate()
        {
            var catalog = DemoCardCatalog.Create(CombatConfig.Default);
            var entries = catalog.Entries.ToDictionary(entry => entry.Id);

            Assert.That(entries[CardIds.Momentum].DeckType, Is.EqualTo(CardCategory.Movement));
            Assert.That(entries[CardIds.Momentum].GameplayType, Is.EqualTo(CardGameplayType.Buff));
            Assert.That(entries[CardIds.Flashbang].DeckType, Is.EqualTo(CardCategory.Action));
            Assert.That(entries[CardIds.Flashbang].GameplayType, Is.EqualTo(CardGameplayType.Field));
            Assert.That(entries[CardIds.Redraw].GameplayType, Is.EqualTo(CardGameplayType.Utility));
        }

        [Test]
        public void CardCorePresentationRefsUseStringIdsWithFallbacks()
        {
            var catalog = DemoCardCatalog.Create(CombatConfig.Default);

            Assert.That(catalog.Entries.Select(entry => entry.PresentationRef.PresentationId), Is.All.Not.Empty);
            Assert.That(catalog.Entries.Select(entry => entry.PresentationRef.FrameId), Is.All.Not.Empty);
            var placeholder = CardPresentationRef.Placeholder();
            Assert.That(placeholder.FrameId, Is.EqualTo(CardPresentationRef.PlaceholderFrameId));
            Assert.That(placeholder.UsesPlaceholderFrame, Is.True);
            Assert.That(typeof(CardPresentationRef).Assembly.GetReferencedAssemblies().Any(name => name.Name == "UnityEngine"), Is.False);
        }


        [Test]
        public void ApprovedCatalogSpecialCardsResolveThroughRuntimeEffects()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 3, movementHandSize: 1, actionHandSize: 3);
            var linkedAttackCatalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".linked-test",
                "Linked attack test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move2Hex, "Move", CardCategory.Movement, CardEffectType.Move, 1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.MoveLinkedStrike, "Move Linked Attack", CardCategory.Action, CardEffectType.Attack, 1, 3, 2, "living_monster_in_range", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.Flashbang, "Flashbang", CardCategory.Action, CardEffectType.FieldObject, 1, 5, 0, "walkable_map_cell", areaRadius: 2, fieldObjectKind: CardFieldObjectKind.MassImmobilize, durationTurns: 2, status: CardCatalogStatus.Approved)
                });
            var state = CreateStateWithCatalog(linkedAttackCatalog, config, enemyCoord: new HexCoord(2, 0));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerAttack(new HexCoord(2, 0), CardIds.MoveLinkedStrike), Is.True);
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(8), "Moved 1 hex ??N(2) damage." );

            // Ranged field objects cannot be placed on the player's own tile (1,0); drop the flashbang on
            // the vacated (0,0) instead.
            Assert.That(state.TryPlayerFieldObject(new HexCoord(0, 0), CardIds.Flashbang), Is.True);
            Assert.That(state.PendingFieldObjects.Objects, Is.Empty);
            Assert.That(state.FieldObjects.Objects.Single().Kind, Is.EqualTo(FieldObjectKind.MassImmobilize));

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction(drawPlayerTurnHands: false);

            Assert.That(state.FieldObjects.Objects.Single().Kind, Is.EqualTo(FieldObjectKind.MassImmobilize));
        }

        [Test]
        public void ApprovedCatalogSpecificActionCardIdsSelectMatchingVariant()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 15);
            var state = CreateStateWithCatalog(DemoCardCatalog.Create(config), config, enemyCoord: new HexCoord(1, 0));

            Assert.That(state.TryPlayerMove(new HexCoord(0, 1)), Is.True);
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerScout(new HexCoord(1, 0), CardIds.Minefinder), Is.True);
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(8), "Minefinder should deal N(2) damage to the revealed monster.");

            Assert.That(state.TryPlayerDefend(CardIds.DoubleEdgedShield), Is.True);
            Assert.That(state.Player.Block, Is.EqualTo(0), "Double Edged Shield should not add generic block.");
        }

        /// <summary>
        /// D02/D03 publish their effects under a shared behaviour ref rather than the card id (unlike
        /// D00/D01, which use the id directly). Presentation matches on the ref first, so a cue authored for
        /// one of these cards is only reachable through the SourceCardId tier — and that tier is dead unless
        /// the rules layer names the card. It did not, which left CVD02/CVD03 permanently unmatched.
        /// </summary>
        [Test]
        public void BehaviourRefDefendCardsNameTheirSourceCardForPresentation()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 15);
            var state = CreateStateWithCatalog(DemoCardCatalog.Create(config), config, enemyCoord: new HexCoord(1, 0));

            AdvanceToPlayerAction(state);

            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            Assert.That(state.TryPlayerDefend(CardIds.ShelterTaunt), Is.True);
            var immunity = events.Single(candidate =>
                candidate.Kind == EffectKind.Block
                && candidate.SourceRef == CardEffectRefs.DefendDamageImmunity);
            Assert.That(
                immunity.SourceCardId,
                Is.EqualTo(CardIds.ShelterTaunt),
                "D02's damage-immunity Block must name D02 so a card-scoped cue can select it.");

            events.Clear();
            Assert.That(state.TryPlayerDefend(CardIds.DoubleEdgedShield), Is.True);
            var reflect = events.Single(candidate =>
                candidate.Kind == EffectKind.StatusEffectApplied
                && candidate.StatusKind == StatusEffectKind.Reflect);
            Assert.That(
                reflect.SourceRef,
                Is.EqualTo(CardIds.DoubleEdgedShield),
                "D03 publishes Reflect under its own card id, never as a Block.");
            Assert.That(
                reflect.SourceCardId,
                Is.EqualTo(CardIds.DoubleEdgedShield),
                "D03's Reflect must name D03 so a card-scoped cue can select it.");
        }

        /// <summary>
        /// D05 부적 방패 nullifies the incoming damage and grants no block, so it reported nothing at all to
        /// presentation and the play was invisible — no VFX, no sound, no text. D02 does the same thing and
        /// announces itself with a zero-amount Block that reads 피해 면역; D05 now takes that same path.
        /// </summary>
        [Test]
        public void DamageImmunityDefendCardsAnnounceThemselvesDespiteGrantingNoBlock()
        {
            // D05 is authored in cards.csv only, not in the factory catalog, so the card is built here.
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 4);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".damage-immunity-test",
                "Damage immunity announce test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move2Hex, "Move", CardCategory.Movement, CardEffectType.Move, 1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.TalismanShield, "Talisman Shield", CardCategory.Action, CardEffectType.Defend, 1, 0, 0, "self", status: CardCatalogStatus.Approved, gameplayType: CardGameplayType.Defend, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self)
                });
            var state = CreateStateWithCatalog(catalog, config, enemyCoord: new HexCoord(1, 0));

            AdvanceToPlayerAction(state);

            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            Assert.That(state.TryPlayerDefend(CardIds.TalismanShield), Is.True);
            Assert.That(state.Player.Block, Is.EqualTo(0), "D05 nullifies damage rather than granting block.");

            var announce = events.Single(candidate => candidate.Kind == EffectKind.Block);
            Assert.That(announce.SourceRef, Is.EqualTo(CardEffectRefs.DefendDamageImmunity));
            Assert.That(announce.SourceCardId, Is.EqualTo(CardIds.TalismanShield));
            Assert.That(
                CombatEffectSourceClassifier.IsDamageImmunitySource(announce.SourceRef),
                Is.True,
                "The announce must classify as damage immunity, or presentation hides its zero-amount Block.");
        }

        [Test]
        public void ScoutCardsRespectSelectedCardRange()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 3, movementHandSize: 1, actionHandSize: 1);
            var shortRangeScoutCatalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".scout-range-test",
                "Scout range test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move2Hex, "Move", CardCategory.Movement, CardEffectType.Move, 1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.Minefinder, "Minefinder", CardCategory.Action, CardEffectType.Scout, 1, 1, 2, "walkable_map_cell", areaRadius: 2, status: CardCatalogStatus.Approved)
                });
            var state = CreateStateWithCatalog(shortRangeScoutCatalog, config, enemyCoord: new HexCoord(3, 0));

            Assert.That(state.TryPlayerMove(new HexCoord(0, 1)), Is.True);
            AdvanceToPlayerAction(state);
            Assert.That(state.ValidateScoutTarget(new HexCoord(0, 0), CardIds.Minefinder).IsValid, Is.True);
            Assert.That(state.ValidateScoutTarget(new HexCoord(3, 0), CardIds.Minefinder).IsValid, Is.False);
            Assert.That(state.TryPlayerScout(new HexCoord(3, 0), CardIds.Minefinder), Is.False);
            Assert.That(state.LastFailureReason, Is.EqualTo("Scout target is out of range."));
        }

        [Test]
        public void ShelterTauntZeroesCurrentMonsterDamageThenDoublesNextMonsterAction()
        {
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 0, 1, 6, actionBudget: 3, movementHandSize: 1, actionHandSize: 1);
            var state = CreateStateWithCatalog(CreateSingleDefendCatalog(CardIds.ShelterTaunt, "Shelter Taunt", 0, buffDebuff: "Strength:100", durationTurns: 2), config, enemyCoord: new HexCoord(1, 0));
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerDefend(CardIds.ShelterTaunt), Is.True);
            var hpBeforeZeroTurn = state.Player.Hp;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBeforeZeroTurn), "Protection Zone should prevent the next monster hit from damaging the player.");
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerAction(state);

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBeforeZeroTurn - 12), "Protection Zone should double the following monster hit after the zero-damage hit is consumed.");
        }

        [Test]
        public void DoubleEdgedShieldMitigatesHalfAndReflectsRemainder()
        {
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 0, 1, 6, actionBudget: 3, movementHandSize: 1, actionHandSize: 1);
            var state = CreateStateWithCatalog(CreateSingleDefendCatalog(CardIds.DoubleEdgedShield, "Double Edged Shield", 50, buffDebuff: "Reflect:50", durationTurns: 1), config, enemyCoord: new HexCoord(1, 0));
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerDefend(CardIds.DoubleEdgedShield), Is.True);
            var hpBefore = state.Player.Hp;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            // 반사(Reflect) 키워드 계약(game_keywords.csv): 받는 피해를 50% 경감하고 그만큼 시전자에게 반사.
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - 3), "Double Edged Shield mitigates half of the incoming damage.");
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(7), "Double Edged Shield should reflect half of the incoming damage to the monster.");
        }

        /// <summary>
        /// WS-I I-05(DEC-2026-08-19-08): 반사 계산이 ÷2 하드코딩이라 연마(D03+ Reflect:75)가 무효였다 —
        /// 이제 저작 퍼센트가 그대로 소비된다(6의 75% = 4 반사·2 피격, 내림).
        /// </summary>
        [Test]
        public void ReflectPercentIsConsumedByTheDamageMath()
        {
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 0, 1, 6, actionBudget: 3, movementHandSize: 1, actionHandSize: 1);
            var state = CreateStateWithCatalog(CreateSingleDefendCatalog(CardIds.DoubleEdgedShield, "Double Edged Shield", 50, buffDebuff: "Reflect:75", durationTurns: 1), config, enemyCoord: new HexCoord(1, 0));
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerDefend(CardIds.DoubleEdgedShield), Is.True);
            var hpBefore = state.Player.Hp;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - 2), "75% 반사는 피해 6 중 4를 경감한다(내림).");
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(6), "경감분 4가 그대로 공격자에게 되돌아간다.");
        }

        /// <summary>
        /// WS-I I-14(사용자 판정: 다음 턴·전체): D02의 강화는 이번 턴 몬스터 행동에 새면 안 되고,
        /// 다음 턴 시작에 <b>모든</b> 몬스터에게 붙는다. I-19: 무적은 아이콘 투영 상태로 표시되고
        /// 불리언과 같은 경계(다음 턴 시작)에서 사라진다.
        /// </summary>
        [Test]
        public void RefinedShelterTauntKeepsTheInvincibilityAndDropsTheProvoke()
        {
            // 보호구역 안에서 도발+ (효과 연마 2차): 「다음 턴 모든 적 강화」가 빠진다. 무적은 그대로.
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 0, 1, 6, actionBudget: 3, movementHandSize: 1, actionHandSize: 1);
            var state = CreateStateWithCatalog(CreateSingleDefendCatalog(CardIds.ShelterTaunt, "Shelter Taunt", 0, buffDebuff: "Strength:100", durationTurns: 1), config, enemyCoord: new HexCoord(1, 0));
            Assert.That(state.TryRefineCard(CardIds.ShelterTaunt, out var reason), Is.True, reason);
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerAction(state);
            var hpBefore = state.Player.Hp;
            Assert.That(state.TryPlayerDefend(CardIds.ShelterTaunt), Is.True, state.LastFailureReason);

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore), "무적은 연마 뒤에도 그대로다.");
            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Strength),
                Is.False, "연마된 도발은 다음 턴에 아무 적도 강화하지 않는다.");
        }

        [Test]
        public void ShelterTauntStrengthLandsNextTurnAndInvincibleIsProjected()
        {
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 0, 1, 6, actionBudget: 3, movementHandSize: 1, actionHandSize: 1);
            var state = CreateStateWithCatalog(CreateSingleDefendCatalog(CardIds.ShelterTaunt, "Shelter Taunt", 0, buffDebuff: "Strength:100", durationTurns: 1), config, enemyCoord: new HexCoord(1, 0));
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerDefend(CardIds.ShelterTaunt), Is.True);

            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Strength),
                Is.False, "강화는 예약만 되고 이번 턴 몬스터 행동에 새면 안 된다(문안: 다음 턴).");
            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Invincible && effect.TargetUnitId == "player"),
                Is.True, "무적은 아이콘 투영 상태로 표시된다(I-19).");

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction(); // 몬스터 행동(무적으로 0) + 다음 턴 시작

            Assert.That(
                state.Monsters.All(monster => state.ActiveEffects.Any(effect =>
                    effect.Kind == StatusEffectKind.Strength && effect.TargetUnitId == monster.Id)),
                Is.True, "다음 턴 시작에 모든 몬스터가 강화를 받는다.");
            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Invincible),
                Is.False, "무적 투영은 불리언 리셋과 같은 경계에서 사라진다.");
        }

        [Test]
        public void ReflectMagnitudeAndDurationComeFromCardData()
        {
            // The point of P0.5: authoring different numbers must produce a different effect. Before the
            // promotion these came from a hardcoded importer fallback + a hardcoded 1-turn literal, so this
            // card would have granted Reflect 50 for 1 turn no matter what the data said.
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 0, 1, 6, actionBudget: 3, movementHandSize: 1, actionHandSize: 1);
            var state = CreateStateWithCatalog(
                CreateSingleDefendCatalog(
                    CardIds.DoubleEdgedShield,
                    "Double Edged Shield",
                    50,
                    buffDebuff: "Reflect:80",
                    durationTurns: 3),
                config,
                enemyCoord: new HexCoord(1, 0));
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerDefend(CardIds.DoubleEdgedShield), Is.True);

            var reflect = state.ActiveEffects.Single(effect => effect.Kind == StatusEffectKind.Reflect);
            Assert.That(reflect.Amount, Is.EqualTo(80), "buff_debuff must drive the magnitude.");
            Assert.That(reflect.RemainingTurns, Is.EqualTo(3), "duration must drive the turn count.");
        }

        [Test]
        public void MomentumAgilityGrantComesFromCardDataAndSurvivesSuspend()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 3, movementHandSize: 1, actionHandSize: 1);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".momentum-test",
                "Momentum test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.Momentum, "Momentum", CardCategory.Movement, CardEffectType.Move,
                        1, 0, 0, "self",
                        status: CardCatalogStatus.Approved, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self,
                        durationTurns: 2, buffDebuff: "Agility:4"),
                    // A catalog must define at least one action-deck card; this one is never played.
                    new CardCatalogEntry(
                        CardIds.OldArmor, "Old Suit", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 5, "self", status: CardCatalogStatus.Approved),
                });
            var state = CreateStateWithCatalog(catalog, config);

            Assert.That(state.TryPlayerMove(state.PlayerCoord, CardIds.Momentum), Is.True);

            // The grant is deferred to the next turn start, so the carried duration has to survive a
            // suspend/resume in between — otherwise leaving mid-combat would quietly reset it.
            var resumed = CombatState.CreateDefaultDemo();
            resumed.RestoreFromSuspend(state.CreateSuspendSnapshot());
            AdvanceToNextOverallTurn(resumed);

            var agility = resumed.ActiveEffects.Single(effect => effect.Kind == StatusEffectKind.Agility);
            Assert.That(agility.Amount, Is.EqualTo(4), "buff_debuff must drive the carried Agility magnitude.");
            Assert.That(agility.RemainingTurns, Is.EqualTo(2), "duration must ride along through the suspend envelope.");
        }

        [Test]
        public void DoubleEdgedShieldLethalReflectClearsMonsterOccupancyForNextMovement()
        {
            var config = new CombatConfig(20, 2, 2, 1, 4, 4, 0, 1, 6, actionBudget: 3, movementHandSize: 1, actionHandSize: 1);
            var monsterCoord = new HexCoord(1, 0);
            var state = CreateStateWithCatalog(CreateSingleDefendCatalog(CardIds.DoubleEdgedShield, "Double Edged Shield", 50, buffDebuff: "Reflect:50", durationTurns: 1), config, enemyCoord: monsterCoord);
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerDefend(CardIds.DoubleEdgedShield), Is.True);

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            var actionRecord = state.LastMonsterActionRecords.Single();
            Assert.That(actionRecord.MonsterId, Is.EqualTo("normal-enemy"));
            Assert.That(actionRecord.AttackedPlayer, Is.True);
            Assert.That(actionRecord.AffectedPlayer, Is.True);
            Assert.That(actionRecord.DamageToPlayer, Is.EqualTo(3), "Reflect (반사) halves the player's incoming damage; the record carries the mitigated amount.");
            Assert.That(actionRecord.ShouldPresent, Is.True, "Lethal reflected monster hits should still present the monster action record.");
            Assert.That(state.Monsters.Single().IsDead, Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.TryPlayerMove(monsterCoord), Is.True, "The tile vacated by a reflected-dead monster should be walkable next turn.");
        }

        [Test]
        public void TreasurefinderHealsFromTreasureObjectsInsideRevealRadius()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 3, movementHandSize: 1, actionHandSize: 1);
            var treasureCatalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".treasure-test",
                "Treasure finder test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move2Hex, "Move", CardCategory.Movement, CardEffectType.Move, 1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.Treasurefinder, "Treasurefinder", CardCategory.Action, CardEffectType.Scout, 1, 3, 2, "walkable_map_cell", areaRadius: 2, status: CardCatalogStatus.Approved)
                });
            var map = new HexMapData(
                CombatState.CreateDemoMap(3).AllCells,
                objectRefs: new[]
                {
                    new HexMapObjectData("treasure-1", "TreasureChest", "test-treasure", new HexCoord(1, 0)),
                    new HexMapObjectData("treasure-2", "TreasureChest", "test-treasure", new HexCoord(2, 0)),
                    new HexMapObjectData("treasure-far", "TreasureChest", "test-treasure", new HexCoord(3, 0))
                });
            var state = new CombatState(map, new HexCoord(0, 0), new HexCoord(3, 0), config, cardCatalog: treasureCatalog);
            state.Player.ApplyDamage(6);
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerScout(new HexCoord(0, 0), CardIds.Treasurefinder), Is.True);

            Assert.That(state.Player.Hp, Is.EqualTo(18), "Treasurefinder should heal 2 HP per treasure object inside the reveal radius.");
        }

        [Test]
        public void TreasurefinderHealingStopsAtMaxHp()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            var treasureCatalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".treasure-overheal-test",
                "Treasurefinder capped heal test",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move2Hex, "Move", CardCategory.Movement, CardEffectType.Move, 1, 2, 2, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.Treasurefinder, "Treasurefinder", CardCategory.Action, CardEffectType.Scout, 1, 3, 2, "walkable_map_cell", areaRadius: 2, status: CardCatalogStatus.Approved)
                });
            var map = new HexMapData(
                CombatState.CreateDemoMap(3).AllCells,
                objectRefs: new[]
                {
                    new HexMapObjectData("treasure-1", "TreasureChest", "test-treasure", new HexCoord(1, 0)),
                    new HexMapObjectData("treasure-2", "TreasureChest", "test-treasure", new HexCoord(2, 0))
                });
            var state = new CombatState(map, new HexCoord(0, 0), new HexCoord(3, 0), config, cardCatalog: treasureCatalog);
            var effects = new List<EffectResultEvent>();
            state.EffectResolved += effects.Add;
            state.Player.ApplyDamage(2);
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerScout(new HexCoord(0, 0), CardIds.Treasurefinder), Is.True);

            Assert.That(state.Player.Hp, Is.EqualTo(state.Player.MaxHp), "Treasurefinder heal must not exceed MaxHp.");
            Assert.That(effects.Single(effect => effect.Kind == EffectKind.Heal).AppliedAmount, Is.EqualTo(2));
        }

        [Test]
        public void DefaultApprovedRuntimeDrawsOneMoveAndFiveActionCardsTogether()
        {
            var state = CreateStateWithCatalog(DemoCardCatalog.Create(CombatConfig.Default), CombatConfig.Default);

            Assert.That(state.MaxKi, Is.EqualTo(4));
            Assert.That(state.CurrentKi, Is.EqualTo(4));
            Assert.That(state.MovementDeck.HandCount, Is.EqualTo(1));
            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(5));
        }

        [Test]
        public void OneStrikeEnoughSpendsFixedCostAfterMovementAndDealsFlatDamage()
        {
            var config = new CombatConfig(20, 12, 2, 2, 0, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".one-strike-test",
                "One strike enough test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move, 1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.OneStrikeEnough, "One Strike Enough", CardCategory.Action, CardEffectType.Attack, 3, 2, 8, "living_monster_in_range_2", status: CardCatalogStatus.Approved)
                });
            var state = CreateStateWithCatalog(catalog, config, enemyCoord: new HexCoord(1, 0));

            Assert.That(state.TryPlayerMove(state.PlayerCoord, CardIds.Move1Hex), Is.True);
            Assert.That(state.CurrentKi, Is.EqualTo(3));
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), CardIds.OneStrikeEnough), Is.True);

            Assert.That(state.CurrentKi, Is.EqualTo(0));
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(4), "One Strike Enough deals flat damage (8) for its fixed Ki cost (3).");
        }

        [Test]
        public void FinalBlowCanBeUsedAtZeroKiAndDealsZero()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 1, movementHandSize: 1, actionHandSize: 1);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".final-blow-zero-test",
                "Final blow zero Ki test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move, 1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.FinalBlow, "Final Blow", CardCategory.Action, CardEffectType.Attack, 1, 1, 3, "living_monster_in_range", status: CardCatalogStatus.Approved, costMode: CardCostMode.SpendAll, scalingMode: CardScalingMode.SpentKi)
                });
            var state = CreateStateWithCatalog(catalog, config, enemyCoord: new HexCoord(1, 0));

            Assert.That(state.TryPlayerMove(state.PlayerCoord, CardIds.Move1Hex), Is.True);
            Assert.That(state.CurrentKi, Is.EqualTo(0));
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), CardIds.FinalBlow), Is.True);
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(10));
        }

        [Test]
        public void FinalBlowHandSnapshotShowsRemainingKiAsSpendAllCost()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".final-blow-cost-text-test",
                "Final blow cost text test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move, 1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.FinalBlow, "Final Blow", CardCategory.Action, CardEffectType.Attack, 1, 1, 3, "living_monster_in_range", status: CardCatalogStatus.Approved, costMode: CardCostMode.SpendAll, scalingMode: CardScalingMode.SpentKi)
                });
            var state = CreateStateWithCatalog(catalog, config, enemyCoord: new HexCoord(1, 0));
            Assert.That(state.EndAction(), Is.True);

            var finalBlow = state.GetHandCards().Single(card => card.Id == CardIds.FinalBlow);

            Assert.That(finalBlow.Cost, Is.EqualTo(state.CurrentKi));
            Assert.That(finalBlow.Cost, Is.EqualTo(4));
        }

        [Test]
        public void FinishingTouchScalesDamageByAttackCardsInHand()
        {
            var config = new CombatConfig(20, 12, 2, 1, 0, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 2);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".finishing-touch-test",
                "Finishing touch test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move, 1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.FinishingTouch, "Finishing Touch", CardCategory.Action, CardEffectType.Attack, 1, 1, 3, "living_monster_in_range", status: CardCatalogStatus.Approved, scalingMode: CardScalingMode.AttackCardsInHand),
                    new CardCatalogEntry(CardIds.DoubleHit, "Double Hit", CardCategory.Action, CardEffectType.Attack, 1, 1, 2, "living_monster_in_range", status: CardCatalogStatus.Approved, hitCount: 2)
                });
            var state = CreateStateWithCatalog(catalog, config, enemyCoord: new HexCoord(1, 0));

            Assert.That(state.TryPlayerMove(state.PlayerCoord, CardIds.Move1Hex), Is.True);
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), CardIds.FinishingTouch), Is.True);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(6), "12 - (2 attack cards in hand * 3) = 6.");
        }

        [Test]
        public void DoubleHitAppliesTwoDamageEvents()
        {
            var config = new CombatConfig(20, 10, 2, 1, 0, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".double-hit-test",
                "Double hit test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move, 1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.DoubleHit, "Double Hit", CardCategory.Action, CardEffectType.Attack, 1, 1, 2, "living_monster_in_range", status: CardCatalogStatus.Approved, hitCount: 2)
                });
            var state = CreateStateWithCatalog(catalog, config, enemyCoord: new HexCoord(1, 0));

            Assert.That(state.TryPlayerMove(state.PlayerCoord, CardIds.Move1Hex), Is.True);
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), CardIds.DoubleHit), Is.True);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(6), "Double Hit should apply N(2) damage twice.");
        }

        [Test]
        public void RedrawDiscardsMovementAndActionHandsThenDrawsSameCounts()
        {
            var config = new CombatConfig(20, 10, 2, 1, 0, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 2, actionHandSize: 2);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".redraw-test",
                "Redraw test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move, 1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.Move2Hex, "Move 2", CardCategory.Movement, CardEffectType.Move, 1, 2, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.Move3Hex, "Move 3", CardCategory.Movement, CardEffectType.Move, 1, 3, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.Redraw, "Redraw", CardCategory.Action, CardEffectType.Utility, 1, 0, 0, "current_action_hand_except_self", status: CardCatalogStatus.Approved, gameplayType: CardGameplayType.Utility, playMode: CardPlayMode.Self),
                    new CardCatalogEntry(CardIds.DoubleHit, "Double Hit", CardCategory.Action, CardEffectType.Attack, 1, 1, 2, "living_monster_in_range", status: CardCatalogStatus.Approved, hitCount: 2),
                    new CardCatalogEntry(CardIds.OldArmor, "Defend", CardCategory.Action, CardEffectType.Defend, 1, 0, 3, "self", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.Minefinder, "Scout", CardCategory.Action, CardEffectType.Scout, 1, 2, 0, "unrevealed_hex", status: CardCatalogStatus.Approved)
                });
            var state = CreateStateWithCatalog(catalog, config, enemyCoord: new HexCoord(1, 0));

            Assert.That(state.TryPlayerMove(state.PlayerCoord, CardIds.Move1Hex), Is.True);
            Assert.That(state.TryPlayerUtility(CardIds.Redraw), Is.True);

            Assert.That(state.MovementDeck.DiscardPile.Select(card => card.Id), Does.Contain(CardIds.Move2Hex));
            Assert.That(state.MovementDeck.Hand.Select(card => card.Id), Is.EqualTo(new[] { CardIds.Move3Hex }));
            Assert.That(state.ActionDeck.DiscardPile.Select(card => card.Id), Is.EquivalentTo(new[] { CardIds.Redraw, CardIds.DoubleHit }));
            Assert.That(state.ActionDeck.Hand.Select(card => card.Id), Is.EqualTo(new[] { CardIds.OldArmor, CardIds.Minefinder }));
        }


        [Test]
        public void MultipleMovementCardsCanBePlayedBeforeEnteringActionPhaseWithoutActionTopUp()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 8, movementHandSize: 3, actionHandSize: 5);
            var state = CreateStateWithCatalog(DemoCardCatalog.Create(config), config);
            var initialActionHand = state.ActionDeck.HandCount;

            Assert.That(state.TryPlayerMove(state.PlayerCoord, CardIds.Move1Hex), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.TryPlayerMove(new HexCoord(1, 0), CardIds.Move2Hex), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.CurrentKi, Is.EqualTo(5));

            AdvanceToPlayerAction(state);
            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(initialActionHand), "Action phase must not top up cards after simultaneous turn-start draw.");
        }

        [Test]
        public void HolyLightChoiceHealOptionIsRuntimePlayable()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 12);
            var state = CreateStateWithCatalog(DemoCardCatalog.Create(config), config);
            state.Player.ApplyDamage(5);
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerChoiceOption(CardIds.HolyLight, "heal"), Is.True);

            Assert.That(state.Player.Hp, Is.EqualTo(19));
            Assert.That(state.GetCombatCards().Single(card => card.Id == CardIds.HolyLight).IsDiscarded, Is.True);
            Assert.That(state.CurrentKi, Is.EqualTo(3));
        }

        [Test]
        public void HolyLightChoiceAttackOptionIsRuntimePlayable()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 12);
            var state = CreateStateWithCatalog(DemoCardCatalog.Create(config), config, enemyCoord: new HexCoord(1, 0));
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerChoiceOption(CardIds.HolyLight, "attack", new HexCoord(1, 0)), Is.True);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(6));
            Assert.That(state.GetCombatCards().Single(card => card.Id == CardIds.HolyLight).IsDiscarded, Is.True);
            Assert.That(state.CurrentKi, Is.EqualTo(3));
        }

        [Test]
        public void AdditionalCostMetadataCanDriveSacrificeAttackDamageCard()
        {
            var config = new CombatConfig(20, 20, 2, 1, 4, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 14);
            var state = CreateStateWithCatalog(DemoCardCatalog.Create(config), config, enemyCoord: new HexCoord(1, 0));
            AdvanceToPlayerAction(state);
            var sacrifices = state.ActionDeck.Hand
                .Where(card => card.Id != CardIds.Sacrifice)
                .Take(2)
                .ToList();

            Assert.That(state.TryPlayerSacrificeAttack(new HexCoord(1, 0), CardIds.Sacrifice, sacrifices), Is.True);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(10));
            Assert.That(state.GetCombatCards().Single(card => card.Id == CardIds.Sacrifice).IsDiscarded, Is.True);
            Assert.That(sacrifices.All(card => state.ActionDeck.RemovedPile.Any(removed => removed.InstanceId == card.InstanceId)), Is.True);
            Assert.That(sacrifices.All(card => state.ActionDeck.DiscardPile.All(discarded => discarded.InstanceId != card.InstanceId)), Is.True);
        }

        // Regression: an auto-targeted attack (no explicit cardId) whose best card is the sacrifice attack was
        // rejected with "This method only supports the sacrifice attack card." TryPlayerAttack picks by
        // range/area ordering, but it forwarded the caller's (empty) cardId, so TryPlayerSacrificeAttack
        // re-resolved through FindActionCard(Attack, "") — the *first* Attack card in hand, which ignores that
        // ordering. Whenever a different attack card sat earlier in hand the two disagreed and the attack died
        // on the callee's guard. This surfaced only as an intermittent TurnSequenceTests failure, because the
        // unseeded combat RNG decides whether a run reaches that situation; pinned deterministically here.
        [Test]
        public void AutoTargetedSacrificeAttackResolvesTheCardItSelected()
        {
            const string plainAttackId = "plain-attack-test";
            // 제물(소멸 비용)은 A10_Sacrifice 클래스가 선언하므로 픽스처는 출하 id를 써야 그 규칙을 받는다(DEC-2026-09-06-01).
            const string sacrificeAttackId = CardIds.Sacrifice;
            var target = new HexCoord(2, 0);
            // enemyChaseRange 0 keeps the monster on its authored tile, so the distances the card ordering
            // depends on survive ResolveMonsterMovement.
            var config = new CombatConfig(20, 20, 2, 1, 4, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 14);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".auto-targeted-sacrifice-test",
                "Auto-targeted sacrifice attack test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move2Hex, "Move", CardCategory.Movement, CardEffectType.Move, 1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    // Both cards reach a target 2 tiles away (range + areaRadius == 2), but FindAttackCardForTarget
                    // orders by areaRadius ascending, so the sacrifice card below is the one it selects. This card
                    // exists only to sit earlier in hand, which is what used to be resolved instead.
                    new CardCatalogEntry(plainAttackId, "Plain Attack", CardCategory.Action, CardEffectType.Attack, 1, 1, 4, "living_monster_in_range", areaRadius: 1, status: CardCatalogStatus.Approved, gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy),
                    new CardCatalogEntry(sacrificeAttackId, "Sacrifice Attack", CardCategory.Action, CardEffectType.Attack, 1, 2, 4, "living_monster_and_hand_card", status: CardCatalogStatus.Approved, gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy)
                });
            var state = CreateStateWithCatalog(catalog, config, enemyCoord: target);
            AdvanceToPlayerAction(state);

            var firstAttackInHand = state.ActionDeck.Hand.First(card => card.EffectType == CardEffectType.Attack);
            Assert.That(
                firstAttackInHand.Id,
                Is.EqualTo(plainAttackId),
                "fixture precondition: a non-sacrifice attack card must sit earlier in hand than the sacrifice "
                + "card, or FindActionCard(Attack, \"\") would resolve the sacrifice card anyway and this "
                + "regression would not be exercised. Reorder the catalog if the deck shuffle changes.");

            Assert.That(
                state.TryPlayerAttack(target),
                Is.True,
                $"an auto-targeted sacrifice attack should resolve; failed with: {state.LastFailureReason}");

            // The sacrifice card — not the plain one sitting earlier in hand — must be the card that resolved,
            // which its exile cost makes observable.
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(16), "one sacrificed card at amount 4");
            Assert.That(state.GetCombatCards().Single(card => card.Id == sacrificeAttackId).IsDiscarded, Is.True);
            Assert.That(state.ActionDeck.RemovedPile.Any(removed => removed.Id == plainAttackId), Is.True, "the sacrificed card should be exiled");
        }

        [Test]
        public void SacrificeAttackNormalTargetPathPaysAdditionalCost()
        {
            var config = new CombatConfig(20, 20, 2, 1, 4, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 3);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".sacrifice-normal-path-test",
                "Sacrifice normal path test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move, 1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.Sacrifice, "Sacrifice Attack", CardCategory.Action, CardEffectType.Attack, 1, 1, 5, "living_monster_and_hand_card", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.DoubleHit, "Double Hit", CardCategory.Action, CardEffectType.Attack, 1, 1, 2, "living_monster_in_range", status: CardCatalogStatus.Approved, hitCount: 2),
                    new CardCatalogEntry(CardIds.OldArmor, "Old Suit", CardCategory.Action, CardEffectType.Defend, 1, 0, 5, "self", status: CardCatalogStatus.Approved)
                });
            var state = CreateStateWithCatalog(catalog, config, enemyCoord: new HexCoord(1, 0));
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), CardIds.Sacrifice), Is.True);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(10));
            Assert.That(state.GetCombatCards().Single(card => card.Id == CardIds.Sacrifice).IsDiscarded, Is.True);
            Assert.That(state.ActionDeck.DiscardPile.Select(card => card.Id), Has.Member(CardIds.Sacrifice));
            Assert.That(state.ActionDeck.RemovedPile.Select(card => card.Id), Is.SupersetOf(new[]
            {
                CardIds.DoubleHit,
                CardIds.OldArmor
            }));
        }

        [Test]
        public void MultiplyingStrikeInjectsTemporaryCopyIntoDrawPile()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            var state = CreateStateWithCatalog(DemoCardCatalog.Create(config), config, enemyCoord: new HexCoord(1, 0));
            AdvanceToPlayerAction(state);
            Assert.That(state.DebugInjectCardIntoHand(CardIds.MultiplyingStrike), Is.True);

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), CardIds.MultiplyingStrike), Is.True);

            Assert.That(state.GetCombatCards().Single(card => card.Id == CardIds.MultiplyingStrike).IsDiscarded, Is.True);
            Assert.That(state.ActionDeck.DrawPile.Any(card => card.Id == CardIds.MultiplyingStrikeCopy && card.IsTemporary), Is.True);
        }

        [Test]
        public void MultiplyingStrikeRepeatsFixedDamageOncePerMultiplyingStrikeCard()
        {
            var config = new CombatConfig(20, 10, 2, 1, 0, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".multiplying-strike-test",
                "Multiplying strike test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move, 1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.MultiplyingStrike, "Multiplying Strike", CardCategory.Action, CardEffectType.Attack, 1, 1, 2, "living_monster_in_range", status: CardCatalogStatus.Approved)
                });
            var state = CreateStateWithCatalog(catalog, config, enemyCoord: new HexCoord(1, 0));

            Assert.That(state.TryPlayerMove(state.PlayerCoord, CardIds.Move1Hex), Is.True);
            AdvanceToPlayerAction(state);
            Assert.That(state.DebugInjectCardIntoHand(CardIds.MultiplyingStrike), Is.True);
            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), CardIds.MultiplyingStrike), Is.True);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(6),
                "2 multiplying-strike cards => 2 hits x 2 fixed damage = 4 (10 -> 6).");
        }

        [Test]
        public void PostActionMetadataCanDriveTargetShotMark()
        {
            var config = new CombatConfig(20, 20, 2, 1, 4, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 14);
            var state = CreateStateWithCatalog(DemoCardCatalog.Create(config), config, enemyCoord: new HexCoord(1, 0));
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), CardIds.TargetShot), Is.True);
            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), CardIds.DoubleHit), Is.True);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(12), "Target Shot deals 2, then ApplyMark doubles the first hit of a two-hit 2-damage attack.");
        }

        [Test]
        public void FlashbangFieldObjectImmobilizesEveryMonsterInFootprint()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 3, movementHandSize: 1, actionHandSize: 5);
            var catalog = new CardCatalogDefinition(
                DemoCardCatalog.SourceId + ".flashbang-aoe-test",
                "Flashbang AOE test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move2Hex, "Move", CardCategory.Movement, CardEffectType.Move, 1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(CardIds.Flashbang, "Flashbang", CardCategory.Action, CardEffectType.FieldObject, 1, 5, 0, "walkable_map_cell", areaRadius: 2, fieldObjectKind: CardFieldObjectKind.MassImmobilize, durationTurns: 2, status: CardCatalogStatus.Approved)
                });
            var state = new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("flash-a", new HexCoord(2, 0), 10),
                    new MonsterConfig("flash-b", new HexCoord(0, 2), 10)
                },
                config,
                cardCatalog: catalog);
            AdvanceToPlayerAction(state);

            // Footprint centered at (1,1), radius 2 covers both monsters at (2,0) and (0,2).
            Assert.That(state.TryPlayerFieldObject(new HexCoord(1, 1), CardIds.Flashbang), Is.True);
            Assert.That(state.PendingFieldObjects.Objects, Is.Empty);
            Assert.That(state.FieldObjects.Objects.Single().Kind, Is.EqualTo(FieldObjectKind.MassImmobilize));

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction(drawPlayerTurnHands: false);

            Assert.That(state.FieldObjects.Objects.Single().Kind, Is.EqualTo(FieldObjectKind.MassImmobilize));

            var monsterIds = state.Monsters.Select(monster => monster.Id).ToList();
            Assert.That(monsterIds, Has.Count.EqualTo(2));
            foreach (var monsterId in monsterIds)
            {
                Assert.That(
                    state.ActiveEffects.Any(effect => effect.TargetUnitId == monsterId && effect.Kind == StatusEffectKind.Immobilize),
                    Is.True,
                    $"섬광(F03) MassImmobilize must apply 속박(Immobilize) ActiveEffect to every monster in the footprint, including '{monsterId}'.");
            }
        }

        [Test]
        public void DoubleEdgedShieldReflectsFieldDamageToFieldSource()
        {
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 0, 1, 0, actionBudget: 4, movementHandSize: 1, actionHandSize: 15);
            var state = CreateStateWithCatalog(DemoCardCatalog.Create(config), config);
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerDefend(CardIds.DoubleEdgedShield), Is.True);
            state.FieldObjects.Add(new FieldObject(state.PlayerCoord, radius: 0, remainingTurns: 1, FieldObjectKind.FieldDamage, value: 5, sourceUnitId: "normal-enemy"));

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            // 반사(Reflect) 키워드 계약: 필드 피해 5도 50% 경감(2)하고 그만큼 필드 소스에게 반사한다.
            Assert.That(state.Player.Hp, Is.EqualTo(17));
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(8));
        }

        // The combat turn model interposes a monster-movement phase between the player's movement and action
        // phases: PlayerMovement -> (EndAction) -> MonsterMovement -> (ResolveMonsterMovement) -> PlayerAction.
        // Test monsters use EnemyChaseRange = 0, so nothing relocates during the monster-movement phase.
        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True, "EndAction should advance player movement into the monster-movement phase.");
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction), "ResolveMonsterMovement should advance into the player action phase.");
        }

        // PlayerMovement -> MonsterMovement -> PlayerAction -> MonsterAction -> next overall turn start,
        // which is where deferred grants (M05 momentum) actually land.
        private static void AdvanceToNextOverallTurn(CombatState state)
        {
            var turn = state.OverallTurnNumber;
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(state.OverallTurnNumber, Is.GreaterThan(turn), "A full turn cycle should have started the next overall turn.");
        }

        private static CombatState CreateStateWithCatalog(CardCatalogDefinition catalog, int actionHandSize = 14)
        {
            var config = TestCombatConfigs.Standard(actionBudget: 3, movementHandSize: 1, actionHandSize: actionHandSize);
            return CreateStateWithCatalog(catalog, config);
        }

        private static CombatState CreateStateWithCatalog(CardCatalogDefinition catalog, CombatConfig config, HexCoord? enemyCoord = null)
        {
            return new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                enemyCoord ?? new HexCoord(3, 0),
                config,
                cardCatalog: catalog,
                monsterCatalog: CreateConfigMonsterCatalog(config));
        }

        private static MonsterCatalogDefinition CreateConfigMonsterCatalog(CombatConfig config)
        {
            return new MonsterCatalogDefinition(
                "approved-card-config-monsters",
                "Approved Card Config Monsters",
                new[]
                {
                    new MonsterCatalogEntry(
                        CombatCatalogFactory.ThreeEyeDogMonsterId,
                        "Config Monster",
                        "test",
                        "test.config",
                        config.EnemyMaxHp,
                        config.EnemyChaseRange,
                        10,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("config-attack", "Config Attack", config.EnemyAttackRange, 0, config.EnemyAttackDamage)
                        })
                });
        }

        // buffDebuff/durationTurns mirror what cards.csv authors: the reflect/strength magnitudes and their
        // durations are card data now, not handler constants, so a fixture has to author them like the CSV does.
        private static CardCatalogDefinition CreateSingleDefendCatalog(string defendId, string defendName, int amount, string buffDebuff = "", int durationTurns = 0)
        {
            return new CardCatalogDefinition(
                DemoCardCatalog.SourceId + "." + defendId + "-test",
                defendName + " test catalog",
                new[]
                {
                    new CardCatalogEntry(CardIds.Move2Hex, "Move", CardCategory.Movement, CardEffectType.Move, 1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(defendId, defendName, CardCategory.Action, CardEffectType.Defend, 1, 0, amount, "self", status: CardCatalogStatus.Approved, durationTurns: durationTurns, buffDebuff: buffDebuff)
                });
        }
    }
}



