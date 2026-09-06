using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct CombatCardSnapshot
    {
        public CombatCardSnapshot(
            string id,
            CombatCardKind kind,
            string name,
            string description,
            int value,
            bool isUsable,
            bool isDiscarded,
            string status,
            int cost = 0,
            int range = 0,
            string pile = "",
            string catalogSourceId = "",
            CardUsePhase phaseAvailability = CardUsePhase.Default,
            CardPlayMode playMode = CardPlayMode.ManualTarget,
            CardFieldObjectKind fieldObjectKind = CardFieldObjectKind.None,
            int durationTurns = 0,
            int areaRadius = 0,
            string instanceId = "",
            int upgradeLevel = 0,
            bool isTemporary = false,
            string choiceOptionTexts = "",
            string illustrationId = "",
            int baseCost = int.MinValue,
            CardTargetMode targetMode = CardTargetMode.None,
            bool isStatusCard = false,
            int baseValue = int.MinValue,
            int healValue = int.MinValue)
        {
            HealValue = healValue == int.MinValue ? value : healValue;
            Id = id ?? string.Empty;
            Kind = kind;
            Name = name;
            Description = description;
            Value = value;
            BaseValue = baseValue == int.MinValue ? value : baseValue;
            IsUsable = isUsable;
            IsDiscarded = isDiscarded;
            Status = status;
            Cost = cost;
            BaseCost = baseCost == int.MinValue ? cost : baseCost;
            TargetMode = targetMode;
            Range = range;
            Pile = pile ?? string.Empty;
            CatalogSourceId = catalogSourceId ?? string.Empty;
            PhaseAvailability = phaseAvailability;
            PlayMode = playMode;
            FieldObjectKind = fieldObjectKind;
            DurationTurns = durationTurns;
            AreaRadius = areaRadius < 0 ? 0 : areaRadius;
            InstanceId = string.IsNullOrWhiteSpace(instanceId) ? Id : instanceId;
            UpgradeLevel = upgradeLevel < 0 ? 0 : upgradeLevel;
            IsTemporary = isTemporary;
            ChoiceOptionTexts = choiceOptionTexts ?? string.Empty;
            IllustrationId = illustrationId ?? string.Empty;
            IsStatusCard = isStatusCard;
        }

        public string Id { get; }
        public string InstanceId { get; }
        public string SelectionKey => string.IsNullOrWhiteSpace(InstanceId) ? Id : InstanceId;
        public CombatCardKind Kind { get; }
        public string Name { get; }
        public string Description { get; }
        /// <summary>
        /// 이 카드가 <b>지금</b> 내는 수치(공격 1타 피해 / 획득 방어도). 저작값이 아니라 실효값이며,
        /// 강화·쇠약·허점·유물·지형·표식이 반영돼 있다. 대상 의존분(허점·표식)은 몬스터를 겨눈
        /// 스냅샷에서만 합류한다(D-7).
        /// </summary>
        public int Value { get; }

        /// <summary>회복 축의 값(WS-I I-08). heal 컬럼이 저작된 카드(A03)만 <see cref="Value"/>와 갈리고,
        /// 미저작이면 Value로 폴백한다 — 갈림길 회복 선택지 문안이 이 값을 읽는다.</summary>
        public int HealValue { get; }

        /// <summary>이 카드의 저작 수치 — 런타임 보정 이전의 값(<see cref="BaseCost"/>와 같은 계약).</summary>
        public int BaseValue { get; }

        /// <summary>실효 <see cref="Value"/>가 저작 <see cref="BaseValue"/>와 다른가 = 강조 대상인가.</summary>
        public bool IsValueModified => Value != BaseValue;
        public bool IsUsable { get; }
        public bool IsDiscarded { get; }
        public string Status { get; }
        public int Cost { get; }
        public int KiCost => Cost;

        /// <summary>The card's authored base Ki cost before runtime modifiers (e.g. Final Blow's SpendAll).</summary>
        public int BaseCost { get; }

        /// <summary>True when the effective <see cref="Cost"/> differs from the authored <see cref="BaseCost"/>.</summary>
        public bool IsCostModified => Cost != BaseCost;
        public CardTargetMode TargetMode { get; }
        public int Range { get; }
        public string Pile { get; }
        public string CatalogSourceId { get; }
        public CardUsePhase PhaseAvailability { get; }
        public CardPlayMode PlayMode { get; }
        public CardFieldObjectKind FieldObjectKind { get; }
        public int DurationTurns { get; }
        public int AreaRadius { get; }
        public int UpgradeLevel { get; }
        public bool IsTemporary { get; }
        /// <summary>갈림길 선택지 문안(데이터). 선택지 규칙은 <c>CardBehaviorRegistry.Get(Id).Choices</c>.</summary>
        public string ChoiceOptionTexts { get; }
        public string IllustrationId { get; }

        /// <summary>
        /// 상태 카드(X01~X03)인가 — 저작이 정하는 <b>카드의 영구 속성</b>이지 지금의 상태가 아니다
        /// (근거는 <c>CardEffectType.Status</c>). 손패·덱 목록·보상 어디에 놓이든 값이 같다.
        /// <para>
        /// 예전에는 뷰가 <see cref="Status"/> 문자열이 "사용 불가"인지로 이걸 대신 판정했는데,
        /// 그 라벨은 <b>손패에서만</b> 찍힌다 — 덱 목록 스냅샷의 <see cref="Status"/>에는 더미 이름이
        /// 들어가므로 목록에서는 상태 카드가 평범한 이동 카드로 보였다. 그래서 축을 하나로 모았다.
        /// </para>
        /// </summary>
        public bool IsStatusCard { get; }
    }
}
