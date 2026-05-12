# Starter Scene UI — Build Notes

Build of the Starter scene UI for the Lunar Lions NASA SUITS 2026 app on Magic Leap 2. Captures the original brief, decisions, and what landed in the repo so it can be picked up later without re-deriving context.

Branch: `starter-scene` (created off `main` at `59c633b2`).

---

## Original prompt

> Build the starter UI in this Magic Leap 2 NASA SUITS app. Reference mockups are in `docs/` — see `complete.png` for the composite layout and the four individual PNGs for per-panel detail.
>
> **Scene:** `Assets/Scenes/final_scenes/Starter.unity` already exists. Open it first and inspect what's in it.
> - It contains an `XR Interaction Hands Setup` GameObject with the XR Origin, Camera Offset, and Main Camera. Do not delete, duplicate, or modify this object or its children. Reuse it.
> - If the scene contains other meaningful work beyond the XR rig (existing UI, scripts, gameplay objects), stop and report what's there before adding anything. Only proceed to build the starter UI if the scene is otherwise empty.
>
> **Stack constraints:**
> - Unity 2022.3.7f1, OpenXR + Magic Leap SDK 2.6.0, XR Interaction Toolkit, XR Hands, TextMeshPro
> - No MRTK. Do not import `Microsoft.MixedReality.*` namespaces or MRTK prefabs.
> - World-space Unity UI Canvas with `TrackedDeviceGraphicRaycaster` on the canvas for the START button.
>
> **Parenting (important):** Create a single root GameObject (e.g. `StarterUI`) as a child of the existing Camera Offset under `XR Interaction Hands Setup`. All three panels and the START button live under this root. Use local coordinates relative to the Camera Offset — this body-locks the UI to the user's playspace so it appears in front of them regardless of where they're standing. Place the root at approximately local `(0, 0, 1.5)` (1.5m forward, eye level since Camera Offset already accounts for head height).
>
> **Layout — three panels:**
> - **Left "DISPLAYS"** — informational only. Header + four rows: Vitals (Oxygen, CO₂, pressure), Navigation (Your path), LTV Mission (Status and repair steps.), Checklist (Steps before leaving the airlock.).
> - **Center "LUNAR LIONS · NASA SUITS 2026"** — Columbia crown logo at top (Image component, sprite field left unassigned), title, divider, description: "Lunar Lions supports lunar EVAs with AR navigation, telemetry, procedural guidance, and LTV repair assistance on Magic Leap 2."
> - **Right "CONTROLS"** — informational only. Header + two rows: Pinch (Touch thumb to index finger to select.), Voice (Say "Hey Luna" to talk to the assistant).
> - **START button** — bottom-right of the layout, green pill with "START" text and right-chevron. Only interactive element.
>
> **Curved-feeling arrangement:** Side panels rotate ~15° inward (local Y rotation) toward the center. Flat rectangular Canvases — no curving shader or curved-UI package.
>
> **Styling:** Rounded-corner panels, near-opaque fills (~95% alpha), dark text on light fill. Bold label + lighter gray descriptor.
>
> **START button behavior:**
> - Script at `Assets/UI-Scripts/StartButtonHandler.cs`
> - `[SerializeField] private string nextSceneName = "Mission";` (overridable in Inspector)
> - Wired to `Button.onClick` → `SceneManager.LoadScene(nextSceneName)`
> - Empty-string guard with `Debug.LogWarning`, no crash
> - Update `EditorBuildSettings.scenes` so both `final_scenes/Starter` and `final_scenes/Mission` are in the build list
>
> **Hard constraints:**
> - Do not touch the XR Interaction Hands Setup or any child.
> - No second XR Origin or Camera.
> - No voice/pinch-detection code. Voice/Pinch labels are static text.
> - Do not modify `Packages/manifest.json` (per-user local path).
> - Do not modify `MagicLeapInput.inputactions`.
> - No animations, audio, or transitions.
> - Minimal comments.

---

## Pre-existing scene content (found and decided)

`Starter.unity` was **not empty**. Before-changes contents beyond scene defaults + the XR rig:

1. World-Space `Canvas` added to the XR rig with a `Next/Begin` button wired to `SceneSwitcher.LoadARScene` — **but the `SceneSwitcher.cs` file was missing from the project**, so the reference was already broken on `main`.
2. `HelloWorld` GameObject — a separate screen-overlay canvas with a welcome text at `localScale (0,0,0)` (effectively hidden).
3. `SceneManager` root GameObject with a MonoBehaviour pointing to the same missing `SceneSwitcher` script (broken).
4. `TssUnityApiService` prefab instance (TSS HTTP backend service).

**Decisions made (with user):**
- Replace UI, keep services → remove (1), (2); keep (3) and (4).
- Remove the missing-script component from `SceneManager` → strip the dangling MonoBehaviour; keep the GameObject.

---

## Final files

