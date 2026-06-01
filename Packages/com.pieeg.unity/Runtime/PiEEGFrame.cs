using System;
using System.Collections.Generic;

namespace PiEEG.Unity
{
    /// <summary>JSON wire format from PiEEG-server: <c>{ "t": ..., "n": ..., "channels": [...] }</c>.</summary>
    [Serializable]
    public class PiEEGFrame
    {
        public double t;
        public long n;
        public List<float> channels;
    }

    /// <summary>Welcome payload sent by the server on connect.</summary>
    [Serializable]
    public class PiEEGWelcome
    {
        public string status;
        public int sample_rate;
        public int channels;
        public bool filter;
        public bool recording;
    }
}
