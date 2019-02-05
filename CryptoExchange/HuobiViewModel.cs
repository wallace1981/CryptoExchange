using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Huobi;

namespace Exchange.Net
{
    class HuobiViewModel : ExchangeViewModel
    {
        public override string ExchangeName => "Huobi";
        protected override string DefaultMarket => "BTC";
        protected override bool HasMarketSummariesPull => true;
        protected override bool HasMarketSummariesPush => false;
        protected override bool HasTradesPull => false;
        protected override bool HasTradesPush => true;
        protected override bool HasOrderBookPull => false;
        protected override bool HasOrderBookPush => true;

		public override bool FilterByMarket(string symbol, string market)
        {
            return symbol.EndsWith(market, StringComparison.CurrentCultureIgnoreCase);
        }

		public override bool FilterByAsset(string symbol, string asset)
        {
            return symbol.StartsWith(asset, StringComparison.CurrentCultureIgnoreCase);
        }

		public async override Task<List<Balance>> GetBalances()
        {
            await Task.Delay(50);
            return new List<Balance>();
        }

        public async override Task<List<Transfer>> GetDeposits(string asset = null)
        {
            await Task.Delay(50);
            return new List<Transfer>();
        }

        public async override Task<List<Order>> GetOpenOrders()
        {
            await Task.Delay(50);
            return new List<Order>();
        }

		public async override Task<List<SymbolInformation>> GetMarkets()
        {
            var tmp = await client.GetMarketsAsync();
            UpdateStatus($"{tmp.Count} markets listed");
            //symbols.AddRange(tmp.Select(p => p.baseCurrency + p.quoteCurrency));
            return tmp.Where(x => x.symbolPartition != "bifurcation").Select(
                p => new SymbolInformation()
                {
                    BaseAsset = p.baseCurrency,
                    MaxPrice = 0,
                    MinPrice = 0,
                    MinQuantity = 0,
                    QuantityDecimals = p.amountPrecision,
                    PriceDecimals = p.pricePrecision,
                    QuoteAsset = p.quoteCurrency,
                    Symbol = p.symbol
                }).ToList();
        }

		public override IObservable<PriceTicker> SubscribeMarketSummaries(IEnumerable<string> symbols, string period)
        {
            var obs = Observable.FromEventPattern<HuobiApiClient.DetailTickHandler, string, Huobi.WsTick>(h => client.DetailTick += h, h => client.DetailTick -= h);
            obs = obs.Buffer(TimeSpan.FromSeconds(1)).SelectMany(selector => selector.ToObservable());
            client.SubscribeMarketSummariesAsync(symbols);
            return obs.Select(
                x => new PriceTicker()
                {
                    Symbol = x.Sender,
                    LastPrice = x.EventArgs.close,
                    PriceChangePercent = Math.Round(((x.EventArgs.close / x.EventArgs.open) - 1M) * 100M, 2),
                    Volume = x.EventArgs.vol
                });
        }

        private void OnDetailTick(string symbol, WsTick tick)
        {
            throw new NotImplementedException();
        }

		public async override Task<List<PriceTicker>> GetMarketSummaries()
        {
            var tmp = await client.GetPriceTickerAsync();
            return tmp.Select(
                x => new PriceTicker()
            {
                Symbol = x.symbol,
                LastPrice = x.close,
                Volume = x.vol
            }).ToList();
        }

        public async override Task<List<PublicTrade>> GetTrades(string symbol, int limit = 50)
        {
            await Task.Delay(50);
            return new List<PublicTrade>();
        }

        public async override Task<List<Transfer>> GetWithdrawals(string asset = null)
        {
            await Task.Delay(50);
            return new List<Transfer>();
        }

        //List<string> symbols = new List<string>();
        HuobiApiClient client = new HuobiApiClient();

        internal void UpdateStatus(string msg)
        {
            Status = $"{ExchangeName}: {msg}.";
        }
    }
}
