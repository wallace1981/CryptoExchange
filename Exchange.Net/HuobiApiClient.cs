using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Huobi.WebSocketAPI;
using Newtonsoft.Json;
using RestSharp.Deserializers;
using WebSocket4Net;

namespace Exchange.Net
{
    public class HuobiApiClient : ExchangeApiCore
    {
        public delegate void DetailTickHandler(string symbol, Huobi.WsTick tick);

        public event DetailTickHandler DetailTick;

        #region Public API

        const string MarketsEndpoint = "v1/common/symbols";
        const string TickersEndpoint = "market/tickers";
        const string HistoryEndpoint = "market/trade";
        const string DepthEndpoint = "market/depth";

        public Task<ApiResult<List<Huobi.Market>>> GetMarketsAsync()
        {
            var requestMessage = CreateRequestMessage(null, MarketsEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<List<Huobi.Market>>(requestMessage, contentPath: "markets");
        }

        public Task<ApiResult<List<Huobi.Kline>>> GetPriceTickerAsync()
        {
            var requestMessage = CreateRequestMessage(null, TickersEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<List<Huobi.Kline>>(requestMessage, contentPath: "tickers");
        }

        public Task<ApiResult<Huobi.Depth>> GetDepthAsync(string market, string step = "step0")
        {
            var requestParams = new Dictionary<string, object>() { { "symbol", market }, { "type", step } };
            var requestMessage = CreateRequestMessage(requestParams, DepthEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Huobi.Depth>(requestMessage, contentPath: $"depth-{market}");
        }

        public Task<ApiResult<Huobi.TradeWrapper>> GetMarketHistoryAsync(string market)
        {
            var requestParams = new Dictionary<string, object>() { { "symbol", market } };
            var requestMessage = CreateRequestMessage(requestParams, HistoryEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Huobi.TradeWrapper>(requestMessage, contentPath: $"trades-{market}");
        }

        #endregion

        public IObservable<Huobi.WsTick> ObserveMarketSummaries(IEnumerable<string> symbols)
        {
            // Huobi hasn't API to get current price for all symbols at once.
            // We will subscribe to socket here, but will return List of WsTick objects
            // which value later will be updated by socket messages.

            const string url = "wss://api.huobi.pro/ws";

            var ws = new HuobiWebSocketWrapper(url, "ticker", System.Security.Authentication.SslProtocols.Tls12);
            foreach (var symbol in symbols)
            {
                string req = $"market.{symbol.ToLower()}.detail";
                ws.Send(JsonConvert.SerializeObject(new Huobi.WsSubRequest() { id = req, sub = req }));
            }
            return ws.Observe().Buffer(TimeSpan.FromSeconds(1)).SelectMany(x => x.ToObservable()).Select(OnSocketData2);

            //var ws = new WebSocket(url);
            //ws.Security.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            //ws.DataReceived += new EventHandler<WebSocket4Net.DataReceivedEventArgs>(OnSocketData);
            //ws.Error += Ws_Error;
            //ws.Opened += (sender, e) =>
            //{
            //    foreach (var symbol in symbols)
            //    {
            //        string req = $"market.{symbol.ToLower()}.detail";
            //        ws.Send(JsonConvert.SerializeObject(new Huobi.WsSubRequest() { id = req, sub = req }));
            //    }
            //};
            //ws.Open();
        }

        private void OnSocketData(object sender, WebSocket4Net.DataReceivedEventArgs args)
        {
            WebSocket ws = (WebSocket)sender;
            var msgStr = GZipHelper.GZipDecompressString(args.Data);
            //System.Diagnostics.Trace.WriteLine($"{msgStr}");
            Huobi.WsResponseMessage wsMsg = JsonConvert.DeserializeObject<Huobi.WsResponseMessage>(msgStr);
            if (wsMsg.ping != 0)
            {
                ws.Send(JsonConvert.SerializeObject(new Huobi.WsPong() { pong = wsMsg.ping }));
            }
            else if (wsMsg.pong != 0)
            {
                ws.Send(JsonConvert.SerializeObject(new Huobi.WsPing() { ping = wsMsg.pong }));
            }
            else if (wsMsg.subbed != null)
            {
                // ignore;
                if (wsMsg.status != "ok")
                {
                    System.Diagnostics.Trace.WriteLine($"Failed to subscribe to {wsMsg.subbed}");
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine($"Subscribed to {wsMsg.subbed}");
                }
            }
            else
            {
                var parts = wsMsg.ch.Split('.');
                var response = JsonConvert.DeserializeObject<Huobi.WsTickResponseMessage>(msgStr);
                if (DetailTick != null)
                    DetailTick.Invoke(parts[1], response.tick);
            }
        }

        private Huobi.WsTick OnSocketData2(string content)
        {
            var wsMsg = JsonConvert.DeserializeObject<Huobi.WsTickResponseMessage>(content);
            var parts = wsMsg.ch.Split('.');
            wsMsg.tick.symbol = parts[1];
            return wsMsg.tick;
        }

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
                var tmp = JsonConvert.DeserializeObject<Huobi.ApiResult<T>>(content);
                if (tmp.success)
                    result = new ApiResult<T>(tmp.tick != null ? tmp.tick : tmp.data, null, sw.ElapsedMilliseconds);
                else
                    result = new ApiResult<T>(tmp.tick != null ? tmp.tick : tmp.data, new ApiError(-1, tmp.errMsg), sw.ElapsedMilliseconds);
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

        const string restApiUrl = "https://api.huobi.pro/";
        protected override string LogName => "huobi";
        private static HttpClient httpClient = new HttpClient() { BaseAddress = new Uri(restApiUrl) };
        // v1/common/symbols

        internal Huobi.ApiResult<IList<Huobi.Market>> DeserializeMarkets(string json)
        {
            return JsonConvert.DeserializeObject<Huobi.ApiResult<IList<Huobi.Market>>>(json);
            var sw = Stopwatch.StartNew();
            // status: "string"
            // data: []
            var result = new Huobi.ApiResult<IList<Huobi.Market>>();

            JsonTextReader reader = new JsonTextReader(new StringReader(json));
            if (reader.Read() && reader.TokenType != JsonToken.StartObject)
                return null;
            while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
            {
                switch (reader.Value)
                {
                    case "status":  // string
                        result.status = reader.ReadAsString();
                        break;
                    case "data":    // array
                        if (reader.Read() && reader.TokenType == JsonToken.StartArray)
                        {
                            var arr = new List<Huobi.Market>();
                            while (reader.Read() && reader.TokenType == JsonToken.StartObject)
                            {
                                var m = new Huobi.Market();
                                while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                                {
                                    switch (reader.Value)
                                    {
                                        case "base-currency":
                                            m.baseCurrency = reader.ReadAsString();
                                            break;
                                        case "quote-currency":
                                            m.quoteCurrency = reader.ReadAsString();
                                            break;
                                        case "price-precision":
                                            m.pricePrecision = reader.ReadAsInt32().GetValueOrDefault();
                                            break;
                                        case "amount-precision":
                                            m.amountPrecision = reader.ReadAsInt32().GetValueOrDefault();
                                            break;
                                        case "symbol-partition":
                                            m.symbolPartition = reader.ReadAsString();
                                            break;
                                        case "symbol":
                                            m.symbol = reader.ReadAsString();
                                            break;
                                        default:
                                            reader.Read(); // skip unknown tags
                                            break;
                                    }
                                }
                                arr.Add(m);
                            }
                            if (reader.TokenType == JsonToken.EndArray)
                                result.data = arr;
                        }
                        break;
                    case "ts":      // timestamp: long
                        break;
                    case "err-code":
                        result.errCode = reader.ReadAsString();
                        break;
                    case "err-msg":
                        result.errMsg = reader.ReadAsString();
                        break;
                    default:
                        reader.Read(); // skip
                        break;
                }
            }
            Debug.Print($"Huobi markets: took {sw.ElapsedMilliseconds}ms");
            if (reader.Read() && reader.TokenType != JsonToken.EndObject)
                return null;

            return result;
        }

        internal Huobi.ApiResult<IList<Huobi.Kline>> DeserializeMarketSummaries(string json)
        {
            return JsonConvert.DeserializeObject<Huobi.ApiResult<IList<Huobi.Kline>>>(json);
            // status: "string"
            // data: []
            var result = new Huobi.ApiResult<IList<Huobi.Kline>>();
            var sw = Stopwatch.StartNew();
            JsonTextReader reader = new JsonTextReader(new StringReader(json));
            while (reader.Read() && reader.TokenType != JsonToken.StartObject)
                ;
            while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
            {
                switch (reader.Value)
                {
                    case "status":  // string
                        result.status = reader.ReadAsString();
                        break;
                    case "data":    // array
                        if (reader.Read() && reader.TokenType == JsonToken.StartArray)
                        {
                            var arr = new List<Huobi.Kline>();
                            while (reader.Read() && reader.TokenType == JsonToken.StartObject)
                            {
                                var m = new Huobi.Kline();
                                while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                                {
                                    switch (reader.Value)
                                    {
                                        case "low":
                                            m.low = reader.ReadAsDecimal().GetValueOrDefault();
                                            break;
                                        case "high":
                                            m.high = reader.ReadAsDecimal().GetValueOrDefault();
                                            break;
                                        case "open":
                                            m.open = reader.ReadAsDecimal().GetValueOrDefault();
                                            break;
                                        case "close":
                                            m.close = reader.ReadAsDecimal().GetValueOrDefault();
                                            break;
                                        case "amount":
                                            m.amount = reader.ReadAsDecimal().GetValueOrDefault();
                                            break;
                                        case "vol":
                                            m.vol = reader.ReadAsDecimal().GetValueOrDefault();
                                            break;
                                        case "count":
                                            m.count = reader.ReadAsInt32().GetValueOrDefault();
                                            break;
                                        case "symbol":
                                            m.symbol = reader.ReadAsString();
                                            break;
                                        default:
                                            reader.Read(); // skip unknown tags
                                            break;
                                    }
                                }
                                arr.Add(m);
                            }
                            if (reader.TokenType == JsonToken.EndArray)
                                result.data = arr;
                        }
                        break;
                    case "ts":      // timestamp: long
                        reader.Read();
                        break;
                    case "err-code":
                        result.errCode = reader.ReadAsString();
                        break;
                    case "err-msg":
                        result.errMsg = reader.ReadAsString();
                        break;
                    default:
                        reader.Read(); // skip
                        break;
                }
            }
            if (reader.Read() && reader.TokenType != JsonToken.EndObject)
                return null;
            Debug.Print($"Huobi tickers: took {sw.ElapsedMilliseconds}ms");
            return result;
        }
    }
}

namespace Huobi
{
    public class Market
    {
        [JsonProperty("base-currency")]
        public string baseCurrency { get; set; }
        [JsonProperty("quote-currency")]
        public string quoteCurrency { get; set; }
        [JsonProperty("price-precision")]
        public int pricePrecision { get; set; }
        [JsonProperty("amount-precision")]
        public int amountPrecision { get; set; }
        [JsonProperty("symbol-partition")]
        public string symbolPartition { get; set; }
        public string symbol { get; set; }
    }

    public class Kline
    {
        public decimal open { get; set; }
        public decimal close { get; set; }
        public decimal low { get; set; }
        public decimal high { get; set; }
        public decimal amount { get; set; }
        public long count { get; set; }
        public decimal vol { get; set; }
        public string symbol { get; set; }
    }

    public class Depth
    {
        public long id { get; set; }
        public long ts { get; set; }
        public decimal[][] bids { get; set; }
        public decimal[][] asks { get; set; }
    }

    public class TradeWrapper
    {
        public long id { get; set; }
        public long ts { get; set; }
        public List<Trade> data { get; set; }
    }

    public class Trade
    {
        public string id { get; set; } // HUOBI WTF?!
        public decimal price { get; set; }
        public decimal amount { get; set; }
        public string direction { get; set; }
        public long ts { get; set; }
    }

    public class ApiResult<T>
    {
        public string status { get; set; }
        public string ch { get; set; }
        public long ts { get; set; }
        [JsonProperty("err-code")]
        public string errCode { get; set; }
        [JsonProperty("err-msg")]
        public string errMsg { get; set; }
        public T data { get; set; }
        public T tick { get; set; }
        [JsonIgnore]
        public bool success => (status == "ok");
    }

    public class WsSubRequest
    {
        public string sub { get; set; }
        public string id { get; set; }
    }

    public class WsResponseMessage
    {
        public long ping { get; set; }
        public long pong { get; set; }
        public string id { get; set; }
        public string subbed { get; set; }
        public long ts { get; set; }
        public string status { get; set; }
        public string ch { get; set; }
    }

    public class WsTickResponseMessage : WsResponseMessage
    {
        public WsTick tick { get; set; }
    }

    public class WsTick
    {
        public decimal amount { get; set; }
        public decimal open { get; set; }
        public decimal high { get; set; }
        public decimal low { get; set; }
        public decimal close { get; set; }
        public decimal vol { get; set; }
        public long ts { get; set; }
        public long id { get; set; }
        public long count { get; set; }
        public string symbol { get; set; }
    }

    public class WsTrade
    {
        public long id { get; set; }
        public decimal price { get; set; }
        public long time { get; set; }
        public decimal amount { get; set; }
        public string direction { get; set; } // buy/sell
        public long tradeId { get; set; }
        public long ts { get; set; }
    }

    public class WsPong
    {
        public long pong { get; set; }
    }

    public class WsPing
    {
        public long ping { get; set; }
    }
}