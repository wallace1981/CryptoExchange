using ReactiveUI;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using Terminal.WPF;

namespace Exchange.Net
{
    public abstract class ExchangeViewModel : ReactiveObject, ISupportsActivation
    {

        // NOTE: below props should be NORMAL props since they has to be changed from UI.
        public virtual int OrderBookMaxItemCount { get; set; } = 25;
        public TimeSpan OrderBookRetrieveInterval => TimeSpan.FromSeconds(3);
        public int TradesMaxItemCount { get; set; } = 50;
        public TimeSpan TradesRetrieveInterval => TimeSpan.FromSeconds(1);
        public string PriceTickerFilter { get; set; }
        public TimeSpan PriceTickerRetrieveInterval => TimeSpan.FromSeconds(1);

        public bool IsActive
        {
            get => isActive;
            set => this.RaiseAndSetIfChanged(ref isActive, value);
        }

        public bool IsInitializing
        {
            get => isInitializing;
            set => this.RaiseAndSetIfChanged(ref isInitializing, value);
        }

        public bool IsLoadingOrderBook
        {
            get => isLoadingOrderBook;
            set => this.RaiseAndSetIfChanged(ref isLoadingOrderBook, value);
        }

        public bool IsLoadingTrades
        {
            get => isLoadingTrades;
            set => this.RaiseAndSetIfChanged(ref isLoadingTrades, value);
        }

        public string Status
        {
            get { return this.status; }
            set { this.RaiseAndSetIfChanged(ref this.status, value); }
        }

        public bool IsBusy
        {
            get { return Interlocked.Read(ref this.busyCounter) > 0L; }
        }

        public string CurrentSymbol
        {
            get { return this.currentSymbol; }
            set { if (value != null) this.RaiseAndSetIfChanged(ref this.currentSymbol, value); }
        }

        public string CurrentMarketSummariesPeriod
        {
            get => this.currentMarketSummariesPeriod;
            set => this.RaiseAndSetIfChanged(ref this.currentMarketSummariesPeriod, value);
        }

        public SymbolInformation CurrentSymbolInformation
        {
            get { return this.currentSymbolInformation; }
            set { this.RaiseAndSetIfChanged(ref this.currentSymbolInformation, value); }
        }

        public PriceTicker CurrentSymbolTickerPrice
        {
            get { return this.currentSymboTickerPrice; }
            set { if (value != null) this.RaiseAndSetIfChanged(ref this.currentSymboTickerPrice, value); }
        }

        public string CurrentMarket
        {
            get { return this.currentMarket; }
            set { this.RaiseAndSetIfChanged(ref this.currentMarket, value); }
        }

        public string MarketFilter
        {
            get { return this.marketFilter; }
            set { this.RaiseAndSetIfChanged(ref this.marketFilter, value); }
        }

        public TimeSpan RefreshMarketSummariesElapsed
        {
            get { return refreshMarketSummariesElapsed; }
            set { this.RaiseAndSetIfChanged(ref this.refreshMarketSummariesElapsed, value); }
        }

        public TimeSpan RefreshTradesElapsed
        {
            get { return refreshTradesElapsed; }
            set { this.RaiseAndSetIfChanged(ref this.refreshTradesElapsed, value); }
        }

        public TimeSpan RefreshDepositsElapsed
        {
            get { return refreshDepositsElapsed; }
            set { this.RaiseAndSetIfChanged(ref this.refreshDepositsElapsed, value); }
        }

        public int OrderBookMergeDecimals
        {
            get { return orderBookMergeDecimals; }
            set { this.RaiseAndSetIfChanged(ref this.orderBookMergeDecimals, value); }
        }

