using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MoreAfflictionsPlugin.APIs;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace MoreAfflictionsMod
{
    [BepInPlugin("com.khakixd.moreafflictions", "More Afflictions", "0.2.7")]
    public class MoreAfflictionsPlugin : BaseUnityPlugin
    {
        private Harmony _harmony;

        // Demo config
        private ConfigEntry<bool> _demoEnable;
        private ConfigEntry<string> _demoName;
        private ConfigEntry<float> _demoCap;
        private ConfigEntry<float> _demoStartAmount;
        private ConfigEntry<bool> _demoAutoTick;
        private ConfigEntry<float> _demoTickPerSecond;
        private ConfigEntry<string> _demoHexColor;   // e.g. "#34C3FFFF"

        private void Awake()
        {
            _demoEnable = Config.Bind("Testing.Demo", "Enable", false, "Enable a demo custom affliction for testing.");
            _demoName = Config.Bind("Testing.Demo", "Name", "Demo_Affliction", "Name of the demo custom affliction.");
            _demoCap = Config.Bind("Testing.Demo", "Cap", 1.0f, "Cap for the demo affliction.");
            _demoStartAmount = Config.Bind("Testing.Demo", "StartAmount", 0.25f, "Initial amount to trigger UI.");
            _demoAutoTick = Config.Bind("Testing.Demo", "AutoTick", true, "If true, increases value over time (local HUD).");
            _demoTickPerSecond = Config.Bind("Testing.Demo", "TickPerSecond", 0.02f, "Increase per second.");
            _demoHexColor = Config.Bind("Testing.Demo", "Color", "#34C3FF", "HTML color for demo bar (RGB or RGBA).");

            if (_demoEnable.Value)
                AfflictionsAPI.RegisterStatus(_demoName.Value, _demoCap.Value, null);

            _harmony = new Harmony("com.khakixd.moreafflictions");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            AfflictionsAPI.OnStatusCreated += TryCreateBarForCustomStatus;

            if (_demoEnable.Value)
                StartCoroutine(DemoRoutine());
        }

        private void OnDestroy()
        {
            AfflictionsAPI.OnStatusCreated -= TryCreateBarForCustomStatus;
            _harmony?.UnpatchSelf();
        }

        private void TryCreateBarForCustomStatus(CharacterAfflictions ca, int index, float amount)
        {
            try
            {
                if (Character.observedCharacter == null || Character.observedCharacter.refs == null) return;
                if (!ReferenceEquals(ca, Character.observedCharacter.refs.afflictions)) return;

                var bar = Object.FindFirstObjectByType<StaminaBar>();
                if (bar == null) return;

                string statusName = AfflictionsAPI.GetNameForIndex(index) ?? ("CustomStatus_" + index);
                if (bar.transform.Find("AfflictionBar_" + statusName) != null) return;

                // Pick any existing BarAffliction as visual template
                var template = bar.GetComponentsInChildren<BarAffliction>(true).FirstOrDefault();
                if (template == null) { Logger.LogWarning("[MoreAfflictions] No BarAffliction template found."); return; }

                var cloneGO = Instantiate(template.gameObject, template.transform.parent);
                cloneGO.name = "AfflictionBar_" + statusName;
                cloneGO.transform.SetSiblingIndex(template.transform.GetSiblingIndex());
                cloneGO.SetActive(true);

                // Marker so our Harmony postfixes size this bar
                var helper = cloneGO.AddComponent<BarAfflictionCustom>();
                helper.statusName = statusName;
                helper.rtf = cloneGO.GetComponent<RectTransform>();

                // === TEST: force‑tint the WHOLE hierarchy (parent + all children) ===
                var rgb = ChooseTintRGB(statusName);
                ApplyTintToHierarchy(cloneGO, rgb, preserveAlpha: true);

                // Ensure StaminaBar drives this clone
                bar.afflictions = bar.GetComponentsInChildren<BarAffliction>(true);

                Logger.LogInfo($"[UI] Spawned+tinted custom bar '{statusName}' (idx {index}) amount={amount}");
            }
            catch (System.Exception ex)
            {
                Logger.LogError("Error spawning custom status bar: " + ex);
            }
        }

        private IEnumerator DemoRoutine()
        {
            while (Character.observedCharacter == null) yield return null;

            StaminaBar bar = null;
            while ((bar = Object.FindFirstObjectByType<StaminaBar>()) == null)
                yield return null;

            var ca = Character.observedCharacter.refs != null ? Character.observedCharacter.refs.afflictions : null;
            if (ca != null && !string.IsNullOrEmpty(_demoName.Value))
            {
                if (!ca.AddStatus(_demoName.Value, _demoStartAmount.Value))
                    ca.SetStatus(_demoName.Value, _demoStartAmount.Value);
            }

            if (_demoAutoTick.Value && ca != null && _demoTickPerSecond.Value > 0f)
            {
                for (; ; )
                {
                    if (Character.observedCharacter != null &&
                        ReferenceEquals(ca, Character.observedCharacter.refs.afflictions))
                    {
                        ca.AddStatus(_demoName.Value, _demoTickPerSecond.Value * Time.deltaTime);
                    }
                    yield return null;
                }
            }
        }

        // -------- helpers --------

        // Force‑tint all Images under root. Preserves each Image's alpha.
        private static void ApplyTintToHierarchy(GameObject root, Color rgb, bool preserveAlpha)
        {
            var images = root.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                var c0 = img.color;
                img.color = preserveAlpha ? new Color(rgb.r, rgb.g, rgb.b, c0.a)
                                          : new Color(rgb.r, rgb.g, rgb.b, 1f);
            }
        }

        // RGB only (alpha is preserved per‑Image in ApplyTintToHierarchy)
        private Color ChooseTintRGB(string statusName)
        {
            if (!string.IsNullOrEmpty(_demoName.Value) &&
                string.Equals(statusName, _demoName.Value, System.StringComparison.OrdinalIgnoreCase) &&
                ColorUtility.TryParseHtmlString(_demoHexColor.Value, out var parsed))
            {
                return new Color(parsed.r, parsed.g, parsed.b, 1f);
            }

            unchecked
            {
                int hash = statusName.GetHashCode();
                float hue = ((hash & 0xFFFFFF) / (float)0xFFFFFF);
                Color hsvCol = Color.HSVToRGB(hue, 0.65f, 0.95f);
                hsvCol.a = 1f;
                return hsvCol;
            }
        }
    }
}
