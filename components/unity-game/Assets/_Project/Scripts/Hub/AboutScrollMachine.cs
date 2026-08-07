using UnityEngine;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The about document's scroll, as pure EditMode-testable physics: a joystick deflection drives a
    /// VELOCITY (ramped up and down, never applied raw), and the velocity moves a position that is
    /// clamped to the document's own length.
    ///
    /// Why a velocity and not "position += axis · speed · dt": an arcade stick is a digital-feeling
    /// switch at the ends of its travel, so mapping deflection straight onto movement makes the page
    /// jump into motion and stop dead — the founder asked for «плавный» scrolling. Ramping through
    /// <see cref="AboutLayout.ScrollAcceleration"/> / <see cref="AboutLayout.ScrollDeceleration"/> gives
    /// the page weight on the way in and a short glide on the way out, and the deadzone is re-scaled
    /// (not just cut) so crossing it does not snap the speed from 0 to a quarter of maximum.
    ///
    /// <see cref="Position"/> is "how far DOWN the document we have scrolled", 0 = the very top; pushing
    /// the stick UP walks it back towards 0, the way a page moves under a reading eye.
    /// </summary>
    public sealed class AboutScrollMachine
    {
        private readonly float _maxSpeed;
        private readonly float _acceleration;
        private readonly float _deceleration;
        private readonly float _deadzone;

        /// <summary>How far down the document the view is, in reference px (0 = top).</summary>
        public float Position { get; private set; }

        /// <summary>Current scroll velocity, px/s (positive = travelling further down the document).</summary>
        public float Velocity { get; private set; }

        /// <summary>
        /// The furthest <see cref="Position"/> may reach: document height minus viewport height, never
        /// below 0. Set by the view every frame — a document shorter than the screen simply cannot scroll.
        /// </summary>
        public float MaxPosition { get; set; }

        /// <summary>True when the view is parked at the first line.</summary>
        public bool AtTop => Position <= 0.001f;

        /// <summary>True when the view is parked at the last line.</summary>
        public bool AtBottom => Position >= Mathf.Max(0f, MaxPosition) - 0.001f;

        public AboutScrollMachine()
            : this(AboutLayout.ScrollMaxSpeed, AboutLayout.ScrollAcceleration,
                   AboutLayout.ScrollDeceleration, AboutLayout.ScrollDeadzone)
        {
        }

        public AboutScrollMachine(float maxSpeed, float acceleration, float deceleration, float deadzone)
        {
            _maxSpeed = Mathf.Max(1f, maxSpeed);
            _acceleration = Mathf.Max(1f, acceleration);
            _deceleration = Mathf.Max(1f, deceleration);
            _deadzone = Mathf.Clamp(deadzone, 0f, 0.9f);
        }

        /// <summary>Snap back to the top of the document, motionless (a fresh entry into the screen).</summary>
        public void Reset()
        {
            Position = 0f;
            Velocity = 0f;
        }

        /// <summary>
        /// Advance one frame. <paramref name="axisUp"/> is the joystick's Y axis, −1…+1, where +1 is
        /// pushed UP (towards the beginning of the document).
        /// </summary>
        public void Tick(float axisUp, float dt)
        {
            if (dt <= 0f) return;

            float input = Deflection(axisUp);
            // Up on the stick walks the reading position back towards the top, so the sign flips here.
            float targetVelocity = -input * _maxSpeed;

            // BRAKING — the stick centred, or pushed against the current direction of travel — uses the
            // steeper rate. Reversing through the gentle acceleration rate would make the page keep
            // sliding the wrong way for a third of a second after the player has already pushed back.
            bool braking = Mathf.Approximately(input, 0f) || Velocity * targetVelocity < 0f;
            float rate = braking ? _deceleration : _acceleration;
            Velocity = Mathf.MoveTowards(Velocity, targetVelocity, rate * dt);

            float max = Mathf.Max(0f, MaxPosition);
            float next = Position + Velocity * dt;

            // Hitting an end kills the velocity rather than storing it up: otherwise a player who held
            // the stick at the bottom for a while would have to "unwind" that momentum before the
            // document moved back at all.
            if (next <= 0f)
            {
                next = 0f;
                if (Velocity < 0f) Velocity = 0f;
            }
            else if (next >= max)
            {
                next = max;
                if (Velocity > 0f) Velocity = 0f;
            }

            Position = next;
        }

        /// <summary>
        /// The deadzone-corrected deflection, −1…+1: everything under <see cref="AboutLayout.ScrollDeadzone"/>
        /// reads as centred, and everything above it is re-scaled so the response still starts from zero.
        /// </summary>
        public float Deflection(float axisUp)
        {
            float magnitude = Mathf.Abs(axisUp);
            if (magnitude <= _deadzone) return 0f;
            float scaled = (magnitude - _deadzone) / (1f - _deadzone);
            return Mathf.Clamp01(scaled) * Mathf.Sign(axisUp);
        }
    }
}
