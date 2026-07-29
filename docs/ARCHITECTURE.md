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

## The attract screen (former menu)

The HubMenu scene is an **attract screen** (founder's decision 2026-07-29): a full-screen LOOPING video
with sound (`AttractVideoScreen`: StreamingAssets/Explainer.mp4 → VideoPlayer → RenderTexture → RawImage
on a bottom-sorted canvas, letterboxed FitInside; the clip is swappable on the cabinet like the config —
no rebuild). The old game list, joystick navigation and ALL text are gone; `HubMenuModel` is deleted.
`HubMenuController` remains the scene's host: it runs the DDOL janitor sweep and builds the video screen.

- **No text over the video.** The themed sprite rain IS the game hint; the charge bar (bg+fill) only
  exists while something charges (`charge > 0`), so an idle cabinet shows a clean video frame. The single
  approved exception (gate-2): the "СКОРО" overlay when a soon-slot reaches full charge.
- **Launching** lives exclusively in hold-to-launch: every control charges its OWN slot to full.
  *History: the red-button instant-select (launched the highlighted row) was removed at gate 2 of
  all-games-wiring; the list + informational highlight followed at attract-video-screen.*

A malformed or missing config logs one clear console error and degrades to an empty slot list — the
attract screen still comes up (video plays; nothing can charge).

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

- **EditMode** — `LauncherConfig` parsing (valid / malformed / empty / installed-vs-planned), the
  shipped config's founder layout + loadable rain-sprite sets, and the degradation chain (broken JSON →
  loader error → `HoldToLaunchController` builds with zero slots, no crash). The old `HubMenuModel`
  navigation tests are gone with the model itself (attract-video-screen).
- **PlayMode** — `AttractVideoPlayModeTests`: the video plays + loops with an audio track, the themed
  rain is one-hot per slot (expectations read from the shipped config), and the two contract captures
  (idle = clean frame, Sisyphus half-charge over the video). The `TestGame` rig loop runs through the
  hold path: a config injected into the scene's real `HoldToLaunchController` binds it to the crank;
  crank to full → launch, MENU returns, re-enter proves a clean start.

Run headless (Editor closed):

```
"<UnityEditor>" -runTests -batchmode -nographics -projectPath components/unity-game \
  -testPlatform EditMode  -testResults .studio/test-editmode.xml -logFile -
"<UnityEditor>" -runTests -batchmode            -projectPath components/unity-game \
  -testPlatform PlayMode  -testResults .studio/test-playmode.xml -logFile -
```

## Hold-to-launch (stage ③, landed)

Layered on top of the hub-v0 menu without changing it: each slot is bound to a physical control
(`controlName` in the config), and *holding / cranking / deflecting* that control for ~5 s charges a bar
0→1 to launch it. Releasing decays the charge to 0 over ~1 s (a fresh hold starts from 0, never
backwards), and only one control charges at a time (exclusivity). At full charge an **installed** slot
loads its scene; a **planned** slot flashes a "СКОРО — <game>" overlay and resets. A rain of
slot-coloured objects fills the screen in proportion to the charge and clears as it decays.

```
 launcher-config.json  "tuning" { chargeSeconds, decaySeconds, thresholds }  +  per-slot controlName
        │ parsed by                                                                │
        ▼                                                                          ▼
 LaunchTuning ─────────────►  LaunchChargeMachine  (pure: charge/decay/exclusivity/launch-edge)
 ArcadeInput ─read only─► ControlReadings ─► LaunchInputSampler (pure: per-control engagement + thresholds
                                                                  + crank |Δ°|>thr with a decay timeout)
                                                                          │ engaged[] per slot
        HoldToLaunchController (MonoBehaviour: adapter + progress bar + rain + "СКОРО" overlay)  ◄──┘
```

The charge/decay/exclusivity logic and the engagement thresholds are **pure C#**, fully EditMode-tested
(`LaunchChargeMachineTests`, `LaunchInputSamplerTests`, `LaunchTuningTests`); `HoldToLaunchController` is
the thin Unity adapter + view. Input still arrives **only** through `ArcadeInput` — the per-slot
engagement aggregation (a joystick/height/crank "is-this-control-active?" reader) lives on the hub side
over the package's public API and is a candidate to promote into `arcade-controls`.

The engagement rules per control kind (spec §2.3): buttons — held; height sensors — value above a
threshold; joystick — deflection magnitude above a threshold; crank — `|degrees this frame|` above a
threshold, latched for a short timeout so the gaps between discrete turns don't read as "released".

