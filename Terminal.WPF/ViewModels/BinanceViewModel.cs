using DynamicData;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Exchange.Net
{
    public partial class BinanceViewModel : ExchangeViewModel
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
            var resultExchangeInfo = await client.GetExchangeInfoAsync().ConfigureAwait(false);
            if (resultExchangeInfo.Success)
            {
                GetExchangeInfoElapsed = resultExchangeInfo.ElapsedMilliseconds;
                currentExchangeInfo = resultExchangeInfo.Data;
                var symbols = resultExchangeInfo.Data.symbols.Select(CreateSymbolInformation);
                ProcessExchangeInfo(symbols);
                var serverTime = resultExchangeInfo.Data.serverTime.FromUnixTimestamp();
                UpdateStatus($"Server Time: {serverTime}; Number of trading pairs: {symbols.Count()}");
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetExchangeInfo: {resultExchangeInfo.Error.ToString()}");
            }
        }

        private DateTime ticker24hrLastRun = DateTime.MinValue;

        protected override async Task GetTickersImpl()
        {
            if (Markets.Count() < 1)
                return;
            if (ticker24hrLastRun <= DateTime.Now)
            {
                await GetTickers24hr();
            }
            else
            {
                await GetTickersAlt();
            }
        }

        private async Task GetTickers24hr()
        {
            var resultTickers = await client.Get24hrPriceTickerAsync().ConfigureAwait(false);
            if (resultTickers.Success)
            {
                ticker24hrLastRun = DateTime.Now.AddSeconds(6);
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

        private async Task GetTickersAlt()
        {
            var priceTickersTask = client.GetPriceTickerAsync();
            var bookTickersTask = client.GetBookTickerAsync();
            await Task.WhenAll(priceTickersTask, bookTickersTask).ConfigureAwait(false);
            if (priceTickersTask.IsCompleted && bookTickersTask.IsCompleted)
            {
                if (priceTickersTask.Result.Success && bookTickersTask.Result.Success)
                {
                    GetTickersElapsed = Math.Max(priceTickersTask.Result.ElapsedMilliseconds, bookTickersTask.Result.ElapsedMilliseconds);
                    var tickers = priceTickersTask.Result.Data.Select(ToPriceTicker);
                    foreach (var ticker in tickers)
                    {
                        var bookTicker = bookTickersTask.Result.Data.SingleOrDefault(x => x.symbol == ticker.Symbol);
                        if (bookTicker != null)
                        {
                            ticker.Bid = bookTicker.bidPrice;
                            ticker.Ask = bookTicker.askPrice;
                        }
                    }
                    ProcessPriceTicker(tickers);
                    UpdateStatus(ServerStatus);
                }
                else
                {
                    var err = priceTickersTask.Result.Success ? bookTickersTask.Result.Error : priceTickersTask.Result.Error;
                    UpdateStatus(ServerStatus, $"GetTickers: {err.ToString()}");
                }
            }
        }

        protected override async Task GetTradesImpl()
        {
            if (CurrentSymbol == null)
                return;
            var si = CurrentSymbolInformation;
            var resultTrades = await client.GetRecentTradesAsync(si.Symbol, TradesMaxItemCount).ConfigureAwait(true);
            if (resultTrades.Success)
            {
                GetTradesElapsed = resultTrades.ElapsedMilliseconds;
                var trades = resultTrades.Data.Select(x => ToPublicTrade(x, si)).Reverse().ToList();// pair is <symbol,trades>
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
            var resultDepth = await client.GetDepthAsync(si.Symbol, OrderBookMaxItemCount).ConfigureAwait(true);
            if (resultDepth.Success)
            {
                GetDepthElapsed = resultDepth.ElapsedMilliseconds;
                var depth = resultDepth.Data;
                var asks = depth.asks.Select(a => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = decimal.Parse(a[0]), Quantity = decimal.Parse(a[1]), Side = TradeSide.Sell });
                var bids = depth.bids.Select(b => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = decimal.Parse(b[0]), Quantity = decimal.Parse(b[1]), Side = TradeSide.Buy });
                ProcessOrderBook(asks.Reverse().Concat(bids));
                UpdateStatus(ServerStatus);
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetDepth: {resultDepth.Error.ToString()}");
            }
        }

        protected override async Task<Balance> GetAssetBalance(string asset)
        {
            var result = await client.GetAccountInfoAsync();
            //var result = await Task.FromResult(client.GetAccountInfoOffline());
            if (result.Success)
            {
                var b = result.Data.balances.SingleOrDefault(x => x.asset == asset);
                return new Balance(asset) { Free = b.free, Locked = b.locked };
            }
            else
                throw new Exception(result.Error.ToString());
        }

        protected override async Task<List<Balance>> GetBalancesAsync()
        {
            var result = await client.GetAccountInfoAsync();
            if (result.Success)
            {
                return result.Data.balances.Select(
                    p => new Balance(p.asset, UsdAssets.Contains(p.asset))
                    {
                        Free = p.free,
                        Locked = p.locked
                    }).ToList();
            }
            else
                throw new Exception(result.Error.ToString());
        }

        private Binance.ExchangeInfo currentExchangeInfo;

        protected override async Task<SymbolInformation> GetFullSymbolInformation()
        {
            var si = CurrentSymbolInformation;
            si.QuoteAssetBalance = await GetAssetBalance(si.QuoteAsset);
            si.QuoteAssetBalance.Free = Math.Round(si.QuoteAssetBalance.Free, si.PriceDecimals);
            si.PriceTicker = GetPriceTicker(si.Symbol);
            var filter = currentExchangeInfo.symbols.SingleOrDefault(x => x.symbol == si.Symbol).filters.SingleOrDefault(x => x.filterType == Binance.FilterType.PERCENT_PRICE.ToString());
            if (filter != null)
            {
                if (filter != null)
                {
                    si.MaxPrice = si.PriceTicker.WeightedAveragePrice * filter.multiplierUp;
                    si.MinPrice = si.PriceTicker.WeightedAveragePrice * filter.multiplierDown;
                }
            }
            return si;
        }

        protected override IObservable<PriceTicker> ObserveTickers123()
        {
            return client.SubscribeMarketSummariesAsync(null).Select(ToPriceTicker);
        }


        public override string ExchangeName => "Binance";

        protected override string DefaultMarket => "BTC";
        protected override bool HasMarketSummariesPull => false;
        protected override bool HasMarketSummariesPush => true;
        public override int OrderBookMaxItemCount { get; set; } = 20;
        public override int[] OrderBookSizeList => new int[] { 5, 10, 20, 50, 100, 500, 1000 };
        public override int[] RecentTradesSizeList => new int[] { 5, 10, 20, 50, 100, 500, 1000 };
        // NOTE: below is SYMBOL dependant.
        public override string[] OrderTypes => new string[] { "LIMIT", "LIMIT_MAKER", "MARKET", "STOP_LOSS_LIMIT", "TAKE_PROFIT_LIMIT" };
        //5, 10, 20, 50, 100, 500, 1000
        protected override bool HasTradesPush => true;
        protected override bool HasOrderBookPush => true;

        public DateTime ServerTime
        {
            get { return this.serverTime; }
            set { this.RaiseAndSetIfChanged(ref this.serverTime, value); }
        }

        public ICommand GetServerTimeCommand => getServerTimeCommand;
        //      public override async Task<List<PublicTrade>> GetTrades(string symbol, int limit = 50)
        //      {
        //          var trades = await client.GetRecentTradesAsync(symbol, limit).ConfigureAwait(false);
        //          UpdateStatus();
        //          //var trades = await client.GetAggTradesAsync(symbol, limit).ConfigureAwait(false);
        //          return trades.Select(
        //              t => new PublicTrade()
        //              {
        //                  Id = t.id,
        //                  Price = t.price,
        //                  Quantity = t.qty,
        //                  Side = t.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
        //                  Symbol = symbol,
        //                  Timestamp = t.time.FromUnixTimestamp()
        //              }).OrderByDescending(x => x.Id).ToList();
        //      }

        //public override async Task<List<SymbolInformation>> GetMarkets()
        //      {
        //          var tmp = await client.GetExchangeInfoAsync().ConfigureAwait(false);
        //          UpdateStatus();
        //	//return tmp.symbols.Where(p => (p.quoteAsset == "BTC" || p.quoteAsset == "USDT") && p.status == "TRADING").Select(CreateSymbolInformation).OrderBy(si => si.Symbol).ToList();
        //          return tmp.symbols.Where(p => p.status == "TRADING").Select(CreateSymbolInformation).OrderBy(si => si.Symbol).ToList();
        //      }

        SymbolInformation CreateSymbolInformation(Binance.Market market)
        {
            var priceFilter = market.filters.SingleOrDefault((f) => f.filterType == Binance.FilterType.PRICE_FILTER.ToString());
            var lotSizeFilter = market.filters.SingleOrDefault((f) => f.filterType == Binance.FilterType.LOT_SIZE.ToString());
            var minNotionalFilter = market.filters.SingleOrDefault((f) => f.filterType == Binance.FilterType.MIN_NOTIONAL.ToString());
            var cmcEntry = GetCmcEntry(market.baseAsset);
            return new SymbolInformation()
            {
                BaseAsset = market.baseAsset,
                QuoteAsset = market.quoteAsset,
                Symbol = market.symbol,
                Status = market.status,
                MinPrice = priceFilter.minPrice,
                MaxPrice = priceFilter.maxPrice,
                TickSize = priceFilter.tickSize,
                PriceDecimals = DigitsCount(priceFilter.tickSize),
                MinQuantity = lotSizeFilter.minQty,
                MaxQuantity = lotSizeFilter.maxQty,
                StepSize = lotSizeFilter.stepSize,
                QuantityDecimals = DigitsCount(lotSizeFilter.stepSize),
                MinNotional = minNotionalFilter.minNotional,
                CmcId = cmcEntry != null ? cmcEntry.id : -1,
                CmcName = cmcEntry != null ? cmcEntry.name : market.baseAsset,
                CmcSymbol = cmcEntry != null ? cmcEntry.symbol : market.symbol
            };
        }

		//public override async Task<List<PriceTicker>> GetMarketSummaries()
  //      {
  //          if (DateTime.Now > lastGet24hrPriceTicker.AddSeconds(20))
  //          {
  //              var tmp = await client.Get24hrPriceTickerAsync().ConfigureAwait(false);
  //              lastGet24hrPriceTicker = DateTime.Now;
  //              UpdateStatus();
  //              return tmp.Select(
  //                  p => new PriceTicker()
  //                  {
  //                      LastPrice = p.lastPrice,
  //                      PriceChangePercent = p.priceChangePercent,
  //                      Symbol = p.symbol,
  //                      Volume = p.quoteVolume
  //                  }).ToList();
  //          }
  //          else
  //          {
  //              var tmp = await client.GetPriceTickerAsync();
  //              UpdateStatus();
  //              return tmp.Select(
  //                  p => new PriceTicker()
  //                  {
  //                      LastPrice = p.price,
  //                      Symbol = p.symbol
  //                  }).ToList();
  //          }
  //      }

		//public override IObservable<PriceTicker> SubscribeMarketSummaries(IEnumerable<string> symbols, string period)
  //      {
  //          if (period == "24hr")
  //          {
  //              var obs2 = client.Get24hrPriceTickerAsync().ToObservable().SelectMany(x => x.ToObservable());
  //              var obs = client.SubscribeMarketSummariesAsync(symbols);
  //              UpdateStatus();
  //              return obs2.Select(ToPriceTicker).Concat(obs.Select(ToPriceTicker));
  //          }
  //          else
  //          {
  //              var obs2 = client.GetPriceTickerAsync().ToObservable().SelectMany(x => x.ToObservable());
  //              var obs = client.SubscribeKlinesAsync(symbols, period);
  //              UpdateStatus();
  //              return obs2.Select(ToPriceTicker).Concat(obs.Select(ToPriceTicker));
  //          }
  //      }

		//public override async Task<List<Balance>> GetBalances()
  //      {
  //          var tmp = await client.GetAccountInfoAsync().ConfigureAwait(false);
  //          UpdateStatus();
  //          return tmp.balances.Select(
  //              p => new Balance(p.asset, UsdAssets.Contains(p.asset))
  //              {
  //                  Free = p.free,
  //                  Locked = p.locked
  //              }).ToList();
  //      }

  //      public override async Task<List<Transfer>> GetDeposits(string asset = null)
  //      {
  //          var tmp = await client.GetDepositHistoryAsync(asset).ConfigureAwait(false);
  //          UpdateStatus();
  //          return tmp.depositList.Select(
  //              x => new Transfer()
  //              {
  //                  Address = x.address,
  //                  //Comission = x.TxCost,
  //                  Asset = x.asset,
  //                  Quantity = x.amount,
  //                  Status = Code2DepositStatus(x.status),
  //                  Timestamp = x.insertTime.FromUnixTimestamp(),
  //                  Type = TransferType.Deposit
  //              }).ToList();
  //      }

  //      public override async Task<List<Transfer>> GetWithdrawals(string asset = null)
  //      {
  //          var tmp = await client.GetWithdrawHistoryAsync(asset).ConfigureAwait(false);
  //          UpdateStatus();
  //          return tmp.withdrawList.Select(
  //              x => new Transfer()
  //              {
  //                  Address = x.address,
  //                  //Comission = x.TxCost,
  //                  Asset = x.asset,
  //                  Quantity = x.amount,
  //                  Status = Code2WithdrawalStatus(x.status),
  //                  Timestamp = x.applyTime.FromUnixTimestamp(),
  //                  Type = TransferType.Withdrawal
  //              }).ToList();
  //      }

        private static TransferStatus ToDepositStatus(int code)
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

        private static TransferStatus ToWithdrawalStatus(int code)
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

		//private IEnumerable<Binance.Trade> GetTradesDiff(string symbol)
  //      {
		//	if (symbol != lastTradeSymbol)
		//	{
		//		lastTradeId = 0;
		//		lastTradeSymbol = symbol;
		//	}
		//	var trades = client.GetRecentTrades(symbol, TradesMaxItemCount);
  //          trades.RemoveAll(x => x.id <= lastTradeId);
  //          if (trades.Count > 0)
		//		lastTradeId = trades.Last().id;
  //          return trades.AsEnumerable();
  //      }

		//public override IObservable<PublicTrade> SubscribeTrades()
  //      {
		//	//return Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).SelectMany<long, PublicTrade>(selector => GetTradesDiff(CurrentSymbol).Select(
		//		//t => new PublicTrade()
		//		//{
		//			//Id = t.id,
		//			//Price = t.price,
		//			//Quantity = t.qty,
		//			//Side = t.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
		//			//Symbol = CurrentSymbol,
		//			//Timestamp = t.time.FromUnixTimestamp()
		//	    //}));
        
		//	//var obs = Observable.FromEventPattern<BinanceApiClient.PublicTradeHandler, object, Binance.WsTrade>(h => client.Trade += h, h => client.Trade -= h);
  //          var obs = client.SubscribePublicTradesAsync(CurrentSymbol, OrderBookMaxItemCount);
  //          UpdateStatus();
  //          return obs.Select(x => new PublicTrade() {
  //              Id = x.tradeId,
		//		Symbol = x.symbol,
		//		Price = x.price,
		//		Quantity = x.quantity,
		//		Side = x.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
		//		Timestamp = x.tradeTime.FromUnixTimestamp()
  //          });
		//}

        //public override OrderBook GetOrderBook(string symbol, int limit = 25)
        //{
        //    var depth = client.GetDepth(symbol, limit);
        //    UpdateStatus();
        //    var book = new OrderBook();
        //    foreach (var bid in depth.bids)
        //    {
        //        book.Bids.Add(new OrderBookEntry() { Price = decimal.Parse(bid[0]), Quantity = decimal.Parse(bid[1]), Side = TradeSide.Buy });
        //    }
        //    foreach (var ask in depth.asks)
        //    {
        //        book.Asks.Add(new OrderBookEntry() { Price = decimal.Parse(ask[0]), Quantity = decimal.Parse(ask[1]), Side = TradeSide.Sell });
        //    }
        //    return book;
        //}

        //public override IObservable<OrderBookEntry> SubscribeOrderBook(string symbol)
        //{
        //    var obs = client.SubscribeOrderBook(CurrentSymbol);
        //    UpdateStatus();
        //    return obs.SelectMany(ConvertDepth);
        //}

        private IEnumerable<OrderBookEntry> ConvertDepth(Binance.WsDepth depth)
        {
            return depth.bids.Select(y => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals)
                {
                    Price = decimal.Parse(y[0]),
                    Quantity = decimal.Parse(y[1]),
                    Side = TradeSide.Buy
                }).Concat(depth.asks.Select(y => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals)
                {
                    Price = decimal.Parse(y[0]),
                    Quantity = decimal.Parse(y[1]),
                    Side = TradeSide.Sell
                })
            );
        }

        //public async override Task<List<Order>> GetOpenOrders()
        //{
        //    var orders = await client.GetOpenOrdersAsync();
        //    UpdateStatus();
        //    return orders.Select((arg) => new Order()
        //    {
        //        Price = arg.price,
        //        Quantity = arg.origQty,
        //        Side = arg.side == "BUY" ? TradeSide.Buy : TradeSide.Sell,
        //        StopPrice = arg.stopPrice,
        //        Symbol = arg.symbol,
        //        Timestamp = arg.time.FromUnixTimestamp(),
        //        Type = arg.type
        //    }).OrderByDescending(x => x.Timestamp).ToList();
        //}

        public override bool FilterByMarket(string symbol, string market)
        {
            return symbol.EndsWith(market, StringComparison.CurrentCultureIgnoreCase);
        }
		public override bool FilterByAsset(string symbol, string asset)
        {
            return symbol.StartsWith(asset, StringComparison.CurrentCultureIgnoreCase);
        }

        private readonly ReactiveCommand<long, DateTime> getServerTimeCommand;
        BinanceApiClient client = new BinanceApiClient();
        DateTime lastGet24hrPriceTicker = DateTime.MinValue;
        private DateTime serverTime;

        internal void UpdateStatus()
        {
            Status = $"Binance: Weight is {client.Weight}, expiration in {client.WeightReset.TotalSeconds} secs.";
        }

        internal PriceTicker ToPriceTicker(Binance.PriceTicker ticker)
        {
            return new PriceTicker()
            {
                LastPrice = ticker.price,
                Symbol = ticker.symbol,
                SymbolInformation = GetSymbolInformation(ticker.symbol)
            };
        }

        internal PriceTicker ToPriceTicker(Binance.PriceTicker24hr ticker)
        {
            var si = GetSymbolInformation(ticker.symbol);
            if (si.Status != "BREAK") return new PriceTicker()
            {
                Bid = ticker.bidPrice,
                Ask = ticker.askPrice,
                BuyVolume = 0m,
                HighPrice = Math.Round(ticker.highPrice, si.PriceDecimals),
                LastPrice = Math.Round(ticker.lastPrice, si.PriceDecimals),
                LowPrice = Math.Round(ticker.lowPrice, si.PriceDecimals),
                PriceChange = Math.Round(ticker.priceChange, si.PriceDecimals),
                PriceChangePercent = ticker.priceChangePercent,
                QuoteVolume = Math.Round(ticker.quoteVolume, si.PriceDecimals),
                Symbol = ticker.symbol,
                SymbolInformation = si,
                Volume = Math.Round(ticker.volume, si.QuantityDecimals),
                WeightedAveragePrice = Math.Round(ticker.weightedAvgPrice, si.PriceDecimals)
            };
            return new PriceTicker()
            {
                Symbol = ticker.symbol,
                SymbolInformation = si,
            };
        }

        internal PriceTicker ToPriceTicker(Binance.WsPriceTicker24hr ticker)
        {
            var si = GetSymbolInformation(ticker.symbol);
            return new PriceTicker()
            {
                Bid = ticker.bidPrice,
                Ask = ticker.askPrice,
                BuyVolume = 0m,
                HighPrice = Math.Round(ticker.highPrice, si.PriceDecimals),
                LastPrice = Math.Round(ticker.lastPrice, si.PriceDecimals),
                LowPrice = Math.Round(ticker.lowPrice, si.PriceDecimals),
                PriceChange = Math.Round(ticker.priceChange, si.PriceDecimals),
                PriceChangePercent = ticker.priceChangePercent,
                QuoteVolume = ticker.quoteVolume,
                Symbol = ticker.symbol,
                SymbolInformation = si,
                Volume = Math.Round(ticker.volume, si.QuantityDecimals),
                WeightedAveragePrice = Math.Round(ticker.weightedAvgPrice, si.PriceDecimals)
            };
        }

        protected static PriceTicker ToPriceTicker(Binance.WsCandlestick ticker)
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

        protected PublicTrade ToPublicTrade(Binance.AggTrade x, SymbolInformation si)
        {
            return new PublicTrade(si)
            {
                Id = x.id,
                Price = x.price,
                Quantity = x.qty,
                Side = !x.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
                Time = x.time.FromUnixTimestamp()
            };
        }

        protected PublicTrade ToPublicTrade(Binance.Trade x, SymbolInformation si)
        {
            return new PublicTrade(si)
            {
                Id = x.id,
                Price = x.price,
                Quantity = x.qty,
                Side = !x.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
                Time = x.time.FromUnixTimestamp()
            };
        }

        protected PublicTrade ToPublicTrade(Binance.WsTrade x)
        {
            return new PublicTrade(GetSymbolInformation(x.symbol))
            {
                Id = x.tradeId,
                Price = x.price,
                Quantity = x.quantity,
                Side = !x.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
                Time = x.tradeTime.FromUnixTimestamp()
            };
        }

        // *****************************
        // V2
        //

        public BinanceViewModel()
        {
            var defaultAccount = new ExchangeAccount("default", client);
            Accounts.AddOrUpdate(defaultAccount);
            CurrentAccount = defaultAccount;

            getServerTimeCommand = ReactiveCommand.CreateFromTask<long, DateTime>(x => GetServerTimeAsync());

            this.WhenActivated(registerDisposable =>
            {
                registerDisposable(getServerTimeCommand);

                //var resultExchangeInfo = client.GetExchangeInfoOffline();
                //currentExchangeInfo = resultExchangeInfo.Data;
                //var symbols = resultExchangeInfo.Data.symbols.Select(CreateSymbolInformation);
                //ProcessExchangeInfo(symbols);

                //var resultTickers = client.GetPriceTicker24hrOffline();
                //var tickers = resultTickers.Data.Select(ToPriceTicker);
                //ProcessPriceTicker(tickers);
            });
            //CurrentSymbol = "LTCBNB";
        }

        protected async Task<DateTime> GetServerTimeAsync()
        {
            var result = await client.GetServerTimeAsync().ConfigureAwait(false);
            if (result.Success)
            {
                client.SetServerTimeOffset(result.Data.serverTime, result.ElapsedMilliseconds);
                return result.Data.serverTime.FromUnixTimestamp(convertToLocalTime: false);
            }
            else
                return DateTime.UtcNow;
        }

        protected async override Task<IEnumerable<SymbolInformation>> GetMarketsAsync()
        {
            var result = await client.GetExchangeInfoAsync().ConfigureAwait(false);
            if (result.Success)
            {
                client.SetServerTimeOffset(result.Data.serverTime, result.ElapsedMilliseconds);
                return result.Data.symbols.Select(CreateSymbolInformation);
            }
            else
                return Enumerable.Empty<SymbolInformation>();
        }

        protected async override Task<IEnumerable<PriceTicker>> GetTickersAsync()
        {
            var result = await client.Get24hrPriceTickerAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(ToPriceTicker);
            else
                return Enumerable.Empty<PriceTicker>();
        }

        protected async override Task<IEnumerable<PublicTrade>> GetPublicTradesAsync(string market, int limit)
        {
            var result = await client.GetRecentTradesAsync(market, limit);
            if (result.Success)
            {
                var si = GetSymbolInformation(market);
                var trades = result.Data.Select(x => ToPublicTrade(x, si)).Reverse().ToList();
                return trades;
            }
            else
                return Enumerable.Empty<PublicTrade>();
        }

        protected async override Task<IEnumerable<OrderBookEntry>> GetOrderBookAsync(string market, int limit)
        {
            var result = await client.GetDepthAsync(market, limit);
            if (result.Success)
            {
                var depth = result.Data;
                var asks = depth.asks.Select(a => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = decimal.Parse(a[0]), Quantity = decimal.Parse(a[1]), Side = TradeSide.Sell });
                var bids = depth.bids.Select(b => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = decimal.Parse(b[0]), Quantity = decimal.Parse(b[1]), Side = TradeSide.Buy });
                return asks.Reverse().Concat(bids);
            }
            else
                return Enumerable.Empty<OrderBookEntry>();
        }

        protected override IObservable<IEnumerable<OrderBookEntry>> ObserveDepth(string market, int limit)
        {
            Debug.Print($"ObserveDepth() : {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            var obs = client.ObserveOrderBook(market).Select(OnOrderBook3);
            return obs;
        }

        protected override IObservable<IEnumerable<PublicTrade>> ObserveRecentTrades(string market, int limit)
        {
            Debug.Print($"ObserveRecentTrades() : {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            var obs = client.SubscribePublicTradesAsync(market).Select(OnRecentTrades);
            return obs;
        }

        System.Threading.SemaphoreSlim tradesLock = new System.Threading.SemaphoreSlim(1, 1);

        private IEnumerable<PublicTrade> OnRecentTrades(Binance.WsTrade trade)
        {
            Debug.Print($"OnRecentTrades() : {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            try
            {
                tradesLock.Wait();
                if (lastTradeId == 0)
                {
                    var trades = GetPublicTradesAsync(trade.symbol, TradesMaxItemCount).Result;
                    lastTradeId = trades.FirstOrDefault().Id;
                    return trades;
                }
                else if (trade.tradeId > lastTradeId)
                {
                    var newTrade = ToPublicTrade(trade);
                    lastTradeId = newTrade.Id;
                    return Enumerable.Repeat(newTrade, 1);
                }
                else
                {
                    Debug.Print($"Trade {trade.tradeId} dropped");
                }

                return Enumerable.Empty<PublicTrade>();
            }
            finally
            {
                tradesLock.Release();
            }
        }

        System.Threading.SemaphoreSlim _lock = new System.Threading.SemaphoreSlim(1, 1);
        private IEnumerable<OrderBookEntry> OnOrderBook3(Binance.WsDepth depth)
        {
            Debug.Print($"OnOrderBook3() : {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            try
            {
                _lock.Wait();
                if (OrderBook.lastUpdateId == 0)
                {
                    var result = client.GetDepthAsync(depth.symbol, OrderBookMaxItemCount).Result;
                    if (result.Success)
                    {
                        var tmp = Convert(result.Data, OrderBook.SymbolInformation);
                        OrderBook.lastUpdateId = result.Data.lastUpdateId;
                        return tmp;
                        //Debug.Print($"{lastUpdateId}");
                    }
                }
                if (depth.finalUpdateId <= OrderBook.lastUpdateId)
                {
                    Debug.Print($"{depth.firstUpdateId} : {depth.finalUpdateId}  dropped");
                }
                else if (depth.firstUpdateId <= OrderBook.lastUpdateId + 1 && depth.finalUpdateId >= OrderBook.lastUpdateId + 1)
                {
                    var sw = Stopwatch.StartNew();
                    var bookUpdates = Convert(depth, OrderBook.SymbolInformation);

                    OrderBook.lastUpdateId = depth.finalUpdateId;
                    Debug.Print($"Update {OrderBook.lastUpdateId} took {sw.ElapsedMilliseconds}ms.");

                    return bookUpdates;
                }
                else
                {
                    Debug.Print($"{depth.firstUpdateId} : {depth.finalUpdateId}  dropped");
                }
                return Enumerable.Empty<OrderBookEntry>();
            }
            finally
            {
                _lock.Release();
            }
        }

        List<OrderBookEntry> Convert(Binance.Depth depth, SymbolInformation si)
        {
            return depth.asks.Select(y => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals)
            {
                Price = decimal.Parse(y[0]),
                Quantity = decimal.Parse(y[1]),
                Side = TradeSide.Sell
            }).Reverse().Concat(depth.bids.Select(y => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals)
            {
                Price = decimal.Parse(y[0]),
                Quantity = decimal.Parse(y[1]),
                Side = TradeSide.Buy
            })
            ).ToList();
        }

        internal List<OrderBookEntry> Convert(Binance.WsDepth depth, SymbolInformation si)
        {
            return depth.asks.Select(y => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals)
            {
                Price = decimal.Parse(y[0]),
                Quantity = decimal.Parse(y[1]),
                Side = TradeSide.Sell
            }).Reverse().Concat(depth.bids.Select(y => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals)
            {
                Price = decimal.Parse(y[0]),
                Quantity = decimal.Parse(y[1]),
                Side = TradeSide.Buy
            })
            ).ToList();
        }


        protected override async Task<Order> PlaceOrder(TradingRule rule)
        {
            var result = await client.PlaceOrderAsync(
                rule.Market,
                rule.OrderSide == TradeSide.Buy ? Binance.TradeSide.BUY : Binance.TradeSide.SELL,
                (Binance.OrderType)Enum.Parse(typeof (Binance.OrderType), rule.OrderType, ignoreCase:true),
                rule.OrderVolume).ConfigureAwait(false);
            // TODO: below is an EXACT COPY of ExecuteBuy(), so do home work and refactor.
            if (result.Success)
            {
                var x = result.Data;
                var si = GetSymbolInformation(x.symbol);
                var order = new Order(si)
                {
                    OrderId = x.orderId.ToString(),
                    Price = x.price,
                    Quantity = x.origQty,
                    ExecutedQuantity = x.executedQty,
                    Side = x.side == Binance.TradeSide.BUY.ToString() ? TradeSide.Buy : TradeSide.Sell,
                    Created = x.transactTime.FromUnixTimestamp(),
                    Updated = x.transactTime.FromUnixTimestamp(),
                    Status = Convert(x.status),
                    Type = x.type
                };
                if (x.fills?.Length > 0)
                {
                    order.Fills = x.fills
                        .Select(t => new OrderTrade(si)
                        {
                            Id = t.tradeId.ToString(),
                            OrderId = x.orderId.ToString(),
                            Comission = t.comission,
                            ComissionAsset = t.comissionAsset,
                            Price = t.price,
                            Quantity = t.qty
                        }).ToArray();
                }
                return order;
            }
            else
            {
                throw new ApiException(result.Error);
            }
        }

        protected override async Task GetBalanceImpl()
        {
            var result = await GetBalancesAsync();
            var mngr = CurrentAccount?.BalanceManager;
            foreach (Balance b in result)
            {
                mngr.AddUpdateBalance(b);
            }
            foreach (var ticker in MarketSummaries)
                mngr.UpdateWithLastPrice(ticker.Symbol, ticker.LastPrice.GetValueOrDefault());
        }

        //var result = await client.PlaceOrderAsync("BNBBTC", Binance.TradeSide.SELL, Binance.OrderType.LIMIT, 1m, 0.0029m).ConfigureAwait(false);
        //if (!result.Success)
        //    throw new Exception(result.Error.ToString());
        //var query = await client.QueryOrderAsync(result.Data.symbol, result.Data.orderId);
        //if (!query.Success)
        //    throw new Exception(query.Error.ToString());
        //query = await client.QueryOrderAsync(result.Data.symbol, origClientOrderId: result.Data.clientOrderId);
        //if (!query.Success)
        //    throw new Exception(query.Error.ToString());
        //var cancel = await client.CancelOrderAsync(result.Data.symbol, result.Data.orderId);
        //if (!cancel.Success)
        //    throw new Exception(cancel.Error.ToString());

        protected override async Task GetOpenOrdersImpl()
        {
            var ordersResult = await client.GetOpenOrdersAsync().ConfigureAwait(false);
            UpdateStatus();
            if (ordersResult.Success)
            {
                var orders = ordersResult.Data.Select(arg => new Order(GetSymbolInformation(arg.symbol))
                {
                    OrderId = arg.orderId.ToString(),
                    Price = arg.price,
                    Quantity = arg.origQty,
                    Side = arg.side == "BUY" ? TradeSide.Buy : TradeSide.Sell,
                    StopPrice = arg.stopPrice,
                    Created = arg.time.FromUnixTimestamp(),
                    Updated = arg.updateTime.FromUnixTimestamp(),
                    Type = arg.type
                }).OrderByDescending(x => x.Created);
                CurrentAccount.OpenOrders.AddOrUpdate(orders);
            }
        }

        protected override async Task GetDepositsImpl()
        {
            var result = await client.GetDepositHistoryAsync().ConfigureAwait(false);
            UpdateStatus();
            if (result.Success)
            {
                var deposits = result.Data.depositList.Select(
                    x => new Transfer()
                    {
                        Id = x.txId,
                        Address = x.address,
                        //Comission = x.TxCost,
                        Asset = x.asset,
                        Quantity = x.amount,
                        Status = Code2DepositStatus(x.status),
                        Timestamp = x.insertTime.FromUnixTimestamp(),
                        Type = TransferType.Deposit
                    }).ToList();
                CurrentAccount.Deposits.AddOrUpdate(deposits);
            }
        }

        protected override async Task GetWithdrawalsImpl()
        {
            var result = await client.GetWithdrawHistoryAsync().ConfigureAwait(false);
            UpdateStatus();
            if (result.Success)
            {
                var withdrawals = result.Data.withdrawList.Select(
                    x => new Transfer()
                    {
                        Id = x.txId,
                        Address = x.address,
                        //Comission = x.TxCost,
                        Asset = x.asset,
                        Quantity = x.amount,
                        Status = Code2WithdrawalStatus(x.status),
                        Timestamp = x.applyTime.FromUnixTimestamp(),
                        Type = TransferType.Withdrawal
                    });
                CurrentAccount.Withdrawals.AddOrUpdate(withdrawals);
            }
        }

        protected override async Task GetOrdersHistoryImpl()
        {
            var ordersResult = await client.GetOrdersHistoryAsync(CurrentSymbol).ConfigureAwait(false);
            UpdateStatus();
            if (ordersResult.Success)
            {
                var orders = ordersResult.Data
                    .Where(x => x.status != Binance.OrderStatus.NEW.ToString())
                    .Select(arg => new Order(GetSymbolInformation(arg.symbol))
                    {
                        OrderId = arg.orderId.ToString(),
                        Price = arg.price,
                        Quantity = arg.origQty,
                        Side = arg.side == "BUY" ? TradeSide.Buy : TradeSide.Sell,
                        StopPrice = arg.stopPrice,
                        Created = arg.time.FromUnixTimestamp(),
                        Type = arg.type
                    })
                    .OrderByDescending(x => x.Created);
                CurrentAccount.OrdersHistory.AddOrUpdate(orders);
            }
        }

        protected override async Task GetTradesHistoryImpl()
        {
            var ordersResult = await client.GetAccountTradesAsync(CurrentSymbol).ConfigureAwait(false);
            UpdateStatus();
            if (ordersResult.Success)
            {
            }
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

        private async void Initialize123()
        {
            var taskServerTime = client.GetServerTimeAsync();
            var taskExchangeInfo = client.GetExchangeInfoAsync();
            var taskPriceTicker = client.Get24hrPriceTickerAsync();

            await Task.WhenAll(taskServerTime, taskExchangeInfo, taskPriceTicker);
            UpdateStatus();

            if (taskServerTime.Result.Success)
                ServerTime = taskServerTime.Result.Data.serverTime.FromUnixTimestamp();
            if (taskExchangeInfo.Result.Success)
                ProcessExchangeInfo(taskExchangeInfo.Result.Data.symbols.Select(CreateSymbolInformation));
            if (taskPriceTicker.Result.Success)
                ProcessPriceTicker(taskPriceTicker.Result.Data.Select(ToPriceTicker));

            //Run();
            //var test = await client.TestPlaceOrderAsync2("GOBTC", Binance.TradeSide.BUY, Binance.OrderType.LIMIT, 1.0m, 0.00000400m);
            //Debug.Print(test.Error?.ToString());
        }

        protected override void SubscribeMarketData()
        {
            base.SubscribeMarketData();

            SubscribeCommandIsExecuting(getServerTimeCommand);
            getServerTimeCommand.Subscribe(
                x =>
                {
                    ServerTime = x;
                    Status = ServerTime.ToString();
                }).DisposeWith(Disposables);
            Observable.Interval(TimeSpan.FromSeconds(1)).InvokeCommand(GetServerTimeCommand).DisposeWith(Disposables);

            var sub24hrPriceTickerWs = client.SubscribeMarketSummariesAsync(null);
            sub24hrPriceTickerWs.Subscribe(
                (Binance.WsPriceTicker24hr ticker) =>
                {
                    OnRefreshMarketSummary2(ToPriceTicker(ticker));
                }).DisposeWith(Disposables);
        }

        //protected new IObservable<PublicTrade> ObserveRecentTrades(string symbol, int limit)
        //{
        //    var si = GetSymbolInformation(symbol);
        //    var obs2 = client.GetRecentTradesAsync(symbol, limit).ToObservable().SelectMany(x => x.Data.ToObservable());
        //    var obs = client.SubscribePublicTradesAsync(symbol, TradesMaxItemCount);
        //    UpdateStatus();
        //    return obs2.Select(x => ToPublicTrade(x, si)).Concat(obs.Select(ToPublicTrade));
        //}

        private async Task<Binance.Trade[]> GetTradesDiff(string symbol, int limit)
        {
            if (symbol != lastTradeSymbol)
            {
                lastTradeId = 0;
                lastTradeSymbol = symbol;
            }
            var result = await client.GetRecentTradesAsync(symbol, limit);
            if (result.Success)
            {
                var trades = result.Data.ToList();
                trades.RemoveAll(x => x.id <= lastTradeId);
                if (trades.Count > 0)
                    lastTradeId = result.Data.Last().id;
                return result.Data.ToArray();
            }
            else
            {
                return Enumerable.Empty<Binance.Trade>().ToArray();
            }
        }

        //protected override bool IsValidMarket(SymbolInformation si)
        //{
        //    return base.IsValidMarket(si) && si.Status != Binance.MarketStatus.BREAK.ToString();
        //}
    }
}
