using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 출하 저작을 그대로 실어 도감 도메인을 세우는 공유 자리.
    /// <para>
    /// 도메인 <b>하나</b>의 계약을 재는 시험(<c>CodexCardDomainTests</c> 등)은 각자 로더를 갖는 편이
    /// 읽기 쉽다. 여기 있는 것은 <b>도메인을 가로지르는</b> 시험을 위한 것이다 — "모든 도메인이
    /// 원값 표를 채우는가" 같은 물음은 여섯을 한꺼번에 세워야 물을 수 있고, 그 조립을 시험마다
    /// 복사하면 도메인이 하나 늘 때 어느 시험이 그것을 빠뜨렸는지 아무도 모른다.
    /// </para>
    /// </summary>
    public static class CodexShippingDomains
    {
        // cs:1175에서 빌드 null 지뢰 수리로 Resources/ 하위로 이동했다 — 이 경로가 정본이다.
        public const string TrapCatalogPath = "Assets/Data/Object/Trap/Resources/TrapPresetCatalog.asset";

        /// <summary>시험은 색을 보지 않는다 — 도메인마다 다른 색을 넣어 봐야 읽는 사람만 헷갈린다.</summary>
        public static readonly Color Accent = Color.white;

        /// <summary>
        /// 🔴 형상 라이브러리를 먼저 실어야 한다 — 안 실으면 몬스터 패턴 도해와 형상 도메인이
        /// 예외가 아니라 <b>조용히 빈 집합</b>이 된다.
        /// </summary>
        public static void EnsureAttackShapeLibrary()
        {
            AttackShapeLibrary.Initialize(
                AttackShapeCatalogCsv.ConvertText(
                    File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8)));
        }

        public static CardCatalogDefinition LoadCardCatalog()
        {
            var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            try
            {
                asset.SetRows(CardCatalogAsset.ParseCsvText(File.ReadAllText(CombatCsvPaths.CardsCsv)));
                return asset.ToCardCatalogDefinition(CombatConfig.Default);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        public static StatusEffectCatalogDefinition LoadStatusEffectCatalog() =>
            StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);

        public static RelicCatalogDefinition LoadRelicCatalog() =>
            RelicCatalogCsv.ConvertText(File.ReadAllText(CombatCsvPaths.RelicsCsv, Encoding.UTF8));

        public static ConsumableItemCatalogDefinition LoadConsumableCatalog() =>
            ConsumableItemCatalogCsv.ConvertText(
                File.ReadAllText(CombatCsvPaths.ConsumableItemsCsv, Encoding.UTF8));

        public static CodexObjectCatalog LoadMapObjectCodexCatalog() =>
            CodexObjectCatalogCsv.ConvertText(
                File.ReadAllText(CombatCsvPaths.MapObjectsCsv, Encoding.UTF8));

        public static MapObjectCatalogSet LoadMapObjectCatalogSet()
        {
            var catalogSet = AssetDatabase.LoadAssetAtPath<MapObjectCatalogSet>(
                MapObjectCatalogSet.DefaultCatalogAssetPath);
            Assert.That(catalogSet, Is.Not.Null, "출하 맵 오브젝트 카탈로그를 찾지 못했다.");
            return catalogSet;
        }

        public static TrapPresetCatalog LoadTrapCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TrapPresetCatalog>(TrapCatalogPath);
            Assert.That(catalog, Is.Not.Null, "출하 함정 프리셋 카탈로그를 찾지 못했다.");
            return catalog;
        }

        public static MonsterCatalogDefinition LoadMonsterCatalog() =>
            MonsterCatalogCsvConverter.ConvertDirectories(
                    CombatCsvPaths.MonsterDirectory,
                    CombatCsvPaths.PresentationDirectory,
                    "codex-shared-test",
                    "Codex Shared Test")
                .MonsterCatalog;

        /// <summary>
        /// 로비가 세우는 것과 <b>같은 일곱</b>(카드 · 상태이상 · 유물 · 함정 · 소모품 · 몬스터 · 오브젝트).
        /// 개발 전용인 형상 도메인은 <see cref="BuildAttackShapeDomain"/>으로 따로 세운다 —
        /// 그것까지 여기 넣으면 "출하에 서는 도메인"을 세는 시험이 하나 더 세게 된다.
        /// </summary>
        public static IReadOnlyList<ICodexDomain> BuildAll()
        {
            EnsureAttackShapeLibrary();

            return new ICodexDomain[]
            {
                new CodexCardDomain(LoadCardCatalog(), Accent),
                new CodexStatusEffectDomain(LoadStatusEffectCatalog(), icons: null, Accent),
                new CodexRelicDomain(LoadRelicCatalog(), Accent),
                new CodexTrapDomain(LoadTrapCatalog(), icons: null, Accent),
                new CodexConsumableItemDomain(LoadConsumableCatalog(), Accent),
                new CodexMonsterDomain(LoadMonsterCatalog(), Accent),
                new CodexObjectDomain(LoadMapObjectCodexCatalog(), Accent, LoadMapObjectCatalogSet()),
            };
        }

        public static CodexAttackShapeDomain BuildAttackShapeDomain()
        {
            EnsureAttackShapeLibrary();
            return new CodexAttackShapeDomain(Accent);
        }
    }
}
