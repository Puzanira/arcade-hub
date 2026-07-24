# Arcade Hub — Architecture

The hub is one Unity project (6000.5.3f1, URP) that hosts the arcade launcher and, later, all seven
cabinet games. This document describes what exists after increment **hub-v0**: the input package is
wired in, a menu runs the loop `menu → game scene → back to menu`, and a built-in `TestGame` proves
the game-scene contract before real games arrive.

## Layers

```
 StreamingAssets/launcher-config.json   (data, editable on disk — the museum case)
                │  parsed by
                ▼
 LauncherConfig / LauncherSlot  ── pure C#, EditMode-tested ──►  HubMenuModel (cursor + wrap-around)
                                                                        │ drives
 arcade-controls package                                                ▼
 ArcadeInput (8 logical controls) ──── read only ────►  HubMenuController / TestGameController
   (KeyboardBackend now, SerialBackend later)                 (MonoBehaviours, procedural UGUI)
```

Nothing in the hub reads a raw device. Input arrives exclusively through `ArcadeInput` from the
`com.aigamestudio.arcade-controls` package; a source scan of `Assets/_Project/Scripts/Hub` is part of
the done-contract (no `Keyboard`, `Mouse`, `Input.`, `InputSystem`, `GetKey`, …). Swapping keyboard
for the Arduino `SerialBackend` later is a package/backend change, invisible to the hub.

## Project layout

```
components/unity-game/
  Packages/manifest.json          → file: dependency on the local arcade-controls package
  Assets/StreamingAssets/
    launcher-config.json          → 7 "control → game" slots (name, entry scene, installed/planned)
  Assets/_Project/
    Scripts/Hub/                  → AiGameStudio.ArcadeHub asmdef
      LauncherSlot.cs             → one slot (data)
      LauncherConfig.cs           → parse + validate JSON (throws FormatException on bad input)
      LauncherConfigLoader.cs     → reads StreamingAssets; bad/missing file → empty menu, never a crash
      HubMenuModel.cs             → pure navigation model (wrap-around selection)
      HubMenuController.cs        → builds the menu UGUI, reads ArcadeInput, loads/announces slots
      TestGameController.cs       → the built-in test game (see the contract below)
      HubScenes.cs                → canonical scene-name constants
    Scenes/HubMenu.unity          → Build Settings scene 0
    Scenes/TestGame.unity         → Build Settings scene 1
    Tests/EditMode/               → LauncherConfig + HubMenuModel logic tests
    Tests/PlayMode/               → full HubMenu → TestGame → HubMenu loop, headless
```

## Package dependency

`Packages/manifest.json` pins arcade-controls as a **local `file:` dependency**:

```json
"com.aigamestudio.arcade-controls":
  "file:../../../../arcade-controls/components/unity-game/Packages/com.aigamestudio.arcade-controls"
```

This is honest for v0 — one PC, both repos side by side under `AI_GAME_STUDIO/projects`. When the
package moves to GitHub it becomes a git URL pinned to a tag (`…?path=…#<tag>`); that is a deliberate,
separate decision recorded in the increment contract. The relative path survives moving the whole
studio tree.

## The menu

`HubMenuController` loads `launcher-config.json`, builds one row per slot procedurally (UGUI `Text` on a
screen-space canvas; font `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`), and drives a
`HubMenuModel` cursor:

- **Navigate** — joystick vertical (`ArcadeInput.Joystick.Vector.y`), one move per push (an arm/release
  latch), wrapping top↔bottom.
- **Select** — red-button rising edge (`ArcadeInput.RedButton`). Input is *polled* (not event
  subscription) so it survives `ArcadeInput.Initialize` being called again across scene loads/tests.
- **Installed slot** → `SceneManager.LoadScene(entryScene)`.
- **Planned slot** → shows a "COMING SOON — <game>" line; loads nothing.

A malformed or missing config logs one clear console error and yields an **empty** menu — the launcher
still comes up. Each row's text is one-hot (its own unique game name), so a crossed row cannot pass the
visibility/content tests silently.

## Game-scene contract (what TestGame demonstrates)

A game scene added to the hub must:

1. **Boot by scene load.** All setup happens in `Awake`/`Start`; entering the scene is the only trigger.
   `TestGame` builds its screen (backdrop, title, a joystick-driven marker) in `Start`.
2. **Read input only through `ArcadeInput`.** No raw devices. `TestGame` moves its marker from
   `ArcadeInput.Joystick`.
3. **Die cleanly on `ArcadeInput.MenuButton`.** On the button's rising edge the scene loads `HubMenu`
   and every object it created is destroyed with the scene.
4. **Hold no persistent state.** No `DontDestroyOnLoad`, no static gameplay state that survives exit, so
   every entry is a clean start. `TestGameController.InstanceCount` (a live counter) is asserted to be
   exactly `1` after `menu → game → menu → game`, never `2` — the PlayMode test's leak guard.

Real games will be UPM packages consumed by git URL (`?path=…#<tag>`), their entry scenes added to the
hub's Build Settings and declared in `launcher-config.json`. The contract above is what the integration
kit (`ARCADE_INTEGRATION.md` + contract tests + CI) will enforce per repo — see
`system/specs/ARCADE_CABINET_SPEC.md` §2.4.

## Tests

- **EditMode** — `LauncherConfig` parsing (valid / malformed / empty / installed-vs-planned) and
  `HubMenuModel` navigation (wrap-around, empty menu, installed/planned selection).
- **PlayMode** — loads `HubMenu`, takes over input with a `FakeBackend`, asserts all 7 rows are visible
  by real rect size **and** viewport overlap (a big off-screen rect fails) with correct per-row content,
  navigates, enters `TestGame` with RED, returns with MENU, and re-enters to prove a clean start.

Run headless (Editor closed):

```
"<UnityEditor>" -runTests -batchmode -nographics -projectPath components/unity-game \
  -testPlatform EditMode  -testResults .studio/test-editmode.xml -logFile -
"<UnityEditor>" -runTests -batchmode            -projectPath components/unity-game \
  -testPlatform PlayMode  -testResults .studio/test-playmode.xml -logFile -
```

## Path to stage ③ (launcher v1)

hub-v0 is a **keyed menu**: press RED to select. Stage ③ replaces instant selection with the real
cabinet interaction — *hold/crank a control for 5 seconds* to launch (a charge bar 0→1, decay on
release, exclusivity while one control is active, and the themed "rain of objects" fill). The launcher
becomes a deterministic state machine (`Idle → Charging(C) → Launching`) that is pure-C# testable, still
reading only `ArcadeInput`. The menu, config, package seam, and game-scene contract built here are the
foundation it sits on; none of them change when hold-to-launch lands.
```