        public virtual int[] OrderBookMergeDecimalsList => new int[] { 10, 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        public virtual int[] OrderBookSizeList => new int[] { 5, 10, 25, 50, 100 };
        public virtual int[] RecentTradesSizeList => new int[] { 5, 10, 25, 50, 100 };
        public TradeSide[] TradeSides => new TradeSide[] { TradeSide.Buy, TradeSide.Sell };

        public IEnumerable<SymbolInformation> Markets => marketsMapping?.Values;
        public ReactiveList<string> MarketAssets => marketAssets;
        public ReactiveList<PriceTicker> MarketSummaries => marketSummaries;
        public ReactiveList<PublicTrade> RecentTrades => recentTrades;
        public OrderBook OrderBook { get; }
        public ReactiveList<Transfer> Deposits => deposits;
        public ReactiveList<Transfer> Withdrawals => withdrawals;
        public ReactiveList<Order> OpenOrders { get; }
        public ReactiveList<Order> OrderHistory { get; }
        public ReactiveList<TradingRuleProxy> TradingRuleProxies => tradingRuleProxies;
        public BalanceManager BalanceManager => balanceManager;
        public ReactiveList<TradeTaskViewModel> TradeTasks { get; }

        protected abstract string DefaultMarket { get; }
        protected List<string> UsdAssets = new List<string>();
        public abstract string ExchangeName { get; }

        protected virtual bool HasMarketSummariesPull => true;
        protected virtual bool HasMarketSummariesPush => false;
        protected virtual bool HasTradesPull => true;
        protected virtual bool HasTradesPush => false;
        protected virtual bool HasOrderBookPull => true;
        protected virtual bool HasOrderBookPush => false;

        ReactiveList<PublicTrade> recentTrades;
        ReactiveList<Transfer> deposits;
        ReactiveList<Transfer> withdrawals;
        ReactiveList<PriceTicker> marketSummaries;
        ReactiveList<string> marketAssets;
        ReactiveList<TradingRuleProxy> tradingRuleProxies = new ReactiveList<TradingRuleProxy>();
        BalanceManager balanceManager;
        string currentSymbol;
        SymbolInformation currentSymbolInformation;
        PriceTicker currentSymboTickerPrice;
        string currentMarketSummariesPeriod = "30m"; // NOTE: exchange dependable value!
        string currentMarket;
        string marketFilter;
        string status;
        private bool isInitializing, isLoadingOrderBook, isLoadingTrades;
        long busyCounter;
        int orderBookMergeDecimals;

#if !GTK
        readonly ViewModelActivator viewModelActivator = new ViewModelActivator();
#endif

        TimeSpan refreshMarketSummariesElapsed;
        TimeSpan refreshTradesElapsed;
        TimeSpan refreshDepositsElapsed;

        public ExchangeViewModel()
        {
            currentMarket = DefaultMarket;

            marketAssets = new ReactiveList<string>();
            marketSummaries = new ReactiveList<PriceTicker>();
            recentTrades = new ReactiveList<PublicTrade>();
            OrderBook = new OrderBook(null);
            OpenOrders = new ReactiveList<Order>();
            OrderHistory = new ReactiveList<Order>();
            deposits = new ReactiveList<Transfer>();
            withdrawals = new ReactiveList<Transfer>();
            balanceManager = new BalanceManager();
            TradeTasks = new ReactiveList<TradeTaskViewModel>();

            marketAssets.EnableThreadSafety();
            marketSummaries.EnableThreadSafety();
            TradeTasks.EnableThreadSafety();

            setCurrentMarketCommand = ReactiveCommand.Create<string>(x => CurrentMarket = x);
            setCurrentSymbolCommand = ReactiveCommand.Create<string>(x => { CurrentSymbol = x; CurrentSymbolInformation = GetSymbolInformation(x); });
            setActiveCommand = ReactiveCommand.Create<bool>(x => Run(x));
            getMarketsCommand = ReactiveCommand.CreateFromTask<long, IEnumerable<SymbolInformation>>(x => GetMarketsAsync());
            //getTickersCommand = ReactiveCommand.CreateFromTask<long, IEnumerable<PriceTicker>>(x => GetTickersAsync());
            refreshCommand = ReactiveCommand.CreateFromTask<int>(x => RefreshCommandExecute(x));
            refreshPrivateDataCommand = ReactiveCommand.CreateFromTask(RefreshPrivateDataExecute);
            CancelOrderCommand = ReactiveCommand.CreateFromTask<string>(CancelOrder);
            SubmitOrderCommand = ReactiveCommand.CreateFromTask<NewOrder>(SubmitOrder);
            //CreateRuleCommand = ReactiveCommand.Create<object>(CreateRuleExecute);
            CreateRuleCommand = ReactiveCommand.CreateFromTask<string>(CreateTradeTaskExecute);
            SubmitRuleCommand = ReactiveCommand.Create<object>(SubmitRuleExecute);

            var subSI = this.ObservableForProperty(vm => vm.CurrentSymbolInformation)
                .Subscribe(x =>
                {
                    OrderBook.SymbolInformation = x.Value;
                    OrderBookMergeDecimals = x.Value.PriceDecimals;
                });
            var subMD = this.ObservableForProperty(vm => vm.OrderBookMergeDecimals).Subscribe(x => OrderBook.MergeDecimals = x.Value);

            this.WhenActivated(registerDisposable =>
            {
                registerDisposable(subSI);
                registerDisposable(subMD);
                registerDisposable(disposablesHandle);
                registerDisposable(setCurrentMarketCommand);
                registerDisposable(getMarketsCommand);
                //registerDisposable(getTickersCommand);
                registerDisposable(refreshCommand);
                Activate();
            });
        }

        public ICommand SetCurrentMarketCommand => setCurrentMarketCommand;
        public ICommand SetCurrentSymbolCommand => setCurrentSymbolCommand;
        public ICommand SetActiveCommand => setActiveCommand;
        public ICommand CreateRuleCommand { get; }
        public ICommand SubmitRuleCommand { get; }
        // Public functionality
        public ICommand GetMarketsCommand => getMarketsCommand;
        //public ICommand GetTickersCommand => getTickersCommand;
        //public ICommand GetTradesCommand { get; }
        public ICommand GetOrderBookCommand { get; }
        // Signed functionality
        public ICommand GetOpenOrdersCommand { get; }
        public ICommand GetDepositsCommand { get; }
        public ICommand GetWithdrawalsCommand { get; }
        public ICommand GetOrderHistoryCommand { get; }
        public ICommand CancelOrderCommand { get; }
        public ICommand SubmitOrderCommand { get; }
        public ICommand RefreshCommand => refreshCommand;
        public ICommand RefreshPrivateDataCommand => refreshPrivateDataCommand;

        private readonly ReactiveCommand<string, Unit> setCurrentMarketCommand;
        private readonly ReactiveCommand<string, Unit> setCurrentSymbolCommand;
        private readonly ReactiveCommand<bool, Unit> setActiveCommand;
        private readonly ReactiveCommand<long, IEnumerable<SymbolInformation>> getMarketsCommand;
        //private readonly ReactiveCommand<long, IEnumerable<PriceTicker>> getTickersCommand;
        private readonly ReactiveCommand<int, Unit> refreshCommand;
        private readonly ICommand refreshPrivateDataCommand;


        bool isActive = false;
        public void Activate()
        {
            Initialize();
            //if (isActive)
            //    DoDispose();
            //else
            //    ;// Run();
            //isActive = !isActive;
        }

        private void UpdateBusyCount(bool busy)
        {
            if (busy)
                Interlocked.Increment(ref busyCounter);
            else
                Interlocked.Decrement(ref busyCounter);
            this.RaisePropertyChanged(nameof(IsBusy));
        }

        public SymbolInformation GetSymbolInformation(string symbol)
        {
            SymbolInformation market = null;
            return marketsMapping.TryGetValue(symbol, out market) ? market : null;
        }

        protected CoinMarketCap.PublicAPI.Listing GetCmcEntry(string asset)
        {
            var cmcEntry = cmc_listing.FirstOrDefault(x => CorrectAssetName(asset).Equals(x.symbol, StringComparison.CurrentCultureIgnoreCase));
            Debug.WriteLineIf(cmcEntry == null, $"{ExchangeName} : Missing {asset}.");
            return cmcEntry;
        }

        protected virtual string CorrectAssetName(string asset)
        {
            // TODO: put this list to some config file.
            return asset.ToUpper()
                .Replace("BCC", "BCH")
                .Replace("YOYO", "YOYOW")
                .Replace("BQX", "ETHOS")
                .Replace("IOTA", "MIOTA")
                .Replace("VEN", "VET")
                .Replace("BCHABC", "BCH")
                .Replace("BCHSV", "BSV");
        }

        protected virtual bool IsValidMarket(SymbolInformation si)
        {
            // TODO: review this condition.
            return true;
        }

        private void ResetMarketSummaries()
        {
            foreach (var ticker in marketSummaries)
            {
                using (ticker.SuppressChangeNotifications())
                {
                    ticker.BuyVolume = 0m;
                    ticker.PriceChangePercent = 0m;
                    ticker.Volume = null;
                }
            }
        }

        protected void OnRefreshMarketSummary(PriceTicker ticker)
        {
            var idx = -1;
            if (!tickersMapping.TryGetValue(ticker.Symbol, out idx))
                return;
            var old = marketSummaries[idx];
            Debug.Assert(old.Symbol == ticker.Symbol);
#if GTK
            ticker.PrevLastPrice = old.LastPrice;
            marketSummaries[idx] = ticker;
            SymbolInformation market = null;
            Debug.Assert(symbols.TryGetValue(ticker.Symbol, out market));
            BalanceManager.UpdateWithLastPrice(market.ProperSymbol, ticker.LastPrice);
            //UpdateBalances();
#else
            old.LastPrice = ticker.LastPrice;
            if (ticker.Volume != null) old.Volume = ticker.Volume;
            if (ticker.PriceChangePercent != null) old.PriceChangePercent = ticker.PriceChangePercent;
            //UpdateBalances();
#endif
        }

        private void UpdateBalances()
        {
            foreach (var idx in tickersMapping.Values)
            {
                var ticker = MarketSummaries[idx];
                if (ticker.LastPrice > decimal.Zero && ticker.IsPriceChanged)
                {
                    BalanceManager.UpdateWithLastPrice(ticker.SymbolInformation.ProperSymbol, (ticker.Bid ?? ticker.LastPrice).Value);
                }
            }
        }

        public abstract bool FilterByMarket(string symbol, string market);
        public abstract bool FilterByAsset(string symbol, string asset);

        // *****************************
        // V2
        //
        protected virtual bool TickersAreMarketListDependable => false;

        protected abstract Task<IEnumerable<SymbolInformation>> GetMarketsAsync();

        protected abstract Task<IEnumerable<PriceTicker>> GetTickersAsync();

        protected virtual Task<IEnumerable<PriceTicker>> GetTickersAsync(IEnumerable<SymbolInformation> markets)
        {
            return GetTickersAsync();
        }

        protected abstract Task<IEnumerable<PublicTrade>> GetPublicTradesAsync(string market, int limit);

        protected abstract Task<IEnumerable<OrderBookEntry>> GetOrderBookAsync(string market, int limit);

        protected virtual IObservable<IEnumerable<PriceTicker>> ObserveTickers()
        {
            var obs = Observable.FromAsync(GetTickersAsync);
            return Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).SelectMany(x => obs);
        }

