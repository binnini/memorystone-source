#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using AIGD;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using SeoulPlayup.Combat.Runtime;
using UnityEditor;

namespace SeoulPlayup.Combat.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_CombatVfxAudit
    {
        [AiTool
        (
            "combat-vfx-audit",
            Title = "Combat VFX / Audit",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("EffectVfxCatalog의 내부 참조 무결성을 감사한다: 프리팹 배열의 null 슬롯(끊긴 참조), " +
            "사용 가능한 프리팹이 없는 엔트리, 렌더 불가한 loop 큐, 중복 cueId. 추가로 방출 가능한 " +
            "EffectKind/StatusEffectKind가 카탈로그에 렌더 가능하게 커버되는지 교차검사(미커버=방출되나 VFX 없음, " +
            "일부는 의도적으로 무VFX일 수 있어 사람 판단용). 추가로 cards.csv를 카탈로그와 대조해 카드별 VFX " +
            "커버리지(전용 큐/behaviorId 공유/범용 폴백/무연출)를 보고한다 — 카탈로그가 EffectKind 폴백 티어를 " +
            "가지므로 큐 누락이 런타임에 에러로 드러나지 않아, 이 대조가 없으면 미저작 카드가 조용히 통과한다. " +
            "폴백/무연출 판정의 근거가 되는 EffectKind 추정은 카드의 저작 컬럼에서 유도한 휴리스틱이며 " +
            "probedKinds로 함께 노출된다. 실제 카탈로그 에셋에 실행하는 읽기 전용 감사.")]
        public CombatVfxAuditResult Audit
        (
            [Description("EffectVfxCatalog 에셋 경로. 기본은 런타임 기본 카탈로그.")]
            string catalogPath = "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset",
            [Description("cards.csv 경로. 기본은 SoT 소스. 파일이 없으면 카드 커버리지 섹션만 생략된다.")]
            string cardsCsvPath = CombatCsvPaths.CardsCsv
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(catalogPath);
                if (catalog == null)
                {
                    throw new InvalidOperationException($"No EffectVfxCatalog asset at '{catalogPath}'.");
                }

                var result = new CombatVfxAuditResult { catalogPath = catalogPath };
                var entries = catalog.Entries;
                result.totalEntries = entries.Length;

                var cueIdCounts = new Dictionary<string, int>(StringComparer.Ordinal);

                for (var i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    var label = DescribeEntry(entry, i);

                    var cueId = entry.CueId;
                    if (!string.IsNullOrWhiteSpace(cueId))
                    {
                        cueIdCounts.TryGetValue(cueId, out var count);
                        cueIdCounts[cueId] = count + 1;
                    }

                    if (entry.Deprecated)
                    {
                        result.deprecatedCount++;
                        continue;
                    }

                    var prefabs = entry.Prefabs;
                    var nullSlots = prefabs.Count(prefab => prefab == null);
                    var hasUsablePrefab = prefabs.Any(prefab => prefab != null);

                    if (prefabs.Length > 0 && nullSlots > 0)
                    {
                        result.entriesWithNullPrefabSlot.Add($"{label}: {nullSlots}/{prefabs.Length} prefab slots are null");
                    }

                    if (!hasUsablePrefab)
                    {
                        result.entriesWithoutPrefab.Add(label);
                        if (entry.Loop)
                        {
                            result.loopEntriesWithoutPrefab.Add(label);
                        }
                    }
                }

                foreach (var pair in cueIdCounts.Where(pair => pair.Value > 1).OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    result.duplicateCueIds.Add($"{pair.Key} (x{pair.Value})");
                }

                // Cross-reference: which emittable effects the catalog does NOT cover. An EffectKind /
                // StatusEffectKind that combat can raise but that has no non-deprecated, renderable catalog
                // entry produces no VFX at runtime. These enums are the canonical emittable set (combat
                // raises EffectResultEvent with these), so a value absent from the catalog is a coverage gap.
                var renderable = entries
                    .Where(entry => entry != null && !entry.Deprecated && entry.Prefabs.Any(prefab => prefab != null))
                    .ToList();

                var coveredKinds = new HashSet<EffectKind>(renderable.Select(entry => entry.Kind));
                foreach (EffectKind kind in Enum.GetValues(typeof(EffectKind)))
                {
                    if (!coveredKinds.Contains(kind))
                    {
                        result.uncoveredEffectKinds.Add(kind.ToString());
                    }
                }

                // Status coverage: only entries that actually key on StatusKind (status-apply or loop tier)
                // count as covering a status effect; a plain-kind entry's StatusKind field is inert.
                var coveredStatusKinds = new HashSet<StatusEffectKind>(
                    renderable.Where(entry => entry.MatchStatusKind || entry.Loop).Select(entry => entry.StatusKind));
                foreach (StatusEffectKind status in Enum.GetValues(typeof(StatusEffectKind)))
                {
                    if (!coveredStatusKinds.Contains(status))
                    {
                        result.uncoveredStatusEffectKinds.Add(status.ToString());
                    }
                }

                AppendCardCoverage(result, catalog, cardsCsvPath);

                return result;
            });
        }

        // Card-side coverage: which authored cards have their own cue, borrow one via behaviorId, render only
        // the generic EffectKind fallback, or render nothing. Missing cards.csv is reported in the result
        // rather than thrown, so the catalog-integrity half of the audit still returns.
        private static void AppendCardCoverage(
            CombatVfxAuditResult result, EffectVfxCatalog catalog, string cardsCsvPath)
        {
            result.cardsCsvPath = cardsCsvPath;
            if (!File.Exists(cardsCsvPath))
            {
                result.cardsWithoutAnyVfx.Add($"(cards.csv not found at '{cardsCsvPath}' — card coverage skipped)");
                return;
            }

            IReadOnlyList<CardCatalogCsvRow> rows;
            try
            {
                rows = CardCatalogAsset.ParseCsvText(File.ReadAllText(cardsCsvPath, new UTF8Encoding(false, true)));
            }
            catch (Exception ex)
            {
                result.cardsWithoutAnyVfx.Add($"(cards.csv parse failed: {ex.Message} — card coverage skipped)");
                return;
            }

            var reports = CardVfxCoverage.EvaluateAll(rows, catalog);
            result.cardsTotal = reports.Count;

            foreach (var report in reports)
            {
                var label = $"{report.CardId} {report.CardName}".TrimEnd();
                switch (report.Status)
                {
                    case CardVfxCoverageStatus.Dedicated:
                        result.cardsDedicated++;
                        break;
                    case CardVfxCoverageStatus.SharedBehavior:
                        result.cardsSharedBehavior++;
                        result.cardsSharingBehaviorCue.Add(
                            $"{label} → {string.Join(", ", report.MatchedCueIds)} (behaviorId={report.BehaviorId})");
                        break;
                    case CardVfxCoverageStatus.FallbackOnly:
                        result.cardsFallbackOnly++;
                        result.cardsOnFallbackOnly.Add(
                            $"{label} → generic {string.Join("/", report.FallbackKinds)} (behaviorId={report.BehaviorId})");
                        break;
                    case CardVfxCoverageStatus.NoVfx:
                        result.cardsNoVfx++;
                        var reason = report.ProbedKinds.Count == 0
                            ? "no effect kind derived from its authored columns"
                            : $"no renderable entry for {string.Join("/", report.UncoveredKinds)}";
                        result.cardsWithoutAnyVfx.Add($"{label} → {reason} (behaviorId={report.BehaviorId})");
                        break;
                }

                result.cardVfxCoverage.Add(new CardVfxCoverageRow
                {
                    cardId = report.CardId,
                    cardName = report.CardName,
                    cardType = report.CardType,
                    behaviorId = report.BehaviorId,
                    status = report.Status.ToString(),
                    matchedCueIds = report.MatchedCueIds,
                    matchKeys = report.MatchKeys,
                    probedKinds = report.ProbedKinds,
                    fallbackKinds = report.FallbackKinds,
                    uncoveredKinds = report.UncoveredKinds
                });
            }
        }

        private static string DescribeEntry(EffectVfxCatalog.Entry entry, int index)
        {
            var cueId = entry.CueId;
            var identity = string.IsNullOrWhiteSpace(cueId) ? $"#{index}" : cueId;
            return $"{identity} (kind={entry.Kind}, target={entry.TargetFilter})";
        }
    }
}
