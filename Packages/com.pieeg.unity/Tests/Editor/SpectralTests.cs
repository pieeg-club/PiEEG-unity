using System;
using System.Collections.Generic;
using NUnit.Framework;
using PiEEG.Unity;
using UnityEngine;

namespace PiEEG.Unity.Tests
{
    /// <summary>
    /// Hardware-free tests for the spectral core. They feed synthetic signals and assert that the
    /// band-power pipeline behaves like the server's <c>spectral.py</c> / OSC bridge.
    /// </summary>
    public class SpectralTests
    {
        const int SampleRate = 250;
        const int FftSize = 512;

        static double[] Sine(double freq, int n, int sampleRate, double amp = 50.0)
        {
            var s = new double[n];
            for (int i = 0; i < n; i++)
                s[i] = amp * Math.Sin(2.0 * Math.PI * freq * i / sampleRate);
            return s;
        }

        [Test]
        public void FftEngine_Rejects_NonPowerOfTwo()
        {
            Assert.Throws<ArgumentException>(() => new FftEngine(500, SampleRate));
        }

        [Test]
        public void FftEngine_PureTone_Peaks_In_Expected_Band()
        {
            var fft = new FftEngine(FftSize, SampleRate);
            var samples = Sine(10.0, FftSize, SampleRate); // 10 Hz → Alpha (8–13)
            var psd = fft.ComputePsd(samples, FftSize);

            double alpha = fft.BandMean(psd, 8f, 13f);
            double delta = fft.BandMean(psd, 0.5f, 4f);
            double theta = fft.BandMean(psd, 4f, 8f);
            double beta = fft.BandMean(psd, 13f, 30f);
            double gamma = fft.BandMean(psd, 30f, 100f);

            Assert.Greater(alpha, delta);
            Assert.Greater(alpha, theta);
            Assert.Greater(alpha, beta);
            Assert.Greater(alpha, gamma);
        }

        [Test]
        public void BandPowerAnalyzer_Warms_Up_Then_Reports_Dominant_Band()
        {
            var analyzer = new BandPowerAnalyzer(SampleRate, FftSize) { Smoothing = 0f };

            // 20 Hz tone → Beta (13–30).
            var tone = Sine(20.0, FftSize, SampleRate);

            // Not enough samples yet.
            for (int i = 0; i < FftSize - 1; i++)
                analyzer.Feed(new PiEEGFrame { channels = new List<float> { (float)tone[i] } });
            Assert.IsFalse(analyzer.Tick(), "Should not compute before buffers are full.");
            Assert.IsFalse(analyzer.HasData);

            analyzer.Feed(new PiEEGFrame { channels = new List<float> { (float)tone[FftSize - 1] } });
            Assert.IsTrue(analyzer.Tick(), "Should compute once buffers are full.");
            Assert.IsTrue(analyzer.HasData);

            // The rolling normaliser maps each band's first sample to its own max (1.0), so compare
            // dominance on the RAW band powers (µV²/Hz), which reflect the true spectrum.
            float beta = analyzer.Raw["Beta"];
            float alpha = analyzer.Raw["Alpha"];
            float delta = analyzer.Raw["Delta"];

            Assert.Greater(beta, alpha);
            Assert.Greater(beta, delta);

            // Normalised outputs are always clamped to [0,1] and keyed by OSC parameter name.
            Assert.That(analyzer.Get("EEG_Beta"), Is.InRange(0f, 1f));
            Assert.IsTrue(analyzer.Values.ContainsKey("EEG_Beta"));
        }

        [Test]
        public void RollingNormalizer_Maps_Max_To_One()
        {
            var norm = new RollingNormalizer(window: 16);
            norm.UpdateAndNormalise("Alpha", 1f);
            norm.UpdateAndNormalise("Alpha", 5f);
            float v = norm.UpdateAndNormalise("Alpha", 10f); // current is the max
            Assert.That(v, Is.EqualTo(1f).Within(1e-5f));

            float half = norm.UpdateAndNormalise("Alpha", 5f); // half of the rolling max (10)
            Assert.That(half, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void NeuroBinding_Evaluate_Clamps_And_Uses_Curve()
        {
            var b = new NeuroBinding
            {
                responseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            };
            Assert.That(b.Evaluate(0.5f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(b.Evaluate(2f), Is.EqualTo(1f).Within(1e-4f));   // input clamped
            Assert.That(b.Evaluate(-1f), Is.EqualTo(0f).Within(1e-4f));  // input clamped
        }

        [Test]
        public void NeuroBinding_ParameterName_Falls_Back_To_SourceId()
        {
            var b = new NeuroBinding { sourceId = "EEG_Alpha", parameterNameOverride = "" };
            Assert.AreEqual("EEG_Alpha", b.ParameterName);

            b.parameterNameOverride = "EEG_Frontal_Alpha";
            Assert.AreEqual("EEG_Frontal_Alpha", b.ParameterName);
        }
    }
}
