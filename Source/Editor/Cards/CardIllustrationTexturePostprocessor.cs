using UnityEditor;

namespace SeoulPlayup.Cards.Editor
{
    /// <summary>
    /// Gives textures dropped into <see cref="CardIllustrationCatalogBaker.IllustrationFolder"/>
    /// the Sprite import settings the shipped card illustrations use.
    /// <para>
    /// Without this, a newly added PNG imports as a plain Texture
    /// (<c>textureType: 0</c>, <c>spriteMode: 0</c>). The baker collects sprites with
    /// <c>AssetDatabase.FindAssets("t:Sprite", ...)</c>, so such a file is skipped and the card
    /// silently falls back to the placeholder illustration — no error, no warning. The generation
    /// pipeline drops PNGs here from outside Unity, which makes that trap easy to hit repeatedly.
    /// </para>
    /// </summary>
    public sealed class CardIllustrationTexturePostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(CardIllustrationCatalogBaker.IllustrationFolder + "/"))
                return;

            var importer = (TextureImporter)assetImporter;

            // Only seed defaults on the very first import (no .meta yet). Re-applying on every
            // reimport would silently revert a designer's deliberate per-asset tweaks.
            if (!importer.importSettingsMissing)
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
        }
    }
}
