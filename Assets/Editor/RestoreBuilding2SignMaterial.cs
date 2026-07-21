using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot recovery: SetupBuilding2's first pass overwrote "007.1"/"007.1 (emission)"'s
/// original emission with a warm HDR color that only belonged on "007.1 (glass)", and
/// Undo wasn't available to recover it afterward. This re-extracts a pristine copy of
/// the original FBX from the still-present Store_Front.zip (never touched by Unity), lets
/// Unity import it fresh at a temp path — generating brand-new, unmodified materials —
/// reads the real original emission values off that untouched copy, and copies them onto
/// the corrupted materials on the actual in-scene FBX. Then deletes the temp copy.
/// Run once; safe to re-run (no-ops if the corrupted materials already look restored).
/// </summary>
public static class RestoreBuilding2SignMaterial
{
    private const string ZipPath = "Assets/Models/building_2/source/Store_Front.zip";
    private const string TempExtractDir = "Assets/Models/building_2/source/_TempRestoreReference";
    private const string TempFbxPath = TempExtractDir + "/Store Front.fbx";
    private const string LiveFbxPath = "Assets/Models/building_2/Store Front.fbx";

    [MenuItem("CyberBoss/Restore Building 2 Sign Material (one-shot)")]
    public static void Run()
    {
        var fullZipPath = Path.Combine(Application.dataPath, "..", ZipPath);
        if (!File.Exists(fullZipPath))
        {
            Debug.LogError($"[RestoreB2Sign] Zip not found at '{ZipPath}' — cannot recover original values this way.");
            return;
        }

        var fullExtractDir = Path.Combine(Application.dataPath, "..", TempExtractDir);
        Directory.CreateDirectory(fullExtractDir);

        // Deliberately using only the core ZipArchive stream API (System.IO.Compression),
        // not ZipFile.ExtractToDirectory/ExtractToFile — those convenience helpers live in
        // System.IO.Compression.FileSystem, which isn't reliably available under Unity's
        // scripting runtime. Extracting just the one FBX entry manually avoids that.
        var flattenedPath = Path.Combine(fullExtractDir, "Store Front.fbx");
        bool foundFbx = false;
        using (var fileStream = File.OpenRead(fullZipPath))
        using (var archive = new System.IO.Compression.ZipArchive(fileStream, System.IO.Compression.ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith("Store Front.fbx", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                using (var entryStream = entry.Open())
                using (var destStream = File.Create(flattenedPath))
                {
                    entryStream.CopyTo(destStream);
                }
                foundFbx = true;
                break;
            }
        }

        if (!foundFbx)
        {
            Debug.LogError("[RestoreB2Sign] Could not find 'Store Front.fbx' inside the zip.");
            return;
        }

        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(TempFbxPath, ImportAssetOptions.ForceUpdate);

        var referenceMaterials = new Dictionary<string, Material>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(TempFbxPath))
            if (asset is Material mat)
                referenceMaterials[mat.name] = mat;

        if (referenceMaterials.Count == 0)
        {
            Debug.LogError("[RestoreB2Sign] The fresh temp import produced no materials — aborting without touching the live asset.");
            CleanupTemp(fullExtractDir);
            return;
        }

        var liveMaterials = new Dictionary<string, Material>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(LiveFbxPath))
            if (asset is Material mat)
                liveMaterials[mat.name] = mat;

        int restored = 0;
        foreach (var name in new[] { "007.1", "007.1 (emission)" })
        {
            if (!referenceMaterials.TryGetValue(name, out var reference))
            {
                Debug.LogWarning($"[RestoreB2Sign] Reference copy has no material named '{name}' — skipping.");
                continue;
            }
            if (!liveMaterials.TryGetValue(name, out var live))
            {
                Debug.LogWarning($"[RestoreB2Sign] Live FBX has no material named '{name}' — skipping.");
                continue;
            }

            var refEmission = reference.HasProperty("_EmissionColor") ? reference.GetColor("_EmissionColor") : Color.black;
            var refBaseColor = reference.HasProperty("_BaseColor") ? reference.GetColor("_BaseColor") : Color.white;
            var refBaseMap = reference.HasProperty("_BaseMap") ? reference.GetTexture("_BaseMap") : null;
            var refEmissionMap = reference.HasProperty("_EmissionMap") ? reference.GetTexture("_EmissionMap") : null;

            // Don't trust the reference's own keyword state — freshly-imported FBX
            // materials in this project have consistently needed emission enabled
            // explicitly (same gotcha as the window fix and FixB2Material.cs/
            // SetWindowGlowEmission.cs elsewhere in this project); Unity's default FBX
            // import doesn't reliably flip this on even when a real emission color or
            // map is present. Decide based on whether there's actually something to emit.
            bool hasRealEmission = refEmission.maxColorComponent > 0.001f || refEmissionMap != null;

            // Only overwrite a texture slot if the reference actually resolved one — the
            // temp copy has no textures folder alongside it, so a null here almost always
            // means "couldn't find the file," not "this material has no texture." Don't
            // let that null clobber whatever the live material currently has wired.
            if (refBaseMap != null) live.SetTexture("_BaseMap", refBaseMap);
            live.SetColor("_BaseColor", refBaseColor);
            if (refEmissionMap != null) live.SetTexture("_EmissionMap", refEmissionMap);
            live.SetColor("_EmissionColor", refEmission);
            if (hasRealEmission)
            {
                live.EnableKeyword("_EMISSION");
                live.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                live.DisableKeyword("_EMISSION");
            }

            EditorUtility.SetDirty(live);
            restored++;
            Debug.Log($"[RestoreB2Sign] Restored '{name}' — BaseMap={(refBaseMap != null ? refBaseMap.name : "none")}, " +
                $"EmissionColor={refEmission}, EmissionMap={(refEmissionMap != null ? refEmissionMap.name : "none")}, " +
                $"emission enabled={hasRealEmission} (read from the untouched reference copy).");
        }

        AssetDatabase.SaveAssets();
        CleanupTemp(fullExtractDir);

        Debug.Log($"[RestoreB2Sign] Done — restored {restored} material(s). Save the scene (Ctrl+S) if it looks right.");
    }

    private static void CleanupTemp(string fullExtractDir)
    {
        AssetDatabase.DeleteAsset(TempExtractDir);
        if (Directory.Exists(fullExtractDir))
            Directory.Delete(fullExtractDir, recursive: true);
        var metaPath = TempExtractDir + ".meta";
        var fullMetaPath = Path.Combine(Application.dataPath, "..", metaPath);
        if (File.Exists(fullMetaPath)) File.Delete(fullMetaPath);
        AssetDatabase.Refresh();
    }
}
