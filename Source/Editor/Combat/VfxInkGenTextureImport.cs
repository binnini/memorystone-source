using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Combat
{
    /// <summary>
    /// 그림 반입 마스크(<c>ink_gen_*</c>)의 임포트 설정을 못 박는다.
    ///
    /// <para>이 텍스처들은 Unity 밖에서 굽는다(<c>tools/vfx-ink-bake/bake.py</c> — 먹 원화를
    /// 거리장·그레인으로 변환). 절차 마스크(<c>ink_mask_*</c>)는 <see cref="VfxInkMaskBaker"/>가
    /// 굽고 나서 임포터를 직접 잠그지만, 밖에서 온 PNG는 그 손을 못 타므로 여기서 자동으로
    /// 같은 설정을 건다 — 재베이크 때마다 사람이 임포터를 만질 필요가 없어야 한다.</para>
    ///
    /// <para>🔴 하나라도 어긋나면 마스크가 조용히 망가진다(<see cref="VfxInkMaskBaker"/> 주석과
    /// 같은 이유): 색이 아니라 데이터라 <b>sRGB 끔</b>(감마가 거리장 경사를 휘어 겹 두께가
    /// 어긋난다) · <b>무압축</b>(블록 아티팩트 = 키라인 우글거림) · <b>mip 끔</b>.
    /// 형상 거리장은 <b>Clamp</b>(가장자리가 UV 밖으로 반복되면 안 된다),
    /// 그레인(<c>*_grain</c>)만 <b>Repeat</b>(흐르는 먹이 UV를 넘어 타일된다 — §2 반입 규약).</para>
    /// </summary>
    public sealed class VfxInkGenTextureImport : AssetPostprocessor
    {
        private const string Prefix = VfxInkMaskBaker.MaskFolder + "/ink_gen_";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Prefix))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = assetPath.EndsWith("_grain.png")
                ? TextureWrapMode.Repeat
                : TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
