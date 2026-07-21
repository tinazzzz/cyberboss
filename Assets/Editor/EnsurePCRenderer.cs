using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;

/// Confirms which renderer the active URP asset uses, and if it's Mobile_Renderer,
/// switches to PC_Renderer which has full HDR post-processing support.
public class EnsurePCRenderer
{
    [MenuItem("CyberBoss/Ensure PC Renderer")]
    public static void Execute()
    {
        var urpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                       as UniversalRenderPipelineAsset;
        if (urpAsset == null) { Debug.LogError("[Renderer] No URP asset!"); return; }

        // Read the active renderer name via serialized object
        var so = new SerializedObject(urpAsset);
        so.Update();

        var rendererListProp = so.FindProperty("m_RendererDataList");
        if (rendererListProp != null && rendererListProp.arraySize > 0)
        {
            var firstRenderer = rendererListProp.GetArrayElementAtIndex(0).objectReferenceValue
                                as ScriptableRendererData;
            if (firstRenderer != null)
                Debug.Log($"[Renderer] Active renderer: {firstRenderer.name} at " +
                          $"{AssetDatabase.GetAssetPath(firstRenderer)}");
            else
                Debug.LogWarning("[Renderer] Renderer slot 0 is null!");
        }

        // Load PC_Renderer
        var pcRenderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(
            "Assets/Settings/PC_Renderer.asset");
        var mobileRenderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(
            "Assets/Settings/Mobile_Renderer.asset");

        Debug.Log($"[Renderer] PC_Renderer found: {pcRenderer != null}");
        Debug.Log($"[Renderer] Mobile_Renderer found: {mobileRenderer != null}");

        // If the active renderer is Mobile, switch to PC for full post-processing
        if (rendererListProp != null && rendererListProp.arraySize > 0 && pcRenderer != null)
        {
            var activeRenderer = rendererListProp.GetArrayElementAtIndex(0).objectReferenceValue
                                 as ScriptableRendererData;
            if (activeRenderer != null && activeRenderer.name.Contains("Mobile"))
            {
                Debug.LogWarning("[Renderer] Active renderer is Mobile — switching to PC_Renderer for HDR bloom.");
                rendererListProp.GetArrayElementAtIndex(0).objectReferenceValue = pcRenderer;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(urpAsset);
                AssetDatabase.SaveAssets();
                Debug.Log("[Renderer] Switched to PC_Renderer. Reimport may be needed.");
            }
            else
            {
                Debug.Log("[Renderer] Active renderer is already PC-class — no change needed.");
            }
        }

        Debug.Log("[Renderer] Done.");
    }
}
