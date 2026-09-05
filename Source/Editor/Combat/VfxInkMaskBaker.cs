using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Combat
{
    /// <summary>
    /// 먹/카툰 VFX의 <b>형상 마스크</b>를 에셋으로 굽는다.
    /// (A단계 벤치 <c>VfxStyleVariantLab.BakeMask</c>의 승격판 — 저쪽은 메모리에만 굽고 버렸다.)
    ///
    /// <para>🔴🔴 <b>왜 마스크가 재입힘의 본체인가.</b> 확정 사양 4겹
    /// [발광 림 · 다크 키라인 · 짙은 먹 몸통 · 네온 코어]은 <b>우리가 구운 마스크에서만 선다.</b>
    /// 같은 값 한 벌을 팩 텍스처에 먹이면 겹이 하나도 안 잡힌다 — 팩 알파는 <b>거리장이 아니라
    /// 형상 그림</b>이라 값이 대부분 최상단에 몰려 있고, 그러면 테두리 두 겹이 차지할 알파 구간이
    /// 아예 없다(A003·A014 실측). 그래서 머티리얼만 갈아 끼우는 것은 재입힘이 아니고,
    /// <b>형상까지 우리 것으로 갈아야</b> 확정 사양이 화면에 나온다.</para>
    ///
    /// <para>🔴 <b>알파는 반드시 거리장(distance field)으로 굽는다.</b> <c>InkParticle</c>의 테두리
    /// 밴드는 알파 <i>경사</i>를 먹고 산다 — 알파가 0/1로 딱 떨어지는 하드 마스크를 물리면
    /// <b>키라인이 아예 안 나온다</b>. 굽는 순간의 경사 폭(<c>ramp</c>)이 곧 테두리 두 겹이 설
    /// 여백이므로, 셰이더 <c>_EdgeWidth</c>만으로는 굵기가 정해지지 않는다.</para>
    ///
    /// <para>🔴 <b>임포트 설정이 틀리면 마스크가 조용히 망가진다.</b> 이건 색이 아니라 마스크라
    /// <b>sRGB를 꺼야</b> 하고(감마 보정을 먹으면 경사가 휘어 겹 두께가 어긋난다), 압축을 먹이면
    /// 경사에 블록 아티팩트가 생겨 키라인이 우글거린다 → <b>Uncompressed</b>. 형상 마스크는
    /// 가장자리가 UV 밖으로 반복되면 안 되므로 <b>Clamp</b>다(타일러블 그레인만 Repeat).</para>
    ///
    /// <para>생성비 0 — 이미지 생성 API를 쓰지 않고 전부 코드로 굽는다. 형태는 코드가, 질감은
    /// 텍스처가 맡는다는 분업에서 <b>형태 쪽</b>이 여기다.</para>
    /// </summary>
    public static class VfxInkMaskBaker
    {
        // ⚠️ 대소문자를 <b>디스크와 정확히 맞춘다</b>. 이 프로젝트의 아트 폴더는 이미 `Art/VFX`이고,
        // macOS는 대소문자를 가리지 않아 "Art/Vfx"로 써도 <b>같은 폴더에 조용히 들어간다</b> —
        // 그러다 대소문자를 가리는 곳(CI·다른 OS·AssetDatabase 조회)에서 마스크를 못 찾는다.
        public const string MaskFolder = "Assets/Art/VFX/InkMasks";

        /// <summary>구울 때의 기본 경사 폭 <b>상한</b>. A단계 확정 사양 벤치가 쓴 값이다.
        ///
        /// <para>⚠️ <b>이것이 실제 경사 폭이 되는 것은 형상이 이보다 두꺼울 때뿐이다.</b>
        /// <see cref="Bake"/>가 형상의 실제 최대 반폭으로 이 값을 가둔다(이유는 거기 주석에 있다).
        /// 출하 여섯 중 <b>넷</b>이 이보다 얇아서, 가두기 전에는 알파가 1에 도달하지 못했다.</para></summary>
        public const float DefaultRamp = 0.115f;

        private const int MaskSize = 512;

        public enum InkMaskKind
        {
            /// <summary>붓형 테이퍼 호 — 눌렀다 떼는 획. 참격(A003·A004·A013)의 형상.</summary>
            BrushSlash,

            /// <summary>각진 파편 다발 — 카드 일러의 임팩트 어휘. 착탄·펀치의 형상.</summary>
            Shard,

            /// <summary>붓으로 그은 링 — 굵기가 불균일하고 한쪽이 <b>끊겨 있다</b>(여백).
            /// 충격파·파문·균열 링의 형상.</summary>
            Ring,

            /// <summary>꺾이며 뻗는 균열선 — 곧은 선이 아니라 각지게 꺾인다.</summary>
            Crack,

            /// <summary>한쪽이 굵고 끝으로 맺히는 획 — 스트레치 렌더러(십자 팔·돌진 궤적)용.</summary>
            Streak,

            /// <summary>갈라진 불꽃 혀 — 뿌리에서 굵고 끝이 휘며 맺힌다. 브레스(A007·A020)의 형상.</summary>
            Flame,

            /// <summary>등폭 리본. A단계 비교용(형태가 왜 지는지 보여 주는 대조군)이라
            /// 출하 프리팹에는 쓰지 않는다.</summary>
            Ribbon,

            /// <summary>붓형 + 절차 비백. A단계 비교용 — 코드로 질감을 흉내 낸 정직한 상한이다.</summary>
            BrushDry,

            /// <summary>
            /// <b>리본 단면 띠</b> — 아크 리본 메시(<see cref="VfxInkArcMeshBaker"/>) 전용.
            ///
            /// <para>🔑 아크를 쓰면 <b>형상은 메시가 들고 텍스처는 단면만 든다</b>(팩이 메시·텍스처를
            /// 나눠 쓰는 방식과 같다). 그래서 이 마스크는 U축으로는 균일하고 <b>V축(리본 폭 방향)으로만</b>
            /// 거리장이다 — 가운데가 네온 코어, 양 가장자리가 키라인·발광 림이 된다.</para>
            ///
            /// <para>🔴 <b>이것만 ramp가 다르다.</b> 다른 마스크는 얇은 형상이라 경사를 좁게 깔지만,
            /// 띠는 <b>폭 전체에 4겹을 펼쳐야</b> 하므로 경사가 반폭(0.5) 전체를 써야 한다.
            /// 기본 ramp(0.115)로 구우면 가운데 77%가 전부 알파 1이 되어 <b>통째로 코어색</b>이 된다.</para>
            /// </summary>
            Band,
        }

        /// <summary>형상마다 경사 폭이 다르다 — <see cref="InkMaskKind.Band"/> 주석 참조.</summary>
        private static float RampFor(InkMaskKind kind)
        {
            return kind == InkMaskKind.Band ? 0.5f : DefaultRamp;
        }

        /// <summary>출하에 쓰는 마스크. 비교용(Ribbon·BrushDry)은 에셋으로 굽지 않는다 —
        /// 판정이 끝난 대조군이 리포지토리에 남으면 어느 것이 확정값인지 알 수 없게 된다.</summary>
        private static readonly InkMaskKind[] ShippingKinds =
        {
            InkMaskKind.BrushSlash,
            InkMaskKind.Shard,
            InkMaskKind.Ring,
            InkMaskKind.Crack,
            InkMaskKind.Streak,
            InkMaskKind.Flame,
            InkMaskKind.Band,
        };

        public static string AssetPath(InkMaskKind kind)
        {
            return MaskFolder + "/ink_mask_" + ToSlug(kind) + ".png";
        }

        public static Texture2D Load(InkMaskKind kind)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath(kind));
        }

        [MenuItem("Tools/Seoul Playup/Combat/Bake Ink VFX Masks")]
        public static void BakeAllMenu()
        {
            Debug.Log(BakeAll());
        }

        /// <summary>출하 마스크 전량을 굽고 임포트 설정까지 잠근다. 멱등 — 같은 경로를 덮어쓴다.</summary>
        public static string BakeAll()
        {
            EnsureFolder(MaskFolder);
            var written = new List<string>();
            foreach (var kind in ShippingKinds)
            {
                written.Add(BakeToAsset(kind, RampFor(kind)));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "[VfxInkMaskBaker] baked " + written.Count + " masks:\n  " + string.Join("\n  ", written);
        }

        public static string BakeToAsset(InkMaskKind kind, float ramp = DefaultRamp)
        {
            EnsureFolder(MaskFolder);
            var path = AssetPath(kind);
            var texture = Bake(kind, ramp);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ApplyImportSettings(path);
            return path;
        }

        /// <summary>🔴 마스크로서의 요건을 임포터에 못 박는다 — 하나라도 어긋나면 겹이 어긋난다.</summary>
        private static void ApplyImportSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("[VfxInkMaskBaker] 임포터를 못 찾았다: " + path);
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            // 색이 아니라 마스크다 — 감마 보정을 먹으면 거리장 경사가 휘고 겹 두께가 틀어진다.
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = MaskSize;
            // 압축은 경사에 블록 아티팩트를 남기고, 그 아티팩트가 그대로 키라인 우글거림이 된다.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // ─────────────────────────────────────────────────────────────────────
        // 굽기 — 알파 = 실루엣 경계에서 0, 안쪽으로 ramp만큼 걸쳐 1까지 오른다
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>메모리 텍스처 한 장. 비교 벤치(<c>VfxStyleVariantLab</c>)도 이 함수를 쓴다 —
        /// 벤치에서 본 형상과 출하 에셋이 갈라지면 안 되기 때문이다.</summary>
        public static Texture2D Bake(InkMaskKind kind, float ramp)
        {
            var tex = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false)
            {
                name = "InkMask_" + ToSlug(kind),
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[MaskSize * MaskSize];
            ramp = Mathf.Max(0.002f, ramp);

            // 🔴🔴 <b>경사 폭은 형상의 최대 반폭보다 넓을 수 없다.</b> 알파는 <c>inside / ramp</c>인데
            // <c>inside</c>는 실루엣 경계까지의 거리라 <b>최대값이 곧 그 형상의 최대 반폭</b>이다.
            // 반폭이 ramp보다 얇으면 알파가 <b>1에 영영 도달하지 못한다</b> — 그러면 확정 사양 4겹이
            // 조용히 무너진다. 네온 코어는 <c>banded ≥ 0.90</c>을 요구하므로 <b>아예 발동하지 않고</b>,
            // 화면에 남는 값이 전부 하단부라 픽셀 대부분이 림·키라인 구간에 몰려 <b>속 빈 윤곽선</b>이 된다.
            //
            // 실측(정규화 이전, ramp 0.115 고정): 여섯 중 <b>넷</b>이 포화에 실패했다 —
            // Crack 0.33 · BrushSlash 0.59 · Ring 0.64 · Shard 0.74 (Streak·Flame만 1.00).
            // Crack은 보이는 픽셀의 <b>95%가 림+키라인</b>이라 먹 몸통이 사실상 없었고, 화면에서
            // 균열선이 <b>철사 윤곽</b>으로 찍혔다. 같은 그림 안의 Streak(1.00)만 네온 코어가 제대로
            // 서 있어서, <b>한 장 안에서 정규화의 유무가 그대로 품질 차이</b>로 보였다.
            //
            // 그래서 형상마다 실제 최대 반폭을 재서 ramp를 거기에 <b>가둔다</b>. 이미 포화하던
            // Streak·Flame은 <c>Min</c>이 원래 ramp를 그대로 돌려주므로 <b>출력이 한 픽셀도 바뀌지 않는다</b> —
            // 고장 난 넷만 고쳐진다.
            var maxInside = 0f;
            for (var y = 0; y < MaskSize; y++)
            {
                for (var x = 0; x < MaskSize; x++)
                {
                    maxInside = Mathf.Max(maxInside, Distance(kind, (x + 0.5f) / MaskSize, (y + 0.5f) / MaskSize));
                }
            }

            if (maxInside > 0.0001f)
            {
                ramp = Mathf.Min(ramp, maxInside);
            }

            for (var y = 0; y < MaskSize; y++)
            {
                for (var x = 0; x < MaskSize; x++)
                {
                    var u = (x + 0.5f) / MaskSize;
                    var v = (y + 0.5f) / MaskSize;
                    var inside = Distance(kind, u, v); // 안쪽이면 양수(=경계까지의 거리)
                    var alpha = Mathf.Clamp01(inside / ramp);
                    if (kind == InkMaskKind.BrushDry)
                    {
                        alpha *= DryStreak(u, v);
                    }

                    // RGB는 흰색 고정 — 색은 전적으로 틴트와 셰이더 겹이 낸다. 텍스처에 색이 구워져
                    // 들어가면 틴트가 그 위에 곱해져 색이 두 번 먹는다(파랑×노랑=초록 사고와 같은 꼴).
                    pixels[y * MaskSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>리본 단면 — U는 무시하고 <b>V축 가운데로 갈수록</b> 값이 커진다.
        /// 가장자리(v=0·1)에서 0, 한가운데(v=0.5)에서 0.5.</summary>
        private static float BandDistance(float v)
        {
            return 0.5f - Mathf.Abs(v - 0.5f);
        }

        private static float Distance(InkMaskKind kind, float u, float v)
        {
            switch (kind)
            {
                case InkMaskKind.Shard: return ShardDistance(u, v);
                case InkMaskKind.Ring: return RingDistance(u, v);
                case InkMaskKind.Crack: return CrackDistance(u, v);
                case InkMaskKind.Streak: return StreakDistance(u, v);
                case InkMaskKind.Flame: return FlameDistance(u, v);
                case InkMaskKind.Band: return BandDistance(v);
                default: return ArcDistance(kind, u, v);
            }
        }

        /// <summary>호(弧) — 중심 c, 반지름 r 위를 도는 띠. 각도를 따라 반폭이 변한다.</summary>
        private static float ArcDistance(InkMaskKind kind, float u, float v)
        {
            var c = new Vector2(0.5f, 1.02f);
            const float r = 0.62f;
            var d = new Vector2(u, v) - c;
            var radius = d.magnitude;
            var angle = Mathf.Atan2(d.y, d.x);

            const float a0 = -2.62f; // -150°
            const float a1 = -0.52f; //  -30°
            var t = Mathf.InverseLerp(a0, a1, angle);
            if (t <= 0f || t >= 1f)
            {
                return -1f;
            }

            float halfWidth;
            if (kind == InkMaskKind.Ribbon)
            {
                halfWidth = 0.052f; // 등폭 — 대조군
            }
            else
            {
                // 붓: 눌렀다 떼는 획 — 앞쪽에서 부풀고 끝으로 가늘어지며 맺힌다.
                var swell = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)), 0.55f);
                halfWidth = 0.086f * swell * Mathf.Lerp(1.15f, 0.28f, t);
            }

            return halfWidth - Mathf.Abs(radius - r);
        }

        /// <summary>각진 파편 다발 — 중심에서 뻗는 삼각 스파이크. 길이가 불균등해야 붙박이로 안 보인다.</summary>
        private static float ShardDistance(float u, float v)
        {
            var d = new Vector2(u, v) - new Vector2(0.5f, 0.5f);

            // ⚠️ 극좌표 쐐기(각도 폭 × 반지름)로 그리면 가장자리가 휘어 <b>잎사귀</b>가 된다
            // (A단계에서 실제로 꽃잎으로 보였다). 각진 파편은 <b>직선 삼각형</b>이라야 하므로
            // 데카르트 좌표로 친다.
            var best = -1f;
            float[] lengths = { 0.46f, 0.30f, 0.40f, 0.22f, 0.44f, 0.27f, 0.36f };
            float[] widths = { 0.085f, 0.062f, 0.078f, 0.05f, 0.082f, 0.058f, 0.07f };
            for (var i = 0; i < lengths.Length; i++)
            {
                var spikeAngle = i * (Mathf.PI * 2f / lengths.Length) + 0.24f;
                var dir = new Vector2(Mathf.Cos(spikeAngle), Mathf.Sin(spikeAngle));
                var side = new Vector2(-dir.y, dir.x);
                var along = Vector2.Dot(d, dir);
                var across = Mathf.Abs(Vector2.Dot(d, side));
                var length = lengths[i];
                if (along < 0f || along > length)
                {
                    continue;
                }

                var halfWidth = widths[i] * (1f - along / length);
                best = Mathf.Max(best, Mathf.Min(halfWidth - across, along));
            }

            return best;
        }

        /// <summary>
        /// 붓으로 그은 링. 등폭 원이 아니라 <b>굵기가 각도를 따라 변하고 한 곳이 끊긴다</b>.
        /// 🔑 오오카미가 붓질을 만드는 장치가 「굵기 불균일 + 여백」인데, 등폭 완전원은 그 둘을
        /// 다 잃어서 <b>스티커</b>로 읽힌다(A단계 등폭 리본이 진 것과 같은 이유).
        /// </summary>
        private static float RingDistance(float u, float v)
        {
            var d = new Vector2(u, v) - new Vector2(0.5f, 0.5f);
            var radius = d.magnitude;
            var angle = Mathf.Atan2(d.y, d.x); // -π..π

            const float r = 0.38f;

            // 붓을 뗀 자리 — 한 곳을 통째로 비워 여백을 만든다.
            var gapCenter = 1.15f;
            var gap = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, gapCenter * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
            if (gap < 0.30f)
            {
                return -1f;
            }

            // 굵기 불균일 — 두 주기를 겹쳐 규칙적인 물결로 안 보이게 한다.
            var swell = 1f + 0.36f * Mathf.Sin(angle * 2f + 0.7f) + 0.18f * Mathf.Sin(angle * 5f - 1.3f);
            // 끊긴 자리 양옆은 붓이 떠오르는 구간이라 가늘어져야 획으로 읽힌다.
            var taper = Mathf.Clamp01((gap - 0.30f) / 0.55f);
            var halfWidth = 0.052f * swell * Mathf.Lerp(0.25f, 1f, taper);

            return halfWidth - Mathf.Abs(radius - r);
        }

        /// <summary>
        /// 균열선 — 중앙에서 아래로 뻗으며 <b>각지게 꺾이는</b> 꺾은선과 잔가지 둘.
        /// 매끄러운 곡선으로 그리면 균열이 아니라 뱀이 된다.
        /// </summary>
        private static float CrackDistance(float u, float v)
        {
            var p = new Vector2(u, v);
            var best = -1f;

            // 본선: 위에서 아래로 지그재그. 폭은 뿌리에서 굵고 끝에서 맺힌다.
            Vector2[] spine =
            {
                new Vector2(0.50f, 0.94f), new Vector2(0.56f, 0.76f), new Vector2(0.44f, 0.60f),
                new Vector2(0.55f, 0.42f), new Vector2(0.46f, 0.24f), new Vector2(0.52f, 0.08f),
            };
            best = Mathf.Max(best, Polyline(p, spine, 0.038f, 0.008f));

            // 잔가지 — 균열은 갈라진다. 하나뿐이면 그냥 선이다.
            Vector2[] branchA = { new Vector2(0.50f, 0.68f), new Vector2(0.30f, 0.58f), new Vector2(0.18f, 0.60f) };
            best = Mathf.Max(best, Polyline(p, branchA, 0.022f, 0.006f));

            Vector2[] branchB = { new Vector2(0.50f, 0.34f), new Vector2(0.70f, 0.28f), new Vector2(0.82f, 0.32f) };
            best = Mathf.Max(best, Polyline(p, branchB, 0.021f, 0.006f));

            return best;
        }

        /// <summary>꺾은선을 따라가는 띠. 반폭은 시작 <paramref name="halfStart"/>에서
        /// 끝 <paramref name="halfEnd"/>까지 선형으로 좁아진다.</summary>
        private static float Polyline(Vector2 p, IReadOnlyList<Vector2> points, float halfStart, float halfEnd)
        {
            var total = 0f;
            for (var i = 0; i < points.Count - 1; i++)
            {
                total += Vector2.Distance(points[i], points[i + 1]);
            }

            var travelled = 0f;
            var best = -1f;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                var ab = b - a;
                var length = ab.magnitude;
                if (length < 1e-5f)
                {
                    continue;
                }

                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / (length * length));
                var closest = a + ab * t;
                var progress = total < 1e-5f ? 0f : (travelled + length * t) / total;
                var half = Mathf.Lerp(halfStart, halfEnd, progress);
                best = Mathf.Max(best, half - Vector2.Distance(p, closest));
                travelled += length;
            }

            return best;
        }

        /// <summary>
        /// 스트레치 렌더러용 획 — 파티클이 진행 방향으로 늘어나므로 마스크는 <b>U축을 따라</b>
        /// 굵다가 맺히는 하나의 획이다. 양 끝이 다 뭉툭하면 늘어났을 때 소시지가 된다.
        /// </summary>
        private static float StreakDistance(float u, float v)
        {
            // 획은 U 0.06~0.94 구간에 산다. 앞(U=0)이 굵고 뒤로 갈수록 맺힌다.
            var t = Mathf.InverseLerp(0.06f, 0.94f, u);
            if (t <= 0f || t >= 1f)
            {
                return -1f;
            }

            var swell = Mathf.Pow(Mathf.Sin(Mathf.PI * t), 0.35f);
            var halfWidth = 0.19f * swell * Mathf.Lerp(1f, 0.22f, t);
            return halfWidth - Mathf.Abs(v - 0.5f);
        }

        /// <summary>
        /// 불꽃 혀 — 뿌리(V=0)에서 굵고 끝으로 휘며 맺힌다. 가장자리를 톱니로 흔들어 갈라진 결을 준다.
        /// 🔑 팩의 불은 <b>둥근 구름</b>이라 아무리 색을 바꿔도 카툰 화염이 안 된다 —
        /// 화염을 화염으로 읽히게 하는 건 색이 아니라 <b>혀의 실루엣</b>이다.
        /// </summary>
        private static float FlameDistance(float u, float v)
        {
            // 🔴 <b>혀는 +U를 향한다</b>(+V가 아니다). 브레스는 스트레치 렌더러로 그리는데
            // 그 렌더러는 텍스처의 <b>U축을 속도 방향에 정렬</b>하므로, 혀를 세로로 구우면
            // 전방으로 뻗는 대신 옆으로 눕는다.
            var t = Mathf.InverseLerp(0.06f, 0.95f, u);
            if (t <= 0f || t >= 1f)
            {
                return -1f;
            }

            // 끝으로 갈수록 한쪽으로 휜다 — 좌우대칭이면 불이 아니라 나뭇잎이다.
            var sway = 0.06f * Mathf.Sin(t * 2.4f + 0.5f) * t;
            var half = 0.19f * Mathf.Pow(1f - t, 0.8f) * Mathf.Pow(Mathf.Clamp01(t * 5f), 0.45f);
            half *= 1f + 0.16f * Mathf.Sin(t * 16f);
            return half - Mathf.Abs(v - 0.5f - sway);
        }

        /// <summary>절차적 비백 — A단계 대조군 전용. 진짜 붓처럼 불규칙하지 않고 결이 고르게
        /// 반복되는 티가 나는데, 그 한계 자체가 판정 재료였다.</summary>
        private static float DryStreak(float u, float v)
        {
            var c = new Vector2(0.5f, 1.02f);
            var d = new Vector2(u, v) - c;
            var across = d.magnitude * 46f;
            var along = Mathf.Atan2(d.y, d.x) * 5f;

            var n = ValueNoise(across, along) * 0.65f + ValueNoise(across * 2.3f, along * 1.7f) * 0.35f;
            return Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, (n - 0.22f) * 2.4f) * 0.85f + 0.15f);
        }

        private static float ValueNoise(float x, float y)
        {
            var xi = Mathf.FloorToInt(x);
            var yi = Mathf.FloorToInt(y);
            var xf = x - xi;
            var yf = y - yi;
            xf = xf * xf * (3f - 2f * xf);
            yf = yf * yf * (3f - 2f * yf);
            var a = Hash(xi, yi);
            var b = Hash(xi + 1, yi);
            var cc = Hash(xi, yi + 1);
            var dd = Hash(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, xf), Mathf.Lerp(cc, dd, xf), yf);
        }

        private static float Hash(int x, int y)
        {
            var h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        private static string ToSlug(InkMaskKind kind)
        {
            return kind.ToString().ToLowerInvariant();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
