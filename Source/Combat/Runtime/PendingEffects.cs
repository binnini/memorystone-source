using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 🔴 <b>「예약(booking)」 — 값 + 지속 턴 + 발효 시점.</b> 지금 정해 두고 <b>나중 경계에서</b>
    /// 발효되는 효과를 한 개념으로 모은 타입이다.
    ///
    /// 왜 타입이 됐나(2026-08-31 T6): <see cref="CombatState"/>에 <c>pending*</c> 필드가 8개 흩어져
    /// 있었고, 그중 셋이 <b>(Amount, Turns) 쌍</b>이었다. 쌍이 반복된다는 것은 <b>이름 없는 개념이
    /// 숨어 있다는 신호</b>다. 게다가 예약/소진 짝이 이미 손으로 <b>두 번</b> 쓰여 있었다
    /// (지연 자기 속박 · 도발 강화) — 둘 다 「반복 예약은 <b>더한 값이 아니라 더 긴 쪽</b>」으로
    /// 병합하고, 소진할 때 값을 꺼내 0으로 되돌린다. <b>같은 규칙의 사본 두 벌</b>이었다.
    ///
    /// 이제 병합 규칙은 <see cref="MergeLongest"/> 한 곳에만 있고, 소진은 <b>꺼내면서 비운다</b> —
    /// 값을 읽고 지우는 것을 잊는 형태의 이중 소비가 타입상 불가능하다.
    ///
    /// ⚠️ <b>수명이 두 종류라는 것을 의식할 것.</b> 예약 넷은 <b>턴 경계</b>에서 소진되고,
    /// <see cref="IncomingDamageNullifiedThisMonsterAction"/>는 <b>몬스터 행동 경계</b> 플래그다
    /// (<see cref="ResetPerMonsterAction"/>가 그 차이의 표현이다). 둘을 한 번에 소진하는 메서드를
    /// 만들지 않은 이유가 이것이다 — 섞는 순간 D02·D05의 무효화 창이 턴 길이로 늘어난다.
    ///
    /// 🔑 <b>인계문의 <c>ConsumeForTurnStart()</c> 한 방 대신 항목별 <c>Take*</c>를 골랐다</b>(착수자 판단).
    /// 네 예약은 <c>BeginNextOverallTurn</c>의 <b>서로 다른 세 지점</b>에서 소진되고
    /// (기 차감 → 유물 턴 시작 훅 → 민첩 → 속박·도발), 그 사이에
    /// <c>ResolveTurnStartRelicTriggers</c>가 낀다. 맨 앞에서 한꺼번에 꺼내면 <b>오늘은</b> 같지만,
    /// 턴 시작에 예약을 거는 유물이 하나 생기는 순간 그 예약이 조용히 사라진다. 소진 지점을
    /// 옮기는 것은 이 트랙의 몫이 아니다(순서 판정은 T7).
    /// </summary>
    internal sealed class PendingEffects
    {
        /// <summary>
        /// 예약 한 건. 지속 턴만 쓰는 예약(자기 속박)은 <see cref="Amount"/>를 0으로 둔다.
        /// </summary>
        internal readonly struct Booking
        {
            internal Booking(int amount, int turns)
            {
                Amount = amount;
                Turns = turns;
            }

            internal int Amount { get; }
            internal int Turns { get; }
        }

        private Booking agility;
        private Booking selfImmobilize;
        private Booking provokeStrength;
        private int nextTurnKiPenalty;
        private int activeMovementRangeModifier;
        private bool incomingDamageNullifiedThisMonsterAction;

        // ------------------------------------------------------------------ 예약

        /// <summary>
        /// 추진(M-계열)이 이번 턴 이동을 포기하고 넘기는 민첩. 🔑 <b>병합하지 않고 덮어쓴다</b> —
        /// 카드가 두 필드를 따로 쓰던 형태를 그대로 옮긴 것이고, 한 턴에 두 번 걸 수 없는 카드다.
        /// </summary>
        internal void BookAgility(int amount, int turns)
        {
            agility = new Booking(amount, turns);
        }

        /// <summary>
        /// D04/D06이 방어의 대가로 무는 자기 속박. 즉시 걸지 않고 예약하는 것이 핵심이다 —
        /// 즉시 걸면 바로 뒤에 U03을 써서 지워 버릴 수 있고, 그러면 카드 값이 공짜가 된다.
        /// </summary>
        internal void BookSelfImmobilize(int turns)
        {
            selfImmobilize = MergeLongest(selfImmobilize, 0, Math.Max(1, turns));
        }

        /// <summary>
        /// D02 「보호구역 안에서 도발」의 다음 턴 적 강화. 즉시 부여하면 이번 턴 몬스터 행동부터
        /// 강화가 적용돼 문안(다음 턴)과 어긋난다 — 지연 자기 속박과 같은 예약 문법이다.
        /// </summary>
        internal void BookProvokeStrength(int amount, int durationTurns)
        {
            provokeStrength = MergeLongest(provokeStrength, Math.Max(0, amount), Math.Max(1, durationTurns));
        }

        /// <summary>미련(X06)이 걸던 다음 턴 기 차감. 현재 저작에서는 적립되지 않는다(세이브 호환용).</summary>
        internal void BookNextTurnKiPenalty(int amount)
        {
            nextTurnKiPenalty = Math.Max(0, amount);
        }

        /// <summary>
        /// 🔴 <b>반복 예약은 더한 값이 아니라 더 긴 쪽이다</b>(D10). 예전에는 이 규칙이
        /// <c>SchedulePlayerDelayedImmobilize</c>와 <c>ScheduleProvokeStrength</c>에 <b>따로</b>
        /// 쓰여 있었다 — 한쪽만 고치면 두 예약이 다른 규칙으로 쌓인다.
        /// </summary>
        private static Booking MergeLongest(Booking existing, int amount, int turns)
        {
            return new Booking(Math.Max(existing.Amount, amount), Math.Max(existing.Turns, turns));
        }

        // ------------------------------------------------------------------ 소진 (꺼내면서 비운다)

        /// <summary>민첩 예약을 꺼내고 비운다. 예약이 없으면 <c>Amount</c>가 0이다.</summary>
        internal Booking TakeAgility()
        {
            var taken = agility;
            agility = default;
            return taken;
        }

        /// <summary>자기 속박 예약을 꺼내고 비운다. 없으면 false이며 상태는 그대로다.</summary>
        internal bool TryTakeSelfImmobilize(out int turns)
        {
            turns = selfImmobilize.Turns;
            if (turns <= 0)
            {
                return false;
            }

            selfImmobilize = default;
            return true;
        }

        /// <summary>
        /// 도발 강화 예약을 꺼내고 비운다. 없으면 false이며 상태는 그대로다.
        /// ⚠️ 판정 기준이 <c>Turns</c>가 아니라 <c>Amount</c>인 것은 예전 코드 그대로다 —
        /// 강화량 0으로 예약되면 지속 턴만 남아 다음 예약까지 살아남는다(관찰된 적 없는 구석이지만
        /// 동작 무변경을 위해 보존한다). 정리한다면 T7의 순서·수명 판정과 함께다.
        /// </summary>
        internal bool TryTakeProvokeStrength(out int amount, out int turns)
        {
            amount = provokeStrength.Amount;
            turns = provokeStrength.Turns;
            if (amount <= 0)
            {
                return false;
            }

            provokeStrength = default;
            return true;
        }

        /// <summary>다음 턴 기 차감을 꺼내고 비운다(1회성 예약).</summary>
        internal int TakeNextTurnKiPenalty()
        {
            var taken = nextTurnKiPenalty;
            nextTurnKiPenalty = 0;
            return taken;
        }

        // ------------------------------------------------------------------ 턴 한정 보정

        /// <summary>이동 사거리에 얹히는 턴 한정 보정. 턴 시작에 <see cref="ClearMovementRangeModifier"/>로 지워진다.</summary>
        internal int MovementRangeModifier => activeMovementRangeModifier;

        internal void ClearMovementRangeModifier()
        {
            activeMovementRangeModifier = 0;
        }

        // ------------------------------------------------------------------ 몬스터 행동 경계 (수명이 다르다)

        /// <summary>D02·D05가 이번 몬스터 행동 동안 들어오는 피해를 0으로 만드는가.</summary>
        internal bool IncomingDamageNullifiedThisMonsterAction => incomingDamageNullifiedThisMonsterAction;

        internal void NullifyIncomingDamageThisMonsterAction()
        {
            incomingDamageNullifiedThisMonsterAction = true;
        }

        /// <summary>
        /// 🔴 행동 경계 리셋. 턴 경계 소진(<c>Take*</c>)과 <b>섞지 않는다</b> — 수명이 다르다.
        /// </summary>
        internal void ResetPerMonsterAction()
        {
            incomingDamageNullifiedThisMonsterAction = false;
        }

        // ------------------------------------------------------------------ 세이브

        /// <summary>
        /// 🔑 세이브 <b>직렬화 형태는 그대로 둔다</b> — <see cref="CombatSuspendData"/>의 필드 이름이
        /// 바뀌면 기존 세이브가 깨진다. 바뀐 것은 <b>누가 그 필드들을 채우는가</b>뿐이다:
        /// 예전에는 <c>CreateSuspendSnapshot</c>이 8줄을 손으로 나열했고, 새 예약을 추가하고
        /// 거기 안 적으면 <b>컴파일은 되고 세이브만 조용히 깨졌다.</b> 이제 예약을 늘리면
        /// 이 두 메서드가 바로 옆에 있어 빠뜨릴 자리가 없다.
        /// </summary>
        internal void WriteTo(CombatSuspendData data)
        {
            data.PendingMovementRangeBonus = agility.Amount;
            data.PendingMovementRangeBonusTurns = agility.Turns;
            data.PendingSelfImmobilizeTurns = selfImmobilize.Turns;
            data.PendingProvokeStrengthAmount = provokeStrength.Amount;
            data.PendingProvokeStrengthTurns = provokeStrength.Turns;
            data.PendingNextTurnKiPenalty = nextTurnKiPenalty;
            data.ActiveMovementRangeModifier = activeMovementRangeModifier;
        }

        /// <inheritdoc cref="WriteTo"/>
        internal void ReadFrom(CombatSuspendData data)
        {
            agility = new Booking(data.PendingMovementRangeBonus, data.PendingMovementRangeBonusTurns);
            selfImmobilize = new Booking(0, data.PendingSelfImmobilizeTurns);
            provokeStrength = new Booking(data.PendingProvokeStrengthAmount, data.PendingProvokeStrengthTurns);
            nextTurnKiPenalty = data.PendingNextTurnKiPenalty;
            activeMovementRangeModifier = data.ActiveMovementRangeModifier;
        }
    }
}
