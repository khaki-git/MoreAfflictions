using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MoreAfflictionsPlugin.APIs;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MoreAfflictionsMod
{
    [BepInPlugin("com.khakixd.moreafflictions", "More Afflictions", "0.2.8")]
    public class MoreAfflictionsPlugin : BaseUnityPlugin
    {
        private Harmony _harmony;

        // Demo config (unchanged)
        private ConfigEntry<bool> _demoEnable;
        private ConfigEntry<string> _demoName;
        private ConfigEntry<float> _demoCap;
        private ConfigEntry<float> _demoStartAmount;
        private ConfigEntry<bool> _demoAutoTick;
        private ConfigEntry<float> _demoTickPerSecond;
        private ConfigEntry<string> _demoHexColor;

        // Weak registry to avoid recreating UI if it already exists
        private static readonly Dictionary<int, HashSet<string>> _barNamesPerStaminaBar =
            new Dictionary<int, HashSet<string>>();


        private void Awake()
        {
            _demoEnable = Config.Bind("Testing.Demo", "Enable", true, "Enable a demo custom affliction for testing.");
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

            AfflictionsAPI.StatusAdded += OnCustomStatusAddedFirstTime;

            // Clear weak registry on scene loads so we never point at destroyed objects
            SceneManager.sceneLoaded += (_, __) => _barNamesPerStaminaBar.Clear();

            if (_demoEnable.Value)
                StartCoroutine(DemoRoutine());
        }

        private void OnDestroy()
        {
            AfflictionsAPI.StatusAdded -= OnCustomStatusAddedFirstTime;
            SceneManager.sceneLoaded -= (_, __) => _barNamesPerStaminaBar.Clear();
            _harmony?.UnpatchSelf();
        }

        private void OnCustomStatusAddedFirstTime(CharacterAfflictions ca, int index, float amount)
        {
            // Only for the local HUD
            if (Character.observedCharacter == null || Character.observedCharacter.refs == null) return;
            if (!ReferenceEquals(ca, Character.observedCharacter.refs.afflictions)) return;

            var bar = Object.FindFirstObjectByType<StaminaBar>();
            if (bar == null) return;

            string statusName = AfflictionsAPI.GetNameForIndex(index) ?? ("CustomStatus_" + index);

            // 1) Try to find an existing custom bar for this status anywhere under StaminaBar (active or inactive)
            var existing = FindAllCustomBars(bar, statusName);
            if (existing.Count > 0)
            {
                // 1a) Self-heal if there are duplicates
                for (int i = 1; i < existing.Count; i++)
                {
                    if (existing[i] != null)
                        Object.Destroy(existing[i].gameObject);
                }
                Track(bar, statusName);
                return; // reuse the one we have; nothing else to do
            }

            // 2) Build a new one from a vanilla template
            var template = bar.GetComponentsInChildren<BarAffliction>(true).FirstOrDefault();
            if (template == null)
            {
                Logger.LogWarning("[MoreAfflictions] No BarAffliction template found under StaminaBar; cannot spawn custom bar.");
                return;
            }

            var cloneGO = Instantiate(template.gameObject, template.transform.parent);
            cloneGO.name = "AfflictionBar_" + statusName;
            cloneGO.transform.SetSiblingIndex(template.transform.GetSiblingIndex());
            cloneGO.SetActive(true);

            var helper = cloneGO.AddComponent<BarAfflictionCustom>();
            helper.statusName = statusName;
            helper.rtf = cloneGO.GetComponent<RectTransform>();

            // Tint pass: set RGB on whole hierarchy, preserve each image's alpha
            var rgb = ChooseTintRGB(statusName);
            foreach (var img in cloneGO.GetComponentsInChildren<Image>(true))
            {
                var c0 = img.color;
                img.color = new Color(rgb.r, rgb.g, rgb.b, c0.a);
            }

            // Refresh StaminaBar's cache
            bar.afflictions = bar.GetComponentsInChildren<BarAffliction>(true);

            Track(bar, statusName);
            Logger.LogInfo($"[UI] Spawned custom bar for '{statusName}' amount={amount}");
        }

        // --- Registry helpers ---
        private static void Track(StaminaBar bar, string statusName)
        {
            if (bar == null || string.IsNullOrEmpty(statusName)) return;
            int id = bar.GetInstanceID();
            if (!_barNamesPerStaminaBar.TryGetValue(id, out var set))
            {
                set = new HashSet<string>();
                _barNamesPerStaminaBar[id] = set;
            }
            set.Add(statusName);
        }

        private static List<BarAfflictionCustom> FindAllCustomBars(StaminaBar bar, string statusName)
        {
            var all = bar.GetComponentsInChildren<BarAfflictionCustom>(true);
            var list = new List<BarAfflictionCustom>(2);
            foreach (var bac in all)
            {
                if (bac != null && string.Equals(bac.statusName, statusName, System.StringComparison.OrdinalIgnoreCase))
                    list.Add(bac);
            }
            return list;
        }

        // --- Demo routine (unchanged) ---
        private System.Collections.IEnumerator DemoRoutine()
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

        // --- Color helpers ---
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
