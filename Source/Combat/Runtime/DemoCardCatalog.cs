using System.Collections.Generic;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime.Cards;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 개발·테스트 전용 카드 카탈로그(옛 <c>ApprovedCardCatalogFactory</c>, P3-b에서 이름·역할을 바꿈).
    /// <b>출하 카드의 저작 표면이 아니다</b> — 출하 정본은 <c>cards.csv</c>(→ <c>CardCatalogAsset</c>)이고 이 카탈로그는
    /// 카탈로그를 넘기지 않은 <see cref="CombatState"/>(테스트 픽스처·개발 도구·샌드박스)가 받는 고정 시드다.
    /// 게임 본편(<c>MapCombatController</c>)은 CSV 에셋이 없으면 여기로 떨어지지 않고 예외를 낸다(S11 조용한 폴백 금지).
    /// id는 <see cref="CardIds"/>(출하 id)라 카드 규칙은 레지스트리가 준다 — 수치는 출하값과 다를 수 있으며 테스트는 수치를 핀하지 않는다.
    /// 옛 팩토리의 Draft 카드(M07 「Move 5」·M08 「빠른 거북」)와 디버그 카드 4종은 폐기했다.
    /// </summary>
    public static class DemoCardCatalog
    {
        /// <summary>출하 CSV 카탈로그의 <see cref="CombatCatalogFactory.CardCatalogSourceId"/>와 다르게 두어 세이브·증거 문자열에서 구분된다.</summary>
        public const string SourceId = "demo-card-catalog";

        /// <summary>I01 「조사」 — 출하 cards.csv에 없는 데모 전용 카드(목표 조사 규칙 <c>TryPlayerInvestigate</c>의 픽스처).</summary>
        public const string ObjectiveInvestigateId = "I01";

        public static CardCatalogDefinition Create(CombatConfig config)
        {
            return new CardCatalogDefinition(
                SourceId,
                "Demo card catalog (dev/test seed)",
                CreateEntries(config));
        }

        private static IEnumerable<CardCatalogEntry> CreateEntries(CombatConfig config)
        {
            var attackRange = System.Math.Max(1, config.AttackRange);

            yield return Entry(CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move, 1, 1, 1, "reachable_hex", gameplayType: CardGameplayType.Move, targetMode: CardTargetMode.Tile);
            yield return Entry(CardIds.Move2Hex, "Move 2", CardCategory.Movement, CardEffectType.Move, 2, 2, 2, "reachable_hex", gameplayType: CardGameplayType.Move, targetMode: CardTargetMode.Tile);
            yield return Entry(CardIds.Move3Hex, "Move 3", CardCategory.Movement, CardEffectType.Move, 3, 3, 3, "reachable_hex", gameplayType: CardGameplayType.Move, targetMode: CardTargetMode.Tile);
            yield return Entry(CardIds.Move4Hex, "Move 4", CardCategory.Movement, CardEffectType.Move, 4, 4, 4, "reachable_hex", gameplayType: CardGameplayType.Move, targetMode: CardTargetMode.Tile);
            yield return Entry(CardIds.Momentum, "Momentum", CardCategory.Movement, CardEffectType.Move, 1, 0, 2, "self", durationTurns: 1, gameplayType: CardGameplayType.Buff, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self, buffDebuff: "Agility:2");
            yield return Entry(CardIds.RandomJourney, "Random Journey", CardCategory.Movement, CardEffectType.Move, 1, 0, 0, "random_reachable_hex_radius_2", areaRadius: 2, gameplayType: CardGameplayType.Move, targetMode: CardTargetMode.RandomReachable);

            yield return Entry(CardIds.Sweep, "Sweep", CardCategory.Action, CardEffectType.Attack, 1, 0, 3, "living_monsters_in_area", areaRadius: 1, gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.SelfArea);
            yield return Entry(CardIds.OldArmor, "Old Suit", CardCategory.Action, CardEffectType.Defend, 1, 0, 5, "self", gameplayType: CardGameplayType.Defend, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self);
            yield return Entry(CardIds.Minefinder, "Minefinder", CardCategory.Action, CardEffectType.Scout, 1, 5, 2, "walkable_map_cell", areaRadius: 2, gameplayType: CardGameplayType.Scout, scalingMode: CardScalingMode.Flat, targetMode: CardTargetMode.Tile);
            yield return Entry(ObjectiveInvestigateId, "조사", CardCategory.Action, CardEffectType.Investigate, 1, 1, 0, "revealed_objective_in_range", gameplayType: CardGameplayType.Utility, targetMode: CardTargetMode.Tile);
            yield return Entry(CardIds.DoubleHit, "Double Hit", CardCategory.Action, CardEffectType.Attack, 1, 4, 2, "living_monster_in_range", gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy, hitCount: 2);
            yield return Entry(CardIds.MoveLinkedStrike, "Move Linked Attack", CardCategory.Action, CardEffectType.Attack, 1, 1, 2, "living_monster_in_range", gameplayType: CardGameplayType.Attack, scalingMode: CardScalingMode.MovedThisTurn, targetMode: CardTargetMode.Enemy);
            yield return Entry(CardIds.HolyLight, "Holy Light", CardCategory.Action, CardEffectType.Attack, 1, 3, 4, "choice_self_or_enemy", gameplayType: CardGameplayType.Attack, playMode: CardPlayMode.Choice, targetMode: CardTargetMode.OptionThenTarget);
            yield return Entry(CardIds.FinishingTouch, "Finishing Touch", CardCategory.Action, CardEffectType.Attack, 2, 1, 3, "living_monster_in_range", gameplayType: CardGameplayType.Attack, scalingMode: CardScalingMode.AttackCardsInHand, targetMode: CardTargetMode.Enemy);
            yield return Entry(CardIds.FinalBlow, "Final Blow", CardCategory.Action, CardEffectType.Attack, config.MaxKi, 1, 3, "living_monster_in_range", gameplayType: CardGameplayType.Attack, costMode: CardCostMode.SpendAll, scalingMode: CardScalingMode.SpentKi, targetMode: CardTargetMode.Enemy);
            yield return Entry(CardIds.OneStrikeEnough, "One Strike Enough", CardCategory.Action, CardEffectType.Attack, 3, 2, 8, "living_monster_in_range_2", gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy);
            yield return Entry(CardIds.MultiplyingStrike, "Multiplying Strike", CardCategory.Action, CardEffectType.Attack, 1, 1, 2, "living_monster_in_range", gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy);
            yield return new CardCatalogEntry(
                CardIds.MultiplyingStrikeCopy, "Multiplying Strike Copy", CardCategory.Action, CardEffectType.Attack,
                1, 1, 2, "living_monster_in_range",
                "Temporary copy injected by Multiplying Strike.",
                0, CardUsePhase.Action, CardPlayMode.ManualTarget, CardFieldObjectKind.None, 0,
                CardCatalogStatus.Approved, CardGameplayType.Attack, CardCostMode.Fixed, CardTargetMode.Enemy,
                CardScalingMode.Flat, CreatePresentationRef(CardIds.MultiplyingStrikeCopy, CardGameplayType.Attack),
                includeInGameplayDecks: false, visibleInCatalog: true);
            yield return Entry(CardIds.Sacrifice, "Sacrifice Attack", CardCategory.Action, CardEffectType.Attack, 1, attackRange, 5, "living_monster_and_hand_card", gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy);
            yield return Entry(CardIds.TargetShot, "Target Shot", CardCategory.Action, CardEffectType.Attack, 1, 3, 2, "living_monster_in_range_3", gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy);

            yield return Entry(CardIds.ShelterTaunt, "Shelter Taunt", CardCategory.Action, CardEffectType.Defend, 1, 0, 0, "self", durationTurns: 1, gameplayType: CardGameplayType.Defend, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self, buffDebuff: "Strength:100");
            yield return Entry(CardIds.DoubleEdgedShield, "Double Edged Shield", CardCategory.Action, CardEffectType.Defend, 1, 0, 50, "self", durationTurns: 1, gameplayType: CardGameplayType.Defend, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self, buffDebuff: "Reflect:50");

            yield return Entry(CardIds.Treasurefinder, "Treasurefinder", CardCategory.Action, CardEffectType.Scout, 1, 5, 2, "walkable_map_cell", areaRadius: 1, gameplayType: CardGameplayType.Scout, scalingMode: CardScalingMode.ObjectsInRevealArea, targetMode: CardTargetMode.Tile);

            yield return Entry(CardIds.Firebomb, "Firebomb", CardCategory.Action, CardEffectType.FieldObject, 2, 2, 6, "walkable_map_cell", areaRadius: 1, gameplayType: CardGameplayType.Field, fieldObjectKind: CardFieldObjectKind.FieldDamage, durationTurns: 2, targetMode: CardTargetMode.Tile);
            yield return Entry(CardIds.SacredLamp, "Sacred Campfire", CardCategory.Action, CardEffectType.FieldObject, 3, 1, 3, "walkable_map_cell", areaRadius: 2, gameplayType: CardGameplayType.Field, fieldObjectKind: CardFieldObjectKind.ConditionalHeal, durationTurns: 4, targetMode: CardTargetMode.Tile);
            yield return Entry(CardIds.Flashbang, "Flashbang", CardCategory.Action, CardEffectType.FieldObject, 2, 2, 0, "walkable_map_cell", areaRadius: 2, gameplayType: CardGameplayType.Field, fieldObjectKind: CardFieldObjectKind.MassImmobilize, durationTurns: 2, targetMode: CardTargetMode.Tile);

            yield return Entry(CardIds.Redraw, "Redraw", CardCategory.Action, CardEffectType.Utility, 1, 0, 0, "current_action_hand_except_self", gameplayType: CardGameplayType.Utility, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self);
        }

        private static CardCatalogEntry Entry(
            string id,
            string displayName,
            CardCategory deckType,
            CardEffectType actionType,
            int cost,
            int range,
            int amount,
            string targeting,
            int areaRadius = 0,
            CardUsePhase phaseAvailability = CardUsePhase.Default,
            CardPlayMode playMode = CardPlayMode.ManualTarget,
            CardFieldObjectKind fieldObjectKind = CardFieldObjectKind.None,
            int durationTurns = 0,
            CardGameplayType gameplayType = CardGameplayType.Move,
            CardCostMode costMode = CardCostMode.Fixed,
            CardTargetMode targetMode = CardTargetMode.None,
            CardScalingMode scalingMode = CardScalingMode.Flat,
            string shapeId = null,
            int hitCount = 1,
            string buffDebuff = "")
        {
            return new CardCatalogEntry(
                id,
                displayName,
                deckType,
                actionType,
                cost,
                range,
                amount,
                targeting,
                "Demo card catalog (dev/test seed) — not a shipping authoring surface",
                areaRadius,
                phaseAvailability,
                playMode,
                fieldObjectKind,
                durationTurns,
                CardCatalogStatus.Approved,
                gameplayType,
                costMode,
                targetMode,
                scalingMode,
                CreatePresentationRef(id, gameplayType),
                shapeId: shapeId,
                hitCount: hitCount,
                buffDebuff: buffDebuff);
        }

        private static CardPresentationRef CreatePresentationRef(string id, CardGameplayType gameplayType)
        {
            var type = gameplayType.ToString().ToLowerInvariant();
            return new CardPresentationRef(
                $"card.presentation.{id}",
                $"card.frame.{type}",
                $"card.icon.{type}",
                string.Empty,
                $"card.vfx.{id}",
                $"card.sfx.{type}");
        }
    }
}
