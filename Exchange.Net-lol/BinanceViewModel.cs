using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Exchange.Net
{
    public class BinanceViewModel : ExchangeViewModel
    {


        public override string ExchangeName => "Binance";

        protected override string DefaultMarket => "BTC";
        protected override bool HasMarketSummariesPull => false;
        protected override bool HasMarketSummariesPush => true;
		protected override bool HasTradesPush => true;
        protected override bool HasOrderBookPull => true;

        public override async Task<List<PublicTrade>> GetTrades(string symbol, int limit = 50)
        {
            var trades = await client.GetRecentTradesAsync(symbol, limit).ConfigureAwait(false);
            UpdateStatus();
            //var trades = await client.GetAggTradesAsync(symbol, limit).ConfigureAwait(false);
            return trades.Select(
                t => new PublicTrade()
                {
                    Id = t.id,
                    Price = t.price,
                    Quantity = t.qty,
                    Side = t.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
                    Symbol = symbol,
                    Timestamp = t.time.FromUnixTimestamp()
                }).OrderByDescending(x => x.Id).ToList();
        }

		public override async Task<List<SymbolInformation>> GetMarkets()
        {
            var tmp = await client.GetExchangeInfoAsync().ConfigureAwait(false);
            UpdateStatus();
			//return tmp.symbols.Where(p => (p.quoteAsset == "BTC" || p.quoteAsset == "USDT") && p.status == "TRADING").Select(CreateSymbolInformation).OrderBy(si => si.Symbol).ToList();
            return tmp.symbols.Where(p => p.status == "TRADING").Select(CreateSymbolInformation).OrderBy(si => si.Symbol).ToList();
        }

        SymbolInformation CreateSymbolInformation(Binance.Market market)
        {
            var priceFilter = market.filters.SingleOrDefault((f) => f.filterType == Binance.FilterType.PRICE_FILTER.ToString());
            var lotSizeFilter = market.filters.SingleOrDefault((f) => f.filterType == Binance.FilterType.LOT_SIZE.ToString());
            var cmcEntry = cmc_listing.FirstOrDefault(x => CorrectAssetName(market.baseAsset).Equals(x.symbol, StringComparison.CurrentCultureIgnoreCase));
            Debug.WriteLineIf(cmcEntry == null, $"Missing {market.baseAsset}");
            return new SymbolInformation()
            {
                BaseAsset = market.baseAsset,
                QuoteAsset = market.quoteAsset,
                Symbol = market.symbol,
                Status = market.status,
                MinPrice = priceFilter.minPrice,
                MaxPrice = priceFilter.maxPrice,
                PriceDecimals = DigitsCount(priceFilter.tickSize),
                MinQuantity = priceFilter.minQty,
                MaxQuantity = priceFilter.maxQty,
                QuantityDecimals = DigitsCount(lotSizeFilter.stepSize),
                CmcId = cmcEntry != null ? cmcEntry.id : -1,
                CmcName = cmcEntry != null ? cmcEntry.name : market.baseAsset,
                CmcSymbol = cmcEntry != null ? cmcEntry.symbol : market.symbol
            };
        }

        internal static string CorrectAssetName(string asset)
        {
            return asset.Replace("BCC", "BCH").Replace("YOYO", "YOYOW").Replace("BQX", "ETHOS").Replace("IOTA", "MIOTA");
        }

		public override async Task<List<PriceTicker>> GetMarketSummaries()
        {
            if (DateTime.Now > lastGet24hrPriceTicker.AddSeconds(20))
            {
                var tmp = await client.Get24hrPriceTickerAsync().ConfigureAwait(false);
                lastGet24hrPriceTicker = DateTime.Now;
                UpdateStatus();
                return tmp.Select(
                    p => new PriceTicker()
                    {
                        LastPrice = p.lastPrice,
                        PriceChangePercent = p.priceChangePercent,
                        Symbol = p.symbol,
                        Volume = p.quoteVolume
                    }).ToList();
            }
            else
            {
                var tmp = await client.GetPriceTickerAsync();
                UpdateStatus();
                return tmp.Select(
                    p => new PriceTicker()
                    {
                        LastPrice = p.price,
                        Symbol = p.symbol
                    }).ToList();
            }
        }

		public override IObservable<PriceTicker> SubscribeMarketSummaries(IEnumerable<string> symbols, string period)
        {
            if (period == "24hr")
            {
                var obs2 = client.Get24hrPriceTickerAsync().ToObservable().SelectMany(x => x.ToObservable());
                var obs = client.SubscribeMarketSummariesAsync(symbols);
                UpdateStatus();
                return obs2.Select(ToPriceTicker).Concat(obs.Select(ToPriceTicker));
            }
            else
            {
                var obs2 = client.GetPriceTickerAsync().ToObservable().SelectMany(x => x.ToObservable());
                var obs = client.SubscribeKlinesAsync(symbols, period);
                UpdateStatus();
                return obs2.Select(ToPriceTicker).Concat(obs.Select(ToPriceTicker));
            }
        }

		public override async Task<List<Balance>> GetBalances()
        {
            var tmp = await client.GetAccountInfoAsync().ConfigureAwait(false);
            UpdateStatus();
            return tmp.balances.Select(
                p => new Balance(p.asset, UsdAssets.Contains(p.asset))
                {
                    Free = p.free,
                    Locked = p.locked
                }).ToList();
        }

        public override async Task<List<Transfer>> GetDeposits(string asset = null)
        {
            var tmp = await client.GetDepositHistoryAsync(asset).ConfigureAwait(false);
            UpdateStatus();
            return tmp.depositList.Select(
                x => new Transfer()
                {
                    Address = x.address,
                    //Comission = x.TxCost,
                    Asset = x.asset,
                    Quantity = x.amount,
                    Status = Code2DepositStatus(x.status),
                    Timestamp = x.insertTime.FromUnixTimestamp(),
                    Type = TransferType.Deposit
                }).ToList();
        }

        public override async Task<List<Transfer>> GetWithdrawals(string asset = null)
        {
            var tmp = await client.GetWithdrawHistoryAsync(asset).ConfigureAwait(false);
            UpdateStatus();
            return tmp.withdrawList.Select(
                x => new Transfer()
                {
                    Address = x.address,
                    //Comission = x.TxCost,
                    Asset = x.asset,
                    Quantity = x.amount,
                    Status = Code2WithdrawalStatus(x.status),
                    Timestamp = x.applyTime.FromUnixTimestamp(),
                    Type = TransferType.Withdrawal
                }).ToList();
        }

        private static TransferStatus Code2DepositStatus(int code)
        {
            switch (code)
            {
                case 0:
                    return TransferStatus.Pending;
                case 1:
                    return TransferStatus.Completed;
            }
            return TransferStatus.Undefined;
        }

        private static TransferStatus Code2WithdrawalStatus(int code)
        {
            switch (code)
            {
                case 0:
                    return TransferStatus.EmailSent;
                case 1:
                    return TransferStatus.Cancelled;
                case 2:
                    return TransferStatus.AwaitingApproval;
                case 3:
                    return TransferStatus.Rejected;
                case 4:
                    return TransferStatus.Processing;
                case 5:
                    return TransferStatus.Failed;
                case 6:
                    return TransferStatus.Completed;
            }
            return TransferStatus.Undefined;
        }

		long lastTradeId = 0;
		string lastTradeSymbol = null;

		private IEnumerable<Binance.Trade> GetTradesDiff(string symbol)
        {
			if (symbol != lastTradeSymbol)
			{
				lastTradeId = 0;
				lastTradeSymbol = symbol;
			}
			var trades = client.GetRecentTrades(symbol, TradesMaxItemCount);
            trades.RemoveAll(x => x.id <= lastTradeId);
            if (trades.Count > 0)
				lastTradeId = trades.Last().id;
            return trades.AsEnumerable();
        }

		public override IObservable<PublicTrade> SubscribeTrades()
        {
			//return Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).SelectMany<long, PublicTrade>(selector => GetTradesDiff(CurrentSymbol).Select(
				//t => new PublicTrade()
				//{
					//Id = t.id,
					//Price = t.price,
					//Quantity = t.qty,
					//Side = t.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
					//Symbol = CurrentSymbol,
					//Timestamp = t.time.FromUnixTimestamp()
			    //}));
        
			//var obs = Observable.FromEventPattern<BinanceApiClient.PublicTradeHandler, object, Binance.WsTrade>(h => client.Trade += h, h => client.Trade -= h);
            var obs = client.SubscribePublicTradesAsync(CurrentSymbol, OrderBookMaxItemCount);
            UpdateStatus();
            return obs.Select(x => new PublicTrade() {
                Id = x.tradeId,
				Symbol = x.symbol,
				Price = x.price,
				Quantity = x.quantity,
				Side = x.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
				Timestamp = x.tradeTime.FromUnixTimestamp()
            });
		}

        public override OrderBook GetOrderBook(string symbol, int limit = 25)
        {
            var depth = client.GetDepth(symbol, limit);
            UpdateStatus();
            var book = new OrderBook();
            foreach (var bid in depth.bids)
            {
                book.Bids.Add(new OrderBookEntry() { Price = decimal.Parse(bid[0]), Quantity = decimal.Parse(bid[1]), Side = TradeSide.Buy });
            }
            foreach (var ask in depth.asks)
            {
                book.Asks.Add(new OrderBookEntry() { Price = decimal.Parse(ask[0]), Quantity = decimal.Parse(ask[1]), Side = TradeSide.Sell });
            }
            return book;
        }

        public override IObservable<OrderBookEntry> SubscribeOrderBook(string symbol)
        {
            var obs = client.SubscribeOrderBook(CurrentSymbol);
            UpdateStatus();
            return obs.SelectMany(ConvertDepth);
        }

        private IEnumerable<OrderBookEntry> ConvertDepth(Binance.WsDepth depth)
        {
            return depth.bids.Select(y => new OrderBookEntry()
                {
                    Price = decimal.Parse(y[0]),
                    Quantity = decimal.Parse(y[1]),
                    Side = TradeSide.Buy
                }).Concat(depth.asks.Select(y => new OrderBookEntry()
                {
                    Price = decimal.Parse(y[0]),
                    Quantity = decimal.Parse(y[1]),
                    Side = TradeSide.Sell
                })
            );
        }

        public async override Task<List<Order>> GetOpenOrders()
        {
            var orders = await client.GetOpenOrdersAsync();
            UpdateStatus();
            return orders.Select((arg) => new Order()
            {
                Price = arg.price,
                Quantity = arg.origQty,
                Side = arg.side == "BUY" ? TradeSide.Buy : TradeSide.Sell,
                StopPrice = arg.stopPrice,
                Symbol = arg.symbol,
                Timestamp = arg.time.FromUnixTimestamp(),
                Type = arg.type
            }).OrderByDescending(x => x.Timestamp).ToList();
        }

        public override bool FilterByMarket(string symbol, string market)
        {
            return symbol.EndsWith(market, StringComparison.CurrentCultureIgnoreCase);
        }
		public override bool FilterByAsset(string symbol, string asset)
        {
            return symbol.StartsWith(asset, StringComparison.CurrentCultureIgnoreCase);
        }

        BinanceApiClient client = new BinanceApiClient();
        DateTime lastGet24hrPriceTicker = DateTime.MinValue;

        internal void UpdateStatus()
        {
            Status = $"Binance: Weight is {client.Weight}, expiration in {client.WeightReset.TotalSeconds} secs.";
        }

        internal static int DigitsCount(decimal value)
        {
            decimal factor = 1m;
            for (int idx = 1; idx <= 8; idx += 1)
            {
                factor = factor * 10m;
                if (factor * value == 1m)
                    return idx;
            }
            return 0;
        }
    
        internal static PriceTicker ToPriceTicker(Binance.PriceTicker ticker)
        {
            return new PriceTicker()
            {
                LastPrice = ticker.price,
                Symbol = ticker.symbol
            };
        }

        internal static PriceTicker ToPriceTicker(Binance.PriceTicker24hr ticker)
        {
            return new PriceTicker()
            {
                LastPrice = ticker.lastPrice,
                PriceChangePercent = ticker.priceChangePercent,
                Symbol = ticker.symbol,
                Volume = ticker.quoteVolume
            };
        }

        internal static PriceTicker ToPriceTicker(Binance.WsPriceTicker24hr ticker)
        {
            return new PriceTicker()
            {
                Symbol = ticker.symbol,
                LastPrice = ticker.lastPrice,
                PriceChangePercent = ticker.priceChangePercent,
                Volume = ticker.quoteVolume
            };
        }

        internal static PriceTicker ToPriceTicker(Binance.WsCandlestick ticker)
        {
            return new PriceTicker()
            {
                Symbol = ticker.symbol,
                LastPrice = ticker.kline.closePrice,
                PriceChangePercent = (100m * ticker.kline.closePrice / ticker.kline.openPrice) - 100m,
                Volume = ticker.kline.quoteVolume,
                BuyVolume = ticker.kline.takerBuyQuoteVolume
            };
        }

        internal static PublicTrade ToPublicTrade(Binance.Trade x)
        {
            return new PublicTrade()
            {
                Id = x.id,
                Price = x.price,
                Quantity = x.qty,
                Side = x.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
                Timestamp = x.time.FromUnixTimestamp()
            };
        }

        // *****************************
        // V2
        //
        public BinanceViewModel()
        {
            Initialize();
        }

        public override void Activate(bool active = true)
        {
            Disposables.Dispose();
        }

        private async void Initialize()
        {
            ApiResult<Binance.ServerTime> crServerTime = null;
            ApiResult<Binance.ExchangeInfo> crExchangeInfo = null;
            ApiResult<Binance.PriceTicker24hr[]> crPriceTicker = null;

            var tasks = new List<Task>();
            tasks.Add(new Task(() => { crServerTime = client.GetServerTime2(); }));
            tasks.Add(new Task(() => { crExchangeInfo = client.GetExchangeInfo2(); }));
            tasks.Add(new Task(() => { crPriceTicker = client.Get24hrPriceTicker2(); }));
            tasks.ForEach(t => t.Start());

            await Task.WhenAll(tasks).ConfigureAwait(false);
            UpdateStatus();

            if (crServerTime.Success)
                ServerTime = crServerTime.Data.serverTime.FromUnixTimestamp();
            if (crExchangeInfo.Success)
                ProcessExchangeInfo(crExchangeInfo.Data.symbols.Where(sym => sym.status != "BREAK").Select(CreateSymbolInformation));
            if (crPriceTicker.Success)
                Process24hrPriceTicker(crPriceTicker.Data.Select(ToPriceTicker));

            var subServerTime = Observable.Interval(TimeSpan.FromSeconds(2)).Publish();
            var subExchangeInfo = Observable.Interval(TimeSpan.FromMinutes(5)).Publish();
            var sub24hrPriceTicker = Observable.Interval(TimeSpan.FromSeconds(10)).Publish();
            var subCurrentSymbol = this.WhenAnyValue(vm => vm.CurrentSymbol).Where(id => id != null).DistinctUntilChanged().Publish();

            subServerTime.Subscribe(
               (long x) =>
               {
                   var result = client.GetServerTime2();
                   UpdateStatus();
                   if (result.Success)
                   {
                       ServerTime = result.Data.serverTime.FromUnixTimestamp();
                   }
               }).DisposeWith(Disposables);

            subExchangeInfo.Subscribe(
                (long x) =>
                {
                    var result = client.GetExchangeInfo2();
                    UpdateStatus();
                    if (result.Success)
                    {
                        ProcessExchangeInfo(result.Data.symbols.Select(CreateSymbolInformation));
                    }
                }).DisposeWith(Disposables);

            sub24hrPriceTicker.Subscribe(
                (long x) =>
                {
                    var result = client.Get24hrPriceTicker2();
                    UpdateStatus();
                    if (result.Success)
                        Process24hrPriceTicker(result.Data.Select(ToPriceTicker));
                }).DisposeWith(Disposables);

            subCurrentSymbol.Subscribe(
                (string symbol) =>
                {
                    var obs = Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(10));
                    PublicTradesHandle.Disposable = obs.Subscribe(
                        (long x) =>
                        {
                            var result = client.GetRecentTrades2(symbol, TradesMaxItemCount);
                            if (result.Success)
                            {
                                var trades = result.Data.Select(ToPublicTrade).Reverse().ToList();
                                trades.ForEach(t => t.Symbol = symbol);
                                ProcessPublicTrades(trades);
                            }
                        });
                    obs = Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(2));
                    DepthHandle.Disposable = obs.Subscribe(
                        (long x) =>
                        {
                            var result = client.GetDepth2(symbol, OrderBookMaxItemCount);
                            if (result.Success)
                            {
                                var depth = result.Data;
                                var asks = depth.asks.Select(a => new OrderBookEntry() { Price = decimal.Parse(a[0]), Quantity = decimal.Parse(a[1]), Side = TradeSide.Sell });
                                var bids = depth.bids.Select(b => new OrderBookEntry() { Price = decimal.Parse(b[0]), Quantity = decimal.Parse(b[1]), Side = TradeSide.Buy });
                                ProcessOrderBook(asks.Reverse().Concat(bids));
                            }
                        });
                }).DisposeWith(Disposables);

            //subServerTime.Connect().DisposeWith(Disposables);
            //subExchangeInfo.Connect().DisposeWith(Disposables);
            //sub24hrPriceTicker.Connect().DisposeWith(Disposables);
            //subCurrentSymbol.Connect().DisposeWith(Disposables);
        }

        protected override bool IsValidMarket(SymbolInformation si)
        {
            return base.IsValidMarket(si) && si.Status != Binance.MarketStatus.BREAK.ToString();
        }
    }
}
