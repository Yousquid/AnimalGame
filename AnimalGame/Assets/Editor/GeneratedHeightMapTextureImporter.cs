using System.IO;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.EditorTools
{
    /// <summary>
    /// Keeps generated DEM data linear, readable and uncompressed.  In
    /// particular, forcing R16 prevents Unity from silently collapsing the
    /// reconstructed 16-bit height field back to an 8-bit sprite texture.
    /// </summary>
    public sealed class GeneratedHeightMapTextureImporter : AssetPostprocessor
    {
        private const string HeightFileName = "Rocky_Moutain_Height_R16.png";
        private const string MaskFileName = "Rocky_Moutain_PlayableMask.png";

        public override uint GetVersion()
        {
            return 1;
        }

        private void OnPreprocessTexture()
        {
            string fileName = Path.GetFileName(assetPath);
            bool isHeight = fileName == HeightFileName;
            bool isMask = fileName == MaskFileName;
            if (!isHeight && !isMask)
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = isHeight ? FilterMode.Bilinear : FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;

            TextureImporterPlatformSettings settings =
                importer.GetDefaultPlatformTextureSettings();
            settings.maxTextureSize = 2048;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            settings.format = isHeight
                ? TextureImporterFormat.R16
                : TextureImporterFormat.R8;
            importer.SetPlatformTextureSettings(settings);
        }
    }
}
