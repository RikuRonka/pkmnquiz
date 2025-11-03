using UnityEditor;
using UnityEngine;

public static class OptimizeSprites
{
    [MenuItem("Tools/Optimize Sprites (Crunch 50, Max 512, No Mips)")]
    public static void Run()
    {
        // Tweak this to match your folders
        var guids = AssetDatabase.FindAssets(
            "t:Texture2D",
            new[] { "Assets/Sprites", "Assets/Resources/Sprites", "Assets/Typeicons" }
        );

        int changed = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
                continue;

            bool dirty = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }

            if (importer.maxTextureSize > 512)
            {
                importer.maxTextureSize = 512;
                dirty = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Compressed)
            {
                importer.textureCompression = TextureImporterCompression.Compressed;
                dirty = true;
            }

            if (!importer.crunchedCompression)
            {
                importer.crunchedCompression = true;
                dirty = true;
            }

            if (importer.compressionQuality != 50)
            {
                importer.compressionQuality = 50;
                dirty = true;
            }

            if (dirty)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                changed++;
            }
        }

        Debug.Log($"OptimizeSprites: reimported {changed} textures.");
    }
}
