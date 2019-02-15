using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Exchange.Net
{
    class HuobiViewModel : ExchangeViewModel
    {
        protected string ServerStatus;
        protected string ClientStatus => $"Weight is {client.Weight}, expiration in {client.WeightReset.TotalSeconds} secs.";

        protected void UpdateStatus(string serverStatus, string clientMsg = null)
        {
            Status = string.Join("  ", serverStatus, clientMsg ?? ClientStatus);
            ServerStatus = serverStatus;
        }

        protected override async Task GetExchangeInfoImpl()
        {
            var resultExchangeInfo = await client.GetMarketsAsync().ConfigureAwait(true);
            if (resultExchangeInfo.Success)
            {
                GetExchangeInfoElapsed = resultExchangeInfo.ElapsedMilliseconds;
                var symbols = resultExchangeInfo.Data.Select(CreateSymbolInformation);
                ProcessExchangeInfo(symbols);
                UpdateStatus($"Number of trading pairs: {symbols.Count()}");
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetExchangeInfo: {resultExchangeInfo.Error.ToString()}");
            }
        }

        protected override async Task GetTickersImpl()
        {
            if (Markets.Count() < 1)
                return;
            var resultTickers = await client.GetPriceTickerAsync().ConfigureAwait(true);
            if (resultTickers.Success)
            {
                GetTickersElapsed = resultTickers.ElapsedMilliseconds;
                var tickers = resultTickers.Data.Select(ToPriceTicker);
                ProcessPriceTicker(tickers);
                UpdateStatus(ServerStatus);
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetTickers: {resultTickers.Error.ToString()}");
            }
        }

        protected override async Task GetTradesImpl()
        {
            if (CurrentSymbol == null)
                return;
            var si = CurrentSymbolInformation;
            var resultTrades = await client.GetMarketHistoryAsync(si.Symbol).ConfigureAwait(true);
            if (resultTrades.Success)
            {
                GetTradesElapsed = resultTrades.ElapsedMilliseconds;
                string baseTradeId = resultTrades.Data.id.ToString();
                resultTrades.Data.data.ForEach(x => x.id = x.id.Replace(baseTradeId, string.Empty));
                var trades = resultTrades.Data.data.Select(x => ToPublicTrade(x, si)).Reverse().ToList();// pair is <symbol,trades>
                ProcessPublicTrades(trades);
                UpdateStatus(ServerStatus);
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetTrades: {resultTrades.Error.ToString()}");
            }
        }

        protected override async Task GetDepthImpl()
        {
            if (CurrentSymbol == null)
                return;
            var si = CurrentSymbolInformation;
            var resultDepth = await client.GetDepthAsync(si.Symbol).ConfigureAwait(true);
            if (resultDepth.Success)
            {
                GetDepthElapsed = resultDepth.ElapsedMilliseconds;

                var depth = resultDepth.Data;
                var asks = depth.asks.Select(a => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = Math.Round(a[0], si.PriceDecimals), Quantity = Math.Round(a[1], si.QuantityDecimals), Side = TradeSide.Sell });
                var bids = depth.bids.Select(b => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = Math.Round(b[0], si.PriceDecimals), Quantity = Math.Round(b[1], si.QuantityDecimals), Side = TradeSide.Buy });
                var items = asks.Take(OrderBookMaxItemCount).Reverse().Concat(bids.Take(OrderBookMaxItemCount));
                ProcessOrderBook(items);
                UpdateStatus(ServerStatus);
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetDepth: {resultDepth.Error.ToString()}");
            }
        }












        public override string ExchangeName => "Huobi";
        protected override string DefaultMarket => "BTC";
        //protected override bool HasMarketSummariesPull => true;
        //protected override bool HasMarketSummariesPush => false;
        //protected override bool HasTradesPull => false;
        //protected override bool HasTradesPush => true;
        //protected override bool HasOrderBookPull => false;
        //protected override bool HasOrderBookPush => true;

		public override bool FilterByMarket(string symbol, string market)
        {
            return symbol.EndsWith(market, StringComparison.CurrentCultureIgnoreCase);
        }

		public override bool FilterByAsset(string symbol, string asset)
        {
            return symbol.StartsWith(asset, StringComparison.CurrentCultureIgnoreCase);
        }

        public HuobiViewModel()
        {
        }

        protected async override Task<IEnumerable<SymbolInformation>> GetMarketsAsync()
        {
            var result = await client.GetMarketsAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(CreateSymbolInformation);
            else
                return Enumerable.Empty<SymbolInformation>();
        }

        protected async override Task<IEnumerable<PriceTicker>> GetTickersAsync()
        {
            var result = await client.GetPriceTickerAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(ToPriceTicker);
            else
                return Enumerable.Empty<PriceTicker>();
        }

        //protected override void SubscribeMarketData()
        //{
        //    var sub24hrPriceTickerWs = client.ObserveMarketSummaries(Markets.Select(x => x.Symbol)).Publish();
        //    sub24hrPriceTickerWs.Subscribe(
        //        (Huobi.WsTick ticker) =>
        //        {
        //            OnRefreshMarketSummary2(ToPriceTicker(ticker));
        //        }).DisposeWith(Disposables);
        //    sub24hrPriceTickerWs.Connect().DisposeWith(Disposables);
        //}

        protected async override Task<IEnumerable<PublicTrade>> GetPublicTradesAsync(string market, int limit)
        {
            var result = await client.GetMarketHistoryAsync(market);
            if (result.Success)
            {
                var si = GetSymbolInformation(market);
                string baseTradeId = result.Data.id.ToString();
                result.Data.data.ForEach(x => x.id = x.id.Replace(baseTradeId, string.Empty));
                var trades = result.Data.data.Select(x => ToPublicTrade(x, si)).Reverse().ToList();
                return trades;
            }
            else
                return Enumerable.Empty<PublicTrade>();
        }

        protected async override Task<IEnumerable<OrderBookEntry>> GetOrderBookAsync(string market, int limit)
        {
            var result = await client.GetDepthAsync(market).ConfigureAwait(false);
            if (result.Success)
            {
                var depth = result.Data;
                var si = GetSymbolInformation(market);
                var asks = depth.asks.Select(a => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = Math.Round(a[0], si.PriceDecimals), Quantity = Math.Round(a[1], si.QuantityDecimals), Side = TradeSide.Sell });
                var bids = depth.bids.Select(b => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = Math.Round(b[0], si.PriceDecimals), Quantity = Math.Round(b[1], si.QuantityDecimals), Side = TradeSide.Buy });
                return asks.Take(limit).Reverse().Concat(bids.Take(limit));
            }
            else
                return Enumerable.Empty<OrderBookEntry>();
        }

        //public override IObservable<PriceTicker> SubscribeMarketSummaries(IEnumerable<string> symbols, string period)
        //      {
        //          var obs = Observable.FromEventPattern<HuobiApiClient.DetailTickHandler, string, Huobi.WsTick>(h => client.DetailTick += h, h => client.DetailTick -= h);
        //          obs = obs.Buffer(TimeSpan.FromSeconds(1)).SelectMany(selector => selector.ToObservable());
        //          client.SubscribeMarketSummariesAsync(symbols);
        //          return obs.Select(
        //              x => new PriceTicker()
        //              {
        //                  Symbol = x.Sender,
        //                  LastPrice = x.EventArgs.close,
        //                  PriceChangePercent = Math.Round(((x.EventArgs.close / x.EventArgs.open) - 1M) * 100M, 2),
        //                  Volume = x.EventArgs.vol
        //              });
        //      }

        internal SymbolInformation CreateSymbolInformation(Huobi.Market market)
        {
            var cmcEntry = GetCmcEntry(market.baseCurrency);
            return new SymbolInformation()
            {
                BaseAsset = market.baseCurrency.ToUpper(),
                MaxPrice = 0,
                MinPrice = 0,
                MinQuantity = 0,
                QuantityDecimals = market.amountPrecision,
                PriceDecimals = market.pricePrecision,
                QuoteAsset = market.quoteCurrency.ToUpper(),
                Symbol = market.symbol,
                CmcId = cmcEntry != null ? cmcEntry.id : -1,
                CmcName = cmcEntry != null ? cmcEntry.name : market.baseCurrency.ToUpper(),
                CmcSymbol = cmcEntry != null ? cmcEntry.symbol : market.symbol
            };
        }

        internal PriceTicker ToPriceTicker(Huobi.WsTick x)
        {
            return new PriceTicker()
            {
                HighPrice = x.high,
                PriceChange = x.close - x.open,
                PriceChangePercent = CalcChangePercent(x.close, x.open),
                LastPrice = x.close,
                LowPrice = x.low,
                QuoteVolume = x.vol,
                Symbol = x.symbol,
                SymbolInformation = GetSymbolInformation(x.symbol),
                Volume = x.amount
            };
        }

        internal PriceTicker ToPriceTicker(Huobi.Kline x)
        {
            return new PriceTicker()
            {
                HighPrice = x.high,
                PriceChange = x.close - x.open,
                PriceChangePercent = CalcChangePercent(x.close, x.open),
                LastPrice = x.close,
                LowPrice = x.low,
                QuoteVolume = x.vol,
                Symbol = x.symbol,
                SymbolInformation = GetSymbolInformation(x.symbol),
                Volume = x.amount
            };
        }

        internal PublicTrade ToPublicTrade(Huobi.Trade x, SymbolInformation si)
        {
            return new PublicTrade(si)
            {
                Id = long.Parse(x.id),
                Price = x.price,
                Quantity = x.amount,
                Side = x.direction == "buy" ? TradeSide.Buy : TradeSide.Sell,
                Time = x.ts.FromUnixTimestamp()
            };
        }

        //List<string> symbols = new List<string>();
        HuobiApiClient client = new HuobiApiClient();

        internal void UpdateStatus(string msg)
        {
            Status = $"{ExchangeName}: {msg}.";
        }
    }
}
