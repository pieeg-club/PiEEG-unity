using System.Collections.Generic;
using UnityEngine;

namespace PiEEG.Unity
{
    /// <summary>
    /// Per-band sliding-window maximum normaliser. A faithful port of <c>_RollingMax</c> from the
    /// server OSC bridge (<c>osc_vrchat.py</c>): each band's raw µV²/Hz power is divided by the
    /// maximum seen over a rolling window, mapping it to <c>[0, 1]</c> and adapting to the user's
    /// personal signal range without manual calibration. Defaults to a 5-minute window at 4 Hz
    /// (1200 samples), matching the bridge.
    /// </summary>
    public sealed class RollingNormalizer
    {
        readonly int _window;
        readonly Dictionary<string, Queue<float>> _hist = new Dictionary<string, Queue<float>>();

        public RollingNormalizer(int window = 1200)
        {
            _window = Mathf.Max(1, window);
        }

        /// <summary>Pushes a raw band power and returns its normalised value in <c>[0, 1]</c>.</summary>
        public float UpdateAndNormalise(string band, float value)
        {
            if (!_hist.TryGetValue(band, out var q))
            {
                q = new Queue<float>();
                _hist[band] = q;
            }
            q.Enqueue(value);
            while (q.Count > _window) q.Dequeue();

            float max = 1e-9f;
            foreach (var v in q)
                if (v > max) max = v;

            return Mathf.Clamp01(value / max);
        }

        public void Reset() => _hist.Clear();
    }
}
