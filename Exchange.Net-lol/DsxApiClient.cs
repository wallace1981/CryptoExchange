using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Exchange.Net
{
    class DsxApiClient : ExchangeApiCore
    {
        private const string GetServerTimeEndpoint = "";
        private const string GetExchangeInfoEndpoint = "/mapi/info";
        private const string GetPriceTickerEndpoint = "";
        private const string Get24hrPriceTickerEndpoint = "/mapi/ticker";
        private const string GetTradesEndpoint = "/mapi/trades";

        public DsxApiClient()
        {
            if (LoadApiKeys("dsx.hash"))
            {
                this.Encryptor = new HMACSHA512(ApiSecret.ToByteArray());
            }
        }

        private string SignRequest(string postBody)
        {
            return Convert.ToBase64String(SignString(postBody));
        }

        #region Public API

        public async Task<DSX.ExchangeInfo> GetExchangeInfoAsync()
        {
            var request = new RestSharp.RestRequest(GetExchangeInfoEndpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<DSX.ExchangeInfo>(request);
            if (response.IsSuccessful)
            {
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public ApiResult<DSX.ExchangeInfo> GetExchangeInfo()
        {
            var request = new RestSharp.RestRequest(GetExchangeInfoEndpoint, RestSharp.Method.GET);
            var response = ExecutePublicRequest<DSX.ExchangeInfo>(request);
            return response;
        }

        public async Task<List<DSX.Trade>> GetTradesAsync(string symbol, int limit = 150)
        {
            var endpoint = $"/mapi/trades/{symbol}?{limit}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Dictionary<string,List<DSX.Trade>>>(request);
            if (response.IsSuccessful)
            {
                var result = response.Data;
                return result.Values.FirstOrDefault();
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public ApiResult<Dictionary<string, DSX.Trade[]>> GetTrades(string symbol, int limit = 150)
        {
            var request = new RestSharp.RestRequest($"{GetTradesEndpoint}/{symbol}?{limit}", RestSharp.Method.GET);
            var response = ExecutePublicRequest<Dictionary<string, DSX.Trade[]>>(request);
            return response;
        }

        public async Task<Dictionary<string, DSX.Ticker>> GetTickerAsync(string symbol)
        {
            var endpoint = $"mapi/ticker/{symbol}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Dictionary<string, DSX.Ticker>>(request);
            if (response.IsSuccessful)
            {
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public Dictionary<string, DSX.Ticker> GetTicker(string symbol)
        {
            var endpoint = $"mapi/ticker/{symbol}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = client.Execute<Dictionary<string, DSX.Ticker>>(request);
            if (response.IsSuccessful)
            {
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }


        public ApiResult<Dictionary<string, DSX.Ticker>> GetTicker2(string symbol)
        {
            var request = new RestSharp.RestRequest($"{Get24hrPriceTickerEndpoint}/{symbol}", RestSharp.Method.GET);
            var response = ExecutePublicRequest<Dictionary<string, DSX.Ticker>>(request);
            return response;
        }
        public Dictionary<string, DSX.OrderBook> GetDepth(string symbol)
        {
            var endpoint = $"mapi/depth/{symbol}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = client.Execute<Dictionary<string, DSX.OrderBook>>(request);
            if (response.IsSuccessful)
            {
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

		public async Task<Dictionary<string, DSX.OrderBook>> GetDepthAsync(string symbol)
        {
            var endpoint = $"mapi/depth/{symbol}";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<Dictionary<string, DSX.OrderBook>>(request);
            if (response.IsSuccessful)
            {
                var result = response.Data;
                return result;
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public ApiResult<T> ExecutePublicRequest<T>(RestSharp.IRestRequest request, string contentPath = null)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var response = client.Execute(request);
                sw.Stop();
                if (response.IsSuccessful)
                {
                    Log.DebugFormat("{0} {1} - {2}ms : {3} ({4}).", request.Method.ToString(), client.BuildUri(request), sw.ElapsedMilliseconds, response.StatusDescription, (int)response.StatusCode);
#if DEBUG
                    if (contentPath != null)
                        File.WriteAllText(contentPath, response.Content);
#endif
                    var data = JsonConvert.DeserializeObject<T>(response.Content);
                    return new ApiResult<T>(data, null, sw.ElapsedMilliseconds);
                }
                else
                {
                    Log.WarnFormat("{0} {1} - {2}ms : {3} ({4}).", request.Method.ToString(), client.BuildUri(request), sw.ElapsedMilliseconds, response.StatusDescription, (int)response.StatusCode);
                    return new ApiResult<T>(default(T), new ApiError((int)response.StatusCode, response.StatusDescription), sw.ElapsedMilliseconds);
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
        #endregion

        #region Account API
        // NOTE: We are going to serialize signed API calls using semaphore.
        // This way 'nonce' will always be correct.
        // TODO: Implement calls per minute/hour (because API has numeric limitation for signed calls).
        private SemaphoreSlim slim = new SemaphoreSlim(1);

        public async Task<List<DSX.Transfer>> GetDepositsAsync(string asset = null)
        {
            return await GetTranfersHistoryAsync("Incoming", asset);
        }

        public async Task<List<DSX.Transfer>> GetWithdrawalsAsync(string asset = null)
        {
            return await GetTranfersHistoryAsync("Withdraw", asset);
        }

        private async Task<List<DSX.Transfer>> GetTranfersHistoryAsync(string type, string asset = null)
        {
            const string endpoint = "tapi/v2/history/transactions";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.POST);
            if (asset != null)
            {
                request.AddParameter("currency", asset);
            }
            request.AddParameter("type", type);
            //request.AddParameter("nonce", nonce);
            //var content = new FormUrlEncodedContent(request.Parameters.Select(x => new KeyValuePair<string, string>(x.Name, x.Value.ToString())));
            //var body = await content.ReadAsStringAsync();
            //request.AddHeader("Key", ApiKey.ToManagedString());
            //request.AddHeader("Sign", SignRequest(body));

            //var response = await client.ExecuteTaskAsync(request);
            var response = await RequestSignedApiAsync(request);
            if (response.IsSuccessful)
            {
                response.Content = response.Content.Replace("\"return\"", "\"result\"");
                var apiResult = client.Deserialize<DSX.ApiResult<Dictionary<int, DSX.Transfer>>>(response).Data;
                if (apiResult.success)
                {
                    return apiResult.result.Values.ToList();
                }
                else
                {
                    throw new Exception(apiResult.error);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<DSX.AccountInfo> GetAccountInfoAsync()
        {
            const string endpoint = "tapi/v2/info/account";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.POST);
            //request.AddParameter("nonce", nonce);
            //var content = new FormUrlEncodedContent(request.Parameters.Select(x => new KeyValuePair<string, string>(x.Name, x.Value.ToString())));
            //var body = await content.ReadAsStringAsync();
            //request.AddHeader("Key", ApiKey.ToManagedString());
            //request.AddHeader("Sign", SignRequest(body));

            //var response = await client.ExecuteTaskAsync(request);
            var response = await RequestSignedApiAsync(request);
            if (response.IsSuccessful)
            {
                response.Content = response.Content.Replace("\"return\"", "\"result\"");
                var apiResult = client.Deserialize<DSX.ApiResult<DSX.AccountInfo>>(response).Data;
                if (apiResult.success)
                {
                    return apiResult.result;
                }
                else
                {
                    throw new Exception(apiResult.error);
                }
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        private async Task<RestSharp.IRestResponse> RequestSignedApiAsync(RestSharp.IRestRequest request)
        {
            try
            {
                await slim.WaitAsync().ConfigureAwait(true);
                request.AddParameter("nonce", nonce);
                var content = new FormUrlEncodedContent(request.Parameters.Select(x => new KeyValuePair<string, string>(x.Name, x.Value.ToString())));
                var body = await content.ReadAsStringAsync();
                request.AddHeader("Key", ApiKey.ToManagedString());
                request.AddHeader("Sign", SignRequest(body));
                var response = await client.ExecuteTaskAsync(request);
                return response;
            }
            finally
            {
                slim.Release();
            }
        }

        #endregion

        RestSharp.RestClient client = new RestSharp.RestClient("https://dsx.uk/");
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

    public class OrderBook
    {
        public List<List<decimal>> asks { get; set; }
        public List<List<decimal>> bids { get; set; }
    }

    public class ApiResult<T>
    {
        public bool success { get; set; }
        public string error { get; set; }
        public T result { get; set; }
    }
}