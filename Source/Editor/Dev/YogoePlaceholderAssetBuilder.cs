using System.IO;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Dev
{
    /// <summary>
    /// 신규 요괴 6종(두두리·두억시니·어둑시니·거구귀·그슨새·야광귀)과 나무 말뚝의 <b>임시</b> 모델 생성기 — 아트 반입 전까지 쓰는 placeholder다
    /// (터렛 <see cref="AutoTurretPlaceholderAssetBuilder"/> · 철조각 선례). 아트 대기가 저작·검증을
    /// 막지 않게 하는 것이 유일한 목적이므로, 판정 대상은 "저 칸에 무엇이 있는가"뿐이다.
    ///
    /// <para>🔴 그래도 <b>실루엣은 다르게</b> 만든다(인계문 §5 — 터렛 레벨이 트림색으로만 갈려 판정이
    /// 밀린 선례). 거구귀는 벌어진 입, 그슨새는 삿갓, 야광귀는 작고 낮은 그림자다. 색만 다른 같은
    /// 상자로 두면 "어느 축이 실제로 도는가"를 실플레이로 볼 수 없다.</para>
    ///
    /// <para>머티리얼은 반드시 <c>.mat</c> 에셋으로 만든다 — 절차 생성 머티리얼은 프리팹 저장 시
    /// fileID:0으로 유실된다(2026-08-18 실증).</para>
    ///
    /// 멱등하다 — 다시 돌리면 같은 경로를 덮어쓴다.
    /// </summary>
    public static class YogoePlaceholderAssetBuilder
    {
        private const string Root = "Assets/Art/Characters/monster/Yogoe";
        private const string PrefabFolder = Root + "/Prefabs";
        private const string MaterialFolder = Root + "/Materials";

        /// <summary>M008 두두리 placeholder 경로(monster_catalog.csv visualPrefabPath와 같아야 한다).</summary>
        public const string DuduriPrefabPath = PrefabFolder + "/Duduri_Placeholder.prefab";

        /// <summary>M010 두억시니 placeholder 경로.</summary>
        public const string DuokseokiniPrefabPath = PrefabFolder + "/Duokseokini_Placeholder.prefab";

        /// <summary>M009 어둑시니 placeholder 경로.</summary>
        public const string EodukshiniPrefabPath = PrefabFolder + "/Eodukshini_Placeholder.prefab";

        /// <summary>M012 거구귀 placeholder 경로.</summary>
        public const string GeogugwiPrefabPath = PrefabFolder + "/Geogugwi_Placeholder.prefab";

        /// <summary>M013 그슨새 placeholder 경로.</summary>
        public const string GeuseunsaePrefabPath = PrefabFolder + "/Geuseunsae_Placeholder.prefab";

        /// <summary>M014 야광귀 placeholder 경로.</summary>
        public const string YagwanggwiPrefabPath = PrefabFolder + "/Yagwanggwi_Placeholder.prefab";

        /// <summary>M906 나무 말뚝(두두리 소환 기물) placeholder 경로.</summary>
        public const string WoodenStakePrefabPath = PrefabFolder + "/WoodenStake_Placeholder.prefab";

        // 헥스 타일 반경 1 · 프리미티브 한 변 1 → localScale이 곧 월드 크기. 모델 규격은 높이 1.0 발치 피벗.
        private static readonly Color DuduriSkin = new Color(0.78f, 0.30f, 0.24f);
        private static readonly Color DuduriClub = new Color(0.52f, 0.38f, 0.24f);
        private static readonly Color GeogugwiColor = new Color(0.30f, 0.20f, 0.26f);
        private static readonly Color GeogugwiMaw = new Color(0.86f, 0.24f, 0.20f);
        private static readonly Color GeuseunsaeColor = new Color(0.46f, 0.48f, 0.42f);
        private static readonly Color YagwanggwiColor = new Color(0.30f, 0.42f, 0.62f);
        private static readonly Color WoodenStakeColor = new Color(0.46f, 0.34f, 0.20f);
        private static readonly Color DuokseokiniColor = new Color(0.38f, 0.34f, 0.44f);
        private static readonly Color EodukshiniColor = new Color(0.16f, 0.15f, 0.22f);

        [MenuItem("Seoul Playup/Dev/Rebuild Yogoe Placeholder Assets")]
        public static void Rebuild()
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);

            BuildDuduri();
            BuildDuokseokini();
            BuildEodukshini();
            BuildGeogugwi();
            BuildGeuseunsae();
            BuildYagwanggwi();
            BuildWoodenStake();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Yogoe placeholder assets rebuilt into {PrefabFolder} (두두리 방망이 · 두억시니 낮고 넓음 · 어둑시니 뭉개진 덩어리 · 거구귀 벌어진 입 · 그슨새 삿갓 · 야광귀 낮은 그림자 · 나무 말뚝).");
        }

        /// <summary>
        /// M008 두두리 — 방망이를 든 덩치. 방망이가 실루엣의 식별자이고, 「두드리다」에서 온 목신이라
        /// 도깨비 시절의 실루엣이 원형에 오히려 더 맞는다(형상 무변경·이름만 승계).
        /// </summary>
        private static void BuildDuduri()
        {
            var skin = CreateOrUpdateMaterial("Yogoe_Duduri", DuduriSkin, emissionScale: 0.35f);
            var club = CreateOrUpdateMaterial("Yogoe_DuduriClub", DuduriClub, emissionScale: 0.15f);
            var root = new GameObject("Duduri_Placeholder");
            try
            {
                var body = AddPrimitive(root.transform, PrimitiveType.Capsule, "Body", skin);
                body.transform.localScale = new Vector3(0.55f, 0.42f, 0.55f);
                body.transform.localPosition = new Vector3(0f, 0.42f, 0f);

                var horn = AddPrimitive(root.transform, PrimitiveType.Cube, "Horn", skin);
                horn.transform.localScale = new Vector3(0.10f, 0.22f, 0.10f);
                horn.transform.localPosition = new Vector3(0.12f, 0.92f, 0f);

                // 방망이는 정면(+Z)을 향해 어깨 옆으로 뻗는다 — 위에서 봐도 한 칸 안에서 읽힌다.
                var weapon = AddPrimitive(root.transform, PrimitiveType.Cube, "Club", club);
                weapon.transform.localScale = new Vector3(0.14f, 0.14f, 0.62f);
                weapon.transform.localPosition = new Vector3(0.34f, 0.55f, 0.20f);
                weapon.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);

                PrefabUtility.SaveAsPrefabAsset(root, DuduriPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// M010 두억시니 — <b>낮고 넓은</b> 덩치. 이동 1의 시각적 근거가 실루엣이어야 한다(§5):
        /// 높이를 키우는 대신 폭을 키워, 멀리서도 "빠르지 않겠구나"로 읽히게 한다.
        /// </summary>
        private static void BuildDuokseokini()
        {
            var material = CreateOrUpdateMaterial("Yogoe_Duokseokini", DuokseokiniColor, emissionScale: 0.25f);
            var root = new GameObject("Duokseokini_Placeholder");
            try
            {
                var body = AddPrimitive(root.transform, PrimitiveType.Cube, "Body", material);
                body.transform.localScale = new Vector3(1.05f, 0.52f, 0.85f);
                body.transform.localPosition = new Vector3(0f, 0.26f, 0f);

                // 머리는 몸에 파묻혀 낮다 — 목이 보이면 곧바로 "사람 크기"로 읽힌다.
                var head = AddPrimitive(root.transform, PrimitiveType.Cube, "Head", material);
                head.transform.localScale = new Vector3(0.52f, 0.34f, 0.46f);
                head.transform.localPosition = new Vector3(0f, 0.66f, 0.18f);

                var leftFist = AddPrimitive(root.transform, PrimitiveType.Cube, "FistL", material);
                leftFist.transform.localScale = new Vector3(0.30f, 0.30f, 0.30f);
                leftFist.transform.localPosition = new Vector3(-0.58f, 0.18f, 0.28f);

                var rightFist = AddPrimitive(root.transform, PrimitiveType.Cube, "FistR", material);
                rightFist.transform.localScale = new Vector3(0.30f, 0.30f, 0.30f);
                rightFist.transform.localPosition = new Vector3(0.58f, 0.18f, 0.28f);

                PrefabUtility.SaveAsPrefabAsset(root, DuokseokiniPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// M009 어둑시니 — <b>형체가 뭉개진</b> 검은 덩어리. 은신이 정체성이라 실루엣도 윤곽이 흐릿해야
        /// 하는데, placeholder에서 흐림을 흉내 낼 수는 없으므로 <b>비대칭으로 겹친 덩어리</b>로 대신한다.
        /// 발광을 거의 주지 않아 밤 맵에서 가장 어둡게 읽히는 것이 이 놈의 표식이다.
        /// </summary>
        private static void BuildEodukshini()
        {
            var material = CreateOrUpdateMaterial("Yogoe_Eodukshini", EodukshiniColor, emissionScale: 0.08f);
            var root = new GameObject("Eodukshini_Placeholder");
            try
            {
                var core = AddPrimitive(root.transform, PrimitiveType.Sphere, "Core", material);
                core.transform.localScale = new Vector3(0.62f, 0.78f, 0.58f);
                core.transform.localPosition = new Vector3(0f, 0.42f, 0f);

                var lump = AddPrimitive(root.transform, PrimitiveType.Sphere, "Lump", material);
                lump.transform.localScale = new Vector3(0.44f, 0.34f, 0.40f);
                lump.transform.localPosition = new Vector3(0.18f, 0.78f, -0.06f);

                var skirt = AddPrimitive(root.transform, PrimitiveType.Sphere, "Skirt", material);
                skirt.transform.localScale = new Vector3(0.80f, 0.22f, 0.70f);
                skirt.transform.localPosition = new Vector3(-0.04f, 0.11f, 0.04f);

                PrefabUtility.SaveAsPrefabAsset(root, EodukshiniPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// M012 거구귀 — <b>벌어진 입</b>이 전부다. 윗입술은 하늘에 아랫입술은 땅에 닿는다는 설화를
        /// 한 칸 안에서 읽히게 하려면 몸을 키우는 것이 아니라 <b>위아래로 벌어진 두 판</b>을 세워야 한다.
        /// 안쪽에 붉은 목구멍을 넣어 정면에서 「입」으로 읽히게 했다.
        /// </summary>
        private static void BuildGeogugwi()
        {
            var body = CreateOrUpdateMaterial("Yogoe_Geogugwi", GeogugwiColor, emissionScale: 0.18f);
            var maw = CreateOrUpdateMaterial("Yogoe_GeogugwiMaw", GeogugwiMaw, emissionScale: 0.9f);
            var root = new GameObject("Geogugwi_Placeholder");
            try
            {
                var lower = AddPrimitive(root.transform, PrimitiveType.Cube, "JawLower", body);
                lower.transform.localScale = new Vector3(0.92f, 0.26f, 0.72f);
                lower.transform.localPosition = new Vector3(0f, 0.13f, 0.08f);
                lower.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);

                var upper = AddPrimitive(root.transform, PrimitiveType.Cube, "JawUpper", body);
                upper.transform.localScale = new Vector3(0.92f, 0.26f, 0.72f);
                upper.transform.localPosition = new Vector3(0f, 0.86f, 0.02f);
                upper.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);

                // 목구멍 — 두 판 사이가 비면 「입」이 아니라 「부서진 상자」로 읽힌다.
                var throat = AddPrimitive(root.transform, PrimitiveType.Sphere, "Throat", maw);
                throat.transform.localScale = new Vector3(0.54f, 0.42f, 0.40f);
                throat.transform.localPosition = new Vector3(0f, 0.50f, -0.18f);

                PrefabUtility.SaveAsPrefabAsset(root, GeogugwiPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// M013 그슨새 — <b>삿갓(주젱이)</b>이 식별자다. 몸은 가늘게 두고 넓은 원뿔 갓을 얹어,
        /// 위에서 내려다보는 실게임 카메라에서 <b>갓의 원</b>만 보이게 한다 — 「얼굴이 안 보이는 것」이
        /// 이 요괴의 인상이다.
        /// </summary>
        private static void BuildGeuseunsae()
        {
            var material = CreateOrUpdateMaterial("Yogoe_Geuseunsae", GeuseunsaeColor, emissionScale: 0.2f);
            var root = new GameObject("Geuseunsae_Placeholder");
            try
            {
                var body = AddPrimitive(root.transform, PrimitiveType.Capsule, "Body", material);
                body.transform.localScale = new Vector3(0.26f, 0.40f, 0.26f);
                body.transform.localPosition = new Vector3(0f, 0.40f, 0f);

                // 갓 — 원뿔 프리미티브가 없으므로 납작한 실린더 둘을 겹쳐 기울기를 만든다.
                var brim = AddPrimitive(root.transform, PrimitiveType.Cylinder, "Brim", material);
                brim.transform.localScale = new Vector3(0.92f, 0.04f, 0.92f);
                brim.transform.localPosition = new Vector3(0f, 0.80f, 0f);

                var crown = AddPrimitive(root.transform, PrimitiveType.Cylinder, "Crown", material);
                crown.transform.localScale = new Vector3(0.46f, 0.10f, 0.46f);
                crown.transform.localPosition = new Vector3(0f, 0.88f, 0f);

                PrefabUtility.SaveAsPrefabAsset(root, GeuseunsaePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// M014 야광귀 — <b>작고 낮다</b>. 이동 3·저체력이라 "빠르고 약한 것"으로 읽혀야 하므로 부피를
        /// 최소로 두고, 훔친 신발 한 짝을 옆구리에 달아 절도 축을 실루엣에 남긴다.
        /// </summary>
        private static void BuildYagwanggwi()
        {
            var material = CreateOrUpdateMaterial("Yogoe_Yagwanggwi", YagwanggwiColor, emissionScale: 0.55f);
            var root = new GameObject("Yagwanggwi_Placeholder");
            try
            {
                var body = AddPrimitive(root.transform, PrimitiveType.Capsule, "Body", material);
                body.transform.localScale = new Vector3(0.26f, 0.28f, 0.26f);
                body.transform.localPosition = new Vector3(0f, 0.30f, 0f);

                var head = AddPrimitive(root.transform, PrimitiveType.Sphere, "Head", material);
                head.transform.localScale = new Vector3(0.28f, 0.24f, 0.28f);
                head.transform.localPosition = new Vector3(0f, 0.62f, 0.04f);

                // 훔친 신발 — 한 짝만 들고 있어야 "가져가는 중"으로 읽힌다.
                var shoe = AddPrimitive(root.transform, PrimitiveType.Cube, "StolenShoe", material);
                shoe.transform.localScale = new Vector3(0.14f, 0.10f, 0.26f);
                shoe.transform.localPosition = new Vector3(0.26f, 0.24f, 0.10f);
                shoe.transform.localRotation = Quaternion.Euler(0f, 18f, 0f);

                PrefabUtility.SaveAsPrefabAsset(root, YagwanggwiPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// M906 나무 말뚝 — 두두리가 세우는 기물. <b>사람 형상이 아니어야</b> 한다: 몬스터로 세지 않는
        /// 물건이므로 실루엣이 요괴처럼 읽히면 플레이어가 "저것도 잡아야 하나"로 오해한다.
        /// </summary>
        private static void BuildWoodenStake()
        {
            var material = CreateOrUpdateMaterial("Yogoe_WoodenStake", WoodenStakeColor, emissionScale: 0.1f);
            var root = new GameObject("WoodenStake_Placeholder");
            try
            {
                var post = AddPrimitive(root.transform, PrimitiveType.Cylinder, "Post", material);
                post.transform.localScale = new Vector3(0.22f, 0.42f, 0.22f);
                post.transform.localPosition = new Vector3(0f, 0.42f, 0f);

                // 깎인 머리 — 위가 뾰족해야 「말뚝」이지 「기둥」이 아니다.
                var tip = AddPrimitive(root.transform, PrimitiveType.Cube, "Tip", material);
                tip.transform.localScale = new Vector3(0.18f, 0.18f, 0.18f);
                tip.transform.localPosition = new Vector3(0f, 0.90f, 0f);
                tip.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);

                PrefabUtility.SaveAsPrefabAsset(root, WoodenStakePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject AddPrimitive(Transform parent, PrimitiveType type, string name, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            // 모델 콜라이더는 카드 타깃팅·클릭 히트테스트를 가로챈다(터렛 placeholder와 같은 계약).
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static Material CreateOrUpdateMaterial(string name, Color color, float emissionScale)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null)
            {
                material.shader = shader;
            }

            material.color = color;
            // URP Lit은 _BaseColor를 본다 — _Color만 세우면 화면에서는 흰색이다(터렛 선례).
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            // 밤 맵이라 발광이 없으면 색 구분 자체가 죽는다.
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emissionScale);
            }

            EditorUtility.SetDirty(material);
            return material;
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
