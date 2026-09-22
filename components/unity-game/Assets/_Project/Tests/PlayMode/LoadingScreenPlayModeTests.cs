using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using AiGameStudio.ArcadeControls;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// The cabinet's loading screen (founder at the live cabinet, 2026-09-22: «показать что идет
    /// загрузка»). Both transitions the launcher owns are BLOCKING scene loads, so the one thing these
    /// tests have to prove is the ORDER: the screen is on the glass while the old scene is still
    /// running, and only then does the load happen. A screen raised on the same line as
    /// <c>SceneManager.LoadScene</c> would pass every "is it built?" assertion and still be invisible to
    /// the player, so every case here asserts the thing that actually matters — that the load has NOT
    /// been issued yet while the screen is up.
    ///
    /// The seam that makes this observable is <see cref="LoadingScreen.AutoLoad"/>: with it off the
    /// screen holds its pending load, which is exactly the frame the player sees first, frozen for a
    /// test to inspect and photograph.
    ///
    /// Covered here: the launch path, the return path (over a real game with a dense UI — the return
    /// watchdog fires inside code the launcher knows nothing about), the "no input is intercepted" rule,
    /// and the anti-flash hold that keeps the screen up until the attract reel has a frame to show.
    /// </summary>
    public class LoadingScreenPlayModeTests
    {
        private FakeBackend _fake;

        private static readonly BackendSnapshot Neutral = default;
        private static BackendSnapshot MenuHeld => new BackendSnapshot { MenuHeld = true };

        // Slot 2 in the shipped layout: «Спасибо, не надо» on the green button. Chosen as the return
        // case because its screen is DENSE — a full card-and-scales UI on its own canvases — and it
        // pumps ArcadeInput itself, so this test can drive the exit gesture directly.
        private const int DenseGameSlot = 2;
        private const string DenseGameScene = "ThanksNoThanks";
        private static BackendSnapshot HoldDenseGame => new BackendSnapshot { GreenHeld = true };

        [SetUp]
        public void SetUp()
        {
            LoadingScreen.AutoLoad = true;
        }

        [TearDown]
        public void TearDown()
        {
            LoadingScreen.AutoLoad = true;
            if (LoadingScreen.Instance != null)
                UnityEngine.Object.DestroyImmediate(LoadingScreen.Instance.gameObject);
        }

        // -------- rig --------

        private void TakeOverInput()
        {
            foreach (var r in UnityEngine.Object.FindObjectsByType<ArcadeInputRunner>())
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

        private IEnumerator OpenMenu()
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;
            TakeOverInput();
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
            yield return null;
        }

        // Charge a slot's own control to full — the cabinet's only launch gesture — and stop the moment
        // the loading screen appears (which, with AutoLoad off, is BEFORE any scene load).
        private IEnumerator ChargeUntilLoadingScreen(HoldToLaunchController htl, BackendSnapshot control)
        {
            int guard = 0;
            while (LoadingScreen.Instance == null && guard < 300)
            {
                Push(control);
                htl.Tick(0.1f);
                yield return null;
                guard++;
            }
            Assert.IsNotNull(LoadingScreen.Instance,
                "Charging a slot to full must raise the loading screen.");
        }

        // The watchdog's exit gesture: a SEEN release (plus its spawn-frame warmup), then a fresh press.
        private IEnumerator MenuGestureUntilLoadingScreen()
        {
            for (int i = 0; i < 4; i++) { Push(Neutral); yield return null; }

            int guard = 0;
            Push(MenuHeld);
            while (LoadingScreen.Instance == null && guard < 120)
            {
                Push(MenuHeld);
                yield return null;
                guard++;
            }
            Assert.IsNotNull(LoadingScreen.Instance,
                "The MENU exit gesture must raise the loading screen.");
        }

        private static void AssertLooksLikeTheCabinet(LoadingScreen screen, string expectedTitle)
        {
            Assert.IsTrue(screen.gameObject.activeInHierarchy, "The loading screen is active.");
            Assert.IsNotNull(screen.Canvas, "It brings its own canvas.");
            Assert.IsTrue(screen.Canvas.isActiveAndEnabled, "That canvas is live, not merely built.");
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, screen.Canvas.renderMode,
                "Screen-space-overlay is what puts it above everything any camera drew.");
            Assert.AreEqual(LoadingScreen.CanvasSortingOrder, screen.Canvas.sortingOrder);
            Assert.AreEqual(expectedTitle, screen.TitleLabel.text, "The title names what is loading.");
            Assert.AreEqual(LoadingScreen.Caption, screen.CaptionLabel.text);
            Color field = AttractZones.BackgroundColor;
            Assert.AreEqual(field.r, screen.Backdrop.color.r, 1e-3, "The field is the attract screen's own flat tone.");
            Assert.AreEqual(field.g, screen.Backdrop.color.g, 1e-3);
            Assert.AreEqual(field.b, screen.Backdrop.color.b, 1e-3);
            Assert.AreEqual(1f, screen.Backdrop.color.a, 1e-3, "The field is fully opaque.");
            Assert.IsNotNull(screen.TitleLabel.font, "The title is set in a real face, not a missing one.");
            Assert.IsNotNull(screen.CaptionLabel.font);
        }

        // -------- 1. launching a game --------

        [UnityTest]
        public IEnumerator Launch_PutsTheScreenOnTheGlass_BeforeTheBlockingLoad()
        {
            yield return OpenMenu();

            var htl = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl, "HoldToLaunchController must be wired into the HubMenu scene.");
            htl.AutoTick = false;
            LauncherSlot slot = htl.Config.slots[DenseGameSlot];

            LoadingScreen.AutoLoad = false; // hold the pending load: this is the frame the player sees
            yield return ChargeUntilLoadingScreen(htl, HoldDenseGame);

            LoadingScreen screen = LoadingScreen.Instance;
            AssertLooksLikeTheCabinet(screen, slot.displayName);
            Assert.IsFalse(screen.LoadIssued, "The screen is up BEFORE the load is issued.");
            Assert.IsTrue(htl.IsLaunching, "The launcher is latched while the loading screen owns the screen.");
            Assert.AreEqual(HubScenes.HubMenu, SceneManager.GetActiveScene().name,
                "The old scene is still running — the freeze has not started yet.");

            // And it stays that way: the screen is genuinely waiting to be drawn, not racing the load.
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(HubScenes.HubMenu, SceneManager.GetActiveScene().name,
                    "The load must not fire while the screen is held.");
                Assert.IsNotNull(LoadingScreen.Instance, "The screen stays up while it waits.");
                yield return null;
            }

            // What the player sees at the cabinet when a game starts.
            yield return Capture("loading-launch.png", screen, isolated: true);

            // Release it: the game loads underneath the screen, which then hands the screen over.
            LoadingScreen.AutoLoad = true;
            yield return WaitForActiveScene(DenseGameScene, 240);
            Assert.IsTrue(screen == null || screen.LoadIssued, "The held load was issued once released.");

            int guard = 0;
            while (LoadingScreen.Instance != null && guard < 240) { yield return null; guard++; }
            Assert.IsNull(LoadingScreen.Instance,
                "Once the game has drawn a frame the loading screen gets out of the way.");
        }

        // -------- 2. returning to the launcher, from a game with a dense UI --------

        [UnityTest]
        public IEnumerator Return_CoversADenseGameUi_BeforeTheBlockingLoad()
        {
            yield return OpenMenu();

            var htl = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl);
            htl.AutoTick = false;
            yield return ChargeUntilLoadingScreen(htl, HoldDenseGame);
            yield return WaitForActiveScene(DenseGameScene, 240);
            Assert.IsNotNull(LauncherReturn.Instance, "The launcher armed its return watchdog.");

            TakeOverInput(); // inside the game: it pumps ArcadeInput itself, so the test may take over
            int settle = 0;
            while (LoadingScreen.Instance != null && settle < 240) { Push(Neutral); yield return null; settle++; }

            // What is on the cabinet a frame before the player presses MENU — the screen the loading
            // screen has to cover. Kept as the companion still to loading-return.png.
            yield return Capture("game-before-return.png", null);

            LoadingScreen.AutoLoad = false; // hold the pending load: the player's first frame of the return
            yield return MenuGestureUntilLoadingScreen();

            LoadingScreen screen = LoadingScreen.Instance;
            AssertLooksLikeTheCabinet(screen, AttractOverlay.CabinetName);
            Assert.IsFalse(screen.LoadIssued, "The screen is up BEFORE the return load is issued.");
            Assert.AreEqual(DenseGameScene, SceneManager.GetActiveScene().name,
                "The game is still on screen — the screen went up first, the freeze has not started.");

            // It does not depend on the game's canvases or on their sorting: it brings its own, above
            // every canvas that game has.
            int highestGameCanvas = int.MinValue;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>())
            {
                if (c == screen.Canvas || c.transform.IsChildOf(screen.transform)) continue;
                if (!c.isActiveAndEnabled) continue;
                highestGameCanvas = Mathf.Max(highestGameCanvas, c.rootCanvas.sortingOrder);
            }
            Assert.Greater(highestGameCanvas, int.MinValue,
                "The game under test must actually have canvases — otherwise this proves nothing.");
            Assert.Greater(screen.Canvas.sortingOrder, highestGameCanvas,
                "The loading screen sorts above every canvas the game brought.");

            // The eye's proof, not the inspector's: the rendered frame is the loading screen's field,
            // not the game's UI.
            yield return Capture("loading-return.png", screen, assertCoversTheScreen: true);

            LoadingScreen.AutoLoad = true;
            yield return WaitForActiveScene(HubScenes.HubMenu, 240);
            Assert.IsNull(LauncherReturn.Instance, "The watchdog still destroys itself on the way back.");
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<HubMenuController>(), "The launcher is back.");
        }

        // -------- 3. it never takes input --------

        [UnityTest]
        public IEnumerator TheScreen_TakesNoInput_AndTheControlsKeepWorkingUnderIt()
        {
            yield return OpenMenu();

            var htl = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl);
            htl.AutoTick = false;

            bool hadEventSystem = EventSystem.current != null;
            LoadingScreen screen = LoadingScreen.Show("Проверка");
            yield return null;

            Assert.IsNull(screen.GetComponentInChildren<GraphicRaycaster>(true),
                "No GraphicRaycaster: the screen must be INCAPABLE of taking a pointer, not merely uninterested.");
            Assert.IsNull(screen.GetComponentInChildren<Selectable>(true),
                "Nothing on it is selectable.");
            foreach (Graphic g in screen.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(g.raycastTarget, $"'{g.name}' must not be a raycast target.");
            if (!hadEventSystem)
                Assert.IsNull(EventSystem.current, "The screen does not drag an EventSystem into the cabinet.");

            // The bar is alive: the notches march while frames are running. (They necessarily stand
            // still during the blocking load itself — which is exactly why the bar is drawn FULL, so a
            // frozen frame cannot be misread as progress stuck part-way.)
            float notchX = screen.Notches.anchoredPosition.x;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreNotEqual(notchX, screen.Notches.anchoredPosition.x,
                "The loading bar's notch row must march — a dead bar is what 'hung' looks like.");

            // Behavioural: with the screen up, the cabinet's own polled input still reaches the launcher —
            // a control charges its slot exactly as it would without it.
            for (int i = 0; i < 10; i++)
            {
                Push(HoldDenseGame);
                htl.Tick(0.1f);
                yield return null;
            }
            Assert.AreEqual(DenseGameSlot, htl.ActiveSlot, "The green button still charges its own slot.");
            Assert.Greater(htl.Charge, 0.1f, "The charge still grows under the loading screen.");

            UnityEngine.Object.DestroyImmediate(screen.gameObject);
        }

        // -------- 4. the anti-flash hold --------

        [UnityTest]
        public IEnumerator Return_HoldsTheScreen_UntilTheAttractReelHasItsFirstFrame()
        {
            // Driven through the built-in Test Game — the launcher's THIRD transition path (that scene
            // returns itself, no watchdog), so this also proves that path goes through the screen.
            yield return OpenMenu();

            var htl = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl);
            htl.AutoTick = false;
            htl.InitializeWith(new LauncherConfig
            {
                slots = new List<LauncherSlot>
                {
                    new LauncherSlot { controlName = "Crank", displayName = "Test Game",
                                       entryScene = HubScenes.TestGame, status = "installed" },
                }
            });

            yield return ChargeUntilLoadingScreen(htl, new BackendSnapshot { CrankDeltaDegrees = 10f });
            yield return WaitForActiveScene(HubScenes.TestGame, 240);
            TakeOverInput();
            int settle = 0;
            while (LoadingScreen.Instance != null && settle < 240) { Push(Neutral); yield return null; settle++; }

            // MENU: the test game returns to the launcher — behind the loading screen.
            for (int i = 0; i < 2; i++) { Push(Neutral); yield return null; }
            Push(MenuHeld);
            int guard = 0;
            while (LoadingScreen.Instance == null && guard < 120) { yield return null; guard++; }
            Assert.IsNotNull(LoadingScreen.Instance, "The test game's MENU return also raises the screen.");
            Assert.AreEqual(AttractOverlay.CabinetName, LoadingScreen.Instance.TitleLabel.text,
                "On the way back the screen names the cabinet — where the player is going.");
            Assert.IsTrue(LoadingScreen.Instance.WaitsForAttractReel,
                "A load of the menu holds for the reel (the menu's first frames have no picture yet).");

            yield return WaitForActiveScene(HubScenes.HubMenu, 240);

            // THE point: the menu is loaded, the attract clip is not decoding yet — and the screen is
            // still up, so the player never sees the empty menu the reel is about to fill.
            var video = UnityEngine.Object.FindAnyObjectByType<AttractVideoScreen>();
            Assert.IsNotNull(video, "The menu builds its attract screen on entry.");
            if (!video.IsShowingFrames)
                Assert.IsNotNull(LoadingScreen.Instance,
                    "The loading screen must outlive the scene load — dropping it there is the blank flash.");

            // And it does let go: on the reel's first frame, or on its own hard cap if the clip is broken.
            float waited = 0f;
            while (LoadingScreen.Instance != null && waited < LoadingScreen.MaxHoldSeconds + 3f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.IsNull(LoadingScreen.Instance,
                "The screen always lets go — the reel arrived, or the hard cap expired.");
        }

        // -------- capture (mirrors the games-integration screenshot harness) --------

        // Shoots the live frame at 1920×1080 into HUB_SHOT_DIR. Screen-space-overlay canvases are
        // invisible to an offscreen camera, so they are temporarily retargeted at it and put back
        // afterwards — the scene the test goes on to use is left exactly as it was.
        private IEnumerator Capture(string fileName, LoadingScreen screen,
            bool assertCoversTheScreen = false, bool isolated = false)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                yield break;

            const int W = 1920, H = 1080;

            // An offscreen camera cannot see screen-space-overlay canvases at all, so a shot has to
            // retarget them at it — and that is the one way this harness differs from the cabinet.
            //
            //   isolated:true  — a FRESH camera and only the loading screen's own canvas. On the cabinet
            //                    an overlay canvas is composited after the scene camera's post-processing,
            //                    so borrowing a scene camera (with a URP volume on it) would stamp that
            //                    camera's vignette and tonemap onto a screen that never gets them. This
            //                    is the honest picture of what the player sees.
            //   isolated:false — the scene's own camera and EVERY overlay canvas, so the shot can prove
            //                    the loading screen actually covers the game underneath it.
            Camera cam = isolated ? null : Camera.main;
            if (cam == null && !isolated)
                foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>())
                    if (c.isActiveAndEnabled) { cam = c; break; }
            GameObject tempCamGo = null;
            if (cam == null)
            {
                tempCamGo = new GameObject("CaptureCamera", typeof(Camera));
                cam = tempCamGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.transform.position = new Vector3(0f, 0f, -10f);
            }

            var retargeted = new List<Canvas>();
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>())
            {
                if (!canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    continue;
                if (isolated && canvas != screen.Canvas)
                    continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1f;
                retargeted.Add(canvas);
            }
            yield return null;

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            Color32[] px = tex.GetPixels32();
            int magenta = 0;
            foreach (var p in px)
                if (p.r > 220 && p.g < 60 && p.b > 220) magenta++;

            // The bottom half of a loading screen is nothing but its own field: if any of it is still
            // showing the game underneath, the screen is not covering the cabinet.
            int fieldPixels = 0, sampled = 0;
            Color32 field = AttractZones.BackgroundColor;
            for (int y = 0; y < H / 2; y += 8)
            {
                for (int x = 0; x < W; x += 8)
                {
                    Color32 p = px[y * W + x];
                    sampled++;
                    if (Mathf.Abs(p.r - field.r) <= 6 && Mathf.Abs(p.g - field.g) <= 6 && Mathf.Abs(p.b - field.b) <= 6)
                        fieldPixels++;
                }
            }

            string dir = Environment.GetEnvironmentVariable("HUB_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Application.temporaryCachePath;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[loading-screen] wrote '{path}' ({W}x{H}); magenta={magenta}px, " +
                      $"field={fieldPixels}/{sampled} of the lower half");

            cam.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(rt);
            if (tempCamGo != null) UnityEngine.Object.Destroy(tempCamGo);

            // Put the scene back the way it was before the shot.
            foreach (var canvas in retargeted)
            {
                if (canvas == null) continue;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
            }
            yield return null;

            Assert.AreEqual(0, magenta, $"'{fileName}' must contain ZERO magenta (no missing shaders/fonts).");
            if (assertCoversTheScreen)
                Assert.Greater(fieldPixels, (int)(sampled * 0.98f),
                    $"'{fileName}': the game's UI is still showing through the loading screen.");
        }
    }
}
