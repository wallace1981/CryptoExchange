using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Exchange.Net
{
    public class DsxApiClient : ExchangeApiCore
    {
        // V3: https://dsx.uk/developers/publicApi
        // V2: https://dsx.docs.apiary.io/

        protected override string LogName => "dsx";

        public DsxApiClient() : base("dsx.hash", typeof(HMACSHA512))
        {
        }

        private string SignRequest(string postBody)
        {
            return Convert.ToBase64String(SignString(postBody));
        }

        #region Public API

        private const string GetServerTimeEndpoint = "";
        private const string GetExchangeInfoEndpoint = "mapi/info";
        private const string GetPriceTickerEndpoint = "";
        private const string Get24hrPriceTickerEndpoint = "mapi/ticker";
        private const string GetTradesEndpoint = "mapi/trades";
        private const string GetDepthEndpoint = "mapi/depth";
        private const string GetLastBarsEndpoint = "mapi/lastBars";
        private const string GetBarsFromMomentEndpoint = "mapi/barsFromMoment";
        private const string GetBarsInPeriodEndpoint = "mapi/periodBars";

        public Task<ApiResult<DSX.ExchangeInfo>> GetExchangeInfoAsync()
        {
            var requestMessage = CreateRequestMessage(null, GetExchangeInfoEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<DSX.ExchangeInfo>(requestMessage, contentPath: "markets");
        }

        public Task<ApiResult<Dictionary<string, DSX.Ticker>>> GetTickerAsync(string symbols)
        {
            var requestParams = new Dictionary<string, object>() { { "/symbols", symbols }, { "ignore_invalid", 1 } };
            var requestMessage = CreateRequestMessage(requestParams, Get24hrPriceTickerEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Dictionary<string, DSX.Ticker>>(requestMessage, contentPath: "tickers");
        }

        public Task<ApiResult<Dictionary<string, DSX.Trade[]>>> GetTradesAsync(string symbol, int limit = 150)
        {
            var requestParams = new Dictionary<string, object>() { { "/symbol", symbol }, { "limit", limit } };
            var requestMessage = CreateRequestMessage(requestParams, GetTradesEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Dictionary<string, DSX.Trade[]>>(requestMessage, contentPath: $"trades-{symbol}");
        }

        public Task<ApiResult<Dictionary<string, DSX.OrderBook>>> GetDepthAsync(string symbol)
        {
            var requestParams = new Dictionary<string, object>() { { "/symbol", symbol } };
            var requestMessage = CreateRequestMessage(requestParams, GetDepthEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Dictionary<string, DSX.OrderBook>>(requestMessage, contentPath: $"depth-{symbol}");
        }

        public Task<ApiResult<Dictionary<string, List<DSX.Bar>>>> GetLastBarstAsync(string symbols, string period, int amount)
        {
            var requestParams = new Dictionary<string, object>() { { "/symbols", symbols }, { "/period", period }, { "/amount", amount }, { "ignore_invalid", 1 } };
            var requestMessage = CreateRequestMessage(requestParams, GetLastBarsEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Dictionary<string, List<DSX.Bar>>>(requestMessage, contentPath: "lastBars");
        }

        public Task<ApiResult<Dictionary<string, List<DSX.Bar>>>> GetBarsFromMomentAsync(string symbols, string period, DateTime moment)
        {
            var requestParams = new Dictionary<string, object>() { { "/symbols", symbols }, { "/period", period }, { "/moment", moment.ToUnixSeconds() }, { "ignore_invalid", 1 } };
            var requestMessage = CreateRequestMessage(requestParams, GetBarsFromMomentEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Dictionary<string, List<DSX.Bar>>>(requestMessage, contentPath: "momentBars");
        }
        #endregion

        #region Account API

        const string NewOrderEndpoint = "tapi/v2/order/new";
        const string OrderStatusEndpoint = "tapi/v2/order/status";
        const string CancelOrderEndpoint = "tapi/v2/order/cancel";
        const string CancellAllOrdersEndpoint = "tapi/v2/order/cancel/all";
        const string OrdersEndpointV2 = "tapi/v2/orders";
        const string OrdersEndpoint = "tapi/v3/orders";
        const string TransactionsEndpoint = "tapi/v2/history/transactions";
        const string TradesHistoryEndpoint = "tapi/v2/history/trades";
        const string OrdersHistoryEndpoint = "tapi/v2/history/orders";
        const string AccountEndpoint = "tapi/v3/info/account";
        const string FeesEndpoint = "tapi/v3/fees";
        const string TradingVolumeEndpoint = "tapi/v3/volume";


        // NOTE: We are going to serialize signed API calls using semaphore.
        // This way 'nonce' will always be correct.
        // TODO: Implement calls per minute/hour (because API has numeric limitation for signed calls).
        private SemaphoreSlim slim = new SemaphoreSlim(1);

        public Task<ApiResult<Dictionary<long, DSX.Transfer>>> GetDepositsAsync(string asset = null)
        {
            return GetTranfersHistoryAsync("Incoming", asset);
        }

        public Task<ApiResult<Dictionary<long, DSX.Transfer>>> GetWithdrawalsAsync(string asset = null)
        {
            return GetTranfersHistoryAsync("Withdraw", asset);
        }

        public Task<ApiResult<DSX.AccountInfo>> GetAccountInfoAsync()
        {
            var requestParams = new Dictionary<string, object>() {};
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, AccountEndpoint, HttpMethod.Post);
            return ExecuteSignedRequestAsync<DSX.AccountInfo>(requestMessage, contentPath: "accountInfo");
        }

        public Task<ApiResult<DSX.NewOrder>> NewOrderAsync(string side, string pair, decimal rate, decimal volume, string orderType)
        {
            var requestParams = new Dictionary<string, object>() { { "type", side }, { "rate", rate }, { "volume", volume }, { "pair", pair }, { "orderType", orderType } };
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, NewOrderEndpoint, HttpMethod.Post);
            return ExecuteSignedRequestAsync<DSX.NewOrder>(requestMessage, contentPath: $"newOrder-{pair}");
        }

        public Task<ApiResult<DSX.Order>> GetOrderAsync(long orderId)
        {
            var requestParams = new Dictionary<string, object>() { { "orderId", orderId } };
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, OrderStatusEndpoint, HttpMethod.Post);
            return ExecuteSignedRequestAsync<DSX.Order>(requestMessage, contentPath: $"order-{orderId}");
        }

        public Task<ApiResult<DSX.NewOrder>> CancelOrderAsync(long orderId)
        {
            var requestParams = new Dictionary<string, object>() { { "orderId", orderId } };
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, CancelOrderEndpoint, HttpMethod.Post);
            return ExecuteSignedRequestAsync<DSX.NewOrder>(requestMessage, contentPath: $"cancelOrder-{orderId}");
        }

        public Task<ApiResult<DSX.NewOrder>> CancelAllOrdersAsync()
        {
            var requestParams = new Dictionary<string, object>();
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, CancelOrderEndpoint, HttpMethod.Post);
            return ExecuteSignedRequestAsync<DSX.NewOrder>(requestMessage, contentPath: $"cancelAllOrders");
        }

        public Task<ApiResult<List<DSX.Order>>> GetOrdersAsync(string pair)
        {
            var requestParams = new Dictionary<string, object>() { { "pair", pair } };
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, OrdersEndpointV2, HttpMethod.Post);
            return ExecuteSignedRequestAsync<Dictionary<long, DSX.Order>, List<DSX.Order>>(requestMessage, ConvertOrders, contentPath: $"orders-{pair}");
        }

        public Task<ApiResult<List<DSX.Order>>> GetOrdersAsync()
        {
            var requestParams = new Dictionary<string, object>() {};
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, OrdersEndpoint, HttpMethod.Post);
            return ExecuteSignedRequestAsync<Dictionary<long, DSX.Order>, List<DSX.Order>>(requestMessage, ConvertOrders, contentPath: $"orders-all");
        }

        public Task<ApiResult<DSX.Fees>> GetCurrentFeesAsync()
        {
            var requestParams = new Dictionary<string, object>();
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, FeesEndpoint, HttpMethod.Post);
            return ExecuteSignedRequestAsync<DSX.Fees>(requestMessage, contentPath: "fees");
        }

        public Task<ApiResult<DSX.TradingVolume>> GetTradingVolumeAsync()
        {
            var requestParams = new Dictionary<string, object>();
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, TradingVolumeEndpoint, HttpMethod.Post);
            return ExecuteSignedRequestAsync<DSX.TradingVolume>(requestMessage, contentPath: "volume");
        }

        public Task<ApiResult<List<DSX.Deal>>> GetTradesHistoryAsync(string pair = null, int? limit = null, long? fromId = null, long? toId = null)
        {
            var requestParams = new Dictionary<string, object>() { { "pair", pair }, { "count", limit }, { "fromId", fromId }, { "endId", toId } };
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, TradesHistoryEndpoint, HttpMethod.Post);
            return ExecuteSignedRequestAsync<Dictionary<long, DSX.Deal>, List<DSX.Deal>>(requestMessage, ConvertTrades, contentPath: "tradesHistory");
        }

        public Task<ApiResult<List<DSX.Order>>> GetOrdersHistoryAsync(int? limit = null, long? fromId = null, long? toId = null)
        {
            var requestParams = new Dictionary<string, object>() { { "count", limit }, { "fromId", fromId }, { "endId", toId } };
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, OrdersHistoryEndpoint, HttpMethod.Post);
            return ExecuteSignedRequestAsync<Dictionary<long, DSX.Order>, List<DSX.Order>>(requestMessage, ConvertOrders, contentPath: "ordersHistory");
        }

        private List<DSX.Order> ConvertOrders(Dictionary<long, DSX.Order> keyValuePairs)
        { // NOTE: not efficient.
            var result = keyValuePairs.ToList();
            result.ForEach(x => x.Value.id = x.Key);
            return result.Select(x => x.Value).ToList();
        }

        private List<DSX.Deal> ConvertTrades(Dictionary<long, DSX.Deal> keyValuePairs)
        { // NOTE: not efficient.
            var result = keyValuePairs.ToList();
            result.ForEach(x => x.Value.id = x.Key);
            return result.Select(x => x.Value).ToList();
        }

        private Task<ApiResult<Dictionary<long, DSX.Transfer>>> GetTranfersHistoryAsync(string type, string asset = null)
        {
            var requestParams = new Dictionary<string, object>() { { "type", type }, { "currency", asset } };
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, TransactionsEndpoint, HttpMethod.Post);
            return ExecuteSignedRequestAsync<Dictionary<long, DSX.Transfer>>(requestMessage, contentPath: $"transfers{type}");
        }
        
        #endregion

        #region Helpers

        protected HttpRequestMessage CreateRequestMessage(SecureString apiKey, SecureString apiSecret, IDictionary<string, object> requestParams, string endpoint, HttpMethod method)
        {
            requestParams.Add("nonce", nonce);
            var uri = requestParams.BuildQuery();
            HttpContent content = requestParams.ToFormUrlEncodedContent();
            var requestMessage = new HttpRequestMessage(method, new Uri(endpoint, UriKind.Relative)) { Content = content };
            requestMessage.Headers.Add("Key", ApiKey.ToManagedString());
            requestMessage.Headers.Add("Sign", SignRequest(uri.TrimStart('?')));
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
                var tmp = JsonConvert.DeserializeObject<T>(content);
                result = new ApiResult<T>(tmp, null, sw.ElapsedMilliseconds);
            }
            else
            {
                var reason = responseMessage.ReasonPhrase;
                if ((int)responseMessage.StatusCode == MAINTENANCE_CODE)
                {
                    reason = "Platform Maintenance";
                }
                Debug.Print($"{content}");
                Log.WarnFormat("{0} {1} - {2}ms : {3} ({4}).", requestMessage.Method, uri, sw.ElapsedMilliseconds, reason, (int)responseMessage.StatusCode);
                result = new ApiResult<T>(default(T), new ApiError((int)responseMessage.StatusCode, reason), sw.ElapsedMilliseconds);
            }

            requestMessage.Dispose();
            responseMessage.Content.Dispose();
            responseMessage.Dispose();

            return result;
        }

        protected async Task<ApiResult<T>> ExecuteSignedRequestAsync<T>(HttpRequestMessage requestMessage, string contentPath = null)
        {
            try
            {
                await slim.WaitAsync().ConfigureAwait(false);
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
                    var tmp = JsonConvert.DeserializeObject<DSX.ApiResult<T>>(content);
                    if (tmp.success)
                        result = new ApiResult<T>(tmp.result, null, sw.ElapsedMilliseconds);
                    else
                        result = new ApiResult<T>(default(T), new ApiError(-1, tmp.error));
                    UpdateWeight(1);
                }
                else
                {
                    Debug.Print($"{content}");
                    Log.WarnFormat("{0} {1} - {2}ms : {3} ({4}).", requestMessage.Method, uri, sw.ElapsedMilliseconds, responseMessage.ReasonPhrase, (int)responseMessage.StatusCode);
                    result = new ApiResult<T>(default(T), new ApiError((int)responseMessage.StatusCode, responseMessage.ReasonPhrase), sw.ElapsedMilliseconds);
                }

                requestMessage.Dispose();
                responseMessage.Content.Dispose();
                responseMessage.Dispose();

                return result;
            }
            finally
            {
                slim.Release();
            }

        }

        protected async Task<ApiResult<TResult>> ExecuteSignedRequestAsync<T, TResult>(HttpRequestMessage requestMessage, Func<T, TResult> conv, string contentPath = null)
        {
            try
            {
                await slim.WaitAsync().ConfigureAwait(false);
                var sw = Stopwatch.StartNew();
                var responseMessage = await httpClient.SendAsync(requestMessage).ConfigureAwait(false);
                var content = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
                sw.Stop();

                var uri = responseMessage.RequestMessage.RequestUri.ToString();
                Debug.Print($"{requestMessage.Method} {uri} : {responseMessage.StatusCode}");

                ApiResult<TResult> result = null;

                if (responseMessage.IsSuccessStatusCode)
                {
                    Log.DebugFormat("{0} {1} - {2}ms : {3} ({4}).", requestMessage.Method, uri, sw.ElapsedMilliseconds, responseMessage.ReasonPhrase, (int)responseMessage.StatusCode);
                    DumpJson(contentPath, content);
                    var tmp = JsonConvert.DeserializeObject<DSX.ApiResult<T>>(content);
                    if (tmp.success)
                        result = new ApiResult<TResult>(conv(tmp.result), null, sw.ElapsedMilliseconds);
                    else
                        result = new ApiResult<TResult>(default(TResult), new ApiError(-1, tmp.error));
                    UpdateWeight(1);
                }
                else
                {
                    Debug.Print($"{content}");
                    Log.WarnFormat("{0} {1} - {2}ms : {3} ({4}).", requestMessage.Method, uri, sw.ElapsedMilliseconds, responseMessage.ReasonPhrase, (int)responseMessage.StatusCode);
                    result = new ApiResult<TResult>(default(TResult), new ApiError((int)responseMessage.StatusCode, responseMessage.ReasonPhrase), sw.ElapsedMilliseconds);
                }

                requestMessage.Dispose();
                responseMessage.Content.Dispose();
                responseMessage.Dispose();

                return result;
            }
            finally
            {
                slim.Release();
            }

        }
        #endregion

        const string restApiUrl = "https://dsx.uk/";
        const int MAINTENANCE_CODE = 418;
        private static HttpClient httpClient = new HttpClient() { BaseAddress = new Uri(restApiUrl) };
    }
}

