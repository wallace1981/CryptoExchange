using Newtonsoft.Json;
using RestSharp;
using RestSharp.Deserializers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocket4Net;

namespace Exchange.Net
{
    public class BinanceApiClient : ExchangeApiCore
    {
        public delegate void SymbolTickerHandler(object sender, Binance.WsPriceTicker24hr tick);
		public delegate void PublicTradeHandler(object sender, Binance.WsTrade trade);

        public event SymbolTickerHandler DetailTicker;
		public event PublicTradeHandler Trade;

        public int Weight => weight;
        public TimeSpan WeightReset => weightDueTime - DateTime.Now;

        protected override string LogName => "binance";

        public BinanceApiClient()
        {
            if (LoadApiKeys("binance.hash"))
            {
                this.Encryptor = new HMACSHA256(ApiSecret.ToByteArray());
            }
        }

        #region  Public API

        private const string GetServerTimeEndpoint = "/api/v1/time";
        private const int GetServerTimeWeight = 1;
        private const string GetExchangeInfoEndpoint = "/api/v1/exchangeInfo";
        private const int GetExchangeInfoWeight = 1;
        private const string GetPriceTickerEndpoint = "/api/v3/ticker/price";
        private const int GetPriceTickerWeight = 1;
        private const string Get24hrPriceTickerEndpoint = "/api/v1/ticker/24hr";
        private const int Get24hrPriceTickerWeight = 40;
        private const string GetRecentTradesEndpoint = "/api/v1/trades";
        private const int GetRecentTradesWeight = 1;
        private const string GetDepthEndpoint = "/api/v1/depth";
        private const int GetDepthWeight = 100;


