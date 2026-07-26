using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using AiGameStudio.ArcadeControls;
using AiGameStudio.ArcadeHub;
using HomeAlone;
using ThanksNoThanks;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// games-integration (stage ②): the launcher now runs the two REAL packaged games. Drives the whole
    /// loop headless with a <see cref="FakeBackend"/> — menu → game scene → back to the menu → game again —
    /// for Home Alone (RedButton) and Life Choices (GreenButton, via the hold-to-launch charge), proving
    /// each game's scene loads with its controller alive, that MenuButton hands control back to the
    /// launcher (the <see cref="LauncherReturn"/> watchdog, since a packaged game doesn't know the hub's
    /// menu scene), and that a second entry is a clean start.
    ///
    /// When run WITH graphics (no -nographics) each test also captures a 1920×1080 PNG of the running game
    /// to <c>HUB_SHOT_DIR</c> and asserts zero magenta (no missing shaders/sprites). Under -nographics the
    /// capture is skipped — cam.Render() would hard-crash the batch editor — and only the loop is asserted.
    /// </summary>
    public class GamesIntegrationPlayModeTests
    {
        private FakeBackend _fake;

        private static BackendSnapshot RedHeld => new BackendSnapshot { RedHeld = true };
        private static BackendSnapshot GreenHeld => new BackendSnapshot { GreenHeld = true };
        private static BackendSnapshot MenuHeld => new BackendSnapshot { MenuHeld = true };
        private static BackendSnapshot JoyDown => new BackendSnapshot { Joystick = new Vector2(0f, -1f) };
        private static readonly BackendSnapshot Neutral = default;

        // Replace whatever backend the current scene's runner installed with a code-driven fake we pump.
        private void TakeOverInput()
        {
            var runner = UnityEngine.Object.FindAnyObjectByType<ArcadeInputRunner>();
            if (runner != null) UnityEngine.Object.DestroyImmediate(runner);
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

        private IEnumerator NavDown()
        {
            Push(JoyDown); yield return null; yield return null; // move once and latch
            Push(Neutral); yield return null; yield return null; // re-arm for the next push
        }

        // Enter Home Alone from the menu: highlight its row (slot 1) and press its bound RedButton. RED is
        // the menu's select control (hub-v0 instant-select coexists with hold-to-launch), so the highlighted
        // installed slot launches — Home Alone's own control launching Home Alone, from the menu.
        private IEnumerator EnterHomeAlone()
        {
            yield return NavDown(); // slot 0 (Test Game) -> slot 1 (Home Alone)
            Push(RedHeld);
            yield return WaitForActiveScene("Apartment", 180);
        }

        // Enter Life Choices via the genuine hold-to-launch charge on its bound GreenButton (a control the
        // menu does NOT consume): crank the charge to full and the controller loads the scene.
        private IEnumerator EnterLifeChoices(HoldToLaunchController htl)
        {
            int guard = 0;
            while (SceneManager.GetActiveScene().name != "ThanksNoThanks" && guard < 200)
            {
                _fake.Next = GreenHeld;
                ArcadeInput.Update(0.1f);
                htl.Tick(0.1f);
                yield return null;
                guard++;
            }
            yield return WaitForActiveScene("ThanksNoThanks", 5);
        }

        // The MenuButton exit gesture the launcher's return watchdog requires: a SEEN release (+ warmup),
        // then a fresh press. Matches Home Alone's own exit contract; a plain edge (Life Choices) passes too.
        private IEnumerator ExitToMenuByMenuButton()
        {
            for (int i = 0; i < 4; i++) { Push(Neutral); yield return null; } // release seen + warmup
            Push(MenuHeld);
            yield return WaitForActiveScene(HubScenes.HubMenu, 180);
        }

        // ---------------- Home Alone ----------------

        [UnityTest]
        public IEnumerator HomeAlone_LaunchesFromMenu_ReturnsByMenuButton_ReentersClean()
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;
            TakeOverInput();

            yield return EnterHomeAlone();

            var game = UnityEngine.Object.FindAnyObjectByType<GameController>();
            Assert.IsNotNull(game, "Home Alone's GameController must be alive after launching Apartment.");
            Assert.IsNotNull(game.Session, "The match session booted.");
            Assert.IsTrue(LauncherReturn.Instance != null,
                "The launcher armed its return watchdog for the external game.");

            TakeOverInput(); // re-take input inside the game scene (its runner reset ArcadeInput on load)
            yield return TryCapture("hub-into-homealone.png");

            // MenuButton hands control back to the launcher.
            yield return ExitToMenuByMenuButton();
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<GameController>(FindObjectsSortMode.None).Length,
                "Returning to the menu tears the game scene down (no GameController survivor).");
            Assert.IsNull(LauncherReturn.Instance, "The return watchdog destroyed itself once the menu is up.");
            var menu = UnityEngine.Object.FindAnyObjectByType<HubMenuController>();
            Assert.IsNotNull(menu, "The launcher menu is back.");

            // Second entry is a clean start: exactly one controller, no menu survivor in the game scene.
            TakeOverInput();
            yield return EnterHomeAlone();
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<GameController>(FindObjectsSortMode.None).Length,
                "Re-entering Home Alone is a clean start (one GameController, never two).");
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<HubMenuController>(FindObjectsSortMode.None).Length,
                "No launcher menu survives into the game scene (no DontDestroyOnLoad leak).");

            // Land back in the menu so the test leaves a clean scene.
            TakeOverInput();
            yield return ExitToMenuByMenuButton();
        }

        // ---------------- Life Choices ----------------

        [UnityTest]
        public IEnumerator LifeChoices_LaunchesByHoldToLaunch_ReturnsByMenuButton_ReentersClean()
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;
            TakeOverInput();

            var htl = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl, "HoldToLaunchController must be wired into the HubMenu scene.");
            htl.AutoTick = false;

            yield return EnterLifeChoices(htl);

            var driver = UnityEngine.Object.FindAnyObjectByType<GameDriver>();
            Assert.IsNotNull(driver, "Life Choices' GameDriver must be alive after launching ThanksNoThanks.");
            Assert.IsNotNull(driver.Game, "The game constructed on boot.");
            Assert.IsTrue(LauncherReturn.Instance != null,
                "The launcher armed its return watchdog for the external game.");

            TakeOverInput();
            driver.DebugPreviewArcadeShot(); // pose a representative mid-life card (the game's own screenshot hook)
            yield return TryCapture("hub-into-lifechoices.png");

            yield return ExitToMenuByMenuButton();
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<GameDriver>(FindObjectsSortMode.None).Length,
                "Returning to the menu tears the game scene down (no GameDriver survivor).");
            Assert.IsNull(LauncherReturn.Instance, "The return watchdog destroyed itself once the menu is up.");
            var menu = UnityEngine.Object.FindAnyObjectByType<HubMenuController>();
            Assert.IsNotNull(menu, "The launcher menu is back.");

            // Second entry is a clean start. (Re-take input first: the reloaded HubMenu's runner reset
            // ArcadeInput back to the keyboard backend on the way in.)
            TakeOverInput();
            var htl2 = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl2);
            htl2.AutoTick = false;
            yield return EnterLifeChoices(htl2);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<GameDriver>(FindObjectsSortMode.None).Length,
                "Re-entering Life Choices is a clean start (one GameDriver, never two).");
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<HubMenuController>(FindObjectsSortMode.None).Length,
                "No launcher menu survives into the game scene (no DontDestroyOnLoad leak).");

            TakeOverInput();
            yield return ExitToMenuByMenuButton();
        }

        // ---------------- Menu screenshot (3× [PLAY]) ----------------

        [UnityTest]
        public IEnumerator Capture_HubMenu_ShowsThreeInstalledGames()
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var menu = UnityEngine.Object.FindAnyObjectByType<HubMenuController>();
            Assert.IsNotNull(menu, "HubMenuController present.");

            // Three rows read "[ PLAY ]" (Test Game, Home Alone, Life Choices are installed).
            int playRows = 0;
            for (int i = 0; i < menu.RowCount; i++)
                if (menu.Rows[i].text.Contains("[ PLAY ]")) playRows++;
            Assert.AreEqual(3, playRows, "Exactly three menu rows are installed and show [ PLAY ].");

            yield return TryCapture("hub-menu-3play.png");
        }

        // -------- screenshot helper (mirrors the packaging screenshot harness) --------

        // Capture the active scene (game world/UI) to a PNG under HUB_SHOT_DIR and assert zero magenta.
        // No-op (test still passes) when there is no graphics device — cam.Render() crashes under -nographics.
        private IEnumerator TryCapture(string fileName)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                yield break;

            for (int i = 0; i < 4; i++) yield return null; // let the scene settle/render its HUD

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

            // Pull ScreenSpaceOverlay UI into the camera's render so it lands in the RenderTexture.
            var reparented = new List<Canvas>();
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