namespace DSX
{
    public class ExchangeInfo
    {
        public long server_time { get; set; } // UNIX (1522057909)
        public Dictionary<string, Pair> pairs { get; set; }
    }

    public class Pair
    {
        public string pair { get; set; }
        public int decimal_places { get; set; }
        public decimal min_price { get; set; }
        public decimal max_price { get; set; }
        public decimal min_amount { get; set; }
        public bool hidden { get; set; }
        public decimal fee { get; set; }
        public int amount_decimal_places { get; set; }
        public string quoted_currency { get; set; }
        public string base_currency { get; set; }
    }

    public class Trade
    {
        public decimal amount { get; set; }
        public decimal price { get; set; }
        public long timestamp { get; set; }
        public long tid { get; set; }
        public string type { get; set; }     // bid, ask
    }

    public class Ticker
    {
        public string pair { get; set; }
        public decimal high { get; set; }
        public decimal low { get; set; }
        public decimal last { get; set; }
        public decimal buy { get; set; }
        public decimal sell { get; set; }
        public decimal avg { get; set; }
        public decimal vol { get; set; }
        public decimal vol_cur { get; set; }
        public long updated { get; set; }
    }

    public class Bar
    {
        public string pair { get; set; }
        public decimal high { get; set; }
        public decimal open { get; set; }
        public decimal low { get; set; }
        public decimal close { get; set; }
        public decimal amount { get; set; }
        public long timestamp { get; set; }
    }

