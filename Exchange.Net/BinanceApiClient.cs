using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using WebSocket4Net;

namespace Exchange.Net
{
    public static class DictionaryExtensions
    {
        // NOTE: could be mixed /depth/BTCUSD?ignore_invalid=1
        public static string BuildQuery(this IDictionary<string, object> keyValuePairs)
        {
            if (keyValuePairs == null)
                return string.Empty;
            var segmentParams = keyValuePairs
                .Where(x => x.Key != null && x.Value != null && x.Key.StartsWith(@"/"))
                .Select(x => Uri.EscapeDataString(x.Value.ToString()));
            var pairs = keyValuePairs
                .Where(x => x.Key != null && x.Value != null && !x.Key.StartsWith(@"/"))
                .Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value.ToString()));
            var segment = string.Join(@"/", segmentParams);
            if (!string.IsNullOrWhiteSpace(segment))
                segment = @"/" + segment;
            var query = string.Join("&", pairs);
            return string.Join("?", segment, query).TrimEnd('?');
        }

        public static FormUrlEncodedContent ToFormUrlEncodedContent(this IDictionary<string, object> keyValuePairs)
        {
            return new FormUrlEncodedContent(keyValuePairs.Where(x => x.Key != null && x.Value != null).Select(x => new KeyValuePair<string, string>(x.Key, x.Value.ToString())));
        }
    }

    public abstract class ApiRequest
    {
        public string GetQueryString()
        {
            var props = GetQueryProperties();
            var keyValuePairs = props.ToDictionary(p => p.Name, p => p.GetValue(this));
            return keyValuePairs.BuildQuery();
        }

        public abstract bool Validate();

        protected virtual IEnumerable<PropertyInfo> GetQueryProperties()
        {
            return GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetMethod.IsPublic);
        }
    }

    public abstract class PublicRequest : ApiRequest
    {

    }

    public class BlankPublicRequest : PublicRequest
    {
        public override bool Validate()
        {
            return true;
        }
    }

    public class TradesRequest : PublicRequest
    {
        public string symbol { get; set; }
        public int limit { get; set; }

        public override bool Validate()
        {
            throw new NotImplementedException();
        }
    }

    public class KlinesRequest : PublicRequest
    {
        public string symbol { get; set; }
        public string interval { get; set; }
        public long? startTime { get; set; }
        public long? endTime { get; set; }
        public int? limit { get; set; }

        public override bool Validate()
        {
            throw new NotImplementedException();
        }
    }

    public abstract class SignedRequest : ApiRequest
    {
        public long timestamp { get; set; }
        public string signature { get; set; }// => Sign();
        public long? recvWindow { get; set; }

        public SignedRequest()
        {
        }

        //protected override IEnumerable<PropertyInfo> GetQueryProperties()
        //{
        //    return base.GetQueryProperties().Where(p => p.Name != nameof(signature));
        //}
    }

    public class BlankSignedRequest : SignedRequest
    {
        public BlankSignedRequest()
        {
        }

        public override bool Validate()
        {
            return true;
        }
    }

    public class NewOrderRequest : SignedRequest
    {
        public string symbol { get; set; }
        public Binance.TradeSide side { get; set; }
        public Binance.OrderType type { get; set; }
        public Binance.TimeInForce? timeInForce { get; set; }
        public decimal quantity { get; set; }
        public decimal? price { get; set; }
        public string newClientOrderId { get; set; } // A unique id for the order. Automatically generated if not sent.
        public decimal? stopPrice { get; set; }      // Used with STOP_LOSS, STOP_LOSS_LIMIT, TAKE_PROFIT, and TAKE_PROFIT_LIMIT orders.
        public decimal? icebergQty { get; set; }     // Used with LIMIT, STOP_LOSS_LIMIT, and TAKE_PROFIT_LIMIT to create an iceberg order.
        public Binance.OrderRespType? newOrderRespType { get; set; } // Set the response JSON. ACK, RESULT, or FULL; MARKET and LIMIT order types default to FULL, all other orders default to ACK.

        public NewOrderRequest()
        {
        }

        public override bool Validate()
        {
            /* Additional mandatory parameters:
                LIMIT               timeInForce, quantity, price
                MARKET              quantity
                STOP_LOSS           quantity, stopPrice
                STOP_LOSS_LIMIT     timeInForce, quantity, price, stopPrice
                TAKE_PROFIT         quantity, stopPrice
                TAKE_PROFIT_LIMIT   timeInForce, quantity, price, stopPrice
                LIMIT_MAKER         quantity, price
                
               Other info:
                LIMIT_MAKER are LIMIT orders that will be rejected if they would immediately match and trade as a taker.
                STOP_LOSS and TAKE_PROFIT will execute a MARKET order when the stopPrice is reached.
                Any LIMIT or LIMIT_MAKER type order can be made an iceberg order by sending an icebergQty.
                Any order with an icebergQty MUST have timeInForce set to GTC.
               
               Trigger order price rules against market price for both MARKET and LIMIT versions:
                Price above market price: STOP_LOSS BUY, TAKE_PROFIT SELL
                Price below market price: STOP_LOSS SELL, TAKE_PROFIT BUY
                */
            return false;
        }
    }

    public class BaseQueryOrderRequest : SignedRequest
    {
        public string symbol { get; set; }

        public override bool Validate()
        {
            return !string.IsNullOrWhiteSpace(symbol);
        }
    }

    public class AdvancedQueryOrderRequest : SignedRequest
    {
        public string symbol { get; set; }
        public long? startTime { get; set; }
        public long? endTime { get; set; }
        public long? fromId { get; set; }
        public int? limit { get; set; }

        public override bool Validate()
        {
            return !string.IsNullOrWhiteSpace(symbol);
        }
    }

    public class QueryOrderRequest : SignedRequest
    {
        public string symbol { get; set; }
        public long? orderId { get; set; }
        public string origClientOrderId { get; set; }

        public override bool Validate()
        {
            if (string.IsNullOrWhiteSpace(symbol)) return false;
            // check if symbol is in list of symbols and that symbol status is TRADE.
            if (orderId == null && string.IsNullOrWhiteSpace(origClientOrderId)) return false;
            return true;
        }
    }

    public class BinanceApiClient : ExchangeApiCore
    {
        protected override string LogName => "binance";

        public BinanceApiClient() : base("binance.hash", typeof(HMACSHA256))
        {
        }

        public void SetServerTimeOffset(long utc, double elapsedMilliseconds)
        {
            CalcServerTimeOffset(utc, elapsedMilliseconds);
        }

        #region  Public API

        const string GetServerTimeEndpoint = "/api/v1/time";
        const int GetServerTimeWeight = 1;
        const string GetExchangeInfoEndpoint = "/api/v1/exchangeInfo";
        const int GetExchangeInfoWeight = 1;
        const string GetPriceTickerEndpoint = "/api/v3/ticker/price";
        const int GetPriceTickerWeight = 1;
        const string Get24hrPriceTickerEndpoint = "/api/v1/ticker/24hr";
        const int Get24hrPriceTickerWeight = 40;
        const string GetRecentTradesEndpoint = "/api/v1/trades";
        const int GetRecentTradesWeight = 1;
        const string GetAggregatedTradesEndpoint = "/api/v1/aggTrades";
        const int GetAggregatedTradesWeight = 1;
        const string GetDepthEndpoint = "/api/v1/depth";
        const int GetDepthWeight = 100;
        const string GetKlinesEndpoint = "/api/v1/klines";
        const int GetKlinesWeight = 1;
        const string GetBookTickerEndpoint = "/api/v3/ticker/bookTicker";
        const int GetBookTickerWeight = 2;
        const string GetAvgPriceEndpoint = "/api/v3/avgPrice";
        const int GetAvgPriceWeight = 1;

        public Task<ApiResult<Binance.ServerTime>> GetServerTimeAsync()
        {
            var requestMessage = CreateRequestMessage(new BlankPublicRequest(), GetServerTimeEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.ServerTime>(requestMessage, GetServerTimeWeight);
        }

        public IObservable<ApiResult<Binance.ServerTime>> ObserveServerTime()
        {
            var obs = Observable.FromAsync(GetServerTimeAsync);
            return Observable.Interval(TimeSpan.FromSeconds(2)).SelectMany(x => obs);
        }


        public Task<ApiResult<Binance.ExchangeInfo>> GetExchangeInfoAsync()
        {
            var requestMessage = CreateRequestMessage(new BlankPublicRequest(), GetExchangeInfoEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.ExchangeInfo>(requestMessage, GetExchangeInfoWeight, contentPath: "exchangeInfo");
        }

        public IObservable<ApiResult<Binance.ExchangeInfo>> ObserveExchangeInfo()
        {
            var obs = Observable.FromAsync(GetExchangeInfoAsync);
            return Observable.Interval(TimeSpan.FromMinutes(5)).SelectMany(x => obs);
        }

        public Task<ApiResult<Binance.PriceTicker[]>> GetPriceTickerAsync()
        {
            var requestMessage = CreateRequestMessage(new BlankPublicRequest(), GetPriceTickerEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.PriceTicker[]>(requestMessage, GetPriceTickerWeight, contentPath: "tickers");
        }

        public IObservable<ApiResult<Binance.PriceTicker[]>> ObservePriceTicker()
        {
            var obs = Observable.FromAsync(GetPriceTickerAsync);
            //.SelectMany(x => x.Success ?
            //x.Data.Select(y => new ApiResult<Binance.PriceTicker>(y, x.Error, x.ElapsedMilliseconds)) :
            //Enumerable.Repeat(new ApiResult<Binance.PriceTicker>(null, x.Error, x.ElapsedMilliseconds), 1));
            return Observable.Interval(TimeSpan.FromSeconds(1)).SelectMany(x => obs);
        }

        public Task<ApiResult<Binance.PriceTicker24hr>> Get24hrPriceTickerAsync(string symbol)
        {
            var requestParams = new Dictionary<string, object>() { { "symbol", symbol } };
            var requestMessage = CreateRequestMessage(requestParams, Get24hrPriceTickerEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.PriceTicker24hr>(requestMessage, 1, contentPath: $"ticker24hr-{symbol}");
        }

        public Task<ApiResult<Binance.PriceTicker24hr[]>> Get24hrPriceTickerAsync()
        {
            var requestMessage = CreateRequestMessage(new BlankPublicRequest(), Get24hrPriceTickerEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.PriceTicker24hr[]>(requestMessage, Get24hrPriceTickerWeight, contentPath: "ticker24hr");
        }

        public IObservable<ApiResult<Binance.PriceTicker24hr[]>> Observe24hrPriceTicker()
        {
            var obs = Observable.FromAsync(Get24hrPriceTickerAsync);
            return Observable.Interval(TimeSpan.FromSeconds(10)).SelectMany(x => obs);
        }

        public Task<ApiResult<Binance.Trade[]>> GetRecentTradesAsync(string symbol, int limit = 500)
        {
            var requestMessage = CreateRequestMessage(new TradesRequest() { symbol = symbol, limit = limit }, GetRecentTradesEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.Trade[]>(requestMessage, GetRecentTradesWeight, contentPath: $"trades-{symbol}");
        }

        public Task<ApiResult<Binance.AggTrade[]>> GetAggregatedTradesAsync(string symbol, int limit = 500)
        {
            var requestMessage = CreateRequestMessage(new TradesRequest() { symbol = symbol, limit = limit }, GetAggregatedTradesEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.AggTrade[]>(requestMessage, GetAggregatedTradesWeight, contentPath: $"trades-{symbol}");
        }

        public IObservable<ApiResult<Binance.Trade[]>> ObserveRecentTrades(string symbol, int limit = 500)
        {
            var obs = Observable.FromAsync(() => GetRecentTradesAsync(symbol, limit));
            //.SelectMany(x => x.Success ?
            //x.Data.Select(y => new ApiResult<Binance.Trade>(y, x.Error, x.ElapsedMilliseconds)) :
            //Enumerable.Repeat(new ApiResult<Binance.Trade>(null, x.Error, x.ElapsedMilliseconds), 1));
            return Observable.Interval(TimeSpan.FromSeconds(1)).SelectMany(x => obs);
        }

        public Task<ApiResult<Binance.Depth>> GetDepthAsync(string symbol, int limit = 100)
        {
            var requestMessage = CreateRequestMessage(new TradesRequest() { symbol = symbol, limit = limit }, GetDepthEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.Depth>(requestMessage, limit < GetDepthWeight ? 1 : limit / GetDepthWeight, contentPath: $"depth-{symbol}");
        }

        public IObservable<ApiResult<Binance.Depth>> ObserveDepth(string symbol, int limit = 100)
        {
            var obs = Observable.FromAsync(() => GetDepthAsync(symbol, limit));
            return Observable.Interval(TimeSpan.FromSeconds(1)).SelectMany(x => obs);
        }

        public Task<ApiResult<List<object[]>>> GetKlinesAsync(string market, string interval, long? startTime = null, long? endTime = null, int? limit = null)
        {
            var requestMessage = CreateRequestMessage(new KlinesRequest() { symbol = market, interval = interval, limit = limit, startTime = startTime, endTime = endTime }, GetKlinesEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<List<object[]>>(requestMessage, GetKlinesWeight, contentPath: $"klines-{market}-{interval}");
        }

        public Task<ApiResult<List<Binance.BookTicker>>> GetBookTickerAsync(string market = null)
        {
            var requestMessage = CreateRequestMessage(new KlinesRequest() { symbol = market }, GetBookTickerEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<List<Binance.BookTicker>>(requestMessage, GetKlinesWeight, contentPath: $"bookTicker");
        }

        public IObservable<ApiResult<List<Binance.BookTicker>>> ObserveBookTicker(string market = null)
        {
            var obs = Observable.FromAsync(() => GetBookTickerAsync(market));
            return Observable.Interval(TimeSpan.FromSeconds(2)).SelectMany(x => obs);
        }

        public Task<ApiResult<Binance.AvgPrice>> GetAveragePrice(string symbol)
        {
            var requestParams = new Dictionary<string, object>() { { "symbol", symbol } };
            var requestMessage = CreateRequestMessage(requestParams, GetAvgPriceEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.AvgPrice>(requestMessage, GetAvgPriceWeight, contentPath: $"avgPrice-{symbol}");
        }

        #endregion

        #region Test API
        public ApiResult<Binance.ExchangeInfo> GetExchangeInfoOffline()
        {
            var json = LoadJson("exchangeInfo");
            return new ApiResult<Binance.ExchangeInfo>(JsonConvert.DeserializeObject<Binance.ExchangeInfo>(json), null);
        }

        public ApiResult<Binance.PriceTicker24hr[]> GetPriceTicker24hrOffline()
        {
            var json = LoadJson("ticker24hr");
            return new ApiResult<Binance.PriceTicker24hr[]>(JsonConvert.DeserializeObject<Binance.PriceTicker24hr[]>(json), null);
        }

        public ApiResult<Binance.AccountInfo> GetAccountInfoOffline()
        {
            var json = LoadJson("account");
            return new ApiResult<Binance.AccountInfo>(JsonConvert.DeserializeObject<Binance.AccountInfo>(json), null);
        }
        #endregion

        #region Signed API

        private const string PlaceOrderEndpoint = "/api/v3/order";
        private const int PlaceOrderWeight = 1;
        private const string TestPlaceOrderEndpoint = "/api/v3/order/test";
        private const int TestPlaceOrderWeight = 1;
        private const string QueryOrderEndpoint = "/api/v3/order";
        private const int QueryOrderWeight = 1;
        private const string CancelOrderEndpoint = "/api/v3/order";
        private const int CancelOrderWeight = 1;
        private const string GetOpenOrdersEndpoint = "/api/v3/openOrders";
        private const int GetOpenOrdersWeight = 40;
        private const string GetAllOrdersEndpoint = "/api/v3/allOrders";
        private const int GetAllOrdersWeight = 5;
        private const string GetTradeListEndpoint = "/api/v3/myTrades";
        private const int GetTradeListWeight = 5;

        private const string GetAccountInfoEndpoint = "/api/v3/account";
        private const int GetAccountInfoWeight = 5;
        private const string GetDepositHistoryEndpoint = "/wapi/v3/depositHistory.html";
        private const int GetDepositHistoryWeight = 1;
        private const string GetWithdrawHistoryEndpoint = "/wapi/v3/withdrawHistory.html";
        private const int GetWithdrawHistoryWeight = 1;

        public Task<ApiResult<Binance.AccountInfo>> GetAccountInfoAsync()
        {
            var requestMessage = CreateRequestMessage(new BlankSignedRequest(), GetAccountInfoEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.AccountInfo>(requestMessage, GetAccountInfoWeight, contentPath: "account");
        }

        public Task<ApiResult<Binance.DepositHistory>> GetDepositHistoryAsync(string asset = null)
        {
            var requestMessage = CreateRequestMessage(new BlankSignedRequest(), GetDepositHistoryEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.DepositHistory>(requestMessage, GetDepositHistoryWeight, contentPath: "deposits");
        }

        public Task<ApiResult<Binance.WithdrawHistory>> GetWithdrawHistoryAsync(string asset = null)
        {
            var requestMessage = CreateRequestMessage(new BlankSignedRequest(), GetWithdrawHistoryEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.WithdrawHistory>(requestMessage, GetWithdrawHistoryWeight, contentPath: "withdrawals");
        }

        public Task<ApiResult<Binance.Order[]>> GetOpenOrdersAsync(string symbol = null)
        {
            var requestMessage = CreateRequestMessage(new BlankSignedRequest(), GetOpenOrdersEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.Order[]>(requestMessage, symbol != null ? 1 : GetOpenOrdersWeight, contentPath: "openOrders");
        }

        public Task<ApiResult<Binance.Order[]>> GetOrdersHistoryAsync(string symbol)
        {
            var requestMessage = CreateRequestMessage(new BaseQueryOrderRequest() { symbol = symbol }, GetAllOrdersEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.Order[]>(requestMessage, symbol != null ? 1 : GetAllOrdersWeight, contentPath: $"allOrders-{symbol}");
        }

        public Task<ApiResult<Binance.AccountTrade[]>> GetAccountTradesAsync(string symbol, long? start = null, long? end = null, long? fromId = null, int? limit = null)
        {
            var requestMessage = CreateRequestMessage(new AdvancedQueryOrderRequest() { symbol = symbol, startTime = start, endTime = end, fromId = fromId, limit = limit }, GetTradeListEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.AccountTrade[]>(requestMessage, GetTradeListWeight, contentPath: $"accTrades-{symbol}");
        }

        public Task<ApiResult<Binance.NewOrderResponseResult>> PlaceOrderAsync(string symbol, Binance.TradeSide side, Binance.OrderType type, decimal amount, decimal? price = null, decimal? stopPrice = null, Binance.TimeInForce? tif = null, string newClientOrderId = null, Binance.OrderRespType? newOrderRespType = null)
        {
            if (type == Binance.OrderType.LIMIT || type == Binance.OrderType.STOP_LOSS_LIMIT || type == Binance.OrderType.TAKE_PROFIT_LIMIT)
            {
                if (tif == null)
                {
                    tif = Binance.TimeInForce.GTC;
                }
            }
            if (type == Binance.OrderType.STOP_LOSS_LIMIT || type == Binance.OrderType.TAKE_PROFIT_LIMIT)
            {
                if (newOrderRespType == null)
                {
                    newOrderRespType = Binance.OrderRespType.FULL;
                }
            }
            var request = new NewOrderRequest()
            {
                newOrderRespType = newOrderRespType,
                price = price,
                stopPrice = stopPrice,
                quantity = amount,
                side = side,
                symbol = symbol,
                timeInForce = tif,
                type = type
            };
            var requestMessage = CreateRequestMessage(request, PlaceOrderEndpoint, HttpMethod.Post);
            return ExecuteRequestAsync<Binance.NewOrderResponseResult>(requestMessage, PlaceOrderWeight, $"newOrder-{symbol}");
        }

        public Task<ApiResult<Binance.Blank>> TestPlaceOrderAsync(string symbol, Binance.TradeSide side, Binance.OrderType type, decimal amount, decimal? price = null, decimal? stopPrice = null, Binance.TimeInForce? tif = null, string newClientOrderId = null)
        {
            var request = new NewOrderRequest()
            {
                newOrderRespType = null,
                price = price,
                quantity = amount,
                side = side,
                symbol = symbol,
                timeInForce = tif,
                type = type
            };
            var requestMessage = CreateRequestMessage(request, TestPlaceOrderEndpoint, HttpMethod.Post);
            return ExecuteRequestAsync<Binance.Blank>(requestMessage, TestPlaceOrderWeight, $"testNewOrder-{symbol}");
        }

        public Task<ApiResult<Binance.QueryOrderResponseResult>> QueryOrderAsync(string symbol, long? orderId = null,  string origClientOrderId = null)
        {
            // Notes:
            // * Either orderId or origClientOrderId must be sent.
            // * For some historical orders cummulativeQuoteQty will be < 0, meaning the data is not available at this time.
            var request = new QueryOrderRequest()
            {
                symbol = symbol,
                orderId = orderId,
                origClientOrderId = origClientOrderId
            };
            var requestMessage = CreateRequestMessage(request, QueryOrderEndpoint, HttpMethod.Get);
            return ExecuteRequestAsync<Binance.QueryOrderResponseResult>(requestMessage, QueryOrderWeight, $"queryOrder-{symbol}");
        }

        public Task<ApiResult<Binance.CancelOrderResponseResult>> CancelOrderAsync(string symbol, long? orderId = null, string origClientOrderId = null)
        {
            // Notes:
            // * Either orderId or origClientOrderId must be sent.
            var request = new QueryOrderRequest()
            {
                symbol = symbol,
                orderId = orderId,
                origClientOrderId = origClientOrderId
            };
            var requestMessage = CreateRequestMessage(request, CancelOrderEndpoint, HttpMethod.Delete);
            return ExecuteRequestAsync<Binance.CancelOrderResponseResult>(requestMessage, CancelOrderWeight, $"cancelOrder-{symbol}");
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
            var ws = new WebSocketWrapper(uri2, "ticker");
            return ws.Observe().SelectMany(OnTickerSocketMessage);
        }

        public IObservable<Binance.WsCandlestick> SubscribeKlinesAsync(IEnumerable<string> symbols, string interval)
        {
            // A single connection to stream.binance.com is only valid for 24 hours; expect to be disconnected at the 24 hour mark
            const string url = "wss://stream.binance.com:9443";
            const string req = "/stream?streams=";

            var streamId = string.Join("-", symbols) + "@kline_" + interval;
            var uri = url + req + string.Join("/", symbols.Select(s => s.ToLower() + "@kline_" + interval));
            var ws = new WebSocketWrapper(uri, streamId);
            return ws.Observe().Select(OnKlineSocketMessage);
        }

        /// <summary>
        /// The Trade Streams push raw trade information; each trade has a unique buyer and seller
        /// </summary>
        /// <param name="symbol">Symbol.</param>
        public IObservable<Binance.WsTrade> SubscribePublicTradesAsync(string symbol)
        {
            // All symbols for streams are lowercase
            // A single connection to stream.binance.com is only valid for 24 hours; expect to be disconnected at the 24 hour mark
            const string req = "/{0}@trade";

            var ws = new WebSocketWrapper(wssUrl + string.Format(req, symbol.ToLower()), $"{symbol} trades");
            return ws.Observe().Select(OnTradeSocketMessage);
            //var ws = new WebSocket(url + string.Format(req, symbol.ToLower()));
            ////ws.Security.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            //ws.Error += Ws_Error;
            //var obs = Observable.FromEventPattern<EventHandler<MessageReceivedEventArgs>, object, MessageReceivedEventArgs>(
            //    h => { ws.MessageReceived += h; ws.Open(); },
            //    h => { ws.MessageReceived -= h; ws.Close(); }
            //    );
            ////ws.MessageReceived += Ws_OnTradesSocketMessage;
            //ws.Opened += (object sender, EventArgs e) =>
            //{
            //    Debug.Print("Opened trades stream");
            //};
            //ws.Closed += (object sender, EventArgs e) =>
            //{
            //    Debug.Print("Closed trades stream");
            //};
            ////ws.Open();
            //return obs.Select(OnTradeSocketMessage);
        }

        /// <summary>
        /// Order book price and quantity depth updates used to locally manage an order book pushed every second.
        /// </summary>
        /// <param name="symbol">Symbol.</param>
        public IObservable<Binance.WsDepth> ObserveOrderBook(string symbol)
        {
            // All symbols for streams are lowercase
            // A single connection to stream.binance.com is only valid for 24 hours; expect to be disconnected at the 24 hour mark
            const string req = "/{0}@depth";

            var ws = new WebSocketWrapper(wssUrl + string.Format(req, symbol.ToLower()), $"{symbol} depth");
            return ws.Observe().Select(OnDepthSocketMessage2);
        }

        private void Ws_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            Debug.Print(e.Exception.ToString());
            throw e.Exception;
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

        IEnumerable<Binance.WsPriceTicker24hr> OnTickerSocketMessage(string message)
        {
            var tickers = JsonConvert.DeserializeObject<IEnumerable<Binance.WsPriceTicker24hr>>(message);
            return tickers;
        }

        Binance.WsDepth OnDepthSocketMessage(EventPattern<object, MessageReceivedEventArgs> p)
        {
            return OnDepthSocketMessage2(p.EventArgs.Message);
        }

        Binance.WsDepth OnDepthSocketMessage2(string message)
        {
            var depth = JsonConvert.DeserializeObject<Binance.WsDepth>(message.Replace(",[]", string.Empty));
            //DumpJson(depth.finalUpdateId.ToString(), p.EventArgs.Message);
            return depth;
        }

        internal Binance.WsTrade OnTradeSocketMessage(EventPattern<object, MessageReceivedEventArgs> p)
        {
            return OnTradeSocketMessage(p.EventArgs.Message);
        }

        internal Binance.WsTrade OnTradeSocketMessage(string message)
        {
            var trade = JsonConvert.DeserializeObject<Binance.WsTrade>(message);
            //Debug.Print($"Trade: {trade.tradeId} {trade.symbol} {trade.price} {trade.quantity} {trade.isBuyerMaker}");
            return trade;
        }

        internal Binance.WsCandlestick OnKlineSocketMessage(string message)
        {
            var result = JsonConvert.DeserializeObject<Binance.WsResponse<Binance.WsCandlestick>>(message);
            return result.data;
        }

        #endregion

        #region Helpers

        protected HttpRequestMessage CreateRequestMessage(SignedRequest requestParams, string endpoint, HttpMethod method)
        {
            long offset = Interlocked.Read(ref serverTimeOffsetMilliseconds);
            requestParams.timestamp = DateTime.UtcNow.AddMilliseconds(offset).ToUnixTimestamp();
            var query = requestParams.GetQueryString(); // Query w/o signature
            requestParams.signature = ByteArrayToHexString(SignString(query.TrimStart('?')));
            HttpContent content = null;
            if (method == HttpMethod.Get)
                endpoint = endpoint + requestParams.GetQueryString(); // Take query string with signature.
            else
                content = new StringContent(requestParams.GetQueryString().TrimStart('?')); // Take query string with signature.
            var requestMessage = new HttpRequestMessage(method, endpoint) { Content = content };
            requestMessage.Headers.Add("X-MBX-APIKEY", ApiKey.ToManagedString());
            requestMessage.Properties.Add("QUERY", query);
            return requestMessage;
        }

        protected HttpRequestMessage CreateRequestMessage(PublicRequest requestParams, string endpoint, HttpMethod method)
        {
            var query = requestParams.GetQueryString();
            HttpContent content = null;
            if (method == HttpMethod.Get)
                endpoint = endpoint + query;
            else
                content = new StringContent(query);
            var requestMessage = new HttpRequestMessage(method, endpoint) { Content = content };
            requestMessage.Properties.Add("QUERY", query);
            requestMessage.Headers.Add("Keep-Alive", "6000");
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
            var requestMessage = new HttpRequestMessage(method, endpoint) { Content = content };
            requestMessage.Properties.Add("QUERY", query);
            return requestMessage;
        }

        protected const int RateLimitStatusCode = 429;
        protected const int BannedStatusCode = 418;

        protected async Task<ApiResult<T>> ExecuteRequestAsync<T>(HttpRequestMessage requestMessage, int endpointWeight, string contentPath = null)
        {
            ApiResult<T> result = null;
            try
            {
                var sw = Stopwatch.StartNew();
                var responseMessage = await httpClient.SendAsync(requestMessage).ConfigureAwait(false);
                var content = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
                sw.Stop();

                var uri = responseMessage.RequestMessage.RequestUri.ToString();
                Debug.Print($"{requestMessage.Method} {uri} : {responseMessage.StatusCode}");

                DumpJson(contentPath, content);

                if (responseMessage.IsSuccessStatusCode)
                {
                    UpdateWeight(endpointWeight);
                    Log.DebugFormat("{0} {1} - {2}ms : {3} ({4}).", requestMessage.Method, uri, sw.ElapsedMilliseconds, responseMessage.ReasonPhrase, (int)responseMessage.StatusCode);
                    DumpJson(contentPath, content);
                    result = new ApiResult<T>(JsonConvert.DeserializeObject<T>(content.Replace(",[]", string.Empty)), null, sw.ElapsedMilliseconds);
                }
                else if ((int)responseMessage.StatusCode == RateLimitStatusCode)
                {
                    //TODO
                }
                else if ((int)responseMessage.StatusCode == BannedStatusCode)
                {
                    //TODO
                }
                else if (responseMessage.StatusCode >= HttpStatusCode.BadRequest && responseMessage.StatusCode < HttpStatusCode.InternalServerError)
                {
                    // HTTP 4XX return codes are used for for malformed requests; the issue is on the sender's side.
                    var err = JsonConvert.DeserializeObject<Binance.Error>(content);
                    result = new ApiResult<T>(default(T), new ApiError(err.code, err.msg), sw.ElapsedMilliseconds);
                }
                else
                {
                    Debug.Print($"{content}");
                    result = new ApiResult<T>(default(T), new ApiError((int)responseMessage.StatusCode, responseMessage.ReasonPhrase), sw.ElapsedMilliseconds);
                }

                responseMessage.Content.Dispose();
                responseMessage.Dispose();
            }
            catch (HttpRequestException ex)
            {
                for (var innerEx = ex.InnerException; innerEx != null; innerEx = innerEx.InnerException)
                {
                    var socketEx = innerEx as System.Net.Sockets.SocketException;
                    if (socketEx?.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionReset)
                    {
                        // An existing connection was forcibly closed by the remote host (10054)
                        // because of Keep-Alive=true. Retry.
                        result = new ApiResult<T>(default(T), new ApiError(socketEx.ErrorCode, socketEx.Message));
                        break;
                    }
                }
                if (result == null)
                {
                    result = new ApiResult<T>(default(T), new ApiError(ex.HResult, ex.Message));
                }
            }
            catch (Exception ex)
            {
                result = new ApiResult<T>(default(T), new ApiError(ex.HResult, ex.Message));
            }
            finally
            {
                requestMessage.Dispose();
            }
            return result;
        }

        #endregion

        internal void CalcServerTimeOffset(long serverTime, double callDelayMilliseconds)
        {
            var diff = serverTime.FromUnixTimestamp(convertToLocalTime: false) - DateTime.UtcNow;
            var offset = diff.TotalMilliseconds - callDelayMilliseconds / 2.0;
            Interlocked.Exchange(ref serverTimeOffsetMilliseconds, (long)offset);
            Debug.Print($"Server Time Offset: {offset}ms (diff: {diff.TotalMilliseconds}ms; callDelay: {callDelayMilliseconds}).");
        }

        private const string apiUrl = "https://api.binance.com";
        private const string wssUrl = "wss://stream.binance.com:9443/ws";
        private static HttpClient httpClient = new HttpClient() { BaseAddress = new Uri(apiUrl) };
        private long serverTimeOffsetMilliseconds;
    }
}

// Refer to: https://github.com/binance-exchange/binance-official-api-docs

namespace Binance
{
    public class Blank
    {
    }

    public class CallResult
    {
        public string msg { get; set; }
        public bool success { get; set; } = true;
    }

    public class Error
    {
        public int code { get; set; }
        public string msg { get; set; }
    }

    public class ServerTime
    {
        public long serverTime { get; set; }
    }

    public class ExchangeInfo
    {
        public string timezone { get; set; }
        public long serverTime { get; set; }
        public IEnumerable<Market> symbols { get; set; }
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
        public IEnumerable<Filter> filters { get; set; }
    }

    public enum TradeSide
    {
        BUY,
        SELL
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
        PERCENT_PRICE,
        LOT_SIZE,
        MIN_NOTIONAL,
        ICEBERG_PARTS,
        MARKET_LOT_SIZE,
        MAX_NUM_ORDERS,
        MAX_NUM_ALGO_ORDERS,
        MAX_NUM_ICEBERG_ORDERS
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

    public enum OrderRespType
    {
        ACK,
        RESULT,
        FULL
    }

    public class Filter
    {
        public string filterType { get; set; }
        public decimal minPrice { get; set; }       // "PRICE_FILTER"
        public decimal maxPrice { get; set; }       // "PRICE_FILTER"
        public decimal tickSize { get; set; }       // "PRICE_FILTER"
        public decimal multiplierUp { get; set; }   // "PERCENT_PRICE"
        public decimal multiplierDown { get; set; } // "PERCENT_PRICE"
        public int avgPriceMins { get; set; }       // "PERCENT_PRICE"
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
        [JsonProperty("a")]
        public long id { get; set; }
        [JsonProperty("p")]
        public decimal price { get; set; }
        [JsonProperty("q")]
        public decimal qty { get; set; }
        [JsonProperty("f")]
        public decimal firstTradeId { get; set; }
        [JsonProperty("l")]
        public decimal finalTradeId { get; set; }
        [JsonProperty("T")]
        public long time { get; set; }
        [JsonProperty("m")]
        public bool isBuyerMaker { get; set; }
        [JsonProperty("M")]
        public bool isBestMatch { get; set; }
    }

    public class Depth
    {
        public long lastUpdateId { get; set; }
        public List<List<string>> bids { get; set; }
        public List<List<string>> asks { get; set; }
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

    public class BookTicker
    {
        public string symbol { get; set; }
        public decimal bidPrice { get; set; }
        public decimal bidQty { get; set; }
        public decimal askPrice { get; set; }
        public decimal askQty { get; set; }
    }

    public class AvgPrice
    {
        public int mins { get; set; }
        public decimal price { get; set; }
    }

    public class AccountInfo : CallResult
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

    public class DepositHistory : CallResult
    {
        public IEnumerable<Transfer> depositList { get; set; }
    }

    public class WithdrawHistory : CallResult
    {
        public IEnumerable<Transfer> withdrawList { get; set; }
    }

    public class Transfer : CallResult
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
        public long orderId { get; set; }
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
        public AccountTrade[] fills { get; set; }
    }

    public class NewOrderResponseResult
    {
        public string symbol { get; set; }
        public long orderId { get; set; }
        public string clientOrderId { get; set; }
        public long transactTime { get; set; }
        public decimal price { get; set; }
        public decimal origQty { get; set; }
        public decimal executedQty { get; set; }
        public decimal cummulativeQuoteQty { get; set; }
        public string status { get; set; }
        public string timeInForce { get; set; }
        public string type { get; set; }
        public string side { get; set; }
        public OrderFill[] fills { get; set; }
    }

    public class QueryOrderResponseResult : NewOrderResponseResult
    {
        public decimal stopPrice { get; set; }
        public decimal icebergQty { get; set; }
        public long time { get; set; }
        public long updateTime { get; set; }
        public bool isWorking { get; set; }
    }

    public class CancelOrderResponseResult : NewOrderResponseResult
    {
    }

    public class OrderFill
    {
        public long tradeId { get; set; }
        public decimal price { get; set; }
        public decimal qty { get; set; }
        public decimal comission { get; set; }
        public string comissionAsset { get; set; }
    }

    public class AccountTrade : OrderFill
    {
        public string symbol { get; set; }
        public long id { get; set; }
        public long orderId { get; set; }
        public long time { get; set; }
        public string status { get; set; }
        public bool isBuyer { get; set; }
        public bool isMaker { get; set; }
        public bool isBestMatch { get; set; }
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
        public long firstUpdateId { get; set; }
        [JsonProperty("u")]
        public long finalUpdateId { get; set; }
        [JsonProperty("b")]
        public List<List<string>> bids { get; set; }
        [JsonProperty("a")]
        public List<List<string>> asks { get; set; }
    }

    #endregion

}