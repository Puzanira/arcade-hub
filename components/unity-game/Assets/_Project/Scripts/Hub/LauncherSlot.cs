using System;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// One "control -> game" slot in the launcher menu. Pure data, parsed from
    /// <c>launcher-config.json</c> in StreamingAssets (editable on disk, no rebuild — the museum case).
    /// </summary>
    [Serializable]
    public class LauncherSlot
    {
        /// <summary>Logical launch control bound to this slot (e.g. "Crank", "Joystick"). Metadata for v0.</summary>
        public string controlName;

        /// <summary>Human-readable game name shown in the menu row.</summary>
        public string displayName;

        /// <summary>Scene loaded when an installed slot is chosen. Empty for planned slots.</summary>
        public string entryScene;

        /// <summary>"installed" or "planned". Anything that is not "installed" is treated as planned.</summary>
        public string status;

        /// <summary>True only when the slot is installed AND has a non-empty entry scene to load.</summary>
        public bool IsInstalled =>
            string.Equals(status, "installed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(entryScene);
    }
}
