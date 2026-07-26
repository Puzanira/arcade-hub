namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// Engine-free snapshot of the raw scalar readings the launcher needs from the arcade controls for
    /// one frame. The MonoBehaviour adapter fills this from <c>ArcadeInput</c>; EditMode tests fill it
    /// directly, so all engagement/threshold logic stays pure-C# and testable without a scene.
    /// </summary>
    public struct ControlReadings
    {
        /// <summary>Crank rotation this frame, in degrees (signed).</summary>
        public float CrankDeltaDegrees;

        public bool RedHeld;
        public bool GreenHeld;
        public bool BangHeld;

        /// <summary>Height sensor A, 0..1.</summary>
        public float HeightA;

        /// <summary>Height sensor B, 0..1.</summary>
        public float HeightB;

        /// <summary>Joystick axes, each -1..1 (kept as two floats to stay engine-free).</summary>
        public float JoystickX;
        public float JoystickY;
    }
}
