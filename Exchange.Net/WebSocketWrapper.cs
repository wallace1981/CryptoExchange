using Huobi.WebSocketAPI;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading.Tasks;
using WebSocket4Net;

namespace Exchange.Net
{
    public class WebSocketWrapper
    {
        public WebSocketWrapper(string url, string id = null, System.Security.Authentication.SslProtocols sslProtocols = System.Security.Authentication.SslProtocols.Default)
        {
            this.url = url;
            this.id = id;
            this.sslProtocols = sslProtocols;
        }

        public virtual IObservable<string> Observe()
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
            if (webSocket?.State == WebSocketState.Open)
                webSocket?.Send(text);
            else
                sendQueue.Enqueue(text);
        }

        protected void Open()
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

        protected void Close(bool dispose = false)
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
            ws.Security.EnabledSslProtocols = sslProtocols;
            ws.Closed += Ws_Closed;
            ws.DataReceived += Ws_DataReceived;
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
            ws.DataReceived -= Ws_DataReceived;
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

        void Ws_DataReceived(object sender, WebSocket4Net.DataReceivedEventArgs e)
        {
            DataReceived?.Invoke(this, e);
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            Debug.Print($"Socket {id} opened");
            while (!sendQueue.IsEmpty)
            {
                if (sendQueue.TryDequeue(out string text))
                    webSocket.Send(text);
            }
        }

        private const int WsNormalCloseCode = 1000;
        private WebSocket webSocket;
        private string url, id;
        private System.Security.Authentication.SslProtocols sslProtocols = System.Security.Authentication.SslProtocols.Default;

        protected event EventHandler<MessageReceivedEventArgs> MessageReceived;
        protected event EventHandler<WebSocket4Net.DataReceivedEventArgs> DataReceived;
        private ConcurrentQueue<string> sendQueue = new ConcurrentQueue<string>();
    }

    public class HuobiWebSocketWrapper : WebSocketWrapper
    {
        public HuobiWebSocketWrapper(string url, string id = null, System.Security.Authentication.SslProtocols sslProtocols = System.Security.Authentication.SslProtocols.Default) : base(url, id, sslProtocols)
        {
        }

        public override IObservable<string> Observe()
        {
            var obs = Observable.FromEventPattern<EventHandler<WebSocket4Net.DataReceivedEventArgs>, object, WebSocket4Net.DataReceivedEventArgs>(
                h => { DataReceived += h; Open(); },
                h => { DataReceived -= h; Close(dispose: true); }
                );
            return obs.Select(x => Process(x.EventArgs)).Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private string Process(WebSocket4Net.DataReceivedEventArgs args)
        {
            return Process(args.Data);
        }

        private string Process(byte[] data)
        {
            var msgStr = GZipHelper.GZipDecompressString(data);
            //Debug.WriteLine($"{msgStr}");
            Huobi.WsResponseMessage wsMsg = JsonConvert.DeserializeObject<Huobi.WsResponseMessage>(msgStr);
            if (wsMsg.ping != 0)
            {
                Send(JsonConvert.SerializeObject(new Huobi.WsPong() { pong = wsMsg.ping }));
            }
            else if (wsMsg.pong != 0)
            {
                Send(JsonConvert.SerializeObject(new Huobi.WsPing() { ping = wsMsg.pong }));
            }
            else if (wsMsg.subbed != null)
            {
                // ignore;
                if (wsMsg.status != "ok")
                {
                    Debug.WriteLine($"Failed to subscribe to {wsMsg.subbed}");
                }
                else
                {
                    Debug.WriteLine($"Subscribed to {wsMsg.subbed}");
                }
            }
            else if (wsMsg.ch != null)
            {
                //Debug.WriteLine($"{wsMsg.ch}: {wsMsg.subbed}");
                return msgStr;
            }

            return null;
        }

    }
}
