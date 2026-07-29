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

        private static void AssertInstalledOn(LauncherConfig config, string control, string displayName)
        {
            LauncherSlot slot = config.slots.Find(s => s.controlName == control);
            Assert.IsNotNull(slot, $"Slot bound to {control} present.");
            Assert.AreEqual(displayName, slot.displayName, $"{control} launches {displayName}.");
            Assert.IsTrue(slot.IsInstalled, $"{displayName} is installed with a real entry scene.");
        }

        [Test]
        public void BrokenFile_ToLoader_ToMenuController_ComesUpWithZeroRows_NoCrash()
        {
            // The full done-contract #2 chain: broken JSON on disk -> loader degrades with one
            // console error -> the menu controller builds an EMPTY menu instead of crashing.
            string path = WriteTempConfig(@"{ ""slots"": [ { ""displayName"": ");

            LogAssert.Expect(LogType.Error, LoadErrorPattern);
            LauncherConfig config = LauncherConfigLoader.LoadFrom(path);

            var go = new GameObject("HubMenuUnderTest");
            try
            {
                var menu = go.AddComponent<HubMenuController>();
                menu.InitializeWith(config); // must not throw

                Assert.AreEqual(0, menu.RowCount, "Empty config must yield an empty menu (zero rows).");
                Assert.AreEqual(0, menu.SelectedIndex);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
