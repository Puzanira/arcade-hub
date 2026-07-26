using System;
using System.Collections.Generic;
using UnityEngine;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The parsed launcher configuration: an ordered list of "control -> game" slots.
    /// Parsing is pure and EditMode-testable; malformed JSON throws a clear <see cref="FormatException"/>
    /// so callers can decide how to degrade (the loader turns it into an empty menu, never a crash).
    /// </summary>
    [Serializable]
    public class LauncherConfig
    {
        public List<LauncherSlot> slots = new List<LauncherSlot>();

        /// <summary>
        /// Hold-to-launch tuning (charge/decay seconds, thresholds). Optional in JSON — an omitted or
        /// partial block degrades to shipped defaults via <see cref="TuningOrDefault"/>.
        /// </summary>
        public LaunchTuning tuning = new LaunchTuning();

        /// <summary>The tuning with every non-positive coefficient replaced by its default (never null).</summary>
        public LaunchTuning TuningOrDefault() => (tuning ?? new LaunchTuning()).Normalized();

        /// <summary>
        /// Parse a config from a JSON string. Throws <see cref="FormatException"/> on empty or malformed
        /// input. A valid document with an empty (or missing) slot array yields an empty, usable config.
        /// </summary>
        public static LauncherConfig FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("Launcher config JSON is empty.");

            LauncherConfig config;
            try
            {
                config = JsonUtility.FromJson<LauncherConfig>(json);
            }
            catch (Exception e)
            {
                throw new FormatException("Launcher config JSON is not valid: " + e.Message, e);
            }

            if (config == null)
                throw new FormatException("Launcher config JSON did not deserialize to an object.");
            if (config.slots == null)
                config.slots = new List<LauncherSlot>();

            return config;
        }
    }
}
