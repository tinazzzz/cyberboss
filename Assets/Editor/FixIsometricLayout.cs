using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Quick fix: adjusts camera angle and building heights without full rebuild.
/// - Camera to 48 deg X / 45 deg Y (true isometric diagonal, steep enough to see floor)
/// - Front towers (SE/SW) scaled short so they don't block the arena view
/// - Back towers (NE/NW) kept tall for dramatic skyline
public class FixIsometricLayout
{
    [MenuItem("CyberBoss/Fix Isometric Layout")]
    public static void Execute()
    {
        // ── Camera ────────────────────────────────────────────────────────
        var cm = GameObject.Find("CM_IsometricCamera");
        if (cm != null)
        {
            cm.transform.position = new Vector3(20f, 28f, -20f);
            cm.transform.rotation = Quaternion.Euler(48f, 45f, 0f);
        }

        // ── Scene view ────────────────────────────────────────────────────
        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            sv.pivot     = new Vector3(0f, 2f, 0f);
            sv.rotation  = Quaternion.Euler(48f, 45f, 0f);
            sv.size      = 28f;
            sv.orthographic = false;
            sv.Repaint();
        }

        // ── Building heights ──────────────────────────────────────────────
        // Front (near-camera) towers: shrink to height 6-7 so arena floor is visible.
        // Back towers: push up to 22-24 for dramatic background.
        ScaleToHeight("TowerSE", 7f);
        ScaleToHeight("TowerSW", 6f);
        ScaleToHeight("TowerNE", 24f);
        ScaleToHeight("TowerNW", 20f);
        ScaleToHeight("BldS1", 5f);
        ScaleToHeight("BldS2", 4f);
        ScaleToHeight("BldN1", 15f);
        ScaleToHeight("BldN2", 13f);

        // Move all buildings outward from center (±11 → ±13)
        // so more open floor is visible from the isometric view
        ShiftOutward("TowerNE",  new Vector3( 13f, 0f,  13f));
        ShiftOutward("TowerNW",  new Vector3(-13f, 0f,  13f));
        ShiftOutward("TowerSE",  new Vector3( 13f, 0f, -13f));
        ShiftOutward("TowerSW",  new Vector3(-13f, 0f, -13f));
        ShiftOutward("BldN1",    new Vector3(  4f, 0f,  13f));
        ShiftOutward("BldN2",    new Vector3( -4f, 0f,  13f));
        ShiftOutward("BldS1",    new Vector3(  4f, 0f, -13f));
        ShiftOutward("BldS2",    new Vector3( -4f, 0f, -13f));
        ShiftOutward("BldE1",    new Vector3( 13f, 0f,   4f));
        ShiftOutward("BldE2",    new Vector3( 13f, 0f,  -2f));
        ShiftOutward("BldW1",    new Vector3(-13f, 0f,   4f));
        ShiftOutward("BldW2",    new Vector3(-13f, 0f,  -2f));

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        Debug.Log("[CyberBoss] Isometric layout fixed.");
    }

    // Rescales the named building so its Y extent equals targetHeight.
    // The building body cube keeps the same XZ scale; only Y changes.
    // Child window strips and rooftop move proportionally via parent scale.
    static void ScaleToHeight(string name, float targetHeight)
    {
        var go = GameObject.Find("Buildings/" + name);
        if (go == null) return;

        // The body cube is the root (has the main renderer).
        // Its localScale.y is the full height; body centre is at y = height/2.
        var s = go.transform.localScale;
        float oldH = s.y;
        if (oldH <= 0f) return;

        float ratio = targetHeight / oldH;
        go.transform.localScale = new Vector3(s.x, targetHeight, s.z);

        // Shift the body cube up so the base stays on the floor
        var p = go.transform.position;
        go.transform.position = new Vector3(p.x, targetHeight * 0.5f, p.z);
    }

    // Moves the building root AND all its window-strip children to a new base position.
    static void ShiftOutward(string name, Vector3 newBaseXZ)
    {
        var go = GameObject.Find("Buildings/" + name);
        if (go == null) return;

        float halfH = go.transform.localScale.y * 0.5f;
        Vector3 oldPos = go.transform.position;
        Vector3 delta  = new Vector3(newBaseXZ.x - oldPos.x, 0f, newBaseXZ.z - oldPos.z);
        go.transform.position = new Vector3(newBaseXZ.x, halfH, newBaseXZ.z);

        // Move sibling window strip children (they share the parent container, not child of body)
        // Window strips are direct children of the Buildings root named "TowerXX_WN0" etc.
        var buildingsRoot = GameObject.Find("Buildings");
        if (buildingsRoot == null) return;

        foreach (Transform child in buildingsRoot.transform)
        {
            if (child.gameObject == go) continue;
            if (child.name.StartsWith(name + "_"))
                child.position += delta;
        }
    }
}
