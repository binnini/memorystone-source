using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>
    /// 카드 한 장의 규칙(카드 클래스 전환 트랙 · DEC-2026-09-06-01). 인스턴스가 아니라 <b>종류</b>당 하나이며 상태를 갖지 않는다.
    /// 연출을 모른다 — 효과는 지금처럼 <see cref="CombatState"/>의 규칙 메서드가 <c>RaiseEffect</c>로 버퍼에 쌓고
    /// 어셈블러·스케줄러가 재생한다(규칙/연출 분리 불변).
    ///
    /// 훅은 <see cref="CombatState"/>의 카드 사용 경로가 카드 고유 규칙을 묻는 진입점 5종을 그대로 비춘다.
    /// 기본 구현은 「고유 규칙 없음」 — 이동은 요청 그대로, 방어는 <c>block += Amount</c>, 유틸리티는 U01 재드로우,
    /// 정찰은 탐색만, 공격은 <c>Amount + 보너스</c>. 카드 클래스가 override한 훅만 카드 고유 규칙이다.
    /// </summary>
    public abstract class CardBehavior
    {
        private static readonly string[] RuleHookNames =
        {
            nameof(TryResolveMoveDestination), nameof(ApplyAfterMoveResolved), nameof(TryApplyDefend),
            nameof(HasUtilityEffect), nameof(TryApplyUtility), nameof(ApplyAfterScoutReveal), nameof(GetAttackDamage)
        };

        private bool? hasCustomRules;

        /// <summary>카드 id("A00"). 레지스트리 키이자 cards.csv·VFX 큐·세이브가 쓰는 유일한 식별자.</summary>
        public abstract string Id { get; }

        /// <summary>
        /// 이 카드가 효과를 올릴 때(<c>RaiseEffect</c>·상태이상 sourceRef) 쓰는 키. 기본은 카드 id. 옛 behaviorId 문자열에
        /// VFX 큐·오디오·<c>CombatEffectSourceClassifier</c>가 매칭하므로 출하 카드는 그 문자열을 그대로 돌려준다 —
        /// 큐 CSV가 카드 id로 이관되면 override를 지우면 된다. 카드 <b>식별</b>에는 쓰지 않는다(<see cref="CardIds"/>).
        /// </summary>
        public virtual string EffectSourceRef => Id;

        /// <summary>공격이 맞은 뒤의 후속 규칙(옛 cards.csv `postActions`). 표식·속박·전염·복사 주입. 기본 없음.</summary>
        public virtual IReadOnlyList<CardBehaviorMetadata.PostAction> PostActions => Array.Empty<CardBehaviorMetadata.PostAction>();

        /// <summary>
        /// 기력 외 추가 비용(옛 `additionalCost`): <see cref="CardBehaviorMetadata.AdditionalCostDiscardSelectedHandCards"/> ·
        /// <see cref="CardBehaviorMetadata.AdditionalCostExileSelectedHandCards"/> 중 하나이거나 빈 문자열(없음).
        /// </summary>
        public virtual string AdditionalCost => string.Empty;

        /// <summary>갈림길 카드의 선택지(옛 `choiceOptions`). 문안은 데이터(cards.csv)에 남고 여기는 규칙만.</summary>
        public virtual IReadOnlyList<CardBehaviorMetadata.ChoiceOption> Choices => Array.Empty<CardBehaviorMetadata.ChoiceOption>();

        /// <summary>선택지 「행동 부적 뽑기」가 뽑는 장수(옛 `behaviorParams=drawCount:n`). 그 선택지가 없는 카드는 0.</summary>
        public virtual int ChoiceDrawCount => 0;

        /// <summary>자기 대상 선택지가 하나라도 있는가 — 사거리 안에 적이 없어도 사용 가능하게 두는 판정.</summary>
        public bool HasSelfTargetedChoice => Choices.Any(option => string.Equals(option.Target, CardBehaviorMetadata.ChoiceTargetSelf, StringComparison.OrdinalIgnoreCase));

        /// <summary>이동 카드: 요청 목적지를 카드 규칙으로 바꾼다(M06 무작위 이동 등). 기본은 요청 그대로.</summary>
        public virtual bool TryResolveMoveDestination(CombatState state, CardDefinition card, int effectiveRange, HexCoord requested, out HexCoord destination, out string failureReason)
        {
            destination = requested;
            failureReason = string.Empty;
            return true;
        }

        /// <summary>이동 카드: 이동이 확정된 뒤의 후속 규칙(M05 추진력 등).</summary>
        public virtual void ApplyAfterMoveResolved(CombatState state, CardDefinition card)
        {
            state.ClearPendingMovementRangeBonus();
        }

        /// <summary>
        /// 방어 카드: 카드 고유 방어 규칙을 적용하고 부여한 방어막을 돌려준다. <c>false</c>면 호출자가
        /// 기본 경로(<c>block += Amount</c>)를 탄다. 방어막을 주지 않는 카드(D02 무적·D03 반사)는 true + 0.
        /// </summary>
        public virtual bool TryApplyDefend(CombatState state, CardDefinition card, out int blockGranted)
        {
            blockGranted = 0;
            return false;
        }

        /// <summary>유틸리티 카드: 고유 규칙이 있는가(없으면 U01 재드로우 기본 경로).</summary>
        public virtual bool HasUtilityEffect(CombatState state, CardDefinition card)
        {
            return false;
        }

        /// <summary>유틸리티 카드: 고유 규칙 적용. <c>false</c>면 기본 경로.</summary>
        public virtual bool TryApplyUtility(CombatState state, CardDefinition card)
        {
            return false;
        }

        /// <summary>정찰 카드: 탐색이 끝난 뒤의 후속 규칙(S01 드러난 적 피해 등).</summary>
        public virtual void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
        {
        }

        /// <summary>공격 카드: 1타 피해. <paramref name="attackBonus"/>에는 평면 보너스가 이미 합산돼 있다. <paramref name="target"/>는 조준 칸(A12 전염병이 대상의 상태이상을 본다).</summary>
        public virtual int GetAttackDamage(CombatState state, CardDefinition card, int attackBonus, HexCoord target)
        {
            return card.Amount + attackBonus;
        }

        /// <summary>
        /// 이 클래스가 훅을 하나라도 override했는가 — 샌드박스 「핸들러/기본」 readout용. 기본 경로로 도는 카드가
        /// 대부분이므로 false는 오류가 아니고, 방금 규칙을 쓴 카드가 false면 override 시그니처가 틀린 것이다.
        /// </summary>
        public bool HasCustomRules
        {
            get
            {
                if (!hasCustomRules.HasValue)
                {
                    // 규칙 훅만 센다 — Id·EffectSourceRef 같은 선언 속성의 override는 규칙이 아니다.
                    hasCustomRules = RuleHookNames.Any(name =>
                        GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance)?.DeclaringType != typeof(CardBehavior));
                }

                return hasCustomRules.Value;
            }
        }
    }

    /// <summary>
    /// 카탈로그에 없는 카드(테스트 픽스처·개발용 샘플)가 받는 동작 — 기본 경로. 출하 카드가 여기로 떨어지는 것은
    /// <c>CardBehaviorRegistryShippingTests</c>가 막는다 — 조용한 폴백 금지(S11)의 감시자.
    /// </summary>
    internal sealed class UnregisteredCardBehavior : CardBehavior
    {
        public override string Id => string.Empty;
    }

    /// <summary>공격 카드 공통 기반(cards.csv `type=공격`).</summary>
    public abstract class BasicAttackCard : CardBehavior
    {
    }

    /// <summary>이동 카드 공통 기반(`type=이동`).</summary>
    public abstract class BasicMoveCard : CardBehavior
    {
    }

    /// <summary>방어 카드 공통 기반(`type=방어`).</summary>
    public abstract class BasicBlockCard : CardBehavior
    {
    }

    /// <summary>설치(장판) 카드 공통 기반(`type=필드`). 장판의 턴 효과는 <see cref="FieldObjectKind"/>로 디스패치되며 이 클래스 밖에 남는다.</summary>
    public abstract class FieldObjectCard : CardBehavior
    {
    }

    /// <summary>정찰 카드 공통 기반(`type=정찰`).</summary>
    public abstract class ScoutCard : CardBehavior
    {
    }

    /// <summary>유틸리티 카드 공통 기반(`type=유틸리티`).</summary>
    public abstract class UtilityCard : CardBehavior
    {
    }

    /// <summary>
    /// 저주(상태 카드, `type=저주`) 공통 기반. 사용 불가이며 손패·턴말·드로우 술어는 D-7에 따라
    /// 이 트랙에서는 <see cref="CombatState"/>에 남는다(키만 카드 id).
    /// </summary>
    public abstract class StatusCard : CardBehavior
    {
    }
}