### New
| File | Purpose |
|---|---|
| `Assets/UI-Scripts/StartButtonHandler.cs` (+`.meta`) | Single `OnStartPressed()` method, `SceneManager.LoadScene(nextSceneName)`, empty-string guard. GUID `7a8b1c2d3e4f5a6b7c8d9e0f1a2b3c4d`. |

### Modified
| File | Change |
|---|---|
| `Assets/Scenes/final_scenes/Starter.unity` | Rewritten end-to-end (~4350 lines). Old UI/HelloWorld removed, broken script ref stripped from SceneManager, full StarterUI hierarchy inlined as scene-local GameObjects parented to the XR rig via `m_AddedGameObjects`. |
| `ProjectSettings/EditorBuildSettings.asset` | Added `final_scenes/Starter` (GUID `6002158857ea7482ea8717f7bfd0efb1`) and `final_scenes/Mission` (GUID `55d96265320294461b2212e6379667f5`). |

### Deliberately not touched
- `Packages/manifest.json` — has a per-user `file:/Users/.../com.magicleap.unitysdk.tgz` path that pre-existed as an uncommitted local change.
- `MagicLeapInput.inputactions`
- `XR Interaction Hands Setup` prefab and any child (XR Origin, Camera Offset, Main Camera, hand tracking)
- Vosk / MLVoice plumbing

---

## Hierarchy that was built

All under `XR Interaction Hands Setup` (registered via `m_AddedGameObjects` on the rig's prefab instance — same pattern Mission.unity uses for `UIAnchor`):

```
StarterUI                          [Canvas + CanvasScaler + TrackedDeviceGraphicRaycaster]
                                   scale 0.001 (1 unit = 1mm), size 1800×900 → 1.8m × 0.9m
                                   localPosition (0, 0, 1.5)  ← 1.5m forward of rig origin
├── LeftPanel                      [Image rounded bg, 95% alpha gray]
│                                  localRotation: euler (0, +15, 0)  ← tilted inward
│   ├── Header "DISPLAYS"          [TMP, bold, 32pt, dark]
│   ├── Row_Vitals                 [bg + Label "Vitals" + Divider + Descriptor "Oxygen, CO₂, pressure"]
│   ├── Row_Navigation             [... + "Navigation" + "Your path"]
│   ├── Row_LTVMission             [... + "LTV Mission" + "Status and repair steps."]
│   └── Row_Checklist              [... + "Checklist" + "Steps before leaving the airlock."]
├── CenterPanel                    [Image rounded bg, light cyan ~95% alpha]
│   ├── Logo                       [Image, NO sprite assigned (placeholder light-blue tint)]
│   ├── Title                      [TMP "LUNAR LIONS · NASA SUITS 2026", bold, centered]
│   ├── Divider                    [Image thin gray line]
│   └── Description                [TMP "Lunar Lions supports lunar EVAs ...", lighter, centered]
├── RightPanel                     [Image rounded bg, 95% alpha gray]
│                                  localRotation: euler (0, -15, 0)  ← tilted inward
│   ├── Header "CONTROLS"          [TMP, bold, 32pt, dark]
│   ├── Row_Pinch                  [bg + "Pinch" + Divider + "Touch thumb to index finger to select."]
│   └── Row_Voice                  [bg + "Voice" + Divider + 'Say "Hey Luna" to talk to the assistant']
└── StartButton                    [Image green pill bg + Button + StartButtonHandler MB]
    │                              anchoredPosition (660, -380), size (260, 90)
    │                              Button.onClick → StartButtonHandler.OnStartPressed
    ├── Label "START"              [TMP, bold, 28pt, dark]
    └── Chevron ">"                [TMP, bold, 32pt, dark]
```

### Row anatomy (side panels)
Each row is an Image (rounded gray bg) containing three TMP/Image children:
- `Label` — anchored top, bold dark
- `Divider` — anchored center-ish, 1.5px thin gray line
- `Descriptor` — anchored bottom, lighter gray, smaller

---

## Key design choices and why

| Decision | Why |
|---|---|
| Parent UI to the XR rig **root** (sourceID `8358158060413148696`) rather than constructing a stripped Transform reference to the deeper Camera Offset | Camera Offset is inside a nested prefab chain (`XR Interaction Hands Setup` → `XR Origin Hands` → `XR Origin (XR Rig)` base prefab). Fabricating a stripped Transform reference for it by hand was error-prone. Rig root + Device tracking mode gives identical body-locked behavior at local (0, 0, 1.5) for our case. |
| **Inline** the UI as scene-local GameObjects rather than referencing a separate `StarterUI.prefab` | I built a prefab first, but referencing a PrefabInstance as a child of another prefab instance via `m_AddedGameObjects` is finicky in YAML. The scene-local pattern is the one Mission.unity uses for its `UIAnchor`, well-tested. |
| World-space Canvas at scale `0.001` | 1 canvas unit = 1mm, so TMP `fontSize: 24` ≈ 24mm tall text. Readable at 1.5m distance. |
| Single Canvas, panels are rotated RectTransform children | Avoids three separate raycasters. Children with non-zero local Y rotation render fine in a world-space canvas. |
| Rounded fills via Unity's built-in `Background` sprite (fileID `10905`) | No custom sprites needed; matches what the existing scene's Begin button used. |
| `TrackedDeviceGraphicRaycaster` for the canvas (GUID `7951c64acb0fa62458bf30a60089fe2d` from XRI 2.6.4) | Required for XR hands to pinch-select UI. Default `GraphicRaycaster` wouldn't work. |
| `RaycastTarget: 0` on every non-button graphic | Only the START button image (`fileID 5000402`) has `RaycastTarget: 1`. Avoids hit-blocking on labels/dividers. |
| File IDs reserved in the `5000010-5000423` range | Avoids collision with existing scene IDs (which were all `<3000000000`). Easy to remember structure: `5000010+` root, `5000100+` LeftPanel, `5000200+` CenterPanel, `5000300+` RightPanel, `5000400+` StartButton. |

---

## Editor verification checklist

When opening the scene for the first time after pulling this branch:

1. **Open `Assets/Scenes/final_scenes/Starter.unity`.** Unity should import cleanly. No "missing script" warnings on `SceneManager` anymore.
2. **Logo sprite assignment** — required before the center panel looks right:
   - Hierarchy: `XR Interaction Hands Setup` → `StarterUI` → `CenterPanel` → `Logo`
   - In Inspector, the Image component's `Source Image` is empty.
   - Drag a Columbia crown PNG (imported as Sprite type) into that field.
   - If the logo looks tinted, also set the Image's `Color` to white `#FFFFFFFF` (it's currently a light-blue placeholder).
