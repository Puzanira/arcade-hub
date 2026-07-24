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
