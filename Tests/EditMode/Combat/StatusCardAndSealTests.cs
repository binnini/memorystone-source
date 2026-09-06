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
    /// P2.7(묶음 D) 계약: 상태 카드 C-17 / 봉인 C-16.
    ///
    /// 이 두 기능의 최대 리스크는 "막았다고 생각했는데 한 면이 새는 것"이다(계획 R-3) —
    /// 그래서 게이트 테스트는 **세 면을 각각** 두드리고, 봉인은 이동 카드 분기까지 **네 면**을 본다.
    /// </summary>
    public sealed class StatusCardAndSealTests
    {
        // ------------------------------------------------------------------ C-17 사용 불가

        /// <summary>
        /// 상태 카드는 UI 플래그·회색 라벨·실제 실행 세 면 전부에서 거부돼야 한다.
        /// 한 면이라도 default로 새면 "사용 가능한 상태 카드"가 된다.
        /// </summary>
        [Test]
        public void StatusCardIsRejectedOnAllThreeGateFaces()
        {
            var state = CreateStateWithStatusCardInHand(FineDustId);
            var view = state.GetCombatCards().Single(card => card.Id == FineDustId);

            Assert.That(view.IsUsable, Is.False, "① UI usable 플래그");
            Assert.That(view.Status, Is.EqualTo(CombatCardStatusText.StatusCard), "② 회색 라벨");
            Assert.That(CanUse(state, FineDustId), Is.False, "③ 실행 검증");
        }

        [Test]
        public void StatusCardStaysUnusableEvenInTheMovementPhase()
        {
            // 페이즈로 새는 경로 가드: 상태 카드는 어떤 페이즈에서도 못 쓴다.
            var state = CreateStateWithStatusCardInHand(FineDustId);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));

            Assert.That(state.GetCombatCards().Single(card => card.Id == FineDustId).IsUsable, Is.False);
        }

        // ------------------------------------------------------------------ C-17 부가 효과

        [Test]
        public void FineDustVanishesFromHandAtTurnEnd()
        {
            var state = CreateStateWithStatusCardInHand(FineDustId);
            Assert.That(HandIds(state), Does.Contain(FineDustId), "전제: 손패에 있다.");

            AdvanceTurn(state);

            Assert.That(AllZoneIds(state), Does.Not.Contain(FineDustId), "소멸형은 덱에서 완전히 사라진다.");
        }

        [Test]
        public void BrokenGlassHurtsWhenItIsStillInHandAtTurnEndAndStaysInTheDeck()
        {
            var state = CreateStateWithStatusCardInHand(BrokenGlassId);
            var hpBefore = state.Player.Hp;

            AdvanceTurn(state);

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - BrokenGlassDamage));
            Assert.That(AllZoneIds(state), Does.Contain(BrokenGlassId),
                "깨진 유리는 소멸형이 아니다 — 전투 내내 다시 돌아온다.");
        }

        /// <summary>
        /// 🔑깨진 유리의 설계 전부: <b>버리면 아프지 않다</b>. 무조건 피해로 만들면
        /// "버릴 것인가, 쓸 카드를 포기할 것인가"라는 선택이 사라진다.
        /// </summary>
        [Test]
        public void BrokenGlassDoesNotHurtOnceItLeavesTheHand()
        {
            var state = CreateStateWithStatusCardInHand(BrokenGlassId);
            var card = state.ActionDeck.Hand.Single(candidate => candidate.Id == BrokenGlassId);
            Assert.That(state.ActionDeck.DiscardFromHand(card), Is.True);
            var hpBefore = state.Player.Hp;

            AdvanceTurn(state);

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore), "손에 없으면 아프지 않다.");
        }

        // ------------------------------------------------------------------ C-16 봉인

        [Test]
        public void SealLocksExactlyTheAuthoredNumberOfCards()
        {
            var state = CreateDemoInActionPhase();
            var playable = PlayableHandCount(state);
            Assert.That(playable, Is.GreaterThanOrEqualTo(2), "전제: 잠길 카드가 둘 이상 있다.");

            Inject(state, StatusEffectKind.Seal, state.Player.Id, remainingTurns: 2, amount: 2);

            Assert.That(state.SealedCardInstanceIds.Count, Is.EqualTo(2));
        }

        [Test]
        public void SealedCardIsRejectedOnAllGateFacesWhileOthersStayUsable()
        {
            var state = CreateDemoInActionPhase();
            Inject(state, StatusEffectKind.Seal, state.Player.Id, remainingTurns: 2, amount: 1);
            var sealedId = state.SealedCardInstanceIds.Single();
            var sealedCard = AllHandCards(state).Single(card => card.InstanceId == sealedId);

            var view = state.GetCombatCards().Single(card => card.InstanceId == sealedId);
            Assert.That(view.IsUsable, Is.False, "① UI usable 플래그");
            Assert.That(view.Status, Is.EqualTo(CombatCardStatusText.Sealed), "② 회색 라벨");
            Assert.That(CanUse(state, sealedCard.Id, sealedId), Is.False, "③ 실행 검증");

            var others = AllHandCards(state).Where(card => card.InstanceId != sealedId).ToList();
            Assert.That(others, Is.Not.Empty);
            Assert.That(others.All(card => !IsSealed(state, card)), Is.True, "잠긴 카드만 잠긴다.");
        }

        /// <summary>O-11 확정: 봉인은 카드 종류를 가리지 않는다 — 이동 카드도 대상이다.</summary>
        [Test]
        public void SealCanLockMovementCardsToo()
        {
            var state = CreateDemoInActionPhase();
            var moveCards = state.MovementDeck.Hand.ToList();
            Assert.That(moveCards, Is.Not.Empty, "전제: 이동 손패가 있다.");

            // 손패 전체를 덮을 만큼 크게 걸면 이동 카드도 반드시 포함된다.
            Inject(state, StatusEffectKind.Seal, state.Player.Id, remainingTurns: 2, amount: 99);

            Assert.That(
                moveCards.All(card => IsSealed(state, card)),
                Is.True,
                "이동 카드가 봉인에서 빠지면 IsCardUsable의 Move 조기 분기가 안 뚫린 것이다.");
        }

        /// <summary>
        /// 2026-09-05 통합(S5 §10.4)에서 드러난 결함: 라벨은 「봉인」인데 <c>TryPlayerMove</c>에 봉인 검사가 없어
        /// 이동 카드는 <b>실행 면</b>이 새고 있었다. 사용 불가 판정을 <c>GetCardRestriction</c> 한 곳으로 모은 뒤,
        /// 이동 실행 경로도 같은 술어를 지나는지 잠근다. 대조군(봉인 없음)을 먼저 두어 거부가 봉인 때문임을 못박는다.
        /// </summary>
        [Test]
        public void SealedMovementCardIsRejectedByTheMoveExecutionPath()
        {
            var control = CombatState.CreateDefaultDemo();
            Assert.That(control.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            var controlCard = control.MovementDeck.Hand.First(card => card.EffectType == CardEffectType.Move);
            Assert.That(control.TryPlayerMove(control.PlayerCoord, controlCard.Id), Is.True,
                "대조군: 봉인이 없으면 같은 이동 카드가 실행된다. " + control.LastFailureReason);

            var state = CombatState.CreateDefaultDemo();
            var moveCard = state.MovementDeck.Hand.First(card => card.EffectType == CardEffectType.Move);
            Inject(state, StatusEffectKind.Seal, state.Player.Id, remainingTurns: 2, amount: 99);
            Assert.That(IsSealed(state, moveCard), Is.True, "전제: 이동 카드가 봉인됐다.");

            Assert.That(state.TryPlayerMove(state.PlayerCoord, moveCard.Id), Is.False,
                "봉인된 이동 카드가 실행됐다 — 이동 실행 경로가 GetCardRestriction을 지나지 않는다.");
            Assert.That(state.LastFailureReason, Does.Contain("봉인"));
            Assert.That(state.MovementDeck.Hand, Does.Contain(moveCard), "거부됐으면 카드는 손에 그대로다.");
        }

        /// <summary>
        /// 사유가 겹칠 때의 우선순위 계약(2026-09-05 통합): 상태 카드 → 봉인 → 기절/속박 → 무장 해제.
        /// 기절한 채 봉인된 카드는 <b>세 면 모두</b> 「봉인」으로 읽혀야 한다 — 종전에는 실행 검증만 「기절」을 먼저 냈다.
        /// </summary>
        [Test]
        public void SealOutranksStunOnEveryFaceWhenBothApply()
        {
            var state = CreateDemoInActionPhase();
            Inject(state, StatusEffectKind.Stun, state.Player.Id, remainingTurns: 2, amount: 0);
            Inject(state, StatusEffectKind.Seal, state.Player.Id, remainingTurns: 2, amount: 99);

            var card = state.ActionDeck.Hand.First(candidate =>
                candidate.EffectType != CardEffectType.Status && !CardBehaviorRegistry.Resolve(candidate).UsableWhileStunned);
            Assert.That(IsSealed(state, card), Is.True, "전제: 카드가 봉인됐다.");

            var view = state.GetCombatCards().Single(candidate => candidate.InstanceId == card.InstanceId);
            Assert.That(view.IsUsable, Is.False, "① UI usable 플래그");
            Assert.That(view.Status, Is.EqualTo(CombatCardStatusText.Sealed), "② 회색 라벨 — 기절보다 봉인이 먼저");
            Assert.That(CanUseReason(state, card), Does.Contain("봉인"), "③ 실행 거부 사유 — 라벨과 같은 원인");
        }

        [Test]
        public void SealCountLargerThanTheHandIsSafe()
        {
            var state = CreateDemoInActionPhase();
            var playable = PlayableHandCount(state);

            Inject(state, StatusEffectKind.Seal, state.Player.Id, remainingTurns: 2, amount: 99);

            Assert.That(state.SealedCardInstanceIds.Count, Is.EqualTo(playable), "있는 만큼만 잠긴다.");
        }

        /// <summary>
        /// 🔑리롤 악용 차단: 순위는 카드마다 독립이라 <b>다른 카드를 내도 남은 카드의 봉인 여부가 안 변한다</b>.
        /// 손패 집합을 시드로 썼다면 싼 카드를 한 장 버려 봉인을 다시 뽑을 수 있었다.
        /// </summary>
        [Test]
        public void PlayingAnotherCardDoesNotRerollTheSeal()
        {
            var state = CreateDemoInActionPhase();
            Inject(state, StatusEffectKind.Seal, state.Player.Id, remainingTurns: 3, amount: 1);
            var sealedId = state.SealedCardInstanceIds.Single();

            var discardable = AllHandCards(state).First(card => card.InstanceId != sealedId);
            Assert.That(
                state.ActionDeck.DiscardFromHand(discardable) || state.MovementDeck.DiscardFromHand(discardable),
                Is.True);

            Assert.That(state.SealedCardInstanceIds, Is.EqualTo(new[] { sealedId }),
                "같은 카드가 계속 잠겨 있어야 한다 — 버리기로 봉인을 다시 뽑을 수 없다.");
        }

        /// <summary>
        /// O-11 확정: <b>재선정</b>. 턴이 바뀌면 순위가 통째로 다시 매겨진다.
        ///
        /// 🔴 <b>후보 풀을 통제한다</b>(덱 크기 == 손패 크기 → 매 턴 손패가 덱 전체). 예전 판은
        /// 출하 기본 덱을 그대로 써서 후보에 <c>Guid.NewGuid()</c> InstanceId가 섞였고
        /// (<c>PlayerDeckData</c>), 그래서 "재선정"이 아니라 <b>"손패가 바뀌었다"</b>를 재고 있었다 —
        /// 실행마다 갈리는 간헐 1F의 진짜 원인이다. 지금은 후보가 고정 InstanceId 7장뿐이라
        /// 이 테스트는 <b>순위 유도 함수만</b> 잰다.
        /// 몬스터가 없는 픽스처를 쓰는 이유는 전투가 도중에 끝나 턴 진행이 멈추면 계약이 아니라
        /// 픽스처를 재는 셈이 되기 때문이다.
        /// </summary>
        [Test]
        public void SealIsRepickedWhenTheTurnAdvances()
        {
            var picks = CollectSealPicksOverTurns(10);

            Assert.That(picks.Distinct().Count(), Is.GreaterThan(1),
                "여러 턴 내내 같은 카드만 잠겼다 — 재선정이 아니라 고정이다(O-11 위반). "
                + $"실제 선정 순서: {string.Join(", ", picks)}");
        }

        /// <summary>
        /// 🔑 재선정이 <b>몇 장을 실제로 돌리는가</b>. "2종 이상"만 요구하면 10턴에 2종만 도는
        /// 사실상 고정도 통과한다 — 그건 O-11이 말하는 재선정이 아니다.
        /// 후보가 결정적이므로 이 숫자 자체가 결정적이다(<see cref="SealPickSequenceIsDeterministic"/>).
        /// </summary>
        [Test]
        public void SealRepickActuallyRotatesThroughTheHand()
        {
            var picks = CollectSealPicksOverTurns(10);

            Assert.That(picks.Distinct().Count(), Is.GreaterThanOrEqualTo(3),
                "10턴을 굴렸는데 두 장 이하만 돌았다 — 순위에 턴이 거의 안 섞이고 있다. "
                + $"실제 선정 순서: {string.Join(", ", picks)}");
        }

        /// <summary>
        /// 결정성 계약: 봉인은 저장하지 않고 <c>(InstanceId, OverallTurnNumber)</c>에서 유도하므로,
        /// 같은 입력이면 <b>실행마다 같은 카드</b>가 잠겨야 한다(세이브 왕복도 이 성질에 기댄다).
        /// </summary>
        [Test]
        public void SealPickSequenceIsDeterministic()
        {
            Assert.That(CollectSealPicksOverTurns(10), Is.EqualTo(CollectSealPicksOverTurns(10)));
        }

        [Test]
        public void StatusCardsAreNeverSealTargetsBecauseTheyAreAlreadyUnplayable()
        {
            var state = CreateStateWithStatusCardInHand(FineDustId);
            Inject(state, StatusEffectKind.Seal, state.Player.Id, remainingTurns: 2, amount: 99);

            var statusCard = state.ActionDeck.Hand.Single(card => card.Id == FineDustId);
            Assert.That(IsSealed(state, statusCard), Is.False, "이미 못 쓰는 카드를 잠그면 봉인 한 장이 낭비된다.");
        }

        // ------------------------------------------------------------------ 정전 = 지속형 봉인

        /// <summary>
        /// 사용자 확정: 정전은 턴 종료 트리거가 아니라 <b>손패에 있는 동안</b> 잠근다.
        /// 그래서 별도 훈이 없고, 손패를 세는 것이 곧 규칙이다.
        /// </summary>
        [Test]
        public void BlackoutSealsWhileItSitsInHandAndReleasesWhenItLeaves()
        {
            var state = CreateStateWithStatusCardInHand(BlackoutId);

            Assert.That(state.SealedCardInstanceIds.Count, Is.EqualTo(1), "정전이 손에 들어온 즉시 1장이 잠긴다.");

            var blackout = state.ActionDeck.Hand.Single(card => card.Id == BlackoutId);
            Assert.That(state.ActionDeck.DiscardFromHand(blackout), Is.True);

            Assert.That(state.SealedCardInstanceIds, Is.Empty, "손에서 빠지면 즉시 풀린다.");
        }

        [Test]
        public void BlackoutStacksWithTheSealStatusEffect()
        {
            var state = CreateStateWithStatusCardInHand(BlackoutId);
            Inject(state, StatusEffectKind.Seal, state.Player.Id, remainingTurns: 2, amount: 1);

            Assert.That(state.SealedCardInstanceIds.Count, Is.EqualTo(2),
                "두 출처(상태이상 + 손패의 정전)는 한 숫자로 합산된다.");
        }

        // ------------------------------------------------------------------ 삽입 · 세이브

        [Test]
        public void TrapInjectsTheAuthoredStatusCardIntoTheActionDeck()
        {
            var trap = new HexTrapData(
                "status-trap",
                new HexCoord(1, 0),
                radius: 0,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.InjectStatusCard, 2, 0, null, BrokenGlassId) });
            var state = CreateState(StatusCardCatalog(), traps: new[] { trap });

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True, state.LastFailureReason);

            Assert.That(
                AllZoneIds(state).Count(id => id == BrokenGlassId),
                Is.EqualTo(2),
                "Amount = 삽입 장수.");
        }

        /// <summary>
        /// R-7: 상태 카드가 카탈로그에 없으면 구세이브 복원이 깨진다. 왕복이 실제로 되는지 고정한다.
        /// </summary>
        [Test]
        public void StatusCardsSurviveSuspendRoundTrip()
        {
            var state = CreateStateWithStatusCardInHand(BrokenGlassId);
            var snapshot = state.CreateSuspendSnapshot();

            var restored = CreateState(StatusCardCatalog());
            restored.RestoreFromSuspend(snapshot);

            Assert.That(AllZoneIds(restored), Does.Contain(BrokenGlassId));
        }

        // ------------------------------------------------------------------ helpers

        private const string FineDustId = "X01";
        private const string BrokenGlassId = "X02";
        private const string BlackoutId = "X03";
        private const int BrokenGlassDamage = 2;

        private static bool CanUse(CombatState state, string cardId, string instanceId = null)
        {
            var card = AllHandCards(state).FirstOrDefault(candidate =>
                instanceId != null ? candidate.InstanceId == instanceId : candidate.Id == cardId);
            Assert.That(card, Is.Not.Null, "전제: 그 카드가 손패에 있다.");

            var method = typeof(CombatState).GetMethod(
                "CanUseActionCard",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var args = new object[] { card, null };
            return (bool)method.Invoke(state, args);
        }

        private static string CanUseReason(CombatState state, CardDefinition card)
        {
            var method = typeof(CombatState).GetMethod(
                "CanUseActionCard",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(CardDefinition), typeof(string).MakeByRefType() },
                modifiers: null);
            var args = new object[] { card, null };
            Assert.That((bool)method.Invoke(state, args), Is.False, "전제: 실행이 거부된다.");
            return (string)args[1];
        }

        private static bool IsSealed(CombatState state, CardDefinition card)
        {
            return state.SealedCardInstanceIds.Contains(card.InstanceId);
        }

        private static IEnumerable<CardDefinition> AllHandCards(CombatState state)
        {
            return state.ActionDeck.Hand.Concat(state.MovementDeck.Hand).Where(card => card != null);
        }

        private static int PlayableHandCount(CombatState state)
        {
            return AllHandCards(state).Count(card => card.EffectType != CardEffectType.Status);
        }

        private static IEnumerable<string> HandIds(CombatState state)
        {
            return AllHandCards(state).Select(card => card.Id);
        }

        private static IEnumerable<string> AllZoneIds(CombatState state)
        {
            return state.ActionDeck.Hand
                .Concat(state.ActionDeck.DrawPile)
                .Concat(state.ActionDeck.DiscardPile)
                .Where(card => card != null)
                .Select(card => card.Id);
        }

        private static CombatState CreateDemoInActionPhase()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            return state;
        }

        /// <summary>상태 카드 하나를 확실히 손에 쥔 상태를 만든다(주입 → 드로우 대신 직접 손패로).</summary>
        private static CombatState CreateStateWithStatusCardInHand(string statusCardId)
        {
            var state = CreateState(StatusCardCatalog());
            var entry = state.CardCatalog.Entries.Single(candidate => candidate.Id == statusCardId);
            state.ActionDeck.InjectIntoHand(entry.ToCardDefinition(
                state.CardCatalog.SourceId,
                $"{statusCardId}#test",
                isTemporary: string.Equals(statusCardId, FineDustId)));
            return state;
        }

        /// <summary>봉인 대상이 될 수 있는(=사용 가능한 타입의) 카드 인스턴스 하나.</summary>
        private static CardDefinition SealTargetCard(CombatState state, string instanceId)
        {
            var entry = state.CardCatalog.Entries.Single(candidate =>
                candidate.Id == CardIds.OldArmor);
            return entry.ToCardDefinition(state.CardCatalog.SourceId, instanceId);
        }

        private static CombatState CreateState(CardCatalogDefinition catalog, IEnumerable<HexTrapData> traps = null)
        {
            var map = new HexMapData(
                Enumerable.Range(-1, 8).Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false)),
                trapRefs: traps);
            return new CombatState(
                map,
                new HexCoord(0, 0),
                System.Array.Empty<MonsterConfig>(),
                CombatConfig.Default,
                cardCatalog: catalog);
        }

        /// <summary>
        /// cards.csv의 X01~X03을 그대로 옮긴 픽스처. 실제 저작과 어긋나면
        /// <c>ShippingMapTrapAuditTests</c>·<c>CardCatalogCsvImporterTests</c>가 잡는다.
        /// </summary>
        private static CardCatalogDefinition StatusCardCatalog()
        {
            return new CardCatalogDefinition(
                "status-card-test",
                "Status card test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.Move2Hex, "Move", CardCategory.Movement, CardEffectType.Move,
                        1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        CardIds.OldArmor, "Defend", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 5, "self", status: CardCatalogStatus.Approved),
                    StatusEntry(FineDustId, "미세먼지", CardEffectRefs.StatusFineDust, 0),
                    StatusEntry(BrokenGlassId, "깨진 유리", CardEffectRefs.StatusBrokenGlass, BrokenGlassDamage),
                    StatusEntry(BlackoutId, "정전", CardEffectRefs.StatusBlackout, 0)
                });
        }

        private static CardCatalogEntry StatusEntry(string id, string name, string effectRef, int amount)
        {
            return new CardCatalogEntry(
                id, name, CardCategory.Action, CardEffectType.Status,
                0, 0, amount, "none",
                status: CardCatalogStatus.Approved,
                gameplayType: CardGameplayType.Utility,
                includeInGameplayDecks: false,
                visibleInCatalog: false);
        }

        private static void Inject(CombatState state, StatusEffectKind kind, string unitId, int remainingTurns, int amount)
        {
            typeof(CombatState)
                .GetMethod(
                    "AddDurationStatusEffect",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(StatusEffectKind), typeof(string), typeof(int), typeof(int), typeof(string) },
                    modifiers: null)
                .Invoke(state, new object[] { kind, unitId, remainingTurns, amount, "test" });
        }

        // ------------------------------------------------------------------ 봉인 재선정 픽스처

        /// <summary>봉인 재선정 픽스처의 고정 InstanceId 전량(행동 6 + 이동 1).</summary>
        private static readonly string[] FixedSealDeckInstanceIds =
        {
            "seal-target-0", "seal-target-1", "seal-target-2",
            "seal-target-3", "seal-target-4", "seal-target-5",
            "seal-move-0"
        };

        /// <summary>
        /// 봉인 1장을 건 채 <paramref name="turns"/>턴을 굴리며 매 턴 잠긴 카드를 기록한다.
        /// 매 턴 <b>후보 풀이 고정 7장 그대로인지</b>를 함께 단언한다 — 그게 깨지면 이 테스트는
        /// 순위가 아니라 덱 순환을 재기 시작하므로, 조용히 통과하게 두면 안 된다.
        /// </summary>
        private static List<string> CollectSealPicksOverTurns(int turns)
        {
            var state = CreateStateWithFixedSealDeck();
            Inject(state, StatusEffectKind.Seal, state.Player.Id, remainingTurns: turns * 3 + 10, amount: 1);

            var picks = new List<string>();
            var seenTurns = new HashSet<int>();
            for (var i = 0; i < turns; i++)
            {
                Assert.That(state.IsTerminal, Is.False, "전제: 전투가 도중에 끝나지 않는다(몬스터 없음).");
                Assert.That(
                    AllHandCards(state).Select(card => card.InstanceId),
                    Is.EquivalentTo(FixedSealDeckInstanceIds),
                    $"전제: {i + 1}번째 턴의 후보 풀이 고정 7장이어야 한다(덱 크기 == 손패 크기).");

                seenTurns.Add(state.OverallTurnNumber);
                picks.Add(state.SealedCardInstanceIds.Single());
                AdvanceTurn(state);
            }

            Assert.That(seenTurns.Count, Is.EqualTo(turns), "전제: 턴이 매번 실제로 넘어갔다.");
            return picks;
        }

        /// <summary>
        /// 덱을 손패 크기와 <b>같게</b> 만든 상태 — 매 턴 버리고 다시 뽑아도 손패는 늘 같은 7장이라
        /// 후보 풀이 셔플과 무관하게 결정적이다(<c>DeckState</c>의 셔플 RNG는 GUID 시드다).
        /// </summary>
        private static CombatState CreateStateWithFixedSealDeck()
        {
            var catalog = StatusCardCatalog();
            var deck = new PlayerDeckData(
                movementCards: new[]
                {
                    new PlayerCardInstanceData("seal-move-0", CardIds.Move2Hex)
                },
                actionCards: Enumerable.Range(0, 6).Select(i =>
                    new PlayerCardInstanceData($"seal-target-{i}", CardIds.OldArmor)));

            // CombatConfig.Default와 같되 행동 손패만 6(=덱 장수). 이동은 1이 하한이라 1로 둔다.
            var config = new CombatConfig(80, 30, 2, 1, 4, 4, 6, 1, 5, 4, 1, 6, 7, 4);

            var map = new HexMapData(
                Enumerable.Range(-1, 8).Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false)));
            return new CombatState(
                map,
                new HexCoord(0, 0),
                System.Array.Empty<MonsterConfig>(),
                config,
                cardCatalog: catalog,
                playerDeck: deck);
        }

        private static void AdvanceTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }
    }
}
