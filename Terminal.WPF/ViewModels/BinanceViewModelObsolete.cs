using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Exchange.Net
{

    public partial class BinanceViewModel
    {
        long lastTradeId = 0;
        string lastTradeSymbol = null;

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
                return result.Data.Select(Convert);
            else
                return Enumerable.Empty<PriceTicker>();
        }

        protected async override Task<IEnumerable<PublicTrade>> GetPublicTradesAsync(string market, int limit)
        {
            var result = await client.GetRecentTradesAsync(market, limit).ConfigureAwait(false);
            if (result.Success)
            {
                var si = GetSymbolInformation(market);
                var trades = result.Data.Select(x => Convert(x, si)).Reverse().ToList();
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
                var si = GetSymbolInformation(market);
                var depth = result.Data;
                var asks = depth.asks.Select(a => new OrderBookEntry(si) { Price = decimal.Parse(a[0]), Quantity = decimal.Parse(a[1]), Side = TradeSide.Sell });
                var bids = depth.bids.Select(b => new OrderBookEntry(si) { Price = decimal.Parse(b[0]), Quantity = decimal.Parse(b[1]), Side = TradeSide.Buy });
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
                    var newTrade = Convert(trade);
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
            return depth.asks.Select(y => new OrderBookEntry(si)
            {
                Price = decimal.Parse(y[0]),
                Quantity = decimal.Parse(y[1]),
                Side = TradeSide.Sell
            }).Reverse().Concat(depth.bids.Select(y => new OrderBookEntry(si)
            {
                Price = decimal.Parse(y[0]),
                Quantity = decimal.Parse(y[1]),
                Side = TradeSide.Buy
            })
            ).ToList();
        }

        internal List<OrderBookEntry> Convert(Binance.WsDepth depth, SymbolInformation si)
        {
            return depth.asks.Select(y => new OrderBookEntry(si)
            {
                Price = decimal.Parse(y[0]),
                Quantity = decimal.Parse(y[1]),
                Side = TradeSide.Sell
            }).Reverse().Concat(depth.bids.Select(y => new OrderBookEntry(si)
            {
                Price = decimal.Parse(y[0]),
                Quantity = decimal.Parse(y[1]),
                Side = TradeSide.Buy
            })
            ).ToList();
        }
      public ICommand GetServerTimeCommand => getServerTimeCommand;




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
                ProcessPriceTicker(taskPriceTicker.Result.Data.Select(Convert));

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

            var sub24hrPriceTickerWs = client.SubscribeMarketSummaries(null);
            sub24hrPriceTickerWs.Subscribe(
                (Binance.WsPriceTicker24hr ticker) =>
                {
                    OnRefreshMarketSummary2(Convert(ticker));
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

        public IEnumerable<OrderBookEntry> ConvertDepth(Binance.WsDepth depth)
        {
            return depth.bids.Select(y => new OrderBookEntry(OrderBook.SymbolInformation)
            {
                Price = decimal.Parse(y[0]),
                Quantity = decimal.Parse(y[1]),
                Side = TradeSide.Buy
            }).Concat(depth.asks.Select(y => new OrderBookEntry(OrderBook.SymbolInformation)
            {
                Price = decimal.Parse(y[0]),
                Quantity = decimal.Parse(y[1]),
                Side = TradeSide.Sell
            })
            );
        }

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
    }

}