        protected virtual IObservable<IEnumerable<PriceTicker>> ObserveTickers(IEnumerable<SymbolInformation> markets)
        {
            var obs = Observable.FromAsync(() => GetTickersAsync(markets));
            return Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).SelectMany(x => obs);
        }

        protected virtual IObservable<IEnumerable<PublicTrade>> ObserveRecentTrades(string market, int limit)
        {
            var obs = Observable.FromAsync(() => GetPublicTradesAsync(market, TradesMaxItemCount));
            return Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).SelectMany(x => obs);
        }

        protected virtual IObservable<IEnumerable<OrderBookEntry>> ObserveDepth(string market, int limit)
        {
            var obs = Observable.FromAsync(() => GetOrderBookAsync(market, OrderBookMaxItemCount));
            return Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).SelectMany(x => obs);
        }

        protected virtual Task<List<Balance>> GetBalancesAsync()
        {
            return Task.Run(() => new List<Balance>());
        }

        protected virtual Task<List<Order>> GetOpenOrdersAsync(IEnumerable<string> markets)
        {
            return Task.Run(() => new List<Order>());
        }

        /*
         protected virtual void ManageOrderBook(IEnumerable<OrderBookEntry> bookUpdates)
         {
             var sw = Stopwatch.StartNew();

             if (OrderBook.IsEmpty)
             {
                 OrderBook.AddRange(bookUpdates);
                 return;
             }

             foreach (var e in OrderBook.ToList())
             {
                 if (!bookUpdates.Any(x => x.Price == e.Price))
                     OrderBook.Remove(e);
             }

             foreach (var e in bookUpdates)
             {
                 // add or update
                 var item = OrderBook.LastOrDefault(x => e.Side == x.Side && e.Price <= x.Price);
                 if (item != null)
                 {
                     if (e.Price == item.Price)
                         item.Quantity = e.Quantity;
                     else
                     {
                         var idx = OrderBook.IndexOf(item);
                         OrderBook.Insert(idx + 1, e);
                         //Debug.Print($"Inserting {e.Price} after {item.Price}");
                     }
                 }
                 else
                 {
                     if (e.Side == TradeSide.Sell)
                         OrderBook.Insert(0, e);
                     else
                     {
                         item = OrderBook.FirstOrDefault(x => x.Side == e.Side);
                         var idx = OrderBook.IndexOf(item);
                         OrderBook.Insert(idx, e);
                         //Debug.Print($"Inserting {e.Price} before {item.Price}");
                     }
                 }
             }
             Debug.Print($"ManageOrderBook took {sw.ElapsedMilliseconds}ms.");
         }
         */

        private IEnumerable<OrderBookEntry> AggregateOrderBook(IEnumerable<OrderBookEntry> orderBook, decimal factor)
        {
            var si = GetSymbolInformation(CurrentSymbol);

            return orderBook.GroupBy(x => MergePrice(x.Price, factor, x.Side), x => x,
                (priceLevel, entries) => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = priceLevel, Quantity = entries.Sum(y => y.Quantity), Side = entries.First().Side });
        }

        private static decimal MergePrice(decimal price, decimal factor, TradeSide side)
        {
            if (side == TradeSide.Buy)
            {
                return Math.Truncate(price * factor) / factor;
            }
            else
            {
                return Math.Ceiling(price * factor) / factor;
            }
        }

        private async void Initialize123()
        {
            IEnumerable<SymbolInformation> markets = null;
            IEnumerable<PriceTicker> tickers = null;
            try
            {
                IsInitializing = true;
                if (TickersAreMarketListDependable)
                {
                    markets = await GetMarketsAsync();
                    tickers = await GetTickersAsync(markets);
                }
                else
                {
                    var taskExchangeInfo = GetMarketsAsync();
                    var taskPriceTicker = GetTickersAsync();
                    await Task.WhenAll(taskExchangeInfo, taskPriceTicker);
                    markets = taskExchangeInfo.Result;
                    tickers = taskPriceTicker.Result;
                }
                ProcessExchangeInfo(markets);
                ProcessPriceTicker(tickers);
                //InitializeSigned();
            }
            finally
            {
                IsInitializing = false;
            }
        }

        private async void InitializeSigned()
        {
            // balance
            var balances = await GetBalancesAsync();
            BalanceManager.Balances.AddRange(balances);
            // open orders
            var orders = await GetOpenOrdersAsync(marketsMapping.Keys);
            OpenOrders.AddRange(orders);
            // orders history
            // trades history
        }

        protected virtual void SubscribeMarketData()
        {
            if (HasMarketSummariesPush)
            {

            }
            else if (HasMarketSummariesPull)
            {
                if (TickersAreMarketListDependable)
                    ObserveTickers(marketsMapping.Values).Subscribe(ProcessPriceTicker).DisposeWith(Disposables);
                else
                    ObserveTickers().Subscribe(ProcessPriceTicker).DisposeWith(Disposables);
            }

            var subCurrentSymbol = this.WhenAnyValue(vm => vm.CurrentSymbol).Where(id => id != null);
            subCurrentSymbol.Subscribe(SubscribeMarketData).DisposeWith(Disposables);
        }

        protected void SubscribeMarketData(string market)
        {
            // Not checking if already subscribed - SerialDisposable will do a dirty job for us.
            RecentTrades.Clear();
            OrderBook.Clear();
            var subOrderBookMergeDecimals = this.ObservableForProperty(vm => vm.OrderBookMergeDecimals);
            subOrderBookMergeDecimals.Subscribe(x => OrderBook.Clear()).DisposeWith(Disposables);
            //if (HasTradesPush)
            //{
            //    IsLoadingTrades = true;
            //    var obs = ObserveRecentTrades(market, TradesMaxItemCount);
            //    TradesHandle.Disposable = obs.ObserveOnDispatcher().Subscribe(ProcessPublicTradesPush);
            //    TradesHandle.Disposable.DisposeWith(Disposables);
            //}
            //else if (HasTradesPull)
            //{
            //    var obs = ObserveRecentTrades(market, TradesMaxItemCount);
            //    TradesHandle.Disposable = obs.ObserveOnDispatcher().Subscribe(ProcessPublicTrades);
            //    TradesHandle.Disposable.DisposeWith(Disposables);
            //}
            //if (HasOrderBookPush)
            //{
            //    IsLoadingOrderBook = true;
            //    var obs = ObserveDepth(market, OrderBookMaxItemCount);
            //    DepthHandle.Disposable = obs.ObserveOnDispatcher().Subscribe(ProcessOrderBookPush);
            //    DepthHandle.Disposable.DisposeWith(Disposables);
            //}
            //else if (HasOrderBookPull)
            //{
            //    var obs = ObserveDepth(market, OrderBookMaxItemCount);
            //    DepthHandle.Disposable = obs.ObserveOnDispatcher().Subscribe(ProcessOrderBook);
            //    DepthHandle.Disposable.DisposeWith(Disposables);
            //}
        }

        protected void Run(bool enable)
        {
            isActive = enable;
            TickersSubscribed = enable;
            TradesSubscribed = enable;
            DepthSubscribed = enable;
            if (!enable)
            {
                Status = "Stopped.";

                DoDispose();
            }
            else
            {
                Status = "Starting...";
                Subscribe();
                GetExchangeInfoCommand.Execute(null);

                //SubscribeMarketData();
            }
        }

        private void Subscribe()
        {
            var subCurrentSymbol = this.WhenAnyValue(vm => vm.CurrentSymbol).Where(id => id != null);
            subCurrentSymbol.Subscribe(SubscribeMarketData).DisposeWith(Disposables);
        }

        protected void SubscribeCommandIsExecuting(ReactiveCommand cmd)
        {
            Disposables.Add(cmd.IsExecuting.Skip(1).Subscribe(x => UpdateBusyCount(x)));
        }

        internal void ProcessExchangeInfo(IEnumerable<SymbolInformation> markets)
        {
            if (this.marketsMapping.Count == 0)
            {
                // a small optimization - since we know it is empty,
                // do not check every symbol to be in here.
                foreach (var market in markets)
                {
                    this.marketsMapping.TryAdd(market.Symbol, market);
                }
            }
            else
            {
                foreach (var market in markets)
                {
                    var id = market.Symbol;
                    if (this.marketsMapping.TryGetValue(id, out SymbolInformation oldMarket))
                    {
                        // update.
                        oldMarket.Status = market.Status;
                    }
                    else
                    {
                        // add new.
                        this.marketsMapping.TryAdd(market.Symbol, market);
                    }
                }
            }
            marketAssets.AddRange(markets.Where(IsValidMarket).Select(x => x.QuoteAsset).Distinct().Except(marketAssets));
        }

        internal void ProcessPriceTicker(IEnumerable<PriceTicker> priceTicker)
        {
            var sw = Stopwatch.StartNew();
            if (MarketSummaries.IsEmpty)
            {
                var idCache = new HashSet<string>(marketsMapping.Values.Where(IsValidMarket).Select(si => si.Symbol));
                MarketSummaries.AddRange(priceTicker.Where(t => idCache.Contains(t.Symbol)));
                for (int idx = 0; idx < MarketSummaries.Count; idx += 1)
                {
                    var ticker = MarketSummaries[idx];
                    Debug.Assert(tickersMapping.TryAdd(ticker.Symbol, idx));
                }
            }
            else
            {
                var iter = marketSummaries.GetEnumerator();
                foreach (var ticker in priceTicker)
                {
                    OnRefreshMarketSummary2(ticker);
                }
            }
            Debug.Print($"ProcessPriceTicker took {sw.ElapsedMilliseconds}ms.");
        }

        internal void ProcessPublicTrades(IEnumerable<PublicTrade> trades)
        {
            var tradesList = trades as List<PublicTrade> ?? trades.ToList();
            if (RecentTrades.All(x => x.SymbolInformation.Symbol != CurrentSymbol))
                RecentTrades.Clear();
            long? lastId = RecentTrades.FirstOrDefault()?.Id;
            if (lastId != null)
                tradesList.RemoveAll(x => x.Id <= lastId);
            using (RecentTrades.SuppressChangeNotifications())
            {
                if (tradesList.Count > 0)
                {
                    RecentTrades.InsertRange(0, tradesList);
                    if (RecentTrades.Count > TradesMaxItemCount)
                    {
                        RecentTrades.RemoveRange(TradesMaxItemCount, Math.Abs(TradesMaxItemCount - RecentTrades.Count));
                    }
                }
                CalcQuantityPercentageTotal(RecentTrades);
            }
        }

        internal void ProcessPublicTradesPush(IEnumerable<PublicTrade> trades)
        {
            IsLoadingTrades = false;
            var tradesList = trades as List<PublicTrade> ?? trades.ToList();
            if (RecentTrades.IsEmpty)
            {
                RecentTrades.AddRange(tradesList);
            }
            else
            {
                foreach (var trade in tradesList)
                {
                    RecentTrades.Insert(0, trade);
                    if (RecentTrades.Count > TradesMaxItemCount)
                    {
                        RecentTrades.Remove(recentTrades.Last());
                    }
                }
            }
            CalcQuantityPercentageTotal(RecentTrades);
        }

        private void CalcQuantityPercentageTotal(IEnumerable<PublicTrade> trades)
        {
            if (trades.Count() < 1) return;
            var totals = trades.Select(x => x.Total);
            decimal qmin = totals.Min();
            decimal qmax = totals.Max();
            foreach (var t in trades.ToList())
            {
                t.QuantityPercentage = OrderBook.CalcPercent(qmin, qmax, t.Total);
            }
        }

        internal void ProcessOrderBook(IEnumerable<OrderBookEntry> depth)
        {
            //var tmp = depth.ToList();
            //CalcCumulativeTotal(tmp);
            //using (OrderBook.SuppressChangeNotifications())
            //{
            //    OrderBook.Clear();
            //    OrderBook.AddRange(tmp);
            //}
            OrderBook.MergeDecimals = OrderBookMergeDecimals;
            OrderBook.Update(depth.ToList());
        }

        internal void ProcessOrderBookPush(IEnumerable<OrderBookEntry> depth)
        {
            IsLoadingOrderBook = false;
            OrderBook.MergeDecimals = OrderBookMergeDecimals;
            OrderBook.UpdateIncremental(depth.ToList());
        }

        internal static void CalcCumulativeTotal(IList<OrderBookEntry> entries)
        {
            if (entries.Count == 0)
                return;
            var total = decimal.Zero;
            decimal? maxAsk = entries.Where(x => x.Side == TradeSide.Sell).Select(x => x.Quantity).Max();
            foreach (var e in entries.Where(x => x.Side == TradeSide.Sell).Reverse().ToList())
            {
                total += e.Total;
                e.TotalCumulative = total;
                e.QuantityPercentage = CalcChangePercent(maxAsk.GetValueOrDefault(), e.Quantity);
            }
            total = decimal.Zero;
            decimal? maxBid = entries.Where(x => x.Side == TradeSide.Buy).Select(x => x.Quantity).Max();
            foreach (var e in entries.Where(x => x.Side == TradeSide.Buy).ToList())
            {
                total += e.Total;
                e.TotalCumulative = total;
                e.QuantityPercentage = CalcChangePercent(maxBid.GetValueOrDefault(), e.Quantity);
            }
        }

        internal static decimal CalcFactor(int num)
        {
            decimal result = 1m;
            if (num == 10)
                return 0.1m;
            else for (int idx = 1; idx <= num; idx += 1)
                    result = result * 10m;
            return result;
        }

        protected PriceTicker GetPriceTicker(string market)
        {
            if (tickersMapping.TryGetValue(market, out int idx))
            {
                return MarketSummaries[idx];
            }
            return null;
        }

        protected virtual Task<SymbolInformation> GetFullSymbolInformation()
        {
            return Task.FromResult(CurrentSymbolInformation);
        }

        protected async void OnRefreshMarketSummary2(PriceTicker ticker)
        {
            if (tickersMapping.TryGetValue(ticker.Symbol, out int idx))
            {
                PriceTicker oldTicker = MarketSummaries[idx];
                Debug.Assert(oldTicker.Symbol == ticker.Symbol);
                bool tickerChanged = IsTickerChanged(oldTicker, ticker);
#if GTK
                ticker.PrevLastPrice = oldTicker.LastPrice;
                marketSummaries[idx] = ticker;
#else
                oldTicker.HighPrice = ticker.HighPrice;
                oldTicker.LastPriceUsd = CalcUsdPrice(ticker.LastPrice.GetValueOrDefault(), ticker.SymbolInformation);
                oldTicker.LowPrice = ticker.LowPrice;
                oldTicker.PriceChange = ticker.PriceChange;
                oldTicker.QuoteVolume = ticker.QuoteVolume;
                oldTicker.WeightedAveragePrice = ticker.WeightedAveragePrice;
                oldTicker.LastPrice = ticker.LastPrice;
                oldTicker.Bid = ticker.Bid;
                oldTicker.Ask = ticker.Ask;
                if (ticker.Volume != null) oldTicker.Volume = ticker.Volume;
                if (ticker.PriceChangePercent != null) oldTicker.PriceChangePercent = ticker.PriceChangePercent;
#endif
                Debug.Assert(marketsMapping.TryGetValue(ticker.Symbol, out SymbolInformation market));
                if (tickerChanged)
                {
                    BalanceManager.UpdateWithLastPrice(market.ProperSymbol, ticker.Bid.GetValueOrDefault());
                    UpdateWithTicker(ticker);
                    await ProcessTradingRules(ticker);
                }
            }
            else if (marketsMapping.ContainsKey(ticker.Symbol))
            {
                // new PriceTicker?
                if (IsValidMarket(GetSymbolInformation(ticker.Symbol)))
                    AddMarketSummary(ticker);
            }
        }

        protected bool IsTickerChanged(PriceTicker oldTicker, PriceTicker newTicker)
        {
            return oldTicker.LastPrice != newTicker.LastPrice ||
                oldTicker.Bid != newTicker.Bid ||
                oldTicker.Ask != newTicker.Ask ||
                oldTicker.QuoteVolume != newTicker.QuoteVolume;
        }

        protected void OnTrade(PublicTrade trade)
        {
            recentTrades.Insert(0, trade);
            if (recentTrades.Count > TradesMaxItemCount)
            {
                recentTrades.RemoveRange(TradesMaxItemCount, Math.Abs(TradesMaxItemCount - recentTrades.Count));
            }
        }

        protected void UpdateWithTicker(PriceTicker ticker)
        {
            foreach (var order in OrderHistory.Where(x => x.SymbolInformation.Symbol == ticker.Symbol))
            {
                if (order.Side == TradeSide.Buy)
                    order.LastPrice = ticker.Bid.GetValueOrDefault();
                else
                    order.LastPrice = ticker.Ask.GetValueOrDefault();
            }
        }

        #region Trading Rules
        private readonly SemaphoreSlim @lock = new SemaphoreSlim(1, 1);

        private async Task ProcessTradingRules(IEnumerable<PriceTicker> tickers)
        {
            foreach (var proxy in TradingRuleProxies)
            {
                var ticker = tickers.SingleOrDefault(x => x.Symbol.Equals(proxy.Rule.Market, StringComparison.CurrentCultureIgnoreCase));
                if (ticker != null)
                {
                    await ProcessTradingRule(proxy, ticker);
                }
            }
        }

        private async Task ProcessTradingRules(PriceTicker ticker)
        {
            foreach (var proxy in TradingRuleProxies.Where(x => x.Rule.Market.Equals(ticker.Symbol, StringComparison.CurrentCultureIgnoreCase)))
            {
                await ProcessTradingRule(proxy, ticker);
            }
        }

        private async Task ProcessTradingRule(TradingRuleProxy proxy, PriceTicker ticker)
        {

            const int MAX_CONFIRMS = 5;
            try
            {
                var rule = proxy.Rule;
                await @lock.WaitAsync();
                if (rule.IsApplicable(ticker))
                {
                    proxy.Confirmations += 1;
                    proxy.Status = $"Triggered #{proxy.Confirmations}/{MAX_CONFIRMS} times";
                    if (proxy.Confirmations < MAX_CONFIRMS)
                        return;
                    try
                    {
                        Debug.Print("{4} Placing {0} {3} {1} by {2}.", rule.OrderSide, ticker.Symbol, rule.OrderRate, rule.OrderVolume, DateTime.Now);
                        rule.OrderId = await PlaceOrder(rule);
                        if (rule.OrderId == null)
                            rule.IsActive = false;
                        else
                            proxy.Status += $"; Placed order #{rule.OrderId}";
                    }
                    catch (Exception ex)
                    {
                        Debug.Print(ex.ToString());
                        rule.IsActive = false;
                        proxy.Status += $"; Order NOT placed: {ex.Message}";
                    }
                }
                else if (rule.IsActive && rule.OrderId == null)
                {
                    proxy.Status = rule.GetStatus();
                }
            }
            finally
            {
                @lock.Release();
            }
        }

        protected virtual async Task<string> PlaceOrder(TradingRule rule)
        {
            await Task.CompletedTask;
            return null;
        }

        protected void AddRule(TradingRule rule)
        {
            TradingRuleProxies.Add(new TradingRuleProxy(rule));
        }

        protected virtual Task RefreshCommandExecute(int x)
        {
            return Task.CompletedTask;
        }

        protected virtual Task RefreshPrivateDataExecute()
        {
            return Task.CompletedTask;
        }

        private void CreateRuleExecute(object param)
        {
            var wnd = new System.Windows.Window
            {
                Content = new Terminal.WPF.CreateRule() { Margin = new System.Windows.Thickness(6) },
                Owner = System.Windows.Application.Current.MainWindow,
                SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
                Title = "Create Rule",
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                WindowStyle = System.Windows.WindowStyle.ToolWindow
            };
            var viewModel = this;
            var order = new NewOrder(viewModel.CurrentSymbolInformation);
            viewModel.NewOrder = order;
            wnd.DataContext = viewModel;
            wnd.ShowDialog();
        }

        public Interaction<TradeTaskViewModel, bool> CreateTask { get; } = new Interaction<TradeTaskViewModel, bool>();

        private async Task CreateTradeTaskExecute(string param)
        {
            var si = await GetFullSymbolInformation();
            var viewModel = new TradeTaskViewModel(si, ExchangeName);

            var ok = await CreateTask.Handle(viewModel);
            if (ok)
                TradeTasks.Add(viewModel);
        }

        private void SubmitRuleExecute(object param)
        {
            if (NewOrder != null)
            {
                var direction = NewOrder.Side == TradeSide.Buy ? 100m : -100m;
                var rule = new TrailingTakeProfit(NewOrder.Price, NewOrder.QuantityPercentage / direction);
                rule.Market = NewOrder.SymbolInformation.Symbol;
                rule.Property = ThresholdType.LastPrice;
                rule.OrderVolume = NewOrder.Quantity;
                rule.OrderSide = NewOrder.Side;
                rule.OrderType = NewOrder.OrderType;
                AddRule(rule);

            }
        }
