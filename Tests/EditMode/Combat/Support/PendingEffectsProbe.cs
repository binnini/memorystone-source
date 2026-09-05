using System.Reflection;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 예약 효과(<c>PendingEffects</c>)를 리플렉션으로 들여다보는 <b>테스트 전용 탐침</b>.
    ///
    /// 🔴 <b>왜 리플렉션인가</b>: 예약은 이름 그대로 <b>다음 경계에서야</b> 발효되므로, 걸린
    /// 직후에는 관찰 가능한 표면이 없다. 「지금 3턴짜리 자기 속박이 예약돼 있다」를 확인할
    /// 공개 API가 없고, 그걸 위해 <b>제품 코드에 조회 API를 새로 뚫는 것은 더 나쁘다</b>
    /// (테스트 때문에 생긴 API는 곧 규칙 코드가 쓰기 시작한다). <c>PendingEffects</c>는
    /// <c>internal</c>이고 이 프로젝트는 <c>InternalsVisibleTo</c>를 새로 설계하지 않는다
    /// (<c>AGENTS.md</c> → Architecture Constraints).
    ///
    /// ⚠️ <b>임시 조치라는 것을 알고 쓴다.</b> 예약의 진짜 계약은 「예약이 얼마인가」가 아니라
    /// <b>「다음 턴 시작에 무엇이 벌어지나」</b>이고, 그건 T7의 행동 계약 테스트가 잠근다.
    /// 그 테스트들이 붙고 나면 여기 탐침은 줄어야 한다 — 늘면 방향이 거꾸로 간 것이다.
    ///
    /// 🔑 리플렉션을 <b>한 곳에 모아</b> 둔 것이 요점이다. 2026-08-31 T6에서 필드를 타입으로
    /// 올렸을 때, 흩어져 있던 <c>GetField("pendingSelfImmobilizeTurns")</c>류가 전부
    /// <c>NullReferenceException</c>으로 터졌다. 다음에 구조가 또 바뀌면 고칠 곳은 이 파일뿐이다.
    /// </summary>
    internal static class PendingEffectsProbe
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>지금 예약돼 있는 자기 속박 턴 수(0이면 예약 없음).</summary>
        internal static int SelfImmobilizeTurns(CombatState state)
        {
            var booking = HiddenField(Pending(state), "selfImmobilize");
            return (int)booking.GetType().GetProperty("Turns", Hidden).GetValue(booking);
        }

        /// <summary>
        /// D02·D05가 켜는 「이번 몬스터 행동 피해 무효」를 켠다. 카드를 실제로 내는 대신
        /// 그 한 비트만 세우고 싶은 테스트용(카드 경로는 별도 테스트가 덮는다).
        /// </summary>
        internal static void NullifyIncomingDamageThisMonsterAction(CombatState state)
        {
            var pending = Pending(state);
            pending.GetType()
                .GetMethod("NullifyIncomingDamageThisMonsterAction", Hidden)
                .Invoke(pending, System.Array.Empty<object>());
        }

        private static object Pending(CombatState state)
        {
            return typeof(CombatState).GetField("pending", Hidden).GetValue(state);
        }

        private static object HiddenField(object target, string name)
        {
            return target.GetType().GetField(name, Hidden).GetValue(target);
        }
    }
}
