using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Exchange.Net
{
    class CryptopiaApiClient : ExchangeApiCore
    {
        public async Task<List<Cryptopia.TradePair>> GetTradePairsAsync()
        {
            const string endpoint = "GetTradePairs";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Cryptopia.ApiResult<List<Cryptopia.TradePair>>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.Success)
                {
                    return apiResult.Data;
                }
                else
                {
                    throw new Exception(apiResult.Message);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public List<Cryptopia.Market> GetMarkets()
        {
            const string endpoint = "GetMarkets";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = client.Execute<Cryptopia.ApiResult<List<Cryptopia.Market>>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.Success)
                {
                    return apiResult.Data;
                }
                else
                {
                    throw new Exception(apiResult.Message);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public List<Cryptopia.MarketHistory> GetMarketHistory(string symbol)
        {
            const string endpoint = "GetMarketHistory";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = client.Execute<Cryptopia.ApiResult<List<Cryptopia.MarketHistory>>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.Success)
                {
                    return apiResult.Data;
                }
                else
                {
                    throw new Exception(apiResult.Message);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        RestSharp.RestClient client = new RestSharp.RestClient("https://www.cryptopia.co.nz/api/");
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

    public class ApiResult<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
    }
}