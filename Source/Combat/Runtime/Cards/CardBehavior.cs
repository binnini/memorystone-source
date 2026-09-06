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
            nameof(HasUtilityEffect), nameof(TryApplyUtility), nameof(ApplyAfterScoutReveal), nameof(GetAttackDamage),
            nameof(DisposeAfterPlay), nameof(GetRestriction)
            // Upgrade(연마)는 정의 시점 치환이라 「사용 경로의 고유 규칙」 readout(HasCustomRules)에는 세지 않는다.
        };

        private bool? hasCustomRules;

        /// <summary>카드 id("A00"). 레지스트리 키이자 cards.csv·VFX 큐·세이브가 쓰는 유일한 식별자.</summary>
        public abstract string Id { get; }

        /// <summary>
        /// <summary>공격이 맞은 뒤의 후속 규칙(옛 cards.csv `postActions`). 표식·속박·전염·복사 주입. 기본 없음.</summary>
        public virtual IReadOnlyList<CardBehaviorMetadata.PostAction> PostActions => Array.Empty<CardBehaviorMetadata.PostAction>();

        /// <summary>
        /// 기력 외 추가 비용(옛 `additionalCost`): <see cref="CardBehaviorMetadata.AdditionalCostDiscardSelectedHandCards"/> ·
        /// <see cref="CardBehaviorMetadata.AdditionalCostExileSelectedHandCards"/> 중 하나이거나 빈 문자열(없음).
        /// </summary>
        public virtual string AdditionalCost => string.Empty;

        /// <summary>갈림길 카드의 선택지(옛 `choiceOptions`). 문안은 데이터(cards.csv)에 남고 여기는 규칙만.</summary>
        public virtual IReadOnlyList<CardBehaviorMetadata.ChoiceOption> Choices => Array.Empty<CardBehaviorMetadata.ChoiceOption>();

        /// <summary>
        /// 카드 수치(Amount)가 damage/shield/heal이 아니라 <c>duration</c> 컬럼에서 오는가 — U04 호롱불(「지속시간 = 초기 반경」).
        /// 임포터가 묻는다; 옛 behaviorId 유도(utility.torch → duration)를 규칙 선언으로 옮긴 것.
        /// </summary>
        public virtual bool AmountFollowsDuration => false;

        /// <summary>선택지 「행동 부적 뽑기」가 뽑는 장수(옛 `behaviorParams=drawCount:n`). 그 선택지가 없는 카드는 0.</summary>
        public virtual int ChoiceDrawCount(CardDefinition card) => 0;

        /// <summary>
        /// 사용 뒤 이 카드가 가는 더미(옛 cards.csv `exhaustOnPlay`, P3 소멸 통일). 기본 버림. X04 빚 문서만 소멸.
        /// 도감 칩·툴팁이 묻는 <b>선언</b>이며, 상태에 따라 갈리는 카드는 <see cref="DisposeAfterPlay"/>를 override한다.
        /// 다른 카드를 소멸시키는 비용(A10 제물·D05 부적 방패)은 이 선언이 아니라 그 카드의 규칙 안에 있다.
        /// </summary>
        public virtual CardDisposal Disposal => CardDisposal.Discard;

        /// <summary>
        /// 턴 종료 시 손에 남는가(옛 `retainOnTurnEnd`, T5-2 「유지」). D07 만반의 준비·M07 지름길.
        /// 유지 카드는 정원 밖이며 다음 턴 드로우를 깎지 않는다(2026-09-02 #6).
        /// </summary>
        public virtual bool RetainOnTurnEnd => false;

        /// <summary>
        /// 기절 중에도 낼 수 있는가(옛 `usableWhileStunned`). U03 정화 뽑기·D06 입원 — 기절 뒤에 잠긴 정화는 기절을 못 푼다.
        /// 무장 해제에는 적용하지 않는다(그 컬럼이 두 규칙을 뜻하지 않도록, DEC-2026-09-05-06).
        /// </summary>
        public virtual bool UsableWhileStunned => false;

        /// <summary>
        /// 이 카드의 문안에 걸리는 게임 키워드(원형, game_keywords.csv `키워드` 컬럼) — P5 키워드 명시화.
        /// <c>CardKeywordDecorator</c>는 카드 문안에서 <b>여기 선언된 키워드만</b> 강조·링크한다(부분 문자열 우연 매칭 금지).
        /// 선언과 문안의 정합은 <c>CardKeywordTextBindingTests</c>가 양방향으로 감사한다.
        /// </summary>
        public virtual IReadOnlyList<string> Keywords => Array.Empty<string>();

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
        /// 사용이 끝난 카드가 갈 더미. 기본은 <see cref="Disposal"/> 선언 그대로 — 상태에 따라 갈리는 카드만 override한다.
        /// <see cref="CombatState"/>의 단일 배출 지점(<c>ConsumePlayedCard</c>)이 묻는다.
        /// </summary>
        public virtual CardDisposal DisposeAfterPlay(CombatState state, CardDefinition card)
        {
            return Disposal;
        }

        /// <summary>
        /// 이 카드를 지금 낼 수 없는 이유(없으면 <see cref="CombatState.CardRestriction.None"/>). 기본은 <see cref="CombatState"/>의 공통 게이트
        /// (상태 카드·봉인·기절·속박·무장 해제)에 <see cref="UsableWhileStunned"/> 면제를 얹은 것. 실행 검증·UI usable·회색 라벨
        /// 세 면이 전부 이 한 곳을 지난다(DEC-2026-09-05-06) — 카드 고유 제한을 더하려면 여기를 override한다.
        /// </summary>
        public virtual CombatState.CardRestriction GetRestriction(CombatState state, CardDefinition card)
        {
            return state.GetCommonCardRestriction(card, ignoreStun: UsableWhileStunned);
        }

        /// <summary>
        /// 연마(카드 강화, P4 · DEC-2026-09-06-05). <c>null</c> = 연마 불가(옛 card_upgrades.csv에 행이 없던 카드).
        /// 수치 연마는 <c>card.With(amount: 5)</c>처럼 바뀐 축만 적은 복사본을 돌려준다(이동 카드는 range와 amount가 같이 간다).
        /// 효과가 바뀌는 연마는 규칙 훅이 <see cref="CardDefinition.UpgradeLevel"/>로 분기하고 여기서는 <c>card.With()</c>로
        /// 「연마 가능」만 선언한다(U01). 이름의 <c>+</c> 접미와 <c>descriptionUpgraded</c> 문안은 <see cref="CardUpgrades.Resolve"/>가 얹는다.
        /// <paramref name="level"/>은 다단 연마 확장용이며 지금은 1뿐이다(DEC-2026-08-18-02 상한은 로직 게이트).
        /// </summary>
        public virtual CardDefinition Upgrade(CardDefinition card, int level)
        {
            return null;
        }

        /// <summary>연마 후보 판정 — <see cref="Upgrade"/>가 정의를 돌려주는가.</summary>
        public bool CanUpgrade(CardDefinition card)
        {
            return card != null && Upgrade(card, 1) != null;
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
                    // 규칙 훅만 센다 — Id·Keywords 같은 선언 속성의 override는 규칙이 아니다.
                    hasCustomRules = RuleHookNames.Any(name =>
                        GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance)?.DeclaringType != typeof(CardBehavior));
                }

                return hasCustomRules.Value;
            }
        }
    }

    /// <summary>사용된 카드가 가는 더미(<see cref="CardBehavior.Disposal"/>).</summary>
    public enum CardDisposal
    {
        /// <summary>버림 더미 — 재셔플로 돌아온다.</summary>
        Discard,
        /// <summary>소멸 더미 — 이 전투에서 돌아오지 않는다(U02 회수·A13 소멸 스케일의 재료).</summary>
        Exile,
        /// <summary>
        /// 규칙이 이미 배출을 끝냈다 — U01 다시 뽑기처럼 손패 전체를 버리는 카드. 단일 배출 지점은 손을 대지 않는다
        /// (재드로우로 자기 자신이 다시 손에 들어와도 그대로 남는다). <see cref="CardBehavior.Disposal"/> 선언(도감 표기)이 아니라
        /// <see cref="CardBehavior.DisposeAfterPlay"/> 훅에서만 쓴다.
        /// </summary>
        HandledByRule,
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

    /// <summary>
    /// 설치(장판) 카드 공통 기반(`type=필드`). 카드는 어떤 장판을 놓는지(<see cref="FieldKind"/>)만 선언한다 —
    /// 장판의 턴 효과는 <see cref="FieldObjectKind"/>로 디스패치되며 이 클래스 밖에 남는다(장판은 카드가 떠난 뒤에도 산다).
    /// 옛 cards.csv `behaviorId`(field.damage 등)에서 임포터가 유도하던 값이다.
    /// </summary>
    public abstract class FieldObjectCard : CardBehavior
    {
        public abstract CardFieldObjectKind FieldKind { get; }
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
