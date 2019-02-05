using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace Exchange.Net
{
    public class OKexApiClient : ExchangeApiCore
    {
        protected override string LogName => "okex";

        #region Public API
        const string InstrumentsEndpoint = "spot/v3/instruments";
        const string TickerEndpoint = "spot/v3/instruments/ticker";
        const string DepthEndpoint = "spot/v3/instruments/<instrument_id>/book";
        const string TradesEndpoint = "spot/v3/instruments/<instrument_id>/trades";

        public async Task<ApiResult<List<OKex.Instrument>>> GetInstrumentsAsync()
        {
            var requestMessage = CreateRequestMessage(null, InstrumentsEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<List<OKex.Instrument>>(requestMessage, contentPath: "markets").ConfigureAwait(false);
            return result;
        }

        public async Task<ApiResult<List<OKex.Ticker>>> GetTickersAsync()
        {
            var requestMessage = CreateRequestMessage(null, TickerEndpoint, HttpMethod.Get);
            var result = await ExecuteRequestAsync<List<OKex.Ticker>>(requestMessage, contentPath: "tickers").ConfigureAwait(false);
            return result;
        }

        public async Task<ApiResult<List<OKex.Trade>>> GetTradesAsync(string instrument)
        {
            var requestMessage = CreateRequestMessage(null, TradesEndpoint.Replace("<instrument_id>", instrument), HttpMethod.Get);
            var result = await ExecuteRequestAsync<List<OKex.Trade>>(requestMessage, contentPath: $"trades-{instrument}").ConfigureAwait(false);
            return result;
        }

        public async Task<ApiResult<OKex.OrderBook>> GetDepthAsync(string instrument)
        {
            var requestMessage = CreateRequestMessage(null, DepthEndpoint.Replace("<instrument_id>", instrument), HttpMethod.Get);
            var result = await ExecuteRequestAsync<OKex.OrderBook>(requestMessage, contentPath: $"depth-{instrument}").ConfigureAwait(false);
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
                if (true)
                    result = new ApiResult<T>(tmp, null, sw.ElapsedMilliseconds);
                else
                    result = new ApiResult<T>(default(T), new ApiError(-1, "TODO"), sw.ElapsedMilliseconds);
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

        const string restApiUrl = "https://www.okex.com/api/";
        private static HttpClient httpClient = new HttpClient() { BaseAddress = new Uri(restApiUrl) };
    }
}

namespace OKex
{
    public class Instrument
    {
        public string base_currency { get; set; }
        public decimal base_increment { get; set; }
        public decimal base_min_size { get; set; }
        public string instrument_id { get; set; }
        public decimal min_size { get; set; }
        public string product_id { get; set; }
        public string quote_currency { get; set; }
        public decimal quote_increment { get; set; }
        public decimal size_increment { get; set; }
        public decimal tick_size { get; set; }
    }

    public class Ticker
    {
        public decimal best_ask { get; set; }
        public decimal best_bid { get; set; }
        public string instrument_id { get; set; }
        public string product_id { get; set; }
        public decimal last { get; set; }
        public decimal ask { get; set; }
        public decimal bid { get; set; }
        public decimal open_24h { get; set; }
        public decimal high_24h { get; set; }
        public decimal low_24h { get; set; }
        public decimal base_volume_24h { get; set; }
        public DateTime timestamp { get; set; }
        public decimal quote_volume_24h { get; set; }
    }

    public class Trade
    {
        public DateTime timestamp { get; set; }
        public long trade_id { get; set; }
        public decimal price { get; set; }
        public decimal size { get; set; }
        public string side { get; set; }
    }

    public class OrderBook
    {
        public DateTime timestamp { get; set; }
        public List<decimal[]> bids { get; set; }
        public List<decimal[]> asks { get; set; }
    }
}