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
using UnityEngine.Video;
using AiGameStudio.ArcadeControls;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// The attract screen (attract-video-screen increment): HubMenu is a full-screen LOOPING video with
    /// sound and no game list or cursor — only the hold-to-launch stack renders above it. Covers: the
    /// video is alive and looping; the themed sprite rain is ONE-HOT per slot (a charging slot piles ONLY
    /// its own configured sprites — the rain IS the game hint, so cross-slot leakage would mislead the
    /// player); and the two contract screenshots (idle = clean video frame, charge = Sisyphus boulders
    /// over the video).
    ///
    /// NOTE (attract-screen v2, founder 2026-08-05): the screen is no longer text-free. The reel's top
    /// zones are masked out and the launcher draws its own credits ticker and its own game/cabinet title
    /// there — see <see cref="AttractOverlayPlayModeTests"/>. "Clean frame" below therefore means what it
    /// has always ASSERTED — nothing is charging, no bar, no rain — not an absence of text.
    /// </summary>
    public class AttractVideoPlayModeTests
    {
        private FakeBackend _fake;

        private static readonly BackendSnapshot Neutral = default;

        private void TakeOverInput()
        {
            foreach (var r in UnityEngine.Object.FindObjectsByType<ArcadeInputRunner>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(r);
            _fake = new FakeBackend();
            ArcadeInput.Initialize(_fake);
        }

        private IEnumerator LoadAttractScreen()
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null; // HubMenuController.Start builds the video screen
            yield return null;
            TakeOverInput();
        }

        private static AttractVideoScreen FindVideo()
        {
            var host = UnityEngine.Object.FindAnyObjectByType<HubMenuController>();
            Assert.IsNotNull(host, "HubMenuController (attract host) missing from HubMenu scene.");
            Assert.IsNotNull(host.Video, "The attract host must build its AttractVideoScreen.");
            return host.Video;
        }

        // Prepare/decode runs on a REAL-TIME background thread (AVFoundation), while an unfocused editor
        // spins test frames far faster than real time — so waits here are wall-clock, never frame counts.
        private static IEnumerator WaitRealtime(Func<bool> done, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
        }

        // The decode half of the video tests: where a decoder exists (interactive editor, player) frames
        // MUST come up (clock playback or the step fallback) — asserted; a batch editor that never decodes
        // skips EXPLICITLY instead of green-lying.
        private static void RequireDecodeOrBatchIgnore(AttractVideoScreen video, string what)
        {
            if (Application.isBatchMode && (!video.IsShowingFrames || video.Player.frame <= 0))
                Assert.Ignore($"VideoPlayer produced no frames in -batchmode; {what} is covered by the live-editor run (see increment §прогресса).");
        }

        // ---------------- video alive + looping ----------------

        [UnityTest]
        public IEnumerator Video_PlaysAndLoops_WithAudioTrack()
        {
            yield return LoadAttractScreen();
            AttractVideoScreen video = FindVideo();

            // Configuration asserts — valid with or without a live decoder.
            Assert.IsTrue(video.Player.isLooping, "The attract video must loop (attract reel).");
            StringAssert.EndsWith(AttractVideoScreen.FileName, video.Player.url,
                "The player points at the StreamingAssets attract clip.");
            Assert.AreEqual(VideoAudioOutputMode.Direct, video.Player.audioOutputMode,
                "The video's sound is the cabinet's attract audio (direct output).");
            var rt = (RectTransform)video.Screen.transform;
            Assert.Greater(rt.sizeDelta.x, 1900f, "The video screen covers the reference width.");
            Assert.Greater(rt.sizeDelta.y, 1070f, "The video screen covers the reference height.");

            yield return WaitRealtime(() => video.IsShowingFrames && video.Player.frame > 0, 20f);
            RequireDecodeOrBatchIgnore(video, "playback");
            Assert.IsTrue(video.IsShowingFrames,
                "The attract video must be presenting frames after load (clock playback, or the step fallback in an unfocused editor).");
            Assert.Greater(video.Player.frame, 0, "Frames must actually advance (decode is live).");
            Assert.Greater(video.Player.audioTrackCount, (ushort)0, "The clip carries its audio track.");

            // Skeptic #2: IsShowingFrames is "clock OR fallback", so it stays green even when the SILENT
            // step fallback fired. On the FOCUSED path (an interactive editor / the real cabinet) that
            // fallback must be impossible — the reel has to run on the real clock WITH audio. So where we
            // are actually focused, assert the audible clock path explicitly, not the OR. (An unfocused /
            // batch editor legitimately steps silently and is covered by the frame assertions above.)
            if (Application.isFocused && !Application.isBatchMode)
            {
                Assert.IsFalse(video.FallbackStepping,
                    "Focused editor / cabinet: the reel must run on the real clock (audio), never the silent step fallback.");
                Assert.IsTrue(video.Player.isPlaying,
                    "Focused editor / cabinet: the VideoPlayer clock must actually be playing (the audible attract path).");
            }
        }

        // ---------------- one-hot themed rain per slot ----------------

        // The names a slot is allowed to rain, read from the SHIPPED config (single rainSprite or the
        // rainSprites array — e.g. Lady Bug's ten flowers from the parallel increment).
        private static HashSet<string> ExpectedNames(LauncherSlot slot)
        {
            var names = new HashSet<string>();
            if (slot.rainSprites != null)
                foreach (string p in slot.rainSprites)
                    if (!string.IsNullOrEmpty(p)) names.Add(Path.GetFileName(p));
            if (!string.IsNullOrEmpty(slot.rainSprite) && names.Count == 0)
                names.Add(Path.GetFileName(slot.rainSprite));
            return names;
        }

        private IEnumerator ChargeAndAssertOneHot(HoldToLaunchController htl, LauncherConfig config,
            int slotIndex, BackendSnapshot held)
        {
            // Charge to ~30% — enough to pile sprites, far from launching.
            for (int i = 0; i < 15; i++)
            {
                _fake.Next = held;
                ArcadeInput.Update(0.1f);
                htl.Tick(0.1f);
                yield return null;
            }

            Assert.AreEqual(slotIndex, htl.ActiveSlot, $"Slot {slotIndex} should be the one charging.");
            Assert.Greater(htl.FlowerCount, 0, $"Slot {slotIndex}: charging must pile rain sprites.");

            HashSet<string> allowed = ExpectedNames(config.slots[slotIndex]);
            Assert.Greater(allowed.Count, 0, $"Slot {slotIndex} must declare rain sprites in the config.");
            foreach (RectTransform cell in htl.Flowers)
            {
                Sprite sprite = cell.GetComponent<Image>().sprite;
                Assert.IsNotNull(sprite, $"Slot {slotIndex}: every piled cell carries a sprite (no blank tiles).");
                Assert.IsTrue(allowed.Contains(sprite.name),
                    $"Slot {slotIndex} is ONE-HOT: piled sprite '{sprite.name}' must be one of its own ({string.Join(",", allowed)}).");
            }

            // Release and decay back to empty before the next slot charges (exclusivity handover).
            int guard = 0;
            while (htl.FlowerCount > 0 && guard < 40)
            {
                _fake.Next = Neutral;
                ArcadeInput.Update(0.1f);
                htl.Tick(0.1f);
                yield return null;
                guard++;
            }
            Assert.AreEqual(0, htl.FlowerCount, $"Slot {slotIndex}: the pile clears after release.");
        }

        [UnityTest]
        public IEnumerator ThemedRain_IsOneHotPerSlot()
        {
            yield return LoadAttractScreen();

            var htl = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl, "HoldToLaunchController must be wired into the HubMenu scene.");
            htl.AutoTick = false;
            LauncherConfig config = LauncherConfigLoader.LoadFromStreamingAssets();

            // Three representative slots: single-sprite themed (Sisyphus boulders, Factory bottles) and
            // the parallel increment's multi-sprite flower set (Lady Bug, HeightA).
            yield return ChargeAndAssertOneHot(htl, config, 0, new BackendSnapshot { CrankDeltaDegrees = 10f });
            yield return ChargeAndAssertOneHot(htl, config, 1, new BackendSnapshot { RedHeld = true });
            yield return ChargeAndAssertOneHot(htl, config, 5, new BackendSnapshot { HeightA = 1f });
        }

        // ---------------- contract screenshots ----------------

        [UnityTest]
        public IEnumerator Capture_VideoIdle_CleanFrame()
        {
            yield return LoadAttractScreen();
            AttractVideoScreen video = FindVideo();

            // The clean-frame contract holds regardless of the decoder: nothing but the video on screen.
            var htl = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl);
            Assert.AreEqual(0f, htl.Charge, 1e-4, "Idle: nothing charging.");
            Assert.IsFalse(htl.BarFill.gameObject.activeSelf, "Idle: the charge bar is hidden (clean frame).");
            Assert.AreEqual(0, htl.FlowerCount, "Idle: no rain piled.");

            yield return WaitRealtime(() => video.IsShowingFrames && video.Player.frame > 0, 20f);
            RequireDecodeOrBatchIgnore(video, "the idle frame capture");
            Assert.Greater(video.Player.frame, 0, "A decoded frame must be on screen for the idle capture.");

            // The reel opens near-black; seek to the middle so the founder-facing still shows the reel's
            // actual content rather than the dark intro.
            long mid = video.Player.frameCount > 0 ? (long)video.Player.frameCount / 2 : 400;
            video.Player.frame = mid;
            yield return WaitRealtime(() => System.Math.Abs(video.Player.frame - mid) < 40, 10f);

            yield return TryCapture("hub-video-idle.png");
        }

        [UnityTest]
        public IEnumerator Capture_VideoWithSisyphusCharge()
        {
            yield return LoadAttractScreen();
            AttractVideoScreen video = FindVideo();

            // Drive a REAL half-charge on the crank (Sisyphus slot 0) — boulders over the video.
            var htl = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl);
            htl.AutoTick = false;
            for (int i = 0; i < 25; i++)
            {
                _fake.Next = new BackendSnapshot { CrankDeltaDegrees = 10f };
                ArcadeInput.Update(0.1f);
                htl.Tick(0.1f);
                yield return null;
            }
            Assert.Greater(htl.Charge, 0.3f, "The Sisyphus slot charged for the pose.");
            Assert.Greater(htl.FlowerCount, 0, "Boulders are piling for the pose.");
            Assert.IsTrue(htl.BarFill.gameObject.activeSelf, "The charge bar shows while charging.");

            yield return WaitRealtime(() => video.IsShowingFrames && video.Player.frame > 0, 20f);
            RequireDecodeOrBatchIgnore(video, "the charge-over-video capture");
            Assert.Greater(video.Player.frame, 0, "A decoded frame must be behind the pose for the capture.");

            yield return TryCapture("hub-video-charge.png");
        }

        // -------- screenshot helper (mirrors the games-integration harness) --------

        private IEnumerator TryCapture(string fileName, int minNonBackground = -1)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                yield break;

            for (int i = 0; i < 6; i++) yield return null;

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

            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.isActiveAndEnabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
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

            // Non-blank evidence, twofold: pixels far from the camera background (bright content), OR
            // internal variance vs the first pixel (a dark video frame is nearly uniform to the bg test
            // yet still varies within itself; a NOT-rendered capture is perfectly uniform and fails both).
            Color32[] px = tex.GetPixels32();
            int magenta = 0, nonBackground = 0, varied = 0;
            Color32 bg = cam.backgroundColor;
            Color32 first = px.Length > 0 ? px[0] : default;
            foreach (var p in px)
            {
                if (p.r > 220 && p.g < 60 && p.b > 220) magenta++;
                if (Mathf.Abs(p.r - bg.r) + Mathf.Abs(p.g - bg.g) + Mathf.Abs(p.b - bg.b) > 40) nonBackground++;
                if (Mathf.Abs(p.r - first.r) + Mathf.Abs(p.g - first.g) + Mathf.Abs(p.b - first.b) > 12) varied++;
            }

            string dir = Environment.GetEnvironmentVariable("HUB_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Application.temporaryCachePath;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[attract-video] wrote screenshot '{path}' ({W}x{H}); magenta={magenta}px, nonBackground={nonBackground}px, varied={varied}px");

            cam.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(rt);
            if (tempCamGo != null) UnityEngine.Object.Destroy(tempCamGo);

            Assert.AreEqual(0, magenta, $"'{fileName}' must contain ZERO magenta (no missing shaders/sprites).");
            int floor = minNonBackground >= 0 ? minNonBackground : px.Length / 200;
            Assert.Greater(Mathf.Max(nonBackground, varied), floor,
                $"'{fileName}' is not blank — the scene rendered (bright content or in-frame variance).");
        }
    }
}
