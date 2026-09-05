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
            string effectRef,
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
            string additionalCost = "",
            string postActions = "",
            string choiceOptions = "",
            string choiceOptionTexts = "",
            string description = "",
            CardRarity rarity = CardRarity.Basic,
            bool usableWhileStunned = false,
            string behaviorParams = "",
            string stateEffect = "",
            string buffDebuff = "",
            bool exhaustOnPlay = false,
            bool retainOnTurnEnd = false,
            CardCatalogEntry upgradedEntry = null,
            int healAmount = 0)
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
            EffectRef = string.IsNullOrWhiteSpace(effectRef) ? ActionType.ToString() : effectRef;
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
            AdditionalCost = additionalCost ?? string.Empty;
            PostActions = postActions ?? string.Empty;
            ChoiceOptions = choiceOptions ?? string.Empty;
            ChoiceOptionTexts = choiceOptionTexts ?? string.Empty;
            Rarity = rarity;
            UsableWhileStunned = usableWhileStunned;
            BehaviorParams = behaviorParams ?? string.Empty;
            StateEffect = stateEffect ?? string.Empty;
            BuffDebuff = buffDebuff ?? string.Empty;
            ExhaustOnPlay = exhaustOnPlay;
            RetainOnTurnEnd = retainOnTurnEnd;
            UpgradedEntry = upgradedEntry;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public CardCategory DeckType { get; }
        public CardEffectType ActionType { get; }
        public int Cost { get; }
        public int Range { get; }
        public int Amount { get; }
        public string EffectRef { get; }
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
        public string AdditionalCost { get; }
        public string PostActions { get; }
        public string ChoiceOptions { get; }
        public string ChoiceOptionTexts { get; }

        /// <summary>Reward rarity grade. Default <see cref="CardRarity.Basic"/> (never offered as a reward).</summary>
        public CardRarity Rarity { get; }

        /// <summary>
        /// Exempts the card from the 기절(stun) action lockout. Authored per card so the exception stays data,
        /// not a behaviorId special case — 정화 cards need it, since a stun the player cannot clear is a dead end.
        /// </summary>
        public bool UsableWhileStunned { get; }

        /// <summary>Behavior-scoped <c>키:정수</c> scalars — see <see cref="CardBehaviorMetadata.GetBehaviorParam"/>.</summary>
        public string BehaviorParams { get; }

        /// <summary>Authored 상태이상 grants as `kind:amount` (`;`-separated). Duration comes from <see cref="DurationTurns"/>.</summary>
        public string StateEffect { get; }

        /// <summary>Authored 버프/디버프 grants as `kind:amount` (`;`-separated). Duration comes from <see cref="DurationTurns"/>.</summary>
        public string BuffDebuff { get; }

        /// <summary>사용 시 소멸(T5-1 통일 컬럼) — <see cref="CardDefinition.ExhaustOnPlay"/> 참조.</summary>
        public bool ExhaustOnPlay { get; }

        /// <summary>턴 종료 시 손 유지(T5-2) — <see cref="CardDefinition.RetainOnTurnEnd"/> 참조.</summary>
        public bool RetainOnTurnEnd { get; }

        /// <summary>
        /// 연마(카드 강화) 후 값을 통째로 담은 엔트리. null = 이 카드는 연마 불가(card_upgrades.csv에
        /// 행이 없음). 인스턴스의 <c>UpgradeLevel >= 1</c>이면 <see cref="ToCardDefinition(string,string,int,bool)"/>이
        /// 이 엔트리의 값으로 정의를 만든다 — 실행 시점 배율 보정이 아니라 정의 시점 치환이라야
        /// 필드 카드(배치 시점 저작값 사용)에도 연마가 먹는다.
        /// </summary>
        public CardCatalogEntry UpgradedEntry { get; }

        /// <summary>
        /// heal 컬럼의 저작값(WS-I I-08). 0 = 미저작. <see cref="Amount"/>는 damage→shield→heal
        /// 우선순위로 접힌 단일 축이라, damage와 heal을 <b>동시에</b> 저작한 카드(A03 성스러운 빛)의
        /// 회복량이 여기 없으면 죽은 데이터가 된다 — 표시·집행은 <c>CardDefinition.EffectiveHealAmount</c>를 쓴다.
        /// </summary>
        public int HealAmount { get; }

        public CardDefinition ToCardDefinition(string catalogSourceId)
        {
            return ToCardDefinition(catalogSourceId, null, 0, false);
        }

        public CardDefinition ToCardDefinition(string catalogSourceId, string instanceId, int upgradeLevel = 0, bool isTemporary = false)
        {
            if (upgradeLevel >= 1 && UpgradedEntry != null)
            {
                return UpgradedEntry.ToCardDefinition(catalogSourceId, instanceId, upgradeLevel, isTemporary);
            }

            return new CardDefinition(
                Id,
                DisplayName,
                DeckType,
                ActionType,
                Cost,
                Range,
                Amount,
                catalogSourceId,
                EffectRef,
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
                AdditionalCost,
                PostActions,
                ChoiceOptions,
                ChoiceOptionTexts,
                Description,
                UsableWhileStunned,
                BehaviorParams,
                StateEffect,
                BuffDebuff,
                ExhaustOnPlay,
                RetainOnTurnEnd,
                healAmount: HealAmount);
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
