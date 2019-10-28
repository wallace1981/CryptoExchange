using DynamicData;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
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
        protected override bool HasTradesPush => false;
        protected override bool HasOrderBookPush => false;
        [Reactive] public DateTime ServerTime { get; set; }
        //public ICommand GetServerTimeCommand => getServerTimeCommand;
        [Reactive] public string CurrentInterval { get; set; } = "1d";
        private ReactiveCommand<Unit, Unit> GetServerTime { get; }

        public IList<string> KlineIntervals
        {
            get
            {
                var intervals = new List<string>
                {
                    "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "3d", "1w", "1M"
                };
                return intervals;
            }
        }

        public BinanceViewModel()
        {
            var defaultAccount = new ExchangeAccount("default", client);
            Accounts.AddOrUpdate(defaultAccount);
            CurrentAccount = defaultAccount;

            //getServerTimeCommand = ReactiveCommand.CreateFromTask<long, DateTime>(x => GetServerTimeAsync());
            GetServerTime = ReactiveCommand.CreateFromTask(GetServerTimeImpl);

            FetchKlines = ReactiveCommand.CreateFromTask<int>(FetchKlinesImpl);
            ConvertKlines = ReactiveCommand.Create(ConvertKlinesImpl);

            //this.WhenActivated(disposables =>
            //{
            //registerDisposable(getServerTimeCommand);

            //var resultExchangeInfo = client.GetExchangeInfoOffline();
            //currentExchangeInfo = resultExchangeInfo.Data;
            //var symbols = resultExchangeInfo.Data.symbols.Select(CreateSymbolInformation);
            //ProcessExchangeInfo(symbols);

            //var resultTickers = client.GetPriceTicker24hrOffline();
            //var tickers = resultTickers.Data.Select(ToPriceTicker);
            //ProcessPriceTicker(tickers);
            //});
            this.HasSignedAccount = client.IsSigned;
            Disposables.Add(klineSubscriptions);
        }

        protected override void InitializeAsyncImpl(CompositeDisposable disposables)
        {
            Observable
                .Interval(TimeSpan.FromSeconds(2))
                .Select(x => Unit.Default)
                .InvokeCommand(GetServerTime)
                .DisposeWith(Disposables);
        }

        protected override void SetTickersSubscriptionWebSocket(bool isEnabled)
        {
            base.SetTickersSubscriptionWebSocket(isEnabled);
            if (isEnabled)
            {
                var collector = new CompositeDisposable();
                klineSubscriptions.Disposable = collector;
                var pairs = Markets.Where(x => x.QuoteAsset == "BTC" || x.QuoteAsset == "USDT").Select(x => x.Symbol);
                string[] intervals = { "1m", "5m", "15m", "30m", "1h" };
                foreach (var x in intervals)
                {
                    var kline = client.SubscribeKlinesAsync(pairs, x);
                    kline.ObserveOnDispatcher().Select(Convert).Subscribe(OnKline).DisposeWith(collector);
                }
            }
            else
            {
                klineSubscriptions.Disposable = null;
            }
        }

        protected void UpdateStatus(string serverStatus, string clientMsg = null)
        {
            Status = string.Join("  ", serverStatus, clientMsg ?? ClientStatus);
            ServerStatus = serverStatus;
        }

        private async Task GetServerTimeImpl()
        {
            var result = await client.GetServerTimeAsync().ConfigureAwait(false);
            if (result.Success)
            {
                client.SetServerTimeOffset(result.Data.serverTime, result.ElapsedMilliseconds);
                ServerTime = result.Data.serverTime.FromUnixTimestamp(convertToLocalTime: false);
            }
            else
                ServerTime = DateTime.UtcNow;
            UpdateStatus($"Server Time: {ServerTime}");
        }

        private async Task FetchKlinesImpl(int limit)
        {
            var result = await client.GetKlinesAsync(CurrentSymbol, CurrentInterval, limit: limit).ConfigureAwait(false);
        }

        private void ConvertKlinesImpl()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\"Symbol\",\"Volume\",\"Buy Volume\",\"Ratio\"");
            foreach (var json in System.IO.Directory.EnumerateFiles("binance\\db", "klines*.json"))
            {
                var text = System.IO.File.ReadAllText(json);
                var kline = JsonConvert.DeserializeObject<List<object[]>>(text);
                var Symbol = json.Replace("binance\\db\\klines-", "").Replace("-1d.json", "");
                kline.Reverse();
                var volume = kline.Take(10).Sum(x => decimal.Parse(x[7].ToString()));
                var buyVolume = kline.Take(10).Sum(x => decimal.Parse(x[10].ToString()));
                //var summ = new KlineSummary()
                //{
                //    Symbol = json.Replace("klines-", "").Replace("-1d.json", ""),
                //    OpenDate = ((long)kline[0]).FromUnixTimestamp(),
                //    QuoteVolume = (decimal)kline[7],
                //    QuoteBuyVolume = (decimal)kline[10]
                //};

                sb.AppendLine($"{Symbol},{volume},{buyVolume},{buyVolume/volume}");
            }
            System.IO.File.WriteAllText("binance\\db\\klines-10d.csv", sb.ToString());
        }

        protected override async Task GetExchangeInfoImpl()
        {
            var resultExchangeInfo = await client.GetExchangeInfoAsync().ConfigureAwait(false);
            if (resultExchangeInfo.Success)
            {
                client.SetServerTimeOffset(resultExchangeInfo.Data.serverTime, 500);
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
                var tickers = resultTickers.Data.Select(Convert);
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
                    var tickers = priceTickersTask.Result.Data.Select(Convert).ToList();
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
            //long? lastTradeId = RecentTradesCache.Count > 0 ? RecentTradesCache.Items.Select(x => x.Id).FirstOrDefault() : (long?)null;
            //var resultTrades = await client.GetAggregatedTradesAsync(si.Symbol, TradesMaxItemCount, lastTradeId).ConfigureAwait(false);
            var resultTrades = await client.GetRecentTradesAsync(si.Symbol, TradesMaxItemCount).ConfigureAwait(false);
            if (resultTrades.Success)
            {
                GetTradesElapsed = resultTrades.ElapsedMilliseconds;
                var trades = resultTrades.Data.Select(x => Convert(x, si)).Reverse().ToList();
                ProcessPublicTrades(trades);
                UpdateStatus(ServerStatus);
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetAggTrades: {resultTrades.Error.ToString()}");
            }
        }

        protected override async Task GetDepthImpl()
        {
            if (CurrentSymbol == null)
                return;
            var si = CurrentSymbolInformation;
            var resultDepth = await client.GetDepthAsync(si.Symbol, OrderBookMaxItemCount).ConfigureAwait(false);
            if (resultDepth.Success)
            {
                GetDepthElapsed = resultDepth.ElapsedMilliseconds;
                var depth = resultDepth.Data;
                var asks = depth.asks.Select(a => new OrderBookEntry(si) { Price = decimal.Parse(a[0]), Quantity = decimal.Parse(a[1]), Side = TradeSide.Sell });
                var bids = depth.bids.Select(b => new OrderBookEntry(si) { Price = decimal.Parse(b[0]), Quantity = decimal.Parse(b[1]), Side = TradeSide.Buy });
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

        protected override async Task GetBalanceImpl()
        {
            var result = await GetBalancesAsync().ConfigureAwait(false);
            var mngr = CurrentAccount?.BalanceManager;
            foreach (Balance b in result)
            {
                mngr.AddUpdateBalance(b);
            }
            foreach (var ticker in MarketSummaries)
                mngr.UpdateWithLastPrice(ticker.Symbol, ticker.Bid.GetValueOrDefault());
        }

        protected override async Task GetOpenOrdersImpl()
        {
            var ordersResult = await client.GetOpenOrdersAsync().ConfigureAwait(false);
            UpdateStatus();
            if (ordersResult.Success)
            {
                var orders = ordersResult.Data.Select(Convert);
                var obsoleteOrders = CurrentAccount.OpenOrders.Items.Select(x => x.OrderId).Except(orders.Select(x => x.OrderId));
                CurrentAccount.OpenOrders.Remove(obsoleteOrders);
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
                    .Select(Convert);
                CurrentAccount.OrdersHistory.Clear();
                CurrentAccount.OrdersHistory.AddOrUpdate(orders);
            }
        }

        protected override async Task GetTradesHistoryImpl()
        {
            var tradesResult = await client.GetAccountTradesAsync(CurrentSymbol).ConfigureAwait(false);
            UpdateStatus();
            if (tradesResult.Success)
            {
                var trades = tradesResult.Data
                    .Select(Convert);
                CurrentAccount.TradesHistory.Clear();
                CurrentAccount.TradesHistory.AddOrUpdate(trades);
            }
        }

        protected override Task<SymbolInformation> GetFullSymbolInformation()
        {
            return GetFullSymbolInformation(CurrentSymbol);
        }

        protected override async Task<SymbolInformation> GetFullSymbolInformation(string market)
        {
            var si = GetSymbolInformation(market);
            var balances = await GetBalancesAsync();
            //si.QuoteAssetBalance = await GetAssetBalance(si.QuoteAsset);
            //si.QuoteAssetBalance.Free = Math.Round(si.QuoteAssetBalance.Free, si.PriceDecimals);
            si.BaseAssetBalance = balances.SingleOrDefault(x => x.Asset == si.BaseAsset);
            si.QuoteAssetBalance = balances.SingleOrDefault(x => x.Asset == si.QuoteAsset);
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
            return client.SubscribeMarketSummariesAsync(null).Select(Convert).Where(x => x != null);
        }

        protected override IObservable<PublicTrade> ObserveTrades(string market)
        {
            return client.SubscribePublicTradesAsync(market).Select(Convert).Where(x => x != null);
        }

        SymbolInformation CreateSymbolInformation(Binance.Market market)
        {
            var priceFilter = market.filters.SingleOrDefault((f) => f.filterType == Binance.FilterType.PRICE_FILTER.ToString());
            var lotSizeFilter = market.filters.SingleOrDefault((f) => f.filterType == Binance.FilterType.LOT_SIZE.ToString());
            var minNotionalFilter = market.filters.SingleOrDefault((f) => f.filterType == Binance.FilterType.MIN_NOTIONAL.ToString());
            var cmcEntry = GetCmcEntry(market.baseAsset);
            return new SymbolInformation
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
                OrderTypes = market.orderTypes,
                QuantityDecimals = DigitsCount(lotSizeFilter.stepSize),
                MinNotional = minNotionalFilter.minNotional,
                TotalDecimals = DigitsCount(minNotionalFilter.minNotional),
                IsMarginTradingAllowed = market.isMarginTradingAllowed,
                CmcId = cmcEntry != null ? cmcEntry.id : -1,
                CmcName = cmcEntry != null ? cmcEntry.name : market.baseAsset,
                CmcSymbol = cmcEntry != null ? cmcEntry.symbol : market.symbol
            };
        }

        public ReactiveCommand<int, Unit> FetchKlines { get; }
        public ReactiveCommand<Unit, Unit> ConvertKlines { get; }

        private void UpdateStatus()
        {
            Status = $"Binance: Weight is {client.Weight}, expiration in {client.WeightReset.TotalSeconds} secs.";
        }

        public Order Convert(Binance.Order x)
        {
            var si = GetSymbolInformation(x.symbol);
            var result = new Order(si)
            {
                OrderId = x.orderId.ToString(),
                Price = x.price,
                Quantity = x.origQty,
                ExecutedQuantity = x.executedQty,
                Side = (x.side == Binance.TradeSide.BUY.ToString()) ? TradeSide.Buy : TradeSide.Sell,
                Created = x.time.FromUnixTimestamp(),
                Updated = x.updateTime.FromUnixTimestamp(),
                Status = Convert(x.status),
                Type = x.type
            };
            return result;
        }

        public OrderTrade Convert(Binance.AccountTrade x)
        {
            var si = GetSymbolInformation(x.symbol);
            var result = new OrderTrade(si)
            {
                Id = x.id.ToString(),
                OrderId = x.orderId.ToString(),
                Price = x.price,
                Quantity = x.qty,
                Side = (x.isBuyer) ? TradeSide.Buy : TradeSide.Sell,
                Timestamp = x.time.FromUnixTimestamp(),
                Comission = x.commission,
                ComissionAsset = x.commissionAsset
            };
            return result;
        }

        public Order Convert(Binance.NewOrderResponseResult x)
        {
            var si = GetSymbolInformation(x.symbol);
            var result = new Order(si)
            {
                OrderId = x.orderId.ToString(),
                Price = x.price,
                Quantity = x.origQty,
                ExecutedQuantity = x.cummulativeQuoteQty,
                Side = (x.side == Binance.TradeSide.BUY.ToString()) ? TradeSide.Buy : TradeSide.Sell,
                Created = x.transactTime.FromUnixTimestamp(),
                Updated = x.transactTime.FromUnixTimestamp(),
                Status = Convert(x.status),
                Type = x.type
            };
            if (x.fills?.Length > 0)
            {
                result.Fills.AddRange(
                    x.fills.Select(t => new OrderTrade(si)
                    {
                        Id = t.tradeId.ToString(),
                        OrderId = x.orderId.ToString(),
                        Comission = t.commission,
                        ComissionAsset = t.commissionAsset,
                        Price = t.price,
                        Quantity = t.qty
                    })
                );
            }
            return result;
        }

        public Order Convert(Binance.QueryOrderResponseResult x)
        {
            var result = Convert(x as Binance.NewOrderResponseResult);
            result.StopPrice = x.stopPrice;
            result.Created = x.time.FromUnixTimestamp();
            result.Updated = x.updateTime.FromUnixTimestamp();
            return result;
        }

        public PriceTicker Convert(Binance.PriceTicker ticker)
        {
            return new PriceTicker()
            {
                LastPrice = ticker.price,
                Symbol = ticker.symbol,
                SymbolInformation = GetSymbolInformation(ticker.symbol)
            };
        }

        public PriceTicker Convert(Binance.PriceTicker24hr ticker)
        {
            var si = GetSymbolInformation(ticker.symbol);
            if (si == null)
                return null;
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

        public Balance Convert(Binance.Balance balance)
        {
            return new Balance(balance.asset, UsdAssets.Contains(balance.asset))
            {
                Free = balance.free,
                Locked = balance.locked
            };
        }

        public PriceTicker Convert(Binance.WsPriceTicker24hr ticker)
        {
            var si = GetSymbolInformation(ticker.symbol);
            return si != null ? new PriceTicker()
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
            } : null;
        }

        /*public PriceTicker ToPriceTicker(Binance.WsCandlestick ticker)
        {
            return new PriceTicker()
            {
                Symbol = ticker.symbol,
                LastPrice = ticker.kline.closePrice,
                PriceChangePercent = (100m * ticker.kline.closePrice / ticker.kline.openPrice) - 100m,
                Volume = ticker.kline.quoteVolume,
                BuyVolume = ticker.kline.takerBuyQuoteVolume
            };
        }*/

        public Candle Convert(Binance.WsCandlestick candlestick)
        {
            var kline = candlestick.kline;
            return new Candle()
            {
                Open = kline.openPrice,
                High = kline.highPrice,
                Low = kline.lowPrice,
                Close = kline.closePrice,
                QuoteVolume = kline.quoteVolume,
                BuyQuoteVolume = kline.takerBuyQuoteVolume,
                Volume = kline.volume,
                BuyVolume = kline.takerBuyVolume,
                Symbol = candlestick.symbol,
                Interval = candlestick.kline.interval,
                OpenTime = kline.openTime.FromUnixTimestamp(),
                CloseTime = kline.closeTime.FromUnixTimestamp()
            };
        }

        public PublicTrade Convert(Binance.AggTrade x, SymbolInformation si)
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

        public PublicTrade Convert(Binance.Trade x, SymbolInformation si)
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

        public PublicTrade Convert(Binance.WsTrade x)
        {
            var si = GetSymbolInformation(x.symbol);
            if (si == null)
                return null;
            return new PublicTrade(si)
            {
                Id = x.tradeId,
                Price = x.price,
                Quantity = x.quantity,
                Side = !x.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
                Time = x.tradeTime.FromUnixTimestamp()
            };
        }

        private void OnKline(Binance.WsCandlestick candlestick)
        {
            /*var ticker = GetPriceTicker(candlestick.symbol);
            if (ticker != null)
            {
                var candle = Convert(candlestick);
                switch (candlestick.kline.interval)
                {
                    case "1m":
                        ticker.Candle1m = candle;
                        break;
                    case "5m":
                        ticker.Candle5m = candle;
                        AnalyzeCandle(ticker.Candle5m, 1.04m, "5m");
                        break;
                    case "15m":
                        ticker.Candle15m = candle;
                        AnalyzeCandle(ticker.Candle15m, 1.08m, "15m");
                        break;
                    case "30m":
                        ticker.Candle30m = candle;
                        AnalyzeCandle(ticker.Candle30m, 1.10m, "30m");
                        break;
                    case "1h":
                        ticker.Candle1h = candle;
                        AnalyzeCandle(ticker.Candle1h, 1.12m, "1h");
                        break;
                    case "6h":
                        ticker.Candle6h = candle;
                        break;
                    case "12h":
                        ticker.Candle12h = candle;
                        break;
                    case "1d":
                        ticker.Candle1d = candle;
                        break;
                    case "3d":
                        ticker.Candle3d = candle;
                        break;
                    case "1w":
                        ticker.Candle1w = candle;
                        break;
                }
            }*/
        }

        // TODO: below FUNC is EXACT COPY of ExecuteOrder(), so do home work and refactor.
        protected override async Task<Order> PlaceOrder(TradingRule rule)
        {
            var si = GetSymbolInformation(rule.Market);
            var orderType = Binance.OrderType.MARKET;
            Enum.TryParse<Binance.OrderType>(rule.OrderType, ignoreCase: true, result: out orderType);
            decimal? price = null;
            if (orderType == Binance.OrderType.LIMIT)
            {
                if (rule.OrderSide == TradeSide.Buy)
                    price = si.ClampPrice(rule.ThresholdRate * 1.01m);
                else
                    price = si.ClampPrice(rule.ThresholdRate * 0.99m);
            }
            var result = await client.PlaceOrderAsync(
                rule.Market,
                rule.OrderSide == TradeSide.Buy ? Binance.TradeSide.BUY : Binance.TradeSide.SELL,
                orderType,
                rule.OrderVolume,
                price,
                null,
                orderType == Binance.OrderType.LIMIT ? Binance.TimeInForce.IOC : Binance.TimeInForce.GTC
                ).ConfigureAwait(false);
            if (result.Success)
            {
                return Convert(result.Data);
            }
            else
            {
                throw new ApiException(result.Error);
            }
        }

        protected async override Task<bool> CancelOrderImpl(Order order)
        {
            var result = await client.CancelOrderAsync(order.SymbolInformation.Symbol, long.Parse(order.OrderId));
            if (result.Success)
            {
                CurrentAccount.OpenOrders.Remove(order.OrderId);
            }
            return result.Success;
        }

        protected async override Task<bool> SubmitOrderImpl(NewOrder order)
        {
            var orderType = Binance.OrderType.MARKET;
            Enum.TryParse<Binance.OrderType>(order.OrderType, ignoreCase: true, result: out orderType);
            var result = await client.PlaceOrderAsync(
                order.SymbolInformation.Symbol,
                order.Side == TradeSide.Buy ? Binance.TradeSide.BUY : Binance.TradeSide.SELL,
                orderType,
                order.Quantity,
                orderType == Binance.OrderType.LIMIT ? order.Price : default(decimal?)
                ).ConfigureAwait(false);
            if (result.Success)
            {
                CurrentAccount.OpenOrders.AddOrUpdate(Convert(result.Data));
                return true;
            }
            else
            {
                await Alert.Handle(result.Error.Msg);
                throw new ApiException(result.Error);
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

        public static TransferStatus ToDepositStatus(int code)
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

        public static TransferStatus ToWithdrawalStatus(int code)
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

        public override bool FilterByMarket(string symbol, string market)
        {
            return symbol.EndsWith(market, StringComparison.CurrentCultureIgnoreCase);
        }
        public override bool FilterByAsset(string symbol, string asset)
        {
            return symbol.StartsWith(asset, StringComparison.CurrentCultureIgnoreCase);
        }


        #region Testing methods
        public List<SymbolInformation> GetFullMarketInformationOffline()
        {
            var client = new BinanceApiClient();
            var symbols = client.GetExchangeInfoOffline().Data.symbols.Where(x => x.status == Binance.MarketStatus.TRADING.ToString());
            var markets = symbols.Select(CreateSymbolInformation).ToList();
            ProcessExchangeInfo(markets);
            var tickers = client.GetPriceTicker24hrOffline().Data.Select(Convert).Where(x => x != null).ToList();
            ProcessPriceTicker(tickers);
            var account = client.GetAccountInfoOffline().Data.balances.Select(Convert).ToList();
            foreach (var m in markets)
            {
                m.PriceTicker = GetPriceTicker(m.Symbol);
                var filter = symbols.SingleOrDefault(x => x.symbol == m.Symbol).filters.SingleOrDefault(x => x.filterType == Binance.FilterType.PERCENT_PRICE.ToString());
                if (filter != null)
                {
                    if (filter != null)
                    {
                        m.MaxPrice = m.PriceTicker.WeightedAveragePrice * filter.multiplierUp;
                        m.MinPrice = m.PriceTicker.WeightedAveragePrice * filter.multiplierDown;
                    }
                }
                m.BaseAssetBalance = account.SingleOrDefault(x => x.Asset == m.BaseAsset);
                m.QuoteAssetBalance = account.SingleOrDefault(x => x.Asset == m.QuoteAsset);
            }
            return markets;
        }

        #endregion
        SerialDisposable klineSubscriptions = new SerialDisposable();
        private readonly ReactiveCommand<long, DateTime> getServerTimeCommand;
        private Binance.ExchangeInfo currentExchangeInfo;
        private DateTime ticker24hrLastRun = DateTime.MinValue;
        BinanceApiClient client = new BinanceApiClient();
    }

    public class KlineSummary
    {
        public string Symbol { get; set; }
        public DateTime OpenDate { get; set; }
        public decimal QuoteVolume { get; set; }
        public decimal QuoteBuyVolume { get; set; }
    }
}
