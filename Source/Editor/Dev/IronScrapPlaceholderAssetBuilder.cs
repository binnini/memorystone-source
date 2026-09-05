using System.IO;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Dev
{
    /// <summary>
    /// 철조각(보스 기물 M901)의 <b>임시</b> 에셋 생성기 — 아트 반입 전까지 쓰는 placeholder다.
    /// 만드는 것: 빨간 원기둥 모델 1개 + 기본 도형 파티클 VFX 3개(살포 캐스트 / 기물 착지 / 흡수 폭발).
    ///
    /// 왜 코드로 만드는가: 프리팹을 손으로 저작하면 "무엇이 임시이고 무엇이 확정인지"가 사라진다.
    /// 이 파일이 남아 있는 동안은 전부 임시이고, 아트가 들어오면 이 빌더를 지우고 프리팹만 남긴다.
    ///
    /// 멱등하다 — 다시 돌리면 같은 경로를 덮어쓴다. 실행 후 반드시 카탈로그 배선을 확인할 것
    /// (모델은 <c>monster_catalog.csv</c>의 visualPrefabPath, VFX는 EffectVfxCatalog 항목).
    /// </summary>
    public static class IronScrapPlaceholderAssetBuilder
    {
        private const string ModelFolder = "Assets/Art/Characters/monster/IronScrap/Prefabs";
        private const string ModelPath = ModelFolder + "/IronScrapPlaceholder.prefab";
        private const string VfxFolder = "Assets/Prefabs/Vfx/Combat/Boss";
        private const string MaterialFolder = "Assets/Art/Characters/monster/IronScrap/Materials";
        private const string MaterialPath = MaterialFolder + "/IronScrapPlaceholder.mat";

        private const string CastVfxPath = VfxFolder + "/BossProp_VolleyCast.prefab";
        private const string PlacedVfxPath = VfxFolder + "/BossProp_Placed.prefab";
        private const string AbsorbVfxPath = VfxFolder + "/BossProp_AbsorbBlast.prefab";
        private const string ChainLinePath = VfxFolder + "/BossScrapChain_Line.prefab";
        private const string ChainLineMaterialPath = VfxFolder + "/BossScrapChain_Line.mat";
        private const string ChainHitVfxPath = VfxFolder + "/BossScrapChain_Hit.prefab";

        // 폭발 반경 1 원판(인접 중심 간 √3 ≈ 1.73)을 덮는 세계 반경. 카탈로그 scaleMultiplier 1.3이 곱해지므로
        // 프리팹 자체는 1.73/1.3 ≈ 1.33에 맞춘다 — 링이 정확히 반경 1 칸의 바깥 테두리에서 꺼진다.
        private const float AbsorbDiskWorldRadius = 1.33f;
        private static readonly Color ChainYellow = new Color(1f, 0.85f, 0.25f, 1f);

        // 헥스 타일 반경이 1이라는 전제(AtlasTilePresentationView.tileRadius). 한 칸 안에 넉넉히 들어가고
        // 옆 칸 기물과 붙어 보이지 않는 크기다. Unity Cylinder 프리미티브는 반지름 1 · 높이 2라서
        // localScale 0.4/0.6/0.4 → 지름 0.8 · 높이 1.2가 된다.
        private static readonly Vector3 BodyScale = new Vector3(0.4f, 0.6f, 0.4f);
        private static readonly Color ScrapRed = new Color(0.85f, 0.15f, 0.12f, 1f);

        [MenuItem("Seoul Playup/Dev/Rebuild Iron Scrap Placeholder Assets")]
        public static void Rebuild()
        {
            EnsureFolder(ModelFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(VfxFolder);

            var material = CreateOrUpdateMaterial();
            BuildModelPrefab(material);
            BuildCastVfx();
            BuildPlacedVfx();
            BuildAbsorbVfx();
            BuildChainLinePrefab();
            BuildChainHitVfx();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Iron scrap placeholder assets rebuilt:\n"
                + $"  model  {ModelPath}\n"
                + $"  vfx    {CastVfxPath}\n"
                + $"  vfx    {PlacedVfxPath}\n"
                + $"  vfx    {AbsorbVfxPath}\n"
                + $"  line   {ChainLinePath}\n"
                + $"  vfx    {ChainHitVfxPath}");
        }

        private static Material CreateOrUpdateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (shader != null)
            {
                material.shader = shader;
            }

            material.color = ScrapRed;
            // URP Lit은 _BaseColor를 본다 — _Color만 세우면 인스펙터에서만 빨갛고 화면에선 흰색이다.
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", ScrapRed);
            }

            // 살짝 발광시켜 어두운 인디고 야경 배경에서도 읽히게 한다(이 게임의 맵은 밤이다).
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", ScrapRed * 0.6f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildModelPrefab(Material material)
        {
            var root = new GameObject("IronScrapPlaceholder");
            try
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                body.name = "Body";
                // 모델 콜라이더는 카드 타깃팅·클릭 히트테스트를 가로챌 수 있어 지운다. 마우스 호버
                // 판정은 마커 루트에 따로 생기는 바운즈 상자(CharacterHoverTarget)가 맡으며, 그 상자는
                // 렌더러 크기에서 나오므로 모델에 콜라이더가 없어도 정확하다.
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                // 루트 피벗을 바닥에 둔다 — 몬스터 배치는 타일 표면 좌표를 준다.
                body.transform.localPosition = new Vector3(0f, BodyScale.y * 2f * 0.5f, 0f);
                body.transform.localScale = BodyScale;

                var renderer = body.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                PrefabUtility.SaveAsPrefabAsset(root, ModelPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>살포 캐스트: 보스 발치에서 사방으로 퍼지는 붉은 링. "사방에 깔린다"의 예고다.</summary>
        private static void BuildCastVfx()
        {
            var root = new GameObject("BossProp_VolleyCast");
            try
            {
                var burst = AddParticleChild(root, "Ring", ScrapRed);
                var main = burst.main;
                main.duration = 0.8f;
                main.startLifetime = 0.7f;
                main.startSpeed = 7f;
                main.startSize = 0.35f;
                main.gravityModifier = 0f;

                var emission = burst.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });

                var shape = burst.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.4f;
                shape.radiusThickness = 0f;
                // 바닥을 따라 퍼지게 눕힌다(기본 Circle은 XY 평면이라 세로로 선다).
                burst.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                var sizeOverLifetime = burst.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

                var pillar = AddParticleChild(root, "Pillar", ScrapRed);
                var pillarMain = pillar.main;
                pillarMain.duration = 0.6f;
                pillarMain.startLifetime = 0.5f;
                pillarMain.startSpeed = 5f;
                pillarMain.startSize = 0.3f;
                var pillarEmission = pillar.emission;
                pillarEmission.rateOverTime = 0f;
                pillarEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 25) });
                var pillarShape = pillar.shape;
                pillarShape.enabled = true;
                pillarShape.shapeType = ParticleSystemShapeType.Cone;
                pillarShape.angle = 8f;
                pillarShape.radius = 0.2f;

                PrefabUtility.SaveAsPrefabAsset(root, CastVfxPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>기물 착지: 꽂히는 순간의 짧은 흙먼지 + 위로 튀는 파편.</summary>
        private static void BuildPlacedVfx()
        {
            var root = new GameObject("BossProp_Placed");
            try
            {
                var puff = AddParticleChild(root, "Impact", ScrapRed);
                var main = puff.main;
                main.duration = 0.5f;
                main.startLifetime = 0.4f;
                main.startSpeed = 3f;
                main.startSize = 0.22f;
                main.gravityModifier = 1.2f;

                var emission = puff.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

                var shape = puff.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 35f;
                shape.radius = 0.15f;

                PrefabUtility.SaveAsPrefabAsset(root, PlacedVfxPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 흡수 폭발: 기물 자리에서 터지는 구형 파열 + <b>반경 1 원판을 덮는 바닥 링·섬광</b>(2026-09-05 후속 #8 재저작 —
        /// 종전엔 중심 구형 파열뿐이라 「어디까지 아픈가」가 화면에 없었다). 보스로 빨려 드는 부분은 코드가 그린다.
        /// </summary>
        private static void BuildAbsorbVfx()
        {
            var root = new GameObject("BossProp_AbsorbBlast");
            try
            {
                // ① 바닥 링: 반경 1 원판의 바깥 테두리까지 퍼지고 거기서 꺼진다(속도×수명 = 원판 반경).
                var ring = AddParticleChild(root, "DiskRing", ScrapRed);
                var ringMain = ring.main;
                ringMain.duration = 0.6f;
                ringMain.startLifetime = 0.42f;
                ringMain.startSpeed = AbsorbDiskWorldRadius / 0.42f;
                ringMain.startSize = 0.32f;
                ringMain.gravityModifier = 0f;
                var ringEmission = ring.emission;
                ringEmission.rateOverTime = 0f;
                ringEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
                var ringShape = ring.shape;
                ringShape.enabled = true;
                ringShape.shapeType = ParticleSystemShapeType.Circle;
                ringShape.radius = 0.15f;
                ringShape.radiusThickness = 0f;
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ring.transform.localPosition = new Vector3(0f, -0.3f, 0f);
                var ringSize = ring.sizeOverLifetime;
                ringSize.enabled = true;
                ringSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

                // ② 바닥 섬광: 원판 크기의 납작한 원이 한 번 번쩍한다 — 반경이 한 프레임에 통째로 읽힌다.
                var flash = AddParticleChild(root, "DiskFlash", new Color(1f, 0.45f, 0.25f, 0.8f));
                var flashMain = flash.main;
                flashMain.duration = 0.3f;
                flashMain.startLifetime = 0.28f;
                flashMain.startSpeed = 0f;
                flashMain.startSize = AbsorbDiskWorldRadius * 2f;
                flashMain.gravityModifier = 0f;
                var flashEmission = flash.emission;
                flashEmission.rateOverTime = 0f;
                flashEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                var flashShape = flash.shape;
                flashShape.enabled = false;
                flash.transform.localPosition = new Vector3(0f, -0.3f, 0f);
                var flashRenderer = flash.GetComponent<ParticleSystemRenderer>();
                flashRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                var flashSize = flash.sizeOverLifetime;
                flashSize.enabled = true;
                flashSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1f));
                var flashColor = flash.colorOverLifetime;
                flashColor.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(ScrapRed, 1f) },
                    new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
                flashColor.color = gradient;

                var blast = AddParticleChild(root, "Blast", ScrapRed);
                var main = blast.main;
                main.duration = 0.7f;
                main.startLifetime = 0.55f;
                main.startSpeed = 6f;
                main.startSize = 0.3f;
                main.gravityModifier = 0.3f;

                var emission = blast.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 45) });

                var shape = blast.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.25f;

                var sizeOverLifetime = blast.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));

                PrefabUtility.SaveAsPrefabAsset(root, AbsorbVfxPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 사슬 선 프리팹(2026-09-05 후속 #7): LineRenderer 루트 하나. 점 위치는 런타임이 채운다
        /// (<c>MapCombatController.BossProps.cs</c>가 보스→철조각 셀 중심을 따라 점진적으로 긋는다). 아트 반입은
        /// 이 프리팹의 머티리얼(<see cref="ChainLineMaterialPath"/>) 교체 — 발주서 V-7.
        /// </summary>
        private static void BuildChainLinePrefab()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");
            var material = AssetDatabase.LoadAssetAtPath<Material>(ChainLineMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, ChainLineMaterialPath);
            }
            else if (shader != null)
            {
                material.shader = shader;
            }

            material.color = ChainYellow;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", ChainYellow);
            }

            // URP Particles/Unlit: 가법 블렌드(_Blend=2)로 전격처럼 빛나게. 없는 셰이더면 조용히 넘어간다.
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 2f);
            }

            EditorUtility.SetDirty(material);

            var root = new GameObject("BossScrapChain_Line");
            try
            {
                var line = root.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                line.startWidth = 0.13f;
                line.endWidth = 0.09f;
                line.positionCount = 0;
                line.startColor = ChainYellow;
                line.endColor = ChainYellow;
                line.sharedMaterial = material;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                PrefabUtility.SaveAsPrefabAsset(root, ChainLinePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>사슬 명중 스파크(후속 #7): 선이 플레이어 칸에 닿는 순간의 노란 전격 파열. 임시(기본 도형 파티클).</summary>
        private static void BuildChainHitVfx()
        {
            var root = new GameObject("BossScrapChain_Hit");
            try
            {
                var spark = AddParticleChild(root, "Spark", ChainYellow);
                var main = spark.main;
                main.duration = 0.45f;
                main.startLifetime = 0.32f;
                main.startSpeed = 4.5f;
                main.startSize = 0.16f;
                main.gravityModifier = 0.6f;
                var emission = spark.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 32) });
                var shape = spark.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.2f;
                var size = spark.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
                PrefabUtility.SaveAsPrefabAsset(root, ChainHitVfxPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 기본 도형 파티클 한 덩이. <c>playOnAwake=false</c>인 이유는
        /// <c>EffectPresentationController.PlayPrefabParticles</c>가 스폰 직후 직접 재생시키기 때문이다
        /// (수명 계산도 그쪽이 하므로 여기서 Destroy를 붙이면 안 된다).
        /// </summary>
        private static ParticleSystem AddParticleChild(GameObject root, string name, Color color)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            var particles = child.AddComponent<ParticleSystem>();

            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;

            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // 머티리얼이 비면 EffectPresentationController가 렌더러를 꺼 버린다
            // (DisableRenderersWithMissingMaterials) — 기본 파티클 머티리얼을 명시적으로 물린다.
            var particleMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            if (particleMaterial != null)
            {
                renderer.sharedMaterial = particleMaterial;
            }

            return particles;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }

            Directory.CreateDirectory(folder);
        }
    }
}
