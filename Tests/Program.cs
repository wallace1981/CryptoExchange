using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Exchange.Net;

namespace Tests
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var p0 = new Dictionary<string, object>() {};
            var p1 = new Dictionary<string, object>() { { "symbol", "BTCUSDT" }, { "limit", 100 } };
            var p2 = new Dictionary<string, object>() { { "/symbol", "BTCUSDT" }, { "/limit", 100 } };
            var p3 = new Dictionary<string, object>() { { "/symbol", "BTCUSDT" }, { "/limit", 100 }, { "ignore_invalid", true } };
            IDictionary<string, object> p9 = null;

            Console.WriteLine(p0.BuildQuery());
            Console.WriteLine(p1.BuildQuery());
            Console.WriteLine(p2.BuildQuery());
            Console.WriteLine(p3.BuildQuery());
            Console.WriteLine(p9.BuildQuery());

            await TestBinance();
            await TestBittrex();
        }

        static async Task TestBittrex()
        {
            try
            {
                var bittrex = new BittrexApiClient();
                // public api
                var markets = await bittrex.GetMarketsAsync();
                Debug.Assert(markets.Success);
                var si = markets.Data.SingleOrDefault(x => x.MarketCurrency == "ETH" && x.BaseCurrency == "BTC");
                var trades = await bittrex.GetMarketHistoryAsync(si.MarketName);
                Debug.Assert(trades.Success);
                var orderbook = await bittrex.GetOrderBookAsync(si.MarketName);
                Debug.Assert(orderbook.Success);
                var currencies = await bittrex.GetCurrenciesAsync();
                Debug.Assert(currencies.Success);
                var ticker = await bittrex.GetTickerAsync(si.MarketName);
                Debug.Assert(ticker.Success);
                var summary = await bittrex.GetMarketSummaryAsync(si.MarketName);
                Debug.Assert(ticker.Success);

                // signed api
                var balances = await bittrex.GetBalancesAsync();
                Debug.Assert(balances.Success);
                var deposits = await bittrex.GetDepositHistoryAsync();
                Debug.Assert(deposits.Success);
                var withdrawals = await bittrex.GetWithdrawalHistoryAsync();
                Debug.Assert(deposits.Success);

                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        static async Task TestBinance()
        {
            try
            {
                var binance = new BinanceApiClient();
                // public api

                // signed api
                var orders = await binance.GetOpenOrdersAsync();
                Debug.Assert(orders.Success);

                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
