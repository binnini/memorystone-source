using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    [CreateAssetMenu(fileName = "CardCatalog", menuName = "Seoul Playup/Cards/Card Catalog")]
    public sealed class CardCatalogAsset : ScriptableObject
    {
        private const string DefaultDisplayName = "Approved Card System catalog";
        private const string SourceTrace = "cards.csv import";

        // cards.csv 컬럼 집합(DEC-2026-09-06-01 · 카드 클래스 전환 P2-d). 순서는 자유 — 헤더 **이름**으로 읽는다.
        // 로직 토큰 컬럼(behaviorId·additionalCost·postActions·choiceOptions·behaviorParams)과 죽은 scout 컬럼은
        // 카드 클래스(Combat.Runtime/Cards)로 갔고, 표시·메타·수치만 남는다.
        private static readonly string[] ExpectedHeader =
        {
            "id", "name", "description", "type", "target", "cost", "range", "shape",
            "damage", "shield", "heal", "stateEffect", "buff_debuff", "duration",
            "hitCount", "costMode", "scalingMode", "gameplayType", "targeting",
            "status", "includeInDecks", "visibleInCatalog", "illustrationId", "rarity",
            "choiceTexts", "descriptionUpgraded"
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

        [SerializeField] private string sourceId = CombatCatalogFactory.CardCatalogSourceId;
        [SerializeField] private string displayName = DefaultDisplayName;
        [SerializeField] private List<CardCatalogCsvRow> rows = new List<CardCatalogCsvRow>();

        public string SourceId => string.IsNullOrWhiteSpace(sourceId) ? CombatCatalogFactory.CardCatalogSourceId : sourceId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? DefaultDisplayName : displayName;
        public IReadOnlyList<CardCatalogCsvRow> Rows => rows;

        public void SetRows(IEnumerable<CardCatalogCsvRow> importedRows)
        {
            rows = importedRows == null
                ? new List<CardCatalogCsvRow>()
                : importedRows.Select(row => row.Clone()).ToList();
        }

        public CardCatalogDefinition ToCardCatalogDefinition(CombatConfig config)
        {
            // 연마(P4)는 카드 클래스(CardBehavior.Upgrade)가 정의를 치환한다 — 임포터는 저작 행 하나만 엔트리로 만든다.
            return new CardCatalogDefinition(SourceId, DisplayName, rows.Select(row => BuildEntry(row, config, Normalize(row.ChoiceTexts))));
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
            var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < table.Headers.Count; i++)
            {
                indexByName[table.Headers[i]] = i;
            }

            return table.Rows
                .Select(row => CardCatalogCsvRow.FromRecord(name => row.Fields[indexByName[name]]))
                .ToList();
        }

        // 공용 CsvTable로 파싱하고 헤더 **집합**을 기대 스키마와 대조한다(순서 자유 — 컬럼을 중간에 넣어도
        // 뒤쪽 인덱스가 밀리지 않는다). 구조 오류(빈 파일, 빈 헤더, 열 수 불일치, 미종결 인용부)는 CsvTable이
        // 실제 파일 행 번호를 붙여 던진다. 공개 계약(FormatException)은 유지한다.
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

            var actual = new HashSet<string>(table.Headers, StringComparer.Ordinal);
            if (actual.Count != table.Headers.Count)
            {
                throw new FormatException($"{label} header has a duplicate column name.");
            }

            var missing = expectedHeader.Where(name => !actual.Contains(name)).ToList();
            if (missing.Count > 0)
            {
                throw new FormatException($"{label} header is missing column(s): {string.Join(", ", missing)}.");
            }

            var expected = new HashSet<string>(expectedHeader, StringComparer.Ordinal);
            var extra = table.Headers.Where(name => !expected.Contains(name)).ToList();
            if (extra.Count > 0)
            {
                throw new FormatException($"{label} header has unknown column(s): {string.Join(", ", extra)}.");
            }

            return table;
        }

        private static CardCatalogEntry BuildEntry(CardCatalogCsvRow row, CombatConfig config, string choiceOptionTexts)
        {
            ResolveType(row.Type, out var deckType, out var actionType, out var defaultGameplayType);
            ResolveShape(row.Shape, out var areaRadius, out var shapeId);
            // 카드 규칙(선택지·장판 종류·효과 발신 키)은 카드 클래스가 정본이다 — 임포터는 묻기만 한다.
            var behavior = CardBehaviorRegistry.Get(row.Id);
            ResolveTargeting(
                row.Target,
                row.Targeting,
                behavior.Choices.Count > 0,
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

            var amount = ResolveAmount(row, actionType, behavior, config);
            var status = ParseStatus(row.Status);
            var gameplayType = ResolveGameplayType(row.GameplayType, defaultGameplayType);
            var fieldObjectKind = ResolveFieldObjectKind(behavior);
            var costMode = ResolveCostMode(row.CostMode);
            var scalingMode = ResolveScalingMode(row.ScalingMode);
            var duration = ParseOptionalInt(row.Duration, 0, "duration");
            var hitCount = ParseOptionalInt(row.HitCount, 1, "hitCount");

            return new CardCatalogEntry(
                row.Id,
                row.Name,
                deckType,
                actionType,
                ResolveNumber(row.Cost, config, "cost"),
                range,
                amount,
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
                Normalize(choiceOptionTexts),
                Normalize(row.Description),
                ParseRarity(row.Rarity),
                Normalize(row.StateEffect),
                Normalize(row.BuffDebuff),
                // I-08(WS-I): heal 축은 Amount로 접히기 전의 저작값을 따로 보존한다 — damage와 heal을
                // 동시에 저작한 카드(A03)의 회복량이 죽은 데이터가 되지 않도록.
                healAmount: string.IsNullOrWhiteSpace(row.Heal) ? 0 : ResolveNumber(row.Heal, config, "heal"),
                descriptionUpgraded: Normalize(row.DescriptionUpgraded));
        }

        private static bool ValidateRow(CardCatalogCsvRow row, int lineNumber, out string reason)
        {
            if (string.IsNullOrWhiteSpace(row.Id))
            {
                reason = $"CSV row {lineNumber} has an empty id.";
                return false;
            }

            // 카드 id의 유일한 등록부는 CardBehaviorRegistry다 — 클래스 없는 카드는 규칙이 없으므로 임포트가 거부한다.
            if (!CardBehaviorRegistry.TryGet(row.Id, out var behavior))
            {
                reason = $"CSV row {lineNumber} has card id '{row.Id}' with no CardBehavior class registered (Combat.Runtime/Cards).";
                return false;
            }

            if (!TryParseOptionalEnum<CardGameplayType>(row.GameplayType))
            {
                reason = $"CSV row {lineNumber} has unsupported gameplayType '{row.GameplayType}'.";
                return false;
            }

            // 선택지 문안(choiceTexts)은 클래스가 선언한 선택지와 1:1이어야 한다 — 문안 없는 선택지도, 선택지 없는 문안도 거부.
            if (!CardBehaviorMetadata.ValidateChoiceOptionTexts(row.ChoiceTexts))
            {
                reason = $"CSV row {lineNumber} has a malformed choiceTexts; expected 'optionId|displayName|cardText' items.";
                return false;
            }

            var declaredOptions = behavior.Choices.Select(option => option.OptionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var describedOptions = CardBehaviorMetadata.ParseChoiceOptionTexts(row.ChoiceTexts).Select(text => text.OptionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!declaredOptions.SetEquals(describedOptions))
            {
                reason = $"CSV row {lineNumber} choiceTexts must describe exactly the options declared by {behavior.GetType().Name} ({string.Join(", ", declaredOptions)}).";
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

            if (ResolveFieldObjectKind(behavior) != CardFieldObjectKind.None
                && ParseOptionalInt(row.Duration, 0, "duration") <= 0)
            {
                reason = $"CSV row {lineNumber} is a field object but duration is not positive.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidateOptionalBool(string rawValue)
        {
            var value = Normalize(rawValue);
            return string.IsNullOrEmpty(value) || bool.TryParse(value, out _);
        }

        private static int ResolveAmount(CardCatalogCsvRow row, CardEffectType actionType, CardBehavior behavior, CombatConfig config)
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

            // Surviving derivations: a move card's "amount" is its range (M05 authors range 0 → 0; its magnitude
            // lives in buff_debuff), and a card whose class declares AmountFollowsDuration (U04 횃불: 지속시간 =
            // 초기 반경) reads duration — damage/shield/heal have no column for a vision radius, and without this
            // the card would cost ki and do nothing (2026-08-02 실플레이).
            if (actionType == CardEffectType.Move)
            {
                return ResolveNumber(row.Range, config, "range");
            }

            return behavior.AmountFollowsDuration ? ResolveNumber(row.Duration, config, "duration") : 0;
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
            string rawTarget,
            string rawTargeting,
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
                case "random_tile":
                    // 도착지를 모르는 여행(M06): 목적지를 고르지 않고 범위 안 무작위 칸으로 — 옛 behaviorId 유도(move.random_radius_2)를 target 어휘로.
                    targetMode = CardTargetMode.RandomReachable;
                    targeting = string.IsNullOrEmpty(explicitTargeting) ? "random_reachable_hex_radius_2" : explicitTargeting;
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
            return target == "self" || target == "tile" || target == "random_tile" || target == "enemy" || target == "ally" || target == "none";
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

        // gameplayType 컬럼은 표시 분류(카드 배경·테마)의 명시 저작 — 비우면 type에서 파생. 현행 저작: M05 추진력=Buff.
        private static CardGameplayType ResolveGameplayType(string rawGameplayType, CardGameplayType defaultGameplayType)
        {
            return TryParseOptionalEnum<CardGameplayType>(rawGameplayType, out var explicitType)
                ? explicitType
                : defaultGameplayType;
        }

        private static CardFieldObjectKind ResolveFieldObjectKind(CardBehavior behavior)
        {
            return behavior is FieldObjectCard fieldCard ? fieldCard.FieldKind : CardFieldObjectKind.None;
        }

        private static CardCostMode ResolveCostMode(string rawCostMode)
        {
            // costMode is authored entirely in cards.csv (the source of truth). No behavior-based
            // magic defaults — whatever the CSV says (including empty → Fixed) is what ships.
            return TryParseOptionalEnum<CardCostMode>(rawCostMode, out var explicitMode)
                ? explicitMode
                : CardCostMode.Fixed;
        }

        private static CardScalingMode ResolveScalingMode(string rawScalingMode)
        {
            // scalingMode is authored entirely in cards.csv (the source of truth).
            return TryParseOptionalEnum<CardScalingMode>(rawScalingMode, out var explicitMode)
                ? explicitMode
                : CardScalingMode.Flat;
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
        [SerializeField] private string hitCount;
        [SerializeField] private string costMode;
        [SerializeField] private string scalingMode;
        [SerializeField] private string gameplayType;
        [SerializeField] private string targeting;
        [SerializeField] private string status;
        [SerializeField] private string includeInDecks;
        [SerializeField] private string visibleInCatalog;
        [SerializeField] private string illustrationId;
        [SerializeField] private string rarity;
        [SerializeField] private string choiceTexts;
        [SerializeField] private string descriptionUpgraded;

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
        public string HitCount => hitCount;
        public string CostMode => costMode;
        public string ScalingMode => scalingMode;
        public string GameplayType => gameplayType;
        public string Targeting => targeting;
        public string Status => status;
        public string IncludeInDecks => includeInDecks;
        public string VisibleInCatalog => visibleInCatalog;
        public string IllustrationId => illustrationId;
        public string Rarity => rarity;
        public string ChoiceTexts => choiceTexts;
        public string DescriptionUpgraded => descriptionUpgraded;

        /// <summary>헤더 이름으로 읽는다(<paramref name="field"/>: 컬럼 이름 → 값). 고정 인덱스는 중간 삽입에 조용히 어긋나서 버렸다.</summary>
        public static CardCatalogCsvRow FromRecord(Func<string, string> field)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            return new CardCatalogCsvRow
            {
                id = field("id"),
                name = field("name"),
                description = field("description"),
                type = field("type"),
                target = field("target"),
                cost = field("cost"),
                range = field("range"),
                shape = field("shape"),
                damage = field("damage"),
                shield = field("shield"),
                heal = field("heal"),
                stateEffect = field("stateEffect"),
                buffDebuff = field("buff_debuff"),
                duration = field("duration"),
                hitCount = field("hitCount"),
                costMode = field("costMode"),
                scalingMode = field("scalingMode"),
                gameplayType = field("gameplayType"),
                targeting = field("targeting"),
                status = field("status"),
                includeInDecks = field("includeInDecks"),
                visibleInCatalog = field("visibleInCatalog"),
                illustrationId = field("illustrationId"),
                rarity = field("rarity"),
                choiceTexts = field("choiceTexts"),
                descriptionUpgraded = field("descriptionUpgraded"),
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
                hitCount = hitCount,
                costMode = costMode,
                scalingMode = scalingMode,
                gameplayType = gameplayType,
                targeting = targeting,
                status = status,
                includeInDecks = includeInDecks,
                visibleInCatalog = visibleInCatalog,
                illustrationId = illustrationId,
                rarity = rarity,
                choiceTexts = choiceTexts,
                descriptionUpgraded = descriptionUpgraded,
            };
        }
    }
}
