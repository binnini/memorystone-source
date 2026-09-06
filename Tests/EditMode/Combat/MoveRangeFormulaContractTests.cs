using System;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 🔴 <b>이동 사거리 보정은 어느 경로로 물어도 같은 값이어야 한다</b>는 계약.
    ///
    /// 2026-08-31 T2 이전에는 항이 여섯 개인 같은 식이 <c>CombatState.cs</c>에 <b>글자 그대로 네 번</b>
    /// 복사돼 있었다 — 기본 이동(<c>GetEffectiveMoveRange</c>) · 빠른거북 미리보기 · 빠른거북 집행 ·
    /// 이동 카드 <c>AreaRadius</c>(<c>GetEffectiveAreaMoveRange</c>). 네 곳의 차이는 <b>맨 앞 기준값
    /// 하나뿐</b>이었다. 그 상태에서 밸런스 조정으로 항이 하나 늘면 넷 중 셋만 고쳐도 컴파일되고,
    /// 증상은 "미리 본 거리와 실제로 갈 수 있는 거리가 다르다"로만 조용히 나타난다.
    ///
    /// T2에서 <c>ResolveMoveRange(baseValue)</c> 하나로 합쳤다. 그런데 <b>합치기만 하면 다음 사람이
    /// 다시 복사한다</b> — 그래서 이 파일이 실제 산출물이다. 여기 있는 단언은 "지금 한 함수를 쓰고
    /// 있다"가 아니라 <b>"네 경로의 보정량이 같다"</b>를 관찰 가능한 결과로 잠근다. 누가 한 경로를
    /// 다시 인라인하고 항을 하나 빠뜨리면 그 경로의 Δ만 달라지므로 여기서 빨개진다.
    ///
    /// 빠른거북 두 경로(미리보기·집행)는 P3-b(2026-09-06)에서 그 카드(옛 팩토리 전용 Draft)와 함께 은퇴했다 — 남은 경로는
    /// 기본 이동·AreaRadius 둘이며, 이 계약은 그 둘의 Δ가 같은지로 유지된다.
    ///
    /// 측정은 전부 <b>실제로 갈 수 있는 칸</b>으로 한다(직선 회랑이라 최대 도달 거리 = 유효 사거리).
    /// 내부 술어를 부르면 같은 코드를 두 번 부르는 것에 불과해 아무것도 증명하지 못한다.
    ///
    /// ⚠️ 보정 여섯 항 중 <c>activeMovementRangeModifier</c>와 유물
    /// <c>MovementRangeBonus</c>는 여기서 <b>0으로 둔다</b>(전자는 T6 <c>PendingEffects</c>의 대상,
    /// 후자는 유물 셋업이 필요하다). 잠기는 것은 민첩(+) · 둔화(−) · 손패 지각(−) 세 항이고,
    /// Δ가 셋 다 서로 다른 값으로 갈리도록 수치를 골랐다 — 어느 하나를 빠뜨려도 Δ가 달라진다.
    /// </summary>
    [Category("ShippingData")]
    public sealed class MoveRangeFormulaContractTests
    {
        // Δ = +6(민첩) − 1(둔화) − 2(손패 지각 2장) = +3.
        // 한 항을 빠뜨렸을 때의 Δ가 전부 다르다: 민첩 누락 −3 · 둔화 누락 +4 · 지각 누락 +5.
        private const int AgilityAmount = 6;
        private const int SlowAmount = 1;
        private const int TardinessCards = 2;
        private const int ExpectedDelta = AgilityAmount - SlowAmount - TardinessCards;

        private const string PlainMoveCardId = "test-move-plain";
        private const string AreaMoveCardId = "test-move-area";
        private const int PlainMoveRange = 3;
        private const int AreaMoveRadius = 4;

        private StatusEffectCatalogDefinition savedCatalog;

        [SetUp]
        public void SetUp()
        {
            // 프로바이더는 프로세스 전역이라 앞서 돈 테스트의 잔재를 끊는다. 민첩·둔화의 값 축은
            // 폴백 switch가 아니라 실제 출하 CSV에서 와야 이 계약이 의미가 있다.
            savedCatalog = StatusEffectCatalogProvider.Active;
            StatusEffectCatalogProvider.Active = StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);
        }

        [TearDown]
        public void TearDown()
        {
            StatusEffectCatalogProvider.Active = savedCatalog;
        }

        /// <summary>
        /// 네 경로의 보정량(Δ)이 전부 같은지 — 이 파일의 본론.
        /// 기준값은 경로마다 다르므로(저작 Range · AreaRadius · 빠른거북이 뽑은 거리) 비교 대상은
        /// 절대 거리가 아니라 <b>기준값 대비 증감</b>이다.
        /// </summary>
        [Test]
        public void EveryMoveRangePathAppliesTheSameModifierTotal()
        {
            var plainDelta = MeasurePlainMoveRange(WithModifiers(CreateState())) - PlainMoveRange;
            var areaDelta = MeasureAreaMoveRange(WithModifiers(CreateState())) - AreaMoveRadius;

            Assert.That(plainDelta, Is.EqualTo(ExpectedDelta), "기본 이동 경로의 보정량.");
            Assert.That(areaDelta, Is.EqualTo(ExpectedDelta),
                "이동 카드 AreaRadius 경로가 기본 이동과 다른 보정을 받았다 — 식이 갈라졌다.");
        }

        /// <summary>
        /// 보정이 하나도 없으면 네 경로 모두 저작 기준값 그대로다 — 위 테스트의 Δ가 무엇에 대한
        /// 증감인지 고정하는 전제. 이게 깨지면 Δ 비교가 우연히 맞은 것일 수 있다.
        /// </summary>
        [Test]
        public void WithoutAnyModifierEveryPathReturnsItsAuthoredBaseValue()
        {
            Assert.That(MeasurePlainMoveRange(CreateState()), Is.EqualTo(PlainMoveRange));
            Assert.That(MeasureAreaMoveRange(CreateState()), Is.EqualTo(AreaMoveRadius));
        }

        /// <summary>
        /// 이동 불가(속박·기절)는 기준값과 무관하게 사거리 0 — 네 경로가 공유하던 가드다.
        /// 합치면서 이 가드를 한 경로에서만 잃어도 티가 나게 잠근다.
        /// </summary>
        [Test]
        public void MovementBlockZeroesEveryPathRegardlessOfBaseValue()
        {
            var plain = CreateState();
            Assert.That(plain.DebugApplyStatusToPlayer(StatusEffectKind.Immobilize, turns: 3, amount: 1), Is.True);
            Assert.That(MeasurePlainMoveRange(plain), Is.EqualTo(0));

            var area = CreateState();
            Assert.That(area.DebugApplyStatusToPlayer(StatusEffectKind.Immobilize, turns: 3, amount: 1), Is.True);
            Assert.That(MeasureAreaMoveRange(area), Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- 경로별 측정

        private static int MeasurePlainMoveRange(CombatState state)
        {
            state.MovementDeck.InjectIntoHand(MoveCard(PlainMoveCardId, range: PlainMoveRange));
            return MaxReachable(state, PlainMoveCardId);
        }

        private static int MeasureAreaMoveRange(CombatState state)
        {
            // AreaRadius 경로는 저작 Range가 0인 카드에서만 단독으로 관찰된다
            // (그렇지 않으면 기본 이동 경로와의 Max에 가려진다).
            state.MovementDeck.InjectIntoHand(MoveCard(AreaMoveCardId, range: 0, areaRadius: AreaMoveRadius));
            return MaxReachable(state, AreaMoveCardId);
        }

        private static int MaxReachable(CombatState state, string cardId)
        {
            var reachable = state.GetReachablePlayerMoves(cardId);
            return reachable.Count == 0 ? 0 : reachable.Values.Max();
        }

        // ---------------------------------------------------------------- 셋업

        private static CombatState WithModifiers(CombatState state)
        {
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Agility, turns: 9, amount: AgilityAmount), Is.True);
            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Slow, turns: 9, amount: SlowAmount), Is.True);
            for (var i = 0; i < TardinessCards; i++)
            {
                state.ActionDeck.InjectIntoHand(TardinessCard($"test-tardiness-{i}"));
            }

            return state;
        }

        private static CardDefinition MoveCard(string id, int range, int areaRadius = 0)
        {
            return new CardDefinition(
                id,
                id,
                CardCategory.Movement,
                CardEffectType.Move,
                cost: 0,
                range: range,
                amount: 0,
                areaRadius: areaRadius,
                status: CardCatalogStatus.Approved);
        }

        /// <summary>
        /// 손에 있는 동안 이동력을 1 깎는 저주(X07 지각). 사용 불가 상태 카드다. 규칙은 카드 <b>id</b>(P2-b, DEC-2026-09-06-01)로
        /// 저주를 알아보므로 id는 출하 id여야 하고, 두 장을 구분하는 것은 instanceId다.
        /// </summary>
        private static CardDefinition TardinessCard(string instanceId)
        {
            return new CardDefinition(
                CardIds.Tardiness,
                "지각",
                CardCategory.Action,
                CardEffectType.Status,
                cost: 0,
                range: 0,
                amount: 0,
                status: CardCatalogStatus.Approved,
                instanceId: instanceId);
        }

        private static CombatState CreateState()
        {
            // 회랑을 길게 잡는다: 빠른거북 최대 6 + 보정 3 + 여유. 짧으면 지형이 사거리를 잘라
            // 식이 아니라 맵을 재게 된다.
            var config = new CombatConfig(20, 10, 3, 1, 4, 4, 0, 1, 0, 3, 1, 4, 3);
            var map = new HexMapData(
                Enumerable.Range(-2, 26).Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false)));
            return new CombatState(
                map,
                new HexCoord(0, 0),
                Array.Empty<MonsterConfig>(),
                config,
                drawOpeningHands: false);
        }
    }
}
