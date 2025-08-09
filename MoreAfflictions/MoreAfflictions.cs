// MoreAfflictionsPlugin.cs
// Loads Harmony patches, guarantees at least one custom status is registered
// very early, and bootstraps UI clones for every custom status.

using System;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using MoreAfflictionsPlugin.APIs; // ← change if your AfflictionsAPI namespace differs

namespace MoreAfflictionsPlugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class MoreAfflictionsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.khakixd.moreafflictions"; // must match any [BepInDependency] elsewhere
        public const string PluginName = "More Afflictions";
        public const string PluginVersion = "0.4.2";                    // numeric only

        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo("[MoreAfflictions] Awake: Harmony patches applied.");

            // Ensure there is at least ONE custom status BEFORE StaminaBar.Start runs.
            // This guarantees a bar to clone and drive so you can see logs immediately.
            EnsureTestStatus();

            DumpRegistry("Awake");

            Debug.Log("[MoreAfflictions] TEST RegisterStatus call — will dump registry immediately");
            var dumpNames = AfflictionsAPI.GetRegisteredCustomNames();
            Debug.Log("[MoreAfflictions] Registry now: " + string.Join(", ", dumpNames));
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { /* ignore */ }
        }

        private static void EnsureTestStatus()
        {
            int tempIdx;
            if (!AfflictionsAPI.TryGetIndex("ThirstTest", out tempIdx))
            {
                var idx = AfflictionsAPI.RegisterStatus("ThirstTest", cap: 1f, onAdded: null, icon: null);
                Debug.Log($"[MoreAfflictions] Registered built-in test status 'ThirstTest' at index={idx}");
            }
            else
            {
                Debug.Log($"[MoreAfflictions] 'ThirstTest' already registered at index={tempIdx}");
            }
        }

        private static void DumpRegistry(string where)
        {
            var names = AfflictionsAPI.GetRegisteredCustomNames();
            Debug.Log($"[MoreAfflictions] Registry dump ({where}): count={names.Count} [{string.Join(", ", names)}]");
            foreach (var n in names)
            {
                if (AfflictionsAPI.TryGetIndex(n, out var i))
                    Debug.Log($"[MoreAfflictions] Name→Index: {n} => {i}");
                else
                    Debug.LogWarning($"[MoreAfflictions] Name→Index FAILED: {n}");
            }
        }
    }

    // ------------------------------------------------------------
    // UI bootstrapper: creates & maintains BarAffliction clones
    // ------------------------------------------------------------
    [HarmonyPatch(typeof(StaminaBar))]
    internal static class StaminaBar_CustomAfflictions
    {
        // BarAffliction has a private field 'type' (CharacterAfflictions.STATUSTYPE)
        private static readonly System.Reflection.FieldInfo F_BarAffliction_Type =
            AccessTools.Field(typeof(BarAffliction), "type");

        [HarmonyPostfix, HarmonyPatch("Start")]
        private static void Start_Post(StaminaBar __instance)
        {
            Debug.Log("[MoreAfflictions] StaminaBar.Start → bootstrap custom bars");
            SafePrune(__instance);
            EnsureCustomAfflictionsPresent(__instance);
            RefreshCache(__instance);
            DumpAfflictions(__instance, "Start_Post");
        }

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
            if ((Time.frameCount & 63) == 0) DumpAfflictions(__instance, "Update_Pre");
        }

        private static void SafePrune(StaminaBar bar)
        {
            if (!bar) return;
            var src = bar.afflictions ?? Array.Empty<BarAffliction>();
            var clean = src.Where(a => a && !a.Equals(null)).ToArray();
            if (!ReferenceEquals(src, clean))
            {
                Debug.Log($"[MoreAfflictions] Pruned null BarAfflictions (before={src.Length}, after={clean.Length})");
                bar.afflictions = clean;
            }
        }

        private static void RefreshCache(StaminaBar bar)
        {
            if (!bar) return;
            var now = bar.GetComponentsInChildren<BarAffliction>(true);
            if (!ReferenceEquals(now, bar.afflictions))
            {
                Debug.Log($"[MoreAfflictions] RefreshCache: bar.afflictions children={now.Length}");
                bar.afflictions = now;
            }
        }

        private static BarAffliction PickTemplate(StaminaBar bar)
        {
            if (!bar) return null;
            BarAffliction tpl = null;

            if (bar.afflictions != null && bar.afflictions.Length > 0)
                tpl = bar.afflictions.FirstOrDefault(a => a && !a.Equals(null));

            if (!tpl) tpl = bar.GetComponentInChildren<BarAffliction>(true);

            if (tpl) Debug.Log($"[MoreAfflictions] PickTemplate: using '{tpl.gameObject.name}'");
            else Debug.LogError("[MoreAfflictions] PickTemplate: FAILED (no BarAffliction under StaminaBar)");
            return tpl;
        }

        private static void EnsureCustomAfflictionsPresent(StaminaBar bar)
        {
            if (!bar) return;

            var template = PickTemplate(bar);
            if (!template) return;

            var customNames = AfflictionsAPI.GetRegisteredCustomNames();
            if (customNames == null || customNames.Count == 0)
            {
                Debug.Log("[MoreAfflictions] No custom statuses registered yet.");
                return;
            }

            int created = 0, present = 0, skipped = 0;

            foreach (var name in customNames)
            {
                if (!AfflictionsAPI.TryGetIndex(name, out var index) || index < 0)
                {
                    Debug.LogWarning($"[MoreAfflictions] '{name}' has no valid index (TryGetIndex failed).");
                    skipped++;
                    continue;
                }

                if (HasEntryFor(bar, index))
                {
                    present++;
                    continue;
                }

                CreateClone(bar, template, name, index);
                created++;
            }

            Debug.Log($"[MoreAfflictions] EnsureCustomAfflictionsPresent: names={customNames.Count} created={created} present={present} skipped={skipped}");
        }

        private static bool HasEntryFor(StaminaBar bar, int statusIndex)
        {
            // Prefer checking our helper (most reliable), then fallback to private field.
            var all = bar.GetComponentsInChildren<BarAffliction>(true);
            foreach (var a in all)
            {
                if (!a) continue;

                var helper = a.GetComponent<BarAfflictionCustom>();
                if (helper && helper.statusIndex == statusIndex) return true;

                if (F_BarAffliction_Type != null)
                {
                    try
                    {
                        var val = (CharacterAfflictions.STATUSTYPE)F_BarAffliction_Type.GetValue(a);
                        if ((int)val == statusIndex) return true;
                    }
                    catch { /* ignore */ }
                }
            }
            return false;
        }

        private static void CreateClone(StaminaBar bar, BarAffliction template, string statusName, int statusIndex)
        {
            try
            {
                var parent = template.transform.parent;
                var go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
                go.name = "Affliction_Custom_" + statusName;

                // Render after template so it appears to the right.
                go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

                // Set underlying vanilla enum field (if game code ever reads it).
                var vanilla = go.GetComponent<BarAffliction>();
                if (vanilla && F_BarAffliction_Type != null)
                    F_BarAffliction_Type.SetValue(vanilla, (CharacterAfflictions.STATUSTYPE)statusIndex);

                // Add our driver.
                var helper = go.GetComponent<BarAfflictionCustom>();
                if (!helper) helper = go.AddComponent<BarAfflictionCustom>();
                helper.statusName = statusName;
                helper.statusIndex = statusIndex;
                helper.rtf = go.GetComponent<RectTransform>();
                helper.TryApplyIconOnce();

                // 🔹 Apply deterministic colour to every Image in this clone hierarchy
                Color afflictionColor = GetDeterministicColor(statusName);
                ApplyColorToHierarchy(go.transform, afflictionColor);

                // Start inactive; driver toggles when value > 0.
                go.SetActive(false);

                Debug.Log($"[MoreAfflictions] Created clone for '{statusName}' at index={statusIndex} color={afflictionColor}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MoreAfflictions] CreateClone failed for '{statusName}': {ex}");
            }
        }

        /// <summary>
        /// Generates a deterministic, bright colour from a string by hashing it.
        /// </summary>
        private static Color GetDeterministicColor(string input)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in input)
                    hash = hash * 31 + c;

                // Map hash to 0..1 range for hue
                float hue = (hash & 0xFFFFFF) / (float)0xFFFFFF;
                float saturation = 0.8f;
                float value = 0.9f;
                return Color.HSVToRGB(hue, saturation, value);
            }
        }

        /// <summary>
        /// Loops through the given transform and all its descendants, setting any Image's colour.
        /// </summary>
        private static void ApplyColorToHierarchy(Transform root, Color rgbColor)
        {
            foreach (var img in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;
                float originalAlpha = img.color.a; // preserve transparency
                Color newColor = new Color(rgbColor.r, rgbColor.g, rgbColor.b, originalAlpha);
                img.color = newColor;
            }
        }

        private static void DumpAfflictions(StaminaBar bar, string where)
        {
            if (!bar) return;
            try
            {
                var arr = bar.GetComponentsInChildren<BarAffliction>(true);
                int total = arr.Length, custom = 0;

                foreach (var a in arr)
                {
                    if (!a) continue;
                    var h = a.GetComponent<BarAfflictionCustom>();
                    if (h) custom++;
                }

                Debug.Log($"[MoreAfflictions] {where}: total={total} customDriven={custom}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MoreAfflictions] DumpAfflictions error: " + ex);
            }
        }
    }
}