    public class Transfer
    {
        public long id { get; set; }
        public long timestamp { get; set; }
        public string type { get; set; }    // Withdraw, Incoming
        public decimal amount { get; set; }
        public string currency { get; set; }
        public int confirmationsCount { get; set; }
        public string address { get; set; }
        public int status { get; set; }     // 1 - Failed, 2 - Completed, 3 - Processing, 4 - Rejected
        public decimal comission { get; set; }
    }

    public class Balance
    {
        public decimal total { get; set; }
        public decimal available { get; set; }
    }

    public class Rights
    {
        public int info { get; set; }
        public int trade { get; set; }
    }

    public class AccountInfo
    {
        public Dictionary<string, Balance> funds { get; set; }
        public Rights rights { get; set; }
        public long transactionCount { get; set; }
        public long openOrders { get; set; }
        public long serverTime { get; set; }
    }

    public class Fees
    {
        public ProgressiveComissions progressiveCommissions { get; set; }
    }

    public class ProgressiveComissions
    {
        public List<Comission> commissions { get; set; }
        public int indexOfCurrentCommission { get; set; }
        public string currency { get; set; }
    }

    public class Deal
    {
        public long id { get; set; }
        public string pair { get; set; }
        public string type { get; set; } // buy/sell
        public decimal volume { get; set; }
        public decimal rate { get; set; }
        public long orderId { get; set; }
        public long timestamp { get; set; }
        public decimal commission { get; set; }
        public string commissionCurrency { get; set; }
    }

