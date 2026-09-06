using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 카드 연마(camper-workshop-plan.md P0 · 카드 클래스 전환 P4, DEC-2026-09-06-05). 연마 값은 카드 클래스의
    /// <see cref="CardBehavior.Upgrade"/>가 정하고(옛 card_upgrades.csv 은퇴), <see cref="CardUpgrades.Resolve"/>가 정의 시점에
    /// 치환한다 — 실행 시점 배율이 아니라 정의 시점이라야 필드 카드(배치 시점 저작값)에도 먹는다. 그 계약과
    /// <see cref="CombatState.TryRefineCard"/>까지의 흐름을 여기서 감시한다. 저작 수치는 핀하지 않고 「base와 다르다」로 잰다.
    /// </summary>
    public sealed class CardRefineTests
    {
        // ── 클래스 연마 → 정의 시점 치환 ─────────────────────────────────────────────

        [Test]
        public void UpgradeReplacesAuthoredValuesAtDefinitionTimeAndKeepsIdentity()
        {
            var entry = DemoCardCatalog.Create(CombatConfig.Default).Entries.Single(candidate => candidate.Id == CardIds.Sweep);

            var level0 = CardUpgrades.Resolve(entry.ToCardDefinition("test-source", "inst", 0));
            Assert.That(level0.Amount, Is.EqualTo(entry.Amount));
            Assert.That(level0.DisplayName, Is.EqualTo(entry.DisplayName));

            var level1 = CardUpgrades.Resolve(entry.ToCardDefinition("test-source", "inst", 1));
            Assert.That(level1.Amount, Is.GreaterThan(level0.Amount), "A01의 클래스 연마(피해)가 Amount로 도착해야 한다.");
            Assert.That(level1.Cost, Is.EqualTo(level0.Cost), "바꾸지 않은 축은 원본 유지.");
            Assert.That(level1.DisplayName, Is.EqualTo(entry.DisplayName + CardUpgrades.NameSuffix), "D-6: 연마 카드는 카드명 뒤 + 접미.");
            Assert.That(level1.UpgradeLevel, Is.EqualTo(1));
            Assert.That(level1.InstanceId, Is.EqualTo("inst"));
            Assert.That(level1.Id, Is.EqualTo(entry.Id));
        }

        [Test]
        public void RefinedFieldCardCarriesUpgradedPlacementValues()
        {
            // 🔴 필드 카드는 배치 시점 저작값을 그대로 쓴다 — 정의 자체가 연마값이어야 장판 수치가 연마를 따라간다.
            // 출하 필드 카드는 아직 연마를 선언하지 않았으므로(D-6 범위 밖) 계약은 픽스처 클래스 없이 With로 잰다:
            // Resolve는 클래스가 돌려준 정의를 그대로 덱에 싣는다.
            var entry = DemoCardCatalog.Create(CombatConfig.Default).Entries.Single(candidate => candidate.Id == CardIds.Firebomb);
            var baseline = entry.ToCardDefinition("test-source", "inst", 0);

            var refined = baseline.With(amount: baseline.Amount + 3, durationTurns: baseline.DurationTurns + 2, upgradeLevel: 1);

            Assert.That(refined.Amount, Is.EqualTo(baseline.Amount + 3));
            Assert.That(refined.DurationTurns, Is.EqualTo(baseline.DurationTurns + 2));
            Assert.That(refined.FieldObjectKind, Is.EqualTo(CardFieldObjectKind.FieldDamage), "With는 장판 종류·타게팅을 바꾸지 않는다.");
            Assert.That(refined.Targeting, Is.EqualTo(baseline.Targeting));
        }

        /// <summary>연마 가능 카드 집합은 클래스 선언이 정본이다 — 옛 card_upgrades.csv 24행 + D-6의 U01.</summary>
        [Test]
        public void UpgradableSetIsExactlyTheAuthoredCards()
        {
            var demo = DemoCardCatalog.Create(CombatConfig.Default);
            var upgradable = CardBehaviorRegistry.RegisteredIds
                .Where(id => CardBehaviorRegistry.Get(id).CanUpgrade(ProbeDefinition(demo, id)))
                .ToList();

            Assert.That(upgradable, Is.EquivalentTo(new[]
            {
                CardIds.BasicStrike, CardIds.Sweep, CardIds.MoveLinkedStrike, CardIds.FinishingTouch, CardIds.FinalBlow,
                CardIds.DoubleHit, CardIds.OneStrikeEnough, CardIds.MultiplyingStrike, CardIds.Sacrifice, CardIds.TargetShot,
                CardIds.Plague, CardIds.Remnant, CardIds.Intimidate,
                CardIds.BasicBlock, CardIds.OldArmor, CardIds.DoubleEdgedShield, CardIds.HeavyArmor, CardIds.Hospitalization, CardIds.FullyPrepared,
                CardIds.Move1Hex, CardIds.Move2Hex, CardIds.Move3Hex, CardIds.Move4Hex, CardIds.Shortcut,
                CardIds.Redraw,
                // 효과 연마 2차 · 수치 13장(DEC-2026-09-06-08)
                CardIds.Momentum, CardIds.HolyLight, CardIds.Minefinder, CardIds.Treasurefinder, CardIds.StunFlash,
                CardIds.StoneBridgeTap, CardIds.WeakSpot, CardIds.Firebomb, CardIds.SacredLamp, CardIds.Flashbang,
                CardIds.LifestealZone, CardIds.BounceBomb, CardIds.Torch,
                // 효과 연마 2차 · 효과 1차(S04·U02·U03·S00)
                CardIds.Bingo, CardIds.DrawOrRecover, CardIds.CleanseDraw, CardIds.BasicScout,
                // 효과 연마 2차 · 효과 2차(D02·D05·M08·M06)
                CardIds.ShelterTaunt, CardIds.TalismanShield, CardIds.FullSprint, CardIds.RandomJourney
            }), "연마 선언 카드 집합이 바뀌면 밸런스 결정이다 — DEC 없이 늘리거나 줄이지 않는다.");
        }

        /// <summary>
        /// 옛 임포터의 ValidateAmountAxis 이관: 연마가 바꾸는 축은 원본에도 있어야 한다(원본이 0인 축을 연마로 세우면 amount 우선순위가 뒤틀린다).
        /// </summary>
        [Test]
        [Category("ShippingData")]
        public void UpgradedAxesMustExistOnTheBaseCard()
        {
            foreach (var entry in ShippingCatalog().Entries)
            {
                var baseCard = entry.ToCardDefinition("shipping", entry.Id + "#inst", 0);
                var upgraded = CardBehaviorRegistry.Get(entry.Id).Upgrade(baseCard, 1);
                if (upgraded == null)
                {
                    continue;
                }

                Assert.That(upgraded.Id, Is.EqualTo(baseCard.Id), $"{entry.Id}: 연마는 카드 id를 바꾸지 않는다.");
                if (upgraded.Amount != baseCard.Amount)
                {
                    Assert.That(baseCard.Amount, Is.GreaterThan(0), $"{entry.Id}: 원본이 비운 수치 축을 연마가 세웠다.");
                }

                if (upgraded.HealAmount != baseCard.HealAmount)
                {
                    Assert.That(baseCard.HealAmount, Is.GreaterThan(0), $"{entry.Id}: 원본이 비운 heal 축을 연마가 세웠다.");
                }

                if (upgraded.Range != baseCard.Range)
                {
                    Assert.That(baseCard.Range, Is.GreaterThan(0), $"{entry.Id}: 칸수 축이 없는 카드에 사거리 연마를 저작했다.");
                }
            }
        }

        /// <summary>
        /// 연마는 플레이어에게 <b>보여야</b> 한다: 토큰 문안({Damage}·{Heal}·{Duration} …)은 수치가 바뀌면 저절로 달라지고,
        /// 리터럴 문안(「민첩 2」·「범위 1」)이나 효과 연마는 <c>descriptionUpgraded</c>를 저작해야 달라진다. 어느 쪽이든
        /// 「레벨 1의 렌더링 문안 ≠ 레벨 0」이 불변식이다 — U01만 보던 검사를 연마 카드 전체로 일반화(효과 연마 2차).
        /// </summary>
        [Test]
        [Category("ShippingData")]
        public void EveryUpgradeChangesWhatTheCardSays()
        {
            var unchanged = new List<string>();
            foreach (var entry in ShippingCatalog().Entries)
            {
                if (CardBehaviorRegistry.Get(entry.Id).Upgrade(entry.ToCardDefinition("shipping", "probe", 0), 1) == null)
                {
                    continue;
                }

                var before = CombatState.ResolveAuthoredDescription(CardUpgrades.Resolve(entry.ToCardDefinition("shipping", "inst", 0)));
                var after = CombatState.ResolveAuthoredDescription(CardUpgrades.Resolve(entry.ToCardDefinition("shipping", "inst", 1)));
                if (string.Equals(before, after, System.StringComparison.Ordinal))
                {
                    unchanged.Add($"{entry.Id}: {before}");
                }
            }

            Assert.That(unchanged, Is.Empty,
                "연마했는데 카드 문안이 그대로다 — 토큰이 없는 문안이면 descriptionUpgraded를 저작해야 한다: " + string.Join(" | ", unchanged));
        }

        [Test]
        [Category("ShippingData")]
        public void EffectUpgradeUsesTheAuthoredUpgradedDescription()
        {
            var entry = ShippingCatalog().Entries.Single(candidate => candidate.Id == CardIds.Redraw);
            Assert.That(entry.DescriptionUpgraded, Is.Not.Empty, "U01은 효과가 바뀌는 연마라 descriptionUpgraded 문안이 있어야 한다(D-3).");

            var refined = CardUpgrades.Resolve(entry.ToCardDefinition("shipping", "inst", 1));
            Assert.That(refined.Description, Is.EqualTo(entry.DescriptionUpgraded));
            Assert.That(CardUpgrades.Resolve(entry.ToCardDefinition("shipping", "inst", 0)).Description, Is.EqualTo(entry.Description));
        }

        // ── D-6: 효과가 바뀌는 연마 첫 사례 — U01 다시 뽑기+ ─────────────────────────────

        [Test]
        public void RedrawAtLevelZeroDrawsTheSameCount()
        {
            var state = CreateRedrawState();
            var before = state.ActionDeck.HandCount;

            Assert.That(state.TryPlayerUtility(CardIds.Redraw), Is.True, state.LastFailureReason);

            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(before), "연마 전 다시 뽑기는 같은 수만큼 뽑는다.");
        }

        [Test]
        public void RedrawAtLevelOneDrawsOneMoreCard()
        {
            var state = CreateRedrawState();
            Assert.That(state.TryRefineCard(CardIds.Redraw, out var reason), Is.True, reason);
            var before = state.ActionDeck.HandCount;

            Assert.That(state.TryPlayerUtility(CardIds.Redraw), Is.True, state.LastFailureReason);

            // D-6 규칙 문면 그대로 「+1」 — 클래스 상수를 다시 읽으면 상수를 0으로 바꿔도 통과하는 동어반복이 된다.
            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(before + 1),
                "연마된 다시 뽑기는 같은 수 +1장을 뽑아야 한다 — TryApplyUtility의 UpgradeLevel 분기.");
        }

        // ── 효과 연마 2차 — M08 전력 질주+ ──────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void FullSprintBooksAgilityForNextTurnOnlyWhenRefined()
        {
            // 전력 질주+ (DEC-2026-09-06-08): 이동 뒤 다음 턴 민첩 1 — M05와 같은 예약 경로라 다음 전체 턴 시작에 붙는다. 리터럴 기대값.
            foreach (var refined in new[] { false, true })
            {
                var entry = ShippingCatalog().Entries.Single(candidate => candidate.Id == CardIds.FullSprint);
                var state = CombatStateFixture.Arena(4)
                    .WithShippingCardCatalog()
                    .WithEnemyEastAt(3)
                    .WithMovementHand(entry.ToCardDefinition("shipping", "m08#inst"))
                    .Build();
                if (refined)
                {
                    Assert.That(state.TryRefineCard(CardIds.FullSprint, out var reason), Is.True, reason);
                }

                Assert.That(state.TryPlayerMove(new HexCoord(1, 0), CardIds.FullSprint), Is.True, state.LastFailureReason);
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterMovement();
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterAction();

                var agility = state.ActiveEffects.Where(effect => effect.Kind == StatusEffectKind.Agility && effect.TargetUnitId == state.Player.Id).ToList();
                if (refined)
                {
                    Assert.That(agility.Select(effect => effect.Amount), Is.EqualTo(new[] { 1 }), "전력 질주+는 다음 턴 민첩 1을 예약한다.");
                }
                else
                {
                    Assert.That(agility, Is.Empty, "연마 전 전력 질주는 민첩을 주지 않는다.");
                }
            }
        }

        // ── DEC-2026-09-06-08 부수 결정 — A14 으름장+ 쇠약 50 ─────────────────────────

        [Test]
        [Category("ShippingData")]
        public void IntimidateAppliesWeaken50OnlyWhenRefined()
        {
            // 옛 연마 CSV에 죽어 있던 「쇠약 30→50」을 사용자가 적용으로 확정했다. 연마 전은 저작값(stateEffect), 연마 후는 50(리터럴).
            foreach (var refined in new[] { false, true })
            {
                var entry = ShippingCatalog().Entries.Single(candidate => candidate.Id == CardIds.Intimidate);
                var authoredWeaken = CardBehaviorMetadata.GetEffectAmount(entry.StateEffect, nameof(StatusEffectKind.Weaken), 0);
                Assume.That(authoredWeaken, Is.GreaterThan(0), "전제: A14는 stateEffect에 쇠약을 저작한다.");
                var state = CombatStateFixture.Arena(4)
                    .WithShippingCardCatalog()
                    .WithEnemyEastAt(1)
                    .WithActionHand(entry.ToCardDefinition("shipping", "a14#inst"))
                    .Build();
                if (refined)
                {
                    Assert.That(state.TryRefineCard(CardIds.Intimidate, out var reason), Is.True, reason);
                }
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterMovement();

                Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), CardIds.Intimidate), Is.True, state.LastFailureReason);

                var weaken = state.ActiveEffects.Single(effect => effect.Kind == StatusEffectKind.Weaken && effect.TargetUnitId != state.Player.Id);
                Assert.That(weaken.Amount, Is.EqualTo(refined ? 50 : authoredWeaken), refined ? "으름장+는 쇠약 50을 건다." : "연마 전은 저작값 그대로다.");
            }
        }

        // ── CombatState.TryRefineCard (순수 C# 카탈로그) ────────────────────────────────

        [Test]
        public void TryRefineCardUpgradesTheLiveDeckAndTheRunDeckTogether()
        {
            var state = CreateState();
            var authored = FindAnywhere(state.ActionDeck, CardIds.Sweep).Amount;

            Assert.That(state.CanRefineCard(CardIds.Sweep), Is.True);
            Assert.That(state.TryRefineCard(CardIds.Sweep, out var reason), Is.True, reason);

            var live = FindAnywhere(state.ActionDeck, CardIds.Sweep);
            Assert.That(live, Is.Not.Null);
            Assert.That(live.UpgradeLevel, Is.EqualTo(1));
            Assert.That(live.Amount, Is.GreaterThan(authored), "라이브 전투 덱의 카드가 연마 정의로 바뀌어야 한다.");
            Assert.That(live.DisplayName, Does.EndWith(CardUpgrades.NameSuffix));

            var instance = state.PlayerDeck.ActionCards.Single(card => card.CardId == CardIds.Sweep);
            Assert.That(instance.UpgradeLevel, Is.EqualTo(1), "런 지속 덱에도 연마가 반영돼야 세이브 복원 후 유지된다.");
        }

        [Test]
        public void RefineIsOncePerCardByLogicNotBySchema()
        {
            var state = CreateState();
            Assert.That(state.TryRefineCard(CardIds.Sweep, out var reason), Is.True, reason);

            Assert.That(state.TryRefineCard(CardIds.Sweep, out reason), Is.False);
            Assert.That(reason, Does.Contain("이미 연마"));
        }

        [Test]
        public void CardsWithoutAnUpgradeAreNotRefinable()
        {
            // 출하 카드는 저주·A09 말고 전부 연마를 선언하므로(효과 연마 2차) 클래스 없는 필러로 「연마 없음」을 만든다.
            var catalog = new CardCatalogDefinition(
                "refine-none-test", "No-upgrade test catalog",
                new[]
                {
                    new CardCatalogEntry("MOVE-FILLER", "Move", CardCategory.Movement, CardEffectType.Move, 1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry("ACTION-FILLER", "Filler", CardCategory.Action, CardEffectType.Defend, 1, 0, 1, "self", status: CardCatalogStatus.Approved),
                });
            var state = CombatStateFixture.Arena(3).WithEnemyEastAt(3).WithCardCatalog(catalog).Build();

            Assert.That(state.CanRefineCard("ACTION-FILLER", out var reason), Is.False);
            Assert.That(reason, Does.Contain("저작"));
        }

        [Test]
        public void TemporaryCardsAreNotRefinable()
        {
            var state = CreateState();
            var temporary = AttackEntry(amount: 3).ToCardDefinition("refine-test", "tmp-inst", 0, isTemporary: true);

            Assert.That(state.CanRefineCard(temporary, out var reason), Is.False);
            Assert.That(reason, Does.Contain("임시"));
        }

        [Test]
        public void PreviewReturnsBeforeAfterPairWithoutMutating()
        {
            var state = CreateState();

            Assert.That(state.TryPreviewRefinedCard(CardIds.Sweep, out var current, out var refined, out var reason), Is.True, reason);
            Assert.That(refined.Amount, Is.GreaterThan(current.Amount));
            Assert.That(refined.UpgradeLevel, Is.EqualTo(1));

            Assert.That(state.CanRefineCard(CardIds.Sweep), Is.True, "미리보기는 상태를 바꾸면 안 된다.");
            Assert.That(state.PlayerDeck.ActionCards.Single(card => card.CardId == CardIds.Sweep).UpgradeLevel, Is.EqualTo(0));
        }

        [Test]
        public void RefineSurvivesTheDeckSaveRoundtrip()
        {
            var state = CreateState();
            var authored = FindAnywhere(state.ActionDeck, CardIds.Sweep).Amount;
            Assert.That(state.TryRefineCard(CardIds.Sweep, out var reason), Is.True, reason);

            var restoredDeck = PlayerDeckSaveData.FromPlayerDeckData(state.PlayerDeck).ToPlayerDeckData();
            var instance = restoredDeck.ActionCards.Single(card => card.CardId == CardIds.Sweep);
            Assert.That(instance.UpgradeLevel, Is.EqualTo(1));

            var resolved = CardUpgrades.Resolve(PlayerDeckData.ResolveCard(RefineCatalog(), CardCategory.Action, instance));
            Assert.That(resolved.Amount, Is.GreaterThan(authored), "복원 후 재해석해도 연마 정의가 나와야 한다.");

            var restoredState = new CombatState(
                CombatState.CreateDemoMap(3), new HexCoord(0, 0),
                new[] { new MonsterConfig("refine-monster", new HexCoord(3, 0), 10) },
                TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 1),
                cardCatalog: RefineCatalog(), playerDeck: restoredDeck);
            Assert.That(FindAnywhere(restoredState.ActionDeck, CardIds.Sweep).Amount, Is.GreaterThan(authored),
                "복원된 런 덱으로 만든 전투 덱도 연마 정의여야 한다(CreateDeck의 Resolve).");
        }

        [Test]
        public void ReplaceAnywhereKeepsPileAndPosition()
        {
            var a = SimpleCard("a");
            var b = SimpleCard("b");
            var c = SimpleCard("c");
            var deck = new CardDeckState(new[] { a, b, c });
            var replacement = SimpleCard("b");

            Assert.That(deck.TryReplaceAnywhere(b, replacement), Is.True);
            Assert.That(deck.DrawPile, Is.EqualTo(new[] { a, replacement, c }),
                "치환은 순서를 보존해야 한다 — 제거 후 재삽입이면 손패가 이유 없이 재배열된다.");
            Assert.That(deck.TryReplaceAnywhere(b, replacement), Is.False, "이미 치환된 원본은 다시 못 찾는다.");
        }

        [Test]
        public void PlayerDeckUpgradeCardReplacesOnlyTheMatchingInstance()
        {
            var deck = new PlayerDeckData(
                actionCards: new[]
                {
                    new PlayerCardInstanceData("inst-1", "A01"),
                    new PlayerCardInstanceData("inst-2", "A01")
                });

            var upgraded = deck.UpgradeCard("inst-2", 1);
            Assert.That(upgraded.ActionCards.Single(card => card.InstanceId == "inst-1").UpgradeLevel, Is.EqualTo(0));
            Assert.That(upgraded.ActionCards.Single(card => card.InstanceId == "inst-2").UpgradeLevel, Is.EqualTo(1));
            Assert.That(deck.UpgradeCard("missing", 1), Is.SameAs(deck), "없는 인스턴스면 같은 덱을 돌려준다.");
        }

        // ── helpers ───────────────────────────────────────────────────────────────────

        /// <summary>연마 판정용 정의 — 데모 카탈로그에 있으면 그 저작값, 없으면(출하 전용 카드) 최소 정의. CanUpgrade는 null 여부만 본다.</summary>
        private static CardDefinition ProbeDefinition(CardCatalogDefinition demo, string id)
        {
            var entry = demo.Entries.FirstOrDefault(candidate => candidate.Id == id);
            return entry != null
                ? entry.ToCardDefinition("probe", id + "#probe")
                : new CardDefinition(id, id, CardCategory.Action, CardEffectType.Attack, 1, 1, 1);
        }

        private static CardCatalogDefinition ShippingCatalog()
        {
            var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            try
            {
                asset.SetRows(CardCatalogAsset.ParseCsvText(File.ReadAllText(CombatCsvPaths.CardsCsv, new UTF8Encoding(false, true))));
                return asset.ToCardCatalogDefinition(CombatConfig.Default);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        private static CombatState CreateState()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("refine-monster", new HexCoord(3, 0), 10) },
                config,
                cardCatalog: RefineCatalog());
        }

        /// <summary>U01 + 채움 카드 6장. 손패 2장으로 시작해 재드로우 전후 장수를 비교한다.</summary>
        private static CombatState CreateRedrawState()
        {
            var entries = new List<CardCatalogEntry>
            {
                new CardCatalogEntry(
                    CardIds.RandomJourney, "Move", CardCategory.Movement, CardEffectType.Move,
                    1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                new CardCatalogEntry(
                    CardIds.Redraw, "다시 뽑기", CardCategory.Action, CardEffectType.Utility,
                    1, 0, 0, "current_action_hand_except_self", playMode: CardPlayMode.Self, status: CardCatalogStatus.Approved),
            };
            entries.AddRange(Enumerable.Range(1, 6).Select(index => new CardCatalogEntry(
                "FILL" + index, "채움 " + index, CardCategory.Action, CardEffectType.Defend,
                1, 0, 1, "self", playMode: CardPlayMode.Self, status: CardCatalogStatus.Approved)));

            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 2);
            var state = new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("redraw-monster", new HexCoord(3, 0), 10) },
                config,
                cardCatalog: new CardCatalogDefinition("redraw-test", "Redraw test catalog", entries));
            Assert.That(state.ActionDeck.Hand.Any(card => card.Id == CardIds.Redraw), Is.True, "픽스처: U01이 첫 손패에 있어야 한다.");
            return state;
        }

        private static CardCatalogDefinition RefineCatalog()
        {
            return new CardCatalogDefinition(
                "refine-test",
                "Refine test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.RandomJourney, "Move", CardCategory.Movement, CardEffectType.Move,
                        1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    AttackEntry(amount: 3)
                });
        }

        /// <summary>A01 휘둘러치기 id — 연마 값은 클래스(A01_Sweep.Upgrade)가 준다.</summary>
        private static CardCatalogEntry AttackEntry(int amount)
        {
            return new CardCatalogEntry(
                CardIds.Sweep, "테스트 공격", CardCategory.Action, CardEffectType.Attack,
                1, 1, amount, "living_monster_in_range",
                status: CardCatalogStatus.Approved);
        }

        private static CardDefinition SimpleCard(string id)
        {
            return new CardDefinition(id, id, CardCategory.Action, CardEffectType.Attack, 1, 1, 1, instanceId: id + "-inst");
        }

        private static CardDefinition FindAnywhere(CardDeckState deck, string cardId)
        {
            return deck.Hand.Concat(deck.DrawPile).Concat(deck.DiscardPile)
                .FirstOrDefault(card => card.Id == cardId);
        }
    }
}
