using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Exchange.Net
{
    public class BittrexApi : ExchangeApiCore
    {
        public BittrexApi()
        {
            if(LoadApiKeys("bittrex.hash"))
            {
                this.Encryptor = new HMACSHA512(ApiSecret.ToByteArray());
            }
        }

        private string SignRequest(string url)
        {
            return ByteArrayToHexString(SignString(url));
        }

        #region Public API
        public List<Bittrex.Trade> GetMarketHistory(string symbol)
        {
            var endpoint = $"/public/getmarkethistory?market={symbol}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = client.Execute<Bittrex.ApiResult<List<Bittrex.Trade>>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.success)
                {
                    return apiResult.result;
                }
                else
                {
                    throw new Exception(apiResult.message);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<List<Bittrex.Trade>> GetMarketHistoryAsync(string symbol)
        {
            return await Task.Factory.StartNew<List<Bittrex.Trade>>(state => GetMarketHistory(symbol), null);
            //var endpoint = $"/public/getmarkethistory?market={symbol}";
            //var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            //var response = await client.ExecuteTaskAsync<Bittrex.ApiResult<List<Bittrex.Trade>>>(request);
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

        public async Task<List<Bittrex.Market>> GetMarketsAsync()
        {
            const string endpoint = "/public/getmarkets";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Bittrex.ApiResult<List<Bittrex.Market>>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.success)
                {
                    return apiResult.result;
                }
                else
                {
                    throw new Exception(apiResult.message);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<List<Bittrex.MarketSummary>> GetMarketSummariesAsync()
        {
            const string endpoint = "/public/getmarketsummaries";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Bittrex.ApiResult<List<Bittrex.MarketSummary>>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.success)
                {
                    return apiResult.result;
                }
                else
                {
                    throw new Exception(apiResult.message);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<List<Bittrex.MarketSummary>> GetMarketSummariesAsync(IEnumerable<string> symbols)
        {
            const string endpoint = "/public/getmarketsummaries";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Bittrex.ApiResult<List<Bittrex.MarketSummary>>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.success)
                {
                    return apiResult.result;
                }
                else
                {
                    throw new Exception(apiResult.message);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public IObservable<Bittrex.MarketSummary> GetMarketSummaries(IEnumerable<string> symbols)
        {
            const string endpoint = "/public/getmarketsummaries";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = client.Execute<Bittrex.ApiResult<List<Bittrex.MarketSummary>>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.success)
                {
                    return apiResult.result.ToObservable();
                }
                else
                {
                    throw new Exception(apiResult.message);
                }
            }
            else if (response.ResponseStatus == ResponseStatus.Error && response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                throw new Exception(response.StatusDescription);
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<Bittrex.OrderBook> GetOrderBookAsync(string symbol)
        {
            var endpoint = "public/getorderbook?market={symbol}&type=both";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Bittrex.ApiResult<Bittrex.OrderBook>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.success)
                {
                    return apiResult.result;
                }
                else
                {
                    throw new Exception(apiResult.message);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }
        #endregion

        #region Account API
        public async Task<List<Bittrex.Transfer>> GetDepositHistoryAsync(string asset = null)
        {
            var endpoint = $"/account/getdeposithistory?apikey={ApiKey.ToManagedString()}&nonce={nonce}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            if (asset != null)
            {
                request.AddParameter("currency", asset);
            }
            var uri = client.BuildUri(request);
            var apiSign = SignRequest(uri.AbsoluteUri);
            request.AddHeader("apisign", apiSign);
            var response = await client.ExecuteTaskAsync<Bittrex.ApiResult<List<Bittrex.Transfer>>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.success)
                {
                    return apiResult.result;
                }
                else
                {
                    throw new Exception(apiResult.message);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<List<Bittrex.Transfer>> GetWithdrawalHistoryAsync(string asset = null)
        {
            var endpoint = $"/account/getwithdrawalhistory?apikey={ApiKey.ToManagedString()}&nonce={nonce}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            if (asset != null)
            {
                request.AddParameter("currency", asset);
            }
            var uri = client.BuildUri(request);
            var apiSign = SignRequest(uri.AbsoluteUri);
            request.AddHeader("apisign", apiSign);
            var response = await client.ExecuteTaskAsync<Bittrex.ApiResult<List<Bittrex.Transfer>>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.success)
                {
                    return apiResult.result;
                }
                else
                {
                    throw new Exception(apiResult.message);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<List<Bittrex.Balance1>> GetBalancesAsync()
        {
            var endpoint = $"/account/getbalances?apikey={ApiKey.ToManagedString()}&nonce={nonce}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var uri = client.BuildUri(request);
            var apiSign = SignRequest(uri.AbsoluteUri);
            request.AddHeader("apisign", apiSign);
            var response = await client.ExecuteTaskAsync<Bittrex.ApiResult<List<Bittrex.Balance1>>>(request);
            if (response.IsSuccessful)
            {
                var apiResult = response.Data;
                if (apiResult.success)
                {
                    return apiResult.result;
                }
                else
                {
                    throw new Exception(apiResult.message);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }
        #endregion

        RestSharp.RestClient client = new RestSharp.RestClient("https://bittrex.com/api/v1.1");
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
        public decimal PrevDay { get; set; }
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
        public List<OrderBookEntry> buy { get; set; }
        public List<OrderBookEntry> sell { get; set; }
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

    public class ApiResult<TResult>
    {
        public bool success { get; set; }
        public string message { get; set; }
        public TResult result { get; set; }
    }

}