    public class Comission
    {
        public decimal tradingVolume { get; set; }
        public decimal takerCommission { get; set; }
        public decimal makerCommission { get; set; }
    }

    public class OrderBook
    {
        public List<List<decimal>> asks { get; set; }
        public List<List<decimal>> bids { get; set; }
    }

    public class NewOrder
    {
        public long received { get; set; }
        public long remains { get; set; }
        public Dictionary<string, Balance> funds { get; set; }
        public long orderId { get; set; }
    }

    public class Order
    {
        public long id { get; set; }
        public string pair { get; set; }
        public string type { get; set; } // Buy or Sell
        public decimal remainingVolume { get; set; }
        public decimal volume { get; set; }
        public decimal rate { get; set; }
        public long timestampCreated { get; set; }
        public int status { get; set; } //0 — Active, 1 — Filled, 2 — Killed, 3 — Killing, 7 — Rejected
        public string orderType { get; set; } // The order type: limit, market, or fill-or-kill
        public List<Deal> deals { get; set; }
    }

    public class TradingVolume
    {
        public decimal tradingVolume { get; set; }
        public long tradesCount { get; set; }
        public string currency { get; set; }
    }

    public class ApiResult<T>
    {
        public bool success { get; set; }
        public string error { get; set; }
        [JsonProperty("return")]
        public T result { get; set; }
    }
}