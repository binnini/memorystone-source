#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using AIGD;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_CardCatalogAudit
    {
        [AiTool
        (
            "card-catalog-audit",
            Title = "Card Catalog / Audit",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("실제 cards.csv를 CardCatalogAsset 파싱/검증 파이프라인(ParseCsvText → ValidateRows → " +
            "CardCatalogDefinition.Validate)에 통과시켜 카드 카탈로그 전체 정합을 씬 로드 없이 감사한다. " +
            "행/중복/선택지 문안/바인딩 증거를 구조화 JSON으로 반환하는 읽기 전용 감사.")]
        public CardCatalogAuditResult Audit
        (
            [Description("cards.csv 경로. 기본은 SoT 소스.")]
            string cardsCsvPath = CombatCsvPaths.CardsCsv
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var result = new CardCatalogAuditResult
                {
                    cardsCsvPath = cardsCsvPath
                };

                if (!File.Exists(cardsCsvPath))
                {
                    throw new InvalidOperationException($"cards.csv not found at '{cardsCsvPath}'.");
                }

                var encoding = new UTF8Encoding(false, true);

                IReadOnlyList<CardCatalogCsvRow> rows;
                try
                {
                    rows = CardCatalogAsset.ParseCsvText(File.ReadAllText(cardsCsvPath, encoding));
                    result.parsedOk = true;
                }
                catch (Exception ex)
                {
                    // Parse failure is a first-class audit outcome, not a tool error.
                    result.parsedOk = false;
                    result.failureReason = $"cards.csv parse failed: {ex.Message}";
                    return result;
                }

                result.rowCount = rows.Count;

                var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
                try
                {
                    asset.SetRows(rows);

                    result.valid = asset.ValidateRows(out var reason);
                    result.failureReason = reason ?? string.Empty;

                    var definition = asset.ToCardCatalogDefinition(CombatConfig.Default);
                    result.entryCount = definition.Entries.Count;

                    var evidence = definition.CreateBindingEvidence();
                    result.bindingEvidence = new CardBindingEvidence
                    {
                        isValid = evidence.IsValid,
                        failureReason = evidence.FailureReason ?? string.Empty,
                        moveDeckCardIds = evidence.MoveDeckCardIds != null
                            ? new List<string>(evidence.MoveDeckCardIds)
                            : new List<string>(),
                        actionDeckCardIds = evidence.ActionDeckCardIds != null
                            ? new List<string>(evidence.ActionDeckCardIds)
                            : new List<string>()
                    };
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }

                return result;
            });
        }
    }
}
