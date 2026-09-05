using System;

namespace SeoulPlayup.CardCore
{
    public sealed class CardDefinition
    {
        public CardDefinition(
            string id,
            string displayName,
            CardCategory category,
            CardEffectType effectType,
            int cost,
            int range,
            int amount,
            string catalogSourceId = "",
            string effectRef = "",
            string targeting = "",
            int areaRadius = 0,
            CardUsePhase phaseAvailability = CardUsePhase.Default,
            CardPlayMode playMode = CardPlayMode.ManualTarget,
            CardFieldObjectKind fieldObjectKind = CardFieldObjectKind.None,
            int durationTurns = 0,
            CardCatalogStatus status = CardCatalogStatus.MigrationSeed,
            CardGameplayType gameplayType = CardGameplayType.Move,
            CardCostMode costMode = CardCostMode.Fixed,
            CardTargetMode targetMode = CardTargetMode.None,
            CardScalingMode scalingMode = CardScalingMode.Flat,
            CardPresentationRef presentationRef = default,
            bool includeInGameplayDecks = true,
            bool visibleInCatalog = true,
            string shapeId = null,
            string instanceId = null,
            int upgradeLevel = 0,
            bool isTemporary = false,
            int hitCount = 1,
            string additionalCost = "",
            string postActions = "",
            string choiceOptions = "",
            string choiceOptionTexts = "",
            string description = "",
            bool usableWhileStunned = false,
            string behaviorParams = "",
            string stateEffect = "",
            string buffDebuff = "",
            bool exhaustOnPlay = false,
            bool retainOnTurnEnd = false,
            int healAmount = 0)
        {
            HealAmount = Math.Max(0, healAmount);
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Card id is required.", nameof(id)) : id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Description = description ?? string.Empty;
            Category = category;
            EffectType = effectType;
            Cost = Math.Max(0, cost);
            Range = Math.Max(0, range);
            Amount = Math.Max(0, amount);
            CatalogSourceId = catalogSourceId ?? string.Empty;
            EffectRef = effectRef ?? string.Empty;
            Targeting = targeting ?? string.Empty;
            AreaRadius = Math.Max(0, areaRadius);
            PhaseAvailability = phaseAvailability == CardUsePhase.Default
                ? ResolveDefaultPhase(category, effectType)
                : phaseAvailability;
            PlayMode = playMode;
            FieldObjectKind = fieldObjectKind;
            DurationTurns = Math.Max(0, durationTurns);
            Status = status;
            GameplayType = gameplayType == CardGameplayType.Move ? ResolveDefaultGameplayType(effectType) : gameplayType;
            CostMode = costMode;
            TargetMode = targetMode == CardTargetMode.None ? ResolveDefaultTargetMode(playMode, effectType) : targetMode;
            ScalingMode = scalingMode;
            PresentationRef = string.IsNullOrWhiteSpace(presentationRef.PresentationId)
                ? CardPresentationRef.Placeholder(id)
                : presentationRef;
            IncludeInGameplayDecks = includeInGameplayDecks && status != CardCatalogStatus.Draft;
            VisibleInCatalog = visibleInCatalog && status != CardCatalogStatus.Draft;
            ShapeId = shapeId;
            InstanceId = string.IsNullOrWhiteSpace(instanceId) ? Id : instanceId;
            UpgradeLevel = Math.Max(0, upgradeLevel);
            IsTemporary = isTemporary;
            HitCount = Math.Max(1, hitCount);
            AdditionalCost = additionalCost ?? string.Empty;
            PostActions = postActions ?? string.Empty;
            ChoiceOptions = choiceOptions ?? string.Empty;
            ChoiceOptionTexts = choiceOptionTexts ?? string.Empty;
            UsableWhileStunned = usableWhileStunned;
            BehaviorParams = behaviorParams ?? string.Empty;
            StateEffect = stateEffect ?? string.Empty;
            BuffDebuff = buffDebuff ?? string.Empty;
            ExhaustOnPlay = exhaustOnPlay;
            RetainOnTurnEnd = retainOnTurnEnd;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public CardCategory Category { get; }
        public CardEffectType EffectType { get; }
        public int Cost { get; }
        public int Range { get; }
        public int Amount { get; }

        /// <summary>heal 컬럼의 저작값(0 = 미저작). <see cref="CardCatalogEntry.HealAmount"/> 참조.</summary>
        public int HealAmount { get; }

        /// <summary>회복 표시·집행이 쓰는 값 — heal 축이 저작됐으면 그것, 아니면 접힌 Amount(순수 회복 카드).</summary>
        public int EffectiveHealAmount => HealAmount > 0 ? HealAmount : Amount;

        public string CatalogSourceId { get; }
        public string EffectRef { get; }
        public string Targeting { get; }

        /// <summary>Area-of-effect radius in hex steps from the target tile. 0 = single target (default).</summary>
        public int AreaRadius { get; }
        public CardUsePhase PhaseAvailability { get; }
        public CardPlayMode PlayMode { get; }
        public CardFieldObjectKind FieldObjectKind { get; }
        public int DurationTurns { get; }
        public CardCatalogStatus Status { get; }
        public CardGameplayType GameplayType { get; }
        public CardCostMode CostMode { get; }
        public CardTargetMode TargetMode { get; }
        public CardScalingMode ScalingMode { get; }
        public CardPresentationRef PresentationRef { get; }
        public bool IncludeInGameplayDecks { get; }
        public bool VisibleInCatalog { get; }
        // When set, hit detection uses AttackShapeLibrary instead of the AreaRadius circle.
        public string ShapeId { get; }
        public string InstanceId { get; }
        public int UpgradeLevel { get; }
        public bool IsTemporary { get; }
        public int HitCount { get; }
        public string AdditionalCost { get; }
        public string PostActions { get; }
        public string ChoiceOptions { get; }
        public string ChoiceOptionTexts { get; }

        /// <summary>Exempts the card from the 기절(stun) action lockout. See <see cref="CardCatalogEntry.UsableWhileStunned"/>.</summary>
        public bool UsableWhileStunned { get; }

        /// <summary>Behavior-scoped <c>키:정수</c> scalars — see <see cref="CardBehaviorMetadata.GetBehaviorParam"/>.</summary>
        public string BehaviorParams { get; }

        /// <summary>Authored 상태이상 grants as `kind:amount`. Duration comes from <see cref="DurationTurns"/>.</summary>
        public string StateEffect { get; }

        /// <summary>Authored 버프/디버프 grants as `kind:amount`. Duration comes from <see cref="DurationTurns"/>.</summary>
        public string BuffDebuff { get; }

        /// <summary>
        /// 사용 시 버림 더미 대신 소멸 더미로 가는 카드(T5-1 「소멸」 통일 컬럼). 기존 3경로
        /// (additionalCost·전용 behaviorId·isTemporary)는 그대로 유지되며, 신규 저작만 이 플래그를 쓴다.
        /// </summary>
        public bool ExhaustOnPlay { get; }

        /// <summary>
        /// 턴 종료 시 버려지지 않고 손에 남는 카드(T5-2 「유지」). 유지 카드는 손패 자리를 차지하고,
        /// 다음 턴 드로우는 손패 상한까지만 채운다 — 신규 유입 −1이 유지의 기회비용이다.
        /// </summary>
        public bool RetainOnTurnEnd { get; }

        private static CardUsePhase ResolveDefaultPhase(CardCategory category, CardEffectType effectType)
        {
            // Utility cards (e.g. U01 다시 뽑기) and Scout cards (e.g. S01 지뢰찾기) are usable in either
            // player phase — movement or action.
            if (effectType == CardEffectType.Utility || effectType == CardEffectType.Scout)
            {
                return CardUsePhase.BothIfApproved;
            }

            return category == CardCategory.Movement ? CardUsePhase.Movement : CardUsePhase.Action;
        }

        private static CardGameplayType ResolveDefaultGameplayType(CardEffectType effectType)
        {
            switch (effectType)
            {
                case CardEffectType.Move:
                    return CardGameplayType.Move;
                case CardEffectType.Attack:
                    return CardGameplayType.Attack;
                case CardEffectType.Defend:
                    return CardGameplayType.Defend;
                case CardEffectType.Scout:
                    return CardGameplayType.Scout;
                case CardEffectType.FieldObject:
                    return CardGameplayType.Field;
                case CardEffectType.Buff:
                    return CardGameplayType.Buff;
                case CardEffectType.Utility:
                    return CardGameplayType.Utility;
                default:
                    return CardGameplayType.Utility;
            }
        }

        private static CardTargetMode ResolveDefaultTargetMode(CardPlayMode playMode, CardEffectType effectType)
        {
            if (playMode == CardPlayMode.Self)
            {
                return CardTargetMode.Self;
            }

            return effectType == CardEffectType.Move ? CardTargetMode.Tile : CardTargetMode.Enemy;
        }
    }
}
