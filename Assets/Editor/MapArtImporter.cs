using UnityEditor;
using UnityEngine;

namespace SheepGate.EditorTools
{
    /// <summary>
    /// Import contract for generated map images.
    ///
    /// MapChartArt remaps every opaque pixel onto ArtPalette at runtime, so these source textures
    /// must stay readable. Everything else matches the world-art contract: hard pixels, no mipmaps,
    /// no compression, no rescaling, and transparent edges treated as transparency rather than as
    /// a colour to bleed into the sprite.
    /// </summary>
    public sealed class MapArtImporter : AssetPostprocessor
    {
        const string Folder = "Assets/Resources/Art/Map/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder, System.StringComparison.Ordinal) ||
                !assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
        }
    }
}