        public async Task<DateTime> GetServerTime()
        {
            var request = new RestSharp.RestRequest(GetServerTimeEndpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Binance.ServerTime>(request).ConfigureAwait(false);
            if (response.IsSuccessful)
            {
                UpdateWeight(1);
                var result = response.Data;
                return result.serverTime.FromUnixTimestamp(false); // NOTE : this is UTC
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public ApiResult<Binance.ServerTime> GetServerTime2()
        {
            var request = new RestSharp.RestRequest(GetServerTimeEndpoint, RestSharp.Method.GET);
            var result = ExecutePublicRequest<Binance.ServerTime>(request, GetServerTimeWeight);
            return result;
        }

        public ApiResult<Binance.ExchangeInfo> GetExchangeInfo2()
        {
            var request = new RestSharp.RestRequest(GetExchangeInfoEndpoint, RestSharp.Method.GET);
            var result = ExecutePublicRequest<Binance.ExchangeInfo>(request, GetExchangeInfoWeight, contentPath: "exchangeInfo.json");
            return result;
        }

        public ApiResult<Binance.PriceTicker24hr[]> Get24hrPriceTicker2()
        {
            var request = new RestSharp.RestRequest(Get24hrPriceTickerEndpoint, RestSharp.Method.GET);
            var result = ExecutePublicRequest<Binance.PriceTicker24hr[]>(request, Get24hrPriceTickerWeight, contentPath: "ticker24hr.json");
            return result;
        }

        public ApiResult<Binance.Trade[]> GetRecentTrades2(string symbol, int limit = 500)
        {
            var request = new RestSharp.RestRequest(GetRecentTradesEndpoint, RestSharp.Method.GET);
            request.AddParameter("symbol", symbol);
            request.AddParameter("limit", limit);
            var result = ExecutePublicRequest<Binance.Trade[]>(request, GetRecentTradesWeight, contentPath: $"trades-{symbol}.json");
            return result;
        }

        public ApiResult<Binance.Depth> GetDepth2(string symbol, int limit = 100)
        {
            var request = new RestSharp.RestRequest(GetDepthEndpoint, RestSharp.Method.GET);
            request.AddParameter("symbol", symbol);
            request.AddParameter("limit", limit);
            var result = ExecutePublicRequest<Binance.Depth>(request, limit < GetDepthWeight ? 1 : limit / GetDepthWeight, contentPath: $"depth-{symbol}.json");
            return result;
        }




        public ApiResult<DateTime> GetServerTime3()
        {
            var sw = Stopwatch.StartNew();
            var response = publicClient.GetAsync("/api/v1/time").Result;
            response.EnsureSuccessStatusCode();
            var returnedData = response.Content.ReadAsStringAsync().Result;
            var result = JsonConvert.DeserializeObject<Binance.ServerTime>(returnedData);
            sw.Stop();
            Log.DebugFormat("{0} {1} finished ({2}ms).", response.RequestMessage.Method, response.RequestMessage.RequestUri, sw.ElapsedMilliseconds);
            return new ApiResult<DateTime>(result.serverTime.FromUnixTimestamp(false), null, sw.ElapsedMilliseconds);
        }

        public ApiResult<T> ExecutePublicRequest<T>(RestSharp.IRestRequest request, int weight, string contentPath = null)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var response = client.Execute(request);
                sw.Stop();
                if (response.IsSuccessful)
                {
                    UpdateWeight(weight);
                    Log.DebugFormat("{0} {1} - {2}ms : {3} ({4}).", request.Method.ToString(), client.BuildUri(request), sw.ElapsedMilliseconds, response.StatusDescription, (int)response.StatusCode);
#if DEBUG
                    if (contentPath != null)
                        File.WriteAllText(contentPath, response.Content);
#endif
                    var data = JsonConvert.DeserializeObject<T>(response.Content.Replace(",[]", string.Empty));
                    return new ApiResult<T>(data, null, sw.ElapsedMilliseconds);
                }
                else
                {
                    if (response.StatusCode >= HttpStatusCode.InternalServerError ||
                        response.StatusCode == HttpStatusCode.BadRequest ||
                        response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        // Do not retry for public API?
                        var error = client.Deserialize<ApiError>(response).Data;
                        Log.WarnFormat("{0} {1} - {2}ms : {3} ({4}).", request.Method.ToString(), client.BuildUri(request), sw.ElapsedMilliseconds, response.StatusDescription, (int)response.StatusCode);
                        return new ApiResult<T>(default(T), error, sw.ElapsedMilliseconds);
                    }
                    else
                    {
                        Log.WarnFormat("{0} {1} - {2}ms : {3} ({4}).", request.Method.ToString(), client.BuildUri(request), sw.ElapsedMilliseconds, response.StatusDescription, (int)response.StatusCode);
                        return new ApiResult<T>(default(T), new ApiError((int)response.StatusCode, response.StatusDescription), sw.ElapsedMilliseconds);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("{0} {1} failed.", request.Method.ToString(), client.BuildUri(request)), ex);
                return new ApiResult<T>(default(T), new ApiError(-1, ex.Message));
            }
            finally
            {

            }
        }


        public async Task<Binance.ExchangeInfo> GetExchangeInfoAsync()
        {
            const string endpoint = "/api/v1/exchangeInfo";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Binance.ExchangeInfo>(request).ConfigureAwait(false);
#if DEBUG
			System.IO.File.WriteAllText("exchangeInfo.json", response.Content);
#endif
			if (response.IsSuccessful)
            {
                UpdateWeight(1);
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<List<Binance.Trade>> GetRecentTradesAsync(string symbol, int limit)
        {
            var endpoint = $"/api/v1/trades?symbol={symbol}&limit={limit}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<List<Binance.Trade>>(request).ConfigureAwait(false);
#if DEBUG
			System.IO.File.WriteAllText("trades.json", response.Content);
#endif
            if (response.IsSuccessful)
            {
                UpdateWeight(1);
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<List<Binance.AggTrade>> GetAggTradesAsync(string symbol, int limit)
        {
            var endpoint = $"/api/v1/aggTrades?symbol={symbol}&limit={limit}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<List<Binance.AggTrade>>(request).ConfigureAwait(false);
#if DEBUG
            System.IO.File.WriteAllText($"aggTrades-{symbol}.json", response.Content);
#endif
            if (response.IsSuccessful)
            {
                UpdateWeight(1);
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        /// <summary>
        /// Get recent trades (up to last 500)
        /// </summary>
        /// <returns>The recent trades.</returns>
        /// <param name="symbol">Symbol.</param>
        /// <param name="limit">Default 500; max 1000. Valid limits:[5, 10, 20, 50, 100, 500, 1000]</param>
		public List<Binance.Trade> GetRecentTrades(string symbol, int limit)
        {
			var endpoint = $"/api/v1/trades?symbol={symbol}&limit={limit}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = client.Execute<List<Binance.Trade>>(request);
#if DEBUG
            System.IO.File.WriteAllText($"trades-{symbol}.json", response.Content);
#endif
            if (response.IsSuccessful)
            {
                UpdateWeight(1);
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        /// <summary>
        /// Gets the Order book.
        /// </summary>
        /// <returns>Order book.</returns>
        /// <param name="symbol">Symbol.</param>
        /// <param name="limit">Default 100; max 1000. Valid limits:[5, 10, 20, 50, 100, 500, 1000]. Caution: setting limit=0 can return a lot of data.</param>
        public Binance.Depth GetDepth(string symbol, int limit = 100)
        {
            var endpoint = $"/api/v1/depth?symbol={symbol}&limit={limit}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = client.Execute<Binance.Depth>(request);
#if DEBUG
            System.IO.File.WriteAllText($"depth-{symbol}.json", response.Content);
#endif
            if (response.IsSuccessful)
            {
                UpdateWeight(limit < 100 ? 1 : limit / 100);
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }


        public async Task<List<Binance.PriceTicker>> GetPriceTickerAsync(string symbol = null)
        {
            const string endpoint = "/api/v3/ticker/price";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            if (!string.IsNullOrWhiteSpace(symbol))
            {
                request.AddQueryParameter("symbol", symbol);
            }
            var response = await client.ExecuteTaskAsync<List<Binance.PriceTicker>>(request).ConfigureAwait(false);
#if DEBUG
            System.IO.File.WriteAllText("tickerPrice.json", response.Content);
#endif
            if (response.IsSuccessful)
            {
                UpdateWeight(40);
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<List<Binance.PriceTicker24hr>> Get24hrPriceTickerAsync(string symbol = null)
        {
            const string endpoint = "/api/v1/ticker/24hr";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            if (!string.IsNullOrWhiteSpace(symbol))
            {
                request.AddQueryParameter("symbol", symbol);
            }
            var response = await client.ExecuteTaskAsync<List<Binance.PriceTicker24hr>>(request).ConfigureAwait(false);
#if DEBUG
            System.IO.File.WriteAllText("ticker24h.json", response.Content);
#endif
            if (response.IsSuccessful)
            {
                UpdateWeight(symbol != null ? 1 : 40);
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        #endregion

        #region Signed API

        public async Task<Binance.AccountInfo> GetAccountInfoAsync()
        {
            const string endpoint = "/api/v3/account";
        
            var serverTime = await GetServerTime();
            var offset = serverTime - DateTime.UtcNow;
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var result = await RequestSignedApiAsync<Binance.AccountInfo>(request, offset);
            UpdateWeight(5);
            return result;
        }

        public async Task<Binance.DepositHistory> GetDepositHistoryAsync(string asset = null)
        {
            const string endpoint = "/wapi/v3/depositHistory.html";

            var serverTime = await GetServerTime();
            var offset = serverTime - DateTime.UtcNow;
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            if (asset != null)
            {
                request.AddQueryParameter("asset", asset);
            }
            var result = await RequestSignedApiAsync<Binance.DepositHistory>(request, offset);
            UpdateWeight(1);
            return result;
        }

        public async Task<Binance.WithdrawtHistory> GetWithdrawHistoryAsync(string asset = null)
        {
            const string endpoint = "/wapi/v3/withdrawHistory.html";

            var serverTime = await GetServerTime();
            var offset = serverTime - DateTime.UtcNow;
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            if (asset != null)
            {
                request.AddQueryParameter("asset", asset);
            }
            var result = await RequestSignedApiAsync<Binance.WithdrawtHistory>(request, offset);
            UpdateWeight(1);
            return result;
        }

        public async Task<List<Binance.Order>> GetOpenOrdersAsync(string symbol = null)
        {
            const string endpoint = "/api/v3/openOrders";

            var serverTime = await GetServerTime();
            var offset = serverTime - DateTime.UtcNow;
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            if (symbol != null)
            {
                request.AddParameter("symbol", symbol);
            }
            var result = await RequestSignedApiAsync<Binance.Order[]>(request, offset);
            UpdateWeight(symbol != null ? 1 : 40);
            return result.ToList();
        }

        public async Task<Binance.Order[]> GetOpenOrdersAsync2(string symbol = null)
        {
            const string endpoint = "/api/v3/openOrders";

            var serverTime = await GetServerTime();
            var offset = serverTime - DateTime.UtcNow;
            var parameters = new Dictionary<string, string>();
            if (symbol != null)
            {
                parameters.Add("symbol", symbol);
            }
            var result = await ExecuteRequest<Binance.Order[]>(endpoint, HttpMethod.Get, offset, true, 0, parameters);
            if (result.Success)
            {
                return result.Data;
            }
            throw new Exception("Fuck you!");
        }

        private async Task<T> RequestSignedApiAsync<T>(RestSharp.IRestRequest request, TimeSpan offset, int tries = 0)
        {
            try
            {
                //await slim.WaitAsync().ConfigureAwait(true);
                request.AddParameter("timestamp", DateTime.UtcNow.Add(offset).ToUnixTimestamp());
                var uri = client.BuildUri(request);
                var signature = ByteArrayToHexString(SignString(uri.Query.Replace("?", string.Empty)));
                request.AddParameter("signature", signature);
                request.AddHeader("X-MBX-APIKEY", ApiKey.ToManagedString());
                var response = await client.ExecuteTaskAsync<T>(request).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return JsonConvert.DeserializeObject<T>(response.Content);
                    //return response.Data;
                }
                else if (response.StatusCode >= System.Net.HttpStatusCode.InternalServerError)
                {
                    // retry...
                    return await RequestSignedApiAsync<T>(request, offset, tries + 1);
                }
                else if (response.StatusCode >= System.Net.HttpStatusCode.BadRequest)
                {
                    var error = client.Deserialize<ApiError>(response).Data;
                    if (error.Code == -1021 && tries < 5)
                    {
                        // retry...
                        return await RequestSignedApiAsync<T>(request, offset, tries + 1);
                    }
                    else
                        throw new Exception($"Error {error.Code}: {error.Msg}.");
                }
                else
                {
                    throw new Exception(response.ErrorMessage);
                }
            }
            finally
            {
                //slim.Release();
            }
        }
        #endregion

        #region WebSocket API

        /// <summary>
        /// 24hr Ticker statistics for all symbols that changed in an array pushed every second.
        /// </summary>
        /// <param name="symbols">Symbols.</param>
        public IObservable<Binance.WsPriceTicker24hr> SubscribeMarketSummariesAsync(IEnumerable<string> symbols)
        {
            // A single connection to stream.binance.com is only valid for 24 hours; expect to be disconnected at the 24 hour mark
            const string url = "wss://stream.binance.com:9443";
            const string req2 = "/ws/!ticker@arr";
            //const string req = "/stream?streams=";

            //var uri = url + req + string.Join("/", symbols.Select(s => s.ToLower() + "@ticker"));
            var uri2 = url + req2;
            var ws = new WebSocket(uri2);
            //ws.Security.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            ws.Error += Ws_Error;
            var obs = Observable.FromEventPattern<EventHandler<MessageReceivedEventArgs>, object, MessageReceivedEventArgs>(h => ws.MessageReceived += h, h => ws.MessageReceived -= h);
			//ws.MessageReceived += Ws_OnSocketMessage;
            ws.Opened += (sender, e) =>
            {
            };
            ws.Open();
            return obs.SelectMany(OnTickerSocketMessage2);
            //return obs.Select(OnTickerSocketMessage);
        }


        public IObservable<Binance.WsCandlestick> SubscribeKlinesAsync(IEnumerable<string> symbols, string interval)
        {
            // A single connection to stream.binance.com is only valid for 24 hours; expect to be disconnected at the 24 hour mark
            const string url = "wss://stream.binance.com:9443";
            const string req = "/stream?streams=";

            var uri = url + req + string.Join("/", symbols.Select(s => s.ToLower() + "@kline_" + interval));
            var ws = new WebSocket(uri);
            ws.Error += Ws_Error;
            var obs = Observable.FromEventPattern<EventHandler<MessageReceivedEventArgs>, object, MessageReceivedEventArgs>(h => ws.MessageReceived += h, h => ws.MessageReceived -= h);
            ws.Open();
            return obs.Select((EventPattern<object, MessageReceivedEventArgs> arg) => 
            {
                var result = JsonConvert.DeserializeObject<Binance.WsResponse<Binance.WsCandlestick>>(arg.EventArgs.Message);
                return result.data;
            });
        }

        /// <summary>
        /// The Trade Streams push raw trade information; each trade has a unique buyer and seller
        /// </summary>
        /// <param name="symbol">Symbol.</param>
        public IObservable<Binance.WsTrade> SubscribePublicTradesAsync(string symbol, int limit)
        {
            // All symbols for streams are lowercase
            // A single connection to stream.binance.com is only valid for 24 hours; expect to be disconnected at the 24 hour mark
            const string url = "wss://stream.binance.com:9443/ws";
			const string req = "/{0}@trade";

            var ws = new WebSocket(url + string.Format(req, symbol.ToLower()));
            //ws.Security.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            ws.Error += Ws_Error;
            var obs = Observable.FromEventPattern<EventHandler<MessageReceivedEventArgs>, object, MessageReceivedEventArgs>(h => ws.MessageReceived += h, h => ws.MessageReceived -= h);
			//ws.MessageReceived += Ws_OnTradesSocketMessage;
            ws.Opened += (object sender, EventArgs e) =>
            {
            };
            ws.Closed += (object sender, EventArgs e) => 
            {
            };
            ws.Open();

            return obs.Select(OnTradeSocketMessage);
        }

        /// <summary>
        /// Order book price and quantity depth updates used to locally manage an order book pushed every second.
        /// </summary>
        /// <param name="symbol">Symbol.</param>
        public IObservable<Binance.WsDepth> SubscribeOrderBook(string symbol)
        {
            // All symbols for streams are lowercase
            // A single connection to stream.binance.com is only valid for 24 hours; expect to be disconnected at the 24 hour mark
            const string url = "wss://stream.binance.com:9443/ws";
            const string req = "/{0}@depth";

            var ws = new WebSocket(url + string.Format(req, symbol.ToLower()));
            //ws.Security.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            ws.Error += Ws_Error;
            var obs = Observable.FromEventPattern<EventHandler<MessageReceivedEventArgs>, object, MessageReceivedEventArgs>(h => ws.MessageReceived += h, h => ws.MessageReceived -= h);
            //ws.MessageReceived += Ws_OnTradesSocketMessage;
            ws.Opened += (object sender, EventArgs e) =>
            {
            };
            ws.Closed += (object sender, EventArgs e) =>
            {
            };
            ws.Open();
            return obs.Select(OnDepthSocketMessage);
        }
        private void Ws_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            Debug.Print(e.Exception.ToString());
            throw e.Exception;
        }

        void Ws_OnSocketMessage(object sender, MessageReceivedEventArgs args)
        {
            var response = JsonConvert.DeserializeObject<Binance.WsResponse<List<Binance.WsPriceTicker24hr>>>(args.Message);
            var ticker = response.data;
            if (DetailTicker != null)
            {
                foreach (var tick in ticker)
                    DetailTicker.Invoke(this, tick);
            }
        }

		void Ws_OnTradesSocketMessage(object sender, MessageReceivedEventArgs args)
        {
			var publicTrade = JsonConvert.DeserializeObject<Binance.WsTrade>(args.Message);
            if (Trade != null)
            {
				Trade.Invoke(this, publicTrade);
            }
        }

        Binance.WsPriceTicker24hr OnTickerSocketMessage(EventPattern<object, MessageReceivedEventArgs> p)
        {
            var response = JsonConvert.DeserializeObject<Binance.WsResponse<Binance.WsPriceTicker24hr>>(p.EventArgs.Message);
            var ticker = response.data;
            return ticker;
        }

        IList<Binance.WsPriceTicker24hr> OnTickerSocketMessage2(EventPattern<object, MessageReceivedEventArgs> p)
        {
            var tickers = JsonConvert.DeserializeObject<IList<Binance.WsPriceTicker24hr>>(p.EventArgs.Message);
            return tickers;
        }

        Binance.WsDepth OnDepthSocketMessage(EventPattern<object, MessageReceivedEventArgs> p)
        {
            var depth = JsonConvert.DeserializeObject<Binance.WsDepth>(p.EventArgs.Message);
            return depth;
        }

        Binance.WsTrade OnTradeSocketMessage(EventPattern<object, MessageReceivedEventArgs> p)
        {
            var trade = JsonConvert.DeserializeObject<Binance.WsTrade>(p.EventArgs.Message);
            Debug.Print($"Trade: {trade.tradeId} {trade.symbol} {trade.price} {trade.quantity} {trade.isBuyerMaker}");
            return trade;
        }

        #endregion

        private async Task<ApiResult<T>> ExecuteRequest<T>(string resource, HttpMethod method, TimeSpan offset, bool signed = false, int currentTry = 0, Dictionary<string, string> parameters = null)
        {
            var apiResult = (ApiResult<T>)Activator.CreateInstance(typeof(ApiResult<T>));
            string returnedData = "";
            HttpResponseMessage response = null;
            try
            {
                if (signed)
                {
                    signedClient.DefaultRequestHeaders.Clear();
                    signedClient.DefaultRequestHeaders.Add("X-MBX-APIKEY", ApiKey.ToManagedString());

                    ByteArrayContent content = null;
                    if (parameters == null)
                        parameters = new Dictionary<string, string>();
                    parameters.Add("timestamp", DateTime.UtcNow.Add(offset).ToUnixTimestamp().ToString());
                    content = new FormUrlEncodedContent(parameters);
                    var body = await content.ReadAsStringAsync();

                    var signature = ByteArrayToHexString(SignString(body));
                    parameters.Add("signature", signature);
                    content = new FormUrlEncodedContent(parameters);
                    body = await content.ReadAsStringAsync();

                    //log.Write(LogVerbosity.Debug, $"Sending POST request to {uriString} with {body}");
                    if (method == HttpMethod.Get)
                    {
                        // encode URI
                        response = await signedClient.GetAsync(resource + "?" + body);
                    }
                    else if (method == HttpMethod.Post)
                    {
                        // encode POST body
                        response = await signedClient.PostAsync(resource, content);
                    }

                    response.EnsureSuccessStatusCode();
                    returnedData = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ApiResult<T>>(returnedData);
                    apiResult = result;
                }
                else
                {
                    //publicClient.DefaultRequestHeaders.Clear();
                    ////log.Write(LogVerbosity.Debug, $"Sending GET request to {uriString}");
                    //response = await publicClient.GetAsync(uriString);
                    //response.EnsureSuccessStatusCode();
                    //returnedData = await response.Content.ReadAsStringAsync();
                    //var result = JsonConvert.DeserializeObject<T>(returnedData);
                    //apiResult.Success = true;
                    //apiResult.Data = result;
                }
                return apiResult;
            }
            catch (HttpRequestException e)
            {
                throw;
            }
        }

        internal void UpdateWeight(int w)
        {
            if (DateTime.Now >= weightDueTime)
            {
                Interlocked.Exchange(ref weight, w);
                weightDueTime = DateTime.Now.AddMinutes(1);
            }
            else
            {
                Interlocked.Add(ref weight, w);
            }
        }

        private const string apiUrl = "https://api.binance.com";
        RestSharp.RestClient client = new RestSharp.RestClient(apiUrl);
        internal int weight;
        internal DateTime weightDueTime;

        private static HttpClient publicClient = new HttpClient() { BaseAddress = new Uri(apiUrl) };
        private static HttpClient signedClient = new HttpClient() { BaseAddress = new Uri(apiUrl) };
    }
}

// Refer to: https://github.com/binance-exchange/binance-official-api-docs

namespace Binance
{

    public class ServerTime
    {
        public long serverTime { get; set; }
    }

    public class ExchangeInfo
    {
        public string timezone { get; set; }
        public long serverTime { get; set; }
        public List<Market> symbols { get; set; }
    }

    public class Market
    {
        public string symbol { get; set; }
        public string status { get; set; }
        public string baseAsset { get; set; }
        public int baseAssetPrecision { get; set; }
        public string quoteAsset { get; set; }
        public int quotePrecision { get; set; }
        public bool icebergAllowed { get; set; }
        public List<Filter> filters { get; set; }
    }

    public enum MarketStatus
    {
        PRE_TRADING,
        TRADING,
        POST_TRADING,
        END_OF_DAY,
        HALT,
        AUCTION_MATCH,
        BREAK
    }

    public enum FilterType
    {
        PRICE_FILTER,
        LOT_SIZE,
        MIN_NOTIONAL,
        ICEBERG_PARTS,
        MAX_NUM_ALGO_ORDERS
    }

    public enum OrderStatus
    {
        NEW,
        PARTIALLY_FILLED,
        FILLED,
        CANCELED,
        PENDING_CANCEL, // (currently unused)
        REJECTED,
        EXPIRED
    }

    public enum OrderType
    {
        LIMIT,
        MARKET,
        STOP_LOSS,
        STOP_LOSS_LIMIT,
        TAKE_PROFIT,
        TAKE_PROFIT_LIMIT,
        LIMIT_MAKER
    }

    public enum TimeInForce
    {
        GTC, // GoodTillCancel
        IOC, // ImmidiateOrCancel
        FOK  // FillOrKill
    }

    public class Filter
    {
        public string filterType { get; set; }
        public decimal minPrice { get; set; }       // "PRICE_FILTER"
        public decimal maxPrice { get; set; }       // "PRICE_FILTER"
        public decimal tickSize { get; set; }       // "PRICE_FILTER"
        public decimal minQty { get; set; }         // "LOT_SIZE"
        public decimal maxQty { get; set; }         // "LOT_SIZE"
        public decimal stepSize { get; set; }       // "LOT_SIZE"
        public decimal minNotional { get; set; }    // "MIN_NOTIONAL"
        public int limit { get; set; }              // "ICEBERG_PARTS"
        public int maxNumAlgoOrders { get; set; }   // "MAX_NUM_ALGO_ORDERS"
    }

    public class Trade
    {
        public long id { get; set; }
        public decimal price { get; set; }
        public decimal qty { get; set; }
        public long time { get; set; }
        public bool isBuyerMaker { get; set; }
        public bool isBestMatch { get; set; }
    }

    public class AggTrade
    {
        [DeserializeAs(Name = "a")]
        public long id { get; set; }
        [DeserializeAs(Name = "p")]
        public decimal price { get; set; }
        [DeserializeAs(Name = "q")]
        public decimal qty { get; set; }
        [DeserializeAs(Name = "f")]
        public decimal firstTradeId { get; set; }
        [DeserializeAs(Name = "l")]
        public decimal finalTradeId { get; set; }
        [DeserializeAs(Name = "T")]
        public long time { get; set; }
        [DeserializeAs(Name = "m")]
        public bool isBuyerMaker { get; set; }
        [DeserializeAs(Name = "M")]
        public bool isBestMatch { get; set; }
    }

    public class Depth
    {
        public long lastUpdateId { get; set; }
        public List<List<string>> bids { get; set; }
        public List<List<string>> asks { get; set; }
    }

    public class Depth2
    {
        public long lastUpdateId { get; set; }
        public string[][] bids { get; set; }
        public string[][] asks { get; set; }
    }

    public class PriceTicker
    {
        public string symbol { get; set; }
        public decimal price { get; set; }
    }

    public class PriceTicker24hr
    {
        public string symbol { get; set; }
        public decimal priceChange { get; set; }
        public decimal priceChangePercent { get; set; }
        public decimal weightedAvgPrice { get; set; }
        public decimal prevClosePrice { get; set; }
        public decimal lastPrice { get; set; }
        public decimal lastQty { get; set; }
        public decimal bidPrice { get; set; }
        public decimal askPrice { get; set; }
        public decimal openPrice { get; set; }
        public decimal highPrice { get; set; }
        public decimal lowPrice { get; set; }
        public decimal volume { get; set; }
        public decimal quoteVolume { get; set; }
        public long openTime { get; set; }
        public long closeTime { get; set; }
        public long fristId { get; set; }
        public long lastId { get; set; }
        public long count { get; set; }
    }

    public class AccountInfo
    {
        public decimal makerCommission { get; set; }
        public decimal takerCommission { get; set; }
        public decimal buyerCommission { get; set; }
        public decimal sellerCommission { get; set; }
        public bool canTrade { get; set; }
        public bool canWithdraw { get; set; }
        public bool canDeposit { get; set; }
        public long updateTime { get; set; }
        public List<Balance> balances { get; set; }
    }

    public class Balance
    {
        public string asset { get; set; }
        public decimal free { get; set; }
        public decimal locked { get; set; }
    }

    public class DepositHistory
    {
        public List<Transfer> depositList { get; set; }
    }

    public class WithdrawtHistory
    {
        public List<Transfer> withdrawList { get; set; }
    }

    public class Transfer
    {
        public string id { get; set; }
        public decimal amount { get; set; }
        public string address { get; set; }
        public string addressTag { get; set; }
        public string asset { get; set; }
        public string txId { get; set; }
        public long insertTime { get; set; }
        public long applyTime { get; set; }
        // for Deposit: 0:pending,1:success
        // for Withdraw: 0:Email Sent,1:Cancelled,2:Awaiting Approval,3:Rejected,4:Processing,5:Failure 6:Completed
        public int status { get; set; }
    }

    public class Order
    {
        public string symbol { get; set; }
        public long id { get; set; }
        public string clientOrderId { get; set; }
        public decimal price { get; set; }
        public decimal origQty { get; set; }
        public decimal executedQty { get; set; }
        public decimal cummulativeQuoteQty { get; set; }
        public string status { get; set; }
        public string timeInForce { get; set; }
        public string type { get; set; }
        public string side { get; set; }
        public decimal stopPrice { get; set; }
        public decimal icebergQty { get; set; }
        public long time { get; set; }
        public long updateTime { get; set; }
        public bool isWorking { get; set; }
    }

    #region Web socket structures
    // Combined stream events are wrapped as follows: {"stream":"<streamName>","data":<rawPayload>}
    public class WsResponse<T>
    {
        public string stream { get; set; }
        public T data { get; set; }
    }

    public class WsBaseResponse
    {
        [JsonProperty("e")]
        public string eventType { get; set; }
        [JsonProperty("E")]
        public long eventTime { get; set; }
        [JsonProperty("s")]
        public string symbol { get; set; }
    }

    public class WsCandlestick : WsBaseResponse
    {
        [JsonProperty("k")]
        public WsKline kline { get; set; }
    }

    public class WsKline
    {
        [JsonProperty("t")]
        public long openTime { get; set; }
        [JsonProperty("T")]
        public long closeTime { get; set; }
        [JsonProperty("i")]
        public string interval { get; set; }
        [JsonProperty("f")]
        public long fristTradeId { get; set; }
        [JsonProperty("L")]
        public long lastTradeId { get; set; }
        [JsonProperty("o")]
        public decimal openPrice { get; set; }
        [JsonProperty("c")]
        public decimal closePrice { get; set; }
        [JsonProperty("h")]
        public decimal highPrice { get; set; }
        [JsonProperty("l")]
        public decimal lowPrice { get; set; }
        [JsonProperty("v")]
        public decimal volume { get; set; }
        [JsonProperty("q")]
        public decimal quoteVolume { get; set; }
        [JsonProperty("n")]
        public long tradesCount { get; set; }
        [JsonProperty("x")]
        public long isFinal { get; set; }
        [JsonProperty("V")]
        public decimal takerBuyVolume { get; set; }
        [JsonProperty("Q")]
        public decimal takerBuyQuoteVolume { get; set; }
        //"V": "500",     // Taker buy base asset volume
        //"Q": "0.500",   // Taker buy quote asset volume
        //"B": "123456"   // Ignore
    }

    public class WsPriceTicker24hr
    {
        [JsonProperty("e")]
        public string eventType { get; set; }
        [JsonProperty("E")]
        public long eventTime { get; set; }
        [JsonProperty("s")]
        public string symbol { get; set; }
        [JsonProperty("p")]
        public decimal priceChange { get; set; }
        [JsonProperty("P")]
        public decimal priceChangePercent { get; set; }
        [JsonProperty("w")]
        public decimal weightedAvgPrice { get; set; }
        [JsonProperty("x")]
        public decimal prevClosePrice { get; set; }
        [JsonProperty("c")]
        public decimal lastPrice { get; set; }
        [JsonProperty("Q")]
        public decimal lastQty { get; set; }
        [JsonProperty("b")]
        public decimal bidPrice { get; set; }
        [JsonProperty("B")]
        public decimal bidQty { get; set; }
        [JsonProperty("a")]
        public decimal askPrice { get; set; }
        [JsonProperty("A")]
        public decimal askQty { get; set; }
        [JsonProperty("o")]
        public decimal openPrice { get; set; }
        [JsonProperty("h")]
        public decimal highPrice { get; set; }
        [JsonProperty("l")]
        public decimal lowPrice { get; set; }
        [JsonProperty("v")]
        public decimal volume { get; set; }
        [JsonProperty("q")]
        public decimal quoteVolume { get; set; }
        [JsonProperty("O")]
        public long openTime { get; set; }
        [JsonProperty("C")]
        public long closeTime { get; set; }
        [JsonProperty("F")]
        public long fristId { get; set; }
        [JsonProperty("L")]
        public long lastId { get; set; }
        [JsonProperty("n")]
        public long count { get; set; }
    }

    public class WsTrade
    {
        [JsonProperty("e")]
        public string eventType { get; set; }
        [JsonProperty("E")]
        public long eventTime { get; set; }
        [JsonProperty("s")]
        public string symbol { get; set; }
        [JsonProperty("t")]
        public long tradeId { get; set; }
        [JsonProperty("p")]
        public decimal price { get; set; }
        [JsonProperty("q")]
        public decimal quantity { get; set; }
        [JsonProperty("b")]
        public decimal buyerOrderId { get; set; }
        [JsonProperty("a")]
        public decimal sellerOrderId { get; set; }
        [JsonProperty("T")]
        public long tradeTime { get; set; }
        [JsonProperty("m")]
        public bool isBuyerMaker { get; set; }
        [JsonProperty("M")]
        public bool reserved { get; set; }
    }

    public class WsDepth : WsBaseResponse
    {
        [JsonProperty("U")]
        public WsKline firstUpdateId { get; set; }
        [JsonProperty("u")]
        public WsKline finalUpdateId { get; set; }
        [JsonProperty("b")]
        public List<List<string>> bids { get; set; }
        [JsonProperty("a")]
        public List<List<string>> asks { get; set; }
    }

    #endregion

}