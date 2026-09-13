using UnityEditor;
using UnityEngine;

namespace Styles.EditorTools
{
    /// <summary>ComfyUI 产出的 PNG 一落进 Assets/Art 就自动套用正确的导入设置（2D 精灵 + 移动端压缩）。</summary>
    public class ArtImportPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Art/")) return;
            var importer = (TextureImporter)assetImporter;
            var isCharacter = assetPath.Contains("/Characters/");
            var isBackground = assetPath.Contains("/Backgrounds/") || assetPath.Contains("/CG/");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.spritePixelsPerUnit = 100f;
            importer.maxTextureSize = isBackground ? 2048 : 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;

            var settings = new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = 2048,
                format = TextureImporterFormat.ASTC_6x6,
                compressionQuality = 70,
                textureCompression = TextureImporterCompression.Compressed
            };
            importer.SetPlatformTextureSettings(settings);
            settings.name = "iPhone";
            importer.SetPlatformTextureSettings(settings);

            var desktop = new TextureImporterPlatformSettings
            {
                name = "Standalone",
                overridden = true,
                maxTextureSize = 2048,
                format = TextureImporterFormat.BC7,
                compressionQuality = 90
            };
            importer.SetPlatformTextureSettings(desktop);

            var spriteSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(spriteSettings);
            spriteSettings.spriteAlignment = isCharacter ? (int)SpriteAlignment.BottomCenter : (int)SpriteAlignment.Center;
            spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(spriteSettings);
        }
    }
}
