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
            string choiceOptionTexts = "",
            string description = "",
            string stateEffect = "",
            string buffDebuff = "",
            int healAmount = 0,
            string descriptionUpgraded = "")
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
            ChoiceOptionTexts = choiceOptionTexts ?? string.Empty;
            DescriptionUpgraded = descriptionUpgraded ?? string.Empty;
            StateEffect = stateEffect ?? string.Empty;
            BuffDebuff = buffDebuff ?? string.Empty;
        }

        /// <summary>
        /// 일부 축만 바꾼 복사본(연마 P4: 카드 클래스 <c>Upgrade</c>가 <c>card.With(amount: 5)</c> 식으로 쓴다).
        /// null = 그대로. 규칙·표시가 읽는 축만 열어 두었다 — id·종류·타게팅·프레젠테이션은 연마로 바뀌지 않는다.
        /// </summary>
        public CardDefinition With(
            string displayName = null,
            string description = null,
            int? cost = null,
            int? range = null,
            int? amount = null,
            int? healAmount = null,
            int? areaRadius = null,
            int? durationTurns = null,
            int? hitCount = null,
            string shapeId = null,
            string stateEffect = null,
            string buffDebuff = null,
            int? upgradeLevel = null)
        {
            return new CardDefinition(
                Id,
                displayName ?? DisplayName,
                Category,
                EffectType,
                cost ?? Cost,
                range ?? Range,
                amount ?? Amount,
                CatalogSourceId,
                Targeting,
                areaRadius ?? AreaRadius,
                PhaseAvailability,
                PlayMode,
                FieldObjectKind,
                durationTurns ?? DurationTurns,
                Status,
                GameplayType,
                CostMode,
                TargetMode,
                ScalingMode,
                PresentationRef,
                IncludeInGameplayDecks,
                VisibleInCatalog,
                shapeId ?? ShapeId,
                InstanceId,
                upgradeLevel ?? UpgradeLevel,
                IsTemporary,
                hitCount ?? HitCount,
                ChoiceOptionTexts,
                description ?? Description,
                stateEffect ?? StateEffect,
                buffDebuff ?? BuffDebuff,
                healAmount ?? HealAmount,
                DescriptionUpgraded);
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

        /// <summary>갈림길 선택지 문안(cards.csv `choiceTexts`, `optionId|표시명|문안;…`). 선택지 규칙은 카드 클래스 <c>Choices</c>.</summary>
        public string ChoiceOptionTexts { get; }

        /// <summary>강화(연마) 뒤 설명 문안(cards.csv `descriptionUpgraded`, D-3). 비면 원본 토큰 문안이 새 수치로 갱신된다. <c>CardUpgrades.Resolve</c>가 소비한다.</summary>
        public string DescriptionUpgraded { get; }


        /// <summary>Authored 상태이상 grants as `kind:amount`. Duration comes from <see cref="DurationTurns"/>.</summary>
        public string StateEffect { get; }

        /// <summary>Authored 버프/디버프 grants as `kind:amount`. Duration comes from <see cref="DurationTurns"/>.</summary>
        public string BuffDebuff { get; }



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
