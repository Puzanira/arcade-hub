using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// Pins the cabinet's startup DISPLAY contract (founder, 2026-07-29, studio 7ab59e5):
    /// the arcade-hub standalone player must present a <b>1920×1080 fullscreen</b> surface —
    /// that is the physical cabinet screen every game (hub menu, attract video, the wired-in
    /// games) is composed against.
    ///
    /// This guards it at the level that actually decides the built player's window: the
    /// Player Settings baked into the standalone build (there is no runtime
    /// <c>Screen.SetResolution</c> in the hub — the player launches straight into these).
    ///   • <see cref="PlayerSettings.defaultScreenWidth"/>/<see cref="PlayerSettings.defaultScreenHeight"/>
    ///     = the 1920×1080 design surface (the fallback size when a monitor is not run at its
    ///     native resolution).
    ///   • <see cref="PlayerSettings.fullScreenMode"/> = <see cref="FullScreenMode.FullScreenWindow"/>
    ///     so the player fills the cabinet screen with no window chrome / letterbox gutter.
    /// On the cabinet's 1920×1080 panel, native-resolution fullscreen resolves to exactly this
    /// surface. A drift here (someone re-saves Player Settings at 1280×720, windowed, portrait,
    /// …) silently ships a mis-scaled cabinet — this test fails loudly instead.
    /// </summary>
    public class DisplayContractTests
    {
        const int CabinetWidth = 1920;
        const int CabinetHeight = 1080;

        [Test]
        public void DefaultScreenSize_IsCabinet_1920x1080()
        {
            Assert.AreEqual(CabinetWidth, PlayerSettings.defaultScreenWidth,
                "Cabinet display contract: standalone default width must be 1920.");
            Assert.AreEqual(CabinetHeight, PlayerSettings.defaultScreenHeight,
                "Cabinet display contract: standalone default height must be 1080.");
        }

        [Test]
        public void DefaultScreenSize_IsLandscape_16x9()
        {
            // Guards against a portrait / non-16:9 re-save independent of the exact pixel count.
            Assert.Greater(PlayerSettings.defaultScreenWidth, PlayerSettings.defaultScreenHeight,
                "The cabinet is a landscape screen (width > height).");
            Assert.AreEqual(16f / 9f,
                (float)PlayerSettings.defaultScreenWidth / PlayerSettings.defaultScreenHeight, 1e-3f,
                "The cabinet surface must be 16:9.");
        }

        [Test]
        public void StartupIsFullscreen_NoWindowChrome()
        {
            Assert.AreEqual(FullScreenMode.FullScreenWindow, PlayerSettings.fullScreenMode,
                "The cabinet player must launch fullscreen (FullScreenWindow) — no windowed chrome.");
        }
    }
}
