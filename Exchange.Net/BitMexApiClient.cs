using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Exchange.Net
{
    class BitMexApiClient : ExchangeApiCore
    {
        protected override string LogName => "bitmex";

        #region Public API
        const string InstrumentActiveEndpoint = "instrument/active";

        #endregion

        private const string apiUrl = "https://testnet.bitmex.com/api/v1";
        private const string wssUrl = "wss://testnet.bitmex.com/realtime";
        private static HttpClient httpClient = new HttpClient() { BaseAddress = new Uri(apiUrl) };
    }
}
