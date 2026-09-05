using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.Codex.Editor
{
    /// <summary>
    /// 프리팹을 격리 렌더해 도감 썸네일 PNG로 굽는다(<c>docs/codex-plan.md</c> P5·§13.9, P6에서 확장).
    /// <para>
    /// 🔑 <b>도메인을 모른다.</b> 입력은 <see cref="BakeSubject"/>(id + 프리팹 경로)뿐이고, 어느
    /// 카탈로그에서 그 둘을 뽑아 왔는지는 <c>Collect…Subjects</c>가 알아서 한다 — 촬영장·화각·2패스
    /// 합성은 몬스터든 맵 오브젝트든 완전히 같기 때문이다. P6에서 오브젝트를 붙일 때 아래의
    /// 함정 다섯을 다시 밟지 않으려면 <b>새로 짜지 말고 이 대상 목록만 늘릴 것</b>.
    /// </para>
    /// <para>
    /// ⚠️ 오브젝트 프리팹은 몬스터와 크기가 다르다 — 건물 하나가 60,000 삼각형에 수십 미터다.
    /// 화각 자동 맞춤(<see cref="TryRefit"/>)은 크기에 무관하게 돌지만, 장식 90종까지 구울 이유는
    /// 없으므로 <c>map_objects.csv</c>의 <c>visibleInCatalog</c>가 대상을 거른다.
    /// </para>
    /// <para>
    /// 🔴🔴 <b>조명을 손으로 세우지 않는다.</b> 첫 판은 임의의 키+필 등으로 구웠고 사용자 판정이
    /// "색감이 쨍하다 · 인게임과 다른 느낌"이었다. 원인은 취향이 아니라 <b>다른 조명</b>이었다 —
    /// 손으로 세운 등은 따뜻하고(1.15) 앰비언트가 평평한 회색(0.28)인데, 출하 야경은
    /// 차가운 달빛(0.65)에 <b>인디고 3색 앰비언트</b>(하늘 0.055)이고 그 위에 <b>ACES 톤매핑 +
    /// 노출 +0.6 + 채도 +5</b>가 걸린다. 톤매핑이 없으면 하이라이트가 롤오프되지 않아 원색이 튄다.
    /// </para>
    /// <para>
    /// ⇒ 그래서 이 도구는 <b>게임과 같은 것을 쓴다</b>: <see cref="EnvironmentLookPreset"/>(SeoulNight)을
    /// 실제 URP 카메라가 있는 임시 씬에 <see cref="LookPresetApplier"/>로 적용하고 찍는다.
    /// 값을 베끼지 않았으므로 아트 디렉션이 바뀌면 다시 굽는 것만으로 따라온다.
    /// </para>
    /// <para>
    /// 🔴 <b>알파가 살아 있어야 한다.</b> 잠긴 칸의 실루엣은 스프라이트의 알파를 쓰므로 배경이
    /// 불투명하면 실루엣이 <b>네모 판</b>이 된다. 그런데 URP 후처리는 알파를 보존하지 않는다 ⇒
    /// <b>두 번 찍는다</b>: 색은 후처리를 켜고 칸 바닥색 위에, 알파는 후처리를 끄고 투명 위에.
    /// </para>
    /// <para>
    /// 🔑 파일 이름은 <b>몬스터 id</b>로 짓는다(프리팹 이름이 아니다). M901·M905가 프리팹 하나를
    /// 공유하므로(인계문 §2.3) 프리팹으로 이름을 지으면 두 항목이 한 파일을 두고 다툰다.
    /// </para>
    /// </summary>
    public static class CodexThumbnailBaker
    {
        private const string CatalogSourceAssetPath = "Assets/Data/Combat/Catalogs/CombatCatalogTextAssetSource.asset";
        private const string LookPresetPath = "Assets/Data/Lighting/LookPresets/SeoulNight.asset";
        private const string ReportPath = "Temp/codex-thumbnail-bake-report.json";

        /// <summary>
        /// 도감 칸의 썸네일 자리는 160×118이다(<c>CodexOverlayView</c>). 그 비율 그대로 3배로 굽는다 —
        /// 비율이 어긋나면 <c>preserveAspect</c>가 레터박스를 넣어 그림이 작아 보인다.
        /// </summary>
        private const int Width = 480;

        private const int Height = 354;

        /// <summary>
        /// 썸네일이 앉는 칸 바닥색(<c>CodexOverlayView.Sunken</c>). 후처리 패스의 배경으로 쓴다 —
        /// 가장자리 반투명 픽셀이 <b>실제로 깔릴 색</b> 위에서 합성되어야 테두리에 흰 실이 안 생긴다.
        /// </summary>
        private static readonly Color PlateColor = new Color(0.043f, 0.051f, 0.118f, 1f);

        /// <summary>실루엣으로 눌러도 읽히도록 3/4 앞각에서, 살짝 위에서 본다(Q38 확정).</summary>
        private static readonly Quaternion ViewRotation = Quaternion.Euler(18f, 215f, 0f);

        /// <summary>피사체가 칸에 꽉 차되 윤곽이 잘리지 않을 만큼의 여백.</summary>
        private const float FitMargin = 1.12f;

        /// <summary>1차(정찰) 렌더를 얼마나 헐렁하게 잡는가 — 잘리기만 하지 않으면 된다.</summary>
        private const float LooseFitScale = 1.6f;

        /// <summary>
        /// 출하 야경 그대로 굽는다(<b>0 스톱</b>). 노출을 얹지 않는 것이 기본값인 이유는,
        /// 등·톤매핑·채도를 게임에서 가져다 쓰는 마당에 밝기만 손으로 올리면 다시
        /// "인게임과 다른 느낌"이 되기 때문이다.
        /// <para>
        /// 그림이 어두워 보이면 <b>여기를 올리기 전에</b> 그것이 실제로 그 몬스터의 모습인지
        /// 의심할 것 — 삼목구는 검은 개라서 도감에서도 검은 것이 맞다.
        /// </para>
        /// </summary>
        private const float DefaultExtraExposureStops = 0f;

        /// <summary>
        /// 굽기 하나의 입력. 🔑<b>도메인을 모른다</b> — id와 프리팹 경로만 있으면 된다.
        /// 그 아래(촬영장·화각·2패스 합성)는 몬스터든 오브젝트든 완전히 같기 때문이다(P6).
        /// </summary>
        public readonly struct BakeSubject
        {
            public BakeSubject(string id, string displayName, string prefabPath)
            {
                Id = id;
                DisplayName = displayName;
                PrefabPath = prefabPath;
            }

            /// <summary>썸네일 파일 이름의 바탕(<c>codex_thumb_{Id}</c>). 프리팹 이름이 아니다.</summary>
            public string Id { get; }

            public string DisplayName { get; }

            public string PrefabPath { get; }
        }

        [MenuItem("Tools/Codex/Bake Monster Thumbnails")]
        public static void BakeMonsters() =>
            Bake(CollectMonsterSubjects(), "몬스터", DefaultExtraExposureStops, CodexThumbnailCatalog.ThumbnailFolder, true);

        [MenuItem("Tools/Codex/Bake Map Object Thumbnails")]
        public static void BakeMapObjects() =>
            Bake(CollectMapObjectSubjects(), "오브젝트", DefaultExtraExposureStops, CodexThumbnailCatalog.ThumbnailFolder, true);

        /// <summary>둘 다 굽고 카탈로그를 한 번만 다시 세운다.</summary>
        [MenuItem("Tools/Codex/Bake All Thumbnails")]
        public static void BakeAll()
        {
            var subjects = new List<BakeSubject>();
            subjects.AddRange(CollectMonsterSubjects());
            subjects.AddRange(CollectMapObjectSubjects());
            Bake(subjects, "전체", DefaultExtraExposureStops, CodexThumbnailCatalog.ThumbnailFolder, true);
        }

        /// <summary>노출 스톱을 견주어 보기 위한 갈래 굽기 — 카탈로그를 건드리지 않는다.</summary>
        public static void BakeVariant(float extraExposureStops, string outputFolder) =>
            Bake(CollectMonsterSubjects(), "몬스터", extraExposureStops, outputFolder, false);

        /// <summary>
        /// 몬스터 카탈로그 → 굽기 대상. 프리팹 경로는 <c>visualPrefabPath</c>가 그대로 준다.
        /// </summary>
        private static IReadOnlyList<BakeSubject> CollectMonsterSubjects()
        {
            var source = AssetDatabase.LoadAssetAtPath<CombatCatalogTextAssetSource>(CatalogSourceAssetPath);
            if (source == null || !source.HasMonsterCatalog)
            {
                Debug.LogError($"[CodexBake] 몬스터 카탈로그 소스를 읽지 못했다: {CatalogSourceAssetPath}");
                return Array.Empty<BakeSubject>();
            }

            // MonsterCatalogEntry는 구조체다 — null 거르기가 성립하지 않는다.
            return source.CreateMonsterCatalog()?.Entries
                       ?.Select(entry => new BakeSubject(entry.Id, entry.DisplayName, entry.VisualPrefabPath))
                       .ToArray()
                   ?? Array.Empty<BakeSubject>();
        }

        /// <summary>
        /// 오브젝트 도감 저작(<c>map_objects.csv</c>) → 굽기 대상(P6).
        /// <para>
        /// 🔑 <b>도감에 실리는 것만 굽는다.</b> 카탈로그에는 98종이 있지만 90종은 순수 장식이고,
        /// 건물 하나가 60,000 삼각형이라 전부 구울 이유가 없다 — 거르는 축은
        /// <c>visibleInCatalog</c>다.
        /// </para>
        /// <para>
        /// 🔑 파일 이름은 <b>CSV의 안정 id</b>로 짓는다(<c>catalogRef</c>가 아니다). 임시 모델
        /// (<c>treasureChest_tmp</c>)이 정식 모델로 바뀌어도 도감이 같은 파일을 계속 찾게 하기
        /// 위해서다 — P5가 몬스터를 프리팹이 아니라 id로 키잉한 것과 같은 이유.
        /// </para>
        /// </summary>
        private static IReadOnlyList<BakeSubject> CollectMapObjectSubjects()
        {
            var catalogSet = AssetDatabase.LoadAssetAtPath<MapObjectCatalogSet>(
                MapObjectCatalogSet.DefaultCatalogAssetPath);
            if (catalogSet == null)
            {
                Debug.LogError(
                    $"[CodexBake] 맵 오브젝트 카탈로그를 읽지 못했다: {MapObjectCatalogSet.DefaultCatalogAssetPath}");
                return Array.Empty<BakeSubject>();
            }

            CodexObjectCatalog authored;
            try
            {
                authored = CodexObjectCatalogCsv.ConvertFile(CombatCsvPaths.MapObjectsCsv);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[CodexBake] {CombatCsvPaths.MapObjectsCsv}를 읽지 못했다: {exception.Message}");
                return Array.Empty<BakeSubject>();
            }

            var subjects = new List<BakeSubject>();
            foreach (var entry in authored.Entries)
            {
                if (!entry.VisibleInCatalog)
                {
                    continue;
                }

                if (!catalogSet.TryGetEntry(entry.CatalogRef, out var catalogEntry) || catalogEntry.Prefab == null)
                {
                    Debug.LogWarning(
                        $"[CodexBake] '{entry.Id}'의 catalogRef '{entry.CatalogRef}'에 프리팹이 없다 — 건너뛴다.");
                    continue;
                }

                subjects.Add(new BakeSubject(
                    entry.ResolveThumbnailKey(),
                    entry.DisplayName,
                    AssetDatabase.GetAssetPath(catalogEntry.Prefab)));
            }

            return subjects;
        }

        private static void Bake(
            IReadOnlyList<BakeSubject> subjects,
            string label,
            float extraExposureStops,
            string outputFolder,
            bool updateCatalog)
        {
            var preset = AssetDatabase.LoadAssetAtPath<EnvironmentLookPreset>(LookPresetPath);
            if (preset == null)
            {
                Debug.LogError($"[CodexBake] 야경 프리셋을 못 찾았다: {LookPresetPath}");
                return;
            }

            var entries = subjects ?? Array.Empty<BakeSubject>();
            if (entries.Count == 0)
            {
                Debug.LogError($"[CodexBake] 구울 {label} 대상이 없다.");
                return;
            }

            // 🔴 다른 씬이 열려 있으면 그 씬의 등·볼륨이 피사체에 섞인다 — 임시 씬을 Single로 연다.
            //    ⚠️ 저장 확인 모달은 MCP를 멈추므로 띄우지 않고 직접 저장한다(씬 저장은 상시 승인).
            var restorePaths = CaptureOpenScenePaths();
            EditorSceneManager.SaveOpenScenes();

            EnsureFolder(outputFolder);

            var results = new List<BakeResult>(entries.Count);
            BakeRig rig = null;
            try
            {
                rig = new BakeRig(LookPresetPath, extraExposureStops);
                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];
                    EditorUtility.DisplayProgressBar(
                        $"도감 썸네일 굽기 · {label}", $"{entry.Id} · {entry.DisplayName}", (float)index / entries.Count);
                    results.Add(BakeOne(rig, entry, outputFolder));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                rig?.Dispose();
                RestoreScenes(restorePaths);
            }

            AssetDatabase.Refresh();
            ApplyImportSettings(results);
            if (updateCatalog)
            {
                BakeCatalog(results);
            }

            WriteReport(results, extraExposureStops);

            var baked = results.Count(result => result.Baked);
            Debug.Log($"[CodexBake] {label} 썸네일 {baked}/{results.Count}장을 구웠다. 판정 리포트: {ReportPath}");
        }

        private static BakeResult BakeOne(BakeRig rig, BakeSubject entry, string outputFolder)
        {
            var result = new BakeResult
            {
                Id = entry.Id,
                DisplayName = entry.DisplayName,
                PrefabPath = entry.PrefabPath,
            };

            if (string.IsNullOrWhiteSpace(entry.PrefabPath))
            {
                result.Skip = "프리팹 경로 없음";
                return result;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
            if (prefab == null)
            {
                result.Skip = "프리팹을 못 찾음";
                return result;
            }

            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab, rig.SubjectAnchor);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                if (!TryFrame(instance, out var pivot, out var orthoSize, out var reason))
                {
                    result.Skip = reason;
                    return result;
                }

                // 1차 — 넉넉하게, 후처리 없이(알파만 보면 되므로 싸다). 이 장은 버린다.
                rig.Place(pivot, orthoSize * LooseFitScale, ViewRotation);
                var scout = rig.Render(withPostProcessing: false, background: Color.clear);
                try
                {
                    if (!TryRefit(scout, rig.Camera.orthographicSize, ref pivot, ref orthoSize))
                    {
                        result.Skip = "그려진 픽셀이 없음";
                        return result;
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(scout);
                }

                // 2차 — 실제로 그려진 픽셀에 맞춰 색(후처리 O)과 알파(후처리 X)를 따로 찍는다.
                rig.Place(pivot, orthoSize, ViewRotation);
                var color = rig.Render(withPostProcessing: true, background: PlateColor);
                var alpha = rig.Render(withPostProcessing: false, background: Color.clear);
                try
                {
                    var composed = Compose(color, alpha);
                    try
                    {
                        result.OpaquePixelRatio = MeasureOpaqueRatio(composed);
                        result.HasTransparentCorners = HasTransparentCorners(composed);
                        result.MeanChroma = MeasureMeanChroma(composed);

                        var path = Path.Combine(
                            outputFolder,
                            CodexThumbnailCatalog.KeyFor(entry.Id) + ".png");
                        File.WriteAllBytes(path, composed.EncodeToPNG());
                        result.AssetPath = path.Replace('\\', '/');
                        result.Baked = true;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(composed);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(color);
                    UnityEngine.Object.DestroyImmediate(alpha);
                }
            }
            catch (Exception exception)
            {
                result.Skip = exception.Message;
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            return result;
        }

        /// <summary>색은 후처리 패스에서, 알파는 기하 패스에서 — 둘을 합쳐 한 장으로 만든다.</summary>
        private static Texture2D Compose(Texture2D color, Texture2D alpha)
        {
            var rgb = color.GetPixels32();
            var mask = alpha.GetPixels32();
            for (var index = 0; index < rgb.Length; index++)
            {
                rgb[index].a = mask[index].a;
            }

            var composed = new Texture2D(Width, Height, TextureFormat.RGBA32, mipChain: false);
            composed.SetPixels32(rgb);
            composed.Apply();
            return composed;
        }

        /// <summary>
        /// 렌더러 경계로 <b>대략</b> 잡는다. 회전한 AABB의 대각선은 실제 실루엣보다 훨씬 커서
        /// (첫 굽기 실측: 피사체가 칸의 12~23%밖에 안 찼다) 여기 값은
        /// <see cref="TryRefit"/>가 실제 알파로 다시 잡는 <b>출발점</b>일 뿐이다.
        /// </summary>
        private static bool TryFrame(GameObject instance, out Vector3 pivot, out float orthoSize, out string reason)
        {
            reason = null;
            pivot = Vector3.zero;
            orthoSize = 1f;

            var renderers = instance.GetComponentsInChildren<Renderer>()
                .Where(renderer => renderer.enabled && !(renderer is ParticleSystemRenderer))
                .ToArray();
            if (renderers.Length == 0)
            {
                reason = "렌더러가 없음";
                return false;
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            if (bounds.size.sqrMagnitude <= Mathf.Epsilon)
            {
                reason = "경계가 비어 있음";
                return false;
            }

            pivot = bounds.center;
            orthoSize = Mathf.Max(bounds.extents.magnitude, 0.01f);
            return true;
        }

        /// <summary>
        /// 1차 렌더의 <b>알파 경계</b>로 화각을 다시 잡는다. 경계 상자가 아니라 실제로 그려진 픽셀을
        /// 재므로, 보이지 않는 렌더러(그림자 판·체력바 판 등)가 경계를 부풀려도 화각이 안 흔들린다.
        /// </summary>
        private static bool TryRefit(Texture2D scout, float scoutOrthoSize, ref Vector3 pivot, ref float orthoSize)
        {
            var pixels = scout.GetPixels32();
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    if (pixels[y * Width + x].a <= 8)
                    {
                        continue;
                    }

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (minX > maxX)
            {
                return false;
            }

            // 1차의 화면 절반 높이가 orthographicSize였으므로 픽셀 하나가 곧 이만큼의 월드 길이다.
            var worldPerPixel = scoutOrthoSize * 2f / Height;

            var centerX = (minX + maxX + 1) * 0.5f;
            var centerY = (minY + maxY + 1) * 0.5f;
            pivot += ViewRotation * Vector3.right * ((centerX - Width * 0.5f) * worldPerPixel)
                   + ViewRotation * Vector3.up * ((centerY - Height * 0.5f) * worldPerPixel);

            var halfHeight = (maxY - minY + 1) * 0.5f * worldPerPixel;
            var halfWidth = (maxX - minX + 1) * 0.5f * worldPerPixel;
            var aspect = (float)Width / Height;
            orthoSize = Mathf.Max(halfHeight, halfWidth / aspect) * FitMargin;
            return orthoSize > 0f;
        }

        /// <summary>불투명 픽셀 비율 — 0에 가까우면 화각이 빗나가 빈 그림을 구운 것이다.</summary>
        private static float MeasureOpaqueRatio(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var opaque = pixels.Count(pixel => pixel.a > 24);
            return pixels.Length == 0 ? 0f : (float)opaque / pixels.Length;
        }

        /// <summary>
        /// 피사체 픽셀의 평균 채도(max−min). "쨍하다"의 <b>수치</b>다 — 손으로 세운 조명과
        /// 출하 야경을 말이 아니라 숫자로 견줄 수 있어야 판정이 취향 싸움이 되지 않는다.
        /// </summary>
        private static float MeasureMeanChroma(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            double sum = 0;
            var count = 0;
            foreach (var pixel in pixels)
            {
                if (pixel.a <= 200)
                {
                    continue;
                }

                var max = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                var min = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));
                sum += (max - min) / 255.0;
                count++;
            }

            return count == 0 ? 0f : (float)(sum / count);
        }

        /// <summary>네 귀퉁이가 투명한가 — 알파가 살아 있는지 보는 가장 싼 검산.</summary>
        private static bool HasTransparentCorners(Texture2D texture)
        {
            var w = texture.width - 1;
            var h = texture.height - 1;
            return texture.GetPixel(0, 0).a < 0.05f
                && texture.GetPixel(w, 0).a < 0.05f
                && texture.GetPixel(0, h).a < 0.05f
                && texture.GetPixel(w, h).a < 0.05f;
        }

        private static void ApplyImportSettings(IEnumerable<BakeResult> results)
        {
            foreach (var result in results.Where(result => result.Baked))
            {
                var importer = AssetImporter.GetAtPath(result.AssetPath) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }
        }

        private static void BakeCatalog(IEnumerable<BakeResult> results)
        {
            var sprites = AssetDatabase.FindAssets("t:Sprite", new[] { CodexThumbnailCatalog.ThumbnailFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
                .Where(sprite => sprite != null)
                .ToArray();

            EnsureFolder(Path.GetDirectoryName(CodexThumbnailCatalog.DefaultCatalogAssetPath).Replace('\\', '/'));

            var catalog = AssetDatabase.LoadAssetAtPath<CodexThumbnailCatalog>(
                CodexThumbnailCatalog.DefaultCatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CodexThumbnailCatalog>();
                AssetDatabase.CreateAsset(catalog, CodexThumbnailCatalog.DefaultCatalogAssetPath);
            }

            catalog.ConfigureForTests(sprites);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        private static string[] CaptureOpenScenePaths()
        {
            var paths = new List<string>();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!string.IsNullOrEmpty(scene.path))
                {
                    paths.Add(scene.path);
                }
            }

            return paths.ToArray();
        }

        private static void RestoreScenes(string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return;
            }

            EditorSceneManager.OpenScene(paths[0], OpenSceneMode.Single);
            for (var index = 1; index < paths.Length; index++)
            {
                EditorSceneManager.OpenScene(paths[index], OpenSceneMode.Additive);
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        /// <summary>
        /// 에이전트가 화면을 안 보고도 "굽기가 성공했는가"를 물을 수 있게 남기는 표
        /// (선례 <c>VfxLabCapture</c>의 <c>report.json</c>).
        /// </summary>
        private static void WriteReport(IReadOnlyList<BakeResult> results, float extraExposureStops)
        {
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine($"  \"width\": {Width}, \"height\": {Height},");
            builder.AppendLine($"  \"lookPreset\": \"{LookPresetPath}\",");
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"extraExposureStops\": {0:0.00},", extraExposureStops));
            builder.AppendLine($"  \"baked\": {results.Count(result => result.Baked)}, \"total\": {results.Count},");
            builder.AppendLine("  \"entries\": [");
            for (var index = 0; index < results.Count; index++)
            {
                var result = results[index];
                builder.Append("    {");
                builder.Append($"\"id\": \"{Escape(result.Id)}\", ");
                builder.Append($"\"name\": \"{Escape(result.DisplayName)}\", ");
                builder.Append($"\"prefab\": \"{Escape(result.PrefabPath)}\", ");
                builder.Append($"\"baked\": {(result.Baked ? "true" : "false")}, ");
                builder.Append($"\"asset\": \"{Escape(result.AssetPath)}\", ");
                builder.Append(string.Format(
                    CultureInfo.InvariantCulture, "\"opaqueRatio\": {0:0.0000}, ", result.OpaquePixelRatio));
                builder.Append(string.Format(
                    CultureInfo.InvariantCulture, "\"meanChroma\": {0:0.0000}, ", result.MeanChroma));
                builder.Append($"\"transparentCorners\": {(result.HasTransparentCorners ? "true" : "false")}, ");
                builder.Append($"\"skip\": \"{Escape(result.Skip)}\"");
                builder.AppendLine(index == results.Count - 1 ? "}" : "},");
            }

            builder.AppendLine("  ]");
            builder.AppendLine("}");

            Directory.CreateDirectory("Temp");
            File.WriteAllText(ReportPath, builder.ToString(), Encoding.UTF8);
        }

        private static string Escape(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "/").Replace("\"", "'");

        /// <summary>
        /// 출하 야경을 그대로 세운 임시 촬영장. 등·앰비언트·볼륨을 <b>손으로 적지 않고</b>
        /// <see cref="LookPresetApplier"/>로 적용한다 — 게임과 같은 경로여야 색이 같다.
        /// </summary>
        private sealed class BakeRig : IDisposable
        {
            private readonly Scene scene;
            private readonly UniversalAdditionalCameraData cameraData;

            public BakeRig(string presetPath, float extraExposureStops)
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SceneManager.SetActiveScene(scene);

                // 🔴🔴 프리셋은 <b>씬을 만든 뒤에</b> 읽어야 한다. NewScene(Single)이 참조 없는 에셋을
                // 내리면서 먼저 읽어 둔 ScriptableObject의 네이티브 쪽을 없애기 때문이다 — 관리 객체는
                // 살아 있어 `preset.LightRigPrefab.name`은 멀쩡히 읽히는데 `preset == null`은 true가 되는
                // Unity 특유의 가짜 null이다. 그 상태로 부르면 ApplyLightRig·ApplyVolumeProfile이 둘 다
                // 맨 앞 가드에서 조용히 되돌아가, <b>달빛도 ACES도 없는</b> 앰비언트 전용 그림이 구워진다
                // (실측으로 황소 채도가 인게임 39 대 7이었다. 원인은 조명 취향이 아니라 이 한 줄이었다).
                var preset = AssetDatabase.LoadAssetAtPath<EnvironmentLookPreset>(presetPath);
                if (preset == null)
                {
                    throw new InvalidOperationException($"[CodexBake] 야경 프리셋을 못 읽었다: {presetPath}");
                }

                // 환경(앰비언트·스카이박스)은 프리셋이 직접 RenderSettings에 쓴다.
                preset.ApplyEnvironment();
                // 🔴 안개만 끈다 — 카메라 스탠드오프가 피사체 크기에 비례해 멀어서, 지수 안개를
                //    켜 두면 큰 몬스터일수록 초록빛이 더 끼어 <b>크기마다 색이 달라진다</b>.
                RenderSettings.fog = false;

                // 🔴🔴 코드로 만든 씬에는 <b>환경 프로브가 없다</b>. 이걸 부르지 않으면 스카이박스
                // (따뜻한 서울 야경)에서 오는 앰비언트·반사가 통째로 빠져, 차가운 달빛만 남아
                // 몬스터가 회청색으로 뜬다 — 실측으로 황소 채도가 인게임 39 대 굽기 7이었다.
                DynamicGI.UpdateEnvironment();

                var rigAnchor = new GameObject("Preset Rig").transform;
                LookPresetApplier.ApplyLightRig(preset, rigAnchor);

                // 🔑 적용기는 실패를 <b>문자열로만</b> 알린다 — 아무도 안 읽으면 조용히 안 켜진다.
                //    등이 하나도 없으면 그림이 통째로 틀리므로 여기서 멈춘다.
                var lightCount = rigAnchor.GetComponentsInChildren<Light>(includeInactive: false).Length;
                if (lightCount == 0)
                {
                    throw new InvalidOperationException(
                        "[CodexBake] 야경 등이 하나도 서지 않았다 — 앰비언트만으로 구우면 색이 틀린다.");
                }

                var volumeObject = new GameObject("Post Volume");
                var volume = volumeObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 100f;
                LookPresetApplier.ApplyVolumeProfile(preset, volume);
                if (volume.sharedProfile == null)
                {
                    throw new InvalidOperationException(
                        "[CodexBake] 후처리 프로파일이 안 붙었다 — ACES·노출 없이 구우면 원색이 튄다.");
                }

                ApplyExposureBoost(volume, extraExposureStops);

                var cameraObject = new GameObject("Bake Camera");
                Camera = cameraObject.AddComponent<Camera>();
                Camera.orthographic = true;
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.allowHDR = true;
                Camera.enabled = false;

                cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
                cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                cameraData.antialiasingQuality = AntialiasingQuality.High;

                SubjectAnchor = new GameObject("Subject").transform;
            }

            /// <summary>
            /// 노출만 든다. 🔴 <c>volume.profile</c>은 <b>사본</b>을 돌려주므로(원본 에셋은
            /// <c>sharedProfile</c>) 여기서 값을 바꿔도 출하 프로파일이 오염되지 않는다 —
            /// <c>sharedProfile</c>을 건드리면 게임의 야경이 통째로 밝아진다.
            /// </summary>
            private static void ApplyExposureBoost(Volume volume, float extraExposureStops)
            {
                if (Mathf.Approximately(extraExposureStops, 0f))
                {
                    return;
                }

                var profile = volume.profile;
                if (profile == null)
                {
                    return;
                }

                if (!profile.TryGet<ColorAdjustments>(out var adjustments))
                {
                    adjustments = profile.Add<ColorAdjustments>(overrides: true);
                }

                adjustments.postExposure.overrideState = true;
                adjustments.postExposure.value += extraExposureStops;
            }

            public Camera Camera { get; }

            public Transform SubjectAnchor { get; }

            public void Place(Vector3 pivot, float orthoSize, Quaternion rotation)
            {
                var standoff = orthoSize * 8f + 10f;
                Camera.transform.rotation = rotation;
                Camera.transform.position = pivot - rotation * Vector3.forward * standoff;
                Camera.orthographicSize = orthoSize;
                Camera.nearClipPlane = 0.01f;
                Camera.farClipPlane = standoff * 2f + orthoSize * 8f;
                Camera.aspect = (float)Width / Height;
            }

            public Texture2D Render(bool withPostProcessing, Color background)
            {
                cameraData.renderPostProcessing = withPostProcessing;
                Camera.backgroundColor = background;

                var descriptor = new RenderTextureDescriptor(Width, Height, RenderTextureFormat.ARGB32, 24)
                {
                    sRGB = true,
                    msaaSamples = 1,
                };
                var target = RenderTexture.GetTemporary(descriptor);
                var previousActive = RenderTexture.active;
                try
                {
                    Camera.targetTexture = target;
                    Camera.Render();

                    RenderTexture.active = target;
                    var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, mipChain: false);
                    texture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                    texture.Apply();
                    return texture;
                }
                finally
                {
                    Camera.targetTexture = null;
                    RenderTexture.active = previousActive;
                    RenderTexture.ReleaseTemporary(target);
                }
            }

            public void Dispose()
            {
                if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(scene, removeScene: true);
                }
            }
        }

        private sealed class BakeResult
        {
            public string Id;
            public string DisplayName;
            public string PrefabPath;
            public string AssetPath = string.Empty;
            public string Skip = string.Empty;
            public bool Baked;
            public float OpaquePixelRatio;
            public float MeanChroma;
            public bool HasTransparentCorners;
        }
    }
}
