using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using AiGameStudio.ArcadeControls;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// all-games-wiring (founder's layout 2026-07-26): the launcher runs SIX real games. Drives the whole
    /// loop headless with a <see cref="FakeBackend"/> — menu → game scene → back to the menu — for every
    /// installed slot, entering each the ONLY way the cabinet launches (founder's gate-2 decision:
    /// instant-select is gone): holding/cranking the slot's OWN control until the hold-to-launch charge
    /// reaches full. Each case proves the game's scene loads with a live root object (its own namespace),
    /// that the launcher armed its <see cref="LauncherReturn"/> watchdog (a packaged game does not know
    /// the hub's menu scene), and that the MenuButton exit gesture hands the cabinet back.
    ///
    /// External games play with their NATIVE input; the launcher's universal MenuButton return works even
    /// for the two that never pump ArcadeInput (Lady Bug on the legacy Input Manager, Factory on the raw new
    /// Input System) because the watchdog carries its own input pump for <see cref="LauncherSlot.nativeInput"/>
    /// slots. Sisyphus / Home Alone / Life Choices pump ArcadeInput themselves.
    ///
    /// When run WITH graphics (no -nographics) each game case captures a 1920×1080 PNG of the running game to
    /// <c>HUB_SHOT_DIR</c> and asserts zero magenta (no missing shaders/sprites). Under -nographics the
    /// capture is skipped — cam.Render() would hard-crash the batch editor — and only the loop is asserted.
    /// </summary>
    public class GamesIntegrationPlayModeTests
    {
        private FakeBackend _fake;

        private static BackendSnapshot MenuHeld => new BackendSnapshot { MenuHeld = true };
        private static readonly BackendSnapshot Neutral = default;

        // The six installed slots' OWN controls in the shipped founder layout — the launch gesture is
        // "engage this control until the charge is full" (the only launch path).
        private static BackendSnapshot HoldSisyphus => new BackendSnapshot { CrankDeltaDegrees = 10f }; // Crank
        private static BackendSnapshot HoldFactory => new BackendSnapshot { RedHeld = true };           // RedButton
        private static BackendSnapshot HoldLifeChoices => new BackendSnapshot { GreenHeld = true };     // GreenButton
        private static BackendSnapshot HoldLadyBug => new BackendSnapshot { HeightA = 1f };             // HeightA
        private static BackendSnapshot HoldHomeAlone => new BackendSnapshot { HeightB = 1f };           // HeightB
        // Joystick: the slot engages on deflection MAGNITUDE past the tuning threshold (0.5), so a full
        // push on one axis is the gesture — the same "hold your own control" rule as the buttons.
        private static BackendSnapshot HoldMeditation => new BackendSnapshot { Joystick = Vector2.up };  // Joystick

        // Replace whatever backend the current scene's runner installed with a code-driven fake we pump.
        // NEVER destroys the watchdog's own runner: for nativeInput games it is the production return path
        // and killing it would fake the proof (use ExitToMenuViaWatchdogRunner there, not this).
        private void TakeOverInput()
        {
            foreach (var r in UnityEngine.Object.FindObjectsByType<ArcadeInputRunner>(FindObjectsSortMode.None))
                if (LauncherReturn.Instance == null || r.gameObject != LauncherReturn.Instance.gameObject)
                    UnityEngine.Object.DestroyImmediate(r);
            _fake = new FakeBackend();
            ArcadeInput.Initialize(_fake);
        }

        private void Push(BackendSnapshot snapshot, float dt = 0.1f)
        {
            _fake.Next = snapshot;
            ArcadeInput.Update(dt);
        }

        private IEnumerator WaitForActiveScene(string sceneName, int maxFrames)
        {
            int f = 0;
            while (SceneManager.GetActiveScene().name != sceneName && f < maxFrames)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name,
                $"Active scene did not become '{sceneName}' within {maxFrames} frames.");
            yield return null; // let the new scene's Awake/Start settle
        }

        // Enter a game the ONLY way the cabinet launches: hold/crank the slot's own control and drive the
        // hold-to-launch charge to full (AutoTick off, fixed dt for determinism). No cursor, no select
        // button — the menu highlight is informational and cannot launch anything.
        private IEnumerator EnterByHold(BackendSnapshot heldControl, string sceneName)
        {
            var htl = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl, "HoldToLaunchController must be wired into the HubMenu scene.");
            htl.AutoTick = false;

            int guard = 0;
            while (SceneManager.GetActiveScene().name != sceneName && guard < 200)
            {
                _fake.Next = heldControl;
                ArcadeInput.Update(0.1f);
                htl.Tick(0.1f);
                yield return null;
                guard++;
            }
            yield return WaitForActiveScene(sceneName, 5);
        }

        // The MenuButton exit gesture the watchdog requires: a SEEN release (+ warmup), then a fresh
        // press. For ArcadeInput-NATIVE games only — here the test pumps ArcadeInput itself, standing in
        // for the game's own per-frame pump (Sisyphus/Home Alone/Life Choices pump ArcadeInput.Update).
        private IEnumerator ExitToMenuByMenuButton()
        {
            for (int i = 0; i < 4; i++) { Push(Neutral); yield return null; } // release seen + warmup
            Push(MenuHeld);
            yield return WaitForActiveScene(HubScenes.HubMenu, 240);
        }

        // The SAME exit gesture for nativeInput games (Lady Bug, Factory), driven HONESTLY through the
        // production path: those games never pump ArcadeInput, so the watchdog's own ArcadeInputRunner
        // (attached by LauncherReturn.ArmFor for nativeInput slots) is the ONLY pump. The test must NOT
        // destroy it and must NOT call ArcadeInput.Update itself — it only swaps the backend for a fake
        // and lets the LIVE runner poll it each frame, exactly as the keyboard is polled on the cabinet.
        private IEnumerator ExitToMenuViaWatchdogRunner()
        {
            Assert.IsNotNull(LauncherReturn.Instance, "The return watchdog must be alive inside the game.");
            var runner = LauncherReturn.Instance.GetComponent<ArcadeInputRunner>();
            Assert.IsNotNull(runner,
                "A nativeInput slot's watchdog must carry its own ArcadeInputRunner (the game never pumps ArcadeInput).");

            _fake = new FakeBackend();               // swap the backend only; the runner keeps pumping
            ArcadeInput.Initialize(_fake);
            _fake.Next = Neutral;
            for (int i = 0; i < 4; i++) yield return null; // release SEEN via the live runner's pump

            _fake.Next = MenuHeld;                   // fresh press — again read by the runner, not by us
            yield return WaitForActiveScene(HubScenes.HubMenu, 240);
        }

        // -------- DDOL inspection (the hub's janitor must sweep leaked external singletons) --------

        private static void AssertNoDdolObjectsFrom(string ns, string because)
        {
            var probe = new GameObject("~DdolProbe");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            try
            {
                foreach (GameObject root in probe.scene.GetRootGameObjects())
                {
                    if (root == probe) continue;
                    foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null) continue;
                        string tns = mb.GetType().Namespace;
                        if (tns != null && (tns == ns || tns.StartsWith(ns + ".")))
                            Assert.Fail($"{because} (leaked: {mb.GetType().FullName} on '{root.name}')");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        // A game is "alive" when a MonoBehaviour from its own namespace is present and active in the scene.
        private static void AssertGameAlive(string ns, string because)
        {
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                string tns = mb.GetType().Namespace;
                if (tns != null && (tns == ns || tns.StartsWith(ns + ".")))
                    return;
            }
            Assert.Fail(because);
        }

        // Shared body: enter an installed game by charging its own control to full, prove it is alive +
        // the watchdog armed, shoot it, then return to the launcher and prove the launcher is back up.
        // For nativeInput games the exit gesture runs through the LIVE watchdog runner (the production
        // path); for ArcadeInput-native games the test pumps ArcadeInput itself, standing in for the
        // game's own pump.
        private IEnumerator RunGame(BackendSnapshot heldControl, string sceneName, string ns, string shot, bool nativeInput)
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;
            TakeOverInput(); // menu phase: the MENU's scene runner is hub-owned and replaceable

            yield return EnterByHold(heldControl, sceneName);

            AssertGameAlive(ns, $"{ns}: a root object must be alive after launching '{sceneName}' from the menu.");
            Assert.IsTrue(LauncherReturn.Instance != null,
                "The launcher armed its return watchdog for the external game.");

            if (nativeInput)
            {
                // Do NOT touch the watchdog's runner — it is the only ArcadeInput pump in this scene and
                // the exact production path for the MenuButton return. Screenshot first (backend neutral).
                yield return TryCapture(shot);
                yield return ExitToMenuViaWatchdogRunner();
            }
            else
            {
                TakeOverInput(); // re-take input inside the game scene (the game pumps ArcadeInput itself)
                yield return TryCapture(shot);
                yield return ExitToMenuByMenuButton();
            }

            Assert.IsNull(LauncherReturn.Instance, "The return watchdog destroyed itself once the menu is up.");
            Assert.AreEqual(HubScenes.HubMenu, SceneManager.GetActiveScene().name,
                $"MenuButton must return to the launcher from {ns}.");
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<HubMenuController>(), "The launcher menu is back.");
        }

        // ---------------- the five installed games ----------------

        [UnityTest]
        public IEnumerator Sisyphus_LaunchesByCrankHold_IsAlive_ReturnsByMenuButton()
        {
            // Code-genned game: its Main scene is empty; Bootstrap builds SisyphusGame on scene load. This
            // proves the boot fires when the launcher loads the packaged scene (not just at play start).
            yield return RunGame(HoldSisyphus, "Main", "EndlessSisyphus", "hub-into-sisyphus.png", nativeInput: false);
        }

        [UnityTest]
        public IEnumerator Factory_LaunchesByRedHold_ReturnsViaWatchdogPump_JanitorSweepsDdol_ReentryWorks()
        {
            // Red HOLD charges the Factory slot to full — the founder's gate-2 fix: red no longer
            // instant-launches the highlighted row (that path is gone), it charges its OWN slot.
            yield return RunGame(HoldFactory, "Boot", "LastShift", "hub-into-factory.png", nativeInput: true);

            // The hub's DDOL janitor swept Factory's leaked singletons (GameManager from Boot's Ensure,
            // ArduinoInputBridge from its global RuntimeInit). Destroy is deferred — let the frame close.
            yield return null;
            yield return null;
            AssertNoDdolObjectsFrom("LastShift",
                "After returning from Factory the DDOL scene must hold no factory objects (hub janitor).");

            // Re-entry after the sweep works: Factory's Boot recreates what it needs (GameManager.Ensure).
            TakeOverInput();
            yield return EnterByHold(HoldFactory, "Boot");
            AssertGameAlive("LastShift", "Factory must boot again cleanly after the janitor sweep.");

            // Leave the cabinet on the menu — again through the live watchdog runner.
            yield return ExitToMenuViaWatchdogRunner();
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<HubMenuController>(), "The launcher menu is back.");
        }

        [UnityTest]
        public IEnumerator LifeChoices_LaunchesByGreenHold_IsAlive_ReturnsByMenuButton()
        {
            yield return RunGame(HoldLifeChoices, "ThanksNoThanks", "ThanksNoThanks", "hub-into-lifechoices.png", nativeInput: false);
        }

        [UnityTest]
        public IEnumerator LadyBug_LaunchesByHeightAHold_IsAlive_ReturnsViaWatchdogPump()
        {
            // Legacy Input Manager game: never pumps ArcadeInput — the return MUST go through the
            // watchdog's own runner, which this test leaves alive (the honest production path).
            yield return RunGame(HoldLadyBug, "Main", "LadyBug", "hub-into-ladybug.png", nativeInput: true);
        }

        [UnityTest]
        public IEnumerator HomeAlone_LaunchesByHeightBHold_IsAlive_ReturnsByMenuButton()
        {
            yield return RunGame(HoldHomeAlone, "Apartment", "HomeAlone", "hub-into-homealone.png", nativeInput: false);
        }

        [UnityTest]
        public IEnumerator Meditation_LaunchesByJoystickHold_IsAlive_ReturnsByMenuButton()
        {
            // ArcadeInput-native, and its entry scene carries its own ArcadeInputRunner — so the slot is
            // nativeInput:false and the watchdog must NOT bring a second pump (a doubled pump would count
            // every crank degree twice, which on this game is the whole collection mechanic).
            //
            // The game answers the same MenuButton press itself, by ending its run and standing its title
            // back up IN PLACE: it loads no scene, so the launcher's watchdog is the only thing deciding
            // where the cabinet goes next. That is what makes this return unambiguous.
            yield return RunGame(HoldMeditation, "Game", "Meditation", "hub-into-meditation.png", nativeInput: false);
        }

        // ---------------- founder's gate-2 regression: red press must NOT instant-launch ----------------

        [UnityTest]
        public IEnumerator RedPress_DoesNotInstantLaunch_ItChargesTheFactorySlot()
        {
            // The live gate-2 bug: pressing RED launched the HIGHLIGHTED row (cursor sits on Sisyphus by
            // default) instead of charging Factory's red-bound slot. With instant-select removed, a red
            // press must launch NOTHING immediately — it only feeds the Factory slot's charge.
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;
            TakeOverInput();

            var htl = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl, "HoldToLaunchController must be wired into the HubMenu scene.");
            htl.AutoTick = false;

            // (attract screen: there is no cursor at all any more — a red press has nothing to "select".)

            // Hold RED for ~1s of fixed dt: far past any instant edge, far short of the 5s full charge.
            var red = new BackendSnapshot { RedHeld = true };
            for (int i = 0; i < 10; i++)
            {
                _fake.Next = red;
                ArcadeInput.Update(0.1f);
                htl.Tick(0.1f);
                yield return null;
            }

            Assert.AreEqual(HubScenes.HubMenu, SceneManager.GetActiveScene().name,
                "A red press must NOT launch any scene instantly (instant-select is removed).");
            Assert.AreEqual(1, htl.ActiveSlot, "The red press charges its OWN slot — Factory (slot 1).");
            Assert.Greater(htl.Charge, 0.1f, "The Factory slot is charging while red is held.");

            // Release: the charge decays, still no launch — the menu is intact.
            for (int i = 0; i < 15; i++)
            {
                _fake.Next = Neutral;
                ArcadeInput.Update(0.1f);
                htl.Tick(0.1f);
                yield return null;
            }
            Assert.AreEqual(HubScenes.HubMenu, SceneManager.GetActiveScene().name);
            Assert.AreEqual(0f, htl.Charge, 1e-3, "Released early, the charge decays to zero — nothing launched.");
        }

        // (The old 5×[PLAY] menu screenshot test left with the list — the attract screen's video/rain
        // captures live in AttractVideoPlayModeTests.)

        // -------- screenshot helper (mirrors the packaging screenshot harness) --------

        private IEnumerator TryCapture(string fileName)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                yield break;

            for (int i = 0; i < 6; i++) yield return null; // let the scene settle/render its HUD

            const int W = 1920, H = 1080;

            Camera cam = Camera.main;
            if (cam == null)
                foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    if (c.isActiveAndEnabled) { cam = c; break; }
            GameObject tempCamGo = null;
            if (cam == null)
            {
                tempCamGo = new GameObject("CaptureCamera", typeof(Camera));
                cam = tempCamGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.06f, 0.10f, 1f);
                cam.transform.position = new Vector3(0f, 0f, -10f);
            }

            var reparented = new System.Collections.Generic.List<Canvas>();
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.isActiveAndEnabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
                    reparented.Add(canvas);
                }
            }
            yield return null;

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            // NB: this shoots 1920×1080 no matter how big the editor's Game view is, and a game that
            // letterboxes its own fixed design frame to the SCREEN (Медитация: DesignStage.Fit) therefore
            // lands in the middle of the shot at Screen/1920 of its size. That is the game reading the
            // surface it was actually given, not a broken picture — on the cabinet the screen IS 1920×1080
            // and the frame is full. Judge composition from that game's own 1920×1080 frames.
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            Color32[] px = tex.GetPixels32();
            int magenta = 0, nonBackground = 0;
            Color32 bg = cam.backgroundColor;
            foreach (var p in px)
            {
                if (p.r > 220 && p.g < 60 && p.b > 220) magenta++;
                if (Mathf.Abs(p.r - bg.r) + Mathf.Abs(p.g - bg.g) + Mathf.Abs(p.b - bg.b) > 40) nonBackground++;
            }

            string dir = Environment.GetEnvironmentVariable("HUB_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Application.temporaryCachePath;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[games-integration] wrote screenshot '{path}' ({W}x{H}); magenta={magenta}px, nonBackground={nonBackground}px");

            cam.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(rt);
            if (tempCamGo != null) UnityEngine.Object.Destroy(tempCamGo);

            Assert.AreEqual(0, magenta, $"'{fileName}' must contain ZERO magenta (no missing shaders/sprites).");
            Assert.Greater(nonBackground, px.Length / 200, $"'{fileName}' is not blank — the scene rendered.");
        }
    }
}
