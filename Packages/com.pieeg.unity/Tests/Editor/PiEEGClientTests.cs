using System;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PiEEG.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;

namespace PiEEG.Unity.Tests
{
    /// <summary>
    /// Spins up a tiny in-process WebSocket server using <see cref="HttpListener"/>, has the
    /// <see cref="PiEEGClient"/> connect to it, and verifies the welcome + frame messages are
    /// parsed into the expected typed objects. No real hardware required.
    /// </summary>
    public class PiEEGClientTests
    {
        [Test]
        public void Welcome_Json_Parses_Into_Typed_Object()
        {
            const string json = "{\"status\":\"connected\",\"sample_rate\":250,\"channels\":16,\"filter\":false,\"recording\":false}";
            var w = JsonUtility.FromJson<PiEEGWelcome>(json);

            Assert.AreEqual("connected", w.status);
            Assert.AreEqual(250, w.sample_rate);
            Assert.AreEqual(16, w.channels);
            Assert.IsFalse(w.filter);
            Assert.IsFalse(w.recording);
        }

        [Test]
        public void Frame_Json_Parses_Into_Typed_Object()
        {
            const string json = "{\"t\":1711234567.123,\"n\":42,\"channels\":[12.34,-5.67,8.9]}";
            var f = JsonUtility.FromJson<PiEEGFrame>(json);

            Assert.AreEqual(42, f.n);
            Assert.That(f.t, Is.EqualTo(1711234567.123).Within(1e-3));
            Assert.AreEqual(3, f.channels.Count);
            Assert.That(f.channels[0], Is.EqualTo(12.34f).Within(1e-3));
            Assert.That(f.channels[1], Is.EqualTo(-5.67f).Within(1e-3));
        }

        [UnityTest]
        public IEnumerator Client_Receives_Welcome_And_Frame_From_Local_Server()
        {
            int port = GetFreePort();
            using var server = new MiniWsServer(port);
            server.Start();

            var client = new PiEEGClient();
            PiEEGWelcome welcome = null;
            PiEEGFrame frame = null;
            Exception error = null;

            client.OnWelcome += w => welcome = w;
            client.OnFrame   += f => frame = f;
            client.OnError   += e => error = e;

            var connectTask = client.ConnectAsync($"ws://127.0.0.1:{port}/");
            yield return WaitForTask(connectTask, 5f);
            Assert.IsNull(error, $"Connect error: {error}");
            Assert.IsTrue(client.IsOpen, "Client should be connected");

            // Wait until both messages arrive (server sends them on accept).
            float deadline = Time.realtimeSinceStartup + 5f;
            while ((welcome == null || frame == null) && Time.realtimeSinceStartup < deadline)
                yield return null;

            client.Dispose();
            server.Stop();

            Assert.IsNotNull(welcome, "Welcome message not received");
            Assert.AreEqual(250, welcome.sample_rate);
            Assert.AreEqual(8, welcome.channels);

            Assert.IsNotNull(frame, "Frame not received");
            Assert.AreEqual(1, frame.n);
            Assert.AreEqual(8, frame.channels.Count);
            Assert.IsNull(error, $"Runtime error: {error}");
        }

        // ---------- helpers ----------

        static int GetFreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        static IEnumerator WaitForTask(Task t, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!t.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            if (t.IsFaulted) throw t.Exception ?? new Exception("task faulted");
        }

        /// <summary>
        /// Minimal HttpListener-based WebSocket server that, on first connect, sends a welcome
        /// payload followed by a single fake EEG frame. Just enough to exercise the client.
        /// </summary>
        sealed class MiniWsServer : IDisposable
        {
            readonly HttpListener _listener;
            readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public MiniWsServer(int port)
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            }

            public void Start()
            {
                _listener.Start();
                _ = AcceptLoop();
            }

            async Task AcceptLoop()
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    if (!ctx.Request.IsWebSocketRequest)
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }

                    var wsCtx = await ctx.AcceptWebSocketAsync(null);
                    var ws = wsCtx.WebSocket;

                    await SendText(ws, "{\"status\":\"connected\",\"sample_rate\":250,\"channels\":8,\"filter\":false,\"recording\":false}");
                    await SendText(ws, "{\"t\":1.0,\"n\":1,\"channels\":[1,2,3,4,5,6,7,8]}");

                    // Keep socket open until cancelled / disposed.
                    var buf = new byte[1024];
                    while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                    {
                        try { await ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token); }
                        catch { break; }
                    }
                }
                catch { /* listener stopped */ }
            }

            static Task SendText(WebSocket ws, string s)
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                return ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }

            public void Stop()
            {
                _cts.Cancel();
                try { _listener.Stop(); } catch { }
                try { _listener.Close(); } catch { }
            }

            public void Dispose() => Stop();
        }
    }
}
