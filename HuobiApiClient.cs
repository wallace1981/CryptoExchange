using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Huobi.WebSocketAPI;
using Newtonsoft.Json;
using RestSharp.Deserializers;
using WebSocket4Net;

namespace Exchange.Net
{
    class HuobiApiClient : ExchangeApiCore
    {
        public delegate void DetailTickHandler(string symbol, Huobi.WsTick tick);

        public event DetailTickHandler DetailTick;

        public async Task<List<Huobi.Market>> GetMarketsAsync()
        {
            const string endpoint = "v1/common/symbols";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Huobi.ApiResult<List<Huobi.Market>>>(request).ConfigureAwait(false);
#if DEBUG
            System.IO.File.WriteAllText("huobi-symbols.json", response.Content);
#endif
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.status == "ok")
                {
                    return apiResult.data;
                }
                else
                {
                    throw new Exception(apiResult.errMsg);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<List<Huobi.Kline>> GetPriceTickerAsync()
        {
            const string endpoint = "market/tickers";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Huobi.ApiResult<List<Huobi.Kline>>>(request).ConfigureAwait(false);
#if DEBUG
            System.IO.File.WriteAllText("huobi-market_tickers.json", response.Content);
#endif
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.status == "ok")
                {
                    return apiResult.data;
                }
                else
                {
                    throw new Exception(apiResult.errMsg);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public void SubscribeMarketSummariesAsync(IEnumerable<string> symbols)
        {
            // Huobi hasn't API to get current price for all symbols at once.
            // We will subscribe to socket here, but will return List of WsTick objects
            // which value later will be updated by socket messages.

            const string url = "wss://api.huobi.pro/ws";

            var ws = new WebSocket(url);
            ws.Security.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            ws.DataReceived += new EventHandler<DataReceivedEventArgs>(OnSocketData);
            ws.Error += Ws_Error;
            ws.Opened += (sender, e) =>
            {
                foreach (var symbol in symbols)
                {
                    string req = $"market.{symbol.ToLower()}.detail";
                    ws.Send(JsonConvert.SerializeObject(new Huobi.WsSubRequest() { id = req, sub = req }));
                }
            };
            ws.Open();
        }

        private void Ws_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void OnSocketData(object sender, DataReceivedEventArgs args)
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

        RestSharp.RestClient client = new RestSharp.RestClient("https://api.huobi.pro/");
        // v1/common/symbols
    }
}

namespace Huobi
{
    public class Market
    {
        [DeserializeAs(Name = "base-currency")]   
        public string baseCurrency { get; set; }
        [DeserializeAs(Name = "quote-currency")]
        public string quoteCurrency { get; set; }
        [DeserializeAs(Name = "price-precision")]
        public int pricePrecision { get; set; }
        [DeserializeAs(Name = "amount-precision")]
        public int amountPrecision { get; set; }
        [DeserializeAs(Name = "symbol-partition")]
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

    public class ApiResult<T>
    {
        public string status { get; set; }
        [DeserializeAs(Name = "err-code")]
        public string errCode { get; set; }
        [DeserializeAs(Name = "err-msg")]
        public string errMsg { get; set; }
        public T data { get; set; }
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