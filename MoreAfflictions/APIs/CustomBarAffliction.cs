// CustomBarAffliction.cs
// Drives custom affliction bars cloned from the vanilla BarAffliction.
// Includes strong Unity-style null checks and one-time icon application.

using HarmonyLib;
using MoreAfflictionsPlugin.APIs;
using System;
using UnityEngine;
using UnityEngine.UI;

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
        get => rtf ? rtf.sizeDelta.x : 0f;
        set { if (rtf) rtf.sizeDelta = new Vector2(value, rtf.sizeDelta.y); }
    }

    // Cache the icon image once
    private Image _iconImg;
    public void TryApplyIconOnce()
    {
        if (!this || string.IsNullOrEmpty(statusName)) return;

        if (_iconImg == null)
        {
            var imgs = GetComponentsInChildren<Image>(true);
            foreach (var img in imgs)
            {
                if (!img) continue;
                var n = img.gameObject ? img.gameObject.name : null;
                if (!string.IsNullOrEmpty(n) && n.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _iconImg = img;
                    break;
                }
            }
            if (_iconImg == null)
            {
                foreach (var img in imgs)
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
            }
        }
    }
}

[HarmonyPatch]
internal static class BarAfflictionDrivePatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BarAffliction), "ChangeAffliction")]
    private static void ChangeAffliction_Postfix(BarAffliction __instance, StaminaBar bar)
    {
        if (!__instance || !bar) return;

        var helper = __instance.GetComponent<BarAfflictionCustom>();
        if (!helper) return;

        var observed = Character.observedCharacter;
        var ca = observed ? observed.refs?.afflictions : null;
        if (ca == null) return;

        var full = bar.fullBar;
        var minWidth = bar.minAfflictionWidth;

        float current = ca.GetStatus(helper.statusName);
        helper.size = (full ? full.sizeDelta.x : 0f) * Mathf.Clamp01(current);

        try
        {
            if (current > 0.01f)
            {
                if (helper.size < minWidth) helper.size = minWidth;
                if (helper && helper.gameObject && !helper.gameObject.activeSelf)
                    helper.gameObject.SetActive(true);
            }
            else
            {
                if (helper && helper.gameObject && helper.gameObject.activeSelf)
                    helper.gameObject.SetActive(false);
            }
        }
        catch { /* ignore one-frame teardown races */ }

        if (helper.rtf)
            helper.rtf.sizeDelta = new Vector2(helper.size, helper.rtf.sizeDelta.y);

        helper.TryApplyIconOnce();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BarAffliction), "UpdateAffliction")]
    private static void UpdateAffliction_Postfix(BarAffliction __instance, StaminaBar bar)
    {
        if (!__instance || !bar) return;

        var helper = __instance.GetComponent<BarAfflictionCustom>();
        if (!helper) return;

        var rt = helper.rtf;
        if (!rt) return;

        float t = Mathf.Min(Time.deltaTime * 10f, 0.1f);
        float w = Mathf.Lerp(rt.sizeDelta.x, helper.size, t);
        rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
    }
}