#endregion

        protected void DoDispose()
        {
            disposablesHandle.Disposable = null;
            //(disposablesHandle.Disposable as CompositeDisposable)?.Dispose();
        }

        internal void AddMarketSummary(PriceTicker ticker)
        {
            if (tickersMapping.TryAdd(ticker.Symbol, MarketSummaries.Count))
                MarketSummaries.Add(ticker);
        }

        protected decimal CalcUsdPrice(decimal price, SymbolInformation si)
        {
            if (si.QuoteAsset.StartsWith("USD", StringComparison.CurrentCultureIgnoreCase))
            {
                return price;
            }
            var pairUsd = marketsMapping.Values.FirstOrDefault(x => si.BaseAsset == x.BaseAsset && x.QuoteAsset.StartsWith("USD", StringComparison.CurrentCultureIgnoreCase));
            if (pairUsd == null)
            {
                pairUsd = marketsMapping.Values.FirstOrDefault(x => si.QuoteAsset == x.BaseAsset && x.QuoteAsset.StartsWith("USD", StringComparison.CurrentCultureIgnoreCase));
            }
            else
                price = 1m;
            if (pairUsd != null && tickersMapping.TryGetValue(pairUsd.Symbol, out int idx))
            {
                return MarketSummaries[idx].LastPrice.GetValueOrDefault() * price;
            }
            return 0m;
        }

        protected static decimal CalcChangePercent(decimal x, decimal y)
        {
            if (x == decimal.Zero || y == decimal.Zero)
                return decimal.Zero;
            return Math.Round(((x / y) - 1m) * 100m, 2);
        }

        protected static int DigitsCount(decimal value)
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


        private ConcurrentDictionary<string, SymbolInformation> marketsMapping = new ConcurrentDictionary<string, SymbolInformation>();
        private ConcurrentDictionary<string, int> tickersMapping = new ConcurrentDictionary<string, int>(); // maps SYMBOL to index of ticker in Tickers.

        protected CompositeDisposable Disposables
        {
            get
            {
                if (disposablesHandle.Disposable is null)
                    disposablesHandle.Disposable = new CompositeDisposable();
                return disposablesHandle.Disposable as CompositeDisposable;
            }
        }

        protected SerialDisposable TradesHandle { get; } = new SerialDisposable();
        protected SerialDisposable DepthHandle { get; } = new SerialDisposable();
        SerialDisposable disposablesHandle = new SerialDisposable();

        protected static List<CoinMarketCap.PublicAPI.Listing> cmc_listing { get; } = CoinMarketCapApiClient.GetListings();
        protected IEnumerable<string> AllSymbols => marketsMapping.Keys;

        // *************************
        // V3

        public double GetExchangeInfoElapsed
        {
            get { return _GetExchangeInfoElapsed; }
            set { this.RaiseAndSetIfChanged(ref _GetExchangeInfoElapsed, value); }
        }

        public double GetTickersElapsed
        {
            get { return _GetTickersElapsed; }
            set { this.RaiseAndSetIfChanged(ref _GetTickersElapsed, value); }
        }

        public double GetTradesElapsed
        {
            get { return _GetTradesElapsed; }
            set { this.RaiseAndSetIfChanged(ref _GetTradesElapsed, value); }
        }

        public double GetDepthElapsed
        {
            get { return _GetDepthElapsed; }
            set { this.RaiseAndSetIfChanged(ref _GetDepthElapsed, value); }
        }

        public ICommand GetExchangeInfoCommand { get; private set; }
        public ICommand GetTickersCommand { get; private set; }
        public ICommand GetTradesCommand { get; private set; }
        public ICommand GetDepthCommand { get; private set; }

        public bool TickersSubscribed
        {
            get { return tickersSubscribed; }
            set { this.RaiseAndSetIfChanged(ref tickersSubscribed, value); }
        }

        public bool TradesSubscribed
        {
            get { return tradesSubscribed; }
            set { this.RaiseAndSetIfChanged(ref tradesSubscribed, value); }
        }

        public bool DepthSubscribed
        {
            get { return depthSubscribed; }
            set { this.RaiseAndSetIfChanged(ref depthSubscribed, value); }
        }

        public bool PrivateDataSubscribed
        {
            get { return privateDataSubscribed; }
            set { this.RaiseAndSetIfChanged(ref privateDataSubscribed, value); }
        }

        protected void Initialize()
        {
            GetExchangeInfoCommand = ReactiveCommand.CreateFromTask(GetExchangeInfo);
            GetTickersCommand = ReactiveCommand.CreateFromTask(GetTickers);
            GetTradesCommand = ReactiveCommand.CreateFromTask(GetTrades);
            GetDepthCommand = ReactiveCommand.CreateFromTask(GetDepth);

            this.ObservableForProperty(x => x.TickersSubscribed).Subscribe(x => SetTickersSubscription(x.Value));
            this.ObservableForProperty(x => x.TradesSubscribed).Subscribe(x => SetTradesSubscription(x.Value));
            this.ObservableForProperty(x => x.DepthSubscribed).Subscribe(x => SetDepthSubscription(x.Value));
            this.ObservableForProperty(x => x.PrivateDataSubscribed).Subscribe(x => SetPrivateDataSubscription(x.Value));

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            IsInitializing = true;
            await GetExchangeInfo().ConfigureAwait(false);
            await GetTickers().ConfigureAwait(false);
            IsInitializing = false;
            //SetTickersSubscription(true);

            foreach (var file in Directory.EnumerateFiles(".", "????????-*.json"))
            {
                var json = File.ReadAllText(file);
                var taskModel = TradeTaskViewModel.DeserializeModel(json);
                if (taskModel.Exchange == ExchangeName)
                {
                    var si = GetSymbolInformation(taskModel.Symbol);
                    TradeTasks.Add(new TradeTaskViewModel(si, taskModel));
                }
            }
        }

        protected virtual Task GetExchangeInfo()
        {
            return Task.CompletedTask;
        }

        protected virtual Task GetTickers()
        {
            return Task.CompletedTask;
        }

        protected virtual Task GetTrades()
        {
            return Task.CompletedTask;
        }

        protected virtual IObservable<PriceTicker> ObserveTickers123()
        {
            return Observable.Empty<PriceTicker>();
        }

        protected virtual Task GetDepth()
        {
            return Task.CompletedTask;
        }

        protected virtual Task CancelOrder(string orderId)
        {
            return Task.CompletedTask;
        }

        protected virtual Task SubmitOrder(NewOrder order)
        {
            return Task.CompletedTask;
        }

        protected void SetTickersSubscription(bool isEnabled)
        {
            if (HasMarketSummariesPush)
            {
                SetTickersSubscriptionWebSocket(isEnabled);
                return;
            }

            if (isEnabled)
                getTickersSubscription =
                    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(2))
                              .Select(x => Unit.Default)
                              .ObserveOnDispatcher()
                              .InvokeCommand(GetTickersCommand);
            else if (getTickersSubscription != null)
                getTickersSubscription.Dispose();
        }

        protected void SetTickersSubscriptionWebSocket(bool isEnabled)
        {
            if (isEnabled)
                getTickersSubscription = ObserveTickers123()
                                        .Subscribe(OnRefreshMarketSummary2);

            else if (getTickersSubscription != null)
                getTickersSubscription.Dispose();
        }

        protected void SetTradesSubscription(bool isEnabled)
        {
            if (isEnabled)
                getTradesSubscription =
                    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(2))
                              .Select(x => Unit.Default)
                              .ObserveOnDispatcher()
                              .InvokeCommand(GetTradesCommand);
            else if (getTradesSubscription != null)
                getTradesSubscription.Dispose();
        }

        protected void SetDepthSubscription(bool isEnabled)
        {
            if (isEnabled)
                getDepthSubscription =
                    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(2))
                              .Select(x => Unit.Default)
                              .ObserveOnDispatcher()
                              .InvokeCommand(GetDepthCommand);
            else if (getDepthSubscription != null)
                getDepthSubscription.Dispose();
        }

        protected void SetPrivateDataSubscription(bool isEnabled)
        {
            if (isEnabled)
                getPrivateDataSubscription =
                    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(6))
                              .Select(x => Unit.Default)
                              .ObserveOnDispatcher()
                              .InvokeCommand(RefreshPrivateDataCommand);
            else if (getPrivateDataSubscription != null)
                getPrivateDataSubscription.Dispose();
        }

        protected virtual Task<Balance> GetBalance(string asset)
        {
            return Task.FromResult(new Balance(asset));
        }

        public virtual string[] OrderTypes => new string[] { "limit", "market", "fill-or-kill" };

        public NewOrder NewOrder { get; set; }

        protected IEnumerable<SymbolInformation> ValidPairs => marketsMapping.Values.Where(IsValidMarket);


        private double _GetExchangeInfoElapsed;
        private double _GetTickersElapsed;
        private double _GetTradesElapsed;
        private double _GetDepthElapsed;
        IDisposable getTickersSubscription;
        IDisposable getTradesSubscription;
        IDisposable getDepthSubscription;
        IDisposable getPrivateDataSubscription;
        private bool tickersSubscribed;
        private bool tradesSubscribed;
        private bool depthSubscribed;
        private bool privateDataSubscribed;

