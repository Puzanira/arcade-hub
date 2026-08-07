using UnityEngine;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>What one <see cref="AboutScreenMachine.Tick"/> decided.</summary>
    public enum AboutScreenEvent
    {
        /// <summary>Nothing changed this frame.</summary>
        None,

        /// <summary>The menu button just opened the document.</summary>
        Opened,

        /// <summary>The menu button just closed the document.</summary>
        Closed,

        /// <summary>Nobody touched anything for <see cref="AboutScreenMachine.IdleTimeout"/> — the cabinet took itself back to the reel.</summary>
        ClosedByTimeout,
    }

    /// <summary>
    /// The «История проекта» screen's open/close logic, pure and EditMode-testable: the MENU button
    /// toggles the document on its PRESS EDGE, and an untouched cabinet returns to the attract reel by
    /// itself after <see cref="IdleTimeout"/>.
    ///
    /// The edge is armed only after a release has been SEEN — the universal cabinet rule that
    /// <see cref="LauncherReturn"/> already follows. A player exits a game by pressing MENU; that press
    /// is very often still held when the menu scene comes up, and without the arming guard the document
    /// would fly open in his face on the frame the launcher loaded.
    /// </summary>
    public sealed class AboutScreenMachine
    {
        private float _idleTimeout;
        private bool _prevMenuHeld;
        private bool _armed;

        /// <summary>True while the document is up (the reel is suspended and nothing can be launched).</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Seconds since the last input while open; resets on any touch, and only counts while open.</summary>
        public float IdleSeconds { get; private set; }

        /// <summary>
        /// [tune] Seconds of untouched cabinet before the document closes itself. Settable at runtime
        /// (the tuning knob, and the seam PlayMode tests shorten so the timeout is provable in a second
        /// rather than in a minute).
        /// </summary>
        public float IdleTimeout
        {
            get => _idleTimeout;
            set => _idleTimeout = Mathf.Max(0.05f, value);
        }

        /// <summary>True once a MENU release has been observed — before that, a press cannot toggle anything.</summary>
        public bool IsArmed => _armed;

        public AboutScreenMachine() : this(AboutLayout.IdleReturnSeconds) { }

        public AboutScreenMachine(float idleTimeout)
        {
            IdleTimeout = idleTimeout;
        }

        /// <summary>
        /// Advance one frame.
        /// <paramref name="menuHeld"/> is the MENU button's raw held state;
        /// <paramref name="otherInput"/> is "the player is touching ANY other control right now"
        /// (joystick past its deadzone, a button, the crank, a height sensor) — it only keeps the
        /// inactivity timer at zero, it never opens or closes anything.
        /// </summary>
        public AboutScreenEvent Tick(bool menuHeld, bool otherInput, float dt)
        {
            if (dt < 0f) dt = 0f;

            bool pressEdge = menuHeld && !_prevMenuHeld && _armed;
            if (!menuHeld) _armed = true; // a release seen — the next press is a real gesture
            _prevMenuHeld = menuHeld;

            if (pressEdge)
            {
                IsOpen = !IsOpen;
                IdleSeconds = 0f;
                return IsOpen ? AboutScreenEvent.Opened : AboutScreenEvent.Closed;
            }

            if (!IsOpen)
            {
                IdleSeconds = 0f;
                return AboutScreenEvent.None;
            }

            if (menuHeld || otherInput)
            {
                IdleSeconds = 0f;
                return AboutScreenEvent.None;
            }

            IdleSeconds += dt;
            if (IdleSeconds >= _idleTimeout)
            {
                IsOpen = false;
                IdleSeconds = 0f;
                return AboutScreenEvent.ClosedByTimeout;
            }

            return AboutScreenEvent.None;
        }

        /// <summary>Force the document shut (scene teardown / tests), leaving the arming guard alone.</summary>
        public void ForceClose()
        {
            IsOpen = false;
            IdleSeconds = 0f;
        }
    }
}
