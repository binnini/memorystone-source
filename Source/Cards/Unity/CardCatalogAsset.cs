using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    [CreateAssetMenu(fileName = "CardCatalog", menuName = "Seoul Playup/Cards/Card Catalog")]
    public sealed class CardCatalogAsset : ScriptableObject
    {
        private const string DefaultDisplayName = "Approved Card System catalog";
        private const string SourceTrace = "cards.csv import";

        private static readonly string[] ExpectedHeader =
        {
            "id", "name", "description", "type", "target", "cost", "range", "shape",
            "damage", "shield", "heal", "stateEffect", "buff_debuff",
            "duration", "scout", "behaviorId", "hitCount", "costMode", "scalingMode", "targeting",
            "additionalCost", "postActions", "choiceOptions", "behaviorParams",
            "status", "includeInDecks", "visibleInCatalog", "usableWhileStunned", "illustrationId", "rarity",
            "exhaustOnPlay", "retainOnTurnEnd"
        };

        private static readonly string[] ExpectedChoiceOptionHeader =
        {
            "cardId", "optionId", "displayName", "cardText", "sortOrder"
        };

        // card_upgrades.csv — 연마(카드 강화) 저작 표면. 빈 칸 = 원본 유지, 값 있음 = 치환.
        // 값 문법은 cards.csv의 같은 컬럼과 동일하다(shape는 blast-2 같은 승급, stateEffect는 kind:amount).
        private static readonly string[] ExpectedUpgradeHeader =
        {
            "cardId", "cost", "range", "damage", "shield", "heal", "duration", "hitCount", "shape",
            "stateEffect", "buff_debuff", "scout", "descriptionOverride", "note"
        };

        private static readonly HashSet<string> KnownBehaviorIds = new HashSet<string>(StringComparer.Ordinal)
        {
            CardEffectRefs.UtilityTorch,
            CardEffectRefs.ScoutTrapDisarm,
            CardEffectRefs.StatusFineDust,
            CardEffectRefs.StatusBrokenGlass,
            CardEffectRefs.StatusBlackout,
            // T2 저주 카드 9종(2026-08-06)
            CardEffectRefs.StatusDebtNote,
            CardEffectRefs.StatusNightmare,
            CardEffectRefs.StatusLingering,
            CardEffectRefs.StatusTardiness,
            CardEffectRefs.StatusNuisance,
            CardEffectRefs.StatusCursedCharm,
            CardEffectRefs.StatusMurkyFog,
            CardEffectRefs.StatusGoblinPrank,
            CardEffectRefs.StatusVengefulGhost,
            CardEffectRefs.MoveBasic,
            CardEffectRefs.MoveDeferredMomentum,
            CardEffectRefs.MoveRandomRadius2,
            CardEffectRefs.AttackDamage,
            CardEffectRefs.AttackAreaDamage,
            CardEffectRefs.AttackMoveLinked,
            CardEffectRefs.AttackHolyLight,
            CardEffectRefs.AttackFinishingTouch,
            CardEffectRefs.AttackFinalBlow,
            CardEffectRefs.AttackDoubleHit,
            CardEffectRefs.AttackOneStrikeEnough,
            CardEffectRefs.AttackMultiplyingStrike,
            CardEffectRefs.AttackSacrifice,
            CardEffectRefs.AttackTargetShot,
            CardEffectRefs.AttackPlague,
            CardEffectRefs.DefendBlock,
            CardEffectRefs.DefendZeroThenDouble,
            CardEffectRefs.DefendHalfReflect,
            CardEffectRefs.DefendCleanseBlock,
            CardEffectRefs.DefendBlockDelayedImmobilize,
            CardEffectRefs.DefendExileRandomNegate,
            CardEffectRefs.ScoutReveal,
            CardEffectRefs.ScoutEnemyCountDamage,
            CardEffectRefs.ScoutTreasureCountHeal,
            CardEffectRefs.ScoutEnemyStun,
            CardEffectRefs.ScoutEnemyCountHealThreshold,
            CardEffectRefs.ScoutEnemyVulnerable,
            CardEffectRefs.ObjectiveInvestigate,
            CardEffectRefs.FieldDamage,
            CardEffectRefs.FieldHeal,
            CardEffectRefs.FieldLifesteal,
            CardEffectRefs.FieldDamageFirebomb,
            CardEffectRefs.FieldHealSacredCampfire,
            CardEffectRefs.FieldImmobilizeFlashbang,
            CardEffectRefs.UtilityRedraw,
            CardEffectRefs.UtilityCleanseDraw,
            CardEffectRefs.UtilityDrawOrRecover,
        };

        // behaviorParams keys each behaviorId may author. The bag is generic by design (one column instead of
        // one column per scalar), so the schema cannot catch a typo — this allow-list is what does. A behaviorId
        // absent here accepts no params at all, which is why most shipping rows leave the column empty.
        //
        // Key naming convention (fixed at the first use site, S04): lowerCamelCase, describing the scalar's
        // role rather than its unit — `threshold`, `drawCount`. Keys are behavior-scoped, so the same word may
        // be reused by an unrelated behavior with a different meaning.
        private static readonly Dictionary<string, HashSet<string>> KnownBehaviorParamKeys =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                // Entries land alongside the behaviors that read them (e.g. threshold, drawCount).
                {
                    CardEffectRefs.ScoutEnemyCountHealThreshold,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "threshold" }
                },
                {
                    CardEffectRefs.UtilityDrawOrRecover,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "drawCount" }
                },
            };

        private static readonly HashSet<string> KnownDirectionalShapes = new HashSet<string>(StringComparer.Ordinal)
        {
            "single",
            AttackShapeLibrary.Line2,
            AttackShapeLibrary.Line3,
            AttackShapeLibrary.Line4,
            AttackShapeLibrary.ConeNear,
            AttackShapeLibrary.ConeMid,
            AttackShapeLibrary.ConeWide,
            AttackShapeLibrary.TForward,
            AttackShapeLibrary.CrossNear,
            AttackShapeLibrary.CrossFar,
            AttackShapeLibrary.RingNear,
            AttackShapeLibrary.VSplit,
            AttackShapeLibrary.Pincer,
        };

        private static readonly HashSet<string> StateEffectKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // "Burn"은 D-1로 제거된 유령 값이라 카드 저작에서도 허용하지 않는다.
            "Poison", "Stun", "Slow", "Immobilize", "Blind", "VisionDown", "Rupture",
            // T1(2026-08-06): 소비만 있고 부여 경로가 없던 4종의 카드 저작 개방 — 런타임은 ApplyCardStateEffects.
            "Weaken", "Vulnerable", "Disarm", "Seal",
        };

        private static readonly HashSet<string> BuffDebuffKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Reflect", "Agility", "DamageMultiplier", "Vulnerable", "Weak", "Strength",
        };

        private static readonly HashSet<string> KnownCsvCardIds = new HashSet<string>(StringComparer.Ordinal)
        {
            ApprovedCardCatalogFactory.UtilityTorchId,
            ApprovedCardCatalogFactory.ScoutTrapDisarmId,
            // 상태 카드(C-17 / D-17). 전투 중 덱에 삽입되는 사용 불가 카드라 includeInDecks=FALSE다.
            ApprovedCardCatalogFactory.StatusFineDustId,
            ApprovedCardCatalogFactory.StatusBrokenGlassId,
            ApprovedCardCatalogFactory.StatusBlackoutId,
            // T2 저주 카드 9종(CSV-only)
            "X04",
            "X05",
            "X06",
            "X07",
            "X08",
            "X09",
            "X10",
            "X11",
            "X12",
            ApprovedCardCatalogFactory.Move1HexId,
            ApprovedCardCatalogFactory.Move2HexId,
            ApprovedCardCatalogFactory.Move3HexId,
            ApprovedCardCatalogFactory.Move4HexId,
            ApprovedCardCatalogFactory.Move5HexId,
            ApprovedCardCatalogFactory.MoveMomentumId,
            ApprovedCardCatalogFactory.MoveRandomJourneyId,
            ApprovedCardCatalogFactory.MoveFastTurtleId,
            "A00",
            ApprovedCardCatalogFactory.AttackSweepId,
            ApprovedCardCatalogFactory.AttackMoveLinkedId,
            ApprovedCardCatalogFactory.AttackHolyLightId,
            ApprovedCardCatalogFactory.AttackFinishingTouchId,
            ApprovedCardCatalogFactory.AttackFinalBlowId,
            ApprovedCardCatalogFactory.AttackDoubleHitId,
            ApprovedCardCatalogFactory.AttackOneStrikeEnoughId,
            ApprovedCardCatalogFactory.AttackMultiplyingStrikeId,
            ApprovedCardCatalogFactory.AttackMultiplyingStrikeCopyId,
            ApprovedCardCatalogFactory.AttackSacrificeId,
            ApprovedCardCatalogFactory.AttackTargetShotId,
            ApprovedCardCatalogFactory.AttackRemnantId,
            ApprovedCardCatalogFactory.AttackPlagueId,
            // T1(2026-08-06) CSV-only 신규 2종: 으름장(쇠약)·약점 간파(허점).
            "A14",
            // T5-2(2026-08-07) 유지 카드 2종: 만반의 준비(D07)·봐 둔 길(M07) — CSV-only.
            "D07",
            "M07",
            // T5-3(2026-08-07) X코스트 이동: 전력 질주(M08) — CSV-only.
            "M08",
            "D00",
            ApprovedCardCatalogFactory.DefendOldSuitId,
            ApprovedCardCatalogFactory.DefendShelterTauntId,
            ApprovedCardCatalogFactory.DefendDoubleEdgedShieldId,
            ApprovedCardCatalogFactory.DefendHeavyArmorId,
            ApprovedCardCatalogFactory.DefendHospitalizationId,
            ApprovedCardCatalogFactory.DefendTalismanShieldId,
            ApprovedCardCatalogFactory.ScoutBasicId,
            ApprovedCardCatalogFactory.ScoutMinefinderId,
            ApprovedCardCatalogFactory.ScoutTreasurefinderId,
            ApprovedCardCatalogFactory.ScoutStunFlashId,
            ApprovedCardCatalogFactory.ScoutBingoId,
            "S06",
            ApprovedCardCatalogFactory.ObjectiveInvestigateId,
            ApprovedCardCatalogFactory.FieldFirebombId,
            ApprovedCardCatalogFactory.FieldSacredCampfireId,
            ApprovedCardCatalogFactory.FieldFlashbangId,
            ApprovedCardCatalogFactory.FieldLifestealId,
            ApprovedCardCatalogFactory.FieldBounceBombId,
            ApprovedCardCatalogFactory.UtilityRedrawId,
            ApprovedCardCatalogFactory.UtilityDrawOrRecoverId,
            ApprovedCardCatalogFactory.UtilityCleanseDrawId,
        };

        [SerializeField] private string sourceId = ApprovedCardCatalogFactory.SourceId;
        [SerializeField] private string displayName = DefaultDisplayName;
        [SerializeField] private List<CardCatalogCsvRow> rows = new List<CardCatalogCsvRow>();
        [SerializeField] private List<CardChoiceOptionCsvRow> choiceOptionRows = new List<CardChoiceOptionCsvRow>();
        [SerializeField] private List<CardUpgradeCsvRow> upgradeRows = new List<CardUpgradeCsvRow>();

        public string SourceId => string.IsNullOrWhiteSpace(sourceId) ? ApprovedCardCatalogFactory.SourceId : sourceId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? DefaultDisplayName : displayName;
        public IReadOnlyList<CardCatalogCsvRow> Rows => rows;
        public IReadOnlyList<CardChoiceOptionCsvRow> ChoiceOptionRows => choiceOptionRows;
        public IReadOnlyList<CardUpgradeCsvRow> UpgradeRows => upgradeRows;

        public void SetRows(IEnumerable<CardCatalogCsvRow> importedRows)
        {
            rows = importedRows == null
                ? new List<CardCatalogCsvRow>()
                : importedRows.Select(row => row.Clone()).ToList();
        }

        public void SetChoiceOptionRows(IEnumerable<CardChoiceOptionCsvRow> importedRows)
        {
            choiceOptionRows = importedRows == null
                ? new List<CardChoiceOptionCsvRow>()
                : importedRows.Select(row => row.Clone()).ToList();
        }

        public void SetUpgradeRows(IEnumerable<CardUpgradeCsvRow> importedRows)
        {
            upgradeRows = importedRows == null
                ? new List<CardUpgradeCsvRow>()
                : importedRows.Select(row => row.Clone()).ToList();
        }

        public CardCatalogDefinition ToCardCatalogDefinition(CombatConfig config)
        {
            var choiceOptionTextsByCard = BuildChoiceOptionTextsByCard();
            var upgradesByCard = BuildUpgradeRowsByCard();
            return new CardCatalogDefinition(SourceId, DisplayName, rows.Select(row =>
            {
                var texts = choiceOptionTextsByCard.TryGetValue(row.Id, out var found) ? found : string.Empty;
                // 연마 후 엔트리는 "치환된 CSV 행"을 같은 빌드 파이프라인에 통과시켜 만든다 — shape·
                // amount·타게팅 해석이 원본과 한 코드로 일관되고, 정의 시점 치환이라 필드 카드에도 먹는다.
                var upgraded = upgradesByCard.TryGetValue(row.Id, out var upgradeRow)
                    ? BuildEntry(MergeUpgradeRow(row, upgradeRow), config, texts)
                    : null;
                return BuildEntry(row, config, texts, upgraded);
            }));
        }

        public bool ValidateRows(out string reason)
        {
            if (rows == null || rows.Count == 0)
            {
                reason = "Card catalog CSV has no rows.";
                return false;
            }

            var duplicate = rows.GroupBy(row => row.Id, StringComparer.Ordinal)
                .FirstOrDefault(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);
            if (duplicate != null)
            {
                reason = $"CSV has duplicate card id '{duplicate.Key}'.";
                return false;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                if (!ValidateRow(rows[i], i + 2, out reason))
                {
                    return false;
                }
            }

            if (!ValidateChoiceOptionRows(out reason))
            {
                return false;
            }

            if (!ValidateUpgradeRows(out reason))
            {
                return false;
            }

            var catalog = ToCardCatalogDefinition(CombatConfig.Default);
            if (!catalog.Validate(out reason))
            {
                reason = "CardCatalogDefinition validation failed: " + reason;
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static IReadOnlyList<CardCatalogCsvRow> ParseCsvText(string csvText)
        {
            var table = ParseTable(csvText, "CSV", ExpectedHeader);
            return table.Rows.Select(row => CardCatalogCsvRow.FromFields(row.Fields)).ToList();
        }

        public static IReadOnlyList<CardChoiceOptionCsvRow> ParseChoiceOptionsCsvText(string csvText)
        {
            var table = ParseTable(csvText, "Choice options CSV", ExpectedChoiceOptionHeader);
            return table.Rows.Select(row => CardChoiceOptionCsvRow.FromFields(row.Fields)).ToList();
        }

        public static IReadOnlyList<CardUpgradeCsvRow> ParseUpgradesCsvText(string csvText)
        {
            var table = ParseTable(csvText, "Card upgrades CSV", ExpectedUpgradeHeader);
            return table.Rows.Select(row => CardUpgradeCsvRow.FromFields(row.Fields)).ToList();
        }

        // 공용 CsvTable로 파싱하고 헤더 순서를 기대 스키마와 대조한다. 구조 오류(빈 파일, 빈 헤더,
        // 열 수 불일치, 미종결 인용부)는 CsvTable이 실제 파일 행 번호를 붙여 던지므로, description 등
        // 인용 개행이 섞인 행에서도 진단이 어긋나지 않는다. 공개 계약(FormatException)은 유지한다.
        private static CsvTable ParseTable(string csvText, string label, string[] expectedHeader)
        {
            CsvTable table;
            try
            {
                table = CsvTable.Parse(csvText, label);
            }
            catch (ArgumentException ex)
            {
                throw new FormatException(ex.Message, ex);
            }

            if (table.Headers.Count != expectedHeader.Length)
            {
                throw new FormatException($"{label} header has {table.Headers.Count} columns; expected {expectedHeader.Length}.");
            }

            for (var i = 0; i < expectedHeader.Length; i++)
            {
                if (!string.Equals(table.Headers[i], expectedHeader[i], StringComparison.Ordinal))
                {
                    throw new FormatException($"{label} header column {i + 1} is '{table.Headers[i]}'; expected '{expectedHeader[i]}'.");
                }
            }

            return table;
        }

        private static CardCatalogCsvRow MergeUpgradeRow(CardCatalogCsvRow baseRow, CardUpgradeCsvRow upgradeRow)
        {
            return baseRow.CloneWithUpgrade(upgradeRow);
        }

        private Dictionary<string, CardUpgradeCsvRow> BuildUpgradeRowsByCard()
        {
            return (upgradeRows ?? new List<CardUpgradeCsvRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.CardId))
                .GroupBy(row => row.CardId.Trim(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        private bool ValidateUpgradeRows(out string reason)
        {
            reason = string.Empty;
            if (upgradeRows == null || upgradeRows.Count == 0)
            {
                return true;
            }

            var rowByCard = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < upgradeRows.Count; i++)
            {
                var row = upgradeRows[i];
                var lineNumber = i + 2;
                if (row == null || string.IsNullOrWhiteSpace(row.CardId))
                {
                    reason = $"upgrades CSV row {lineNumber} has an empty cardId.";
                    return false;
                }

                if (!seen.Add(row.CardId.Trim()))
                {
                    reason = $"upgrades CSV has duplicate cardId '{row.CardId}'.";
                    return false;
                }

                if (!rowByCard.TryGetValue(row.CardId.Trim(), out var cardRow))
                {
                    reason = $"upgrades CSV row {lineNumber} references unknown cardId '{row.CardId}'.";
                    return false;
                }

                // D-1: 저주(X)·includeInDecks=FALSE 카드는 연마 행 금지 — 후보에 나올 수 없는 카드에
                // 연마 값을 저작하면 데이터가 거짓말이 된다.
                ResolveType(cardRow.Type, out _, out var actionType, out _);
                if (actionType == CardEffectType.Status)
                {
                    reason = $"upgrades CSV row {lineNumber} targets curse/status card '{row.CardId}'; curse cards cannot be refined.";
                    return false;
                }

                if (!ParseBool(cardRow.IncludeInDecks, true))
                {
                    reason = $"upgrades CSV row {lineNumber} targets '{row.CardId}' with includeInDecks=FALSE; only deck cards can be refined.";
                    return false;
                }

                if (!row.HasAnySubstitution)
                {
                    reason = $"upgrades CSV row {lineNumber} for '{row.CardId}' substitutes nothing; remove the row (no row = not refinable).";
                    return false;
                }

                // damage/shield/heal은 amount 해석에서 damage→shield→heal 우선순위를 타므로, 원본이
                // 비워 둔 축에 값을 저작하면 조용히 무시되거나 카드 성격이 뒤틀린다 — 저작 오류로 거부.
                if (!ValidateAmountAxis(row.Damage, cardRow.Damage, "damage", lineNumber, row.CardId, ref reason)
                    || !ValidateAmountAxis(row.Shield, cardRow.Shield, "shield", lineNumber, row.CardId, ref reason)
                    || !ValidateAmountAxis(row.Heal, cardRow.Heal, "heal", lineNumber, row.CardId, ref reason))
                {
                    return false;
                }

                var merged = MergeUpgradeRow(cardRow, row);
                if (!ValidateRow(merged, lineNumber, out var mergedReason))
                {
                    reason = $"upgrades CSV row {lineNumber} for '{row.CardId}' produces an invalid merged card: {mergedReason}";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateAmountAxis(string upgradeValue, string baseValue, string columnName, int lineNumber, string cardId, ref string reason)
        {
            if (!string.IsNullOrWhiteSpace(upgradeValue) && string.IsNullOrWhiteSpace(baseValue))
            {
                reason = $"upgrades CSV row {lineNumber} authors '{columnName}' for '{cardId}' but the base card leaves it empty.";
                return false;
            }

            return true;
        }

        private static CardCatalogEntry BuildEntry(CardCatalogCsvRow row, CombatConfig config, string choiceOptionTexts, CardCatalogEntry upgradedEntry = null)
        {
            ResolveType(row.Type, out var deckType, out var actionType, out var defaultGameplayType);
            ResolveShape(row.Shape, out var areaRadius, out var shapeId);
            ResolveTargeting(
                row.BehaviorId,
                row.Target,
                row.Targeting,
                areaRadius,
                !string.IsNullOrWhiteSpace(row.ChoiceOptions),
                out var targetMode,
                out var playMode,
                out var targeting);

            var range = ResolveNumber(row.Range, config, "range");
            if (actionType == CardEffectType.Attack && areaRadius > 0 && range == 0)
            {
                // Self-centred area attack (range 0 + blast shape): no tile-targeting step — using the
                // card auto-fires a blast centred on the player. Declared purely by data, no per-card code.
                targetMode = CardTargetMode.SelfArea;
            }

            var amount = ResolveAmount(row, config);
            var status = ParseStatus(row.Status);
            var gameplayType = ResolveGameplayType(row.BehaviorId, defaultGameplayType);
            var fieldObjectKind = ResolveFieldObjectKind(row.BehaviorId);
            var costMode = ResolveCostMode(row.CostMode, row.BehaviorId);
            var scalingMode = ResolveScalingMode(row.ScalingMode, row.BehaviorId);
            var duration = ParseOptionalInt(row.Duration, 0, "duration");
            var hitCount = ParseOptionalInt(row.HitCount, ResolveHitCount(row.BehaviorId), "hitCount");

            return new CardCatalogEntry(
                row.Id,
                row.Name,
                deckType,
                actionType,
                ResolveNumber(row.Cost, config, "cost"),
                range,
                amount,
                row.BehaviorId,
                targeting,
                SourceTrace,
                areaRadius,
                CardUsePhase.Default,
                playMode,
                fieldObjectKind,
                duration,
                status,
                gameplayType,
                costMode,
                targetMode,
                scalingMode,
                CreatePresentationRef(row.Id, gameplayType, row.IllustrationId),
                ParseBool(row.IncludeInDecks, true),
                ParseBool(row.VisibleInCatalog, true),
                shapeId,
                hitCount,
                Normalize(row.AdditionalCost),
                Normalize(row.PostActions),
                Normalize(row.ChoiceOptions),
                Normalize(choiceOptionTexts),
                Normalize(row.Description),
                ParseRarity(row.Rarity),
                ParseBool(row.UsableWhileStunned, false),
                Normalize(row.BehaviorParams),
                Normalize(row.StateEffect),
                Normalize(row.BuffDebuff),
                ParseBool(row.ExhaustOnPlay, false),
                ParseBool(row.RetainOnTurnEnd, false),
                upgradedEntry,
                // I-08(WS-I): heal 축은 Amount로 접히기 전의 저작값을 따로 보존한다 — damage와 heal을
                // 동시에 저작한 카드(A03)의 회복량이 죽은 데이터가 되지 않도록. 연마 병합(CloneWithUpgrade)은
                // CSV 행 단계라 이 경로를 그대로 다시 지난다.
                healAmount: string.IsNullOrWhiteSpace(row.Heal) ? 0 : ResolveNumber(row.Heal, config, "heal"));
        }

        private Dictionary<string, string> BuildChoiceOptionTextsByCard()
        {
            return (choiceOptionRows ?? new List<CardChoiceOptionCsvRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.CardId) && !string.IsNullOrWhiteSpace(row.OptionId))
                .GroupBy(row => row.CardId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => string.Join(";", group
                        .OrderBy(row => row.SortOrderValue)
                        .ThenBy(row => row.OptionId, StringComparer.Ordinal)
                        .Select(row => $"{Normalize(row.OptionId)}|{Normalize(row.DisplayName)}|{Normalize(row.CardText)}")),
                    StringComparer.Ordinal);
        }

        private bool ValidateChoiceOptionRows(out string reason)
        {
            reason = string.Empty;
            if (choiceOptionRows == null || choiceOptionRows.Count == 0)
            {
                return true;
            }

            var rowByCard = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < choiceOptionRows.Count; i++)
            {
                var row = choiceOptionRows[i];
                var lineNumber = i + 2;
                if (row == null || string.IsNullOrWhiteSpace(row.CardId) || string.IsNullOrWhiteSpace(row.OptionId))
                {
                    reason = $"choice options CSV row {lineNumber} has an empty cardId or optionId.";
                    return false;
                }

                if (!rowByCard.TryGetValue(row.CardId, out var cardRow))
                {
                    reason = $"choice options CSV row {lineNumber} references unknown cardId '{row.CardId}'.";
                    return false;
                }

                if (!CardBehaviorMetadata.TryGetChoiceOption(cardRow.ChoiceOptions, row.OptionId, out _))
                {
                    reason = $"choice options CSV row {lineNumber} references optionId '{row.OptionId}' that is not declared by card '{row.CardId}'.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(row.DisplayName) || string.IsNullOrWhiteSpace(row.CardText))
                {
                    reason = $"choice options CSV row {lineNumber} must define displayName and cardText.";
                    return false;
                }

                if (!int.TryParse(row.SortOrder, out _))
                {
                    reason = $"choice options CSV row {lineNumber} has invalid sortOrder '{row.SortOrder}'.";
                    return false;
                }

                if (!seen.Add($"{row.CardId}:{row.OptionId}"))
                {
                    reason = $"choice options CSV has duplicate option '{row.CardId}:{row.OptionId}'.";
                    return false;
                }
            }

            foreach (var group in choiceOptionRows.GroupBy(row => row.CardId, StringComparer.Ordinal))
            {
                var cardRow = rowByCard[group.Key];
                var declared = CardBehaviorMetadata.ParseChoiceOptions(cardRow.ChoiceOptions)
                    .Select(option => option.OptionId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var described = group.Select(row => row.OptionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!declared.SetEquals(described))
                {
                    reason = $"choice options CSV must describe every choiceOptions entry for card '{group.Key}'.";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateRow(CardCatalogCsvRow row, int lineNumber, out string reason)
        {
            if (string.IsNullOrWhiteSpace(row.Id))
            {
                reason = $"CSV row {lineNumber} has an empty id.";
                return false;
            }

            if (!KnownCsvCardIds.Contains(row.Id))
            {
                reason = $"CSV row {lineNumber} has unknown CSV card id '{row.Id}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(row.BehaviorId) || !KnownBehaviorIds.Contains(row.BehaviorId))
            {
                reason = $"CSV row {lineNumber} has unregistered behaviorId '{row.BehaviorId}'.";
                return false;
            }

                if (!TryResolveType(row.Type))
                {
                    reason = $"CSV row {lineNumber} has unsupported type '{row.Type}'.";
                    return false;
                }

            if (!TryResolveTarget(row.Target))
            {
                reason = $"CSV row {lineNumber} has unsupported target '{row.Target}'.";
                return false;
            }

            if (!TryParseStatus(row.Status))
            {
                reason = $"CSV row {lineNumber} has unsupported status '{row.Status}'.";
                return false;
            }

            if (!TryParseOptionalEnum<CardCostMode>(row.CostMode))
            {
                reason = $"CSV row {lineNumber} has unsupported costMode '{row.CostMode}'.";
                return false;
            }

            if (!TryParseOptionalEnum<CardScalingMode>(row.ScalingMode))
            {
                reason = $"CSV row {lineNumber} has unsupported scalingMode '{row.ScalingMode}'.";
                return false;
            }

            if (!ValidateNumberOrToken(row.Cost, allowEmpty: false)
                || !ValidateNumberOrToken(row.Range, allowEmpty: false)
                || !ValidateNumberOrToken(row.Damage, allowEmpty: true)
                || !ValidateNumberOrToken(row.Shield, allowEmpty: true)
                || !ValidateNumberOrToken(row.Heal, allowEmpty: true)
                || !ValidateNumberOrToken(row.HitCount, allowEmpty: true))
            {
                reason = $"CSV row {lineNumber} has an invalid numeric token.";
                return false;
            }

            if (!TryResolveShape(row.Shape))
            {
                reason = $"CSV row {lineNumber} has unknown shape '{row.Shape}'.";
                return false;
            }

            if (!ValidateEffectList(row.StateEffect, StateEffectKinds))
            {
                reason = $"CSV row {lineNumber} has unsupported stateEffect '{row.StateEffect}'.";
                return false;
            }

            if (!ValidateEffectList(row.BuffDebuff, BuffDebuffKinds))
            {
                reason = $"CSV row {lineNumber} has unsupported buff_debuff '{row.BuffDebuff}'.";
                return false;
            }

            // These two columns used to be validated but never read (the entry had no field for them), so a
            // typo'd amount was invisible — the authored value simply lost to a hardcoded fallback. Now that
            // they drive gameplay, the amount has to parse.
            if (!CardBehaviorMetadata.ValidateEffectListFormat(row.StateEffect)
                || !CardBehaviorMetadata.ValidateEffectListFormat(row.BuffDebuff))
            {
                reason = $"CSV row {lineNumber} has a malformed stateEffect/buff_debuff amount; expected unique 'kind:정수' items.";
                return false;
            }

            if (!CardBehaviorMetadata.ValidateAdditionalCost(row.AdditionalCost)
                || !CardBehaviorMetadata.ValidatePostActions(row.PostActions)
                || !CardBehaviorMetadata.ValidateChoiceOptions(row.ChoiceOptions))
            {
                reason = $"CSV row {lineNumber} has an invalid behavior metadata token.";
                return false;
            }

            if (!CardBehaviorMetadata.ValidateBehaviorParams(row.BehaviorParams))
            {
                reason = $"CSV row {lineNumber} has an invalid behaviorParams token '{row.BehaviorParams}'; expected unique '키:정수' items.";
                return false;
            }

            if (!TryFindUnknownBehaviorParamKey(row, out var unknownParamKey))
            {
                reason = $"CSV row {lineNumber} authors behaviorParams key '{unknownParamKey}' that behaviorId '{row.BehaviorId}' does not read.";
                return false;
            }

            // Unlike includeInDecks/visibleInCatalog (lenient by legacy convention), a malformed
            // usableWhileStunned fails the import: silently defaulting to false would leave a 정화 card
            // unusable exactly when it is needed, with nothing in the data to show why.
            if (!ValidateOptionalBool(row.UsableWhileStunned))
            {
                reason = $"CSV row {lineNumber} has an invalid usableWhileStunned '{row.UsableWhileStunned}'; expected TRUE/FALSE or empty.";
                return false;
            }

            // usableWhileStunned와 같은 이유로 엄격하다: 오타가 조용히 false로 떨어지면 소멸 저작 카드가
            // 버림 더미로 돌아가는데, 데이터 어디에도 그 이유가 남지 않는다.
            if (!ValidateOptionalBool(row.ExhaustOnPlay))
            {
                reason = $"CSV row {lineNumber} has an invalid exhaustOnPlay '{row.ExhaustOnPlay}'; expected TRUE/FALSE or empty.";
                return false;
            }

            if (!ValidateOptionalBool(row.RetainOnTurnEnd))
            {
                reason = $"CSV row {lineNumber} has an invalid retainOnTurnEnd '{row.RetainOnTurnEnd}'; expected TRUE/FALSE or empty.";
                return false;
            }

            if (ResolveFieldObjectKind(row.BehaviorId) != CardFieldObjectKind.None
                && ParseOptionalInt(row.Duration, 0, "duration") <= 0)
            {
                reason = $"CSV row {lineNumber} is a field object but duration is not positive.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // Returns false (with the offending key) when the row authors a behaviorParams key its behaviorId
        // does not consume — the format check alone cannot catch that.
        private static bool TryFindUnknownBehaviorParamKey(CardCatalogCsvRow row, out string unknownKey)
        {
            unknownKey = string.Empty;
            var keys = CardBehaviorMetadata.ParseBehaviorParamKeys(row.BehaviorParams);
            if (keys.Count == 0)
            {
                return true;
            }

            KnownBehaviorParamKeys.TryGetValue(Normalize(row.BehaviorId), out var allowed);
            foreach (var key in keys)
            {
                if (allowed == null || !allowed.Contains(key))
                {
                    unknownKey = key;
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateOptionalBool(string rawValue)
        {
            var value = Normalize(rawValue);
            return string.IsNullOrEmpty(value) || bool.TryParse(value, out _);
        }

        private static int ResolveAmount(CardCatalogCsvRow row, CombatConfig config)
        {
            if (!string.IsNullOrWhiteSpace(row.Damage))
            {
                return ResolveNumber(row.Damage, config, "damage");
            }

            if (!string.IsNullOrWhiteSpace(row.Shield))
            {
                return ResolveNumber(row.Shield, config, "shield");
            }

            if (!string.IsNullOrWhiteSpace(row.Heal))
            {
                return ResolveNumber(row.Heal, config, "heal");
            }

            // Only the intended derivation survives: a plain move card's "amount" is its range. The former
            // MoveDeferredMomentum(2) / DefendHalfReflect(50) fallbacks were the *effective* values of M05
            // and D03 while their authored buff_debuff was ignored — those now come from buff_debuff.
            // ScoutTreasureCountHeal(2) was already unreachable (S02 authors heal=2).
            switch (row.BehaviorId)
            {
                case CardEffectRefs.MoveBasic:
                case CardEffectRefs.MoveRandomRadius2:
                    return ResolveNumber(row.Range, config, "range");
                // 횃불의 amount는 초기 시야 반경이다. damage/shield/heal 어디에도 속하지 않아 이 유도가
                // 없으면 0으로 떨어지고, GrantTorchLight가 `amount <= 0`에서 조용히 되돌아가 **카드가
                // 코스트만 쓰고 아무 일도 하지 않는다**(2026-08-02 실플레이에서 발견).
                // duration에서 읽는 이유는 C-14의 규칙이 "지속시간 = 초기 반경"이기 때문이다 — 한 칸에서
                // 둘 다 유도하면 "반경 3인데 5턴" 같은 저작이 애초에 표현 불가능해진다.
                case CardEffectRefs.UtilityTorch:
                    return ResolveNumber(row.Duration, config, "duration");
                default:
                    return 0;
            }
        }

        private static void ResolveType(
            string rawType,
            out CardCategory deckType,
            out CardEffectType actionType,
            out CardGameplayType gameplayType)
        {
            var type = Normalize(rawType);
            switch (type)
            {
                case "Move":
                case "이동":
                    deckType = CardCategory.Movement;
                    actionType = CardEffectType.Move;
                    gameplayType = CardGameplayType.Move;
                    return;
                case "Attack":
                case "공격":
                    deckType = CardCategory.Action;
                    actionType = CardEffectType.Attack;
                    gameplayType = CardGameplayType.Attack;
                    return;
                case "Defend":
                case "방어":
                    deckType = CardCategory.Action;
                    actionType = CardEffectType.Defend;
                    gameplayType = CardGameplayType.Defend;
                    return;
                case "Scout":
                case "정찰":
                    deckType = CardCategory.Action;
                    actionType = CardEffectType.Scout;
                    gameplayType = CardGameplayType.Scout;
                    return;
                case "Investigate":
                case "조사":
                    deckType = CardCategory.Action;
                    actionType = CardEffectType.Investigate;
                    gameplayType = CardGameplayType.Utility;
                    return;
                case "Field":
                case "FieldObject":
                case "필드":
                    deckType = CardCategory.Action;
                    actionType = CardEffectType.FieldObject;
                    gameplayType = CardGameplayType.Field;
                    return;
                case "Utility":
                case "유틸리티":
                    deckType = CardCategory.Action;
                    actionType = CardEffectType.Utility;
                    gameplayType = CardGameplayType.Utility;
                    return;
                case "Status":
                case "상태": // 개편 전 별칭(T2) — 신규 저작은 '저주'
                case "저주":
                    // 상태 카드(C-17)는 행동 덱에 섞이지만 결코 사용되지 않는다. GameplayType은 Utility를
                    // 재사용한다 — 그 축은 카드 배경·분류 표현용이라 신규 값을 만들면 프리팹·테마 저작이
                    // 따라오고, 사용 불가는 EffectType이 이미 표현한다.
                    deckType = CardCategory.Action;
                    actionType = CardEffectType.Status;
                    gameplayType = CardGameplayType.Utility;
                    return;
                default:
                    throw new FormatException($"Unsupported card type '{rawType}'.");
            }
        }

        private static bool TryResolveType(string rawType)
        {
            try
            {
                ResolveType(rawType, out _, out _, out _);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
        private static void ResolveTargeting(
            string behaviorId,
            string rawTarget,
            string rawTargeting,
            int areaRadius,
            bool hasChoiceOptions,
            out CardTargetMode targetMode,
            out CardPlayMode playMode,
            out string targeting)
        {
            var explicitTargeting = Normalize(rawTargeting);
            playMode = CardPlayMode.ManualTarget;
            if (hasChoiceOptions)
            {
                targetMode = CardTargetMode.OptionThenTarget;
                playMode = CardPlayMode.Choice;
                targeting = string.IsNullOrEmpty(explicitTargeting) ? "choice_self_or_enemy" : explicitTargeting;
                return;
            }

            switch (behaviorId)
            {
                case CardEffectRefs.MoveBasic:
                    targetMode = CardTargetMode.Tile;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "reachable_hex" : explicitTargeting;
                    return;
                case CardEffectRefs.MoveRandomRadius2:
                    targetMode = CardTargetMode.RandomReachable;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "random_reachable_hex_radius_2" : explicitTargeting;
                    return;
                case CardEffectRefs.AttackAreaDamage:
                    targetMode = CardTargetMode.Enemy;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "living_monsters_in_area" : explicitTargeting;
                    return;
                case CardEffectRefs.AttackDamage:
                    targetMode = CardTargetMode.Enemy;
                    targeting = string.IsNullOrEmpty(explicitTargeting)
                        ? (areaRadius > 0 ? "living_monsters_in_area" : "living_monster_in_range")
                        : explicitTargeting;
                    return;
                case CardEffectRefs.AttackHolyLight:
                    targetMode = CardTargetMode.OptionThenTarget;
                    playMode = CardPlayMode.Choice;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "choice_self_or_enemy" : explicitTargeting;
                    return;
                case CardEffectRefs.AttackOneStrikeEnough:
                    targetMode = CardTargetMode.Enemy;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "living_monster_in_range_2" : explicitTargeting;
                    return;
                case CardEffectRefs.AttackSacrifice:
                    targetMode = CardTargetMode.Enemy;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "living_monster_and_hand_card" : explicitTargeting;
                    return;
                case CardEffectRefs.AttackTargetShot:
                    targetMode = CardTargetMode.Enemy;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "living_monster_in_range_3" : explicitTargeting;
                    return;
                case CardEffectRefs.ObjectiveInvestigate:
                    targetMode = CardTargetMode.Tile;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "revealed_objective_in_range" : explicitTargeting;
                    return;
                case CardEffectRefs.UtilityRedraw:
                    targetMode = CardTargetMode.Self;
                    playMode = CardPlayMode.Self;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "current_action_hand_except_self" : explicitTargeting;
                    return;
            }

            switch (Normalize(rawTarget).ToLowerInvariant())
            {
                case "self":
                    targetMode = CardTargetMode.Self;
                    playMode = CardPlayMode.Self;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "self" : explicitTargeting;
                    return;
                case "tile":
                    targetMode = CardTargetMode.Tile;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "walkable_map_cell" : explicitTargeting;
                    return;
                case "enemy":
                    targetMode = CardTargetMode.Enemy;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "living_monster_in_range" : explicitTargeting;
                    return;
                case "none":
                    // 상태 카드(C-17): 대상이 없다. 겨눌 것이 없으므로 targeting도 비운다 —
                    // 어차피 세 게이트가 전부 막지만, 저작이 "self"를 빌려 쓰면 대상이 있는 것처럼 읽힌다.
                    targetMode = CardTargetMode.None;
                    targeting = explicitTargeting ?? string.Empty;
                    return;
                default:
                    throw new FormatException($"Unsupported target '{rawTarget}'.");
            }
        }

        private static bool TryResolveTarget(string rawTarget)
        {
            var target = Normalize(rawTarget).ToLowerInvariant();
            // "none" = 상태 카드(C-17). 겨눌 것이 없다.
            return target == "self" || target == "tile" || target == "enemy" || target == "ally" || target == "none";
        }

        private static void ResolveShape(string rawShape, out int areaRadius, out string shapeId)
        {
            var shape = Normalize(rawShape);
            if (string.IsNullOrEmpty(shape) || shape == "single" || shape == "blast-0")
            {
                areaRadius = 0;
                shapeId = null;
                return;
            }

            if (shape.StartsWith("blast-", StringComparison.Ordinal))
            {
                areaRadius = ParseOptionalInt(shape.Substring("blast-".Length), 0, "shape radius");
                shapeId = null;
                return;
            }

            if (KnownDirectionalShapes.Contains(shape))
            {
                areaRadius = 0;
                shapeId = shape;
                return;
            }

            throw new FormatException($"Unsupported shape '{rawShape}'.");
        }

        private static bool TryResolveShape(string rawShape)
        {
            try
            {
                ResolveShape(rawShape, out _, out _);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static int ResolveNumber(string rawValue, CombatConfig config, string columnName)
        {
            var value = Normalize(rawValue);
            switch (value)
            {
                case "{AttackRange}":
                    return Math.Max(1, config.AttackRange);
                case "{AttackDamage}":
                    return Math.Max(1, config.AttackDamage);
                case "{MaxKi}":
                    return config.MaxKi;
            }

            return ParseOptionalInt(value, 0, columnName);
        }

        private static bool ValidateNumberOrToken(string rawValue, bool allowEmpty)
        {
            var value = Normalize(rawValue);
            if (string.IsNullOrEmpty(value))
            {
                return allowEmpty;
            }

            if (value.StartsWith("{", StringComparison.Ordinal) || value.EndsWith("}", StringComparison.Ordinal))
            {
                return value == "{AttackRange}" || value == "{AttackDamage}" || value == "{MaxKi}";
            }

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        private static int ParseOptionalInt(string rawValue, int fallback, string columnName)
        {
            var value = Normalize(rawValue);
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            throw new FormatException($"Column '{columnName}' value '{rawValue}' is not an integer.");
        }

        private static CardCatalogStatus ParseStatus(string rawStatus)
        {
            if (Enum.TryParse<CardCatalogStatus>(Normalize(rawStatus), ignoreCase: false, out var status))
            {
                return status;
            }

            throw new FormatException($"Unsupported status '{rawStatus}'.");
        }

        private static bool TryParseStatus(string rawStatus) =>
            Enum.TryParse<CardCatalogStatus>(Normalize(rawStatus), ignoreCase: false, out _);

        // Lenient by design: blank/unknown rarity falls back to Basic so the factory-exported seed CSV
        // (which leaves rarity empty) and any legacy rows still import. Basic cards never drop as rewards.
        private static CardRarity ParseRarity(string rawRarity)
        {
            return Enum.TryParse<CardRarity>(Normalize(rawRarity), ignoreCase: true, out var rarity)
                ? rarity
                : CardRarity.Basic;
        }

        private static bool ParseBool(string rawValue, bool fallback)
        {
            var value = Normalize(rawValue);
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            return bool.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private static CardGameplayType ResolveGameplayType(string behaviorId, CardGameplayType defaultGameplayType)
        {
            if (behaviorId == CardEffectRefs.MoveDeferredMomentum)
            {
                return CardGameplayType.Buff;
            }

            return defaultGameplayType;
        }

        private static CardFieldObjectKind ResolveFieldObjectKind(string behaviorId)
        {
            switch (behaviorId)
            {
                case CardEffectRefs.FieldDamage:
                case CardEffectRefs.FieldDamageFirebomb:
                    return CardFieldObjectKind.FieldDamage;
                case CardEffectRefs.FieldHeal:
                case CardEffectRefs.FieldHealSacredCampfire:
                    return CardFieldObjectKind.ConditionalHeal;
                case CardEffectRefs.FieldImmobilizeFlashbang:
                    return CardFieldObjectKind.MassImmobilize;
                case CardEffectRefs.FieldLifesteal:
                    return CardFieldObjectKind.LifestealDamage;
                default:
                    return CardFieldObjectKind.None;
            }
        }

        private static CardCostMode ResolveCostMode(string rawCostMode, string behaviorId)
        {
            // costMode is authored entirely in cards.csv (the source of truth). No behavior-based
            // magic defaults — whatever the CSV says (including empty → Fixed) is what ships.
            return TryParseOptionalEnum<CardCostMode>(rawCostMode, out var explicitMode)
                ? explicitMode
                : CardCostMode.Fixed;
        }

        private static CardScalingMode ResolveScalingMode(string rawScalingMode, string behaviorId)
        {
            // scalingMode is authored entirely in cards.csv (the source of truth).
            return TryParseOptionalEnum<CardScalingMode>(rawScalingMode, out var explicitMode)
                ? explicitMode
                : CardScalingMode.Flat;
        }

        private static int ResolveHitCount(string behaviorId)
        {
            // hitCount is authored entirely in cards.csv (the source of truth); default to a single hit.
            return 1;
        }

        private static bool ValidateEffectList(string rawList, HashSet<string> allowedKinds)
        {
            var list = Normalize(rawList);
            if (string.IsNullOrEmpty(list))
            {
                return true;
            }

            var items = list.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                var trimmed = item.Trim();
                var kindEnd = trimmed.IndexOf(':');
                if (kindEnd <= 0)
                {
                    return false;
                }

                var kind = trimmed.Substring(0, kindEnd).Trim();
                if (!allowedKinds.Contains(kind))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseOptionalEnum<TEnum>(string rawValue) where TEnum : struct
        {
            return string.IsNullOrEmpty(Normalize(rawValue))
                || (Enum.TryParse<TEnum>(Normalize(rawValue), ignoreCase: false, out var parsed)
                    && Enum.IsDefined(typeof(TEnum), parsed));
        }

        private static bool TryParseOptionalEnum<TEnum>(string rawValue, out TEnum parsed) where TEnum : struct
        {
            var value = Normalize(rawValue);
            if (string.IsNullOrEmpty(value))
            {
                parsed = default;
                return false;
            }

            return Enum.TryParse(value, ignoreCase: false, out parsed)
                && Enum.IsDefined(typeof(TEnum), parsed);
        }

        private static CardPresentationRef CreatePresentationRef(string id, CardGameplayType gameplayType, string illustrationId)
        {
            var type = gameplayType.ToString().ToLowerInvariant();
            return new CardPresentationRef(
                $"card.presentation.{id}",
                $"card.frame.{type}",
                $"card.icon.{type}",
                Normalize(illustrationId),
                $"card.vfx.{id}",
                $"card.sfx.{type}");
        }

        private static string Normalize(string value) => (value ?? string.Empty).Trim();
    }

    [Serializable]
    public sealed class CardCatalogCsvRow
    {
        [SerializeField] private string id;
        [SerializeField] private string name;
        [SerializeField] private string description;
        [SerializeField] private string type;
        [SerializeField] private string target;
        [SerializeField] private string cost;
        [SerializeField] private string range;
        [SerializeField] private string shape;
        [SerializeField] private string damage;
        [SerializeField] private string shield;
        [SerializeField] private string heal;
        [SerializeField] private string stateEffect;
        [SerializeField] private string buffDebuff;
        [SerializeField] private string duration;
        [SerializeField] private string scout;
        [SerializeField] private string behaviorId;
        [SerializeField] private string hitCount;
        [SerializeField] private string costMode;
        [SerializeField] private string scalingMode;
        [SerializeField] private string targeting;
        [SerializeField] private string additionalCost;
        [SerializeField] private string postActions;
        [SerializeField] private string choiceOptions;
        [SerializeField] private string behaviorParams;
        [SerializeField] private string status;
        [SerializeField] private string includeInDecks;
        [SerializeField] private string visibleInCatalog;
        [SerializeField] private string usableWhileStunned;
        [SerializeField] private string illustrationId;
        [SerializeField] private string rarity;
        [SerializeField] private string exhaustOnPlay;
        [SerializeField] private string retainOnTurnEnd;

        public string Id => id;
        public string Name => name;
        public string Description => description;
        public string Type => type;
        public string Target => target;
        public string Cost => cost;
        public string Range => range;
        public string Shape => shape;
        public string Damage => damage;
        public string Shield => shield;
        public string Heal => heal;
        public string StateEffect => stateEffect;
        public string BuffDebuff => buffDebuff;
        public string Duration => duration;
        public string Scout => scout;
        public string BehaviorId => behaviorId;
        public string HitCount => hitCount;
        public string CostMode => costMode;
        public string ScalingMode => scalingMode;
        public string Targeting => targeting;
        public string AdditionalCost => additionalCost;
        public string PostActions => postActions;
        public string ChoiceOptions => choiceOptions;
        public string BehaviorParams => behaviorParams;
        public string Status => status;
        public string IncludeInDecks => includeInDecks;
        public string VisibleInCatalog => visibleInCatalog;
        public string UsableWhileStunned => usableWhileStunned;
        public string IllustrationId => illustrationId;
        public string Rarity => rarity;
        public string ExhaustOnPlay => exhaustOnPlay;
        public string RetainOnTurnEnd => retainOnTurnEnd;

        public static CardCatalogCsvRow FromFields(IReadOnlyList<string> fields)
        {
            if (fields == null || fields.Count != 32)
            {
                throw new ArgumentException("A card catalog CSV row must have exactly 32 fields.", nameof(fields));
            }

            return new CardCatalogCsvRow
            {
                id = fields[0],
                name = fields[1],
                description = fields[2],
                type = fields[3],
                target = fields[4],
                cost = fields[5],
                range = fields[6],
                shape = fields[7],
                damage = fields[8],
                shield = fields[9],
                heal = fields[10],
                stateEffect = fields[11],
                buffDebuff = fields[12],
                duration = fields[13],
                scout = fields[14],
                behaviorId = fields[15],
                hitCount = fields[16],
                costMode = fields[17],
                scalingMode = fields[18],
                targeting = fields[19],
                additionalCost = fields[20],
                postActions = fields[21],
                choiceOptions = fields[22],
                behaviorParams = fields[23],
                status = fields[24],
                includeInDecks = fields[25],
                visibleInCatalog = fields[26],
                usableWhileStunned = fields[27],
                illustrationId = fields[28],
                rarity = fields[29],
                exhaustOnPlay = fields[30],
                retainOnTurnEnd = fields[31],
            };
        }

        public CardCatalogCsvRow Clone()
        {
            return new CardCatalogCsvRow
            {
                id = id,
                name = name,
                description = description,
                type = type,
                target = target,
                cost = cost,
                range = range,
                shape = shape,
                damage = damage,
                shield = shield,
                heal = heal,
                stateEffect = stateEffect,
                buffDebuff = buffDebuff,
                duration = duration,
                scout = scout,
                behaviorId = behaviorId,
                hitCount = hitCount,
                costMode = costMode,
                scalingMode = scalingMode,
                targeting = targeting,
                additionalCost = additionalCost,
                postActions = postActions,
                choiceOptions = choiceOptions,
                behaviorParams = behaviorParams,
                status = status,
                includeInDecks = includeInDecks,
                visibleInCatalog = visibleInCatalog,
                usableWhileStunned = usableWhileStunned,
                illustrationId = illustrationId,
                rarity = rarity,
                exhaustOnPlay = exhaustOnPlay,
                retainOnTurnEnd = retainOnTurnEnd,
            };
        }

        /// <summary>
        /// 연마(카드 강화) 행을 이 행에 겹쳐 「연마 후 카드」의 CSV 행을 만든다. 빈 칸 = 원본 유지.
        /// 카드명은 D-6 표기 규칙대로 <c>+</c> 접미가 붙는다(예: "낡은 방어구+").
        /// </summary>
        public CardCatalogCsvRow CloneWithUpgrade(CardUpgradeCsvRow upgrade)
        {
            var merged = Clone();
            merged.name = (name ?? string.Empty).Trim() + "+";
            merged.cost = Substitute(upgrade.Cost, cost);
            merged.range = Substitute(upgrade.Range, range);
            merged.damage = Substitute(upgrade.Damage, damage);
            merged.shield = Substitute(upgrade.Shield, shield);
            merged.heal = Substitute(upgrade.Heal, heal);
            merged.duration = Substitute(upgrade.Duration, duration);
            merged.hitCount = Substitute(upgrade.HitCount, hitCount);
            merged.shape = Substitute(upgrade.Shape, shape);
            merged.stateEffect = Substitute(upgrade.StateEffect, stateEffect);
            merged.buffDebuff = Substitute(upgrade.BuffDebuff, buffDebuff);
            merged.scout = Substitute(upgrade.Scout, scout);
            merged.description = Substitute(upgrade.DescriptionOverride, description);
            return merged;
        }

        private static string Substitute(string upgradeValue, string baseValue) =>
            string.IsNullOrWhiteSpace(upgradeValue) ? baseValue : upgradeValue.Trim();
    }

    [Serializable]
    public sealed class CardUpgradeCsvRow
    {
        [SerializeField] private string cardId;
        [SerializeField] private string cost;
        [SerializeField] private string range;
        [SerializeField] private string damage;
        [SerializeField] private string shield;
        [SerializeField] private string heal;
        [SerializeField] private string duration;
        [SerializeField] private string hitCount;
        [SerializeField] private string shape;
        [SerializeField] private string stateEffect;
        [SerializeField] private string buffDebuff;
        [SerializeField] private string scout;
        [SerializeField] private string descriptionOverride;
        [SerializeField] private string note;

        public string CardId => cardId;
        public string Cost => cost;
        public string Range => range;
        public string Damage => damage;
        public string Shield => shield;
        public string Heal => heal;
        public string Duration => duration;
        public string HitCount => hitCount;
        public string Shape => shape;
        public string StateEffect => stateEffect;
        public string BuffDebuff => buffDebuff;
        public string Scout => scout;
        public string DescriptionOverride => descriptionOverride;
        public string Note => note;

        /// <summary>note를 제외한 치환 컬럼 중 하나라도 값이 있는가 — 전부 비면 행 자체가 무의미하다.</summary>
        public bool HasAnySubstitution =>
            !string.IsNullOrWhiteSpace(cost)
            || !string.IsNullOrWhiteSpace(range)
            || !string.IsNullOrWhiteSpace(damage)
            || !string.IsNullOrWhiteSpace(shield)
            || !string.IsNullOrWhiteSpace(heal)
            || !string.IsNullOrWhiteSpace(duration)
            || !string.IsNullOrWhiteSpace(hitCount)
            || !string.IsNullOrWhiteSpace(shape)
            || !string.IsNullOrWhiteSpace(stateEffect)
            || !string.IsNullOrWhiteSpace(buffDebuff)
            || !string.IsNullOrWhiteSpace(scout)
            || !string.IsNullOrWhiteSpace(descriptionOverride);

        public static CardUpgradeCsvRow FromFields(IReadOnlyList<string> fields)
        {
            if (fields == null || fields.Count != 14)
            {
                throw new ArgumentException("A card upgrade CSV row must have exactly 14 fields.", nameof(fields));
            }

            return new CardUpgradeCsvRow
            {
                cardId = fields[0],
                cost = fields[1],
                range = fields[2],
                damage = fields[3],
                shield = fields[4],
                heal = fields[5],
                duration = fields[6],
                hitCount = fields[7],
                shape = fields[8],
                stateEffect = fields[9],
                buffDebuff = fields[10],
                scout = fields[11],
                descriptionOverride = fields[12],
                note = fields[13],
            };
        }

        public CardUpgradeCsvRow Clone()
        {
            return new CardUpgradeCsvRow
            {
                cardId = cardId,
                cost = cost,
                range = range,
                damage = damage,
                shield = shield,
                heal = heal,
                duration = duration,
                hitCount = hitCount,
                shape = shape,
                stateEffect = stateEffect,
                buffDebuff = buffDebuff,
                scout = scout,
                descriptionOverride = descriptionOverride,
                note = note,
            };
        }
    }

    [Serializable]
    public sealed class CardChoiceOptionCsvRow
    {
        [SerializeField] private string cardId;
        [SerializeField] private string optionId;
        [SerializeField] private string displayName;
        [SerializeField] private string cardText;
        [SerializeField] private string sortOrder;

        public string CardId => cardId;
        public string OptionId => optionId;
        public string DisplayName => displayName;
        public string CardText => cardText;
        public string SortOrder => sortOrder;
        public int SortOrderValue => int.TryParse(sortOrder, out var value) ? value : 0;

        public static CardChoiceOptionCsvRow FromFields(IReadOnlyList<string> fields)
        {
            if (fields == null || fields.Count != 5)
            {
                throw new ArgumentException("A card choice option CSV row must have exactly 5 fields.", nameof(fields));
            }

            return new CardChoiceOptionCsvRow
            {
                cardId = fields[0],
                optionId = fields[1],
                displayName = fields[2],
                cardText = fields[3],
                sortOrder = fields[4],
            };
        }

        public CardChoiceOptionCsvRow Clone()
        {
            return new CardChoiceOptionCsvRow
            {
                cardId = cardId,
                optionId = optionId,
                displayName = displayName,
                cardText = cardText,
                sortOrder = sortOrder,
            };
        }
    }
}
