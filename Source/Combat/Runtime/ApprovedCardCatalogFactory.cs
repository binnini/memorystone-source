using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Runtime catalog for the currently approved CSV card taxonomy.
    /// </summary>
    public static class ApprovedCardCatalogFactory
    {
        public const string SourceId = "approved-card-system-catalog-2026-06-02";

        public const string MoveBasicId = "M02";
        public const string Move1HexId = "M01";
        public const string Move2HexId = "M02";
        public const string Move3HexId = "M03";
        public const string Move4HexId = "M04";
        public const string Move5HexId = "M07";
        public const string MoveMomentumId = "M05";
        public const string MoveRandomJourneyId = "M06";
        public const string MoveFastTurtleId = "M08";
        public const string AttackSweepId = "A01";
        public const string AttackMoveLinkedId = "A02";
        public const string AttackHolyLightId = "A03";
        public const string AttackFinishingTouchId = "A04";
        public const string AttackFinalBlowId = "A05";
        public const string AttackDoubleHitId = "A06";
        public const string AttackOneStrikeEnoughId = "A07";
        public const string AttackMultiplyingStrikeId = "A08";
        public const string AttackMultiplyingStrikeCopyId = "A09";
        public const string AttackSacrificeId = "A10";
        public const string AttackTargetShotId = "A11";
        // CSV-only card (잔혼 공격): plain attack.damage whose repeat count comes from the ExiledCards
        // scaling mode, so no handler and no factory entry.
        public const string AttackRemnantId = "A13";
        // CSV-only card (전염병): attack handler for the afflicted-target bonus + a SpreadStatus post-action.
        public const string AttackPlagueId = "A12";
        // 묶음 D 신규 2종: 횃불(C-14)·함정 해제(C-13). 둘 다 정상 덱에 섞인다.
        public const string UtilityTorchId = "U04";
        public const string ScoutTrapDisarmId = "S05";

        // 상태 카드(C-17 / D-17): 사용 불가 + 전투 중 덱 삽입. 정상 덱에는 섞이지 않는다.
        public const string StatusFineDustId = "X01";
        public const string StatusBrokenGlassId = "X02";
        public const string StatusBlackoutId = "X03";

        public const string DefendOldSuitId = "D01";
        public const string DefendShelterTauntId = "D02";
        public const string DefendDoubleEdgedShieldId = "D03";
        // CSV-only cards, like ScoutBasicId above: they carry real handlers, but the code catalog stays
        // the pre-CSV seed set, so cards.csv remains their single authoring surface.
        public const string DefendHeavyArmorId = "D04";
        public const string DefendHospitalizationId = "D06";
        // CSV-only card (부적 방패).
        public const string DefendTalismanShieldId = "D05";
        // CSV-only card (정찰의 기초): the code catalog below has no entry for it — reveal-only scouting needs
        // no handler, so cards.csv is its single authoring surface.
        public const string ScoutBasicId = "S00";
        public const string ScoutMinefinderId = "S01";
        public const string ScoutTreasurefinderId = "S02";
        // CSV-only cards (기절초광 / 빙고!): both carry scout handlers but no code-catalog entry, so cards.csv
        // stays their single authoring surface.
        public const string ScoutStunFlashId = "S03";
        public const string ScoutBingoId = "S04";
        public const string ObjectiveInvestigateId = "I01";
        public const string FieldFirebombId = "F01";
        public const string FieldSacredCampfireId = "F02";
        public const string FieldFlashbangId = "F03";
        // CSV-only cards (흡수진 / 콩콩탄탄). F05 needs no handler at all — it is `field.damage` with its own
        // numbers plus hitCount=2, which the field reads as hits-per-tick (DEC-2026-07-24-01).
        public const string FieldLifestealId = "F04";
        public const string FieldBounceBombId = "F05";
        public const string UtilityRedrawId = "U01";
        public const string UtilityCleanseDrawId = "U03";
        // CSV-only card (부적 끌어오기): a choice card, so it resolves through TryPlayerChoiceOption rather
        // than the utility path.
        public const string UtilityDrawOrRecoverId = "U02";

        public const string DebugBindId = "debug-bind";
        public const string DebugSlowId = "debug-slow";
        public const string DebugRuptureId = "debug-rupture";
        public const string DebugKnockbackId = "debug-knockback";

        public static CardCatalogDefinition CreateApprovedCatalog(CombatConfig config)
        {
            return new CardCatalogDefinition(
                SourceId,
                "Approved Card System catalog",
                CreateEntries(config));
        }

        private static IEnumerable<CardCatalogEntry> CreateEntries(CombatConfig config)
        {
            var attackRange = System.Math.Max(1, config.AttackRange);

            yield return Approved(Move1HexId, "Move 1", CardCategory.Movement, CardEffectType.Move, 1, 1, 1, CardEffectRefs.MoveBasic, "reachable_hex", gameplayType: CardGameplayType.Move, targetMode: CardTargetMode.Tile);
            yield return Approved(Move2HexId, "Move 2", CardCategory.Movement, CardEffectType.Move, 2, 2, 2, CardEffectRefs.MoveBasic, "reachable_hex", gameplayType: CardGameplayType.Move, targetMode: CardTargetMode.Tile);
            yield return Approved(Move3HexId, "Move 3", CardCategory.Movement, CardEffectType.Move, 3, 3, 3, CardEffectRefs.MoveBasic, "reachable_hex", gameplayType: CardGameplayType.Move, targetMode: CardTargetMode.Tile);
            yield return Approved(Move4HexId, "Move 4", CardCategory.Movement, CardEffectType.Move, 4, 4, 4, CardEffectRefs.MoveBasic, "reachable_hex", gameplayType: CardGameplayType.Move, targetMode: CardTargetMode.Tile);
            yield return Draft(Move5HexId, "Move 5", CardCategory.Movement, CardEffectType.Move, 2, 5, 5, CardEffectRefs.MoveBasic, "reachable_hex", CardGameplayType.Move);
            yield return Approved(MoveMomentumId, "Momentum", CardCategory.Movement, CardEffectType.Move, 1, 0, 2, CardEffectRefs.MoveDeferredMomentum, "self", durationTurns: 1, gameplayType: CardGameplayType.Buff, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self, buffDebuff: "Agility:2");
            yield return Approved(MoveRandomJourneyId, "Random Journey", CardCategory.Movement, CardEffectType.Move, 1, 0, 0, CardEffectRefs.MoveRandomRadius2, "random_reachable_hex_radius_2", areaRadius: 2, gameplayType: CardGameplayType.Move, targetMode: CardTargetMode.RandomReachable);
            yield return Draft(MoveFastTurtleId, "Fast Turtle", CardCategory.Movement, CardEffectType.Move, 2, 6, 6, CardEffectRefs.MoveFastTurtle, "revealed_random_distance_then_reachable_hex", CardGameplayType.Move);

            yield return Approved(AttackSweepId, "Sweep", CardCategory.Action, CardEffectType.Attack, 1, 0, 3, CardEffectRefs.AttackDamage, "living_monsters_in_area", areaRadius: 1, gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.SelfArea);
            yield return Approved(DefendOldSuitId, "Old Suit", CardCategory.Action, CardEffectType.Defend, 1, 0, 5, CardEffectRefs.DefendBlock, "self", gameplayType: CardGameplayType.Defend, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self);
            yield return Approved(ScoutMinefinderId, "Minefinder", CardCategory.Action, CardEffectType.Scout, 1, 5, 2, CardEffectRefs.ScoutEnemyCountDamage, "walkable_map_cell", areaRadius: 2, gameplayType: CardGameplayType.Scout, scalingMode: CardScalingMode.Flat, targetMode: CardTargetMode.Tile);
            yield return Approved(ObjectiveInvestigateId, "조사", CardCategory.Action, CardEffectType.Investigate, 1, 1, 0, CardEffectRefs.ObjectiveInvestigate, "revealed_objective_in_range", gameplayType: CardGameplayType.Utility, targetMode: CardTargetMode.Tile);
            yield return Approved(AttackDoubleHitId, "Double Hit", CardCategory.Action, CardEffectType.Attack, 1, 4, 2, CardEffectRefs.AttackDamage, "living_monster_in_range", gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy, hitCount: 2);
            yield return Approved(AttackMoveLinkedId, "Move Linked Attack", CardCategory.Action, CardEffectType.Attack, 1, 1, 2, CardEffectRefs.AttackDamage, "living_monster_in_range", gameplayType: CardGameplayType.Attack, scalingMode: CardScalingMode.MovedThisTurn, targetMode: CardTargetMode.Enemy);
            yield return Approved(AttackHolyLightId, "Holy Light", CardCategory.Action, CardEffectType.Attack, 1, 3, 4, CardEffectRefs.AttackDamage, "choice_self_or_enemy", gameplayType: CardGameplayType.Attack, playMode: CardPlayMode.Choice, targetMode: CardTargetMode.OptionThenTarget, choiceOptions: $"heal:{CardBehaviorMetadata.ChoiceEffectHealPlayer}:self;attack:{CardBehaviorMetadata.ChoiceEffectAttackDamage}:enemy");
            yield return Approved(AttackFinishingTouchId, "Finishing Touch", CardCategory.Action, CardEffectType.Attack, 2, 1, 3, CardEffectRefs.AttackDamage, "living_monster_in_range", gameplayType: CardGameplayType.Attack, scalingMode: CardScalingMode.AttackCardsInHand, targetMode: CardTargetMode.Enemy);
            yield return Approved(AttackFinalBlowId, "Final Blow", CardCategory.Action, CardEffectType.Attack, config.MaxKi, 1, 3, CardEffectRefs.AttackDamage, "living_monster_in_range", gameplayType: CardGameplayType.Attack, costMode: CardCostMode.SpendAll, scalingMode: CardScalingMode.SpentKi, targetMode: CardTargetMode.Enemy);
            yield return Approved(AttackOneStrikeEnoughId, "One Strike Enough", CardCategory.Action, CardEffectType.Attack, 3, 2, 8, CardEffectRefs.AttackOneStrikeEnough, "living_monster_in_range_2", gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy);
            yield return Approved(AttackMultiplyingStrikeId, "Multiplying Strike", CardCategory.Action, CardEffectType.Attack, 1, 1, 2, CardEffectRefs.AttackMultiplyingStrike, "living_monster_in_range", gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy, postActions: $"{CardBehaviorMetadata.PostActionInjectCopy}:{CardEffectRefs.AttackMultiplyingStrike}");
            yield return new CardCatalogEntry(
                AttackMultiplyingStrikeCopyId, "Multiplying Strike Copy", CardCategory.Action, CardEffectType.Attack,
                1, 1, 2, CardEffectRefs.AttackMultiplyingStrike, "living_monster_in_range",
                "Temporary copy injected by Multiplying Strike.",
                0, CardUsePhase.Action, CardPlayMode.ManualTarget, CardFieldObjectKind.None, 0,
                CardCatalogStatus.Approved, CardGameplayType.Attack, CardCostMode.Fixed, CardTargetMode.Enemy,
                CardScalingMode.Flat, CreatePresentationRef(AttackMultiplyingStrikeCopyId, CardGameplayType.Attack),
                includeInGameplayDecks: false, visibleInCatalog: true);
            yield return Approved(AttackSacrificeId, "Sacrifice Attack", CardCategory.Action, CardEffectType.Attack, 1, attackRange, 5, CardEffectRefs.AttackDamage, "living_monster_and_hand_card", gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy, additionalCost: CardBehaviorMetadata.AdditionalCostExileSelectedHandCards);
            yield return Approved(AttackTargetShotId, "Target Shot", CardCategory.Action, CardEffectType.Attack, 1, 3, 2, CardEffectRefs.AttackDamage, "living_monster_in_range_3", gameplayType: CardGameplayType.Attack, targetMode: CardTargetMode.Enemy, postActions: CardBehaviorMetadata.PostActionApplyMark);

            yield return Approved(DefendShelterTauntId, "Shelter Taunt", CardCategory.Action, CardEffectType.Defend, 1, 0, 0, CardEffectRefs.DefendZeroThenDouble, "self", durationTurns: 1, gameplayType: CardGameplayType.Defend, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self, buffDebuff: "Strength:100");
            yield return Approved(DefendDoubleEdgedShieldId, "Double Edged Shield", CardCategory.Action, CardEffectType.Defend, 1, 0, 50, CardEffectRefs.DefendHalfReflect, "self", durationTurns: 1, gameplayType: CardGameplayType.Defend, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self, buffDebuff: "Reflect:50");

            yield return Approved(ScoutTreasurefinderId, "Treasurefinder", CardCategory.Action, CardEffectType.Scout, 1, 5, 2, CardEffectRefs.ScoutTreasureCountHeal, "walkable_map_cell", areaRadius: 1, gameplayType: CardGameplayType.Scout, scalingMode: CardScalingMode.ObjectsInRevealArea, targetMode: CardTargetMode.Tile);

            yield return Approved(FieldFirebombId, "Firebomb", CardCategory.Action, CardEffectType.FieldObject, 2, 2, 6, CardEffectRefs.FieldDamage, "walkable_map_cell", areaRadius: 1, gameplayType: CardGameplayType.Field, fieldObjectKind: CardFieldObjectKind.FieldDamage, durationTurns: 2, targetMode: CardTargetMode.Tile);
            yield return Approved(FieldSacredCampfireId, "Sacred Campfire", CardCategory.Action, CardEffectType.FieldObject, 3, 1, 3, CardEffectRefs.FieldHeal, "walkable_map_cell", areaRadius: 2, gameplayType: CardGameplayType.Field, fieldObjectKind: CardFieldObjectKind.ConditionalHeal, durationTurns: 4, targetMode: CardTargetMode.Tile);
            yield return Approved(FieldFlashbangId, "Flashbang", CardCategory.Action, CardEffectType.FieldObject, 2, 2, 0, CardEffectRefs.FieldImmobilizeFlashbang, "walkable_map_cell", areaRadius: 2, gameplayType: CardGameplayType.Field, fieldObjectKind: CardFieldObjectKind.MassImmobilize, durationTurns: 2, targetMode: CardTargetMode.Tile);

            yield return Approved(UtilityRedrawId, "Redraw", CardCategory.Action, CardEffectType.Utility, 1, 0, 0, CardEffectRefs.UtilityRedraw, "current_action_hand_except_self", gameplayType: CardGameplayType.Utility, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self);

            yield return DebugCard(DebugBindId, "[Debug] Bind", attackRange, 0, CardEffectRefs.DebugApplyBind, durationTurns: 2);
            yield return DebugCard(DebugSlowId, "[Debug] Slow", attackRange, 1, CardEffectRefs.DebugApplySlow, durationTurns: 3);
            yield return DebugCard(DebugRuptureId, "[Debug] Rupture Self", attackRange, 1, CardEffectRefs.DebugApplyRupture, durationTurns: 3);
            yield return DebugCard(DebugKnockbackId, "[Debug] Knockback 3", attackRange, 3, CardEffectRefs.DebugKnockback, durationTurns: 0);

        }

        private static CardCatalogEntry Approved(
            string id,
            string displayName,
            CardCategory deckType,
            CardEffectType actionType,
            int cost,
            int range,
            int amount,
            string effectRef,
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
            string additionalCost = "",
            string postActions = "",
            string choiceOptions = "",
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
                effectRef,
                targeting,
                "20_Game_Design/Card_System.md approved taxonomy + Card_Catalog_Guide workflow",
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
                additionalCost: additionalCost,
                postActions: postActions,
                choiceOptions: choiceOptions,
                buffDebuff: buffDebuff);
        }

        private static CardCatalogEntry Draft(
            string id,
            string displayName,
            CardCategory deckType,
            CardEffectType actionType,
            int cost,
            int range,
            int amount,
            string effectRef,
            string targeting,
            CardGameplayType gameplayType,
            string shapeId = null)
        {
            return new CardCatalogEntry(
                id,
                displayName,
                deckType,
                actionType,
                cost,
                range,
                amount,
                effectRef,
                targeting,
                "User-requested draft card placeholder; hidden/unplayable until explicitly implemented.",
                0,
                CardUsePhase.Action,
                CardPlayMode.ManualTarget,
                CardFieldObjectKind.None,
                0,
                CardCatalogStatus.Draft,
                gameplayType,
                CardCostMode.Fixed,
                CardTargetMode.Enemy,
                CardScalingMode.Flat,
                CreatePresentationRef(id, gameplayType),
                includeInGameplayDecks: false,
                visibleInCatalog: false,
                shapeId: shapeId);
        }

        private static CardCatalogEntry DebugCard(
            string id,
            string displayName,
            int range,
            int amount,
            string effectRef,
            int durationTurns)
        {
            return new CardCatalogEntry(
                id,
                displayName,
                CardCategory.Action,
                CardEffectType.Attack,
                0,
                range,
                amount,
                effectRef,
                "living_monster_in_range",
                "Debug card for status effect testing.",
                0,
                CardUsePhase.Action,
                CardPlayMode.ManualTarget,
                CardFieldObjectKind.None,
                durationTurns,
                CardCatalogStatus.Draft,
                CardGameplayType.Attack,
                CardCostMode.Fixed,
                CardTargetMode.Enemy,
                CardScalingMode.Flat,
                CreatePresentationRef(id, CardGameplayType.Attack),
                includeInGameplayDecks: false,
                visibleInCatalog: false);
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
