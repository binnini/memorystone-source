using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 🔴 <b>T6로 예약 8필드를 타입으로 올려도 <u>기존 세이브가 그대로 열린다</u></b>는 계약.
    ///
    /// 왜 별도로 잠그나(2026-08-31 T6 완료 기준 ④): 예약을 <c>PendingEffects</c>로 묶으면서
    /// <c>CombatSuspendData</c>도 하위 객체로 바꾸고 싶은 유혹이 있다. 그러면 <b>직렬화 형태가
    /// 바뀌어 플레이어가 들고 있던 세이브가 조용히 깨진다</b> — 로드가 예외를 던지는 것도 아니고,
    /// 예약 값만 0으로 읽혀 D04의 자기 속박이 사라지는 식으로 나타난다.
    ///
    /// 그래서 바꾼 것은 <b>누가 그 필드를 채우는가</b>뿐이고(이제 <c>PendingEffects.WriteTo/ReadFrom</c>
    /// 한 쌍), 와이어 포맷은 T6 이전과 <b>글자 그대로 같다</b>. 이 파일이 그걸 증명한다:
    /// ① 예약이 걸린 상태의 스냅샷 JSON이 <b>옛 필드 이름</b>을 그대로 들고 있는가,
    /// ② T6 <b>이전 코드가 썼을 JSON</b>을 그대로 먹여도 예약이 복원되는가.
    ///
    /// ⚠️ 아래 JSON은 손으로 적은 <b>옛 세이브 표본</b>이다. 새 필드가 늘어도 이 표본은 고치지 않는다 —
    /// 옛 세이브에 없던 필드가 기본값으로 들어오는 것이 정확한 마이그레이션이고, 그 성질을 재는 것이
    /// 이 표본의 역할이다.
    /// </summary>
    public sealed class PendingEffectsSaveCompatibilityTests
    {
        private const int BookedSelfImmobilizeTurns = 3;
        private const int BookedProvokeAmount = 25;
        private const int BookedProvokeTurns = 2;

        /// <summary>① 예약이 걸린 상태를 저장하면 옛 필드 이름·값이 그대로 나온다.</summary>
        [Test]
        public void SnapshotStillWritesTheLegacyPendingFieldNames()
        {
            var state = BookedState();

            var json = JsonUtility.ToJson(state.CreateSuspendSnapshot());

            Assert.That(json, Does.Contain($"\"PendingSelfImmobilizeTurns\":{BookedSelfImmobilizeTurns}"));
            Assert.That(json, Does.Contain($"\"PendingProvokeStrengthAmount\":{BookedProvokeAmount}"));
            Assert.That(json, Does.Contain($"\"PendingProvokeStrengthTurns\":{BookedProvokeTurns}"));
            Assert.That(json, Does.Contain("\"PendingMovementRangeBonus\""));
            Assert.That(json, Does.Contain("\"PendingMovementRangeBonusTurns\""));
            Assert.That(json, Does.Contain("\"PendingNextTurnKiPenalty\""));
            Assert.That(json, Does.Contain("\"ActiveMovementRangeModifier\""));
        }

        /// <summary>
        /// ② T6 이전 코드가 썼을 JSON을 그대로 먹인다. 예약이 복원돼야 하고, 복원한 상태를 다시
        /// 저장하면 같은 값이 다시 나와야 한다(읽기와 쓰기가 같은 필드를 본다는 뜻).
        /// </summary>
        [Test]
        public void LegacySaveJsonStillRestoresItsBookings()
        {
            var legacy = JsonUtility.FromJson<CombatSuspendData>(LegacySaveJson);
            Assert.That(legacy, Is.Not.Null, "표본 JSON이 CombatSuspendData로 읽히지 않는다 — 형태가 바뀌었다.");

            var state = FreshState();
            state.RestoreFromSuspend(legacy);

            var rewritten = state.CreateSuspendSnapshot();
            Assert.That(rewritten.PendingSelfImmobilizeTurns, Is.EqualTo(BookedSelfImmobilizeTurns),
                "옛 세이브의 자기 속박 예약이 복원되지 않았다 — D04의 대가가 로드 한 번으로 사라진다.");
            Assert.That(rewritten.PendingProvokeStrengthAmount, Is.EqualTo(BookedProvokeAmount));
            Assert.That(rewritten.PendingProvokeStrengthTurns, Is.EqualTo(BookedProvokeTurns));
            Assert.That(rewritten.PendingMovementRangeBonus, Is.EqualTo(2));
            Assert.That(rewritten.PendingMovementRangeBonusTurns, Is.EqualTo(1));
            Assert.That(rewritten.PendingNextTurnKiPenalty, Is.EqualTo(4));
            Assert.That(rewritten.ActiveMovementRangeModifier, Is.EqualTo(0));
        }

        /// <summary>
        /// ③ 복원된 예약이 <b>실제로 발효된다</b>. 필드가 채워졌다는 것만으로는 부족하다 —
        /// 소진 경로가 새 타입을 지나므로, 로드한 세이브에서 턴을 넘겼을 때 속박이 걸려야 한다.
        /// </summary>
        [Test]
        public void RestoredBookingStillFiresAtTheNextTurnStart()
        {
            // 실제 저장 → 파일화(JSON 문자열) → 로드 경로를 그대로 탄다. 손으로 적은 표본은 예약
            // 필드가 평면으로 남아 있는지(위 두 테스트)를 재고, 여기서는 그 값이 살아서 발효되는지를 잰다.
            var json = JsonUtility.ToJson(BookedState().CreateSuspendSnapshot());
            var state = FreshState();
            state.RestoreFromSuspend(JsonUtility.FromJson<CombatSuspendData>(json));

            Assert.That(PendingEffectsProbe.SelfImmobilizeTurns(state), Is.EqualTo(BookedSelfImmobilizeTurns),
                "전제: 로드 직후 예약이 살아 있다.");

            AdvanceOneOverallTurn(state);

            Assert.That(PendingEffectsProbe.SelfImmobilizeTurns(state), Is.EqualTo(0),
                "예약은 소진되면서 비워져야 한다 — 남으면 다음 턴에 또 걸린다.");
            Assert.That(
                state.ActiveEffects.Any(effect =>
                    effect.Kind == StatusEffectKind.Immobilize && effect.TargetUnitId == "player"),
                Is.True,
                "옛 세이브에서 이어받은 자기 속박이 다음 턴 시작에 걸리지 않았다.");
        }

        // ------------------------------------------------------------------ 셋업

        private static CombatState FreshState()
        {
            return CombatStateFixture.Arena(3).WithEnemyEastAt(2).Build();
        }

        private static CombatState BookedState()
        {
            var state = FreshState();
            state.ScheduleProvokeStrength(BookedProvokeAmount, BookedProvokeTurns);
            typeof(CombatState)
                .GetMethod("SchedulePlayerDelayedImmobilize",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(state, new object[] { BookedSelfImmobilizeTurns });
            return state;
        }

        private static void AdvanceOneOverallTurn(CombatState state)
        {
            var turn = state.OverallTurnNumber;
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(state.OverallTurnNumber, Is.GreaterThan(turn), "전제: 턴이 실제로 넘어갔다.");
        }

        /// <summary>
        /// T6 <b>이전</b> 스키마로 쓰인 최소 세이브. 예약 7필드가 전부 최상위 평면 필드다 —
        /// 그게 이 테스트가 지키려는 형태다.
        /// </summary>
        private const string LegacySaveJson =
            "{\"OverallTurn\":5,\"Phase\":0,\"ActionCostRemaining\":4,\"RevealedFastTurtleDistance\":0," +
            "\"PendingMovementRangeBonus\":2,\"PendingMovementRangeBonusTurns\":1," +
            "\"PendingSelfImmobilizeTurns\":3," +
            "\"PendingProvokeStrengthAmount\":25,\"PendingProvokeStrengthTurns\":2," +
            "\"PendingNextTurnKiPenalty\":4,\"ActiveMovementRangeModifier\":0," +
            "\"LastMovedDistance\":0,\"ActionCardsUsedThisTurn\":0,\"TotalActionCardsUsed\":0," +
            "\"BagAttackBonusThisTurn\":0,\"DefensiveCardUsedThisTurn\":false,\"ObjectiveCompleted\":false," +
            "\"MarkedMonsterId\":\"\",\"SealedBossArenaId\":\"\"}";
    }
}