#if !GTK
        public ViewModelActivator Activator => viewModelActivator;
#endif
    }

    public class TradingRule
    {
        public string Market { get; set; }
        public decimal ThresholdRate { get; set; }
        public ThresholdType Property { get; set; }
        public ThresholdOperator Operator { get; set; }
        public TradeSide OrderSide { get; set; }
        public string OrderType { get; set; }
        public decimal OrderRate { get; set; } // Sell or Buy using this rate. 0 for Market Order.
        public decimal OrderVolume { get; set; }
        public decimal RemainingVolume { get; set; }

        public string OrderId { get; set; } // <null> if not yet placed.
        public bool IsActive { get; set; } = true;

        public virtual bool IsApplicable(PriceTicker ticker)
        {
            if (!Market.Equals(ticker.Symbol, StringComparison.CurrentCultureIgnoreCase))
                return false;
            if (!IsActive)
                return false;
            if (OrderId != null)
                return false;
            decimal? value = GetPropertyValue(ticker);
            if (value == null)
                return false;
            switch (Operator)
            {
                case ThresholdOperator.Equal:
                    return value == ThresholdRate;
                case ThresholdOperator.Greater:
                    return value > ThresholdRate;
                case ThresholdOperator.GreaterOrEqual:
                    return value >= ThresholdRate;
                case ThresholdOperator.Less:
                    return value < ThresholdRate;
                case ThresholdOperator.LessOrEqual:
                    return value <= ThresholdRate;
                default:
                    return false;
            }
        }

        protected virtual decimal? GetPropertyValue(PriceTicker ticker)
        {
            decimal? value = null;
            switch (Property)
            {
                case ThresholdType.AskPrice:
                    value = ticker.Ask;
                    break;
                case ThresholdType.BidPrice:
                    value = ticker.Bid;
                    break;
                case ThresholdType.LastPrice:
                    value = ticker.LastPrice;
                    break;
            }
            return value;
        }

        public virtual string GetStatus()
        {
            return "Waiting...";
        }
    }

    public class TrailingTakeProfit : TradingRule
    {
        public decimal TakeProfitPrice { get; set; }
        public decimal TrailingPercent { get; set; }

        public override bool IsApplicable(PriceTicker ticker)
        {
            // if current Ask/Bid/LastPrice >= TakeProfitPrice then
            // activate Trailing.
            // TrailingPrice starts with TakeProfitPrice - (TakeProfitPrice * TrailingPercent)
            decimal? value = GetPropertyValue(ticker);
            value = value + value * TrailingPercent;
            if (!trailingActivated)
            {
                if (base.IsApplicable(ticker))
                {
                    // we reached TakeProfitPrice. Activate Trailing.
                    trailingActivated = true;
                    Operator = TrailingPercent > 0 ? ThresholdOperator.Greater : ThresholdOperator.Less;
                    ThresholdRate = value.GetValueOrDefault();
                }
                return false;
            }
            else
            {
                if (TrailingPercent > 0)
                {
                    if (value < ThresholdRate)
                        ThresholdRate = value.GetValueOrDefault();
                }
                else
                {
                    if (value > ThresholdRate)
                        ThresholdRate = value.GetValueOrDefault();
                }
                return base.IsApplicable(ticker);
            }
        }

        public override string GetStatus()
        {
            if (!trailingActivated)
                return base.GetStatus();
            else
                return $"Trailing activated. TTP price: {ThresholdRate}";
        }

        public TrailingTakeProfit(decimal tpp, decimal tp)
        {
            TakeProfitPrice = tpp;
            TrailingPercent = tp;
            Operator = tp > 0 ? ThresholdOperator.LessOrEqual : ThresholdOperator.GreaterOrEqual;
            ThresholdRate = TakeProfitPrice;
            OrderRate = TakeProfitPrice;
            OrderSide = TradeSide.Sell;
        }

        private bool trailingActivated;
    }

    public class TradingRuleProxy : ReactiveObject
    {
        TradingRule rule;

        public TradingRuleProxy(TradingRule rule)
        {
            this.rule = rule;
        }

        public int Confirmations { get; set; }
        public string Status
        {
            get { return _status; }
            set { this.RaiseAndSetIfChanged(ref _status, value); }
        }

        public TradingRule Rule => rule;
        public string Symbol => rule.Market;
        public string Condition
        {
            get
            {
                switch (rule.Operator)
                {
                    case ThresholdOperator.Equal:
                        return rule.Property + " = " + rule.ThresholdRate;
                    case ThresholdOperator.Greater:
                        return rule.Property + " > " + rule.ThresholdRate;
                    case ThresholdOperator.GreaterOrEqual:
                        return rule.Property + " >= " + rule.ThresholdRate;
                    case ThresholdOperator.Less:
                        return rule.Property + " < " + rule.ThresholdRate;
                    case ThresholdOperator.LessOrEqual:
                        return rule.Property + " <= " + rule.ThresholdRate;
                    default:
                        return "Unexpected";
                }
            }
        }

        private string _status;
    }

    public enum ThresholdType
    {
        LastPrice,
        BidPrice,
        AskPrice
    }

    public enum ThresholdOperator
    {
        Less,
        LessOrEqual,
        Equal,
        Greater,
        GreaterOrEqual
    }

    public enum OrderStatus
    {
        Active,
        Filled,
        PartiallyFilled,
        Rejected,
        Cancelled
    }

    public static class ReactiveListExtensions
    {
        public static void EnableThreadSafety<T>(this ReactiveList<T> list)
        {
            BindingOperations.EnableCollectionSynchronization(list, (list as ICollection).SyncRoot);
        }
    }
}
