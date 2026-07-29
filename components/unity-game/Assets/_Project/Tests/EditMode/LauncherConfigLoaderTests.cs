using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// Done-contract #2 at the LOADER and MENU level (the FromJson throw is covered separately):
    /// a broken config file on disk logs one clear console error, degrades to an empty config,
    /// and the menu controller still comes up with zero rows — never a crash.
    /// </summary>
    public class LauncherConfigLoaderTests
    {
        private static readonly Regex LoadErrorPattern =
            new Regex(@"Failed to load launcher config .* empty slot list");

        private string _tempPath;

        [TearDown]
        public void TearDown()
        {
            if (_tempPath != null && File.Exists(_tempPath)) File.Delete(_tempPath);
            _tempPath = null;
        }

        private string WriteTempConfig(string content)
        {
            _tempPath = Path.Combine(Path.GetTempPath(), "hub-loader-test-" + Path.GetRandomFileName() + ".json");
            File.WriteAllText(_tempPath, content);
            return _tempPath;
        }

        [Test]
        public void LoadFrom_MalformedFile_LogsOneClearError_AndReturnsEmptyConfig()
        {
            string path = WriteTempConfig("{ this is not json ");

            LogAssert.Expect(LogType.Error, LoadErrorPattern);
            LauncherConfig config = LauncherConfigLoader.LoadFrom(path);
            LogAssert.NoUnexpectedReceived(); // exactly the one expected error, nothing else

            Assert.IsNotNull(config, "Loader must return a config object, never null.");
            Assert.IsNotNull(config.slots);
            Assert.AreEqual(0, config.slots.Count, "Broken file must degrade to an empty slot list.");
        }

        [Test]
        public void LoadFrom_MissingFile_LogsOneClearError_AndReturnsEmptyConfig()
        {
            string path = Path.Combine(Path.GetTempPath(), "hub-loader-test-does-not-exist.json");
            Assert.IsFalse(File.Exists(path));

            LogAssert.Expect(LogType.Error, LoadErrorPattern);
            LauncherConfig config = LauncherConfigLoader.LoadFrom(path);
            LogAssert.NoUnexpectedReceived();

            Assert.IsNotNull(config);
            Assert.AreEqual(0, config.slots.Count, "Missing file must degrade to an empty slot list.");
        }

        [Test]
        public void ShippedConfig_MatchesFoundersLayout_FiveInstalled_TwoSoon_NoTestGame()
        {
            // all-games-wiring (founder's layout 2026-07-26): FIVE installed games, TWO "soon" placeholders,
            // and NO built-in Test Game slot (its scene/rig stay in the project but leave the launcher menu).
            LauncherConfig config = LauncherConfigLoader.LoadFromStreamingAssets();

            int installed = 0, soon = 0;
            foreach (LauncherSlot s in config.slots)
            {
                if (s.IsInstalled) installed++;
                else soon++;
            }
            Assert.AreEqual(7, config.slots.Count, "Seven cabinet controls -> seven slots.");
            Assert.AreEqual(5, installed, "Sisyphus, Factory, Life Choices, Lady Bug, Home Alone are installed.");
            Assert.AreEqual(2, soon, "Arcade Prototype and Медитация are still 'soon'.");

            Assert.IsNull(config.slots.Find(s => s.displayName == "Test Game"),
                "Test Game must NOT appear in the shipped launcher menu.");

            // Each installed game sits on its founder-assigned control with a real entry scene.
            AssertInstalledOn(config, "Crank",       "Endless Sisyphus");
            AssertInstalledOn(config, "RedButton",   "Последняя смена (Factory)");
            AssertInstalledOn(config, "GreenButton", "Life Choices");
            AssertInstalledOn(config, "HeightA",     "Lady Bug");
            AssertInstalledOn(config, "HeightB",     "Home Alone");

            // The two "soon" slots are placeholders — planned control bindings, no entry scene.
            LauncherSlot bang = config.slots.Find(s => s.controlName == "BangButton");
            LauncherSlot joy = config.slots.Find(s => s.controlName == "Joystick");
            Assert.AreEqual("Arcade Prototype", bang.displayName);
            Assert.AreEqual("Медитация", joy.displayName);
            Assert.IsFalse(bang.IsInstalled, "Arcade Prototype is a 'soon' placeholder.");
            Assert.IsFalse(joy.IsInstalled, "Медитация is a 'soon' placeholder.");

            // Native-input games (legacy/raw input) carry the pump flag so the return watchdog stays live.
            Assert.IsTrue(config.slots.Find(s => s.displayName == "Lady Bug").nativeInput, "Lady Bug is native-input.");
            Assert.IsTrue(config.slots.Find(s => s.displayName == "Последняя смена (Factory)").nativeInput, "Factory is native-input.");
            Assert.IsFalse(config.slots.Find(s => s.displayName == "Endless Sisyphus").nativeInput, "Sisyphus pumps ArcadeInput itself.");
        }

        [Test]
        public void ShippedConfig_EverySlotCarriesALoadableRainSpriteSet()
        {
            // Founder's gate-2 wave: "по прямоугольникам не понятно, что запускается" — every slot rains
            // themed sprites. Each declared Resources path must actually LOAD (a typo'd path would silently
            // fall back to the solid tile; the runtime fallback stays, but the shipped config must be clean).
            LauncherConfig config = LauncherConfigLoader.LoadFromStreamingAssets();
            Assert.AreEqual(7, config.slots.Count);

            foreach (LauncherSlot slot in config.slots)
            {
                bool hasSet = slot.rainSprites != null && slot.rainSprites.Length > 0;
                bool hasSingle = !string.IsNullOrEmpty(slot.rainSprite);
                Assert.IsTrue(hasSet || hasSingle,
                    $"Slot '{slot.displayName}' must declare rainSprites or rainSprite.");

                if (hasSet)
                    foreach (string path in slot.rainSprites)
                        Assert.IsNotNull(Resources.Load<Sprite>(path),
                            $"Slot '{slot.displayName}': rainSprites path '{path}' must load from Resources.");
                else
                    Assert.IsNotNull(Resources.Load<Sprite>(slot.rainSprite),
                        $"Slot '{slot.displayName}': rainSprite path '{slot.rainSprite}' must load from Resources.");
            }
        }

        private static void AssertInstalledOn(LauncherConfig config, string control, string displayName)
        {
            LauncherSlot slot = config.slots.Find(s => s.controlName == control);
            Assert.IsNotNull(slot, $"Slot bound to {control} present.");
            Assert.AreEqual(displayName, slot.displayName, $"{control} launches {displayName}.");
            Assert.IsTrue(slot.IsInstalled, $"{displayName} is installed with a real entry scene.");
        }

        [Test]
        public void BrokenFile_ToLoader_ToHoldToLaunch_ComesUpEmpty_NoCrash()
        {
            // The degradation chain on the attract screen (the game list is gone — hold-to-launch is the
            // only config consumer on screen): broken JSON on disk -> loader degrades with one console
            // error -> the hold-to-launch stack builds with ZERO slots instead of crashing.
            string path = WriteTempConfig(@"{ ""slots"": [ { ""displayName"": ");

            LogAssert.Expect(LogType.Error, LoadErrorPattern);
            LauncherConfig config = LauncherConfigLoader.LoadFrom(path);

            var go = new GameObject("HoldToLaunchUnderTest");
            try
            {
                var htl = go.AddComponent<HoldToLaunchController>();
                htl.InitializeWith(config); // must not throw

                Assert.AreEqual(0f, htl.Charge, 1e-6f, "Empty config: nothing can charge.");
                Assert.AreEqual(-1, htl.ActiveSlot, "Empty config: no active slot.");
                Assert.AreEqual(0, htl.FlowerCount, "Empty config: nothing piled.");
                htl.Tick(0.1f); // a frame with zero slots must not throw either
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
