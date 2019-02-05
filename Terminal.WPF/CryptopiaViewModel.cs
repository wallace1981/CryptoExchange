using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Exchange.Net
{
    class CryptopiaViewModel : ExchangeViewModel
    {
        public override string ExchangeName => "Cryptopia";

        protected override string DefaultMarket => "BTC";

		public override bool FilterByAsset(string symbol, string asset)
        {
            return symbol.StartsWith(asset, StringComparison.CurrentCultureIgnoreCase);
        }

		public override bool FilterByMarket(string symbol, string market)
        {
            return symbol.EndsWith(market, StringComparison.CurrentCultureIgnoreCase);
        }

        public CryptopiaViewModel()
        {
        }


        protected async override Task<IEnumerable<SymbolInformation>> GetMarketsAsync()
        {
            var result = await client.GetTradePairsAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(CreateSymbolInformation);
            else
                return Enumerable.Empty<SymbolInformation>();
        }

        protected async override Task<IEnumerable<PriceTicker>> GetTickersAsync()
        {
            var result = await client.GetMarketsAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(ToPriceTicker);
            else
                return Enumerable.Empty<PriceTicker>();
        }

        protected async override Task<IEnumerable<PublicTrade>> GetPublicTradesAsync(string market, int limit)
        {
            var result = await client.GetMarketHistoryAsync(market).ConfigureAwait(false);
            if (result.Success)
            {
                var si = GetSymbolInformation(market);
                var trades = result.Data.Select(x => Convert(x, si)).ToList();
                return trades;
            }
            else
                return Enumerable.Empty<PublicTrade>();
        }

        protected async override Task<IEnumerable<OrderBookEntry>> GetOrderBookAsync(string market, int limit)
        {
            var result = await client.GetOrderBookAsync(market, limit).ConfigureAwait(false);
            if (result.Success)
            {
                var depth = result.Data;
                var asks = depth.Sell.Select(a => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = a.Price, Quantity = a.Volume, Side = TradeSide.Sell });
                var bids = depth.Buy.Select(b => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = b.Price, Quantity = b.Volume, Side = TradeSide.Buy });
                return asks.Take(limit).Reverse().Concat(bids.Take(limit));
            }
            else
                return Enumerable.Empty<OrderBookEntry>();
        }

        //protected override void SubscribeMarketData()
        //{
        //    var sub24hrPriceTicker = client.ObserveMarketSummaries().Publish();
        //    sub24hrPriceTicker.Subscribe(
        //        (result) =>
        //        {
        //            if (result.Success)
        //            {
        //                ProcessPriceTicker(result.Data.Select(ToPriceTicker));
        //            }
        //        }).DisposeWith(Disposables);
        //    sub24hrPriceTicker.Connect().DisposeWith(Disposables);
        //}

        internal SymbolInformation CreateSymbolInformation(Cryptopia.TradePair x)
        {
            var cmcEntry = GetCmcEntry(x.Symbol);
            return new SymbolInformation()
            {
                BaseAsset = x.Symbol,
                MaxPrice = x.MaximumPrice,
                MinPrice = x.MinimumPrice,
                MinQuantity = x.MinimumTrade,
                MaxQuantity = x.MinimumTrade,
                QuoteAsset = x.BaseSymbol,
                Symbol = x.Id.ToString(),
                CmcId = cmcEntry != null ? cmcEntry.id : -1,
                CmcName = cmcEntry != null ? cmcEntry.name : x.BaseSymbol,
                CmcSymbol = cmcEntry != null ? cmcEntry.symbol : x.Label
            };
        }

        internal PriceTicker ToPriceTicker(Cryptopia.Market x)
        {
            return new PriceTicker()
            {
                BuyVolume = x.BaseBuyVolume,
                HighPrice = x.High,
                LastPrice = x.LastPrice,
                LowPrice = x.Low,
                PriceChange = x.Change,
                PriceChangePercent = CalcChangePercent(x.Close, x.Open),
                QuoteVolume = x.BaseVolume,
                Symbol = x.TradePairId.ToString(),
                SymbolInformation = GetSymbolInformation(x.TradePairId.ToString()),
                Volume = x.Volume
            };
        }

        internal PublicTrade Convert(Cryptopia.MarketHistory x, SymbolInformation si)
        {
            return new PublicTrade(si)
            {
                Id = x.Timestamp, // NOTE: wont be unique, but at least something...
                Price = x.Price,
                Quantity = x.Amount,
                Side = x.Type == "Buy" ? TradeSide.Buy : TradeSide.Sell,
                Time = x.Timestamp.FromUnixTimestamp()
            };

        }

        CryptopiaApiClient client = new CryptopiaApiClient();
    }
}
