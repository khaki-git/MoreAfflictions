# More Afflictions API for PEAK

A **BepInEx** plugin for the game **PEAK** that extends the vanilla `CharacterAfflictions` system to support **custom status effects**.

This is **not** a general-purpose API for other Unity games — it is specifically for PEAK modding.

---

## Overview

The API allows PEAK mods to:

* Add new statuses beyond the built-in `CharacterAfflictions.STATUSTYPE` enum.
* Define a **cap** value.
* Provide an optional **OnAdded** callback.
* Assign an **icon** for display in the Stamina Bar.
* Automatically integrate custom statuses into the existing UI with **coloured bars**.

### Main Components

| File                         | Purpose                                                                                                                                       |
| ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| **`AfflictionsAPI.cs`**      | Public API for registering and using custom statuses. Includes Harmony patches for array expansion, cap overrides, and callbacks.             |
| **`CustomBarAffliction.cs`** | MonoBehaviour + Harmony patches to size, toggle, and icon the custom bars in the stamina bar UI.                                              |
| **`MoreAfflictions.cs`**     | BepInEx plugin entry point. Loads Harmony patches, ensures at least one test status exists, and bootstraps UI clones for every custom status. |

---

## Installation

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) for PEAK.
2. Compile `MoreAfflictions.dll` and place it in your `BepInEx/plugins` folder.
3. Start PEAK. The plugin will patch the afflictions and UI systems.

---

## Public API

### Namespace

```csharp
using MoreAfflictionsPlugin.APIs;
```

### Register a Custom Status

```csharp
int index = AfflictionsAPI.RegisterStatus(
    string name,
    float cap,
    Action<CharacterAfflictions, float> onAdded,
    Sprite icon = null
);
```

* **`name`** — Unique, case-insensitive name.
* **`cap`** — Maximum value (negative becomes `0`).
* **`onAdded`** — Optional callback when the status value is added to.
* **`icon`** *(optional)* — Sprite displayed in the stamina bar.

> Returns the absolute status index used internally by `CharacterAfflictions`.

### Set a Status Icon

```csharp
AfflictionsAPI.SetStatusIcon("Thirst", mySprite);
```

### Query or Modify a Status by Name

```csharp
float val = afflictions.GetStatus("Thirst");
afflictions.SetStatus("Thirst", 0.5f);
bool changed = afflictions.AddStatus("Thirst", 0.1f);
afflictions.SubtractStatus("Thirst", 0.1f);
```

These are **extension methods** for `CharacterAfflictions`.

### Utility Methods

```csharp
bool found = AfflictionsAPI.TryGetIndex("Thirst", out int idx);
Sprite icon = AfflictionsAPI.GetStatusIcon("Thirst");
List<string> customs = AfflictionsAPI.GetRegisteredCustomNames();
```

---

## Internal Behaviour

### Array Expansion

Patches `CharacterAfflictions.InitStatusArrays` to:

* Increase the status arrays to fit all registered custom statuses.
* Copy over existing values.

### Cap Override

Patches `CharacterAfflictions.GetStatusCap` to return the **custom cap** for custom statuses.

### OnAdded Dispatch

Patches `CharacterAfflictions.AddStatus` to trigger the registered **OnAdded** callback.

---

## UI Integration in PEAK

The stamina bar is patched to:

1. **Clone** an existing `BarAffliction` as a template for each custom status.
2. Set `statusName` and `statusIndex`.
3. Assign a **deterministic bright colour** (preserving original alpha).
4. Add the `BarAfflictionCustom` driver to update the size and visibility.
5. Apply icons from the API if provided.

### `BarAfflictionCustom` Behaviour

* **`size`** — Target width in pixels.
* **`TryApplyIconOnce()`** — Locates an icon `Image` in the bar and applies the sprite.
* Visibility automatically toggles when the value is near zero.

---

## Deterministic Colours

* Generated from a hash of the `statusName`.
* HSV values: Saturation `0.8`, Value `0.9`.
* Original transparency (alpha) is preserved for each UI `Image`.

---

## Example Usage in a PEAK Mod

```csharp
[HarmonyPatch(typeof(MyModInit))]
public static class MyModInitPatch
{
    [HarmonyPostfix]
    public static void Init()
    {
        var icon = LoadMySprite("Assets/Icons/thirst.png");
        AfflictionsAPI.RegisterStatus(
            "Thirst",
            1.0f,
            (ca, amt) => Debug.Log($"Thirst increased by {amt}"),
            icon
        );
    }
}
```

---

## Logging

The plugin logs:

* Registry contents at `Awake`
* Counts of created/present/skipped bars
* Errors during cloning or API usage

---

## Version

`0.4.2`
