// MoreAfflictions\APIs\AfflictionsAPI.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace MoreAfflictionsPlugin.APIs
{
    /// <summary>
    /// Generic modding API for custom afflictions.
    /// - Deterministic, multiplayer-safe registry (alphabetical finalisation -> stable indices).
    /// - RegisterStatus(name, cap, onAdded)
    /// - Global OnStatusCreated (fires when a status first becomes > 0 on a character)
    /// - Optional UI factory per status
    /// - Name-based extension helpers (Get/Set/Add/Subtract)
    /// - Harmony patches expand arrays and respect custom caps
    /// </summary>
    public static class AfflictionsAPI
    {
        // Fired when a (custom) status first becomes > 0 on a character
        public static event Action<CharacterAfflictions, int, float> OnStatusCreated;

        // Optional: per-status UI factory (index -> factory)
        public delegate void StatusUIFactory(CharacterAfflictions ca, int index, float amount);
        private static readonly Dictionary<int, StatusUIFactory> _uiFactoriesByIndex = new Dictionary<int, StatusUIFactory>();
        public static void RegisterUIFactory(string name, StatusUIFactory factory)
        {
            if (factory == null) throw new ArgumentNullException("factory");
            int idx;
            if (!TryGetIndex(name, out idx)) throw new ArgumentException("Unknown status: " + name);
            lock (_lockObj) _uiFactoriesByIndex[idx] = factory;
        }

        // ===== Registry (finalised alphabetically; MP safe) =====
        private static readonly object _lockObj = new object();

        private class CustomStatus
        {
            public string Name;
            public float Cap;
            public Action<CharacterAfflictions, float> OnAdded;
        }

        private static readonly List<CustomStatus> _pending = new List<CustomStatus>();
        private static Dictionary<string, int> _nameToIndexFinal;
        private static List<CustomStatus> _finalOrder;
        private static bool _finalised;
        private static string _signature;

        private static int _baseCount = -1;
        private static int BaseCount
        {
            get
            {
                if (_baseCount < 0)
                    _baseCount = Enum.GetNames(typeof(CharacterAfflictions.STATUSTYPE)).Length;
                return _baseCount;
            }
        }

        internal static int TotalCount
        {
            get
            {
                EnsureFinalised();
                lock (_lockObj) return BaseCount + (_finalOrder != null ? _finalOrder.Count : 0);
            }
        }

        public static int RegisterStatus(string name, float cap, Action<CharacterAfflictions, float> onAdded)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name");
            lock (_lockObj)
            {
                if (_finalised)
                {
                    Debug.LogWarning("[MoreAfflictions] RegisterStatus after finalise; ignoring: " + name);
                    int idxExisting;
                    return TryGetIndex(name, out idxExisting) ? idxExisting : -1;
                }

                // de-dupe (case-insensitive)
                for (int i = 0; i < _pending.Count; i++)
                    if (string.Equals(_pending[i].Name, name, StringComparison.OrdinalIgnoreCase))
                        return -1;

                _pending.Add(new CustomStatus
                {
                    Name = name,
                    Cap = Mathf.Max(0f, cap),
                    OnAdded = onAdded
                });

                // index unknown until finalisation
                return -1;
            }
        }

        public static bool TryGetIndex(string name, out int index)
        {
            EnsureFinalised();
            lock (_lockObj)
            {
                if (_nameToIndexFinal != null && _nameToIndexFinal.TryGetValue(name, out index))
                    return true;
            }

            // fall back to vanilla enum
            string[] names = Enum.GetNames(typeof(CharacterAfflictions.STATUSTYPE));
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        public static string GetNameForIndex(int index)
        {
            EnsureFinalised();
            if (index < 0) return null;

            if (index < BaseCount)
            {
                var names = Enum.GetNames(typeof(CharacterAfflictions.STATUSTYPE));
                return index < names.Length ? names[index] : null;
            }

            int off = index - BaseCount;
            lock (_lockObj)
            {
                if (_finalOrder != null && off >= 0 && off < _finalOrder.Count)
                    return _finalOrder[off].Name;
            }
            return null;
        }

        internal static float GetCapOr(float fallback, int index)
        {
            EnsureFinalised();
            if (index < BaseCount) return fallback;
            int off = index - BaseCount;
            lock (_lockObj)
            {
                if (_finalOrder != null && off >= 0 && off < _finalOrder.Count)
                    return _finalOrder[off].Cap;
            }
            return fallback;
        }

        internal static void InvokeOnAdded(CharacterAfflictions ca, int index, float amount)
        {
            EnsureFinalised();
            int off = index - BaseCount;
            if (off < 0) return;
            CustomStatus cs = null;
            lock (_lockObj)
            {
                if (_finalOrder != null && off >= 0 && off < _finalOrder.Count)
                    cs = _finalOrder[off];
            }
            if (cs != null && cs.OnAdded != null)
            {
                try { cs.OnAdded(ca, amount); }
                catch (Exception ex) { Debug.LogError("MoreAfflictions OnAdded error: " + ex); }
            }
        }

        internal static void RaiseStatusCreated(CharacterAfflictions ca, int index, float amount)
        {
            EnsureFinalised();
            if (index >= BaseCount)
            {
                try
                {
                    InvokeOnAdded(ca, index, amount);

                    StatusUIFactory factory;
                    lock (_lockObj)
                    {
                        if (_uiFactoriesByIndex.TryGetValue(index, out factory) && factory != null)
                            factory(ca, index, amount);
                    }

                    var evt = OnStatusCreated;
                    if (evt != null) evt(ca, index, amount);
                }
                catch (Exception ex)
                {
                    Debug.LogError("MoreAfflictions RaiseStatusCreated error: " + ex);
                }
            }
        }

        internal static void EnsureFinalised()
        {
            if (_finalised) return;
            lock (_lockObj)
            {
                if (_finalised) return;

                var ordered = _pending
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _finalOrder = ordered;
                _nameToIndexFinal = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < ordered.Count; i++)
                    _nameToIndexFinal[ordered[i].Name] = BaseCount + i;

                var sb = new StringBuilder();
                sb.Append(BaseCount).Append("|");
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(ordered[i].Name);
                }
                _signature = sb.ToString();
                _finalised = true;

                Debug.Log("[MoreAfflictions] Registry finalised. BaseCount=" + BaseCount +
                          ", CustomCount=" + ordered.Count +
                          ", Signature=" + _signature);
            }
        }

        // ---------- Extensions ----------
        public static float GetStatus(this CharacterAfflictions ca, string name)
        {
            int idx; return TryGetIndex(name, out idx)
                ? ca.GetCurrentStatus((CharacterAfflictions.STATUSTYPE)idx)
                : 0f;
        }

        public static void SetStatus(this CharacterAfflictions ca, string name, float value)
        {
            int idx; if (TryGetIndex(name, out idx))
                ca.SetStatus((CharacterAfflictions.STATUSTYPE)idx, value);
        }

        public static bool AddStatus(this CharacterAfflictions ca, string name, float amount)
        {
            int idx; return TryGetIndex(name, out idx) &&
                ca.AddStatus((CharacterAfflictions.STATUSTYPE)idx, amount, false);
        }

        public static void SubtractStatus(this CharacterAfflictions ca, string name, float amount)
        {
            int idx; if (TryGetIndex(name, out idx))
                ca.SubtractStatus((CharacterAfflictions.STATUSTYPE)idx, amount, false);
        }
    }

    // ===================== Harmony Patches =====================
    [HarmonyPatch]
    internal static class CharacterAfflictionsPatches
    {
        private static readonly FieldInfo F_Current =
            AccessTools.Field(typeof(CharacterAfflictions), "currentStatuses");
        private static readonly FieldInfo F_Inc =
            AccessTools.Field(typeof(CharacterAfflictions), "currentIncrementalStatuses");
        private static readonly FieldInfo F_Dec =
            AccessTools.Field(typeof(CharacterAfflictions), "currentDecrementalStatuses");
        private static readonly FieldInfo F_Last =
            AccessTools.Field(typeof(CharacterAfflictions), "lastAddedStatus");
        private static readonly FieldInfo F_LastInc =
            AccessTools.Field(typeof(CharacterAfflictions), "lastAddedIncrementalStatus");

        // Ensure registry is finalised BEFORE vanilla arrays are built
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CharacterAfflictions), "InitStatusArrays")]
        private static void InitStatusArrays_Prefix()
        {
            try { AfflictionsAPI.EnsureFinalised(); }
            catch (Exception ex) { Debug.LogError("MoreAfflictions EnsureFinalised prefix: " + ex); }
        }

        // Expand arrays to include custom statuses
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CharacterAfflictions), "InitStatusArrays")]
        private static void InitStatusArrays_Postfix(CharacterAfflictions __instance)
        {
            try
            {
                int total = AfflictionsAPI.TotalCount;
                float[] cur = (float[])F_Current.GetValue(__instance);
                if (cur == null || cur.Length >= total) return;

                float[] inc = (float[])F_Inc.GetValue(__instance);
                float[] dec = (float[])F_Dec.GetValue(__instance);
                float[] last = (float[])F_Last.GetValue(__instance);
                float[] lastInc = (float[])F_LastInc.GetValue(__instance);

                float[] n_cur = new float[total]; Array.Copy(cur, n_cur, cur.Length);
                float[] n_inc = new float[total]; if (inc != null) Array.Copy(inc, n_inc, inc.Length);
                float[] n_dec = new float[total]; if (dec != null) Array.Copy(dec, n_dec, dec.Length);
                float[] n_last = new float[total]; if (last != null) Array.Copy(last, n_last, last.Length);
                float[] n_lastInc = new float[total]; if (lastInc != null) Array.Copy(lastInc, n_lastInc, lastInc.Length);

                F_Current.SetValue(__instance, n_cur);
                F_Inc.SetValue(__instance, n_inc);
                F_Dec.SetValue(__instance, n_dec);
                F_Last.SetValue(__instance, n_last);
                F_LastInc.SetValue(__instance, n_lastInc);
            }
            catch (Exception ex) { Debug.LogError("MoreAfflictions InitStatusArrays postfix: " + ex); }
        }

        // Respect custom caps
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.GetStatusCap))]
        private static void GetStatusCap_Postfix(ref float __result, CharacterAfflictions.STATUSTYPE type)
        {
            try { __result = AfflictionsAPI.GetCapOr(__result, (int)type); }
            catch (Exception ex) { Debug.LogError("MoreAfflictions GetStatusCap postfix: " + ex); }
        }

        // Detect 0 -> >0 in AddStatus
        [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
        private static class AddStatus_Patch
        {
            static void Prefix(CharacterAfflictions __instance,
                               [HarmonyArgument(0)] CharacterAfflictions.STATUSTYPE statusType,
                               out float __state)
                => __state = GetCurrent(__instance, (int)statusType);

            static void Postfix(CharacterAfflictions __instance,
                                [HarmonyArgument(0)] CharacterAfflictions.STATUSTYPE statusType,
                                bool __result,
                                float __state)
            {
                if (!__result) return;
                int idx = (int)statusType;
                float now = GetCurrent(__instance, idx);
                if (__state <= 0f && now > 0f)
                    AfflictionsAPI.RaiseStatusCreated(__instance, idx, now);
            }
        }

        // Detect 0 -> >0 in SetStatus
        [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.SetStatus))]
        private static class SetStatus_Patch
        {
            static void Prefix(CharacterAfflictions __instance,
                               [HarmonyArgument(0)] CharacterAfflictions.STATUSTYPE statusType,
                               out float __state)
                => __state = GetCurrent(__instance, (int)statusType);

            static void Postfix(CharacterAfflictions __instance,
                                [HarmonyArgument(0)] CharacterAfflictions.STATUSTYPE statusType,
                                float __state)
            {
                int idx = (int)statusType;
                float now = GetCurrent(__instance, idx);
                if (__state <= 0f && now > 0f)
                    AfflictionsAPI.RaiseStatusCreated(__instance, idx, now);
            }
        }

        private static float GetCurrent(CharacterAfflictions ca, int idx)
        {
            try
            {
                var cur = (float[])F_Current.GetValue(ca);
                return (cur != null && idx >= 0 && idx < cur.Length) ? cur[idx] : 0f;
            }
            catch { return 0f; }
        }
    }
}
