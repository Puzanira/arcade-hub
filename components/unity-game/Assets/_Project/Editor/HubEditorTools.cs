using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using AiGameStudio.ArcadeControls;

namespace AiGameStudio.ArcadeHub.Editor
{
    /// <summary>
    /// Batch (headless) tooling for the hold-to-launch increment: adds the <see cref="HoldToLaunchController"/>
    /// to the HubMenu scene, and renders posed screenshots of the charge bar + flower pile and the "СКОРО" overlay
    /// to an offscreen RenderTexture (no window needed). Invoked via <c>-executeMethod</c>.
    /// </summary>
    public static class HubEditorTools
    {
        private const string HubMenuScenePath = "Assets/_Project/Scenes/HubMenu.unity";

        /// <summary>Ensure the HubMenu scene's launcher object carries a HoldToLaunchController, then save.</summary>
        public static void AddHoldToLaunchToScene()
        {
            Scene scene = EditorSceneManager.OpenScene(HubMenuScenePath, OpenSceneMode.Single);

            HubMenuController menu = Object.FindAnyObjectByType<HubMenuController>();
            if (menu == null)
            {
                Debug.LogError("[HubEditorTools] HubMenuController not found in HubMenu scene.");
                EditorApplication.Exit(1);
                return;
            }

            var existing = menu.GetComponent<HoldToLaunchController>();
            if (existing == null)
            {
                menu.gameObject.AddComponent<HoldToLaunchController>();
                Debug.Log("[HubEditorTools] Added HoldToLaunchController to '" + menu.gameObject.name + "'.");
            }
            else
            {
                Debug.Log("[HubEditorTools] HoldToLaunchController already present.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[HubEditorTools] Saved HubMenu scene.");
        }

        // The two packaged games' entry scenes live inside their UPM packages; they load by name at runtime
        // (SceneManager.LoadScene) only if their package paths are in Build Settings after HubMenu/TestGame.
        private static readonly string[] GameScenePaths =
        {
            "Packages/com.aigamestudio.game-home-alone/Scenes/Apartment.unity",
            "Packages/com.aigamestudio.game-life-choices/Scenes/ThanksNoThanks.unity",
        };

        /// <summary>
        /// games-integration: append the two packaged game entry scenes to Build Settings (idempotent),
        /// after the hub's own HubMenu/TestGame. Fails the batch if a package scene can't be resolved.
        /// </summary>
        public static void AddGameScenesToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (string path in GameScenePaths)
            {
                if (scenes.Exists(s => s.path == path))
                {
                    Debug.Log("[HubEditorTools] Build scene already present: " + path);
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid))
                {
                    Debug.LogError("[HubEditorTools] Cannot resolve package scene (package unresolved?): " + path);
                    EditorApplication.Exit(2);
                    return;
                }

                scenes.Add(new EditorBuildSettingsScene(path, true));
                Debug.Log("[HubEditorTools] Added build scene: " + path);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[HubEditorTools] Build Settings now has " + EditorBuildSettings.scenes.Length + " scenes.");
        }

        // NOTE (attract-video-screen): the old CaptureMenu3Play / CaptureMenuOnly tooling is GONE. The game
        // list it rendered was deleted with the attract-video increment, so it only ever produced a dark
        // empty frame now — a stale, misleading still. The runtime attract screen is captured by the
        // AttractVideo PlayMode tests instead (hub-video-idle.png / hub-video-charge.png).

        /// <summary>Screenshot: charge bar at ~50% with the flower pile ~half-filling the screen. Path from -screenshotDir.</summary>
        public static void CaptureCharge50()
        {
            string dir = ArgValue("-screenshotDir") ?? Application.temporaryCachePath;
            CapturePose(Path.Combine(dir, "hub-charge50.png"), slotIndex: 0, charge: 0.5f, comingSoon: false);
        }

