// MoreAfflictions_UIBootstrap.cs
// Ensures each custom status has a proper BarAffliction UI instance bound to it.

using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using MoreAfflictionsPlugin.APIs;

namespace MoreAfflictionsPlugin
{
    [BepInPlugin("com.khakixd.moreafflictions", "More Afflictions", "0.4.0")]
    public sealed class MoreAfflictionsPlugin : BaseUnityPlugin
    {
        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony("com.khakixd.moreafflictions");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo("[MoreAfflictions] UIBootstrap loaded.");
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
        }
    }

    /// <summary>
    /// Finds/creates BarAffliction clones for registered custom statuses.
    /// We do NOT patch BarAffliction logic at all; we just supply a correct UI entry.
    /// </summary>
    [HarmonyPatch(typeof(StaminaBar))]
    internal static class StaminaBar_CustomAfflictions
    {
        // reflection handles (field name is "type" in the vanilla BarAffliction)
        private static readonly FieldInfo F_BarAffliction_Type =
            AccessTools.Field(typeof(BarAffliction), "type"); // type: CharacterAfflictions.STATUSTYPE

        // Some builds use "rtf" as the width driver; we don't set it here, the clone keeps it.
        // We only ensure the item exists and is added to StaminaBar.afflictions.

        // Run once when the StaminaBar gets its children cached
        [HarmonyPostfix, HarmonyPatch("Start")]
        private static void Start_Post(StaminaBar __instance)
        {
            SafePrune(__instance);
            EnsureCustomAfflictionsPresent(__instance);
            RefreshCache(__instance);
        }

        // Make sure array stays clean + present even if scene swaps
        [HarmonyPrefix, HarmonyPatch("ChangeBar")]
        private static void ChangeBar_Pre(StaminaBar __instance)
        {
            SafePrune(__instance);
            EnsureCustomAfflictionsPresent(__instance);
            RefreshCache(__instance);
        }

        [HarmonyPrefix, HarmonyPatch("Update")]
        private static void Update_Pre(StaminaBar __instance)
        {
            SafePrune(__instance);
        }

        private static void SafePrune(StaminaBar bar)
        {
            if (bar == null || bar.Equals(null)) return;
            if (bar.afflictions == null) return;

            var clean = bar.afflictions.Where(a => a && !a.Equals(null)).ToArray();
            if (!ReferenceEquals(clean, bar.afflictions))
                bar.afflictions = clean;
        }

        private static void RefreshCache(StaminaBar bar)
        {
            // Make sure StaminaBar.afflictions includes our clones
            var now = bar.GetComponentsInChildren<BarAffliction>(true);
            if (!ReferenceEquals(now, bar.afflictions))
                bar.afflictions = now;
        }

        private static void EnsureCustomAfflictionsPresent(StaminaBar bar)
        {
            if (bar == null || bar.Equals(null)) return;

            // Need a template
            var template = PickTemplate(bar);
            if (template == null) return;

            // Iterate all known custom statuses
            var customNames = AfflictionsAPI.GetRegisteredCustomNames();
            if (customNames == null || customNames.Count == 0) return;

            foreach (var name in customNames)
            {
                int index;
                if (!AfflictionsAPI.TryGetIndex(name, out index) || index < 0)
                    continue;

                // Already have a BarAffliction with that index?
                if (HasEntryFor(bar, index)) continue;

                // Create one
                CreateClone(bar, template, name, index);
            }
        }

        private static BarAffliction PickTemplate(StaminaBar bar)
        {
            // Grab any existing vanilla BarAffliction (e.g., the first one)
            BarAffliction tpl = null;

            if (bar.afflictions != null && bar.afflictions.Length > 0)
            {
                tpl = bar.afflictions.FirstOrDefault(a => a && !a.Equals(null));
            }
            if (tpl == null)
            {
                // Fallback: search hierarchy
                tpl = bar.GetComponentInChildren<BarAffliction>(true);
            }

            return tpl;
        }

        private static bool HasEntryFor(StaminaBar bar, int statusIndex)
        {
            if (bar.afflictions == null) return false;

            for (int i = 0; i < bar.afflictions.Length; i++)
            {
                var a = bar.afflictions[i];
                if (!a || a.Equals(null)) continue;

                try
                {
                    var val = (CharacterAfflictions.STATUSTYPE)F_BarAffliction_Type.GetValue(a);
                    if ((int)val == statusIndex) return true;
                }
                catch { }
            }
            return false;
        }

        private static void CreateClone(StaminaBar bar, BarAffliction template, string statusName, int statusIndex)
        {
            try
            {
                var parent = template.transform.parent;
                var cloneGo = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
                cloneGo.name = "Affliction_Custom_" + statusName;

                // Make it render after the template (to the right)
                cloneGo.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

                // Bind the new status index
                var clone = cloneGo.GetComponent<BarAffliction>();
                if (clone != null && F_BarAffliction_Type != null)
                {
                    F_BarAffliction_Type.SetValue(clone, (CharacterAfflictions.STATUSTYPE)statusIndex);
                }

                // Start disabled; vanilla will enable it based on current value in ChangeAffliction
                cloneGo.SetActive(false);
            }
            catch (Exception ex)
            {
                Debug.LogError("[MoreAfflictions] Failed to create UI clone for '" + statusName + "': " + ex);
            }
        }
    }
}
