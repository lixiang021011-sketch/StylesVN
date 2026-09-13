using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Styles.EditorTools
{
    /// <summary>把 Assets/Fonts 下的中文字体做成 TMP 动态字体资源，供运行时直接加载。</summary>
    public static class TmpFontBuilder
    {
        const string FontDir = "Assets/Fonts";
        const string OutDir = "Assets/Resources/Fonts";
        const string RegularName = "NotoSerifSC-Regular.otf";
        const string OutAsset = OutDir + "/StylesSerif SDF.asset";

        [MenuItem("Styles/6. 生成中文 TMP 字体资源", false, 6)]
        public static void BuildCjkFontAsset()
        {
            var src = Path.Combine(FontDir, RegularName);
            if (!File.Exists(src))
            {
                Debug.LogWarning("[Styles] 找不到字体文件 " + src + "，请先把中文 OTF/TTF 放进 Assets/Fonts。");
                return;
            }
            Directory.CreateDirectory(OutDir);
            var font = AssetDatabase.LoadAssetAtPath<Font>(src);
            if (font == null) { Debug.LogWarning("[Styles] 字体导入失败：" + src); return; }

            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutAsset);
            if (existing != null) { Debug.Log("[Styles] TMP 字体资源已存在，跳过生成。"); Assign(existing); return; }

            // 动态字体：运行时按需加入字形，覆盖全部中日韩字符
            var asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048,
                AtlasPopulationMode.Dynamic, true);
            if (asset == null) { Debug.LogError("[Styles] TMP 字体资源创建失败。"); return; }
            asset.name = "StylesSerif SDF";
            AssetDatabase.CreateAsset(asset, OutAsset);
            // 图集贴图、材质、字体纹理都必须作为子资产写入，否则运行时 atlasTextures 为空 → 添加字形时崩溃
            if (asset.atlasTextures != null)
            {
                for (int i = 0; i < asset.atlasTextures.Length; i++)
                {
                    if (asset.atlasTextures[i] == null) continue;
                    asset.atlasTextures[i].name = "StylesSerif Atlas " + i;
                    AssetDatabase.AddObjectToAsset(asset.atlasTextures[i], asset);
                }
            }
            if (asset.material != null)
            {
                asset.material.name = "StylesSerif SDF Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }
            if (asset.material != null && asset.material.mainTexture == null && asset.atlasTextures != null && asset.atlasTextures.Length > 0)
                asset.material.mainTexture = asset.atlasTextures[0];
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Styles] 中文 TMP 字体资源已生成：" + OutAsset);
            Assign(asset);
        }

        static void Assign(TMP_FontAsset asset)
        {
            if (TMP_Settings.instance == null)
            {
                Debug.LogWarning("[Styles] 未找到 TMP Settings（TextMeshPro 基础资源未导入），运行时会使用显式指定的字体。");
                return;
            }
            // defaultFontAsset 只有只读属性，需要走序列化字段写入
            var so = new SerializedObject(TMP_Settings.instance);
            var prop = so.FindProperty("m_defaultFontAsset");
            if (prop != null)
            {
                prop.objectReferenceValue = asset;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(TMP_Settings.instance);
                AssetDatabase.SaveAssets();
                Debug.Log("[Styles] 已把中文字体设为 TMP 默认字体。");
            }
            else
            {
                Debug.LogWarning("[Styles] 未能写入 TMP 默认字体字段，运行时会显式指定字体。");
            }
        }
    }
}
