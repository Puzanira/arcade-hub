namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The physical control a launcher slot is charged with. Parsed from <c>LauncherSlot.controlName</c>.
    /// <see cref="None"/> is an unknown/unbound control that can never be engaged (so a typo in the config
    /// yields a dead slot instead of a crash).
    /// </summary>
    public enum LaunchControl
    {
        None,
        Crank,
        RedButton,
        GreenButton,
        BangButton,
        HeightA,
        HeightB,

        /// <summary>
        /// EITHER height sensor (relayout-v1, founder 2026-08-07): the two contactless sensors sit side by
        /// side under one pair of hands, so the cabinet binds ONE slot to both — a palm over A or over B
        /// (or over both) charges the same slot, by the same threshold rule as a single sensor. Not a
        /// second control: holding both is exactly as fast as holding one, never twice as fast.
        /// </summary>
        Height,

        Joystick
    }

    /// <summary>Maps the config's <c>controlName</c> string onto a <see cref="LaunchControl"/>.</summary>
    public static class LaunchControls
    {
        public static LaunchControl Parse(string controlName)
        {
            if (string.IsNullOrWhiteSpace(controlName)) return LaunchControl.None;
            switch (controlName.Trim().ToLowerInvariant())
            {
                case "crank": return LaunchControl.Crank;
                case "redbutton": case "red": return LaunchControl.RedButton;
                case "greenbutton": case "green": return LaunchControl.GreenButton;
                case "bangbutton": case "bang": return LaunchControl.BangButton;
                case "heighta": return LaunchControl.HeightA;
                case "heightb": return LaunchControl.HeightB;
                case "height": case "heightany": return LaunchControl.Height;
                case "joystick": return LaunchControl.Joystick;
                default: return LaunchControl.None;
            }
        }
    }
}
