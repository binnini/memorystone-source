using System;

namespace SeoulPlayup.CardCore
{
    public sealed class CardCatalogEntry
    {
        public CardCatalogEntry(
            string id,
            string displayName,
            CardCategory deckType,
            CardEffectType actionType,
            int cost,
            int range,
            int amount,
            string targeting,
            string sourceTrace = "",
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
            int hitCount = 1,
            string choiceOptionTexts = "",
            string description = "",
            CardRarity rarity = CardRarity.Basic,
            string stateEffect = "",
            string buffDebuff = "",
            int healAmount = 0,
            string descriptionUpgraded = "")
        {
            HealAmount = Math.Max(0, healAmount);
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Catalog card id is required.", nameof(id)) : id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Description = description ?? string.Empty;
            DeckType = deckType;
            ActionType = actionType;
            Cost = Math.Max(0, cost);
            Range = Math.Max(0, range);
            Amount = Math.Max(0, amount);
            Targeting = targeting ?? string.Empty;
            SourceTrace = sourceTrace ?? string.Empty;
            AreaRadius = Math.Max(0, areaRadius);
            PhaseAvailability = phaseAvailability == CardUsePhase.Default
                ? ResolveDefaultPhase(deckType, actionType)
                : phaseAvailability;
            PlayMode = playMode;
            FieldObjectKind = fieldObjectKind;
            DurationTurns = Math.Max(0, durationTurns);
            Status = status;
            GameplayType = gameplayType == CardGameplayType.Move ? ResolveDefaultGameplayType(actionType) : gameplayType;
            CostMode = costMode;
            TargetMode = targetMode == CardTargetMode.None ? ResolveDefaultTargetMode(playMode, actionType) : targetMode;
            ScalingMode = scalingMode;
            PresentationRef = string.IsNullOrWhiteSpace(presentationRef.PresentationId)
                ? CardPresentationRef.Placeholder(id)
                : presentationRef;
            IncludeInGameplayDecks = includeInGameplayDecks && status != CardCatalogStatus.Draft;
            VisibleInCatalog = visibleInCatalog && status != CardCatalogStatus.Draft;
            ShapeId = shapeId;
            HitCount = Math.Max(1, hitCount);
            ChoiceOptionTexts = choiceOptionTexts ?? string.Empty;
            Rarity = rarity;
            StateEffect = stateEffect ?? string.Empty;
            BuffDebuff = buffDebuff ?? string.Empty;
            DescriptionUpgraded = descriptionUpgraded ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public CardCategory DeckType { get; }
        public CardEffectType ActionType { get; }
        public int Cost { get; }
        public int Range { get; }
        public int Amount { get; }
        public string Targeting { get; }
        public string SourceTrace { get; }

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
        public int HitCount { get; }

        /// <summary>갈림길 선택지 문안(cards.csv `choiceTexts`). 선택지 규칙은 카드 클래스 <c>Choices</c>가 정본.</summary>
        public string ChoiceOptionTexts { get; }

        /// <summary>Reward rarity grade. Default <see cref="CardRarity.Basic"/> (never offered as a reward).</summary>
        public CardRarity Rarity { get; }


        /// <summary>Authored 상태이상 grants as `kind:amount` (`;`-separated). Duration comes from <see cref="DurationTurns"/>.</summary>
        public string StateEffect { get; }

        /// <summary>Authored 버프/디버프 grants as `kind:amount` (`;`-separated). Duration comes from <see cref="DurationTurns"/>.</summary>
        public string BuffDebuff { get; }



        /// <summary>
        /// heal 컬럼의 저작값(WS-I I-08). 0 = 미저작. <see cref="Amount"/>는 damage→shield→heal
        /// 우선순위로 접힌 단일 축이라, damage와 heal을 <b>동시에</b> 저작한 카드(A03 성스러운 빛)의
        /// 회복량이 여기 없으면 죽은 데이터가 된다 — 표시·집행은 <c>CardDefinition.EffectiveHealAmount</c>를 쓴다.
        /// </summary>
        public int HealAmount { get; }

        /// <summary>강화(연마) 뒤 설명 문안(cards.csv `descriptionUpgraded`, D-3). 비면 원본 토큰 문안이 새 수치로 갱신된다. 소비는 <c>CardUpgrades.Resolve</c>.</summary>
        public string DescriptionUpgraded { get; }

        public CardDefinition ToCardDefinition(string catalogSourceId)
        {
            return ToCardDefinition(catalogSourceId, null, 0, false);
        }

        /// <summary>
        /// 저작 정의. <paramref name="upgradeLevel"/>은 인스턴스의 연마 단계를 <b>실어 나를 뿐</b> 값을 바꾸지 않는다 —
        /// 연마 치환은 카드 클래스(<c>CardBehavior.Upgrade</c>)가 하고, Combat.Runtime의 <c>CardUpgrades.Resolve</c>가 적용한다(P4).
        /// </summary>
        public CardDefinition ToCardDefinition(string catalogSourceId, string instanceId, int upgradeLevel = 0, bool isTemporary = false)
        {
            return new CardDefinition(
                Id,
                DisplayName,
                DeckType,
                ActionType,
                Cost,
                Range,
                Amount,
                catalogSourceId,
                Targeting,
                AreaRadius,
                PhaseAvailability,
                PlayMode,
                FieldObjectKind,
                DurationTurns,
                Status,
                GameplayType,
                CostMode,
                TargetMode,
                ScalingMode,
                PresentationRef,
                IncludeInGameplayDecks,
                VisibleInCatalog,
                ShapeId,
                instanceId,
                upgradeLevel,
                isTemporary,
                HitCount,
                ChoiceOptionTexts,
                Description,
                StateEffect,
                BuffDebuff,
                healAmount: HealAmount,
                descriptionUpgraded: DescriptionUpgraded);
        }

        private static CardUsePhase ResolveDefaultPhase(CardCategory deckType, CardEffectType actionType)
        {
            // Utility cards (e.g. U01 다시 뽑기) and Scout cards (e.g. S01 지뢰찾기) are usable in either
            // player phase — movement or action.
            if (actionType == CardEffectType.Utility || actionType == CardEffectType.Scout)
            {
                return CardUsePhase.BothIfApproved;
            }

            return deckType == CardCategory.Movement ? CardUsePhase.Movement : CardUsePhase.Action;
        }

        private static CardGameplayType ResolveDefaultGameplayType(CardEffectType actionType)
        {
            switch (actionType)
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

        private static CardTargetMode ResolveDefaultTargetMode(CardPlayMode playMode, CardEffectType actionType)
        {
            if (playMode == CardPlayMode.Self)
            {
                return CardTargetMode.Self;
            }

            return actionType == CardEffectType.Move ? CardTargetMode.Tile : CardTargetMode.Enemy;
        }
    }
}
