using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Security;
using System.Threading.Tasks;

namespace Exchange.Net
{
    public class CryptopiaApiClient : ExchangeApiCore
    {
        // https://support.cryptopia.co.nz/csm?id=kb_article&sys_id=a75703dcdbb9130084ed147a3a9619bc
        protected override string LogName => "cryptopia";

        #region Public API
        // https://support.cryptopia.co.nz/csm?id=kb_article&sys_id=40e9c310dbf9130084ed147a3a9619eb

        const string GetTradePairsEndpoint = "GetTradePairs";
        const string GetMarketsEndpoint = "GetMarkets";
        const string GetMarketHistoryEndpoint = "GetMarketHistory";
        const string GetMarketOrdersEndpoint = "GetMarketOrders";

        public async Task<ApiResult<List<Cryptopia.TradePair>>> GetTradePairsAsync()
        {
            var requestMessage = CreateRequestMessage(null, GetTradePairsEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<List<Cryptopia.TradePair>>(requestMessage, contentPath: "markets").ConfigureAwait(false);
            return result;
        }

        public async Task<ApiResult<List<Cryptopia.Market>>> GetMarketsAsync()
        {
            var requestMessage = CreateRequestMessage(null, GetMarketsEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<List<Cryptopia.Market>>(requestMessage, contentPath: "tickers").ConfigureAwait(false);
            return result;
        }

        public IObservable<ApiResult<List<Cryptopia.Market>>> ObserveMarketSummaries()
        {
            var obs = Observable.FromAsync(GetMarketsAsync);
            return Observable.Interval(TimeSpan.FromSeconds(2)).SelectMany(x => obs);
        }

        public Task<ApiResult<List<Cryptopia.MarketHistory>>> GetMarketHistoryAsync(string market)
        {
            var requestParams = new Dictionary<string, object>() { { "/market", market } };
            var requestMessage = CreateRequestMessage(requestParams, GetMarketHistoryEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<List<Cryptopia.MarketHistory>>(requestMessage, contentPath: $"trades-{market}");
        }

        public Task<ApiResult<Cryptopia.OrderBook>> GetOrderBookAsync(string market, int limit = 100)
        {
            var requestParams = new Dictionary<string, object>() { { "/market", market }, { "/limit", limit } };
            var requestMessage = CreateRequestMessage(requestParams, GetMarketOrdersEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Cryptopia.OrderBook>(requestMessage, contentPath: $"depth-{market}");
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
                var tmp = JsonConvert.DeserializeObject<Cryptopia.ApiResult<T>>(content);
                if (tmp.Success)
                    result = new ApiResult<T>(tmp.Data, null, sw.ElapsedMilliseconds);
                else
                    result = new ApiResult<T>(tmp.Data, new ApiError(-1, tmp.Message), sw.ElapsedMilliseconds);
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

        const string restApiUrl = "https://www.cryptopia.co.nz/api/";
        private static HttpClient httpClient = new HttpClient() { BaseAddress = new Uri(restApiUrl) };
    }
}

namespace Cryptopia
{
    public class TradePair
    {
        public long Id { get; set; }
        public string Label { get; set; }
        public string Currency { get; set; }
        public string Symbol { get; set; }
        public string BaseCurrency { get; set; }
        public string BaseSymbol { get; set; }
        public string Status { get; set; }
        public string StatusMessage { get; set; }
        public decimal TradeFee { get; set; }
        public decimal MinimumTrade { get; set; }
        public decimal MaximumTrade { get; set; }
        public decimal MinimumBaseTrade { get; set; }
        public decimal MaximumBaseTrade { get; set; }
        public decimal MinimumPrice { get; set; }
        public decimal MaximumPrice { get; set; }
    }

    public class Market
    {
        public long TradePairId { get; set; }
        public string Label { get; set; }
        public decimal AskPrice { get; set; }
        public decimal BidPrice { get; set; }
        public decimal Low { get; set; }
        public decimal High { get; set; }
        public decimal Volume { get; set; }
        public decimal LastPrice { get; set; }
        public decimal BuyVolume { get; set; }
        public decimal SellVolume { get; set; }
        public decimal Change { get; set; }
        public decimal Open { get; set; }
        public decimal Close { get; set; }
        public decimal BaseVolume { get; set; }
        public decimal BaseBuyVolume { get; set; }
        public decimal BaseSellVolume { get; set; }
    }

    public class MarketHistory
    {
        public long TradePairId { get; set; }
        public string Label { get; set; }
        public string Type { get; set; }        // Buy/Sell
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
        public decimal Total { get; set; }
        public long Timestamp { get; set; }
    }

    public class OrderBookEntry
    {
        public long TradePairId { get; set; }
        public string Label { get; set; }
        public decimal Price { get; set; }
        public decimal Volume { get; set; }
        public decimal Total { get; set; }
    }

    public class OrderBook
    {
        public IList<OrderBookEntry> Buy { get; set; }
        public IList<OrderBookEntry> Sell { get; set; }
    }

    public class ApiResult<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
    }
}