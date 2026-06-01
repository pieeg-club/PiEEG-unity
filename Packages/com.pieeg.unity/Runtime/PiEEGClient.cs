using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PiEEG.Unity
{
    /// <summary>
    /// Minimal async WebSocket client for PiEEG-server. Frames are parsed off the main thread
    /// and dispatched via <see cref="OnFrame"/> / <see cref="OnWelcome"/>. Callers wanting to
    /// touch Unity API from those callbacks should marshal to the main thread themselves
    /// (see <see cref="PiEEGStream"/> for a ready-made MonoBehaviour wrapper).
    /// </summary>
    public class PiEEGClient : IDisposable
    {
        public event Action<PiEEGFrame> OnFrame;
        public event Action<PiEEGWelcome> OnWelcome;
        public event Action<Exception> OnError;
        public event Action OnClosed;

        public bool IsOpen => _ws != null && _ws.State == WebSocketState.Open;

        ClientWebSocket _ws;
        CancellationTokenSource _cts;

        public async Task ConnectAsync(string url, CancellationToken token = default)
        {
            Disconnect();
            _ws = new ClientWebSocket();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            await _ws.ConnectAsync(new Uri(url), _cts.Token);
            _ = Task.Run(ReceiveLoop);
        }

        public Task SendAsync(string json)
        {
            if (!IsOpen) return Task.CompletedTask;
            var bytes = Encoding.UTF8.GetBytes(json);
            return _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[64 * 1024];
            var sb = new StringBuilder();
            try
            {
                while (IsOpen && !_cts.IsCancellationRequested)
                {
                    sb.Length = 0;
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                            OnClosed?.Invoke();
                            return;
                        }
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                    } while (!res.EndOfMessage);

                    Dispatch(sb.ToString());
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
            finally
            {
                OnClosed?.Invoke();
            }
        }

        void Dispatch(string json)
        {
            // Cheap discriminator: welcome message has "status", frames have "channels".
            if (json.IndexOf("\"status\"", StringComparison.Ordinal) >= 0 &&
                json.IndexOf("\"channels\"", StringComparison.Ordinal) >= 0 &&
                json.IndexOf("\"sample_rate\"", StringComparison.Ordinal) >= 0)
            {
                try { OnWelcome?.Invoke(JsonUtility.FromJson<PiEEGWelcome>(json)); }
                catch (Exception ex) { OnError?.Invoke(ex); }
                return;
            }

            try { OnFrame?.Invoke(JsonUtility.FromJson<PiEEGFrame>(json)); }
            catch (Exception ex) { OnError?.Invoke(ex); }
        }

        public void Disconnect()
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _ws?.Dispose(); } catch { /* ignore */ }
            _ws = null;
            _cts = null;
        }

        public void Dispose() => Disconnect();
    }
}
