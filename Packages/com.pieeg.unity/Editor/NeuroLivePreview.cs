using System;
using System.Collections.Concurrent;
using UnityEditor;
using UnityEngine;

namespace PiEEG.Unity.Editor
{
    /// <summary>
    /// Drives the avatar live, <b>in the Unity editor</b>, straight from a PiEEG-server WebSocket so
    /// creators can tune response curves while wearing the hardware — the core DX win. It runs on an
    /// <see cref="EditorApplication.update"/> hook (no Play mode required), computes the exact same
    /// normalised band powers the server's OSC bridge produces, and applies the
    /// <see cref="NeuroBinder"/>'s bindings directly to the targets. What you see here is what the
    /// baked blend tree will do at runtime, because both consume the identical curve evaluation.
    ///
    /// <para>All original blendshape weights and material overrides are restored on
    /// <see cref="Stop"/>.</para>
    /// </summary>
    public sealed class NeuroLivePreview
    {
        readonly ConcurrentQueue<PiEEGFrame> _incoming = new ConcurrentQueue<PiEEGFrame>();
        readonly ConcurrentQueue<string> _errors = new ConcurrentQueue<string>();

        NeuroBinder _binder;
        PiEEGClient _client;
        BandPowerAnalyzer _analyzer;
        NeuroBindingApplier _applier;
        double _nextTick;
        bool _connecting;

        public bool IsRunning { get; private set; }
        public bool IsConnected => _client != null && _client.IsOpen;
        public bool Warming => _analyzer != null && !_analyzer.HasData;
        public BandPowerAnalyzer Analyzer => _analyzer;
        public NeuroBinder Target => _binder;

        /// <summary>Raised (on the main thread) whenever connection state or warm-up changes.</summary>
        public event Action StateChanged;

        public string LastError { get; private set; }

        public void Start(NeuroBinder binder)
        {
            if (binder == null) return;
            Stop();

            _binder = binder;
            _analyzer = new BandPowerAnalyzer(Mathf.Max(1, binder.sampleRate)) { Smoothing = binder.smoothing };
            _applier = new NeuroBindingApplier();
            LastError = null;
            IsRunning = true;

            EditorApplication.update += Tick;
            ConnectAsync();
            StateChanged?.Invoke();
        }

        public void Stop()
        {
            if (!IsRunning && _client == null) return;
            EditorApplication.update -= Tick;
            IsRunning = false;

            _applier?.Restore();
            _applier = null;

            _client?.Dispose();
            _client = null;
            _analyzer = null;
            while (_incoming.TryDequeue(out _)) { }

            SceneView.RepaintAll();
            StateChanged?.Invoke();
        }

        async void ConnectAsync()
        {
            if (_connecting) return;
            _connecting = true;
            try
            {
                _client = new PiEEGClient();
                _client.OnFrame += f => _incoming.Enqueue(f);
                _client.OnError += ex => _errors.Enqueue(ex.Message);
                _client.OnClosed += () => _errors.Enqueue("connection closed");
                await _client.ConnectAsync(_binder.serverUrl);
            }
            catch (Exception ex)
            {
                _errors.Enqueue(ex.Message);
            }
            finally
            {
                _connecting = false;
                StateChanged?.Invoke();
            }
        }

        void Tick()
        {
            if (!IsRunning || _binder == null) { Stop(); return; }

            bool changed = false;
            while (_errors.TryDequeue(out var err))
            {
                LastError = err;
                changed = true;
            }

            _analyzer.Smoothing = _binder.smoothing;
            while (_incoming.TryDequeue(out var frame))
                _analyzer.Feed(frame);

            const double interval = 0.1; // 10 Hz refresh; band powers themselves track ~4 Hz
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextTick) return;
            _nextTick = now + interval;

            bool wasWarming = Warming;
            _analyzer.Tick();
            if (wasWarming != Warming) changed = true;

            if (_analyzer.HasData)
            {
                foreach (var b in _binder.bindings)
                {
                    if (b == null || !b.enabled) continue;
                    float source = _analyzer.Get(b.ParameterName);
                    _applier.Apply(b, b.Evaluate(source));
                }
                SceneView.RepaintAll();
            }

            if (changed) StateChanged?.Invoke();
        }
    }
}
