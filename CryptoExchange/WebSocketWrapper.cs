using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading.Tasks;
using WebSocket4Net;

namespace Exchange.Net
{
    public class WebSocketWrapper
    {
        public WebSocketWrapper(string url, string id = null)
        {
            this.url = url;
            this.id = id;
        }

        public IObservable<string> Observe()
        {
            var obs = Observable.FromEventPattern<EventHandler<MessageReceivedEventArgs>, object, MessageReceivedEventArgs>(
                h => { MessageReceived += h; Open(); },
                h => { MessageReceived -= h; Close(dispose: true); }
                );
            return obs.Select(x => x.EventArgs.Message);
        }

        public void Send(string text)
        {
            // IMPORTANT: This call might be called before socket is initialized/opened...
            // Think about it...
            webSocket?.Send(text);
        }

        private void Open()
        {
            try
            {
                webSocket = CreateSocket();
                webSocket.Open();
            }
            catch (Exception ex)
            {
                Debug.Print($"Socket {id} open exception: {ex.Message}");
                Task.Delay(TimeSpan.FromMilliseconds(250)).ContinueWith((x) => Open());
            }

        }

        private void Close(bool dispose = false)
        {
            if (webSocket?.State == WebSocketState.Open)
            {
                webSocket.Closed -= Ws_Closed;
                webSocket.Close();
                Debug.Print($"Socket {id} closed");
            }
            if (dispose)
                DisposeSocket(ref webSocket);
        }

        internal WebSocket CreateSocket()
        {
            var ws = new WebSocket(url);
            ws.Closed += Ws_Closed;
            ws.MessageReceived += Ws_MessageReceived;
            ws.Error += Ws_Error;
            ws.Opened += Ws_Opened; ;
            return ws;
        }

        internal void DisposeSocket(ref WebSocket ws)
        {
            if (ws == null)
                return;
            ws.Closed -= Ws_Closed;
            ws.MessageReceived -= Ws_MessageReceived;
            ws.Error -= Ws_Error;
            ws.Opened -= Ws_Opened;
            ws.Dispose();
            ws = null;
        }

        void Ws_Closed(object sender, EventArgs e)
        {
            Debug.Print($"Socket {id} closed; reconnecting...");
            DisposeSocket(ref webSocket);
            Task.Delay(TimeSpan.FromMilliseconds(250)).ContinueWith((x) => Open());
        }

        void Ws_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            Debug.Print($"Socket {id} error: {e.Exception.Message}");
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            Debug.Print($"Socket {id} opened");
        }

        private const int WsNormalCloseCode = 1000;
        private WebSocket webSocket;
        private string url, id;

        private event EventHandler<MessageReceivedEventArgs> MessageReceived;
    }
}
