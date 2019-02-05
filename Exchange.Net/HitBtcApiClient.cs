using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WebSocket4Net;

namespace Exchange.Net
{
    public class HitBtcApiClient : ExchangeApiCore
    {
        protected override string LogName => "hitbtc";

        public HitBtcApiClient() : base("hitbtc.hash", typeof (HMACSHA256))
        {
            if (IsSigned)
            {
                clientAuth.Authenticator = new RestSharp.Authenticators.HttpBasicAuthenticator(ApiKey.ToManagedString(), ApiSecret.ToManagedString());
                wsTrading.Opened += (sender, e) =>
                {
                    Debug.Print("Trading socket connected.");
                    var reqLogin = new HitBtc.WsRequestBase() { method = "login", id = "authenticate" };
                    var nn = nonce.ToString();
                    reqLogin.data.Add("algo", HitBtc.AuthAlgo.HS256.ToString());
                    reqLogin.data.Add("pKey", ApiKey.ToManagedString());
                    reqLogin.data.Add("nonce", nn);
                    reqLogin.data.Add("signature", ByteArrayToHexString(SignString(nn)));
                    wsTrading.Send(JsonConvert.SerializeObject(reqLogin));
                };
                wsTrading.Open();
            }
        }

        #region Public API

        const string GetSymbolsEndpoint = "public/symbol";
        const string GetTickersEndpoint = "public/ticker";
        const string GetDepthEndpoint = "public/orderbook";
        const string GetTradesEndpoint = "public/trades";

