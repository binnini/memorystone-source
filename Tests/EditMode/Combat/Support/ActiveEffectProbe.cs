using System.Reflection;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 지금 걸려 있는 상태이상 보관소(<c>CombatState.activeEffects</c>)에 닿는
    /// <b>테스트 전용 탐침</b>. <see cref="PendingEffectsProbe"/>와 같은 역할·같은 이유다.
    ///
    /// 🔴 <b>왜 필요한가</b>: 테스트는 부여 <b>관문</b>(제어 면역 백스톱·수호 소비)을 일부러
    /// 우회해 「이미 걸려 있는 상태」를 세우고 싶을 때가 많다. 공개 부여 API로는 그 상태를
    /// 못 만들고, 그것 때문에 제품 코드에 주입 API를 뚫는 것은 더 나쁘다.
    ///
    /// 🔑 <b>리플렉션을 한 곳에 모은 것이 요점이다.</b> 2026-08-31 P3에서 필드를 타입으로
    /// 올렸을 때, 11개 테스트 파일이 각자 손으로 적어 둔
    /// <c>(List&lt;ActiveEffect&gt;)GetField("activeEffects")</c>가 전부
    /// <c>InvalidCastException</c>으로 터졌다(64건). 같은 사고가 T6에서도 한 번 있었다.
    /// 다음에 구조가 또 바뀌면 고칠 곳은 이 파일 하나다.
    /// </summary>
    internal static class ActiveEffectProbe
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>보관소 자체. 테스트가 직접 담고 빼야 할 때만 쓴다.</summary>
        internal static ActiveEffectRegistry Registry(CombatState state)
        {
            return (ActiveEffectRegistry)typeof(CombatState)
                .GetField("activeEffects", Hidden)
                .GetValue(state);
        }

        /// <summary>
        /// 지속형 상태이상 하나를 <b>관문을 지나지 않고</b> 그대로 심는다.
        /// 부여 규칙(병합·면역·수호)을 재는 테스트는 이걸 쓰면 안 된다 — 그건 공개 경로로 재야 한다.
        /// </summary>
        internal static void Inject(
            CombatState state,
            StatusEffectKind kind,
            string unitId,
            int remainingTurns,
            int amount = 0,
            string sourceRef = "test")
        {
            Registry(state).Add(new ActiveEffect(EffectType.Duration, kind, unitId, remainingTurns, amount, sourceRef));
        }
    }
}
