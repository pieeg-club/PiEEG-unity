using System.Collections.Concurrent;
using UnityEngine;

namespace PiEEG.Unity
{
    /// <summary>
    /// Runtime driver that streams EEG from a PiEEG-server, computes normalised band powers, and
    /// applies a <see cref="NeuroBinder"/>'s routing table to the avatar every frame. This is the
    /// <b>general-purpose</b> path for ordinary Unity projects (URP/Built-in/HDRP, desktop, XR) —
    /// <i>not</i> for VRChat, which strips runtime scripts and is driven by OSC instead.
    ///
    /// <para>Runs in both Edit and Play mode via <c>[ExecuteAlways]</c>, so dropping it on an avatar
    /// gives an immediate live reaction. The WebSocket runs off the main thread; frames are
    /// marshalled back and applied in <see cref="Update"/>.</para>
    /// </summary>
    [AddComponentMenu("PiEEG/Neuro Reactor (runtime)")]
    [RequireComponent(typeof(NeuroBinder))]
    [ExecuteAlways]
    public class NeuroReactor : MonoBehaviour
    {
        [Tooltip("Connect automatically when enabled.")]
        public bool connectOnEnable = true;

        [Tooltip("Band-power recompute rate (Hz). Matches the server OSC bridge default of 4 Hz.")]
        public float updateRateHz = 4f;

        NeuroBinder _binder;
        PiEEGClient _client;
        BandPowerAnalyzer _analyzer;
        NeuroBindingApplier _applier;
        readonly ConcurrentQueue<PiEEGFrame> _incoming = new ConcurrentQueue<PiEEGFrame>();
        double _nextTick;
        bool _connecting;

        public bool IsConnected => _client != null && _client.IsOpen;

        void OnEnable()
        {
            _binder = GetComponent<NeuroBinder>();
            _analyzer = new BandPowerAnalyzer(Mathf.Max(1, _binder.sampleRate))
            {
                Smoothing = _binder.smoothing,
            };
            _applier = new NeuroBindingApplier();
            if (connectOnEnable) Connect();
        }

        void OnDisable()
        {
            Disconnect();
            _applier?.Restore();
            while (_incoming.TryDequeue(out _)) { }
        }

        public async void Connect()
        {
            if (_connecting || IsConnected) return;
            _connecting = true;
            Disconnect();

            _client = new PiEEGClient();
            _client.OnFrame += f => _incoming.Enqueue(f);
            _client.OnError += ex => Debug.LogWarning($"[PiEEG] {ex.Message}", this);

            try
            {
                await _client.ConnectAsync(_binder.serverUrl);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PiEEG] connect failed: {ex.Message}", this);
            }
            finally
            {
                _connecting = false;
            }
        }

        public void Disconnect()
        {
            _client?.Dispose();
            _client = null;
        }

        void Update()
        {
            if (_binder == null) return;
            _analyzer.Smoothing = _binder.smoothing;

            while (_incoming.TryDequeue(out var frame))
                _analyzer.Feed(frame);

            double interval = 1.0 / Mathf.Max(0.5f, updateRateHz);
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= _nextTick)
            {
                _nextTick = now + interval;
                _analyzer.Tick();
            }

            if (!_analyzer.HasData) return;

            foreach (var b in _binder.bindings)
            {
                if (b == null || !b.enabled) continue;
                float source = _analyzer.Get(b.ParameterName);
                _applier.Apply(b, b.Evaluate(source));
            }
        }
    }
}
