using System;
using System.IO;
using UnityEngine;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// Loads the about document from <c>StreamingAssets/About/content.json</c> — the same swappable-on-
    /// the-cabinet idiom as <see cref="LauncherConfigLoader"/>: text and photos are DATA, and replacing
    /// the founder's story means editing that folder, never touching code.
    ///
    /// A missing or malformed file logs ONE clear console error and returns
    /// <see cref="AboutContent.Fallback"/> — the screen opens with its placeholder heading and no
    /// content, and the cabinet keeps running.
    /// </summary>
    public static class AboutContentLoader
    {
        /// <summary>The document's folder inside StreamingAssets (json + its photos live together).</summary>
        public const string FolderName = "About";

        public const string FileName = "content.json";

        /// <summary>Absolute path of the About folder inside StreamingAssets.</summary>
        public static string FolderPath => Path.Combine(Application.streamingAssetsPath, FolderName);

        /// <summary>Absolute path of the document file.</summary>
        public static string ContentPath => Path.Combine(FolderPath, FileName);

        /// <summary>Read + parse the document from StreamingAssets, degrading to the placeholder on any failure.</summary>
        public static AboutContent LoadFromStreamingAssets() => LoadFrom(ContentPath);

        /// <summary>
        /// Read + parse the document at <paramref name="path"/> (the injectable seam used by tests).
        /// Any failure — missing file, malformed JSON — logs ONE clear console error and returns the
        /// placeholder document so the screen still comes up.
        /// </summary>
        public static AboutContent LoadFrom(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                return AboutContent.FromJson(json);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[Hub] Failed to load about content from '{path}': {e.Message}. " +
                    "About screen will come up with a placeholder heading only.");
                return AboutContent.Fallback();
            }
        }

        /// <summary>
        /// Where a <see cref="AboutBlockType.Photo"/> block's file lives: next to the content file that
        /// referenced it. Photos are therefore addressed by bare file name in the JSON, and the whole
        /// document (text + images) moves as one folder.
        /// </summary>
        public static string PhotoPathFor(string contentPath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            string dir = Path.GetDirectoryName(contentPath);
            return string.IsNullOrEmpty(dir) ? fileName : Path.Combine(dir, fileName.Trim());
        }
    }
}
