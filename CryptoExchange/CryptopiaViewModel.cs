using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Exchange.Net
{
    class CryptopiaViewModel : ExchangeViewModel
    {
        public override string ExchangeName => "Cryptopia";

        protected override string DefaultMarket => "BTC";
        protected override bool HasMarketSummariesPush => true;

		public override bool FilterByAsset(string symbol, string asset)
        {
            return symbol.StartsWith(asset, StringComparison.CurrentCultureIgnoreCase);
        }

		public override bool FilterByMarket(string symbol, string market)
        {
            return symbol.EndsWith(market, StringComparison.CurrentCultureIgnoreCase);
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

		public async override Task<List<SymbolInformation>> GetMarkets()
        {
            var tmp = await client.GetTradePairsAsync();
            return tmp.Select(
                p => new SymbolInformation()
                {
                    BaseAsset = p.Symbol,
                    MaxPrice = p.MaximumPrice,
                    MinPrice = p.MinimumPrice,
                    MinQuantity = p.MinimumTrade,
                    MaxQuantity = p.MinimumTrade,
                    QuoteAsset = p.BaseSymbol,
                    Symbol = p.Label
                }).ToList();
        }

		public override Task<List<PriceTicker>> GetMarketSummaries()
        {
            throw new NotImplementedException();
        }

		public override IObservable<PriceTicker> SubscribeMarketSummaries(IEnumerable<string> symbols, string period)
        {
            return Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).SelectMany<long, PriceTicker>(selector => client.GetMarkets().Where(predicate => predicate.Volume > decimal.Zero).Select(
                x => new PriceTicker()
                {
                    Symbol = x.Label,
                    LastPrice = x.LastPrice,
                    Volume = x.BaseVolume,
                    PriceChangePercent = Math.Round(((x.Close / x.Open) - 1M) * 100M, 2)
                }));
        }

        public override Task<List<PublicTrade>> GetTrades(string symbol, int limit = 50)
        {
            throw new NotImplementedException();
        }

        public async override Task<List<Transfer>> GetWithdrawals(string asset = null)
        {
            await Task.Delay(50);
            return new List<Transfer>();
        }

        CryptopiaApiClient client = new CryptopiaApiClient();
    }
}
