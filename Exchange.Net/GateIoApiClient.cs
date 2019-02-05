using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Exchange.Net
{
    public class GateIoApiClient : ExchangeApiCore
    {

        protected override string LogName => "gateio";

        public GateIoApiClient() : base("gateio.hash", typeof(HMACSHA512))
        {
        }

        #region Public API
        const string GetMarketInfoEndpoint = "marketinfo";
        const string GetCoinInfoEndpoint = "coininfo";
        const string GetMarketListEndpoint = "marketlist";
        const string GetTickersEndpoint = "tickers";
        const string GetTradeHistoryEndpoint = "tradeHistory";
        const string GetOrderBookEndpoint = "orderBook";

        public async Task<ApiResult<GateIo.MarketInfo>> GetMarketInfoAsync()
        {
            var requestMessage = CreateRequestMessage(null, GetMarketInfoEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<GateIo.MarketInfo> (requestMessage, contentPath: "marketinfo").ConfigureAwait(false);
            return result;
        }

        public async Task<ApiResult<GateIo.MarketDetails>> GetMarketListAsync()
        {
            var requestMessage = CreateRequestMessage(null, GetMarketListEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<GateIo.MarketDetails>(requestMessage, contentPath: "marketlist").ConfigureAwait(false);
            return result;
        }

        public async Task<ApiResult<Dictionary<string, GateIo.Ticker>>> GetTickersAsync()
        {
            var requestMessage = CreateRequestMessage(null, GetTickersEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<Dictionary<string, GateIo.Ticker>>(requestMessage, contentPath: "tickers").ConfigureAwait(false);
            return result;
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
                var tmp = JsonConvert.DeserializeObject<T>(content);
                if (typeof(T).BaseType == typeof(GateIo.RestResultBase))
                {
                    var restResult = tmp as GateIo.RestResultBase;
                    if (restResult.result)
                        result = new ApiResult<T>(tmp, null, sw.ElapsedMilliseconds);
                    else
                        result = new ApiResult<T>(tmp, new ApiError(restResult.code, restResult.message), sw.ElapsedMilliseconds);
                }
                else
                {
                    var restResult = JsonConvert.DeserializeObject<GateIo.RestResultBase>(content);
                    if (restResult.result)
                        result = new ApiResult<T>(tmp, null, sw.ElapsedMilliseconds);
                    else
                        result = new ApiResult<T>(tmp, new ApiError(restResult.code, restResult.message), sw.ElapsedMilliseconds);
                }
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

        const string restApiUrl = "https://data.gateio.io/api2/1/";
        private static HttpClient httpClient = new HttpClient() { BaseAddress = new Uri(restApiUrl) };
    }
}

namespace GateIo
{
    public class RestResultBase
    {
        public bool result { get; set; } = true;
        public int code { get; set; }
        public string message { get; set; }
    }

    public class Pair
    {
        public int decimal_places { get; set; }
        public decimal min_amount { get; set; }
        public decimal min_amount_a { get; set; }
        public decimal min_amount_b { get; set; }
        public decimal fee { get; set; }
        public int trade_disabled { get; set; }
    }

    public class MarketInfo : RestResultBase
    {
        public List<KeyValuePair<string, Pair>> pairs { get; set; }
    }

    public class CoinInfo
    {
        public int delisted { get; set; }
        public int withdraw_disabled { get; set; }
        public int withdraw_delayed { get; set; }
        public int deposit_disabled { get; set; }
        public int trade_disabled { get; set; }
    }

    public class CoinsInfo : RestResultBase
    {
        public Dictionary<string, CoinInfo> pairs { get; set; }
    }

    public class Market
    {
        public int no { get; set; }
        public string symbol { get; set; }  // "LTC"
        public string name { get; set; }    // "Litecoin"
        public string pair { get; set; }    // "ltc_btc"
        public decimal rate { get; set; }
        public decimal vol_a { get; set; }
        public decimal vol_b { get; set; }
        public string curr_a { get; set; } // "LTC"
        public string curr_b { get; set; } // "BTC"
        public string curr_suffix { get; set; } // "BTC"
        public decimal rate_percent { get; set; }
        public string trend { get; set; }   // "up"
    }

    public class MarketDetails : RestResultBase
    {
        public List<Market> data { get; set; }
    }

    public class Ticker : RestResultBase
    {
        public decimal last { get; set; }
        public decimal lowestAsk { get; set; }
        public decimal highestBid { get; set; }
        public decimal percentChange { get; set; }
        public decimal baseVolume { get; set; }
        public decimal quoteVolume { get; set; }
        public decimal high24hr { get; set; }
        public decimal low24hr { get; set; }
    }

    public class Depth : RestResultBase
    {
        public List<decimal[]> asks { get; set; }
        public List<decimal[]> bids { get; set; }
    }

    public class Trade
    {
        public long tradeID { get; set; }
        public string date { get; set; } // "2017-09-29 11:52:05"
        public long timestamp { get; set; }
        public string type { get; set; } // buy/sell
        public decimal rate { get; set; }
        public decimal amount { get; set; }
        public decimal total { get; set; }
    }

    public class TradeHistory : RestResultBase
    {
        public List<Trade> data { get; set; }
    }
}