        /// <summary>
        /// Screenshot: the shared launch animation at ~50% charge on the first sprite-backed slot (the Lady
        /// Bug flower) — the bottom ~half of the screen piled with that game's flower. Writes hub-flower-rain.png.
        /// </summary>
        public static void CaptureFlower50()
        {
            string dir = ArgValue("-screenshotDir") ?? Application.temporaryCachePath;
            LauncherConfig config = LauncherConfigLoader.LoadFromStreamingAssets();
            // Pose the first slot carrying a loadable rainSprites SET (the Lady Bug flower set — this
            // still is the flower-rain still); single-rainSprite slots are posed by CaptureCharge50.
            int slot = 0;
            for (int i = 0; i < config.slots.Count; i++)
            {
                LauncherSlot s = config.slots[i];
                bool setLoads = false;
                if (s.rainSprites != null)
                    foreach (string p in s.rainSprites)
                        if (!string.IsNullOrEmpty(p) && Resources.Load<Sprite>(p) != null) { setLoads = true; break; }
                if (setLoads) { slot = i; break; }
            }
            CapturePose(Path.Combine(dir, "hub-flower-rain.png"), slotIndex: slot, charge: 0.5f, comingSoon: false);
        }

        /// <summary>Screenshot: a planned ("soon") slot at full charge showing the "СКОРО" overlay.</summary>
        public static void CapturePlannedSoon()
        {
            string dir = ArgValue("-screenshotDir") ?? Application.temporaryCachePath;
            LauncherConfig config = LauncherConfigLoader.LoadFromStreamingAssets();

            // Derive the soon-slot from the SHIPPED config — never a hardcoded index. (The old hardcoded
            // slot 2 is now "Life Choices", an INSTALLED game, so it forced a wrong "СКОРО" overlay onto a
            // real game.) The first non-installed slot is the honest planned example.
            int slot = -1;
            for (int i = 0; i < config.slots.Count; i++)
                if (!config.slots[i].IsInstalled) { slot = i; break; }

            if (slot < 0)
            {
                Debug.LogError("[HubEditorTools] No planned (\"soon\") slot in the shipped config — nothing to pose.");
                EditorApplication.Exit(3);
                return;
            }

            CapturePose(Path.Combine(dir, "hub-planned-soon.png"), slotIndex: slot, charge: 1.0f, comingSoon: true);
        }

        private static void CapturePose(string path, int slotIndex, float charge, bool comingSoon)
        {
            const int w = 1920, h = 1080;

            LauncherConfig config = LauncherConfigLoader.LoadFromStreamingAssets();

            // Offscreen camera + target texture (works in batchmode with graphics, no window).
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            rt.Create();
            var camGO = new GameObject("CaptureCamera", typeof(Camera));
            var cam = camGO.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.10f, 1f);
            cam.orthographic = false;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.targetTexture = rt;
            camGO.transform.position = new Vector3(0f, 0f, -10f);

            // attract-video-screen: no menu background any more (the runtime backdrop is the video,
            // which cannot decode in an edit-mode pose) — the dark camera clear stands in.

            // Hold-to-launch overlay, posed.
            var htlGO = new GameObject("CaptureHTL", typeof(HoldToLaunchController));
            var htl = htlGO.GetComponent<HoldToLaunchController>();
            htl.EditorPose(config, slotIndex, charge, comingSoon);
            RetargetCanvas(htl.Canvas, cam, 10f);

            Canvas.ForceUpdateCanvases();
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            // Magenta / missing-shader guard.
            int magenta = 0;
            Color32[] px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
                if (px[i].r > 220 && px[i].g < 40 && px[i].b > 220) magenta++;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[HubEditorTools] Wrote screenshot '{path}' ({w}x{h}); magentaPixels={magenta}");

            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
        }

        private static void RetargetCanvas(Canvas c, Camera cam, float planeDistance)
        {
            if (c == null) return;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = planeDistance;
        }

        private static string ArgValue(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }
    }
}
