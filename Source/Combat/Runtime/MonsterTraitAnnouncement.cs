using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 몬스터 <b>특성</b>이 발동한 순간의 알림 어휘(2026-09-01 사용자 확정). 규칙층이 내는 신호의
    /// source ref와, 표현층이 띄우는 한국어 문안을 <b>한 곳</b>에 둔다.
    ///
    /// <para>🔴 어휘를 두 벌로 두지 않는 것이 이 파일의 존재 이유다. 유물·소모품 트랙에서 도감이
    /// 별도 문안 표를 들었다가 사이드바와 갈린 전례가 있다 — 규칙층이 ref만 던지고 표현층이 자기
    /// 문자열을 들면 정확히 같은 사고가 난다. 던지는 쪽과 읽는 쪽이 <b>이 표 하나</b>를 지난다.</para>
    ///
    /// <para>🔑 「특성」은 상태이상이 아니다(2026-08-10 확정 어휘). 배지가 들고 있는 다섯 —
    /// 약오름·맷집·견고·뒤끝·은신 — 이 전부이고, 이 표의 항목도 정확히 그 다섯을 따른다.
    /// 새 특성을 만들면 배지·툴팁과 함께 여기에도 한 줄이 는다.</para>
    ///
    /// <para>🔑 <b>은신 해제만 kind가 다르다.</b> 노출은 이미 <see cref="EffectKind.FogReveal"/>로
    /// 신호를 내고 전용 VFX까지 붙어 있어서(「드러나는 순간은 체감의 절반이다」), 새 kind를 만드는
    /// 대신 그 이벤트의 <b>문안만</b> 갈아 끼운다 — 전염·뒤끝 접두와 같은 문법이다. 나머지 특성은
    /// 실을 이벤트가 없어 <see cref="EffectKind.MonsterTraitTriggered"/>(텍스트 전용)로 나간다.</para>
    /// </summary>
    public static class MonsterTraitAnnouncement
    {
        // ── 은신 ────────────────────────────────────────────────────────────────
        /// <summary>다시 숨었다(노출 시간이 다 됐거나 흩어지기로 즉시 복귀).</summary>
        public const string StealthHiddenRef = "trait.stealth.hidden";

        // 노출 쪽 ref는 CombatState가 이미 들고 있다(FogReveal의 source) — 여기서 재정의하지 않고
        // 그 상수를 그대로 읽어 문안만 붙인다. 두 벌이 되는 순간 「공격으로 드러남」과 「정찰로 드러남」이
        // 서로 다른 말을 하게 된다.

        // ── 약오름 ──────────────────────────────────────────────────────────────
        /// <summary>스택이 올랐다. amount = 오른 뒤의 총 스택.</summary>
        public const string AgitationGainedRef = "trait.agitation.gained";

        /// <summary>스택이 0으로 내려갔다 — 「가라앉았다」가 정보다(다시 안전해졌다는 뜻이 아니라 피해가 줄었다는 뜻).</summary>
        public const string AgitationClearedRef = "trait.agitation.cleared";

        // ── 홀림(오라 봉인 · 2026-09-04) ────────────────────────────────────────
        /// <summary>그슨새가 오라 안으로 들어와 카드가 잠겼다. amount = 봉인 장수. 해제는 상태 만료
        /// 신호가 말하므로 별도 항목이 없다(「전이만 알린다」 규약 — 진입 전이가 곧 특성 발동이다).</summary>
        public const string AuraSealAppliedRef = "trait.aura.seal";

        // ── 소매치기(2026-09-04) ────────────────────────────────────────────────
        /// <summary>엽전을 훔쳤다. amount = 이번에 훔친 액수(부분 절도면 그만큼만).</summary>
        public const string PickpocketRef = "trait.pickpocket";

        // ── 지대(요괴 §4-3 · 두억시니 · 2026-09-05) ──────────────────────────────
        /// <summary>
        /// 상태이상 지대를 깔았다(사용자 요구: "두억시니가 지대 기믹을 실행할 때 플로팅 텍스트가
        /// 표시되어야함 — '~~ 지대 생성!' 이런식으로").
        ///
        /// <para>🔴 <b>배치 VFX 이벤트에 문안을 실을 수 없다.</b> 그 이벤트는 <c>targetUnitId="field"</c>·
        /// 수치 0이라 <c>ShouldShowFloatingText</c>의 「칸에 그림만 얹는 배치 큐」 게이트에 걸려
        /// 통째로 침묵한다(폭탄·섬광 장판과 같은 자리). 그래서 알림은 특성 알림 채널로 <b>따로</b>
        /// 나가고, 그림은 그대로 배치 큐가 그린다 — 두 축을 섞지 않는다.</para>
        ///
        /// <para>🔑 어느 지대인지는 이벤트의 <c>StatusKind</c>가 말한다. 문안 조립은 여기 한 곳뿐이다.</para>
        /// </summary>
        public const string StatusZoneCreatedRef = "trait.zone.created";

        // ── 맷집 ────────────────────────────────────────────────────────────────
        /// <summary>첫 피격을 반감하고 소진됐다.</summary>
        public const string ToughnessAbsorbedRef = "trait.toughness.absorbed";

        /// <summary>재장전이 끝나 다시 준비됐다.</summary>
        public const string ToughnessReloadedRef = "trait.toughness.reloaded";

        // ── 견고 ────────────────────────────────────────────────────────────────
        /// <summary>턴 소멸을 받지 않고 방어막이 남았다. amount = 남은 방어막.</summary>
        public const string SturdyKeptRef = "trait.sturdy.kept";
        // ── 수호 재충전(2026-09-05 결정 1) ─────────────────────────────────────
        /// <summary>주기가 차서 수호 충전을 1 얻었다. amount = 충전 후 보유 수.</summary>
        public const string GuardRechargedRef = "trait.guard.recharged";

        // ── 뒤끝 ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 죽은 자리에 마지막 수를 남겼다 — <b>무엇을</b>은 말하지 않는 갈래(디버프).
        /// 디버프는 결과가 플레이어 쪽에 "뒤끝! {상태}"로 이미 뜨므로, 시체 자리에서까지 상태 이름을
        /// 말하면 같은 말을 두 번 한다. 여기서는 <b>원인</b>만 가리킨다.
        /// </summary>
        public const string AftermathRef = "trait.aftermath.triggered";

        /// <summary>심술 — 저주 부적을 덱에 남겼다(2026-09-01 #3: 「뒤끝!」만으로는 뭘 당했는지 모른다).</summary>
        public const string AftermathCurseRef = "trait.aftermath.curse";

        /// <summary>독기 장판을 남겼다.</summary>
        public const string AftermathFieldRef = "trait.aftermath.field";

        /// <summary>쓰러지며 터졌다.</summary>
        public const string AftermathBlastRef = "trait.aftermath.blast";

        /// <summary>훔친 것을 돌려줬다 — 뒤끝의 유일한 이로운 갈래.</summary>
        public const string AftermathRestoreRef = "trait.aftermath.restore";

        /// <summary>
        /// 이 뒤끝 갈래가 시체 자리에 띄울 ref. 규칙층만 부른다(<c>MonsterDeathAftermathKind</c>가
        /// internal이라 표현층은 ref 문자열만 본다) — 그래서 <b>문안 표는 여전히 한 벌</b>이다.
        /// </summary>
        internal static string RefForAftermath(MonsterDeathAftermathKind kind)
        {
            switch (kind)
            {
                case MonsterDeathAftermathKind.Curse: return AftermathCurseRef;
                case MonsterDeathAftermathKind.Field: return AftermathFieldRef;
                case MonsterDeathAftermathKind.Blast: return AftermathBlastRef;
                case MonsterDeathAftermathKind.Restore: return AftermathRestoreRef;
                default: return AftermathRef;
            }
        }

        /// <summary>
        /// 이 이벤트가 특성 알림인가(kind가 전용이거나, 은신 노출처럼 기존 kind를 빌려 쓴 것이거나).
        ///
        /// <para>🔴 쓰는 자리는 <b>연출층의 은폐 필터</b>다. 숨은 몬스터를 겨눈 이벤트는 통째로 삼켜지는데,
        /// 그 규칙을 특성 알림에까지 적용하면 <b>노출 알림이 노출되지 않는다</b> — 은신 공격으로 드러나는
        /// 순간의 「들킴!」이 정확히 그 자리에서 죽는다. 알림을 띄울지는 규칙층
        /// (<c>RaiseMonsterTraitAnnouncement</c>)이 안개·은신을 보고 이미 정했으므로, 두 번 거르면
        /// 늦은 쪽이 이긴다.</para>
        /// </summary>
        public static bool IsAnnouncement(EffectResultEvent resultEvent)
        {
            return resultEvent.Kind == EffectKind.MonsterTraitTriggered
                   || IsStealthReveal(resultEvent.SourceRef);
        }

        /// <summary>은신 노출(공격 해소·정찰 명중) — 문안만 빌려 쓰는 FogReveal 이벤트다.</summary>
        public static bool IsStealthReveal(string sourceRef)
        {
            return string.Equals(sourceRef, CombatState.StealthAttackRevealRef, StringComparison.Ordinal)
                   || string.Equals(sourceRef, CombatState.StealthScoutRevealRef, StringComparison.Ordinal);
        }

        /// <summary>
        /// 이 source ref가 특성 알림인가, 그렇다면 무엇이라 띄우는가.
        /// <paramref name="amount"/>는 항목에 따라 스택·잔량이고, 쓰지 않는 항목은 무시한다.
        /// </summary>
        public static bool TryGetText(string sourceRef, int amount, out string text)
        {
            return TryGetText(sourceRef, amount, null, out text);
        }

        /// <summary>
        /// 상태이상 축을 함께 보는 갈래(지대 생성). <paramref name="statusKind"/>를 모르면 종류를
        /// 빼고 「지대 생성!」으로 내려앉는다 — 이름 없는 지대라도 <b>생겼다는 사실</b>은 알린다.
        /// </summary>
        public static bool TryGetText(string sourceRef, int amount, StatusEffectKind? statusKind, out string text)
        {
            text = string.Empty;
            if (string.IsNullOrEmpty(sourceRef))
            {
                return false;
            }

            switch (sourceRef)
            {
                case StealthHiddenRef:
                    text = "은신!";
                    return true;

                // 노출 — 공격으로 드러났든 정찰에 걸렸든 플레이어가 읽을 사실은 하나다("이제 보인다").
                case CombatState.StealthAttackRevealRef:
                case CombatState.StealthScoutRevealRef:
                    text = "들킴!";
                    return true;

                case AgitationGainedRef:
                    text = $"약오름 {Math.Max(0, amount)}";
                    return true;
                case AgitationClearedRef:
                    text = "약오름 해소";
                    return true;

                case AuraSealAppliedRef:
                    text = "홀림! 부적 봉인";
                    return true;

                case PickpocketRef:
                    text = $"소매치기! 엽전 -{Math.Max(0, amount)}";
                    return true;

                case StatusZoneCreatedRef:
                    text = statusKind.HasValue
                        ? $"{StatusEffectInfo.DisplayName(statusKind.Value)} 지대 생성!"
                        : "지대 생성!";
                    return true;

                case ToughnessAbsorbedRef:
                    text = "맷집!";
                    return true;
                case ToughnessReloadedRef:
                    text = "맷집 회복";
                    return true;

                case SturdyKeptRef:
                    text = "견고!";
                    return true;
                // ── 보스 철조각(2026-09-05 Q20 확정 문안) — 연출 큐와 별개의 알림 채널(BossPropAnnouncements).
                case BossPropAnnouncements.VolleyRef:
                    text = $"철조각 ×{Math.Max(0, amount)}";
                    return true;
                case BossPropAnnouncements.AbsorbedRef:
                    text = $"+{Math.Max(0, amount)} 흡수";
                    return true;
                case BossPropAnnouncements.TrapsPlacedRef:
                    text = "함정 설치!";
                    return true;
                case GuardRechargedRef:
                    text = "수호 충전";
                    return true;

                case AftermathRef:
                    text = "뒤끝!";
                    return true;
                case AftermathCurseRef:
                    text = "저주 부적 부여!";
                    return true;
                case AftermathFieldRef:
                    text = "독기 장판!";
                    return true;
                case AftermathBlastRef:
                    text = "터짐!";
                    return true;
                case AftermathRestoreRef:
                    text = "되돌려줌!";
                    return true;

                default:
                    return false;
            }
        }
    }
}
