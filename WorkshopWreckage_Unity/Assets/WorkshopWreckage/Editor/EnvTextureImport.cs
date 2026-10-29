using UnityEditor;

namespace WorkshopWreckage.EditorTools
{
    /// <summary>Import rules for the baked environment textures (tools/texgen/bake_textures.py):
    /// *_normal.png are normal maps, *_mask.png linear data, *_albedo.png sRGB colour; all
    /// mip-mapped, trilinear and anisotropic so floors and walls stay crisp at grazing angles.</summary>
    public class EnvTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter;
            if (assetPath.Contains("/Resources/ItemSkin/"))
            {
                // the component label atlas (tools/texgen/bake_item_decals.py): sRGB colour + coverage alpha,
                // one shared 1024 texture for every part; clamped, the shader keeps samples inside a tile
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true;
                ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                ti.filterMode = UnityEngine.FilterMode.Trilinear;
                ti.anisoLevel = 4;
                ti.maxTextureSize = 1024;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            if (!assetPath.Contains("/Resources/EnvTex/")) return;
            ti.mipmapEnabled = true;
            ti.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            ti.filterMode = UnityEngine.FilterMode.Trilinear;
            ti.anisoLevel = 8;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            if (assetPath.EndsWith("_normal.png")) ti.textureType = TextureImporterType.NormalMap;
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = !assetPath.EndsWith("_mask.png");
            }
        }
    }
}
