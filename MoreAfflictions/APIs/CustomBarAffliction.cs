// CustomBarAffliction.cs
// Helper + Harmony drivers that actually size, show/hide, and icon the custom bars.

using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using MoreAfflictionsPlugin.APIs; // ← change if your AfflictionsAPI namespace differs

public class BarAfflictionCustom : MonoBehaviour
{
    [Header("Required")]
    public RectTransform rtf;

    [Header("Status Identity")]
    public string statusName;     // e.g., "Thirst" or "ThirstTest"
    public int statusIndex = -1;

    [Header("Runtime")]
    public float size;            // target width in px

    private Image _iconImg;

    public float width
    {
        get => rtf ? rtf.sizeDelta.x : 0f;
        set { if (rtf) rtf.sizeDelta = new Vector2(value, rtf.sizeDelta.y); }
    }

    public void TryApplyIconOnce()
    {
        if (string.IsNullOrWhiteSpace(statusName)) return;

        if (_iconImg == null)
        {
            // Prefer child named "*icon*"; otherwise first simple Image.
            foreach (var img in GetComponentsInChildren<Image>(true))
            {
                if (!img) continue;
                var n = img.gameObject ? img.gameObject.name : null;
                if (!string.IsNullOrEmpty(n) && n.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _iconImg = img; break;
                }
            }
            if (_iconImg == null)
            {
                foreach (var img in GetComponentsInChildren<Image>(true))
                {
                    if (img && img.type == Image.Type.Simple) { _iconImg = img; break; }
                }
            }
        }

        if (_iconImg)
        {
            var spr = AfflictionsAPI.GetStatusIcon(statusName);
            if (spr != null)
            {
                _iconImg.sprite = spr;
                _iconImg.preserveAspect = true;
                _iconImg.enabled = true;
                Debug.Log($"[MoreAfflictions] Icon applied: '{statusName}'");
            }
        }
    }
}

[HarmonyPatch]
internal static class BarAfflictionDrivePatches
{
    // Called when the stamina bar rebuilds its layout.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BarAffliction), "ChangeAffliction")]
    private static void ChangeAffliction_Postfix(BarAffliction __instance, StaminaBar bar)
    {
        if (!__instance || !bar) return;
        var h = __instance.GetComponent<BarAfflictionCustom>();
        if (!h) return;

        var observed = Character.observedCharacter;
        var ca = observed ? observed.refs?.afflictions : null;
        if (ca == null) return;

        var full = bar.fullBar;
        var minW = bar.minAfflictionWidth;

        float current = ca.GetStatus(h.statusName);  // ← your API should map name→value
        h.size = (full ? full.sizeDelta.x : 0f) * Mathf.Clamp01(current);

        bool active = current > 0.01f;
        if (active && h.size < minW) h.size = minW;

        // Toggle visibility cleanly.
        var go = h.gameObject;
        if (go.activeSelf != active)
        {
            go.SetActive(active);
        }

        // Snap width (UpdateAffliction will smooth).
        if (h.rtf)
        {
            h.rtf.sizeDelta = new Vector2(h.size, h.rtf.sizeDelta.y);
        }

        h.TryApplyIconOnce();
    }

    // Called every frame by the vanilla bar to animate widths.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BarAffliction), "UpdateAffliction")]
    private static void UpdateAffliction_Postfix(BarAffliction __instance, StaminaBar bar)
    {
        if (!__instance || !bar) return;
        var h = __instance.GetComponent<BarAfflictionCustom>();
        if (!h || !h.rtf) return;

        float t = Mathf.Min(Time.deltaTime * 10f, 0.12f);
        float w = Mathf.Lerp(h.rtf.sizeDelta.x, h.size, t);
        h.rtf.sizeDelta = new Vector2(w, h.rtf.sizeDelta.y);
    }
}
