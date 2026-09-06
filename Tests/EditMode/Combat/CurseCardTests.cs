using System;
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
    /// T2(2026-08-06) 저주 카드 시스템 — 상태 카드가 '저주'로 개편되며 추가된 신규 9종(X04~X12)의
    /// 런타임 계약. 확정 정본: docs/design/keyword-systematization-and-sts-insights.md §4 T2-B.
    /// "손에 있는 동안" 계열은 정전(C-16)의 규약을, 턴말 계열은 깨진 유리(C-17)의 규약을,
    /// 아지랑이는 미세먼지·A09의 IsTemporary 규약을 각각 따른다.
    /// </summary>
    public sealed class CurseCardTests
    {
        private static readonly HexCoord PlayerCoord = new HexCoord(0, 0);
        private static readonly HexCoord MonsterCoord = new HexCoord(3, 0);

        [Test]
        public void DebtNoteIsPlayableForOneKiAndExhaustsItself()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            InjectIntoHand(state, "X04");
            var kiBefore = state.ActionCostRemaining;

            Assert.That(state.TryPlayerUtility("X04"), Is.True, state.LastFailureReason);

            Assert.That(state.ActionCostRemaining, Is.EqualTo(kiBefore - 1), "빚 문서의 값은 기 1.");
            Assert.That(state.ActionDeck.Hand.Any(card => card.Id == "X04"), Is.False);
            Assert.That(state.ActionDeck.RemovedPile.Any(card => card.Id == "X04"), Is.True,
                "빚 문서는 버림이 아니라 소멸 더미로 가야 한다 — 회수(U02)·소멸 스케일(A13)과 이어지는 계약.");
        }

        [Test]
        public void NightmareReducesVisionWhileInHand()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var baseline = EffectiveVisionRange(state);

            InjectIntoHand(state, "X05");

            Assert.That(EffectiveVisionRange(state), Is.EqualTo(baseline - 1), "악몽은 손에 있는 동안 시야 −1.");
        }

        [Test]
        public void TardinessReducesMoveRangeWhileInHand()
        {
            var state = CreateState();
            var moveCard = state.MovementDeck.Hand.First();
            var baseline = EffectiveMoveRange(state, moveCard);

            InjectIntoHand(state, "X07");

            Assert.That(EffectiveMoveRange(state, moveCard), Is.EqualTo(Math.Max(0, baseline - 1)),
                "지각은 손에 있는 동안 이동 사거리 −1.");
        }

        [Test]
        public void LingeringInHandAtTurnEndShavesNextTurnKi()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            InjectIntoHand(state, "X06");

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.ActionCostRemaining, Is.EqualTo(state.MaxKi - 1),
                "미련: 지난 턴말 손에 있던 장수만큼 이번 턴 기가 깎인다.");

            // 예약은 1회성 — 다음 턴에는 원상 복구된다(카드는 여전히 손/덱을 오염하지만 예약은 소비됐다).
            // 턴말 훅은 액션 페이즈의 EndAction에서만 돌므로 이동 페이즈를 온전히 소화하고 잰다.
            RemoveFromHand(state, "X06");
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(state.ActionCostRemaining, Is.EqualTo(state.MaxKi));
        }

        [Test]
        public void CursedCharmAndMurkyFogApplySelfDebuffsAtTurnEnd()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            InjectIntoHand(state, "X09");
            InjectIntoHand(state, "X10");

            Assert.That(state.EndAction(), Is.True);

            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Weaken && effect.TargetUnitId == "player"),
                Is.True, "부정 탄 부적: 턴말 손에 있으면 쇠약.");
            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Blind && effect.TargetUnitId == "player"),
                Is.True, "궂은 안개: 턴말 손에 있으면 실명.");
        }

        /// <summary>
        /// WS-I I-09/I-10 판정 회귀 잠금: 턴말(EndAction) 부여 저주 디버프는 SkipNextTick 유예를 받아
        /// <b>다음 턴을 온전히 덮는다</b>(1턴 = 실효 1턴). 조사 단계에서 「실효 0턴」 의심이 있었으나
        /// 유예 플래그가 이미 그 구멍을 막고 있음을 이 테스트가 실측·고정한다.
        /// </summary>
        [Test]
        public void TurnEndCurseDebuffsSurviveIntoTheNextPlayerTurn()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            InjectIntoHand(state, "X09");
            InjectIntoHand(state, "X10");

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction(); // 몬스터 행동 + 다음 턴 시작(감소 경계 1회)

            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Weaken && effect.TargetUnitId == "player"),
                Is.True, "부정 탄 부적의 쇠약 1턴은 다음 턴 시작 경계를 넘어 그 턴을 덮어야 한다.");
            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Blind && effect.TargetUnitId == "player"),
                Is.True, "궂은 안개의 실명 1턴은 다음 턴에 시야를 실제로 줄여야 한다.");
        }

        [Test]
        public void GoblinPrankSealsWhileHeldAndEvaporatesAtTurnEnd()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            InjectIntoHand(state, "X11");

            Assert.That(state.SealedCardInstanceIds, Is.Not.Empty,
                "도깨비 장난은 정전과 같은 손패 잠금을 건다.");

            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.ActionDeck.Hand.Any(card => card.Id == "X11"), Is.False,
                "아지랑이: 턴 종료 시 손에서 소멸한다.");
        }

        [Test]
        public void VengefulGhostAppliesVulnerableWhileInHandInsteadOfAFlatDamageBonus()
        {
            // 2026-08-20 #18(사용자 확정): 원귀는 「받는 피해 +1」 flat 항이 아니라 <b>손에 있는 동안
            // 허점 1턴 부여</b>다. 두 축을 함께 두면 같은 저주가 두 번 세지므로 flat 항은 제거됐고,
            // 피해 증가는 이제 허점(IncomingDamageBonusFlat) 한 축으로만 들어온다.
            var without = RunMonsterHit(withVengefulGhost: false);
            var with = RunMonsterHit(withVengefulGhost: true);
            Assert.That(without, Is.GreaterThan(0), "기준 판에서 몬스터 공격이 실제로 닿아야 한다.");
            Assert.That(with, Is.GreaterThan(without),
                "원귀가 손에 있으면 허점이 걸려 더 아프게 맞는다.");
            Assert.That(with - without, Is.EqualTo(StatusEffectInfo.DefaultAmount(StatusEffectKind.Vulnerable)),
                "증가폭은 허점의 저작값이다 — 별도 상수를 두면 키워드 표와 갈라진다.");
        }

        [Test]
        public void CursedGachaGrantsOnceAndInjectsACurse()
        {
            var state = CreateState();
            var drawPileBefore = state.ActionDeck.DrawPile.Count;

            Assert.That(state.TryOpenCursedGachaMachine("cursed-gacha-1", new SeededRewardRandom(7), out var message), Is.True);
            Assert.That(message, Is.Not.Empty);
            Assert.That(state.ActionDeck.DrawPile.Count, Is.EqualTo(drawPileBefore + 1),
                "저주 카드 1장이 덱에 확정 삽입돼야 한다.");
            Assert.That(state.ClaimedEventObjectIds, Does.Contain("cursed-gacha-1"));

            Assert.That(state.TryOpenCursedGachaMachine("cursed-gacha-1", new SeededRewardRandom(7), out _), Is.False,
                "같은 기계는 1회만 열린다(claimedEventObjectIds 소비 대장).");
        }

        [Test]
        public void MonsterPatternInjectColumnPutsCurseIntoDeck()
        {
            // 파이프라인 말단(TryInjectStatusCard)이 A027의 X08을 실제 카탈로그에서 찾아 넣는지 —
            // 패턴 CSV 파싱은 MonsterCatalogCsvConverterTests(ShippingData)가 잠근다.
            var state = CreateState();
            var before = state.ActionDeck.DrawPile.Count;
            var inject = typeof(CombatState).GetMethod("TryInjectStatusCard", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That((bool)inject.Invoke(state, new object[] { "X08" }), Is.True);
            Assert.That(state.ActionDeck.DrawPile.Count, Is.EqualTo(before + 1));
        }

        private sealed class SeededRewardRandom : IRewardRandom
        {
            private readonly Random random;
            public SeededRewardRandom(int seed) { random = new Random(seed); }
            public int Next(int maxExclusive) => random.Next(maxExclusive);
        }

        // ------------------------------------------------------------------ helpers

        private static CombatState CreateState(int enemyDistance = 3)
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 4);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                PlayerCoord,
                new HexCoord(enemyDistance, 0),
                config,
                cardCatalog: CreateCatalog());
        }

        /// <summary>cards.csv의 X행과 같은 값 — 스키마 정합은 CardCatalogCsvImporterTests(ShippingData)가 잠근다.</summary>
        private static CardCatalogDefinition CreateCatalog()
        {
            CardCatalogEntry Curse(string id, string name, string effectRef, int cost = 0)
                => new CardCatalogEntry(
                    id, name, CardCategory.Action, CardEffectType.Status,
                    cost, 0, 0, string.Empty,
                    status: CardCatalogStatus.Approved, includeInGameplayDecks: false, visibleInCatalog: false);

            return new CardCatalogDefinition(
                "curse-card-test",
                "Curse card test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        "D00", "방어의 기초", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 3, "self", status: CardCatalogStatus.Approved),
                    Curse("X04", "빚 문서", CardEffectRefs.StatusDebtNote, cost: 1),
                    Curse("X05", "악몽", CardEffectRefs.StatusNightmare),
                    Curse("X06", "미련", CardEffectRefs.StatusLingering),
                    Curse("X07", "지각", CardEffectRefs.StatusTardiness),
                    Curse("X08", "골칫거리", CardEffectRefs.StatusNuisance),
                    Curse("X09", "부정 탄 부적", CardEffectRefs.StatusCursedCharm),
                    Curse("X10", "궂은 안개", CardEffectRefs.StatusMurkyFog),
                    Curse("X11", "도깨비 장난", CardEffectRefs.StatusGoblinPrank),
                    Curse("X12", "원귀", CardEffectRefs.StatusVengefulGhost),
                });
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        /// <summary>주입(온전한 임시 플래그 포함) 후 즉시 드로우해 손패에 올린다 — 출하 주입 경로 재사용.</summary>
        private static void InjectIntoHand(CombatState state, string cardId)
        {
            var inject = typeof(CombatState).GetMethod("TryInjectStatusCard", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That((bool)inject.Invoke(state, new object[] { cardId }), Is.True, $"{cardId} 주입 실패");
            state.ActionDeck.Draw(1);
            Assert.That(state.ActionDeck.Hand.Any(card => card.Id == cardId), Is.True, $"{cardId}가 손패에 없다");
        }

        private static void RemoveFromHand(CombatState state, string cardId)
        {
            var card = state.ActionDeck.Hand.First(candidate => candidate.Id == cardId);
            state.ActionDeck.PermanentRemoveFromHand(card);
        }

        private static int EffectiveVisionRange(CombatState state)
        {
            var method = typeof(CombatState).GetMethod("GetEffectivePlayerVisionRange", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)method.Invoke(state, null);
        }

        private static int EffectiveMoveRange(CombatState state, CardDefinition moveCard)
        {
            var method = typeof(CombatState).GetMethod("GetEffectiveMoveRange", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)method.Invoke(state, new object[] { moveCard });
        }

        /// <summary>
        /// 몬스터를 인접에 두고 인텐트 커밋 → 공격까지 온전한 턴 사이클(이동 해소 포함)로 굴려
        /// 플레이어가 실제로 잃은 HP를 반환. 손패 재드로우로 원귀가 손을 떠날 수 있어 매 사이클
        /// 손패 존재를 보장한다("손에 있는 동안"이 계약이므로).
        /// </summary>
        private static int RunMonsterHit(bool withVengefulGhost)
        {
            var state = CreateState(enemyDistance: 1);
            var hpBefore = state.Player.Hp;
            for (var i = 0; i < 4 && state.Player.Hp == hpBefore; i++)
            {
                if (withVengefulGhost && state.ActionDeck.Hand.All(card => card.Id != "X12"))
                {
                    InjectIntoHand(state, "X12");
                }

                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterMovement();
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterAction();
            }

            return hpBefore - state.Player.Hp;
        }
    }
}
