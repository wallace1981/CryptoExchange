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
        }

        protected override void InitializeAsyncImpl(CompositeDisposable disposables)
        {
            Observable
                .Interval(TimeSpan.FromSeconds(2))
                .Select(x => Unit.Default)
                .InvokeCommand(GetServerTime)
                .DisposeWith(Disposables);
            //var pairs = Markets.Where(x => x.QuoteAsset == "BTC" || x.QuoteAsset == "USDT").Select(x => x.Symbol);
            //var kline1m = client.SubscribeKlinesAsync(pairs, "1m");
            //kline1m.ObserveOnDispatcher().Subscribe(OnKline).DisposeWith(disposables);
            //var kline5m = client.SubscribeKlinesAsync(pairs, "5m");
            //kline5m.ObserveOnDispatcher().Subscribe(OnKline).DisposeWith(disposables);
            //var kline15m = client.SubscribeKlinesAsync(pairs, "15m");
            //kline15m.ObserveOnDispatcher().Subscribe(OnKline).DisposeWith(disposables);
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
            var resultTrades = await client.GetRecentTradesAsync(si.Symbol, TradesMaxItemCount).ConfigureAwait(true);
            if (resultTrades.Success)
            {
                GetTradesElapsed = resultTrades.ElapsedMilliseconds;
                var trades = resultTrades.Data.Select(x => Convert(x, si)).Reverse().ToList();// pair is <symbol,trades>
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
                mngr.UpdateWithLastPrice(ticker.Symbol, ticker.LastPrice.GetValueOrDefault());
        }

        protected override async Task GetOpenOrdersImpl()
        {
            var ordersResult = await client.GetOpenOrdersAsync().ConfigureAwait(false);
            UpdateStatus();
            if (ordersResult.Success)
            {
                var orders = ordersResult.Data.Select(Convert).OrderByDescending(x => x.Created);
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
                    .Select(Convert)
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

        protected override async Task<SymbolInformation> GetFullSymbolInformation()
        {
            var si = CurrentSymbolInformation;
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
                TotalDecimals = DigitsCount(minNotionalFilter.minNotional),
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
                ExecutedQuantity = x.cummulativeQuoteQty,
                Side = (x.side == Binance.TradeSide.BUY.ToString()) ? TradeSide.Buy : TradeSide.Sell,
                Created = x.time.FromUnixTimestamp(),
                Updated = x.updateTime.FromUnixTimestamp(),
                Status = Convert(x.status),
                Type = x.type
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
                        Comission = t.comission,
                        ComissionAsset = t.comissionAsset,
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

        public Candle Convert(Binance.WsCandlestick ticker)
        {
            return new Candle()
            {
                Open = ticker.kline.openPrice,
                High = ticker.kline.highPrice,
                Low = ticker.kline.lowPrice,
                Close = ticker.kline.closePrice,
                QuoteVolume = ticker.kline.quoteVolume,
                BuyQuoteVolume = ticker.kline.takerBuyQuoteVolume,
                Volume = ticker.kline.volume,
                BuyVolume = ticker.kline.quoteVolume,
                Symbol = ticker.symbol,
                OpenTime = ticker.kline.openTime.FromUnixTimestamp(),
                CloseTime = ticker.kline.closeTime.FromUnixTimestamp()
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
            var ticker = GetPriceTicker(candlestick.symbol);
            if (ticker != null)
            {
                switch (candlestick.kline.interval)
                {
                    case "1m":
                        ticker.Candle1m = Convert(candlestick);
                        break;
                    case "5m":
                        ticker.Candle5m = Convert(candlestick);
                        AnalyzeCandle(ticker.Candle5m);
                        break;
                    case "15m":
                        ticker.Candle15m = Convert(candlestick);
                        break;
                }
            }
        }

        private Dictionary<string, long> candleCache = new Dictionary<string, long>();

        private async void AnalyzeCandle(Candle candle)
        {
            if (candle.Close == decimal.Zero || candle.Open == decimal.Zero)
                return;
            if (candleCache.ContainsKey(candle.Symbol) && candleCache[candle.Symbol] == candle.CloseTime.Ticks)
                return;
            decimal delta = candle.Close / candle.Open;
            if (delta >= 1.04m)
            {
                candleCache[candle.Symbol] = candle.CloseTime.Ticks;
                await Alert.Handle($"{candle.Symbol}: price raised {delta-1m:p2}");
            }
        }

        // TODO: below FUNC is EXACT COPY of ExecuteOrder(), so do home work and refactor.
        protected override async Task<Order> PlaceOrder(TradingRule rule)
        {
            var orderType = Binance.OrderType.MARKET;
            Enum.TryParse<Binance.OrderType>(rule.OrderType, ignoreCase: true, result: out orderType);
            var result = await client.PlaceOrderAsync(
                rule.Market,
                rule.OrderSide == TradeSide.Buy ? Binance.TradeSide.BUY : Binance.TradeSide.SELL,
                orderType,
                rule.OrderVolume,
                orderType == Binance.OrderType.LIMIT ? rule.ThresholdRate : default(decimal?)
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
