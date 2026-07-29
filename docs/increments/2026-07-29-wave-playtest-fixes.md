# Increment: wave-playtest-fixes (батч по гейту-2 основательницы, 2026-07-29)

Founder playtest of the wave (attract-video + all-games + serial 0.3.0 via file:)
produced this bug list. Fix ALL as one batch, then skeptic → suite → founder re-check.

Screen contract (founder, 2026-07-29): cabinet display = **1920×1080** (spec
committed, studio 7ab59e5).

## Bugs

### 1. Factory: красная кнопка не продолжает игру (repro needed)
In-hub launch of «Последняя смена (Factory)» (RedButton slot, nativeInput=true):
on screens that say «КРАСНАЯ КНОПКА — ДАЛЕЕ», pressing Э does not advance.
Standalone factory worked at the previous gate. Suspects (verify, don't guess):
who pumps ArcadeInput inside a nativeInput game in-process (LauncherReturn
watchdog runner vs scene runner after the 0.3.0 ArcadeInputRunner change);
edge (rising Held) loss in factory's ArcadeInputBridge (`ownsBackend=false`
path, `redWasHeld`); Prime/held-carryover from the 5s launch hold.
NOTE: if the root cause lands in the arcade-controls package — DO NOT edit that
tree (founder is live-testing in its editor); report the diagnosis back instead.

### 2. lady_bug: после победы запускается Сизиф (root cause KNOWN)
Both sisyphus and lady_bug entry scenes are named `Main`. lady_bug reloads its
scene BY NAME: `WinSequence.cs:284` and `PauseController.cs:74`
(`SceneManager.LoadScene(SceneManager.GetActiveScene().name)`) — in the hub
build the first `Main` is Sisyphus's. Fix in OUR branch of
/Users/ipuzanova/AI_GAME_STUDIO/external/lady_bug (branch arcade-contract-repack):
reload via `SceneManager.GetActiveScene().buildIndex` (collision-proof both
standalone and in-hub). Check for any other by-name loads in the package.

### 3. lady_bug: на клавиатуре коровка не управляется (repro needed)
In-hub, keyboard-fallback banner shows the keys but the ladybug does not respond.
Suspects: his gameplay input path in keyboard mode (legacy Input vs hub's active
input handling), the f58e724 fallback only switching the START screen mode but
gameplay still polling his serial readers, DDOL/janitor sweeping his input
objects. Reproduce in the hub project, fix in our lady_bug branch.

### 4. home-alone (кот): Escape-коллизия выхода из мини-игр
Escape is now the global MenuButton (exit to hub launcher). The cat game uses
Escape to exit mini-games → collision. Move mini-game exit to **BangButton «!»**
(ArcadeInput.BangButton) AND update every UI hint that mentions Escape/выход.

### 5. home-alone (кот): выбор мини-игры → зелёная кнопка
Mini-game selection must be on **GreenButton** (+ update hints accordingly).
Tree note: home-alone has uncommitted foreign work (slalom/meow from a parallel
session, idle ≥1h). Treat the tree state as a given, build on top, do NOT
commit, do NOT revert anything.

### 6. Хаб: контрактный тест разрешения
Add a test pinning the hub's startup display contract: 1920×1080 fullscreen
(Screen.SetResolution path or ProjectSettings default — implement the check at
the level that actually guards the cabinet; document what it pins).

## Verification

- Suites, honest-exit discipline (only «suite GREEN (exit 0)» + fresh
  .studio/runs dir, no piped exits, editors CLOSED for headless):
  - `studio-go suite --project arcade-hub` (binary /Users/ipuzanova/AI_GAME_STUDIO/bin/studio-go)
  - `studio-go suite --project home-alone` if home-alone changed
- Regression tests for each fixed bug where the harness allows (scene-reload
  target, factory red-advance in-hub, cat exit gesture, green selection).
- Batch screenshots where visuals changed (0 magenta, hints readable) — save to
  the session scratchpad for Maintainer eyeballs.
- lady_bug/factory: kit contract_check.py must stay green in their repos.

## Boundaries

- arcade-controls tree: READ-ONLY this increment (founder live-testing there).
- external repos: fixes only in our existing branches (lady_bug
  arcade-contract-repack, factory arcade-contract-repack), no rebases now.
- No commits anywhere — Maintainer commits after the founder gate.
- Hub editor is closed; open/close editors yourself if needed, close before
  headless. NEVER touch the arcade-controls editor (founder's session).
