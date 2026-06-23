using System;

namespace PiEEG.Unity
{
    /// <summary>
    /// Pure C# radix-2 Cooley-Tukey FFT specialised for EEG spectral analysis. Ported from the
    /// PiEEG dashboard <c>FftEngine</c> (TypeScript) but normalised to match the *server* OSC
    /// bridge math in <c>spectral.py</c> / <c>osc_vrchat.py</c> so the in-editor preview yields the
    /// same band powers VRChat receives over OSC:
    /// <list type="bullet">
    /// <item>Hanning window to suppress spectral leakage.</item>
    /// <item>One-sided power spectral density in µV²/Hz: <c>|X|² / (fs·Σw²)</c>, interior bins ×2.</item>
    /// <item>Band power = arithmetic mean of PSD bins whose frequency falls in <c>[low, high)</c>.</item>
    /// </list>
    /// No allocations on the hot path: scratch buffers are reused between calls. Not thread-safe;
    /// use one instance per worker.
    /// </summary>
    public sealed class FftEngine
    {
        public int Size { get; }
        public int SampleRate { get; }

        /// <summary>One-sided bin count: <c>Size/2 + 1</c>.</summary>
        public int Bins { get; }

        readonly double[] _window;     // Hanning
        readonly double[] _freqs;      // bin → Hz
        readonly int[] _bitReversed;
        readonly double[] _twRe;
        readonly double[] _twIm;
        readonly double _normFactor;   // fs · Σw²

        // Scratch (reused)
        readonly double[] _re;
        readonly double[] _im;
        readonly double[] _psd;

        public FftEngine(int size = EegBands.DefaultFftSize, int sampleRate = EegBands.DefaultSampleRate)
        {
            if (size <= 0 || (size & (size - 1)) != 0)
                throw new ArgumentException("FFT size must be a power of 2", nameof(size));
            if (sampleRate <= 0)
                throw new ArgumentException("Sample rate must be positive", nameof(sampleRate));

            Size = size;
            SampleRate = sampleRate;
            Bins = (size >> 1) + 1;

            _window = new double[size];
            double sumSq = 0.0;
            for (int i = 0; i < size; i++)
            {
                // Hanning: 0.5 (1 - cos(2π i / (N-1)))
                _window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (size - 1)));
                sumSq += _window[i] * _window[i];
            }

            _freqs = new double[Bins];
            double df = (double)sampleRate / size;
            for (int i = 0; i < Bins; i++) _freqs[i] = i * df;

            int logN = Log2(size);
            _bitReversed = new int[size];
            for (int i = 0; i < size; i++) _bitReversed[i] = ReverseBits(i, logN);

            int half = size >> 1;
            _twRe = new double[half];
            _twIm = new double[half];
            for (int k = 0; k < half; k++)
            {
                double angle = -2.0 * Math.PI * k / size;
                _twRe[k] = Math.Cos(angle);
                _twIm[k] = Math.Sin(angle);
            }

            // Server normalisation (spectral.py): norm = fs · Σw²  (PSD = |X|² / norm).
            _normFactor = sampleRate * sumSq;

            _re = new double[size];
            _im = new double[size];
            _psd = new double[Bins];
        }

        /// <summary>
        /// Computes the one-sided PSD of the most recent <see cref="Size"/> samples. The returned
        /// array is owned by this engine and overwritten on the next call — copy it if you need to
        /// retain it. <paramref name="samples"/> must contain at least <see cref="Size"/> elements;
        /// the window is taken from the tail (<c>samples[length-Size .. length)</c>).
        /// </summary>
        public double[] ComputePsd(double[] samples, int length)
        {
            if (length < Size)
                throw new ArgumentException($"Need at least {Size} samples, got {length}", nameof(length));

            int offset = length - Size;
            for (int i = 0; i < Size; i++)
            {
                _re[i] = samples[offset + i] * _window[i];
                _im[i] = 0.0;
            }

            Transform(_re, _im);

            for (int k = 0; k < Bins; k++)
            {
                double power = (_re[k] * _re[k] + _im[k] * _im[k]) / _normFactor;
                // One-sided correction: double interior bins (skip DC and Nyquist).
                if (k > 0 && k < Bins - 1) power *= 2.0;
                _psd[k] = power;
            }
            return _psd;
        }

        /// <summary>Arithmetic mean of PSD bins whose frequency is in <c>[low, high)</c> (µV²/Hz).</summary>
        public double BandMean(double[] psd, float low, float high)
        {
            double sum = 0.0;
            int count = 0;
            for (int k = 0; k < Bins; k++)
            {
                double f = _freqs[k];
                if (f >= low && f < high)
                {
                    sum += psd[k];
                    count++;
                }
            }
            return count > 0 ? sum / count : 0.0;
        }

        // ── In-place radix-2 FFT ────────────────────────────────────────────

        void Transform(double[] re, double[] im)
        {
            int n = Size;
            for (int i = 0; i < n; i++)
            {
                int j = _bitReversed[i];
                if (j > i)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (int size = 2; size <= n; size <<= 1)
            {
                int half = size >> 1;
                int step = n / size;
                for (int i = 0; i < n; i += size)
                {
                    for (int j = 0; j < half; j++)
                    {
                        int tw = j * step;
                        double wr = _twRe[tw];
                        double wi = _twIm[tw];
                        int e = i + j;
                        int o = e + half;
                        double tr = wr * re[o] - wi * im[o];
                        double ti = wr * im[o] + wi * re[o];
                        re[o] = re[e] - tr;
                        im[o] = im[e] - ti;
                        re[e] += tr;
                        im[e] += ti;
                    }
                }
            }
        }

        static int Log2(int v)
        {
            int r = 0;
            while (v > 1) { v >>= 1; r++; }
            return r;
        }

        static int ReverseBits(int x, int bits)
        {
            int r = 0;
            for (int i = 0; i < bits; i++)
            {
                r = (r << 1) | (x & 1);
                x >>= 1;
            }
            return r;
        }
    }
}
