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
        public void ShippedConfig_HasExactlyThreeInstalledSlots_WithGameEntryScenes()
        {
            // games-integration: the launcher ships THREE installed slots — the built-in Test Game plus the
            // two packaged games — each with a real entry scene; every other slot is still planned.
            LauncherConfig config = LauncherConfigLoader.LoadFromStreamingAssets();

            int installed = 0;
            foreach (LauncherSlot s in config.slots)
                if (s.IsInstalled) installed++;
            Assert.AreEqual(3, installed,
                "Test Game + Home Alone + Life Choices are installed; the remaining slots are planned.");

            LauncherSlot testGame = config.slots.Find(s => s.displayName == "Test Game");
            LauncherSlot homeAlone = config.slots.Find(s => s.displayName == "Home Alone");
            LauncherSlot lifeChoices = config.slots.Find(s => s.displayName == "Life Choices");

            Assert.IsNotNull(homeAlone, "Home Alone slot present.");
            Assert.IsNotNull(lifeChoices, "Life Choices slot present.");
            Assert.IsTrue(testGame.IsInstalled, "Test Game stays installed.");
            Assert.IsTrue(homeAlone.IsInstalled, "Home Alone is now installed.");
            Assert.IsTrue(lifeChoices.IsInstalled, "Life Choices is now installed.");
            Assert.AreEqual("Apartment", homeAlone.entryScene, "Home Alone loads its Apartment entry scene.");
            Assert.AreEqual("ThanksNoThanks", lifeChoices.entryScene, "Life Choices loads its ThanksNoThanks entry scene.");
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
                Assert.IsFalse(menu.PlaceholderVisible, "No placeholder should show on an empty menu.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