**Hold-to-launch is the ONLY launch path** (founder's gate-2 decision, 2026-07). Instant-select is
retired: it fired the highlighted row on a red press, which in the all-games layout shadowed charging
the red-bound Factory slot. Now every control — including RedButton — only charges its own slot; the
joystick remains navigation (informational highlight), which does not conflict with charging since the
Joystick-bound slot is a "soon" placeholder and joystick charge is deflection-magnitude-based anyway.

## Games integration (stage ②, landed)

The launcher now runs the first two REAL cabinet games. Each is a UPM package consumed by a local
`file:` dependency (same honest one-PC seam as arcade-controls; git-URL later is a separate decision):

```json
"com.aigamestudio.game-home-alone":  "file:../../../../home-alone/components/unity-game/Assets/_Project",
"com.aigamestudio.game-life-choices": "file:../../../../life-choices/components/unity-game/Assets/_Project"
```

Both games depend on the same `arcade-controls` package the hub already pins, so UPM resolves ONE shared
copy (no version conflict — the games were built on 6000.5.1, the hub hosts on 6000.5.3; "package older
than host" resolves cleanly). Their entry scenes live inside the packages and are added to Build Settings
by package path (`AddGameScenesToBuildSettings`, idempotent):

```
Packages/com.aigamestudio.game-home-alone/Scenes/Apartment.unity
Packages/com.aigamestudio.game-life-choices/Scenes/ThanksNoThanks.unity
```

`launcher-config.json` now ships **three** installed slots — Test Game (Crank), Home Alone (RedButton →
`Apartment`), Life Choices (GreenButton → `ThanksNoThanks`); the rest stay planned.

### Returning to the launcher (`LauncherReturn`)

The game-scene contract says a game "dies cleanly on MenuButton". The built-in `TestGameController`
satisfies it by loading `HubMenu` itself. The packaged games, however, are also standalone-buildable:
on MenuButton they end their run **in place** (freeze, no `DontDestroyOnLoad`/static state) but do NOT
know the hub's menu scene — a standalone build has no launcher to return to. So the LAUNCHER owns the
return: before loading an external game it arms `LauncherReturn`, a launcher-owned `DontDestroyOnLoad`
watchdog that watches the same `ArcadeInput.MenuButton` and loads `HubMenu` on the exit gesture, then
destroys itself. The gesture is the universal cabinet rule (and matches Home Alone's own exit): a
MenuButton still held while the game LOADS must not exit — a release is seen first, then a fresh press
fires (plus a one-frame warmup). It is armed from the single launch path (`HoldToLaunchController.Fire`)
and skipped for the self-returning TestGame. For `nativeInput` slots (games that never pump
`ArcadeInput` — Lady Bug on the legacy Input Manager, Factory on the raw new Input System) the watchdog
carries its own `ArcadeInputRunner` so the MenuButton gesture still reads; ArcadeInput-native games must
not be double-pumped and get no runner. On every menu entry `HubDdolJanitor` sweeps foreign
DontDestroyOnLoad objects leaked by external games (e.g. Factory's `GameManager`/`ArduinoInputBridge`) —
whitelisted namespaces (`AiGameStudio*`, `Unity*`, `TMPro`) survive; games recreate their singletons on
entry.

### Tests

- **EditMode** — a config test pins the shipped `launcher-config.json` to exactly three installed slots
  with the two games' entry scene names.
- **PlayMode** (`GamesIntegrationPlayModeTests`) — headless `FakeBackend` loop for each game: launch from
  the menu (Home Alone via its RedButton, Life Choices via the hold-to-launch charge on GreenButton),
  assert the game scene is active with its controller alive (`GameController` / `GameDriver`), return via
  MenuButton (the `LauncherReturn` watchdog), and re-enter as a clean start (one controller, no menu
  survivor). Run WITH graphics, each test also writes a 1920×1080 PNG (menu 3× [PLAY]; each game running
  from the hub) and asserts zero magenta.

## All-games wiring (stage ④, landed)

The launcher runs all FIVE cabinet games (founder's layout): Crank=Endless Sisyphus,
Red=«Последняя смена (Factory)», Green=Life Choices, HeightA=Lady Bug, HeightB=Home Alone;
Bang=Arcade Prototype and Joystick=Медитация are "soon" placeholders. Test Game left the shipped
config (its scene/rig stay in the project; tests drive it via an injected config). Three external repos
are consumed as `file:` packages on their arcade branches
(`com.aigamestudio.game-endless-sisyphus`, `com.aigamestudio.game-factory`,
`com.aigamestudio.game-lady-bug`); their scenes are in Build Settings by package path — the two `Main`
scenes (Sisyphus, Lady Bug) collide by short name, so their `entryScene` in the config is the full
package path. Compatibility debt, deliberately hub-side: `activeInputHandler=Both` (Lady Bug is on the
legacy Input Manager); external games play with their NATIVE keyboard input for now (moving them onto
`ArcadeInput` is a later increment per game); `nativeInput` slots get a watchdog-carried input pump and
the DDOL janitor sweeps their leaked singletons (see `LauncherReturn` above).

## Original path to stage ③ (for reference)

hub-v0 was a **keyed menu**: press RED to select. Stage ③ replaced instant selection with the real
cabinet interaction — *hold/crank a control for 5 seconds* to launch (a charge bar 0→1, decay on
release, exclusivity while one control is active, and the themed "rain of objects" fill). The launcher
became a deterministic state machine (`Idle → Charging(C) → Launching`) that is pure-C# testable, still
reading only `ArcadeInput`. For a while both selection models coexisted; the founder's gate-2 decision
(2026-07) retired instant-select for good — see "The menu" above.
```
