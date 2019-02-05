using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Exchange.Net
{
    public class BittrexViewModel : ExchangeViewModel
    {

        public BittrexViewModel()
        {
        }

        protected override string DefaultMarket => "BTC";
        public override string ExchangeName => "Bittrex";
        protected override bool HasMarketSummariesPush => true;
        protected override bool HasTradesPush => true;

        public override async Task<List<PublicTrade>> GetTrades(string symbol, int limit = 50)
        {
            var trades = await client.GetMarketHistoryAsync(symbol);
            return trades.Select(
                t => new PublicTrade()
                {
                    Id = t.Id,
                    Price = t.Price,
                    Quantity = t.Quantity,
                    Side = t.OrderType == "BID" ? TradeSide.Buy : TradeSide.Sell,
                    Symbol = symbol,
                    Timestamp = t.TimeStamp
                }).ToList();
        }

		public override async Task<List<SymbolInformation>> GetMarkets()
        {
            var tmp = await client.GetMarketsAsync();
            return tmp.Where(m => m.IsActive).Select(
                x => new SymbolInformation()
                {
                    BaseAsset = x.MarketCurrency,
                    MaxPrice = decimal.MaxValue,
                    MinPrice = decimal.Zero,
                    MinQuantity = x.MinTradeSize,
                    QuantityDecimals = 8,
                    PriceDecimals = 8,
                    QuoteAsset = x.BaseCurrency,
                    Symbol = x.MarketName
                }).ToList();
        }

		public override async Task<List<PriceTicker>> GetMarketSummaries()
        {
            var tmp = await client.GetMarketSummariesAsync();
            return tmp.Select(
                x => new PriceTicker()
                {
                    Symbol = x.MarketName,
                    LastPrice = x.Last,
                    Volume = x.BaseVolume,
                    PriceChangePercent = Math.Round(((x.Last / x.PrevDay) - 1M) * 100M, 2)
                }).ToList();
        }

        long lastId = 0;

        private IEnumerable<Bittrex.Trade> GetTradesDiff(string symbol)
        {
            var trades = client.GetMarketHistory(symbol);
            trades.RemoveAll(x => x.Id <= lastId);
            if (trades.Count > 0)
                lastId = trades.First().Id;
            return trades.AsEnumerable().Reverse();
        }

        public override IObservable<PublicTrade> SubscribeTrades(string symbol, int limit = 50)
        {
            return Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).SelectMany<long, PublicTrade>(selector => GetTradesDiff(symbol).Select(
                t => new PublicTrade()
                {
                    Id = t.Id,
                    Price = t.Price,
                    Quantity = t.Quantity,
                    Side = t.OrderType == "BID" ? TradeSide.Buy : TradeSide.Sell,
                    Symbol = symbol,
                    Timestamp = t.TimeStamp
                }));
        }

		public override IObservable<PriceTicker> SubscribeMarketSummaries(IEnumerable<string> symbols, string period)
        {
            //var xyz = Observable.FromAsync(af => client.GetMarketSummariesAsync(symbols)).SelectMany<List<Bittrex.MarketSummary>, PriceTicker>(selector => selector.ToObservable().Select(
            //    x => new PriceTicker()
            //    {
            //        Symbol = x.MarketName,
            //        LastPrice = x.Last,
            //        Volume = x.BaseVolume,
            //        PriceChangePercent = Math.Round(((x.Last / x.PrevDay) - 1M) * 100M, 2)
            //    }));
            //return Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).SelectMany<long, PriceTicker>(selector => xyz);
            return Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).SelectMany<long, PriceTicker>(selector => client.GetMarketSummaries(symbols).Select(
                x => new PriceTicker()
                {
                    Symbol = x.MarketName,
                    LastPrice = x.Last,
                    Volume = x.BaseVolume,
                    PriceChangePercent = Math.Round(((x.Last / x.PrevDay) - 1M) * 100M, 2)
                }));
        }

		public override async Task<List<Transfer>> GetDeposits(string asset = null)
        {
            var tmp = await client.GetDepositHistoryAsync(asset);
            return tmp.Select(
                x => new Transfer()
                {
                    Address = x.Address,
                    Comission = x.TxCost,
                    Asset = x.Currency,
                    Quantity = x.Amount,
                    Timestamp = x.LastUpdated,
                    Type = TransferType.Deposit
                }).ToList();
        }

		public override async Task<List<Balance>> GetBalances()
        {
            var tmp = await client.GetBalancesAsync();
            return tmp.Select(
                p => new Balance(p.Currency, UsdAssets.Contains(p.Currency))
                {
                    Free = p.Available,
                    Locked = p.Balance - p.Available
                }).ToList();
        }

		public override async Task<List<Transfer>> GetWithdrawals(string asset = null)
        {
            var tmp = await client.GetWithdrawalHistoryAsync(asset);
            return tmp.Select(
                x => new Transfer()
                {
                    Address = x.Address,
                    Comission = x.TxCost,
                    Asset = x.Currency,
                    Quantity = x.Amount,
                    Timestamp = x.Opened,
                    Type = TransferType.Withdrawal
                }).ToList();
        }

		public override bool FilterByMarket(string symbol, string market)
        {
            return symbol.StartsWith(market);
        }
		public override bool FilterByAsset(string symbol, string asset)
        {
            // UGLY!
            return symbol.ToUpper().Contains($"-{asset}".ToUpper());
        }

        BittrexApi client = new BittrexApi();
    }
}