        public Task<ApiResult<IList<HitBtc.Symbol>>> GetSymbolsAsync()
        {
            var requestMessage = CreateRequestMessage(null, GetSymbolsEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<IList<HitBtc.Symbol>>(requestMessage, contentPath: "markets");
        }

        public Task<ApiResult<IList<HitBtc.Ticker>>> GetTickersAsync()
        {
            var requestMessage = CreateRequestMessage(null, GetTickersEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<IList<HitBtc.Ticker>>(requestMessage, contentPath: "tickers");
        }

        /// <summary>
        /// An order book is an electronic list of buy and sell orders for a specific symbol, organized by price level.
        /// </summary>
        /// <param name="market"></param>
        /// <param name="limit">Limit of orderbook levels, default 100. Set 0 to view full orderbook levels</param>
        /// <returns></returns>
        public Task<ApiResult<HitBtc.Depth>> GetDepthAsync(string market, int limit = 100)
        {
            var requestParams = new Dictionary<string, object>() { { "/market", market }, { "limit", limit } };
            var requestMessage = CreateRequestMessage(requestParams, GetDepthEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<HitBtc.Depth>(requestMessage, contentPath: $"depth-{market}");
        }

        public Task<ApiResult<List<HitBtc.Trade>>> GetTradesAsync(string market, int limit = 100)
        {
            var requestParams = new Dictionary<string, object>() { { "/market", market }, { "limit", limit } };
            var requestMessage = CreateRequestMessage(requestParams, GetTradesEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<List<HitBtc.Trade>>(requestMessage, contentPath: $"trades-{market}");
        }

        // NOTE: Ping is NOT required for WebSocket streams.
        public IObservable<HitBtc.Ticker> SubscribeMarketSummariesAsync(IEnumerable<string> symbols)
        {
            if (wsTicker.State == WebSocketState.None)
            {
                wsTicker.Error += Ws_Error;
                var obs = Observable.FromEventPattern<EventHandler<MessageReceivedEventArgs>, object, MessageReceivedEventArgs>(h => wsTicker.MessageReceived += h, h => wsTicker.MessageReceived -= h);
                wsTicker.Opened += (sender, e) =>
                {
                    Debug.Print("Tickers socket connected.");
                    foreach (var symbol in symbols)
                    {
                        //var req = $"{{\"method\":\"subscribeTicker\",\"params\":{{\"symbol\":\"{symbol}\"}}}}";
                        //wsTicker.Send(req);
                        var reqTickersSub = new HitBtc.WsRequestBase() { method = "subscribeTicker", id = "subscribeTicker" };
                        reqTickersSub.data.Add("symbol", symbol);
                        wsTicker.Send(JsonConvert.SerializeObject(reqTickersSub));
                    }
                };
                wsTicker.Open();
                return obs.SelectMany(OnTickerSocketMessage);
            }
            throw new InvalidOperationException();
            //ws.Security.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            //return obs.Select(OnTickerSocketMessage);
        }

        public IObservable<HitBtc.WsTrades> SubscribePublicTradesAsync(string symbol, int limit)
        {
            var ws = wsTrades;
            if (ws.State == WebSocketState.None)
            {
                ws.Error += Ws_Error;
                var obs = Observable.FromEventPattern<EventHandler<MessageReceivedEventArgs>, object, MessageReceivedEventArgs>(h => ws.MessageReceived += h, h => ws.MessageReceived -= h);
                ws.Opened += (sender, e) =>
                {
                    Debug.Print("Trades socket connected.");
                    //var req = $"{{\"method\":\"subscribeTrades\",\"params\":{{\"symbol\":\"{symbol}\"}}}}";
                    //wsTrades.Send(req);
                    var reqTradesSub = new HitBtc.WsRequestBase() { method = "subscribeTrades", id = "subscribeTrades" };
                    reqTradesSub.data.Add("symbol", symbol);
                    ws.Send(JsonConvert.SerializeObject(reqTradesSub));
                };
                ws.Open();
                return obs.SelectMany(OnTradeSocketMessage);
            }
            throw new InvalidOperationException();
        }
        #endregion

        #region Signed API

        public async Task<List<HitBtc.WsBalance>> GetTradingBalance()
        {
            var request = new RestSharp.RestRequest("/trading/balance", RestSharp.Method.GET);
            var response = await clientAuth.ExecuteTaskAsync<List<HitBtc.WsBalance>>(request).ConfigureAwait(false);
            if (response.IsSuccessful)
            {
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        #endregion

        #region Helpers

        protected HttpRequestMessage CreateRequestMessage(SecureString apiKey, SecureString apiSecret, IDictionary<string, object> requestParams, string endpoint, HttpMethod method)
        {
            requestParams.Add("apikey", apiKey.ToManagedString());
            requestParams.Add("nonce", nonce);
            var uri = httpClient.BuildUri(endpoint, requestParams); // Query w/o signature
            requestParams.Add("apisign", ByteArrayToHexString(SignString(uri.AbsoluteUri)));
            uri = httpClient.BuildUri(endpoint, requestParams);     // Query with signature
            HttpContent content = null;
            if (method == HttpMethod.Get)
                endpoint = endpoint + requestParams.BuildQuery(); // Take query string with signature.
            else
                content = new StringContent(requestParams.BuildQuery()); // Take query string with signature.
            var requestMessage = new HttpRequestMessage(method, endpoint) { Content = content };
            return requestMessage;
        }

        protected HttpRequestMessage CreateRequestMessage(IDictionary<string, object> requestParams, string endpoint, HttpMethod method)
        {
            var query = requestParams.BuildQuery();
            HttpContent content = null;
            if (method == HttpMethod.Get)
                endpoint = endpoint + query;
            else
                content = new StringContent(query);
            var requestMessage = new HttpRequestMessage(method, new Uri(endpoint, UriKind.Relative)) { Content = content };
            return requestMessage;
        }

        protected async Task<ApiResult<T>> ExecuteRequestAsync<T>(HttpRequestMessage requestMessage, string contentPath = null)
        {
            var sw = Stopwatch.StartNew();
            var responseMessage = await httpClient.SendAsync(requestMessage).ConfigureAwait(false);
            var content = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
            sw.Stop();

            var uri = responseMessage.RequestMessage.RequestUri.ToString();
            Debug.Print($"{requestMessage.Method} {uri} : {responseMessage.StatusCode}");

            ApiResult<T> result = null;

            if (responseMessage.IsSuccessStatusCode)
            {
                Log.DebugFormat("{0} {1} - {2}ms : {3} ({4}).", requestMessage.Method, uri, sw.ElapsedMilliseconds, responseMessage.ReasonPhrase, (int)responseMessage.StatusCode);
                DumpJson(contentPath, content);
                result = new ApiResult<T>(JsonConvert.DeserializeObject<T>(content.Replace(",[]", string.Empty)), null, sw.ElapsedMilliseconds);
            }
            else
            {
                Debug.Print($"{content}");
                result = new ApiResult<T>(default(T), new ApiError((int)responseMessage.StatusCode, responseMessage.ReasonPhrase), sw.ElapsedMilliseconds);
            }

            requestMessage.Dispose();
            responseMessage.Content.Dispose();
            responseMessage.Dispose();

            return result;
        }

        #endregion

        internal ApiResult<T> ExecutePublicRequest<T>(RestSharp.IRestRequest request) where T : new()
        {
            return ExecutePublicRequestAsync<T>(request).Result;
        }

        internal async Task<ApiResult<T>> ExecutePublicRequestAsync<T>(RestSharp.IRestRequest request) where T : new()
        {
            var result = await client.ExecuteTaskAsync<T>(request);
            if (result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return new ApiResult<T>(result.Data, null);
            }
            else
            {
                var err = JsonConvert.DeserializeObject<HitBtc.Error>(result.Content);
                return new ApiResult<T>(default(T), new ApiError(err.code, err.message));
            }
        }

        internal IEnumerable<HitBtc.Ticker> OnTickerSocketMessage(EventPattern<object, MessageReceivedEventArgs> p)
        {
            var response = JsonConvert.DeserializeObject<HitBtc.WsResponse<HitBtc.Ticker>>(p.EventArgs.Message);
            if (response.data == null && response.result == true)
                return Enumerable.Empty<HitBtc.Ticker>();
            else
                return Enumerable.Repeat(response.data, 1);
        }

        internal IEnumerable<HitBtc.WsTrades> OnTradeSocketMessage(EventPattern<object, MessageReceivedEventArgs> p)
        {
            var response = JsonConvert.DeserializeObject<HitBtc.WsResponse<HitBtc.WsTrades>>(p.EventArgs.Message);
            if (response.data == null && response.result == true)
                return Enumerable.Empty<HitBtc.WsTrades>();
            else
                return Enumerable.Repeat(response.data, 1);
        }

        internal void Ws_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            Debug.Print(e.Exception.ToString());
        }

        RestSharp.RestClient client = new RestSharp.RestClient(restApiUrl);
        RestSharp.RestClient clientAuth = new RestSharp.RestClient(restApiUrl);
        WebSocket wsTicker = new WebSocket(streamingApiUrl);
        WebSocket wsOrderbook = new WebSocket(streamingApiUrl);
        WebSocket wsTrades = new WebSocket(streamingApiUrl);
        WebSocket wsTrading = new WebSocket(streamingApiUrl);

        const string restApiUrl = "https://api.hitbtc.com/api/2/";
        const string streamingApiUrl = "wss://api.hitbtc.com/api/2/ws";
        private static HttpClient httpClient = new HttpClient() { BaseAddress = new Uri(restApiUrl) };
    }
}

namespace HitBtc
{
    // https://api.hitbtc.com/

    // All timestamps are returned in ISO8601 format in UTC. Example: "2017-04-03T10:20:49.315Z"
    // All finance data, i.e. price, quantity, fee etc., should be arbitrary precision numbers and string representation. Example: "10.2000058"

    /* Rate Limits apply:
        * For the Market data, the limit is 100 requests/second per IP;
        * For Trading, the limit is 100 request/second per user;
        * For other requests, including Trading history, the limit is 10 requests/second per user. */

    public enum HttpStatusCode
    {
        OK = 200,                   // Successful request
        BadRequest = 400,           // Returns JSON with the error message
        Unauthorized = 401,         // Authorisation required or failed
        Forbidden = 403,            // Action is forbidden for API key
        TooManyRequests = 429,      // Your connection is being rate limited
        InternalServer = 500,       // Internal Server Error
        ServiceUnavailable = 503,   // Service is down for maintenance
        GatewayTimeout = 504        // Request timeout expired
    }

    public enum AuthAlgo
    {
        BASIC,
        HS256
    }

    public class Error
    {
        public int code { get; set; }
        public string message { get; set; }
        public string description { get; set; }
    }

    public class Symbol
    {
        public string id { get; set; }
        public string baseCurrency { get; set; }
        public string quoteCurrency { get; set; }
        public decimal quantityIncrement { get; set; }
        public decimal tickSize { get; set; }
        public decimal takeLiquidityRate { get; set; }
        public decimal provideLiquidityRate { get; set; }
        public string feeCurrency { get; set; }
    }

    public class Ticker
    {
        public decimal? ask { get; set; }
        public decimal? bid { get; set; }
        public decimal? last { get; set; }
        public decimal? open { get; set; }
        public decimal? low { get; set; }
        public decimal? high { get; set; }
        public decimal? volume { get; set; }
        public decimal? volumeQuote { get; set; }
        public DateTime timestamp { get; set; }
        public string symbol { get; set; }
    }

    public class Trade
    {
        public long id { get; set; }
        public decimal price { get; set; }
        public decimal quantity { get; set; }
        public string side { get; set; }
        public DateTime timestamp { get; set; }
    }

    public class DepthEntry
    {
        public decimal price { get; set; }
        public decimal size { get; set; }
    }

    public class Depth
    {
        public IList<DepthEntry> ask { get; set; }
        public IList<DepthEntry> bid { get; set; }
    }

    public class WsRequestBase
    {
        [JsonProperty(Order = 0)]
        public string method { get; set; }
        [JsonProperty("params", Order = 1)]
        public Dictionary<string, object> data = new Dictionary<string, object>();
        [JsonProperty(Order = 2)]
        public string id { get; set; }
    }


    public class WsResponseBase
    {
        public string jsonrpc { get; set; }
        public bool result { get; set; }
        public string id { get; set; }
    }

    public class WsResponse<T> : WsResponseBase
    {
        [JsonProperty("params")]
        public T data { get; set; }
    }

    public class WsTrades
    {
        public IList<Trade> data { get; set; }
        public string symbol { get; set; }
    }

    public class WsBalance
    {
        public string currency { get; set; }
        public decimal available { get; set; }
        public decimal reserved { get; set; }
    }
}
