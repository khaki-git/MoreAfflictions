// MoreAfflictions\APIs\AfflictionsAPI.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MoreAfflictionsPlugin.APIs
{
    /// <summary>
    /// Custom Afflictions registry (C# 7.3 compatible).
    /// Lets mods add new status entries beyond CharacterAfflictions.STATUSTYPE,
    /// define a cap, optional OnAdded callback, and an optional icon Sprite.
    /// Also exposes extension helpers to use names directly from gameplay/UI.
    /// </summary>
    public static class AfflictionsAPI
    {
        private static readonly object _lockObj = new object();

        private static readonly List<CustomStatus> _customs = new List<CustomStatus>();

        // name -> absolute index (index uses base enum length + offset)
        private static readonly Dictionary<string, int> _nameToIndex =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // name -> icon
        private static readonly Dictionary<string, Sprite> _nameToIcon =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        private static int _cachedBaseCount = -1;

        /// <summary>Number of vanilla statuses (enum length). Cached.</summary>
        private static int BaseCount
        {
            get
            {
                if (_cachedBaseCount < 0)
                {
                    _cachedBaseCount = Enum.GetNames(typeof(CharacterAfflictions.STATUSTYPE)).Length;
                }
                return _cachedBaseCount;
            }
        }

        /// <summary>Total count including custom entries.</summary>
        internal static int TotalCount
        {
            get
            {
                lock (_lockObj) return BaseCount + _customs.Count;
            }
        }

        /// <summary>
        /// Register a new custom status. Returns the absolute index used by CharacterAfflictions arrays.
        /// If the name was already registered, returns the existing index (idempotent).
        /// </summary>
        /// <param name="name">Unique status name (case-insensitive).</param>
        /// <param name="cap">Maximum value (like vanilla caps). Negative becomes 0.</param>
        /// <param name="onAdded">Optional callback when amount is added.</param>
        /// <param name="icon">Optional icon to show in the stamina bar strip.</param>
        public static int RegisterStatus(string name, float cap, Action<CharacterAfflictions, float> onAdded, Sprite icon)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name");

            lock (_lockObj)
            {
                int idx;
                if (_nameToIndex.TryGetValue(name, out idx))
                {
                    // Update icon if provided later by another mod/init step.
                    if (icon != null) _nameToIcon[name] = icon;
                    return idx;
                }

                idx = BaseCount + _customs.Count;

                var cs = new CustomStatus
                {
                    Name = name,
                    Index = idx,
                    Cap = Mathf.Max(0f, cap),
                    OnAdded = onAdded
                };

                _customs.Add(cs);
                _nameToIndex[name] = idx;

                if (icon != null)
                    _nameToIcon[name] = icon;

                return idx;
            }
        }

        public static void SetStatusIcon(string name, Sprite icon)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_lockObj)
            {
                _nameToIcon[name] = icon;
            }
        }


        /// <summary>Overload without icon (kept for older mods using the 3-arg pattern).</summary>
        public static int RegisterStatus(string name, float cap, Action<CharacterAfflictions, float> onAdded)
        {
            return RegisterStatus(name, cap, onAdded, null);
        }

        /// <summary>Try resolve a status NAME (custom or vanilla) to the absolute index.</summary>
        public static bool TryGetIndex(string name, out int index)
        {
            if (string.IsNullOrEmpty(name))
            {
                index = -1;
                return false;
            }

            lock (_lockObj)
            {
                if (_nameToIndex.TryGetValue(name, out index))
                    return true;
            }

            // Fall back to vanilla enum lookup by string (case-insensitive)
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

        /// <summary>Internal: return custom cap if index belongs to a custom entry; otherwise fallback.</summary>
        internal static float GetCapOr(float fallback, int index)
        {
            if (index < BaseCount) return fallback;
            CustomStatus cs = GetCustomByIndex(index);
            return cs != null ? cs.Cap : fallback;
        }

        /// <summary>Internal: dispatch OnAdded for custom statuses.</summary>
        internal static void InvokeOnAdded(CharacterAfflictions ca, int index, float amount)
        {
            CustomStatus cs = GetCustomByIndex(index);
            if (cs != null && cs.OnAdded != null)
                cs.OnAdded(ca, amount);
        }

        /// <summary>Public: get a list of custom status names currently registered.</summary>
        public static List<string> GetRegisteredCustomNames()
        {
            lock (_lockObj)
            {
                var list = new List<string>(_customs.Count);
                for (int i = 0; i < _customs.Count; i++)
                    list.Add(_customs[i].Name);
                return list;
            }
        }

        /// <summary>
        /// Public: get the icon for a status NAME (custom only). Returns null if none set.
        /// </summary>
        public static Sprite GetStatusIcon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (_lockObj)
            {
                Sprite s;
                return _nameToIcon.TryGetValue(name, out s) ? s : null;
            }
        }

        /// <summary>Public: try get custom icon (bool out pattern).</summary>
        public static bool TryGetCustomIcon(string name, out Sprite sprite)
        {
            lock (_lockObj)
            {
                return _nameToIcon.TryGetValue(name, out sprite);
            }
        }

        // ---------- Extension helpers so gameplay/UI code can use names directly ----------

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

        // ---------- Internals ----------

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
    }

    // ---------------- Harmony patches (expand arrays + cap hook) ----------------
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

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CharacterAfflictions), "InitStatusArrays")]
        private static void InitStatusArrays_Postfix(CharacterAfflictions __instance)
        {
            try
            {
                int total = AfflictionsAPI.TotalCount;

                var cur = (float[])F_Current.GetValue(__instance);
                if (cur != null && cur.Length >= total) return;

                var inc = (float[])F_Inc.GetValue(__instance);
                var dec = (float[])F_Dec.GetValue(__instance);
                var last = (float[])F_Last.GetValue(__instance);
                var lastInc = (float[])F_LastInc.GetValue(__instance);

                var n_cur = new float[total]; if (cur != null) Array.Copy(cur, n_cur, cur.Length);
                var n_inc = new float[total]; if (inc != null) Array.Copy(inc, n_inc, inc.Length);
                var n_dec = new float[total]; if (dec != null) Array.Copy(dec, n_dec, dec.Length);
                var n_last = new float[total]; if (last != null) Array.Copy(last, n_last, last.Length);
                var n_lastInc = new float[total]; if (lastInc != null) Array.Copy(lastInc, n_lastInc, lastInc.Length);

                F_Current.SetValue(__instance, n_cur);
                F_Inc.SetValue(__instance, n_inc);
                F_Dec.SetValue(__instance, n_dec);
                F_Last.SetValue(__instance, n_last);
                F_LastInc.SetValue(__instance, n_lastInc);
            }
            catch (Exception ex)
            {
                Debug.LogError("MoreAfflictions InitStatusArrays postfix: " + ex);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.GetStatusCap))]
        private static void GetStatusCap_Postfix(ref float __result, CharacterAfflictions.STATUSTYPE type)
        {
            try
            {
                __result = AfflictionsAPI.GetCapOr(__result, (int)type);
            }
            catch (Exception ex)
            {
                Debug.LogError("MoreAfflictions GetStatusCap postfix: " + ex);
            }
        }

        // Optional: call the OnAdded callback after successful AddStatus for customs.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
        private static void AddStatus_Postfix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, float amount, bool fromRPC)
        {
            try
            {
                AfflictionsAPI.InvokeOnAdded(__instance, (int)statusType, amount);
            }
            catch (Exception ex)
            {
                Debug.LogError("MoreAfflictions AddStatus postfix: " + ex);
            }
        }
    }
}
