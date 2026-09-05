// NOTE(공개 발췌): 이 파일은 서드파티 에셋 「Cartoon FX Remaster (JMO Assets)」의 프리팹·타입을 경로/이름으로만 참조한다. 해당 에셋은 이 리포에 포함되지 않는다.
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Combat
{
    /// <summary>
    /// 은신 진입 연막 생성기 — CFXR2 Poison Cloud를 소유화 복사해 검보라로 리컬러하고, 그 프리팹을
    /// <c>DefaultEffectVfxCatalog</c>에 수제 엔트리로 업서트한다(2026-09-04 사용자 피드백: 어둑시니
    /// 재은신 전이 연출). 멱등: 다시 실행하면 프리팹·엔트리 모두 덮어쓴다.
    ///
    /// 왜 CSV(<c>combat_vfx_cues.csv</c>)가 아니라 카탈로그 직접 저작인가: 그 CSV는 카드/패턴 큐의
    /// 저작 표면이라 리빌드가 패턴 바인딩과 조인한다 — 바인딩 없는 특성 연출 큐는 조인에서 떨어진다.
    /// 철조각(<see cref="BossPropVfxCatalogSetup"/>)·보스 페이즈 전환 버스트와 같은 선례를 따르며,
    /// CSV 리빌드는 cueId가 겹치지 않는 수제 엔트리를 보존하므로 리빌드에 지워지지 않는다.
    ///
    /// 벤더 프리팹·머티리얼은 건드리지 않는다 — CFXR 셰이더가 파티클 버텍스 컬러를 곱하는 구조라
    /// 색은 전부 startColor / colorOverLifetime 모듈에서 얹는다(머티리얼 소유화 불필요).
    /// 리컬러 방식은 휘도 기반 램프 재사상: 원본 색의 밝기만 남기고 색상을
    /// 근흑(#0D081A) → 보라(#8C5CD9) 램프로 치환한다 — 독구름의 명암 리듬은 유지되고 색만 갈린다.
    /// </summary>
    public static class StealthShadowCloudVfxBuilder
    {
        private const string CatalogPath = "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset";
        private const string SourcePath =
            "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Misc/CFXR2 Poison Cloud.prefab";

        private const string OutputPath =
            "Assets/Prefabs/Vfx/Combat/Monster/MonsterStealth_ShadowCloud.prefab";

        private static readonly Color RampDark = new Color(0.051f, 0.031f, 0.102f);
        private static readonly Color RampBright = new Color(0.549f, 0.361f, 0.851f);

        [MenuItem("Tools/VFX/Build Stealth Shadow Cloud From CFXR2 Poison Cloud")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (source == null)
            {
                Debug.LogError($"[StealthShadowCloud] source prefab not found: {SourcePath}");
                return;
            }

            var instance = Object.Instantiate(source);
            instance.name = "MonsterStealth_ShadowCloud";
            try
            {
                foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.startColor = Remap(main.startColor);

                    var colorOverLifetime = ps.colorOverLifetime;
                    if (colorOverLifetime.enabled)
                    {
                        colorOverLifetime.color = Remap(colorOverLifetime.color);
                    }
                }

                // CFXR 일부 프리팹은 광원을 든다 — 독초록 라이트가 남으면 리컬러가 새므로 잠근다.
                foreach (var light in instance.GetComponentsInChildren<Light>(true))
                {
                    light.enabled = false;
                }

                PrefabUtility.SaveAsPrefabAsset(instance, OutputPath);
                Debug.Log($"[StealthShadowCloud] built {OutputPath}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            RegisterCatalogEntry();
            AssetDatabase.SaveAssets();
        }

        private static void RegisterCatalogEntry()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(CatalogPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath);
            if (catalog == null || prefab == null)
            {
                Debug.LogError($"[StealthShadowCloud] catalog({catalog != null}) or prefab({prefab != null}) missing — entry not registered.");
                return;
            }

            var entry = new EffectVfxCatalog.Entry(
                // 규칙층이 이미 MonsterTraitTriggered + trait.stealth.hidden을 낸다(MonsterStealthState) —
                // kind를 늘리지 않고 그 신호에 정확 sourceRef로 맞는다. 「은신!」 텍스트는 특성 알림
                // 경로가 이미 띄우므로 여기서는 숨긴다(중복 방지).
                EffectKind.MonsterTraitTriggered,
                new[] { prefab },
                targetFilter: EffectVfxTargetFilter.Monster,
                sourceRef: MonsterTraitAnnouncement.StealthHiddenRef,
                scaleMultiplier: 1.5f,
                scaleWithRadius: false,
                positionOffset: new Vector3(0f, 0.3f, 0f),
                cueId: MonsterTraitAnnouncement.StealthHiddenRef,
                displayName: "Monster Stealth Hide Cloud",
                category: "monster.trait",
                tags: new[] { "monster", "stealth", "trait" },
                designerNote: "은신 진입 연막(2026-09-04 피드백) — CFXR2 Poison Cloud 검보라 리컬러. "
                              + "Attack4 동작(eodukshini_utility)과 함께 모델 소실을 가린다. 스케일·오프셋은 랩 육안 튜닝 대상.",
                floatingTextMode: EffectFloatingTextMode.Hide,
                attachToActor: false);

            var merged = catalog.Entries
                .Where(existing => existing == null || !string.Equals(existing.CueId, entry.CueId, System.StringComparison.Ordinal))
                .Concat(new[] { entry })
                .ToArray();
            catalog.SetEntries(merged);
            EditorUtility.SetDirty(catalog);
            Debug.Log($"[StealthShadowCloud] catalog entry upserted (cueId={entry.CueId}).");
        }

        private static ParticleSystem.MinMaxGradient Remap(ParticleSystem.MinMaxGradient gradient)
        {
            switch (gradient.mode)
            {
                case ParticleSystemGradientMode.Color:
                    gradient.color = RemapColor(gradient.color);
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    gradient.colorMin = RemapColor(gradient.colorMin);
                    gradient.colorMax = RemapColor(gradient.colorMax);
                    break;
                case ParticleSystemGradientMode.Gradient:
                    gradient.gradient = RemapGradient(gradient.gradient);
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    gradient.gradientMin = RemapGradient(gradient.gradientMin);
                    gradient.gradientMax = RemapGradient(gradient.gradientMax);
                    break;
            }

            return gradient;
        }

        private static Gradient RemapGradient(Gradient source)
        {
            if (source == null)
            {
                return null;
            }

            var remapped = new Gradient { mode = source.mode };
            var colorKeys = source.colorKeys;
            for (var i = 0; i < colorKeys.Length; i++)
            {
                colorKeys[i].color = RemapColor(colorKeys[i].color);
            }

            remapped.SetKeys(colorKeys, source.alphaKeys);
            return remapped;
        }

        /// <summary>휘도만 남기고 검보라 램프로 치환한다. HDR(>1) 성분은 램프 뒤 강도로 되살린다.</summary>
        private static Color RemapColor(Color color)
        {
            var intensity = Mathf.Max(1f, Mathf.Max(color.r, Mathf.Max(color.g, color.b)));
            var normalized = color / intensity;
            var luminance = Mathf.Clamp01(
                0.299f * normalized.r + 0.587f * normalized.g + 0.114f * normalized.b);
            var ramped = Color.Lerp(RampDark, RampBright, luminance) * intensity;
            ramped.a = color.a;
            return ramped;
        }
    }
}
