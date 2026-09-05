using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Combat
{
    /// <summary>
    /// 터렛 사격(A017/A023/A024 · V022~V025) <b>총기 리워크</b> 프리팹 생성기 — 미배정 VFX 배치 1.
    /// 1차분(cs:1157, Hovl 전기 스파크 계열)은 사용자 기각: "실제 총기가 사격하는 느낌"이 목표라
    /// 총구 화염·탄착 불꽃 전문 팩인 <b>WarFX</b>(2026-08-18 신규 반입, JMO)로 갈아탄다.
    ///
    /// 왜 코드로 만드는가 · ThirdParty 무수정 · 완전 언팩 · +Z 전방 규약은
    /// <see cref="MonsterAttackVfxBuilder"/> 클래스 주석의 계약을 그대로 따른다. 프리팹 경로를
    /// 유지하므로 CSV(V022~V025)·카탈로그·테스트는 손대지 않아도 그림만 갈린다.
    ///
    /// 🔴 <b>머즐은 통째 재생성이 아니라 수술 교체다.</b> 기존 프리팹의 <c>Tracer</c> 자식
    /// (+Z 스트레치 탄 궤적)은 1차 검수에서 사용자 승인을 받았다 — <c>MuzzleFlash</c> 자식만
    /// 갈아 끼우고 나머지는 보존한다. 착탄은 보존 대상이 없어 통째 재생성한다.
    ///
    /// 방향 검산(둘 다 벤더 루트 회전을 자식에 보존하는 것으로 충족):
    /// <list type="bullet">
    /// <item>WFX_MF 4P: 팩 자연 전방 +X, 루트에 Y270이 구워져 있어 부모 +Z로 발사된다.</item>
    /// <item>WFX_BImpact: 저작 업(+Y) 분사, 루트 X270으로 부모 −Z(사수 쪽) 분사가 된다 —
    /// 타겟 앵커도 공격 방향으로 페이싱되므로(브리지 <c>TryResolveFacingRotation</c>)
    /// 탄착 불꽃이 사수 쪽으로 튄다.</item>
    /// </list>
    /// </summary>
    public static class TurretGunVfxBuilder
    {
        private const string MuzzlePrefabPath = "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_TurretMuzzle.prefab";
        private const string ImpactPrefabPath = "Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_TurretImpact.prefab";

        private const string MuzzleSource = "Assets/ThirdParty/JMO Assets/WarFX/_Effects/MuzzleFlashes/4Planes/WFX_MF 4P RIFLE1.prefab";
        private const string ImpactMetalSource = "Assets/ThirdParty/JMO Assets/WarFX/_Effects/Bullet Impacts/WFX_BImpact Metal.prefab";
        private const string ImpactConcreteSource = "Assets/ThirdParty/JMO Assets/WarFX/_Effects/Bullet Impacts/WFX_BImpact Concrete.prefab";

        [MenuItem("Tools/Seoul Playup/Combat/Rebuild Turret Gun VFX (미배정 배치 1)")]
        public static void Rebuild()
        {
            var built = new List<string> { BuildMuzzle(), BuildImpact() };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TurretGunVfxBuilder] rebuilt:\n  " + string.Join("\n  ", built));
        }

        /// <summary>머즐 = 기존 프리팹에서 MuzzleFlash 자식만 WarFX 라이플 머즐로 교체(Tracer 보존).</summary>
        private static string BuildMuzzle()
        {
            var root = PrefabUtility.LoadPrefabContents(MuzzlePrefabPath);
            try
            {
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var child = root.transform.GetChild(i);
                    if (child.name == "MuzzleFlash")
                    {
                        Object.DestroyImmediate(child.gameObject);
                    }
                }

                // 1차분은 루트 자체에도 전기 콘 스파크 PS를 얹었다(기각 대상) — 루트
                // GameObject(경로·GUID)는 남기고 컴포넌트만 걷어 낸다. 렌더러가 PS에
                // 의존하므로 렌더러부터 지운다.
                var rootRenderer = root.GetComponent<ParticleSystemRenderer>();
                var rootPs = root.GetComponent<ParticleSystem>();
                if (rootRenderer != null)
                {
                    Object.DestroyImmediate(rootRenderer);
                }
                if (rootPs != null)
                {
                    Object.DestroyImmediate(rootPs);
                }

                var flash = Clone(MuzzleSource);
                flash.name = "MuzzleFlash";
                Attach(flash, root.transform);

                // 실시간 광원 + WFX_LightFlicker는 통째로 뺀다 — 광원 수 계약
                // (MonsterAttackVfxBuilder.StripLights와 같은 이유) + 단발 사격에 플리커는 무의미.
                var lightChild = flash.transform.Find("Light");
                if (lightChild != null)
                {
                    Object.DestroyImmediate(lightChild.gameObject);
                }

                // 원본은 연사 시뮬(loop + rate 10/s · 수명 0.05의 깜빡이는 플래시)이다.
                // 단발 사격이므로 t=0 버스트 하나로 바꾼다 — 플래시 판 4장은 각 1발,
                // 스파크만 12발로 다발을 준다(원본 밀도 그대로는 단발에서 안 읽힌다).
                foreach (var ps in flash.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.loop = false;
                    // 원본 duration 1s는 연사 컨테이너 잔재다 — 파티클은 0.1s 안에 죽는데
                    // 시스템 길이가 1s로 남으면 실측(duration+수명)이 1.05s로 부풀어
                    // 연출 길이 데이터가 거짓말을 한다. 플래시 실제 수명에 맞춘다.
                    main.duration = 0.2f;
                    var emission = ps.emission;
                    emission.rateOverTime = 0f;
                    emission.SetBursts(new[]
                    {
                        new ParticleSystem.Burst(0f, (short)(ps.name == "Sparks" ? 12 : 1)),
                    });
                }

                PrefabUtility.SaveAsPrefabAsset(root, MuzzlePrefabPath);
                return MuzzlePrefabPath;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>착탄 = WFX_BImpact Metal(섬광+금속 스파크)에 Concrete의 파편·직진 연기를 접붙인
        /// 통째 재생성. 같은 경로를 덮어써 GUID가 유지된다.</summary>
        private static string BuildImpact()
        {
            var content = Clone(ImpactMetalSource);
            content.name = "BulletImpact";

            // 수명은 게임(연출 스케줄러)이 관리한다 — 벤더 자기파괴 스크립트는 뺀다.
            foreach (var script in content.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Object.DestroyImmediate(script);
            }

            var concrete = Clone(ImpactConcreteSource);
            Graft(concrete, "Debris", content.transform);
            Graft(concrete, "Directional Smoke", content.transform);
            Object.DestroyImmediate(concrete);

            // 파편 수명 0.5~2s는 콘크리트 폭발용이다 — 총알 탄착은 스냅해야 하므로
            // 0.4~1s로 조인다(실측 길이도 2.5s → 1.5s로 줄어 연출 스케줄이 늘어지지 않는다).
            var debris = content.transform.Find("Debris");
            if (debris != null)
            {
                var debrisMain = debris.GetComponent<ParticleSystem>().main;
                debrisMain.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1f);
            }

            // 🔴 파편·연기 원본 머티리얼은 Legacy Particles/Alpha Blended다 — URP에서
            // 마젠타로 깨진다(WFX/Additive Alpha8 커스텀 셰이더와 달리 SRP 비호환).
            // 색·그림만 승계한 URP 파티클 언릿 사본을 소유 폴더에 굽고 relink한다(클로 선례).
            RelinkToUrpAlphaBlended(content, "Debris", "TurretImpact_Debris");
            RelinkToUrpAlphaBlended(content, "Directional Smoke", "TurretImpact_Smoke");

            // 금속 스파크 6발은 우리 카메라 거리에서 안 읽힌다 — 12발로 단발 임팩트를 세운다.
            var sparks = content.transform.Find("Sparks");
            if (sparks != null)
            {
                var emission = sparks.GetComponent<ParticleSystem>().emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)12) });
            }

            var root = new GameObject("MonsterAttack_TurretImpact");
            try
            {
                Attach(content, root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, ImpactPrefabPath);
                return ImpactPrefabPath;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ── 헬퍼(MonsterAttackVfxBuilder 계약의 축약 사본 — private라 재사용 불가) ──────────

        /// <summary>원본 프리팹을 인스턴스화하고 <b>완전 언팩</b>한다 — variant로 저장되면
        /// 팩 갱신에 우리 개조분이 같이 흔들린다.</summary>
        private static GameObject Clone(string assetPath)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null)
            {
                throw new System.InvalidOperationException("base VFX prefab not found: " + assetPath);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return instance;
        }

        /// <summary>부모에 붙이되 <b>벤더 루트 회전은 보존</b>한다(방향 검산은 클래스 주석).
        /// scalingMode=Hierarchy 강제는 큐 <c>scaleMultiplier</c>(LV 차등 0.8/1/1.25)가 먹기 위한
        /// 전제다 — 기본 Local은 부모 스케일을 무시한다.</summary>
        private static void Attach(GameObject content, Transform parent)
        {
            content.transform.SetParent(parent, false);
            content.transform.localPosition = Vector3.zero;
            foreach (var ps in content.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }

        /// <summary>다른 벤더 프리팹의 자식 하나를 로컬값 그대로 옮겨 붙인다 — Metal·Concrete
        /// 루트가 같은 회전 규약(X270)이라 로컬 보존이 곧 방향 보존이다.</summary>
        private static void Graft(GameObject from, string childName, Transform to)
        {
            var child = from.transform.Find(childName);
            if (child == null)
            {
                throw new System.InvalidOperationException(
                    "graft child not found: " + childName + " in " + from.name);
            }

            child.SetParent(to, false);
        }

        private const string MaterialFolder = "Assets/Prefabs/Vfx/Combat/Materials";

        /// <summary>레거시 알파블렌드 벤더 머티리얼의 그림·틴트를 승계한 URP Particles/Unlit
        /// 사본을 소유 폴더에 만들고(멱등 — 같은 경로 갱신) 렌더러를 갈아 끼운다.</summary>
        private static void RelinkToUrpAlphaBlended(GameObject content, string childName, string materialName)
        {
            var child = content.transform.Find(childName);
            if (child == null)
            {
                return;
            }

            var renderer = child.GetComponent<ParticleSystemRenderer>();
            var source = renderer.sharedMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                throw new System.InvalidOperationException("URP Particles/Unlit shader not found");
            }

            var path = MaterialFolder + "/" + materialName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            // URP 파티클 언릿의 알파 블렌드 세팅 — 인스펙터(BaseShaderGUI)가 하는 일을 수동으로.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (source != null)
            {
                material.SetTexture("_BaseMap", source.mainTexture);
                // 레거시 파티클 셰이더의 틴트는 _TintColor에 산다(_Color 아님).
                var tint = source.HasProperty("_TintColor") ? source.GetColor("_TintColor") : Color.white;
                material.SetColor("_BaseColor", tint);
            }

            EditorUtility.SetDirty(material);
            renderer.sharedMaterial = material;
        }
    }
}
