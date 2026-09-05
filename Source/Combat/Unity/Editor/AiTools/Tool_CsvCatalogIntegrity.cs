#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using AIGD;
using UnityEditor;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_CsvCatalogIntegrity
    {
        [AiTool
        (
            "csv-catalog-integrity",
            Title = "CSV Catalog / Integrity",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("도메인별 실제 출하 CSV를 대응 컨버터로 실제 변환해 파싱/변환 에러를 검출하고(convert 무결성 코어), " +
            "베이크형 도메인은 소스 CSV가 대응 baked `.asset`보다 최신인지(staleness) 함께 감사한다. 각 도메인이 예외 없이 " +
            "변환되는지 + 생성 엔트리 수 + 소스 파일 존재 + 베이크 최신성을 리포트하는 읽기 전용 감사. cards/keywords만 " +
            "baked(.asset), monsters/players/statuseffects는 런타임이 소스 CSV를 TextAsset로 직독하므로 staleness 무관. " +
            "id 포맷 검증은 naming-lint의 csv-id 규칙과 중복이라 이 툴 범위 밖(naming-lint 사용).")]
        public CsvCatalogIntegrityResult Check
        (
            [Description("검사 도메인: all | cards | monsters | bosses | players | statuseffects | keywords | relics | consumableitems | rewards | gacharewards | shopprices | killdroprates | mapobjects. 기본 all.")]
            string domain = "all"
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var requested = string.IsNullOrWhiteSpace(domain) ? "all" : domain.Trim().ToLowerInvariant();
                var result = new CsvCatalogIntegrityResult { requestedDomain = requested };

                var runners = new (string name, Func<CsvDomainResult> run)[]
                {
                    ("cards", CheckCards),
                    ("monsters", CheckMonsters),
                    ("bosses", CheckBosses),
                    ("players", CheckPlayers),
                    ("statuseffects", CheckStatusEffects),
                    ("keywords", CheckKeywords),
                    ("relics", CheckRelics),
                    ("consumableitems", CheckConsumableItems),
                    ("rewards", CheckRewards),
                    ("gacharewards", CheckGachaRewards),
                    ("shopprices", CheckShopPrices),
                    ("killdroprates", CheckKillDropRates),
                    ("mapobjects", CheckMapObjects),
                };

                if (requested != "all" && runners.All(r => r.name != requested))
                {
                    throw new ArgumentException(
                        $"Unknown domain '{domain}'. Valid: all, cards, monsters, bosses, players, statuseffects, keywords, relics, rewards, gacharewards, shopprices, mapobjects.");
                }

                foreach (var runner in runners)
                {
                    if (requested != "all" && runner.name != requested)
                    {
                        continue;
                    }

                    var domainResult = runner.run();
                    ApplyStaleness(domainResult);
                    result.domains.Add(domainResult);
                    result.domainsChecked++;
                    if (!domainResult.convertOk)
                    {
                        result.domainsFailed++;
                    }

                    if (domainResult.bakedAssetStale)
                    {
                        result.staleCount++;
                    }
                }

                result.allOk = result.domainsFailed == 0;
                result.anyStale = result.staleCount > 0;
                return result;
            });
        }

        // Baked ScriptableObject catalog the runtime loads, per domain. Only cards/keywords bake to an
        // `.asset`; monsters/players/statuseffects are read directly from the source CSV at runtime
        // (CombatCatalogTextAssetSource references the Source CSVs by GUID), so they cannot go stale.
        private const string CardCatalogAssetPath = "Assets/Data/Combat/Cards/Catalogs/CardCatalog.asset";
        private const string KeywordCatalogAssetPath = "Assets/Resources/Combat/DefaultCardKeywordCatalog.asset";
        // Unit separator between fields within a signature line, so distinct field layouts can't collide.
        private const string FieldSeparator = "\u001f";

        // Populates the bake-staleness fields on a domain result. "Stale" means the baked asset's CONTENT
        // no longer matches what the current source CSV converts to — i.e. the CSV was edited but the bake
        // menu was not re-run. Compared by content, not by file mtime (mtime is unreliable across VCS
        // checkouts, where the source CSV and its baked asset get near-identical checkout timestamps).
        private static void ApplyStaleness(CsvDomainResult domain)
        {
            switch (domain.name)
            {
                case "cards":
                    ApplyCardsStaleness(domain);
                    break;
                case "keywords":
                    ApplyKeywordsStaleness(domain);
                    break;
                default:
                    domain.stalenessNote =
                        "Runtime reads the source CSV directly (TextAsset-backed); no baked asset to stale.";
                    break;
            }
        }

        private static void ApplyCardsStaleness(CsvDomainResult domain)
        {
            domain.bakedAssetPath = CardCatalogAssetPath;
            var baked = AssetDatabase.LoadAssetAtPath<CardCatalogAsset>(CardCatalogAssetPath);
            if (baked == null)
            {
                domain.bakedAssetStale = true;
                domain.stalenessNote =
                    $"Baked asset '{CardCatalogAssetPath}' does not exist; run menu 'Tools/Cards/Import cards.csv'.";
                return;
            }

            domain.bakedAssetExists = true;
            // cards.csv, card_choice_options.csv and card_upgrades.csv bake together into CardCatalog.asset.
            var freshRows = CardCatalogAsset.ParseCsvText(File.ReadAllText(CombatCsvPaths.CardsCsv));
            var freshChoices =
                CardCatalogAsset.ParseChoiceOptionsCsvText(File.ReadAllText(CombatCsvPaths.CardChoiceOptionsCsv));
            var freshUpgrades = File.Exists(CombatCsvPaths.CardUpgradesCsv)
                ? CardCatalogAsset.ParseUpgradesCsvText(File.ReadAllText(CombatCsvPaths.CardUpgradesCsv))
                : (IReadOnlyList<CardUpgradeCsvRow>)Array.Empty<CardUpgradeCsvRow>();
            var fresh = RowsSignature(freshRows) + "\n##CHOICE##\n" + RowsSignature(freshChoices)
                + "\n##UPGRADE##\n" + RowsSignature(freshUpgrades);
            var stored = RowsSignature(baked.Rows) + "\n##CHOICE##\n" + RowsSignature(baked.ChoiceOptionRows)
                + "\n##UPGRADE##\n" + RowsSignature(baked.UpgradeRows);
            SetContentStaleness(domain, fresh, stored, "Tools/Cards/Import cards.csv");
        }

        private static void ApplyKeywordsStaleness(CsvDomainResult domain)
        {
            domain.bakedAssetPath = KeywordCatalogAssetPath;
            var baked = AssetDatabase.LoadAssetAtPath<CardKeywordCatalog>(KeywordCatalogAssetPath);
            if (baked == null)
            {
                domain.bakedAssetStale = true;
                domain.stalenessNote =
                    $"Baked asset '{KeywordCatalogAssetPath}' does not exist; run menu 'Seoul Playup/Combat/Bake Game Keyword Catalog'.";
                return;
            }

            domain.bakedAssetExists = true;
            var fresh = string.Join("\n", KeywordCatalogCsv.ConvertFile(CombatCsvPaths.GameKeywordsCsv).Entries
                .Select(e => string.Join(FieldSeparator, e.Category, e.Keyword, e.Effect, e.ValueKind.ToString(), e.Value)));
            var stored = string.Join("\n", baked.Entries
                .Select(e => string.Join(FieldSeparator, e.category, e.keyword, e.effect, e.valueKind.ToString(), e.value)));
            SetContentStaleness(domain, fresh, stored, "Seoul Playup/Combat/Bake Game Keyword Catalog");
        }

        // Full-content signature of a row list: one line per row, each line the row's scalar (string /
        // primitive / enum) property values joined in a stable name order. Catches any authored field
        // change, add/remove, or reorder — and is schema-robust (new CSV columns are picked up
        // automatically). Fresh and stored rows are the same row type, so their signatures are comparable.
        private static string RowsSignature(System.Collections.IEnumerable rows)
        {
            var lines = new List<string>();
            foreach (var row in rows)
            {
                if (row == null)
                {
                    lines.Add(string.Empty);
                    continue;
                }

                var props = row.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0)
                    .Where(p => p.PropertyType == typeof(string) || p.PropertyType.IsPrimitive || p.PropertyType.IsEnum)
                    .OrderBy(p => p.Name, StringComparer.Ordinal);
                var parts = new List<string>();
                foreach (var p in props)
                {
                    object value;
                    try
                    {
                        value = p.GetValue(row);
                    }
                    catch
                    {
                        continue;
                    }

                    parts.Add(p.Name + "=" + (value?.ToString() ?? string.Empty));
                }

                lines.Add(string.Join(FieldSeparator, parts));
            }

            return string.Join("\n", lines);
        }

        private static void SetContentStaleness(CsvDomainResult domain, string freshSignature, string storedSignature, string bakeMenu)
        {
            if (string.Equals(freshSignature, storedSignature, StringComparison.Ordinal))
            {
                domain.bakedAssetStale = false;
                domain.stalenessNote = "Baked asset content matches the current source CSV.";
            }
            else
            {
                domain.bakedAssetStale = true;
                domain.stalenessNote =
                    $"Baked asset content differs from the current source CSV; re-run bake menu '{bakeMenu}'.";
            }
        }

        private static CsvDomainResult Run(string name, IReadOnlyList<string> sources, Func<int> convert)
        {
            var domainResult = new CsvDomainResult { name = name };
            domainResult.sourcePaths.AddRange(sources);

            var missing = sources.Where(path => !File.Exists(path) && !Directory.Exists(path)).ToList();
            if (missing.Count > 0)
            {
                domainResult.convertOk = false;
                domainResult.error = "Missing source(s): " + string.Join(", ", missing);
                return domainResult;
            }

            try
            {
                domainResult.entryCount = convert();
                domainResult.convertOk = true;
            }
            catch (Exception ex)
            {
                // A convert failure is a first-class audit finding, not a tool error.
                domainResult.convertOk = false;
                domainResult.error = $"{ex.GetType().Name}: {ex.Message}";
            }

            return domainResult;
        }

        private static CsvDomainResult CheckCards()
        {
            return Run("cards", new[] { CombatCsvPaths.CardsCsv }, () =>
            {
                var rows = CardCatalogAsset.ParseCsvText(File.ReadAllText(CombatCsvPaths.CardsCsv));
                // card_upgrades.csv도 같은 컨버터로 함께 변환한다 — 연마 행이 깨지면 병합 빌드에서
                // 예외가 나므로 convert 무결성이 그대로 잡는다(semantic 검증은 card-catalog-audit 몫).
                var upgrades = File.Exists(CombatCsvPaths.CardUpgradesCsv)
                    ? CardCatalogAsset.ParseUpgradesCsvText(File.ReadAllText(CombatCsvPaths.CardUpgradesCsv))
                    : (IReadOnlyList<CardUpgradeCsvRow>)Array.Empty<CardUpgradeCsvRow>();
                var asset = UnityEngine.ScriptableObject.CreateInstance<CardCatalogAsset>();
                try
                {
                    asset.SetRows(rows);
                    asset.SetUpgradeRows(upgrades);
                    return asset.ToCardCatalogDefinition(CombatConfig.Default).Entries.Count;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            });
        }

        private static CsvDomainResult CheckMonsters()
        {
            return Run(
                "monsters",
                new[] { CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory },
                () => MonsterCatalogCsvConverter
                    .ConvertDirectories(CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory)
                    .MonsterCatalog.Entries.Count);
        }

        // 보스 확장 데이터도 런타임 직접 파싱형(baked 산출물 없음) — convert 무결성만 감사한다.
        private static CsvDomainResult CheckBosses()
        {
            return Run(
                "bosses",
                new[] { CombatCsvPaths.BossProfilesCsv, CombatCsvPaths.BossPhasesCsv },
                () => BossCatalogCsvConverter.ConvertDirectory(CombatCsvPaths.MonsterDirectory).Profiles.Count);
        }

        private static CsvDomainResult CheckPlayers()
        {
            return Run(
                "players",
                new[] { CombatCsvPaths.PlayerCombatProfilesCsv },
                () => PlayerCombatProfileCsvConverter.ConvertFile(CombatCsvPaths.PlayerCombatProfilesCsv).Profiles.Count);
        }

        private static CsvDomainResult CheckStatusEffects()
        {
            return Run(
                "statuseffects",
                new[] { CombatCsvPaths.StatusEffectsCsv },
                () => StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv).Entries.Count);
        }

        private static CsvDomainResult CheckKeywords()
        {
            return Run(
                "keywords",
                new[] { CombatCsvPaths.GameKeywordsCsv },
                () => KeywordCatalogCsv.ConvertFile(CombatCsvPaths.GameKeywordsCsv).Entries.Count);
        }

        // 유물·저주는 런타임 직접 파싱형(baked 산출물 없음) — staleness 무관, convert 무결성만 감사.
        private static CsvDomainResult CheckRelics()
        {
            return Run(
                "relics",
                new[] { CombatCsvPaths.RelicsCsv },
                () => RelicCatalogCsv.ConvertFile(CombatCsvPaths.RelicsCsv).Entries.Count);
        }

        /// <summary>
        /// 소모품(<c>consumable_items.csv</c>). 🔴 2026-08-31까지 이 게이트에 <b>소모품만 없었다</b> —
        /// 12행을 통째로 갈아치웠는데 아무것도 잡지 못했다(인계문 P5-6). 유물과 같은 런타임 직접
        /// 파싱형이라 staleness는 무관하고 convert 무결성만 본다.
        /// </summary>
        private static CsvDomainResult CheckConsumableItems()
        {
            return Run(
                "consumableitems",
                new[] { CombatCsvPaths.ConsumableItemsCsv },
                () => ConsumableItemCatalogCsv.ConvertFile(CombatCsvPaths.ConsumableItemsCsv).Entries.Count);
        }

        private static CsvDomainResult CheckRewards()
        {
            return Run(
                "rewards",
                new[] { CombatCsvPaths.CombatRewardsCsv },
                () =>
                {
                    var weights = CardRewardWeightsCsvConverter.ConvertFile(CombatCsvPaths.CombatRewardsCsv);
                    return weights.Normal.Count + weights.Elite.Count;
                });
        }

        private static CsvDomainResult CheckGachaRewards()
        {
            return Run(
                "gacharewards",
                new[] { CombatCsvPaths.GachaRewardsCsv },
                () => GachaRewardWeightsCsvConverter.ConvertFile(CombatCsvPaths.GachaRewardsCsv).Entries.Count);
        }

        // 맵 오브젝트의 도감 저작(P6). 런타임 직접 파싱형(baked 산출물 없음) — convert 무결성만 감사.
        private static CsvDomainResult CheckMapObjects()
        {
            return Run(
                "mapobjects",
                new[] { CombatCsvPaths.MapObjectsCsv },
                () => CodexObjectCatalogCsv.ConvertFile(CombatCsvPaths.MapObjectsCsv).Entries.Count);
        }

        private static CsvDomainResult CheckShopPrices()
        {
            return Run(
                "shopprices",
                new[] { CombatCsvPaths.ShopPricesCsv },
                () => ShopPricesCsvConverter.ConvertFile(CombatCsvPaths.ShopPricesCsv).Entries.Count);
        }

        // 처치 추가 보상 확률(DEC-2026-08-31-02). 런타임 직접 파싱형 — convert 무결성만 감사.
        private static CsvDomainResult CheckKillDropRates()
        {
            return Run(
                "killdroprates",
                new[] { CombatCsvPaths.KillDropRatesCsv },
                () => KillDropRatesCsvConverter.ConvertFile(CombatCsvPaths.KillDropRatesCsv).Entries.Count);
        }
    }
}
