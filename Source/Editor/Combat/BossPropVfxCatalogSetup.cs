using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Combat
{
    /// <summary>
    /// 보스 기물(철조각) 연출 큐 3종을 <c>DefaultEffectVfxCatalog</c>에 등록한다.
    ///
    /// 왜 CSV(<c>combat_vfx_cues.csv</c>)가 아니라 여기인가: 그 CSV는 <b>카드/패턴</b> 큐의 저작 표면이라
    /// 행이 카드 id나 패턴 id에 묶인다. 기물 연출은 둘 다 아니고 전용 <c>sourceRef</c> 스탬프로만
    /// 해석되므로, 보스 페이즈 전환 버스트(<c>boss.phase.transition</c>)와 <b>같은 선례</b>를 따라
    /// 카탈로그에 직접 저작한다. CSV 리빌드는 cueId가 겹치지 않는 항목을 보존하므로 이 항목들은
    /// 리빌드에 지워지지 않는다(<see cref="VfxCueCatalogTools.RebuildDefaultCatalogFromCombatCsv"/>).
    ///
    /// 멱등하다: 같은 cueId가 있으면 갈아 끼우고, 없으면 덧붙인다.
    /// </summary>
    public static class BossPropVfxCatalogSetup
    {
        private const string CatalogPath = "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset";

        [MenuItem("Tools/Seoul Playup/Combat/Register Boss Prop (철조각) VFX Cues")]
        public static void Register()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"EffectVfxCatalog not found at {CatalogPath}.");
                return;
            }

            var upserts = new List<EffectVfxCatalog.Entry>();
            if (TryBuildEntry(
                    BossPropSourceRefs.VolleyCast,
                    "Boss Prop Volley Cast",
                    "Assets/Prefabs/Vfx/Combat/Boss/BossProp_VolleyCast.prefab",
                    scaleMultiplier: 1.6f,
                    positionOffset: new Vector3(0f, 0.2f, 0f),
                    note: "보스가 철조각을 살포하는 캐스트 순간의 링+기둥 버스트. 임시(기본 도형 파티클) — "
                          + "생성기 Assets/Editor/Dev/IronScrapPlaceholderAssetBuilder.cs.",
                    out var castEntry))
            {
                upserts.Add(castEntry);
            }

            if (TryBuildEntry(
                    BossPropSourceRefs.Placed,
                    "Boss Prop Placed",
                    "Assets/Prefabs/Vfx/Combat/Boss/BossProp_Placed.prefab",
                    scaleMultiplier: 1f,
                    positionOffset: new Vector3(0f, 0.1f, 0f),
                    note: "철조각 하나가 판에 꽂히는 순간의 임팩트 퍼프. 임시(기본 도형 파티클).",
                    out var placedEntry))
            {
                upserts.Add(placedEntry);
            }

            if (TryBuildEntry(
                    BossPropSourceRefs.AbsorbBlast,
                    "Boss Prop Absorb Blast",
                    "Assets/Prefabs/Vfx/Combat/Boss/BossProp_AbsorbBlast.prefab",
                    scaleMultiplier: 1.3f,
                    positionOffset: new Vector3(0f, 0.4f, 0f),
                    note: "철조각이 터지며 보스에게 흡수되는 순간의 폭발. 보스로 빨려 드는 궤적은 "
                          + "MapCombatController.BossProps.cs가 코드로 그린다(목적지가 매번 달라 프리팹으로 못 담는다).",
                    out var absorbEntry))
            {
                upserts.Add(absorbEntry);
            }

            if (TryBuildEntry(
                    CombatState.BossScrapChainSourceRefs.Hit,
                    "Boss Scrap Chain Hit",
                    "Assets/Prefabs/Vfx/Combat/Boss/BossScrapChain_Hit.prefab",
                    scaleMultiplier: 1f,
                    positionOffset: new Vector3(0f, 0.45f, 0f),
                    note: "철조각 사슬이 플레이어 칸에 닿는 순간의 전격 스파크(2026-09-05 후속 #7). 선 자체는 "
                          + "MapCombatController.BossProps.cs가 LineRenderer(BossScrapChain_Line.prefab)로 긋는다. 임시(기본 도형 파티클).",
                    out var chainHitEntry))
            {
                upserts.Add(chainHitEntry);
            }

            if (upserts.Count == 0)
            {
                Debug.LogError("Boss prop VFX cues not registered — 프리팹을 하나도 찾지 못했다. "
                               + "먼저 Seoul Playup/Dev/Rebuild Iron Scrap Placeholder Assets 를 실행할 것.");
                return;
            }

            var upsertedCueIds = new HashSet<string>(upserts.Select(entry => entry.CueId), System.StringComparer.Ordinal);
            var merged = catalog.Entries
                .Where(existing => existing == null || !upsertedCueIds.Contains(existing.CueId))
                .Concat(upserts)
                .ToArray();

            catalog.SetEntries(merged);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Registered {upserts.Count} boss prop VFX cues into {CatalogPath}.");
        }

        private static bool TryBuildEntry(
            string sourceRef,
            string displayName,
            string prefabPath,
            float scaleMultiplier,
            Vector3 positionOffset,
            string note,
            out EffectVfxCatalog.Entry entry)
        {
            entry = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"Boss prop VFX prefab not found: {prefabPath}");
                return false;
            }

            entry = new EffectVfxCatalog.Entry(
                // 보스 페이즈 전환 버스트와 같은 규약: EffectKind를 늘리지 않고 StatusEffectApplied +
                // Field 타깃 + 전용 sourceRef 스탬프로 해석한다. amount 0 + "field" 타깃이라 숫자는 안 뜬다.
                EffectKind.StatusEffectApplied,
                new[] { prefab },
                targetFilter: EffectVfxTargetFilter.Field,
                sourceRef: sourceRef,
                scaleMultiplier: scaleMultiplier,
                scaleWithRadius: false,
                positionOffset: positionOffset,
                cueId: sourceRef,
                displayName: displayName,
                category: "boss.prop",
                tags: new[] { "boss", "prop", "placeholder" },
                designerNote: note,
                floatingTextMode: EffectFloatingTextMode.Hide,
                attachToActor: false);
            return true;
        }
    }
}
