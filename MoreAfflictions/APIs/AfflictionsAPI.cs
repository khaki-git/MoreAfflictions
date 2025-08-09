// MoreAfflictions\APIs\AfflictionsAPI.cs
// C# 7.3 compatible

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MoreAfflictionsPlugin.APIs
{
    /// <summary>
    /// Minimal custom-affliction API:
    ///  - Register custom statuses by name (each gets a virtual index after the vanilla enum).
    ///  - Optional per-status cap and "on added" callback.
    ///  - Optional per-status ICON (Sprite).
    ///  - Name-based extension helpers (Get/Set/Add/Subtract).
    ///  
    /// Internals:
    ///  - Patches CharacterAfflictions to expand arrays and honour caps.
    ///  - Postfixes AddStatus(...) to invoke custom callbacks and raise a StatusAdded event.
    /// </summary>
    public static class AfflictionsAPI
    {
        private static readonly object _lockObj = new object();

        // Registered custom statuses (order is the virtual index offset from BaseCount)
        private static readonly List<CustomStatus> _customs = new List<CustomStatus>();

        // Name -> virtual index
        private static readonly Dictionary<string, int> _nameToIndex =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Optional icon per status name
        private static readonly Dictionary<string, Sprite> _nameToIcon =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        // Cache the base enum length (vanilla statuses)
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
            get { lock (_lockObj) return BaseCount + _customs.Count; }
        }

        /// <summary>
        /// Raised after CharacterAfflictions.AddStatus(...) finishes for ANY status (vanilla or custom).
        /// Signature: (CharacterAfflictions instance, status index, amount).
        /// </summary>
        public static event Action<CharacterAfflictions, int, float> StatusAdded;

        /// <summary>
        /// Register a custom status. If the name already exists, returns its index.
        /// cap applies only to the custom cap in GetStatusCap(). onAdded is invoked when that status gets added.
        /// </summary>
        public static int RegisterStatus(string name, float cap, Action<CharacterAfflictions, float> onAdded)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name");

            lock (_lockObj)
            {
                int idx;
                if (_nameToIndex.TryGetValue(name, out idx))
                    return idx;

                idx = BaseCount + _customs.Count;
                _customs.Add(new CustomStatus
                {
                    Name = name,
                    Index = idx,
                    Cap = Mathf.Max(0f, cap),
                    OnAdded = onAdded
                });
                _nameToIndex[name] = idx;
                return idx;
            }
        }

        /// <summary>Assign or replace an icon Sprite for a registered (or soon-to-be registered) status name.</summary>
        public static void SetStatusIcon(string name, Sprite sprite)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_lockObj) _nameToIcon[name] = sprite;
        }

        /// <summary>Internal: get an icon for a given status NAME (returns null if none set).</summary>
        internal static Sprite GetStatusIcon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (_lockObj)
            {
                Sprite s;
                return _nameToIcon.TryGetValue(name, out s) ? s : null;
            }
        }

        /// <summary>Try to resolve a status NAME to an index (handles both vanilla enum names and custom names).</summary>
        public static bool TryGetIndex(string name, out int index)
        {
            lock (_lockObj)
            {
                if (_nameToIndex.TryGetValue(name, out index))
                    return true;
            }

            // Try vanilla enum names (case-insensitive)
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

        /// <summary>Resolve index → name (vanilla or custom). Returns null if unknown.</summary>
        public static string GetNameForIndex(int index)
        {
            if (index < 0) return null;
            if (index < BaseCount)
            {
                try { return Enum.GetName(typeof(CharacterAfflictions.STATUSTYPE), index); }
                catch { return null; }
            }

            CustomStatus cs = GetCustomByIndex(index);
            return cs != null ? cs.Name : null;
        }

        // --------- Internal helpers used by patches/UI ---------

        internal static float GetCapOr(float fallback, int index)
        {
            if (index < BaseCount) return fallback;
            CustomStatus cs = GetCustomByIndex(index);
            return cs != null ? cs.Cap : fallback;
        }

        internal static void InvokeOnAdded(CharacterAfflictions ca, int index, float amount)
        {
            try
            {
                CustomStatus cs = GetCustomByIndex(index);
                if (cs != null && cs.OnAdded != null)
                    cs.OnAdded(ca, amount);
            }
            catch (Exception ex) { Debug.LogError("MoreAfflictions OnAdded callback error: " + ex); }

            // Public event for anyone (UI spawner etc.)
            try
            {
                if (StatusAdded != null)
                    StatusAdded(ca, index, amount);
            }
            catch (Exception ex) { Debug.LogError("MoreAfflictions StatusAdded event error: " + ex); }
        }

        private static CustomStatus GetCustomByIndex(int index)
        {
            int offset = index - BaseCount;
            if (offset < 0) return null;
            lock (_lockObj)
            {
                return (offset >= 0 && offset < _customs.Count) ? _customs[offset] : null;
            }
        }

        private class CustomStatus
        {
            public string Name;
            public int Index;
            public float Cap;
            public Action<CharacterAfflictions, float> OnAdded;
        }

        // ---------- Extension helpers (name-based) ----------
        public static float GetStatus(this CharacterAfflictions ca, string name)
        {
            int idx;
            return TryGetIndex(name, out idx)
                ? ca.GetCurrentStatus((CharacterAfflictions.STATUSTYPE)idx)
                : 0f;
        }

        public static void SetStatus(this CharacterAfflictions ca, string name, float value)
        {
            int idx;
            if (TryGetIndex(name, out idx))
                ca.SetStatus((CharacterAfflictions.STATUSTYPE)idx, value);
        }

        public static bool AddStatus(this CharacterAfflictions ca, string name, float amount)
        {
            int idx;
            return TryGetIndex(name, out idx) &&
                   ca.AddStatus((CharacterAfflictions.STATUSTYPE)idx, amount, false);
        }

        public static void SubtractStatus(this CharacterAfflictions ca, string name, float amount)
        {
            int idx;
            if (TryGetIndex(name, out idx))
                ca.SubtractStatus((CharacterAfflictions.STATUSTYPE)idx, amount, false);
        }
    }

    // --------- Harmony patches ----------
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

        // Expand arrays to fit customs
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

        // Apply custom caps
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.GetStatusCap))]
        private static void GetStatusCap_Postfix(ref float __result, CharacterAfflictions.STATUSTYPE type)
        {
            try { __result = AfflictionsAPI.GetCapOr(__result, (int)type); }
            catch (Exception ex) { Debug.LogError("MoreAfflictions GetStatusCap postfix: " + ex); }
        }

        // Notify when a status is added (drives OnAdded + StatusAdded event)
        // Signature must match the game's: AddStatus(STATUSTYPE statusType, float amount, bool fromRPC)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus),
            new Type[] { typeof(CharacterAfflictions.STATUSTYPE), typeof(float), typeof(bool) })]
        private static void AddStatus_Postfix(CharacterAfflictions __instance,
                                              CharacterAfflictions.STATUSTYPE statusType,
                                              float amount,
                                              bool fromRPC)
        {
            try
            {
                if (amount > 0f)
                    AfflictionsAPI.InvokeOnAdded(__instance, (int)statusType, amount);
            }
            catch (Exception ex) { Debug.LogError("MoreAfflictions AddStatus postfix: " + ex); }
        }
    }
}
