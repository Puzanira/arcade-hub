using System.Collections.Generic;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The deterministic charge state machine behind hold-to-launch (spec §2.3), pure C# and fully
    /// EditMode-testable. Idle → a control engages → Charging: charge grows 0→1 over
    /// <c>chargeSeconds</c> of activity; on release it decays to 0 over <c>decaySeconds</c> and the slot
    /// is dropped (release does not accumulate backwards — a fresh hold starts from 0). Exclusivity: the
    /// first slot to reach charge &gt; 0 is the only one that can charge until it decays back to 0; all
    /// other engaged slots are ignored while it is active. On reaching full it reports the slot to launch
    /// exactly once (the caller launches an installed slot's scene, or shows "coming soon" and calls
    /// <see cref="Reset"/> for a planned one).
    /// </summary>
    public sealed class LaunchChargeMachine
    {
        private readonly float _chargeSeconds;
        private readonly float _decaySeconds;
        private bool _wasFull;

        public LaunchChargeMachine(LaunchTuning tuning)
        {
            var t = (tuning ?? new LaunchTuning()).Normalized();
            _chargeSeconds = t.chargeSeconds;
            _decaySeconds = t.decaySeconds;
        }

        /// <summary>Current charge, 0..1.</summary>
        public float Charge { get; private set; }

        /// <summary>The slot currently charging, or -1 when idle.</summary>
        public int ActiveSlot { get; private set; } = -1;

        /// <summary>True while a slot is actively accumulating or holding charge.</summary>
        public bool IsActive => ActiveSlot >= 0 && Charge > 0f;

        /// <summary>Drop back to idle (used after a planned slot fires, or to cancel).</summary>
        public void Reset()
        {
            Charge = 0f;
            ActiveSlot = -1;
            _wasFull = false;
        }

        /// <summary>
        /// Advance one frame. <paramref name="engaged"/>[i] is whether slot i's control is engaged now.
        /// Returns the slot index to launch (charge just reached 1) or -1.
        /// </summary>
        public int Tick(IReadOnlyList<bool> engaged, float dt)
        {
            if (dt < 0f) dt = 0f;

            // Acquire an active slot only when idle: the lowest-index engaged slot wins (exclusivity).
            if (ActiveSlot < 0)
            {
                if (engaged != null)
                {
                    for (int i = 0; i < engaged.Count; i++)
                    {
                        if (engaged[i]) { ActiveSlot = i; break; }
                    }
                }
            }

            if (ActiveSlot < 0)
            {
                _wasFull = false;
                return -1;
            }

            bool active = engaged != null && ActiveSlot < engaged.Count && engaged[ActiveSlot];
            if (active)
                Charge += dt / _chargeSeconds;
            else
                Charge -= dt / _decaySeconds;

            if (Charge <= 0f)
            {
                // Fully released — drop the slot so nothing accumulates backwards.
                Charge = 0f;
                ActiveSlot = -1;
                _wasFull = false;
                return -1;
            }

            if (Charge >= 1f)
            {
                Charge = 1f;
                if (!_wasFull)
                {
                    _wasFull = true;
                    return ActiveSlot; // fire exactly once on the rising edge to full
                }
                return -1;
            }

            _wasFull = false;
            return -1;
        }
    }
}
