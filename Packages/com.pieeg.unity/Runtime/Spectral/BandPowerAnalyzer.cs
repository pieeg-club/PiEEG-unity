using System.Collections.Generic;
using UnityEngine;

namespace PiEEG.Unity
{
    /// <summary>
    /// Turns a stream of <see cref="PiEEGFrame"/>s into normalised EEG band powers, mirroring the
    /// server VRChat OSC bridge so the values produced here match what VRChat receives over OSC.
    ///
    /// <para>Pipeline: per-channel ring buffers → Hanning-windowed FFT (<see cref="FftEngine"/>) →
    /// per-band PSD mean averaged across the selected channels → rolling-max normalisation
    /// (<see cref="RollingNormalizer"/>) → optional EMA smoothing for nicer visuals.</para>
    ///
    /// <para>Feed frames from any thread-marshalled callback via <see cref="Feed"/>, then call
    /// <see cref="Tick"/> on a fixed cadence (default 4 Hz, matching the bridge's
    /// <c>interval = 0.25 s</c>). Read results from <see cref="Values"/> (keyed by parameter name,
    /// e.g. <c>EEG_Alpha</c>).</para>
    /// </summary>
    public sealed class BandPowerAnalyzer
    {
        readonly FftEngine _fft;
        readonly RollingNormalizer _normaliser;
        readonly int _fftSize;

        // Per-channel ring buffers.
        double[][] _buffers;
        int[] _writeIdx;
        int[] _count;
        int _numChannels;

        // Scratch for averaged-across-channels samples → reused PSD accumulation.
        double[] _scratch;
        double[] _avgPsd;

        /// <summary>Normalised band powers keyed by OSC parameter name (e.g. <c>EEG_Alpha</c>), in [0,1].</summary>
        public readonly Dictionary<string, float> Values = new Dictionary<string, float>();

        /// <summary>Raw band powers (µV²/Hz) keyed by band name, for diagnostics.</summary>
        public readonly Dictionary<string, float> Raw = new Dictionary<string, float>();

        /// <summary>True once at least one band power has been computed (buffers warmed up).</summary>
        public bool HasData { get; private set; }

        /// <summary>Optional EMA smoothing in [0,1). 0 = none (matches OSC bridge output exactly).</summary>
        public float Smoothing { get; set; }

        public BandPowerAnalyzer(int sampleRate = EegBands.DefaultSampleRate,
                                 int fftSize = EegBands.DefaultFftSize,
                                 int normaliserWindow = 1200)
        {
            _fftSize = fftSize;
            _fft = new FftEngine(fftSize, sampleRate);
            _normaliser = new RollingNormalizer(normaliserWindow);
            _avgPsd = new double[_fft.Bins];
        }

        void EnsureChannels(int n)
        {
            if (_buffers != null && _numChannels == n) return;
            _numChannels = n;
            _buffers = new double[n][];
            _writeIdx = new int[n];
            _count = new int[n];
            for (int c = 0; c < n; c++) _buffers[c] = new double[_fftSize];
            _scratch = new double[_fftSize];
        }

        /// <summary>Appends one frame's per-channel samples to the ring buffers.</summary>
        public void Feed(PiEEGFrame frame)
        {
            if (frame?.channels == null || frame.channels.Count == 0) return;
            EnsureChannels(frame.channels.Count);
            for (int c = 0; c < _numChannels && c < frame.channels.Count; c++)
            {
                var buf = _buffers[c];
                buf[_writeIdx[c]] = frame.channels[c];
                _writeIdx[c] = (_writeIdx[c] + 1) % _fftSize;
                if (_count[c] < _fftSize) _count[c]++;
            }
        }

        /// <summary>
        /// Recomputes band powers from the current buffers and updates <see cref="Values"/>.
        /// No-op (returns false) until every channel buffer is full. Call this at ~4 Hz.
        /// </summary>
        public bool Tick()
        {
            if (_buffers == null) return false;
            for (int c = 0; c < _numChannels; c++)
                if (_count[c] < _fftSize) return false;

            // Average PSD across all channels (equivalent to the bridge's per-channel band mean
            // then channel average, since all channels share the same frequency bins).
            for (int k = 0; k < _fft.Bins; k++) _avgPsd[k] = 0.0;
            for (int c = 0; c < _numChannels; c++)
            {
                CopyChronological(c, _scratch);
                var psd = _fft.ComputePsd(_scratch, _fftSize);
                for (int k = 0; k < _fft.Bins; k++) _avgPsd[k] += psd[k];
            }
            double inv = 1.0 / _numChannels;
            for (int k = 0; k < _fft.Bins; k++) _avgPsd[k] *= inv;

            foreach (var band in EegBands.All)
            {
                float raw = (float)_fft.BandMean(_avgPsd, band.Low, band.High);
                float norm = _normaliser.UpdateAndNormalise(band.Name, raw);

                string key = band.DefaultParameter;
                if (Smoothing > 0f && Values.TryGetValue(key, out float prev))
                    norm = Mathf.Lerp(norm, prev, Smoothing);

                Raw[band.Name] = raw;
                Values[key] = norm;
            }

            HasData = true;
            return true;
        }

        /// <summary>Copies a channel's ring buffer into <paramref name="dst"/> in oldest→newest order.</summary>
        void CopyChronological(int channel, double[] dst)
        {
            var buf = _buffers[channel];
            int start = _writeIdx[channel]; // oldest sample (buffer is full here)
            for (int i = 0; i < _fftSize; i++)
                dst[i] = buf[(start + i) % _fftSize];
        }

        /// <summary>Returns the latest normalised value for a parameter, or 0 if unavailable.</summary>
        public float Get(string parameterName)
            => Values.TryGetValue(parameterName, out float v) ? v : 0f;

        public void Reset()
        {
            _buffers = null;
            _numChannels = 0;
            _normaliser.Reset();
            Values.Clear();
            Raw.Clear();
            HasData = false;
        }
    }
}
