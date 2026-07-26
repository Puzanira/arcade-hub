using System;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// Tuning coefficients for hold-to-launch, loaded from the <c>"tuning"</c> block of
    /// <c>launcher-config.json</c> (data on disk, no rebuild — the museum case). Field initializers hold
    /// the shipped defaults so an omitted block still gives a working launcher; <see cref="Normalized"/>
    /// replaces any non-positive value with its default, which also makes a partially-specified JSON block
    /// safe (JsonUtility would otherwise leave missing numbers at 0).
    /// </summary>
    [Serializable]
    public class LaunchTuning
    {
        public const float DefaultChargeSeconds = 5.0f;
        public const float DefaultDecaySeconds = 1.0f;
        public const float DefaultJoystickDeflectThreshold = 0.5f;
        public const float DefaultCrankDegreesThreshold = 2.0f;
        public const float DefaultCrankActiveTimeout = 0.25f;
        public const float DefaultHeightEngageThreshold = 0.5f;

        /// <summary>Seconds of continuous activity to charge a slot from 0 to 1.</summary>
        public float chargeSeconds = DefaultChargeSeconds;

        /// <summary>Seconds to decay from 1 back to 0 once the control is released.</summary>
        public float decaySeconds = DefaultDecaySeconds;

        /// <summary>Joystick deflection magnitude that counts as "engaged".</summary>
        public float joystickDeflectThreshold = DefaultJoystickDeflectThreshold;

        /// <summary>Per-frame |crank degrees| above which the crank counts as turning.</summary>
        public float crankDegreesThreshold = DefaultCrankDegreesThreshold;

        /// <summary>Seconds the crank stays "active" after its last qualifying turn (fills the gaps between ticks).</summary>
        public float crankActiveTimeout = DefaultCrankActiveTimeout;

        /// <summary>Height-sensor value above which the sensor counts as "held".</summary>
        public float heightEngageThreshold = DefaultHeightEngageThreshold;

        /// <summary>A copy with every non-positive coefficient replaced by its shipped default.</summary>
        public LaunchTuning Normalized()
        {
            return new LaunchTuning
            {
                chargeSeconds = chargeSeconds > 0f ? chargeSeconds : DefaultChargeSeconds,
                decaySeconds = decaySeconds > 0f ? decaySeconds : DefaultDecaySeconds,
                joystickDeflectThreshold = joystickDeflectThreshold > 0f ? joystickDeflectThreshold : DefaultJoystickDeflectThreshold,
                crankDegreesThreshold = crankDegreesThreshold > 0f ? crankDegreesThreshold : DefaultCrankDegreesThreshold,
                crankActiveTimeout = crankActiveTimeout > 0f ? crankActiveTimeout : DefaultCrankActiveTimeout,
                heightEngageThreshold = heightEngageThreshold > 0f ? heightEngageThreshold : DefaultHeightEngageThreshold,
            };
        }
    }
}
