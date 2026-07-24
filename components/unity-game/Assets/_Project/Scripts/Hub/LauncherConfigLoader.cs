using System;
using System.IO;
using UnityEngine;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// Loads <see cref="LauncherConfig"/> from <c>StreamingAssets/launcher-config.json</c>.
    /// A missing or malformed file logs a clear error and returns an EMPTY config so the menu still
    /// comes up (done-contract #2: bad JSON must not crash the launcher).
    /// </summary>
    public static class LauncherConfigLoader
    {
        public const string FileName = "launcher-config.json";

        /// <summary>Absolute path of the config file inside StreamingAssets.</summary>
        public static string ConfigPath => Path.Combine(Application.streamingAssetsPath, FileName);

        /// <summary>Read + parse the config from StreamingAssets, degrading to an empty config (with a console error) on any failure.</summary>
        public static LauncherConfig LoadFromStreamingAssets() => LoadFrom(ConfigPath);

        /// <summary>
        /// Read + parse the config file at <paramref name="path"/> (the injectable seam used by tests).
        /// Any failure — missing file, malformed JSON — logs ONE clear console error and returns an
        /// EMPTY config so the menu still comes up (done-contract #2: never a crash).
        /// </summary>
        public static LauncherConfig LoadFrom(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                return LauncherConfig.FromJson(json);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[Hub] Failed to load launcher config from '{path}': {e.Message}. " +
                    "Menu will come up with an empty slot list.");
                return new LauncherConfig();
            }
        }
    }
}
