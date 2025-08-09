// CustomBarAffliction.cs
using HarmonyLib;
using MoreAfflictionsPlugin.APIs;
using UnityEngine;

/// <summary>
/// Lightweight helper that marks a cloned BarAffliction as "custom" and
/// stores the target status + width we want. The actual driving happens
/// via Harmony postfixes on the vanilla BarAffliction methods below.
/// </summary>
public class BarAfflictionCustom : MonoBehaviour
{
    [Header("Required")]
    public RectTransform rtf;

    [Header("Status")]
    public string statusName;  // assigned by the spawner

    [Header("Runtime")]
    public float size;         // desired width in px for this bar

    public float width
    {
        get { return rtf != null ? rtf.sizeDelta.x : 0f; }
        set { if (rtf != null) rtf.sizeDelta = new Vector2(value, rtf.sizeDelta.y); }
    }
}

// ===== Harmony: drive cloned bars that have BarAfflictionCustom attached =====
[HarmonyPatch]
internal static class BarAfflictionDrivePatches
{
    // Called when the HUD wants every affliction to recompute its desired size.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BarAffliction), "ChangeAffliction")]
    private static void ChangeAffliction_Postfix(BarAffliction __instance, StaminaBar bar)
    {
        var helper = __instance.GetComponent<BarAfflictionCustom>();
        if (helper == null || bar == null || Character.observedCharacter == null) return;

        // Read custom status value, convert to px
        float current = Character.observedCharacter.refs.afflictions.GetStatus(helper.statusName);
        helper.size = bar.fullBar.sizeDelta.x * Mathf.Clamp01(current);

        // Match vanilla affordances: min width + active toggle
        if (current > 0.01f)
        {
            if (helper.size < bar.minAfflictionWidth) helper.size = bar.minAfflictionWidth;
            if (!helper.gameObject.activeSelf) helper.gameObject.SetActive(true);
        }
        else
        {
            if (helper.gameObject.activeSelf) helper.gameObject.SetActive(false);
        }

        // Apply immediately so the visual background (template) looks correct at spawn
        helper.width = helper.size;
    }

    // Called every Update by StaminaBar so the bars ease toward the desired width.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BarAffliction), "UpdateAffliction")]
    private static void UpdateAffliction_Postfix(BarAffliction __instance, StaminaBar bar)
    {
        var helper = __instance.GetComponent<BarAfflictionCustom>();
        if (helper == null || bar == null) return;

        float t = Mathf.Min(Time.deltaTime * 10f, 0.1f);
        helper.width = Mathf.Lerp(helper.width, helper.size, t);
    }
}