3. **Game view sanity** — three panels visible ~1.5m forward, side panels tilted ~15° inward toward center, START button bottom-right.
4. **Button wiring** — Select `StartButton`. In Inspector, `Button` component's onClick list should show `StartButtonHandler.OnStartPressed`. `Next Scene Name` field on the StartButtonHandler component shows `Mission` (editable).
5. **Build Settings** (`File → Build Settings`) — `final_scenes/Starter` and `final_scenes/Mission` are both in the list. If `Starter` is `enabled: 0`, toggle the checkbox.
6. **Play mode** — press START with mock HMD / hand simulator. Should `SceneManager.LoadScene("Mission")` without errors. If `nextSceneName` is cleared and START is pressed, a `Debug.LogWarning` fires, no crash.

---

## Known caveats and follow-ups

- **`Packages/manifest.json` is still uncommitted** (the local Magic Leap SDK path swap from `/Users/tu15/...` to `/Users/richardli/...`). Not introduced by this work — pre-existed on `main`. The team should agree on whether this file should be gitignored or use an env-var-based path.
- **Hand-authored YAML was not Unity-verified.** I built the scene without running Unity. The pattern (regular scene GameObject as `addedObject` of a prefab instance) matches a tested pattern in Mission.unity, but if Unity reports a parse error on first open, common fixes: re-drag `StartButtonHandler.cs` onto the `StartButton` GameObject; verify `TrackedDeviceGraphicRaycaster` is the script on `StarterUI` (GUID `7951c64a...`).
- **`SceneManager` GameObject** is now a placeholder Transform (its only MonoBehaviour was the dangling SceneSwitcher reference). Safe to delete if nothing in the scene depends on its name; left in place defensively.
- **Layout** is a reasonable first pass, not pixel-perfect. The `anchoredPosition` and `sizeDelta` of any child can be tweaked in the Inspector without touching code.
- **No prefab artifact** — the StarterUI hierarchy lives only in `Starter.unity`. If the same layout is wanted in other scenes, copy the `StarterUI` GameObject (right-click → Copy → Paste into target scene under that scene's XR rig), then re-wire the START button's `onClick`.

---

## Quick reference: file IDs

| Range | Owner |
|---|---|
| `5000010 – 5000014` | StarterUI root (GameObject, RectTransform, Canvas, CanvasScaler, TrackedDeviceGraphicRaycaster) |
| `5000100 – 5000113` | LeftPanel + Header |
| `5000120 – 5000135` | LeftPanel `Row_Vitals` |
| `5000140 – 5000155` | LeftPanel `Row_Navigation` |
| `5000160 – 5000175` | LeftPanel `Row_LTVMission` |
| `5000180 – 5000195` | LeftPanel `Row_Checklist` |
| `5000200 – 5000243` | CenterPanel + Logo + Title + Divider + Description |
| `5000300 – 5000313` | RightPanel + Header |
| `5000320 – 5000335` | RightPanel `Row_Pinch` |
| `5000340 – 5000355` | RightPanel `Row_Voice` |
| `5000400 – 5000405` | StartButton (GO, RT, Image, CR, Button, StartButtonHandler) |
| `5000410 – 5000413` | StartButton Label |
| `5000420 – 5000423` | StartButton Chevron |
