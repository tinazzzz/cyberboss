using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;

/// Checks the URP Renderer Data asset for postProcessingEnabled.
/// This flag is independent of the camera's renderPostProcessing —
/// both must be true for bloom/tonemapping to render.
public class CheckURPRenderer
{
    [MenuItem("CyberBoss/Check URP Renderer Data")]
    public static void Execute()
    {
        var urpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                       as UniversalRenderPipelineAsset;
        if (urpAsset == null) { Debug.LogError("[URPCheck] No URP asset!"); return; }

        // Access the renderer list via SerializedObject
        var so = new SerializedObject(urpAsset);
        var rendererListProp = so.FindProperty("m_RendererDataList");

        if (rendererListProp == null)
        {
            Debug.LogWarning("[URPCheck] Could not find m_RendererDataList — trying direct API.");
        }
        else
        {
            Debug.Log($"[URPCheck] Renderer list count: {rendererListProp.arraySize}");
        }

        // Try to load renderer data directly from known paths
        string[] searchPaths = new[]
        {
            "Assets/Settings/URP-HighFidelity-Renderer.asset",
            "Assets/Settings/URP_Renderer.asset",
            "Assets/Settings/UniversalRenderer.asset",
            "Assets/Settings/PC_RPAsset_Renderer.asset",
        };

        ScriptableRendererData rendererData = null;
        foreach (var path in searchPaths)
        {
            rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (rendererData != null)
            {
                Debug.Log($"[URPCheck] Found renderer data at: {path}");
                break;
            }
        }

        if (rendererData == null)
        {
            // Search all assets
            var guids = AssetDatabase.FindAssets("t:ScriptableRendererData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                Debug.Log($"[URPCheck] Found ScriptableRendererData: {path}");
                if (rendererData == null)
                    rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            }
        }

        if (rendererData == null)
        {
            Debug.LogError("[URPCheck] No ScriptableRendererData found anywhere!");
            return;
        }

        // Check postProcessingEnabled on the renderer data
        var rdSo = new SerializedObject(rendererData);
        rdSo.Update();
        var ppProp = rdSo.FindProperty("m_PostProcessingFeatureSet");
        var ppEnabledProp = rdSo.FindProperty("m_PostProcessData");

        // UniversalRendererData has postProcessingEnabled (bool) in older versions
        // In URP 14+ it's always on if the camera requests it
        Debug.Log($"[URPCheck] Renderer data name: {rendererData.name}");
        Debug.Log($"[URPCheck] Renderer data type: {rendererData.GetType().Name}");

        // Print all bool properties on the renderer data
        var it = rdSo.GetIterator();
        it.Next(true);
        while (it.NextVisible(false))
        {
            if (it.propertyType == SerializedPropertyType.Boolean)
                Debug.Log($"[URPCheck] {it.name} = {it.boolValue}");
        }

        Debug.Log("[URPCheck] Done — review bool fields above for any disabled post-process flags.");
    }
}
