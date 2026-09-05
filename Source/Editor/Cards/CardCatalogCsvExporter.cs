using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.CardCore.EditorTools
{
    /// <summary>
    /// 현재 하드코딩된 Approved 카탈로그(<see cref="ApprovedCardCatalogFactory"/>)를
    /// Card_Catalog_Schema.md v1.0 컬럼으로 덤프하는 일회성 시드 생성기.
    ///
    /// 자동으로 채우는 컬럼: id(리네임), name, type, target, cost, range(토큰복원),
    /// shape(areaRadius→blast), damage/shield/heal(1차 추정), duration, behaviorId, status, 플래그.
    /// 수동 보정이 필요한 컬럼: stateEffect, buff_debuff, 그리고 일부 damage/shield/heal
    /// (구 단일 Amount로는 의미 분리가 안 되는 카드 — 콘솔 경고로 표시).
    /// </summary>
    public static class CardCatalogCsvExporter
    {
        private const string OutputPath = CombatCsvPaths.CardsCsv;

        // 토큰 복원용 센티넬: 팩토리가 config.AttackRange/AttackDamage/MaxKi를 그대로 박으므로
        // 흔치 않은 값을 주입해 두면 추출 시 정확히 식별된다.
        private const int SentinelAttackRange = 991;
        private const int SentinelAttackDamage = 992;
        private const int SentinelMaxKi = 993;

        private static readonly string[] Header =
        {
            "id", "name", "description", "type", "target", "cost", "range", "shape",
            "damage", "shield", "heal", "stateEffect", "buff_debuff",
            "duration", "scout", "behaviorId", "hitCount", "costMode", "scalingMode", "targeting",
            "additionalCost", "postActions", "choiceOptions", "behaviorParams",
            "status", "includeInDecks", "visibleInCatalog", "usableWhileStunned", "illustrationId", "rarity",
            "exhaustOnPlay", "retainOnTurnEnd"
        };

        // 정본 cards.csv를 통째로 덮어쓰므로 메뉴 노출 금지(시드 생성은 이미 완료된 일회성 작업).
        // 재실행이 필요하면 코드에서 직접 호출할 것.
        public static void Export()
        {
            var config = new CombatConfig(
                playerMaxHp: 20,
                enemyMaxHp: 10,
                playerMovePoints: 2,
                attackRange: SentinelAttackRange,
                attackDamage: SentinelAttackDamage,
                defenseBlock: 4,
                enemyChaseRange: 5,
                enemyAttackRange: 1,
                enemyAttackDamage: 3,
                actionBudget: SentinelMaxKi);

            var catalog = ApprovedCardCatalogFactory.CreateApprovedCatalog(config);

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Header));

            var exported = 0;
            var needsReview = new List<string>();
            foreach (var entry in catalog.Entries)
            {
                if (entry.Status != CardCatalogStatus.Approved)
                {
                    continue; // Draft/Debug 폐기
                }

                sb.AppendLine(BuildRow(entry, needsReview));
                exported++;
            }

            var dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // BOM 없는 UTF-8 (구글시트 임포트 친화).
            File.WriteAllText(OutputPath, sb.ToString(), new UTF8Encoding(false));
            AssetDatabase.Refresh();

            Debug.Log($"[CardCatalogCsvExporter] {exported}개 카드를 {OutputPath} 로 추출했습니다.");
            if (needsReview.Count > 0)
            {
                Debug.LogWarning(
                    "[CardCatalogCsvExporter] 시트에서 수동 검토 필요(구 단일 Amount로 의미 분리 불가):\n  - "
                    + string.Join("\n  - ", needsReview));
            }
        }

        private static string BuildRow(CardCatalogEntry entry, List<string> needsReview)
        {
            var id = entry.Id;

            var type = ResolveType(entry);
            var target = ResolveTarget(entry.TargetMode);
            var cost = TokenizeCost(entry.Cost);
            var range = TokenizeRange(entry.Range);
            var shape = ResolveShape(entry);
            ResolveStats(entry, out var damage, out var shield, out var heal, needsReview, id);
            var duration = entry.DurationTurns > 0 ? entry.DurationTurns.ToString(CultureInfo.InvariantCulture) : string.Empty;

            var fields = new[]
            {
                id,
                entry.DisplayName,
                Describe(entry),
                type,
                target,
                cost,
                range,
                shape,
                damage,
                shield,
                heal,
                string.Empty, // stateEffect — 수동 오써링
                string.Empty, // buff_debuff — 수동 오써링
                duration,
                string.Empty, // scout — 수동 오써링
                entry.EffectRef,
                entry.HitCount > 1 ? entry.HitCount.ToString(CultureInfo.InvariantCulture) : string.Empty,
                entry.CostMode == CardCostMode.Fixed ? string.Empty : entry.CostMode.ToString(),
                entry.ScalingMode == CardScalingMode.Flat ? string.Empty : entry.ScalingMode.ToString(),
                entry.Targeting,
                entry.AdditionalCost,
                entry.PostActions,
                entry.ChoiceOptions,
                entry.BehaviorParams,
                entry.Status.ToString(),
                entry.IncludeInGameplayDecks ? "true" : "false",
                entry.VisibleInCatalog ? "true" : "false",
                entry.UsableWhileStunned ? "true" : "false",
                string.IsNullOrWhiteSpace(entry.PresentationRef.IllustrationId)
                    ? $"card_illust_{entry.Id}"
                    : entry.PresentationRef.IllustrationId,
                entry.Rarity.ToString(),
                entry.ExhaustOnPlay ? "true" : string.Empty,
                entry.RetainOnTurnEnd ? "true" : string.Empty,
            };

            for (var i = 0; i < fields.Length; i++)
            {
                fields[i] = EscapeCsv(fields[i]);
            }

            return string.Join(",", fields);
        }

        private static string ResolveType(CardCatalogEntry entry)
        {
            if (entry.DeckType == CardCategory.Movement)
            {
                return "이동";
            }

            switch (entry.ActionType)
            {
                case CardEffectType.Attack: return "공격";
                case CardEffectType.Defend: return "방어";
                case CardEffectType.Scout: return "정찰";
                case CardEffectType.FieldObject: return "필드";
                case CardEffectType.Utility: return "유틸리티";
                default: return entry.ActionType.ToString();
            }
        }

        private static string ResolveTarget(CardTargetMode mode)
        {
            switch (mode)
            {
                case CardTargetMode.Self: return "self";
                case CardTargetMode.Enemy: return "enemy";
                case CardTargetMode.Tile:
                case CardTargetMode.RandomReachable: return "tile";
                case CardTargetMode.SelfOrEnemy:
                case CardTargetMode.OptionThenTarget: return "enemy";
                default: return "self";
            }
        }

        private static string Describe(CardCatalogEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.Description))
            {
                return entry.Description;
            }

            switch (entry.ActionType)
            {
                case CardEffectType.Attack:
                    return $"사거리 {TokenizeRange(entry.Range)} 내 피해 {TokenizeAmount(entry.Amount)}를 줍니다.";
                case CardEffectType.Defend:
                    return $"방어막 {TokenizeAmount(entry.Amount)}를 얻습니다.";
                case CardEffectType.Scout:
                    return "지형을 탐색합니다.";
                case CardEffectType.Investigate:
                    return $"사거리 {TokenizeRange(entry.Range)} 내 목표를 조사합니다.";
                case CardEffectType.FieldObject:
                    return $"{DescribeShape(entry)} 범위에 {entry.DurationTurns}턴 동안 배치합니다.";
                case CardEffectType.Buff:
                    return "버프를 적용합니다.";
                case CardEffectType.Utility:
                    return "효과를 사용합니다.";
                default:
                    return $"최대 {entry.Range}칸 이동합니다.";
            }
        }

        private static string DescribeShape(CardCatalogEntry entry)
        {
            if (entry.AreaRadius > 0)
            {
                return $"범위 {entry.AreaRadius}";
            }

            switch (entry.ShapeId)
            {
                case AttackShapeLibrary.Line2: return "직선 2칸";
                case AttackShapeLibrary.Line3: return "직선 3칸";
                case AttackShapeLibrary.Line4: return "직선 4칸";
                case AttackShapeLibrary.ConeNear: return "가까운 부채꼴";
                case AttackShapeLibrary.ConeMid: return "중간 부채꼴";
                case AttackShapeLibrary.ConeWide: return "넓은 부채꼴";
                case AttackShapeLibrary.TForward: return "전방 T자";
                case AttackShapeLibrary.CrossNear: return "인접 십자";
                case AttackShapeLibrary.CrossFar: return "넓은 십자";
                case AttackShapeLibrary.RingNear: return "인접 고리";
                case AttackShapeLibrary.VSplit: return "갈라지는 전방";
                case AttackShapeLibrary.Pincer: return "협공";
                case AttackShapeLibrary.Tremor: return "방사형 진동";
                case AttackShapeLibrary.Single:
                default:
                    return "단일 대상";
            }
        }

        private static string TokenizeAmount(int amount)
        {
            return amount == SentinelAttackDamage
                ? "{Damage}"
                : amount.ToString(CultureInfo.InvariantCulture);
        }
        private static string TokenizeCost(int cost)
        {
            return cost == SentinelMaxKi ? "{MaxKi}" : cost.ToString(CultureInfo.InvariantCulture);
        }

        private static string TokenizeRange(int range)
        {
            return range == SentinelAttackRange ? "{AttackRange}" : range.ToString(CultureInfo.InvariantCulture);
        }

        private static string ResolveShape(CardCatalogEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.ShapeId))
            {
                return entry.ShapeId;
            }

            switch (entry.AreaRadius)
            {
                case 0:
                    return entry.ActionType == CardEffectType.Attack ? "single" : string.Empty;
                case 1: return "blast-1";
                case 2: return "blast-2";
                default: return "blast-" + entry.AreaRadius.ToString(CultureInfo.InvariantCulture);
            }
        }

        // 구 단일 Amount를 1차 추정으로 damage/shield/heal에 배치. 분리 불가 케이스는 needsReview에 적재.
        private static void ResolveStats(
            CardCatalogEntry entry, out string damage, out string shield, out string heal,
            List<string> needsReview, string newId)
        {
            damage = string.Empty;
            shield = string.Empty;
            heal = string.Empty;

            var amount = entry.Amount == SentinelAttackDamage
                ? "{AttackDamage}"
                : entry.Amount.ToString(CultureInfo.InvariantCulture);
            var hasAmount = entry.Amount != 0 || entry.Amount == SentinelAttackDamage;

            switch (entry.ActionType)
            {
                case CardEffectType.Attack:
                    if (hasAmount) damage = amount;
                    break;
                case CardEffectType.Defend:
                    if (hasAmount) shield = amount;
                    // 반사(half_reflect)는 shield가 아니라 buff_debuff Reflect로 가야 함.
                    if (entry.EffectRef.Contains("reflect"))
                        needsReview.Add($"{newId}({entry.DisplayName}): shield={entry.Amount}는 반사%일 가능성 → buff_debuff Reflect로 이동 검토");
                    break;
                case CardEffectType.FieldObject:
                    if (entry.FieldObjectKind == CardFieldObjectKind.ConditionalHeal) { if (hasAmount) heal = amount; }
                    else if (entry.FieldObjectKind == CardFieldObjectKind.FieldDamage) { if (hasAmount) damage = amount; }
                    break;
                case CardEffectType.Scout:
                    if (hasAmount) damage = amount;
                    if (entry.EffectRef.Contains("heal"))
                        needsReview.Add($"{newId}({entry.DisplayName}): damage={entry.Amount}는 회복일 가능성 → heal로 이동 검토");
                    break;
                default:
                    // Move/Utility/Buff: Amount가 사거리/버프값이라 stat 컬럼 비움.
                    if (hasAmount && entry.EffectRef.Contains("momentum"))
                        needsReview.Add($"{newId}({entry.DisplayName}): 추진력 이월값 {entry.Amount} → buff_debuff Agility로 수동 입력");
                    break;
            }
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }
}
