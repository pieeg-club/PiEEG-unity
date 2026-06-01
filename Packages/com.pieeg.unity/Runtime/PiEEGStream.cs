using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Events;

namespace PiEEG.Unity
{
    /// <summary>
    /// Drop-in MonoBehaviour wrapper around <see cref="PiEEGClient"/>. Marshals frames to the
    /// main thread and re-raises them as a UnityEvent so designers can wire things up in the
    /// inspector without writing code.
    /// </summary>
    [AddComponentMenu("PiEEG/PiEEG Stream")]
    public class PiEEGStream : MonoBehaviour
    {
        [System.Serializable] public class FrameEvent : UnityEvent<PiEEGFrame> { }
        [System.Serializable] public class WelcomeEvent : UnityEvent<PiEEGWelcome> { }

        [Tooltip("PiEEG-server WebSocket URL. Default port is 1616.")]
        public string url = "ws://raspberrypi.local:1616";

        [Tooltip("Connect automatically in Start().")]
        public bool connectOnStart = true;

        [Header("Events")]
        public FrameEvent onFrameEvent;
        public WelcomeEvent onWelcomeEvent;

        /// <summary>Raised on the Unity main thread for every EEG frame.</summary>
        public event System.Action<PiEEGFrame> OnFrame;

        /// <summary>Raised on the Unity main thread when the server sends its welcome message.</summary>
        public event System.Action<PiEEGWelcome> OnWelcome;

        readonly ConcurrentQueue<System.Action> _mainThread = new ConcurrentQueue<System.Action>();
        PiEEGClient _client;

        public bool IsConnected => _client != null && _client.IsOpen;

        async void Start()
        {
            if (connectOnStart) await Connect();
        }

        public async System.Threading.Tasks.Task Connect()
        {
            Disconnect();
            _client = new PiEEGClient();
            _client.OnFrame   += f => _mainThread.Enqueue(() => { OnFrame?.Invoke(f); onFrameEvent?.Invoke(f); });
            _client.OnWelcome += w => _mainThread.Enqueue(() => { OnWelcome?.Invoke(w); onWelcomeEvent?.Invoke(w); });
            _client.OnError   += ex => _mainThread.Enqueue(() => Debug.LogError($"[PiEEG] {ex.Message}"));
            _client.OnClosed  += ()  => _mainThread.Enqueue(() => Debug.Log("[PiEEG] connection closed"));
            try { await _client.ConnectAsync(url); }
            catch (System.Exception ex) { Debug.LogError($"[PiEEG] connect failed: {ex.Message}"); }
        }

        public void Disconnect()
        {
            _client?.Dispose();
            _client = null;
        }

        public System.Threading.Tasks.Task Send(string json) => _client?.SendAsync(json) ?? System.Threading.Tasks.Task.CompletedTask;

        void Update()
        {
            while (_mainThread.TryDequeue(out var action)) action();
        }

        void OnDestroy() => Disconnect();
    }
}
