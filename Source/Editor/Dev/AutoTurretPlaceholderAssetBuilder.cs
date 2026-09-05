using System.IO;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Dev
{
    /// <summary>
    /// 자동 공격 터렛(M902/M903/M904)의 <b>임시</b> 모델 생성기 — 아트 반입 전까지 쓰는 placeholder다.
    /// 레벨 구분은 <b>색 하나로만</b> 한다(사용자 확정): LV1 초록 · LV2 노랑 · LV3 빨강.
    ///
    /// 형태를 레벨마다 바꾸지 않는 이유: 지금 판정해야 하는 것은 "저 칸에 터렛이 있고 몇 레벨인가"뿐이고,
    /// 실루엣까지 손대면 임시 에셋이 룩 판정을 오염시킨다. 크기도 세 레벨이 같다.
    ///
    /// 멱등하다 — 다시 돌리면 같은 경로를 덮어쓴다.
    /// </summary>
    public static class AutoTurretPlaceholderAssetBuilder
    {
        private const string Root = "Assets/Art/Characters/monster/AutoTurret";
        private const string PrefabFolder = Root + "/Prefabs";
        private const string MaterialFolder = Root + "/Materials";

        // 헥스 타일 반경이 1이라는 전제(AtlasTilePresentationView.tileRadius). Unity Cube 프리미티브는
        // 한 변이 1이라 localScale이 곧 월드 크기다. 한 칸(내접 폭 ~1.73) 안에 넉넉히 들어간다.
        private static readonly Vector3 BodyScale = new Vector3(0.75f, 0.75f, 0.75f);

        private struct Level
        {
            public string Id;
            public string Suffix;
            public Color Color;
        }

        private static readonly Level[] Levels =
        {
            new Level { Id = "M902", Suffix = "LV1", Color = new Color(0.25f, 0.80f, 0.35f) },
            new Level { Id = "M903", Suffix = "LV2", Color = new Color(0.95f, 0.82f, 0.20f) },
            new Level { Id = "M904", Suffix = "LV3", Color = new Color(0.90f, 0.18f, 0.15f) },
        };

        [MenuItem("Seoul Playup/Dev/Rebuild Auto Turret Placeholder Assets")]
        public static void Rebuild()
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);

            foreach (var level in Levels)
            {
                var material = CreateOrUpdateMaterial(level);
                BuildPrefab(level, material);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Auto turret placeholder assets rebuilt into {PrefabFolder} (LV1 초록 · LV2 노랑 · LV3 빨강).");
        }

        private static Material CreateOrUpdateMaterial(Level level)
        {
            var path = $"{MaterialFolder}/AutoTurret_{level.Suffix}.mat";
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

            material.color = level.Color;
            // URP Lit은 _BaseColor를 본다 — _Color만 세우면 인스펙터에서만 색이 맞고 화면에선 흰색이다.
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", level.Color);
            }

            // 밤 맵에서 색으로만 레벨을 가르므로 살짝 발광시켜 명도차가 죽지 않게 한다.
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", level.Color * 0.55f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildPrefab(Level level, Material material)
        {
            var root = new GameObject($"AutoTurret_{level.Suffix}");
            try
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                // 모델 콜라이더는 카드 타깃팅·클릭 히트테스트를 가로챌 수 있어 지운다. 마우스 호버
                // 판정은 마커 루트에 따로 생기는 바운즈 상자(CharacterHoverTarget)가 맡으며, 그 상자는
                // 렌더러 크기에서 나오므로 모델에 콜라이더가 없어도 정확하다.
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                // 루트 피벗을 바닥에 둔다 — 몬스터 배치는 타일 표면 좌표를 준다.
                body.transform.localPosition = new Vector3(0f, BodyScale.y * 0.5f, 0f);
                body.transform.localScale = BodyScale;

                var renderer = body.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/AutoTurret_{level.Suffix}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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
