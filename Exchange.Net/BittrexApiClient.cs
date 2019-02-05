using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Exchange.Net
{
    public static class HttpClientExtensions
    {
        public static Uri BuildUri(this HttpClient client, string endpoint, IDictionary<string, object> keyValuePairs)
        {
            var query = keyValuePairs.BuildQuery();
            var path = Path.Combine(client.BaseAddress.AbsoluteUri, endpoint);
            return new Uri(path + query, UriKind.Absolute);
        }
    }

    public class BittrexApiClient : ExchangeApiCore
    {
        // https://support.bittrex.com/hc/en-us/articles/115003723911
        // https://github.com/Bittrex/developers

        // We are currently restricting orders to 500 open orders and 200,000 orders a day.
        const int OpenOrders = 500;
        const int OrdersPerDay = 200000;

        protected override string LogName => "bittrex";

        public BittrexApiClient() : base("bittrex.hash", typeof(HMACSHA512))
        {
        }

        private string SignRequest(string url)
        {
            return ByteArrayToHexString(SignString(url));
        }

        #region Public API

        const string GetMarketsEndpoint = "public/getmarkets";
        const string GetCurrenciesEndpoint = "public/getcurrencies";
        const string GetTickerEndpoint = "public/getticker";
        const string GetMarketSummariesEndpoint = "public/getmarketsummaries";
        const string GetMarketSummaryEndpoint = "public/getmarketsummary";
        const string GetMarketHistoryEndpoint = "public/getmarkethistory";
        const string GetOrderBookEndpoint = "public/getorderbook";

        /// <summary>
        /// Used to get the open and available trading markets at Bittrex along with other meta data.
        /// </summary>
        /// <returns></returns>
        public async Task<ApiResult<IList<Bittrex.Market>>> GetMarketsAsync()
        {
            var requestMessage = CreateRequestMessage(null, GetMarketsEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<IList<Bittrex.Market>>(requestMessage, contentPath: "markets").ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Used to get all supported currencies at Bittrex along with other meta data.
        /// </summary>
        /// <returns></returns>
        public async Task<ApiResult<IList<Bittrex.Currency1>>> GetCurrenciesAsync()
        {
            var requestMessage = CreateRequestMessage(null, GetCurrenciesEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<IList<Bittrex.Currency1>>(requestMessage, contentPath: "currencies").ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Used to get the current tick values for a market.
        /// </summary>
        /// <param name="market">A string literal for the market (ex: BTC-LTC). Required.</param>
        /// <returns></returns>
        public async Task<ApiResult<Bittrex.Ticker>> GetTickerAsync(string market)
        {
            var requestParams = new Dictionary<string, object>() { { "market", market } };
            var requestMessage = CreateRequestMessage(requestParams, GetTickerEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<Bittrex.Ticker>(requestMessage, contentPath: $"ticker-{market}").ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Used to get the last 24 hour summary of all active markets.
        /// </summary>
        /// <returns></returns>
        public async Task<ApiResult<IList<Bittrex.MarketSummary>>> GetMarketSummariesAsync()
        {
            var requestMessage = CreateRequestMessage(null, GetMarketSummariesEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<IList<Bittrex.MarketSummary>>(requestMessage, contentPath: "ticker24hr").ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Used to get the last 24 hour summary of a specific market.
        /// </summary>
        /// <param name="market">A string literal for the market (ex: BTC-LTC). Required.</param>
        /// <returns></returns>
        public async Task<ApiResult<IList<Bittrex.MarketSummary>>> GetMarketSummaryAsync(string market)
        {
            var requestParams = new Dictionary<string, object>() { { "market", market } };
            var requestMessage = CreateRequestMessage(requestParams, GetMarketSummaryEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<IList<Bittrex.MarketSummary>>(requestMessage, contentPath: $"ticker24hr-{market}").ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Used to retrieve the latest trades that have occured for a specific market.
        /// </summary>
        /// <param name="market">A string literal for the market (ex: BTC-LTC). Required.</param>
        /// <returns></returns>
        public async Task<ApiResult<IList<Bittrex.Trade>>> GetMarketHistoryAsync(string market)
        {
            var requestParams = new Dictionary<string, object>() { { "market", market } };
            var requestMessage = CreateRequestMessage(requestParams, GetMarketHistoryEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<IList<Bittrex.Trade>>(requestMessage, contentPath: $"trades-{market}").ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Used to get retrieve the orderbook for a given market.
        /// </summary>
        /// <param name="market">A string literal for the market (ex: BTC-LTC). Required.</param>
        /// <param name="type">buy, sell or both to identify the type of orderbook to return. Required.</param>
        /// <returns></returns>
        public async Task<ApiResult<Bittrex.OrderBook>> GetOrderBookAsync(string market, string type = "both")
        {
            var requestParams = new Dictionary<string, object>() { { "market", market }, { "type", type } };
            var requestMessage = CreateRequestMessage(requestParams, GetOrderBookEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<Bittrex.OrderBook>(requestMessage, contentPath: $"depth-{market}").ConfigureAwait(false);
            return result;
        }

        public IObservable<ApiResult<IList<Bittrex.MarketSummary>>> ObserveMarketSummaries()
        {
            var obs = Observable.FromAsync(GetMarketSummariesAsync);
            return Observable.Interval(TimeSpan.FromSeconds(2)).SelectMany(x => obs);
        }

        #endregion

        #region Market API

        const string BuyLimitEndpoint = "market/buylimit";
        const string SellLimitEndpoint = "market/selllimit";
        const string CancelEndpoint = "market/cancel";
        const string GetOpenOrdersEndpoint = "market/getopenorders";

        /// <summary>
        /// Get all orders that you currently have opened. A specific market can be requested.
        /// </summary>
        /// <param name="market">A string literal for the market (ie. BTC-LTC). Optional.</param>
        /// <returns></returns>
        public async Task<ApiResult<IList<Bittrex.Order>>> GetOpenOrders(string market = null)
        {
            var requestParams = new Dictionary<string, object>() { { "market", market} };
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, GetOpenOrdersEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<IList<Bittrex.Order>>(requestMessage, contentPath: "openOrders").ConfigureAwait(false);
            return result;
        }

        #endregion

        #region Account API

        const string GetDepositHistoryEndpoint = "account/getdeposithistory";
        const string GetWithdrawalHistoryEndpoint = "account/getwithdrawalhistory";
        const string GetBalancesEndpoint = "account/getbalances";

        /// <summary>
        /// Used to retrieve your deposit history.
        /// </summary>
        /// <param name="currency">A string literal for the currecy (ie. BTC). If omitted, will return for all currencies. Optional.</param>
        /// <returns></returns>
        public async Task<ApiResult<IList<Bittrex.Transfer>>> GetDepositHistoryAsync(string currency = null)
        {
            var requestParams = new Dictionary<string, object>() { { "currency", currency } };
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, GetDepositHistoryEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<IList<Bittrex.Transfer>>(requestMessage, contentPath: "deposits").ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Used to retrieve your withdrawal history.
        /// </summary>
        /// <param name="currency">A string literal for the currecy (ie. BTC). If omitted, will return for all currencies. Optional.</param>
        /// <returns></returns>
        public async Task<ApiResult<IList<Bittrex.Transfer>>> GetWithdrawalHistoryAsync(string currency = null)
        {
            var requestParams = new Dictionary<string, object>() { { "currency", currency } };
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, requestParams, GetWithdrawalHistoryEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<IList<Bittrex.Transfer>>(requestMessage, contentPath: "withdrawals").ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Used to retrieve all balances from your account.
        /// </summary>
        /// <returns></returns>
        public async Task<ApiResult<IList<Bittrex.Balance1>>> GetBalancesAsync()
        {
            var requestMessage = CreateRequestMessage(ApiKey, ApiSecret, null, GetBalancesEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<IList<Bittrex.Balance1>>(requestMessage, contentPath: "balances").ConfigureAwait(false);
            return result;

            //var endpoint = $"/account/getbalances?apikey={ApiKey.ToManagedString()}&nonce={nonce}";
            //var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            //var uri = client.BuildUri(request);
            //var apiSign = SignRequest(uri.AbsoluteUri);
            //request.AddHeader("apisign", apiSign);
            //var response = await client.ExecuteTaskAsync<Bittrex.ApiResult<List<Bittrex.Balance1>>>(request);
            //if (response.IsSuccessful)
            //{
            //    var apiResult = response.Data;
            //    if (apiResult.success)
            //    {
            //        return apiResult.result;
            //    }
            //    else
            //    {
            //        throw new Exception(apiResult.message);
            //    }
            //}
            //else
            //{
            //    throw new Exception(response.ErrorMessage);
            //}
        }
        
        #endregion

        #region Helpers

        protected HttpRequestMessage CreateRequestMessage(SecureString apiKey, SecureString apiSecret, IDictionary<string, object> requestParams, string endpoint, HttpMethod method)
        {
            if (requestParams == null)
                requestParams = new Dictionary<string, object>();
            requestParams.Add("apikey", apiKey.ToManagedString());
            requestParams.Add("nonce", nonce);
            var uri = httpClient.BuildUri(endpoint, requestParams);
            HttpContent content = null;
            if (method == HttpMethod.Get)
                endpoint = endpoint + requestParams.BuildQuery();
            else
                content = new StringContent(requestParams.BuildQuery());
            var requestMessage = new HttpRequestMessage(method, endpoint) { Content = content };
            requestMessage.Headers.Add("apisign", ByteArrayToHexString(SignString(uri.AbsoluteUri)));
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
                var tmp = JsonConvert.DeserializeObject<Bittrex.ApiResult<T>>(content);
                if (tmp.success)
                    result = new ApiResult<T>(tmp.result, null, sw.ElapsedMilliseconds);
                else
                    result = new ApiResult<T>(tmp.result, new ApiError(-1, tmp.message), sw.ElapsedMilliseconds);
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

        const string restApiUrl = "https://bittrex.com/api/v1.1/";
        private static HttpClient httpClient = new HttpClient() { BaseAddress = new Uri(restApiUrl) };
    }
}

namespace Bittrex
{
    public class Trade
    {
        public long Id { get; set; }
        public DateTime TimeStamp { get; set; } // 2014-07-09T03:21:20.08
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Total { get; set; }
        public string FillType { get; set; }    // FILL, PARTIAL_FILL
        public string OrderType { get; set; }   // BUY, SELL
    }

    public class Market
    {
        public string MarketCurrency { get; set; }
        public string BaseCurrency { get; set; }
        public string MarketCurrencyLong { get; set; }
        public string BaseCurrencyLong { get; set; }
        public decimal MinTradeSize { get; set; }
        public string MarketName { get; set; }
        public bool IsActive { get; set; }
        public DateTime Created { get; set; }
    }

    public class Currency1
    {
        public string Currency { get; set; }        // BTC
        public string CurrencyLong { get; set; }    // Bitcoin
        public int MinConfirmation { get; set; }    // 2
        public decimal TxFee { get; set; }          // 0.00020000
        public bool IsActive { get; set; }          // true
        public string CoinType { get; set; }        // BITCOIN
        public string BaseAddress { get; set; }
    }

    public class Ticker
    {
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public decimal Last { get; set; }
    }

    public class MarketSummary
    {
        public string MarketName { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Volume { get; set; }
        public decimal Last { get; set; }
        public decimal BaseVolume { get; set; }
        public DateTime TimeStamp { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public int OpenBuyOrders { get; set; }
        public int OpenSellOrders { get; set; }
        public decimal PrevDay { get; set; }
        public DateTime Created { get; set; }
        public string DisplayMarketName { get; set; }
    }

    public class Transfer
    {
        public string PaymentUuid { get; set; }
        public string Currency { get; set; }
        public decimal Amount { get; set; }
        public string Address { get; set; }
        public DateTime LastUpdated { get; set; }
        public DateTime Opened { get; set; }
        public bool Authorized { get; set; }
        public bool PendingPayment { get; set; }
        public decimal TxCost { get; set; }
        public string TxId { get; set; }
        public bool Canceled { get; set; }
        public bool InvalidAddress { get; set; }
    }

    public class OrderBookEntry
    {
        public decimal Quantity { get; set; }
        public decimal Rate { get; set; }
    }

    public class OrderBook
    {
        public IList<OrderBookEntry> buy { get; set; }
        public IList<OrderBookEntry> sell { get; set; }
    }

    public class Balance1
    {
        public string Currency { get; set; }
        public decimal Balance { get; set; }
        public decimal Available { get; set; }
        public decimal Pending { get; set; }
        public string CryptoAddress { get; set; }
        public bool Requested { get; set; }
        public string Uuid { get; set; }
    }

    public class Order
    {
        public string Uuid { get; set; }
        public string OrderUuid { get; set; }
        public string Exchange { get; set; }   // BTC-LTC
        public string OrderType { get; set; }  // LIMIT_SELL
        public decimal Quantity { get; set; }
        public decimal QuantityRemaining { get; set; }
        public decimal Limit { get; set; }
        public decimal CommissionPaid { get; set; }
        public decimal Price { get; set; }
        public decimal? PricePerUnit { get; set; }
        public DateTime Opened { get; set; }
        public DateTime? Closed { get; set; }
        public bool CancelInitiated { get; set; }
        public bool ImmediateOrCancel { get; set; }
        public bool IsConditional { get; set; }
        public string Condition { get; set; }
        public string ConditionTarget { get; set; }
    }

    public class ApiResult<TResult>
    {
        public bool success { get; set; }
        public string message { get; set; }
        public TResult result { get; set; }
    }

}