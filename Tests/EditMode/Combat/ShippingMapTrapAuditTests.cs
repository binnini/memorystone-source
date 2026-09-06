#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Unity;
using UnityEngine;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 출하 저작물 전수 감사: 맵에 실제로 놓인 함정이 런타임에서 해소되는지 본다.
    ///
    /// 이 테스트가 없어서 실제로 사고가 났다 — <c>HexTrapEffectKind</c>에는 대응하는
    /// <c>StatusEffectKind</c>가 없는 값(Burn, VisionDown)이 있었고, 그런 함정을 밟으면
    /// <c>ToTrapStatusEffectKind</c>가 <see cref="NotSupportedException"/>을 던져 전투가 죽었다.
    /// 저작 인스펙터의 enum 드롭다운은 그 값들을 아무 경고 없이 보여 줬고, 출하 맵 2종에 실제로
    /// 4건이 들어가 있었다(GwangjinGu Burn×1 + VisionDown×2, EastSeoul Burn×1).
    ///
    /// 그래서 이 감사는 "코드가 값을 처리하는가"가 아니라 <b>"저작이 코드에 착지하는가"</b>를 본다.
    /// 계약: 맵 소스와 프리셋 카탈로그에 저작된 모든 함정 효과 kind는 런타임 해소 경로 중 하나에
    /// 반드시 떨어져야 한다.
    /// </summary>
    public sealed class ShippingMapTrapAuditTests
    {
        [Test]
        public void EveryAuthoredTrapEffectKindResolvesAtRuntime()
        {
            var presetCatalog = TrapPresetCatalog.LoadDefault();
            var sources = LoadAuthoringSources();
            var failures = new List<string>();
            var auditedTraps = 0;

            foreach (var (path, source) in sources)
            {
                foreach (var trapRef in source.TrapRefs.Where(trapRef => trapRef != null))
                {
                    auditedTraps++;
                    // 프리셋이 걸려 있으면 프리셋이 실효값이다 — 런타임이 보는 것과 같은 경로로 굳힌다.
                    var runtimeTrap = trapRef.ToRuntimeTrapData(presetCatalog);
                    foreach (var effect in runtimeTrap.Effects)
                    {
                        if (!TryResolveTrapEffectKind(effect.Kind, out var reason))
                        {
                            failures.Add($"{path} / {trapRef.TrapId}: {reason}");
                        }
                    }
                }
            }

            Assert.That(auditedTraps, Is.GreaterThan(0), "출하 맵에서 함정을 하나도 못 읽었다 — 감사가 빈 채로 통과하고 있다.");
            Assert.That(
                failures,
                Is.Empty,
                "출하 맵에 밟으면 크래시하는 함정이 저작돼 있다:\n" + string.Join("\n", failures));
        }

        [Test]
        public void EveryTrapPresetEffectKindResolvesAtRuntime()
        {
            var presetCatalog = TrapPresetCatalog.LoadDefault();
            Assert.That(presetCatalog, Is.Not.Null);

            var failures = presetCatalog.Presets
                .Where(preset => preset != null && !TryResolveTrapEffectKind(preset.EffectKind, out _))
                .Select(preset => $"preset '{preset.PresetId}': {preset.EffectKind}")
                .ToList();

            Assert.That(
                failures,
                Is.Empty,
                "함정 프리셋 카탈로그에 런타임이 해소하지 못하는 효과가 있다 — 배치하는 순간 지뢰가 된다:\n"
                + string.Join("\n", failures));
        }

        /// <summary>
        /// 소환 함정과 상태 카드 함정은 <b>다른 카탈로그의 id를 문자열로 가리킨다</b> — 그쪽에서 id를
        /// 바꾸거나 지우면 프리셋은 멀쩡해 보이는 채로 아무것도 안 하는 함정이 된다(런타임은 조용히
        /// 무시한다: <c>ApplyTrapSpawnEffect</c>는 report만 남기고, <c>TryInjectStatusCard</c>는 false를
        /// 돌려준다). 컴파일러도 다른 감사도 못 잡는 드리프트라 여기서 못을 박는다.
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void EveryTrapPresetCrossCatalogReferenceResolves()
        {
            var presetCatalog = TrapPresetCatalog.LoadDefault();
            Assert.That(presetCatalog, Is.Not.Null);

            var monsterBundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory);
            // ⚠️출하 cards.csv를 런타임과 같은 경로로 읽는다. 옛 팩토리(P3-b에서 DemoCardCatalog로 은퇴)는 CSV 이전의
            // 하드코딩 폴백이라 상태 카드(X01~X03)가 아예 없다 — 그걸 보면 이 감사는 항상 거짓 실패한다.
            // 캐시하지 않는 이유는 ShippingCardCsvEffectTests.ShippingCatalog 주석 참고(도메인 리로드 함정).
            var cardCatalogAsset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            CardCatalogDefinition cardCatalog;
            try
            {
                cardCatalogAsset.SetRows(CardCatalogAsset.ParseCsvText(
                    File.ReadAllText(CombatCsvPaths.CardsCsv, new UTF8Encoding(false, true))));
                cardCatalog = cardCatalogAsset.ToCardCatalogDefinition(CombatConfig.Default);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cardCatalogAsset);
            }
            var failures = new List<string>();

            foreach (var preset in presetCatalog.Presets.Where(candidate => candidate != null))
            {
                if (preset.EffectKind == HexTrapEffectKind.SpawnMonsters)
                {
                    if (string.IsNullOrWhiteSpace(preset.MonsterDefinitionId))
                    {
                        failures.Add($"preset '{preset.PresetId}': SpawnMonsters인데 monsterDefinitionId가 비었다.");
                    }
                    else if (monsterBundle.MonsterCatalog.Entries.All(
                                 entry => !string.Equals(entry.Id, preset.MonsterDefinitionId, StringComparison.Ordinal)))
                    {
                        failures.Add(
                            $"preset '{preset.PresetId}': monster_catalog.csv에 없는 '{preset.MonsterDefinitionId}'를 부른다.");
                    }
                }

                if (preset.EffectKind != HexTrapEffectKind.InjectStatusCard)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(preset.StatusCardId))
                {
                    failures.Add($"preset '{preset.PresetId}': InjectStatusCard인데 statusCardId가 비었다.");
                    continue;
                }

                // TryInjectStatusCard와 같은 계약: 카드 id로 찾고, 덱에 안 섞이는 카드여야 한다
                // (includeInDecks=TRUE인 카드를 넣으면 상태 카드가 아니라 진짜 카드를 공짜로 주는 함정이 된다).
                var card = cardCatalog.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.Id, preset.StatusCardId, StringComparison.Ordinal));
                if (card == null)
                {
                    failures.Add($"preset '{preset.PresetId}': cards.csv에 없는 상태 카드 '{preset.StatusCardId}'를 넣는다.");
                }
                else if (card.IncludeInGameplayDecks)
                {
                    failures.Add(
                        $"preset '{preset.PresetId}': '{preset.StatusCardId}'는 includeInDecks=TRUE라 "
                        + "상태 카드가 아니다 — 함정이 플레이어에게 쓸 수 있는 카드를 공짜로 준다.");
                }
            }

            Assert.That(
                failures,
                Is.Empty,
                "함정 프리셋이 다른 카탈로그의 사라진 id를 가리킨다 — 배치해도 아무 일도 일어나지 않는다:\n"
                + string.Join("\n", failures));
        }

        /// <summary>
        /// D-4: 함정은 전부 플레이어 전용이다. 런타임은 <c>affectsMonsters</c>를 여전히 지원하지만
        /// (테스트 픽스처가 쓴다) 출하 저작물에서는 쓰지 않기로 확정했다 — 몬스터가 함정을 밟는 순간
        /// 의도 예고·경로 계획이 어긋나기 때문이다. 계약을 저작물 쪽에만 건다.
        /// </summary>
        [Test]
        public void NoAuthoredTrapTargetsMonsters()
        {
            var presetCatalog = TrapPresetCatalog.LoadDefault();
            var offenders = new List<string>();

            foreach (var (path, source) in LoadAuthoringSources())
            {
                offenders.AddRange(source.TrapRefs
                    .Where(trapRef => trapRef != null && trapRef.ToRuntimeTrapData(presetCatalog).AffectsMonsters)
                    .Select(trapRef => $"{path} / {trapRef.TrapId}"));
            }

            offenders.AddRange(presetCatalog.Presets
                .Where(preset => preset != null && preset.AffectsMonsters)
                .Select(preset => $"preset '{preset.PresetId}'"));

            Assert.That(
                offenders,
                Is.Empty,
                "D-4 위반: 출하 함정은 플레이어 전용이어야 한다(affectsMonsters=0).\n" + string.Join("\n", offenders));
        }

        /// <summary>
        /// 스폰 함정(C-7/C-10)의 저작 착지 감사. kind가 해소되는 것만으로는 부족하다 —
        /// <c>monsterDefinitionId</c>가 몬스터 카탈로그에 없으면 밟아도 <b>아무 일도 일어나지 않는다</b>.
        /// 크래시가 아니라 조용한 무발동이라 플레이로는 "함정이 안 터졌나" 정도로만 보이고, 그게 정확히
        /// Burn 유령 값이 4건이나 들어갈 수 있었던 종류의 구멍이다.
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void EveryAuthoredSpawnTrapNamesAKnownMonster()
        {
            var known = new HashSet<string>(
                MonsterCatalogCsvConverter
                    .ConvertDirectories(CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory)
                    .MonsterCatalog.Entries.Select(entry => entry.Id),
                StringComparer.Ordinal);
            Assert.That(known, Is.Not.Empty, "몬스터 카탈로그를 하나도 못 읽었다 — 감사가 빈 채로 통과하고 있다.");

            var presetCatalog = TrapPresetCatalog.LoadDefault();
            var failures = new List<string>();

            foreach (var (path, source) in LoadAuthoringSources())
            {
                foreach (var trapRef in source.TrapRefs.Where(trapRef => trapRef != null))
                {
                    foreach (var effect in trapRef.ToRuntimeTrapData(presetCatalog).Effects
                                 .Where(effect => effect.Kind == HexTrapEffectKind.SpawnMonsters))
                    {
                        if (!known.Contains(effect.MonsterDefinitionId))
                        {
                            failures.Add($"{path} / {trapRef.TrapId}: monsterDefinitionId '{effect.MonsterDefinitionId}'");
                        }
                    }
                }
            }

            failures.AddRange(presetCatalog.Presets
                .Where(preset => preset != null
                                 && preset.EffectKind == HexTrapEffectKind.SpawnMonsters
                                 && !known.Contains(preset.MonsterDefinitionId))
                .Select(preset => $"preset '{preset.PresetId}': monsterDefinitionId '{preset.MonsterDefinitionId}'"));

            Assert.That(
                failures,
                Is.Empty,
                "스폰 함정이 카탈로그에 없는 몬스터를 부른다 — 밟아도 조용히 아무 일도 안 일어난다:\n"
                + string.Join("\n", failures));
        }

        /// <summary>
        /// C-17: 상태 카드 삽입 함정이 가리키는 카드가 실제로 카탈로그에 있는지. 스폰 함정과 같은
        /// 구멍이다 — 없는 id를 저작하면 크래시가 아니라 <b>조용한 무발동</b>이라 플레이로는 안 드러난다.
        /// includeInDecks=FALSE도 함께 요구한다: TRUE인 카드를 넣으면 정상 덱에도 섞여 버린다.
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void EveryAuthoredStatusCardTrapNamesAnInjectableCard()
        {
            // 실제 출하 cards.csv를 런타임과 같은 파싱 경로로 태운다(card-catalog-audit 도구와 동일).
            var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            HashSet<string> injectable;
            try
            {
                asset.SetRows(CardCatalogAsset.ParseCsvText(
                    File.ReadAllText(CombatCsvPaths.CardsCsv, new UTF8Encoding(false, true))));
                injectable = new HashSet<string>(
                    asset.ToCardCatalogDefinition(CombatConfig.Default).Entries
                        .Where(entry => !entry.IncludeInGameplayDecks)
                        .Select(entry => entry.Id),
                    StringComparer.Ordinal);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
            Assert.That(injectable, Is.Not.Empty, "삽입 가능한 카드를 하나도 못 읽었다 — 감사가 빈 채로 통과하고 있다.");

            var presetCatalog = TrapPresetCatalog.LoadDefault();
            var failures = new List<string>();

            foreach (var (path, source) in LoadAuthoringSources())
            {
                foreach (var trapRef in source.TrapRefs.Where(trapRef => trapRef != null))
                {
                    failures.AddRange(trapRef.ToRuntimeTrapData(presetCatalog).Effects
                        .Where(effect => effect.Kind == HexTrapEffectKind.InjectStatusCard
                                         && !injectable.Contains(effect.StatusCardId))
                        .Select(effect => $"{path} / {trapRef.TrapId}: statusCardId '{effect.StatusCardId}'"));
                }
            }

            failures.AddRange(presetCatalog.Presets
                .Where(preset => preset != null
                                 && preset.EffectKind == HexTrapEffectKind.InjectStatusCard
                                 && !injectable.Contains(preset.StatusCardId))
                .Select(preset => $"preset '{preset.PresetId}': statusCardId '{preset.StatusCardId}'"));

            Assert.That(
                failures,
                Is.Empty,
                "상태 카드 함정이 삽입할 수 없는 카드를 가리킨다 — 밟아도 조용히 아무 일도 안 일어난다:\n"
                + string.Join("\n", failures));
        }

        /// <summary>
        /// C-11: 예고형(주기) 함정은 <b>밟기 트리거를 겸할 수 없다</b>. 런타임이 예고형을 밟기 경로에서
        /// 조용히 빼기 때문에, 저작이 triggerOnEnter를 켜 두면 "켰는데 아무 일도 안 일어나는" 값이 된다 —
        /// 그게 정확히 죽은 저작이고, 여기서 저작 단계에 막는다.
        /// </summary>
        [Test]
        public void PeriodicTrapsAreNotAlsoStepTriggered()
        {
            var presetCatalog = TrapPresetCatalog.LoadDefault();
            var offenders = new List<string>();

            foreach (var (path, source) in LoadAuthoringSources())
            {
                offenders.AddRange(source.TrapRefs
                    .Where(trapRef => trapRef != null)
                    .Select(trapRef => (trapRef, runtime: trapRef.ToRuntimeTrapData(presetCatalog)))
                    .Where(entry => entry.runtime.IsPeriodic && entry.runtime.TriggerOnEnter)
                    .Select(entry => $"{path} / {entry.trapRef.TrapId}"));
            }

            offenders.AddRange(presetCatalog.Presets
                .Where(preset => preset != null && preset.PeriodTurns > 0 && preset.TriggerOnEnter)
                .Select(preset => $"preset '{preset.PresetId}'"));

            Assert.That(
                offenders,
                Is.Empty,
                "주기형 함정에 triggerOnEnter가 켜져 있다 — 런타임은 예고형을 밟기 경로에서 빼므로 켜도 아무 일도 안 일어난다:\n"
                + string.Join("\n", offenders));
        }

        /// <summary>
        /// 런타임의 <c>ApplyTrapEffect</c> 분기를 그대로 흉내 낸다: Damage·Teleport·SpawnMonsters는
        /// 상태이상이 아니라 앞단에서 처리되고, 나머지는 전부 <c>ToTrapStatusEffectKind</c>를 통과해야 한다.
        /// 새 <see cref="HexTrapEffectKind"/>를 추가하면서 런타임 분기를 빼먹으면 여기서 잡힌다.
        /// </summary>
        private static bool TryResolveTrapEffectKind(HexTrapEffectKind kind, out string reason)
        {
            reason = string.Empty;
            if (kind == HexTrapEffectKind.Damage
                || kind == HexTrapEffectKind.Teleport
                || kind == HexTrapEffectKind.SpawnMonsters
                || kind == HexTrapEffectKind.InjectStatusCard)
            {
                return true;
            }

            try
            {
                CombatState.ToTrapStatusEffectKind(kind);
                return true;
            }
            catch (NotSupportedException exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        private static List<(string Path, HexSparseMapAuthoringSource Source)> LoadAuthoringSources()
        {
            var sources = AssetDatabase.FindAssets($"t:{nameof(HexSparseMapAuthoringSource)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => (Path: path, Source: AssetDatabase.LoadAssetAtPath<HexSparseMapAuthoringSource>(path)))
                .Where(entry => entry.Source != null)
                .ToList();

            Assert.That(sources, Is.Not.Empty, "맵 저작 소스를 하나도 못 찾았다 — 감사가 빈 채로 통과하고 있다.");
            return sources;
        }
    }
}
#endif
