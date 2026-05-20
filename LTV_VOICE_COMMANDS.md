# LTV Voice Commands

All voice commands related to the LTV (Lunar Terrain Vehicle) repair flow.

## How voice input works

There are **two** voice paths in the project:

1. **MLVoice (Magic Leap system intents)** — a fixed list of exact phrases, each with a
   numeric `Id`, defined in `Assets/AIA/MLVoiceIntentsConfiguration.asset` and dispatched
   by the big `switch` in `VoiceIntents.cs` (`OnVoiceEvent`). **Every command in this
   document is an MLVoice phrase.** Recognition is phrase-based — say the phrase as written.
2. **Vosk + Gemma (free-form)** — the wake phrase **"hey luna"** (Id 105) starts Vosk
   recording and forwards the transcript to the AI. This is not an LTV command itself; it's
   the general assistant path.

Most LTV commands are dispatched as **static C# events** on `VoiceIntents`, so any
scene-local controller can subscribe without `VoiceIntents` holding a reference to it. That
means a phrase only *does something* in a scene where a consumer exists (noted as **Scope**
below).

## LTV repair flow — enter / exit

| Phrase | Id | Scope | Effect |
|--------|----|-------|--------|
| `ltv repair` | 106 | Mission (anywhere MLVoice is active) | Enters the LTV repair flow |
| `start ltv repair` | 107 | Mission | Same as above |
| `begin ltv repair` | 108 | Mission | Same as above |
| `end ltv repair` | 113 | LTV | Exits LTV back to the previous scene |

**Routing:** Ids 106–108 fall through `VoiceIntents` to `TryRouteSceneVoiceCommand`, which
hands the phrase to `LtvVoiceCoordinator` (`Assets/LTV/LtvVoiceCoordinator.cs`). It matches
trigger substrings (`"ltv repair"`, `"ltv-repair"`, `"lt v repair"`), sets
`PendingVoiceTrigger = true`, and loads the `LTV` scene. On load, `LtvSceneBootstrapper`
consumes that flag and auto-starts the diagnosis. Id 113 routes through
`SceneVoiceCoordinator` as a normal scene transition out of LTV.

## LTV step navigation (inside the LTV scene)

| Phrase | Id | Fires event | Handler | Effect |
|--------|----|-------------|---------|--------|
| `next step` | 116 | `LtvNextStepRequested` | `LTVVoiceStepControl.HandleNext` → `LtvHudController.InvokeCheckmark` | Advance to the next procedure step |
| `previous step` | 123 | `LtvPreviousStepRequested` | `LTVVoiceStepControl.HandlePrevious` → `LtvHudController.InvokePrevious` | Go back one step |

**Note:** voice step-nav calls the checkmark/previous **button's `onClick.Invoke()`**, so
voice and a physical button press run the *exact same* code path (`AdvanceStep()` /
`RetreatStep()` on `LtvInstructionService`). "Previous" is ignored on step 0.

## LTV reference map (inside the LTV scene)

| Phrase | Id | Fires event | Handler | Effect |
|--------|----|-------------|---------|--------|
| `show reference map` | 117 | `LtvReferenceMapShowRequested` | `LTVReferenceMapController.Show` | Spawn the world-locked reference map in front of the user |
| `hide reference map` | 118 | `LtvReferenceMapHideRequested` | `LTVReferenceMapController.Hide` | Hide the reference map |

## HUD visibility (global — applies during LTV)

| Phrase | Id | Fires event | Effect |
|--------|----|-------------|--------|
| `clear display` | 114 | `HudClearDisplayRequested` | Hides the HUD |
| `show display` | 115 | `HudShowDisplayRequested` | Shows the HUD |

These are not LTV-exclusive (any scene with a `HUDVisibilityController` responds), but they
work in the LTV scene.

## LTV-waypoint navigation (Mission minimap — LTV-adjacent)

Handled by `NavVoiceCoordinator` (Mission scene minimap), not the LTV repair scene itself,
but they target LTV waypoints:

| Phrase | Id | Effect |
|--------|----|--------|
| `go to ltv1` | 119 | Plot/navigate the minimap path to LTV waypoint 1 |
| `go to ltv2` | 122 | Plot/navigate the minimap path to LTV waypoint 2 |
| `return to base` | 120 | Navigate back to base |
| `clear path` | 121 | Clear the current minimap path |

## Not LTV (listed for completeness / exclusion)

`show` (101), `hide` (102), `rotate` (103), `stop` (104) — generic demo-object controls.
`hey luna` (105) — Vosk/Gemma wake phrase. `start egress` (109), `start mission` (110),
`start ingress` (111), `end ingress` (112) — non-LTV scene transitions.

## Adding or changing a phrase

1. Edit `Assets/AIA/MLVoiceIntentsConfiguration.asset` — add an `Id` + `Value` (the spoken
   phrase). Keep Ids unique; mind the ranges hard-coded in `VoiceIntents.cs`
   (106–108 LTV-repair, 109–113 scene transitions, 114–118 HUD/LTV, 119–122 nav, 123 prev).
2. Handle the new Id in `VoiceIntents.OnVoiceEvent` (add a `case` or extend a range) and, if
   it's a static event, declare the event and have the scene-local controller subscribe.
3. Phrases are matched as configured — choose distinct, easily-recognized wording.
