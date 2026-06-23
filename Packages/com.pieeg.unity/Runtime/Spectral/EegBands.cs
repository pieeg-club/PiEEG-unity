using System;

namespace PiEEG.Unity
{
    /// <summary>
    /// Canonical EEG frequency band, matching the PiEEG-server definitions in
    /// <c>spectral.py</c> and the VRChat OSC bridge (<c>osc_vrchat.py</c>). The
    /// <see cref="Name"/> doubles as the suffix the OSC bridge uses when it emits
    /// <c>/avatar/parameters/EEG_&lt;Band&gt;</c>, so keeping these in sync guarantees the
    /// in-editor preview matches the values VRChat actually receives at runtime.
    /// </summary>
    [Serializable]
    public readonly struct EegBand
    {
        public readonly string Name;
        public readonly float Low;
        public readonly float High;

        public EegBand(string name, float low, float high)
        {
            Name = name;
            Low = low;
            High = high;
        }

        /// <summary>OSC parameter name the server emits for the global average of this band.</summary>
        public string DefaultParameter => "EEG_" + Name;

        public override string ToString() => $"{Name} ({Low:0.#}-{High:0.#} Hz)";
    }

    /// <summary>
    /// Shared spectral constants. These mirror <c>pieeg_server/spectral.py</c> exactly so the
    /// Unity-side FFT produces the same band powers the OSC bridge streams into VRChat.
    /// </summary>
    public static class EegBands
    {
        /// <summary>Hardware default for PiEEG / IronBCI SPI (Hz).</summary>
        public const int DefaultSampleRate = 250;

        /// <summary>FFT window length used by the server OSC bridge (~2 s at 250 Hz).</summary>
        public const int DefaultFftSize = 512;

        /// <summary>OSC parameter name prefix used by the bridge (<c>parameter_prefix</c>).</summary>
        public const string ParameterPrefix = "EEG_";

        /// <summary>The five standard EEG bands, ordered low → high frequency.</summary>
        public static readonly EegBand[] All =
        {
            new EegBand("Delta", 0.5f, 4.0f),
            new EegBand("Theta", 4.0f, 8.0f),
            new EegBand("Alpha", 8.0f, 13.0f),
            new EegBand("Beta", 13.0f, 30.0f),
            new EegBand("Gamma", 30.0f, 100.0f),
        };

        /// <summary>Returns the band whose <see cref="EegBand.Name"/> matches, or null.</summary>
        public static EegBand? Find(string name)
        {
            foreach (var b in All)
                if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
                    return b;
            return null;
        }
    }
}
