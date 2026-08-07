using System;
using System.Collections.Generic;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// Turns raw per-frame <see cref="ControlReadings"/> into a per-slot "engaged" flag, applying the
    /// per-control-kind rule from the cabinet spec (§2.3):
    /// <list type="bullet">
    ///   <item>buttons — held;</item>
    ///   <item>height sensors — value above a threshold (a physical push); a "Height" slot takes EITHER
    ///         sensor (max of the two) against that same threshold;</item>
    ///   <item>joystick — deflection magnitude above a threshold;</item>
    ///   <item>crank — |degrees this frame| above a threshold, latched for a short timeout so the gaps
    ///         between discrete turns don't read as "released".</item>
    /// </list>
    /// Pure C#: no Unity types, no scene, no ArcadeInput — the MonoBehaviour feeds it readings. This is a
    /// candidate to promote into the arcade-controls package (see the increment report).
    /// </summary>
    public sealed class LaunchInputSampler
    {
        private readonly LaunchControl[] _controls;      // one per slot
        private readonly float[] _crankActiveRemaining;  // per-slot crank latch timer
        private readonly LaunchTuning _t;

        public LaunchInputSampler(IReadOnlyList<LauncherSlot> slots, LaunchTuning tuning)
        {
            _t = (tuning ?? new LaunchTuning()).Normalized();
            int n = slots?.Count ?? 0;
            _controls = new LaunchControl[n];
            _crankActiveRemaining = new float[n];
            for (int i = 0; i < n; i++)
                _controls[i] = LaunchControls.Parse(slots[i].controlName);
        }

        public int Count => _controls.Length;

        public LaunchControl ControlAt(int slot) => _controls[slot];

        /// <summary>Fill <paramref name="engaged"/> (length <see cref="Count"/>) for this frame.</summary>
        public void Sample(ControlReadings r, float dt, bool[] engaged)
        {
            if (engaged == null) return;
            for (int i = 0; i < _controls.Length && i < engaged.Length; i++)
                engaged[i] = IsEngaged(i, r, dt);
        }

        private bool IsEngaged(int slot, ControlReadings r, float dt)
        {
            if (dt < 0f) dt = 0f;
            switch (_controls[slot])
            {
                case LaunchControl.RedButton: return r.RedHeld;
                case LaunchControl.GreenButton: return r.GreenHeld;
                case LaunchControl.BangButton: return r.BangHeld;
                case LaunchControl.HeightA: return r.HeightA > _t.heightEngageThreshold;
                case LaunchControl.HeightB: return r.HeightB > _t.heightEngageThreshold;
                // EITHER sensor: the loudest of the two against the SAME threshold a single sensor uses.
                // Engagement is a boolean, so both hands down reads exactly like one — the charge machine
                // can never run at double speed off a two-sensor slot.
                case LaunchControl.Height: return Math.Max(r.HeightA, r.HeightB) > _t.heightEngageThreshold;
                case LaunchControl.Joystick:
                {
                    float mag = (float)Math.Sqrt(r.JoystickX * r.JoystickX + r.JoystickY * r.JoystickY);
                    return mag > _t.joystickDeflectThreshold;
                }
                case LaunchControl.Crank:
                {
                    if (Math.Abs(r.CrankDeltaDegrees) > _t.crankDegreesThreshold)
                        _crankActiveRemaining[slot] = _t.crankActiveTimeout;
                    else
                        _crankActiveRemaining[slot] = Math.Max(0f, _crankActiveRemaining[slot] - dt);
                    return _crankActiveRemaining[slot] > 0f;
                }
                default:
                    return false;
            }
        }
    }
}
