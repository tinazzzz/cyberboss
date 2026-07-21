using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class FixMeleeHitboxAndDamageColors
{
    [MenuItem("CyberBoss/Fix Melee Hitbox + Damage Text Colors")]
    public static void Execute()
    {
        var playerGo = GameObject.Find("Muryotaisu");
        var bossGo   = GameObject.Find("Boss");

        // ── 1. Add PlayerMeleeHitbox to Muryotaisu ───────────────────────
        // The Hit AnimationEvent on Unarmed-Attack-R1 must reach PlayerMeleeHitbox
        // to deal damage. AnimationEventReceiver.Hit() is a stub and does NOT block
        // this — Unity SendMessage calls ALL components with the method name.
        if (playerGo != null)
        {
            if (playerGo.GetComponent<PlayerMeleeHitbox>() == null)
            {
                playerGo.AddComponent<PlayerMeleeHitbox>();
                EditorUtility.SetDirty(playerGo);
                Debug.Log("[Fix] Added PlayerMeleeHitbox to Muryotaisu.");
            }
            else
            {
                Debug.Log("[Fix] PlayerMeleeHitbox already on Muryotaisu.");
            }

            // ── 2. Set player HealthSystem damage text color to green ────
            var playerHs = playerGo.GetComponent<HealthSystem>();
            if (playerHs != null)
            {
                var so = new SerializedObject(playerHs);
                var colorProp = so.FindProperty("_damageTextColor");
                if (colorProp != null)
                {
                    colorProp.colorValue = new Color(0.20f, 1.00f, 0.35f); // neon green
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(playerGo);
                    Debug.Log("[Fix] Player HealthSystem damage text color → neon green.");
                }
                else
                {
                    Debug.LogError("[Fix] _damageTextColor field not found on HealthSystem.");
                }
            }
            else
            {
                Debug.LogWarning("[Fix] HealthSystem not found on Muryotaisu.");
            }
        }
        else
        {
            Debug.LogError("[Fix] Muryotaisu not found in scene.");
        }

        // ── 3. Confirm boss HealthSystem damage text is red ─────────────
        if (bossGo != null)
        {
            var bossHs = bossGo.GetComponent<HealthSystem>();
            if (bossHs != null)
            {
                var so = new SerializedObject(bossHs);
                var colorProp = so.FindProperty("_damageTextColor");
                if (colorProp != null)
                {
                    // Neon red — more visible than pure red
                    colorProp.colorValue = new Color(1.00f, 0.20f, 0.20f);
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(bossGo);
                    Debug.Log("[Fix] Boss HealthSystem damage text color → neon red (confirmed).");
                }
            }
        }
        else
        {
            Debug.LogWarning("[Fix] Boss not found — skipping boss damage color confirmation.");
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Fix] Done. Scene saved.");
    }
}
