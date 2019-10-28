using DynamicData;
using DynamicData.Binding;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Legacy;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;

namespace Exchange.Net
{
    public abstract partial class ExchangeViewModel : ReactiveObject, ISupportsActivation
    {

        // NOTE: below props should be NORMAL props since they has to be changed from UI.
        public abstract string ExchangeName { get; }
        public virtual int OrderBookMaxItemCount { get; set; } = 25;
        public TimeSpan OrderBookRetrieveInterval => TimeSpan.FromSeconds(3);
        public int TradesMaxItemCount { get; set; } = 50;
        public TimeSpan TradesRetrieveInterval => TimeSpan.FromSeconds(1);
        public string PriceTickerFilter { get; set; }
        public TimeSpan PriceTickerRetrieveInterval => TimeSpan.FromSeconds(1);

        public virtual int[] OrderBookMergeDecimalsList => new int[] { 10, 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        public virtual int[] OrderBookSizeList => new int[] { 5, 10, 25, 50, 100 };
        public virtual int[] RecentTradesSizeList => new int[] { 5, 10, 25, 50, 100 };
        public TradeSide[] TradeSides => new TradeSide[] { TradeSide.Buy, TradeSide.Sell };
        // Public Data
        public IEnumerable<SymbolInformation> Markets => marketsMapping?.Values;
        public ReactiveList<string> MarketAssets => marketAssets;
        public ReactiveList<PriceTicker> MarketSummaries => marketSummaries;
        public ReactiveList<PublicTrade> RecentTrades => recentTrades;
        public SourceCache<PublicTrade, long> RecentTradesCache { get; }
        public ReadOnlyObservableCollection<PublicTrade> RecentTradesView => recentTradesView;
        public OrderBook OrderBook { get; }
        public string CurrentSymbol
        {
            get { return this.currentSymbol; }
            set { if (value != null) this.RaiseAndSetIfChanged(ref this.currentSymbol, value); }
        }
        public PriceTicker CurrentSymbolTickerPrice
        {
            get { return this.currentSymboTickerPrice; }
            set { if (value != null) this.RaiseAndSetIfChanged(ref this.currentSymboTickerPrice, value); }
        }
        public ReactiveList<TradingRuleProxy> TradingRuleProxies => tradingRuleProxies;
        public SourceCache<ExchangeAccount, string> Accounts { get; }
        public ReadOnlyObservableCollection<ExchangeAccountViewModel> AccountsViewModels => accViewModels;

        [Reactive] public bool IsActive { get; set; }
        [Reactive] public bool IsInitializing { get; set; }
        [Reactive] public bool IsLoadingOrderBook { get; set; }
        [Reactive] public bool IsLoadingTrades { get; set; }
        [Reactive] public bool TickersSubscribed { get; set; }
        [Reactive] public bool TradesSubscribed { get; set; }
        [Reactive] public bool DepthSubscribed { get; set; }
        [Reactive] public bool PrivateDataSubscribed { get; set; }
        [Reactive] public string Status { get; set; }
        [Reactive] public string CurrentMarket { get; set; }
        [Reactive] public string CurrentMarketSummariesPeriod { get; set; }
        [Reactive] public string MarketFilter { get; set; }
        [Reactive] public TimeSpan RefreshMarketSummariesElapsed { get; set; }
        [Reactive] public TimeSpan RefreshTradesElapsed { get; set; }
        [Reactive] public TimeSpan RefreshDepositsElapsed { get; set; }
        [Reactive] public int OrderBookMergeDecimals { get; set; }
        [Reactive] public double GetExchangeInfoElapsed { get; set; }
        [Reactive] public double GetTickersElapsed { get; set; }
        [Reactive] public double GetTradesElapsed { get; set; }
        [Reactive] public double GetDepthElapsed { get; set; }
        [Reactive] public SymbolInformation CurrentSymbolInformation { get; set; }
        [Reactive] public ExchangeAccount CurrentAccount { get; set; }
        [Reactive] public ExchangeAccountViewModel CurrentAccountViewModel { get; set; }
        [ObservableAsProperty] public bool IsGetOpenOrdersExecuting { get; }

        [Reactive] public bool HasSignedAccount { get; set; }

        protected abstract string DefaultMarket { get; }
        protected List<string> UsdAssets = new List<string>();

        protected virtual bool HasMarketSummariesPull => true;
        protected virtual bool HasMarketSummariesPush => false;
        protected virtual bool HasTradesPull => true;
        protected virtual bool HasTradesPush => false;
        protected virtual bool HasOrderBookPull => true;
        protected virtual bool HasOrderBookPush => false;

        public ReactiveCommand<string, Unit> SetCurrentMarketCommand { get; private set; }
        public ReactiveCommand<string, Unit> SetCurrentSymbolCommand { get; private set; }
        public ReactiveCommand<bool, Unit> SetActiveCommand { get; private set; }
        public ReactiveCommand<long, IEnumerable<SymbolInformation>> GetMarketsCommand { get; private set; }
        public ICommand GetOrderBookCommand { get; private set; }
        public ReactiveCommand<Unit, Unit> GetOpenOrders { get; private set; }
        public ReactiveCommand<Unit, Unit> GetDeposits { get; private set; }
        public ReactiveCommand<Unit, Unit> GetWithdrawals { get; private set; }
        public ReactiveCommand<Unit, Unit> GetOrdersHistory { get; private set; }
        public ReactiveCommand<Unit, Unit> GetTradesHistory { get; private set; }
        public ReactiveCommand<Unit, Unit> GetTradesStatistics { get; private set; }
        public ReactiveCommand<Unit, Unit> GetBalance { get; private set; }
        public ReactiveCommand<Order, Unit> CancelOrderCommand { get; private set; }
        public ReactiveCommand<NewOrder, Unit> SubmitOrderCommand { get; private set; }
        public ReactiveCommand<int, Unit> RefreshCommand { get; private set; }
        public ReactiveCommand<Unit, Unit> RefreshPrivateDataCommand { get; private set; }
        public ReactiveCommand<Unit, Unit> GetExchangeInfo { get; private set; }
        public ReactiveCommand<Unit, Unit> GetTickers { get; private set; }
        public ReactiveCommand<Unit, Unit> GetTradesCommand { get; private set; }
        public ReactiveCommand<Unit, Unit> GetDepthCommand { get; private set; }


        ReactiveList<PublicTrade> recentTrades;
        ReactiveList<PriceTicker> marketSummaries;
        ReactiveList<string> marketAssets;
        ReactiveList<TradingRuleProxy> tradingRuleProxies = new ReactiveList<TradingRuleProxy>();
        ReadOnlyObservableCollection<PublicTrade> recentTradesView;
        ReadOnlyObservableCollection<ExchangeAccountViewModel> accViewModels;
        string currentSymbol;
        PriceTicker currentSymboTickerPrice;

#if !GTK
        readonly ViewModelActivator viewModelActivator = new ViewModelActivator();
#endif

        public ExchangeViewModel()
        {
            Accounts = new SourceCache<ExchangeAccount, string>(x => x.Name);

            CurrentMarket = DefaultMarket;
            CurrentMarketSummariesPeriod = "30m";

            marketAssets = new ReactiveList<string>();
            marketSummaries = new ReactiveList<PriceTicker>();
            recentTrades = new ReactiveList<PublicTrade>();
            RecentTradesCache = new SourceCache<PublicTrade, long>(x => x.Id);
            OrderBook = new OrderBook(null);

            InitializeTradeController();

            MarketAssets.EnableThreadSafety();
            MarketSummaries.EnableThreadSafety();
            OrderBook.Bids.EnableThreadSafety();
            OrderBook.Asks.EnableThreadSafety();

            this.WhenActivated(disposables =>
            {
                Accounts
                    .Connect()
                    .Transform(x => new ExchangeAccountViewModel(x))
                    .Bind(out accViewModels)
                    .Subscribe()
                    .DisposeWith(disposables);

                RecentTradesCache
                    .Connect()
                    .Sort(SortExpressionComparer<PublicTrade>.Descending(t => t.Id))
                    .ObserveOnDispatcher()
                    .Bind(out recentTradesView)
                    .Subscribe()
                    .DisposeWith(disposables);

                var signedDependableCommandCanExecute = this.WhenAnyValue(x => x.HasSignedAccount);
                var symbolDependableCommandCanExecute = this.WhenAnyValue(x => x.HasSignedAccount, y => y.CurrentSymbol, (x, y) => x && !string.IsNullOrWhiteSpace(y)).DistinctUntilChanged();
                var tradeTaskCommandCanExecute = this.WhenAnyValue(x => x.SelectedTradeTask, (TradeTaskViewModel y) => y != null).DistinctUntilChanged();

                SetCurrentMarketCommand = ReactiveCommand.Create<string>(x => CurrentMarket = x).DisposeWith(disposables);
                SetCurrentSymbolCommand = ReactiveCommand.Create<string>(x => { CurrentSymbol = x; CurrentSymbolInformation = GetSymbolInformation(x); }).DisposeWith(disposables);
                SetActiveCommand = ReactiveCommand.Create<bool>(x => Run(x)).DisposeWith(disposables);
                GetMarketsCommand = ReactiveCommand.CreateFromTask<long, IEnumerable<SymbolInformation>>(x => GetMarketsAsync()).DisposeWith(disposables);
                //getTickersCommand = ReactiveCommand.CreateFromTask<long, IEnumerable<PriceTicker>>(x => GetTickersAsync()).DisposeWith(disposables);
                RefreshCommand = ReactiveCommand.CreateFromTask<int>(RefreshCommandExecute).DisposeWith(disposables);
                // TODO: Make below as ReactiveCommand instead of ICommand
                // and add DisposeWith to WhenActivated() section.
                RefreshPrivateDataCommand = ReactiveCommand.CreateFromTask(RefreshPrivateDataExecute).DisposeWith(disposables);
                CreateRule = ReactiveCommand.CreateFromTask<string>(CreateRuleImpl/*, symbolDependableCommandCanExecute*/).DisposeWith(disposables);
                CreateTradeTask = ReactiveCommand.CreateFromTask(CreateTradeTaskImpl, symbolDependableCommandCanExecute).DisposeWith(disposables);
                SubmitRuleCommand = ReactiveCommand.Create<object>(SubmitRuleExecute).DisposeWith(disposables);
                // public data
                GetExchangeInfo = ReactiveCommand.CreateFromTask(GetExchangeInfoImpl).DisposeWith(disposables);
                GetTickers = ReactiveCommand.CreateFromTask(GetTickersImpl).DisposeWith(disposables);
                GetTradesCommand = ReactiveCommand.CreateFromTask(GetTradesImpl).DisposeWith(disposables);
                GetDepthCommand = ReactiveCommand.CreateFromTask(GetDepthImpl).DisposeWith(disposables);
                // signed data
                GetOpenOrders = ReactiveCommand.CreateFromTask(GetOpenOrdersImpl, signedDependableCommandCanExecute).DisposeWith(disposables);
                GetOrdersHistory = ReactiveCommand.CreateFromTask(GetOrdersHistoryImpl, signedDependableCommandCanExecute).DisposeWith(disposables);
                GetTradesHistory = ReactiveCommand.CreateFromTask(GetTradesHistoryImpl, symbolDependableCommandCanExecute).DisposeWith(disposables);
                GetTradesStatistics = ReactiveCommand.CreateFromTask(GetTradesStatisticsImpl, symbolDependableCommandCanExecute).DisposeWith(disposables);
                GetDeposits = ReactiveCommand.CreateFromTask(GetDepositsImpl, signedDependableCommandCanExecute).DisposeWith(disposables);
                GetWithdrawals = ReactiveCommand.CreateFromTask(GetWithdrawalsImpl, signedDependableCommandCanExecute).DisposeWith(disposables);
                GetBalance = ReactiveCommand.CreateFromTask(GetBalanceImpl, signedDependableCommandCanExecute).DisposeWith(disposables);
                CancelOrderCommand = ReactiveCommand.CreateFromTask<Order>(CancelOrderImpl).DisposeWith(disposables);
                SubmitOrderCommand = ReactiveCommand.CreateFromTask<NewOrder>(SubmitOrderImpl).DisposeWith(disposables);

                EnableTradeTask = ReactiveCommand.Create(EnableTradeTaskImpl, tradeTaskCommandCanExecute).DisposeWith(disposables);
                PanicSellTradeTask = ReactiveCommand.CreateFromTask(PanicSellTradeTaskImpl, tradeTaskCommandCanExecute).DisposeWith(disposables);
                DeleteTradeTask = ReactiveCommand.CreateFromTask(DeleteTradeTaskImpl, tradeTaskCommandCanExecute).DisposeWith(disposables);
                DeleteRule = ReactiveCommand.Create<TradingRuleProxy>(DeleteRuleImpl).DisposeWith(disposables);
                NavigateToTradingView = ReactiveCommand.Create(NavigateToTradingViewImpl).DisposeWith(disposables);

                GetOpenOrders.IsExecuting.ToPropertyEx(this, x => x.IsGetOpenOrdersExecuting).DisposeWith(disposables);

                var subSI = this.ObservableForProperty(vm => vm.CurrentSymbolInformation)
                    .Subscribe(x =>
                    {
                        OrderBook.SymbolInformation = x.Value;
                        OrderBook.Bids.Clear();
                        OrderBook.Asks.Clear();
                        OrderBookMergeDecimals = x.Value.PriceDecimals;
                        RecentTradesCache.Clear();
                    }).DisposeWith(disposables);
                var subMD = this.ObservableForProperty(vm => vm.OrderBookMergeDecimals)
                    .Subscribe(x => OrderBook.MergeDecimals = x.Value).DisposeWith(disposables);
                subSI.DisposeWith(disposables);
                subMD.DisposeWith(disposables);

                RefreshCommand.ThrownExceptions.Subscribe(OnCommandException).DisposeWith(disposables);
                GetOpenOrders.ThrownExceptions.Subscribe(OnCommandException).DisposeWith(disposables);

                this.WhenAnyValue(vm => vm.CurrentAccount).Subscribe(x => CurrentAccountViewModel = AccountsViewModels.FirstOrDefault(y => y.Account == x));
                this.WhenAnyValue(vm => vm.CurrentSymbolTickerPrice).Where(x => x != null).Select(x => x.Symbol).InvokeCommand(SetCurrentSymbolCommand);

                this.ObservableForProperty(x => x.TickersSubscribed).Subscribe(x => SetTickersSubscription(x.Value)).DisposeWith(disposables);
                this.ObservableForProperty(x => x.TradesSubscribed).Subscribe(x => SetTradesSubscription(x.Value)).DisposeWith(disposables);
                this.ObservableForProperty(x => x.DepthSubscribed).Subscribe(x => SetDepthSubscription(x.Value)).DisposeWith(disposables);
                this.ObservableForProperty(x => x.PrivateDataSubscribed).Subscribe(x => SetPrivateDataSubscription(x.Value)).DisposeWith(disposables);

                Activate(disposables);
            });
        }

        private async void OnCommandException(Exception ex)
        {
            await ShowException.Handle(ex);
        }

        bool isActive = false;
        public void Activate(CompositeDisposable disposables)
        {
            disposablesHandle.DisposeWith(disposables);
            getTickersSubscription.DisposeWith(disposables);
            Initialize(disposables);
            //if (isActive)
            //    DoDispose();
            //else
            //    ;// Run();
            //isActive = !isActive;
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
                .Replace("BCHSV", "BSV")
                .Replace("GXS", "GXC")
                .Replace("PHB", "PHX");
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
                    foreach (var mngr in Accounts.Items.Select(x => x.BalanceManager))
                    {
                        mngr.UpdateWithLastPrice(ticker.SymbolInformation.ProperSymbol, (ticker.Bid ?? ticker.LastPrice).Value);
                    }
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
                (priceLevel, entries) => new OrderBookEntry(si) { Price = priceLevel, Quantity = entries.Sum(y => y.Quantity), Side = entries.First().Side });
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

        //private async void InitializeSigned()
        //{
        //    // balance
        //    var balances = await GetBalancesAsync();
        //    BalanceManager.Balances.AddRange(balances);
        //    // open orders
        //    var orders = await GetOpenOrdersAsync(marketsMapping.Keys);
        //    OpenOrders.AddRange(orders);
        //    // orders history
        //    // trades history
        //}

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
                GetExchangeInfo.Execute();

                //SubscribeMarketData();
            }
        }

        private void Subscribe()
        {
            var subCurrentSymbol = this.WhenAnyValue(vm => vm.CurrentSymbol).Where(id => id != null);
            subCurrentSymbol.Subscribe(SubscribeMarketData).DisposeWith(Disposables);
        }

        protected void SubscribeCommandIsExecuting(ReactiveCommand<long, DateTime> cmd)
        {
            //Disposables.Add(cmd.IsExecuting.Skip(1).Subscribe(x => UpdateBusyCount(x)));
        }

        protected void ProcessExchangeInfo(IEnumerable<SymbolInformation> markets)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var qassets = new HashSet<string>(marketAssets);
                var usdassets = new HashSet<string>(UsdAssets);

                if (this.marketsMapping.Count == 0)
                {
                    // a small optimization - since we know it is empty,
                    // do not check every symbol to be in here.
                    foreach (var market in markets)
                    {
                        this.marketsMapping.TryAdd(market.Symbol, market);
                        qassets.Add(market.QuoteAsset);
                        if (market.QuoteAsset == Balance.USD || market.QuoteAsset == Balance.USDT)
                            usdassets.Add(market.BaseAsset);
                        //market.QuoteSymbols = markets.Where(x => x.BaseAsset == market.BaseAsset && x != market).ToArray();
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
                            // NOTE: if symbol got removed/status=BREAK -- remove it from any dependencies like QuoteSymbols.
                        }
                        else
                        {
                            // add new.
                            this.marketsMapping.TryAdd(market.Symbol, market);
                            qassets.Add(market.QuoteAsset);
                            if (market.QuoteAsset == Balance.USD || market.QuoteAsset == Balance.USDT)
                                usdassets.Add(market.BaseAsset);
                            //market.QuoteSymbols = markets.Where(x => x.BaseAsset == market.BaseAsset && x != market).ToArray();
                        }
                    }
                }
                //marketAssets.AddRange(markets.Where(IsValidMarket).Select(x => x.QuoteAsset).Distinct().Except(marketAssets));
                if (qassets.Count != MarketAssets.Count)
                {
                    marketAssets.Clear();
                    marketAssets.AddRange(qassets);
                }
                //UsdAssets = markets.Where(m => m.QuoteAsset == Balance.USD || m.QuoteAsset == Balance.USDT).Select(m => m.BaseAsset).ToList();
                if (usdassets.Count != UsdAssets.Count)
                {
                    UsdAssets.Clear();
                    UsdAssets.AddRange(usdassets);
                }
            }
            finally
            {
                Debug.Print($"ProcessExchangeInfo took {sw.ElapsedMilliseconds}ms.");
            }
        }

        protected void ProcessPriceTicker(IEnumerable<PriceTicker> priceTicker)
        {
            var sw = Stopwatch.StartNew();
            try
            {
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
            }
            finally
            {
                Debug.Print($"ProcessPriceTicker took {sw.ElapsedMilliseconds}ms.");
            }
        }

        protected void ProcessPublicTrades(IEnumerable<PublicTrade> trades)
        {
            var tradesList = trades as List<PublicTrade> ?? trades.ToList();
            //if (RecentTrades.All(x => x.SymbolInformation.Symbol != CurrentSymbol))
            //{
            //    RecentTrades.Clear();
            //    RecentTradesCache.Clear();
            //}
            RecentTradesCache.AddOrUpdate(trades);
            if (RecentTradesCache.Count > TradesMaxItemCount)
            {
                RecentTradesCache.Remove(RecentTradesCache.Items.OrderByDescending(x => x.Id).Skip(TradesMaxItemCount));
            }
            CalcQuantityPercentageTotal(RecentTradesCache.Items);
            return;

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

        protected void ProcessPublicTradesPush(IEnumerable<PublicTrade> trades)
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

        protected void ProcessOrderBook(IEnumerable<OrderBookEntry> depth)
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

        private void ProcessOrderBookPush(IEnumerable<OrderBookEntry> depth)
        {
            IsLoadingOrderBook = false;
            OrderBook.MergeDecimals = OrderBookMergeDecimals;
            OrderBook.UpdateIncremental(depth.ToList());
        }

        private static void CalcCumulativeTotal(IList<OrderBookEntry> entries)
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

        private static decimal CalcFactor(int num)
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

        protected virtual Task<SymbolInformation> GetFullSymbolInformation(string market)
        {
            return Task.FromResult(GetSymbolInformation(market));
        }

        protected async void OnRefreshMarketSummary2(PriceTicker ticker)
        {
            if (tickersMapping.TryGetValue(ticker.Symbol, out int idx))
            {
                PriceTicker oldTicker = MarketSummaries[idx];
                Debug.Assert(oldTicker.Symbol == ticker.Symbol);
                if (oldTicker.Candle1m != null && oldTicker.Candle1m.CloseTime >= DateTime.Now)
                    ticker.LastPrice = oldTicker.Candle1m.Close;
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
                    foreach (var mngr in Accounts.Items.Select(x => x.BalanceManager))
                    {
                        mngr.UpdateWithLastPrice(market.ProperSymbol, ticker.Bid.GetValueOrDefault());
                    }
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

        protected void OnKline(Candle candle)
        {
            var ticker = GetPriceTicker(candle.Symbol);
            if (ticker != null)
            {
                switch (candle.Interval)
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
            }
        }

        private Dictionary<string, long> candleCache = new Dictionary<string, long>();
        private async void AnalyzeCandle(Candle candle, decimal changePercent, string interval)
        {
            if (candle.Close == decimal.Zero || candle.Open == decimal.Zero)
                return;
            var key = candle.Symbol + "@" + interval;
            if (candleCache.ContainsKey(key) && candleCache[key] == candle.OpenTime.Ticks)
                return;
            // ignore 1sat changes.
            if (candle.Close - candle.Open == 0.00000001m)
                return;
            if (candle.Open - candle.Close == 0.00000001m)
                return;
            decimal positiveDelta = candle.Close / candle.Open;
            decimal negativeDelta = candle.Open / candle.Close;
            if (positiveDelta >= changePercent)
            {
                candleCache[key] = candle.OpenTime.Ticks;
                //await Alert.Handle($"{candle.Symbol}: price raised {delta-1m:p2}");
                await TelegramNotifier.Notify($"[{ExchangeName}] {key} price +{positiveDelta - 1m:p2}");
            }
            else if (negativeDelta >= changePercent)
            {
                candleCache[key] = candle.OpenTime.Ticks;
                await TelegramNotifier.Notify($"[{ExchangeName}] {key} price -{negativeDelta - 1m:p2}");
            }
        }

        protected void UpdateWithTicker(PriceTicker ticker)
        {
            //foreach (var order in CurrentAccount?.OrdersHistory?.Items?.Where(x => x.SymbolInformation.Symbol == ticker.Symbol))
            //{
            //    if (order.Side == TradeSide.Buy)
            //        order.LastPrice = ticker.Bid.GetValueOrDefault();
            //    else
            //        order.LastPrice = ticker.Ask.GetValueOrDefault();
            //}
            foreach (var task in TradeTasksList.Where(x => x.SymbolInformation.Symbol == ticker.Symbol))
            {
                task.LastPrice = ticker.LastPrice.GetValueOrDefault();
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
            //const int MAX_CONFIRMS = 5;
            try
            {
                var rule = proxy.Rule;
                await @lock.WaitAsync();
                if (rule.IsApplicable(ticker, proxy.SymbolInformation))
                {
                    proxy.Confirmations += 1;
                    proxy.Status = $"Triggered #{proxy.Confirmations}/{proxy.Rule.RequiredConfirmations} times";
                    if (proxy.Confirmations < proxy.Rule.RequiredConfirmations)
                        return;
                    if (proxy.Rule.OrderVolume == 0m || string.IsNullOrEmpty(proxy.Rule.OrderType))
                    {
                        Notify(proxy);
                        proxy.Status += "; Notification sent";
                        rule.IsActive = false;
                    }
                    else try
                    {
                        Debug.Print("{4} Placing {0} {3} {1} by {2}.", rule.OrderSide, ticker.Symbol, rule.OrderRate, rule.OrderVolume, DateTime.Now);
                        rule.Order = await PlaceOrder(rule).ConfigureAwait(false);
                        if (rule.Order != null)
                        {
                            switch (rule.Order.Status)
                            {
                                case OrderStatus.Filled:
                                    var avgPrice = rule.Order.Fills.Sum(x => x.Total) / rule.Order.Fills.Sum(x => x.Quantity);
                                    var si = GetSymbolInformation(rule.Market);
                                    proxy.Status += $"; Order #{rule.Order.OrderId} filled with price {avgPrice.ToString(si.PriceFmt)}";
                                    break;
                                case OrderStatus.Cancelled:
                                    proxy.Status += $"; Order cancelled";
                                    break;
                                case OrderStatus.Rejected:
                                    proxy.Status += $"; Order rejected";
                                    break;
                                case OrderStatus.Active:
                                case OrderStatus.PartiallyFilled:
                                    proxy.Status += $"; Placed order #{rule.Order.OrderId}";
                                    break;
                            }
                        }
                        rule.IsActive = false;
                    }
                    catch (ApiException ex)
                    {
                        Debug.Print(ex.ToString());
                        rule.IsActive = false;
                        proxy.Status += $"; Order NOT placed: {ex.Message}";
                    }
                }
                else
                {
                    if (rule.IsActive && rule.Order == null)
                        proxy.Status = rule.GetStatus();
                }
            }
            finally
            {
                @lock.Release();
            }
        }

        protected async void Notify(TradingRuleProxy proxy)
        {
            string msg = $"[{ExchangeName}] {proxy.Symbol} {proxy.Rule.Operator} {proxy.Rule.ThresholdRate}";
            await TelegramNotifier.Notify(msg);
        }

        protected virtual Task<Order> PlaceOrder(TradingRule rule)
        {
            return Task.FromResult<Order>(null);
        }

        protected void AddRule(TradingRule rule)
        {
            TradingRuleProxies.Add(new TradingRuleProxy(rule, GetSymbolInformation(rule.Market)));
        }

        protected virtual Task RefreshCommandExecute(int x)
        {
            return Task.CompletedTask;
        }

        protected virtual Task RefreshPrivateDataExecute()
        {
            return Task.CompletedTask;
        }

        private async Task CreateRuleImpl(string param)
        {
            var si = await GetFullSymbolInformation();
            var wnd = new System.Windows.Window
            {
                Content = new Terminal.WPF.CreateRule() { Margin = new System.Windows.Thickness(6) },
                Owner = System.Windows.Application.Current.MainWindow,
                SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
                Title = $"Create ${param}",
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                WindowStyle = System.Windows.WindowStyle.ToolWindow
            };
            var viewModel = this;
            var order = new NewOrder(si)
            {
                QuantityPercentage = 0.015m,
                Price = si.PriceTicker.LastPrice.GetValueOrDefault(),
                OrderType = si.OrderTypes.FirstOrDefault()
            };
            viewModel.NewOrder = order;
            viewModel.RuleType = param;
            wnd.DataContext = viewModel;
            wnd.ShowDialog();
        }

        public async Task CreateOrder(OrderBookEntry entry)
        {
            var si = await GetFullSymbolInformation();
            var wnd = new System.Windows.Window
            {
                Content = new Terminal.WPF.SubmitOrder() { Margin = new System.Windows.Thickness(6) },
                Owner = System.Windows.Application.Current.MainWindow,
                SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
                Title = "Submit Order",
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                WindowStyle = System.Windows.WindowStyle.ToolWindow
            };
            var viewModel = this;
            var order = new NewOrder(si, entry);
            viewModel.NewOrder = order;
            wnd.DataContext = viewModel;
            wnd.ShowDialog();
        }

        private void DeleteRuleImpl(TradingRuleProxy proxy)
        {
            if (proxy != null)
            {
                TradingRuleProxies.Remove(proxy);
                proxy.Rule.Delete();
            }
        }

        private void NavigateToTradingViewImpl()
        {
            var alt = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (CurrentSymbolInformation != null)
            {
                var url = $"https://tradingview.com/chart?symbol={ExchangeName}%3A{CurrentSymbolInformation.Symbol}";
                if (alt)
                    url = $"https://www.binance.com/en/trade/{CurrentSymbolInformation.Caption.Replace("/", "_")}";
                Process.Start(url);
            }
        }

        public Interaction<TradeTaskViewModel, bool> CreateTask { get; } = new Interaction<TradeTaskViewModel, bool>();
        public Interaction<Exception, Unit> ShowException { get; } = new Interaction<Exception, Unit>();
        public Interaction<string, bool> Confirm { get; } = new Interaction<string, bool>();
        public Interaction<string, Unit> Alert { get; } = new Interaction<string, Unit>();

        [Reactive]
        public TradeTaskViewModel SelectedTradeTask { get; set; }

        public ReactiveCommand<Unit, Unit> CreateTradeTask { get; private set; }
        public ReactiveCommand<Unit, Unit> EnableTradeTask { get; private set; }
        public ReactiveCommand<Unit, Unit> PanicSellTradeTask { get; private set; }
        public ReactiveCommand<Unit, Unit> DeleteTradeTask { get; private set; }
        public ReactiveCommand<TradingRuleProxy, Unit> DeleteRule { get; private set; }
        public ICommand CreateRule { get; private set; }
        public ICommand SubmitRuleCommand { get; private set; }
        public ReactiveCommand<Unit, Unit> NavigateToTradingView { get; private set; }

        private void EnableTradeTaskImpl()
        {
            SelectedTradeTask.IsEnabled = !SelectedTradeTask.IsEnabled;
        }

        private Task PanicSellTradeTaskImpl()
        {
            return PanicSell(SelectedTradeTask.Model);
        }

        private async Task DeleteTradeTaskImpl()
        {
            var yes = await Confirm.Handle("Вы уверены что хотите удалить задачу?");
            if (yes)
            {
                try
                {
                    TradeTaskViewModel.Delete(SelectedTradeTask.Model);
                    tradeTasks.Remove(SelectedTradeTask.Model);
                }
                catch (Exception)
                {
                    throw;
                }
            }
        }

        private async Task CreateTradeTaskImpl()
        {
            var si = await GetFullSymbolInformation();
            var viewModel = new TradeTaskViewModel(si, ExchangeName);

            var ok = await CreateTask.Handle(viewModel);
            if (ok)
                tradeTasks.Add(viewModel.Model);
        }

        private void SubmitRuleExecute(object param)
        {
            if (NewOrder != null)
            {
                var si = NewOrder.SymbolInformation;
                switch (param?.ToString())
                {
                    case "Alert":
                        {
                            var rule = new TradingRule();
                            rule.Exchange = ExchangeName;
                            rule.Market = NewOrder.SymbolInformation.Symbol;
                            rule.Property = ThresholdType.LastPrice;
                            rule.OrderVolume = 0;
                            rule.OrderSide = NewOrder.Side;
                            rule.OrderType = null;
                            rule.Operator = si.PriceTicker.LastPrice > NewOrder.Price ? ThresholdOperator.LessOrEqual : ThresholdOperator.GreaterOrEqual;
                            rule.ThresholdRate = NewOrder.Price;
                            rule.RequiredConfirmations = 1;
                            rule.Save();
                            AddRule(rule);
                        }
                        break;
                    case "Regular":
                        {
                            var rule = new TradingRule();
                            rule.Exchange = ExchangeName;
                            rule.Market = NewOrder.SymbolInformation.Symbol;
                            rule.Property = (NewOrder.Side == TradeSide.Buy) ? ThresholdType.AskPrice : ThresholdType.BidPrice;
                            rule.OrderVolume = NewOrder.Quantity;
                            rule.OrderSide = NewOrder.Side;
                            rule.OrderType = NewOrder.OrderType;
                            rule.Operator = si.PriceTicker.LastPrice > NewOrder.Price ? ThresholdOperator.LessOrEqual : ThresholdOperator.GreaterOrEqual;
                            rule.ThresholdRate = NewOrder.Price;
                            rule.RequiredConfirmations = 1;
                            rule.Save();
                            AddRule(rule);
                        }
                        break;
                    case "TTP":
                        {
                            var direction = NewOrder.Side == TradeSide.Buy ? 1m : -1m;
                            var rule = new TrailingTakeProfit(NewOrder.Price, NewOrder.QuantityPercentage / direction);
                            rule.Exchange = ExchangeName;
                            rule.Market = NewOrder.SymbolInformation.Symbol;
                            //rule.Property = ThresholdType.LastPrice;
                            rule.Property = (NewOrder.Side == TradeSide.Buy) ? ThresholdType.AskPrice : ThresholdType.BidPrice;
                            rule.OrderVolume = NewOrder.Quantity;
                            rule.OrderSide = NewOrder.Side;
                            rule.OrderType = NewOrder.OrderType;
                            rule.Save();
                            AddRule(rule);
                        }
                        break;
                }
            }
        }

#endregion

        protected void DoDispose()
        {
            disposablesHandle.Disposable = null;
            //(disposablesHandle.Disposable as CompositeDisposable)?.Dispose();
        }

        private void AddMarketSummary(PriceTicker ticker)
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


        protected void Initialize(CompositeDisposable disposables)
        {
            InitializeAsync(disposables);
        }

        protected virtual async void InitializeAsync(CompositeDisposable disposables)
        {
            IsInitializing = true;
            await GetExchangeInfo.Execute();
            await GetTickers.Execute();
            TelegramNotifier.ParseAndRun.Add(ParseAndRun);
            IsInitializing = false;
            //SetTickersSubscription(true);

            LoadRules();
            LoadTradeTasks();

            Observable
                .Interval(TimeSpan.FromSeconds(1))
                .SelectMany(x => TradeTasksList)
                .Where(x => x.IsEnabled)
                .Select(x => x.Model)
                .InvokeCommand(TradeTaskLifecycle)
                .DisposeWith(disposables);

            Observable
                .Timer(TimeSpan.Zero, TimeSpan.FromSeconds(6))
                .Select(x => Unit.Default)
                .InvokeCommand(GetBalance)
                .DisposeWith(disposables);

            InitializeAsyncImpl(disposables);
        }

        protected virtual void InitializeAsyncImpl(CompositeDisposable disposables)
        {

        }

        private void LoadTradeTasks()
        {
            foreach (var file in Directory.EnumerateFiles(".", "????????-*.json"))
            {
                var json = File.ReadAllText(file);
                var taskModel = TradeTaskViewModel.DeserializeModel(json);
                if (taskModel.Exchange == ExchangeName)
                {
                    //var si = GetSymbolInformation(taskModel.Symbol);
                    //TradeTasks.Add(new TradeTaskViewModel(si, taskModel));
                    tradeTasks.Add(taskModel);
                }
            }
        }

        private void LoadRules()
        {
            foreach (var file in Directory.EnumerateFiles(".", "*.rule*"))
            {
                //var json = File.ReadAllText(file);
                //var isTTP = file.EndsWith(".ttp");
                ////var rule = JsonConvert.DeserializeObject<TrailingTakeProfit>(json);
                //var rule = isTTP ?
                //    JsonConvert.DeserializeObject<TrailingTakeProfit>(json) :
                //    JsonConvert.DeserializeObject<TradingRule>(json);
                //rule.Id = Path.ChangeExtension(file, null);
                var rule = TradingRule.Load(file);
                if (rule.Exchange.Equals(ExchangeName, StringComparison.OrdinalIgnoreCase))
                {
                    AddRule(rule);
                }
            }
        }

        protected virtual Task GetExchangeInfoImpl()
        {
            return Task.CompletedTask;
        }

        protected virtual Task GetTickersImpl()
        {
            return Task.CompletedTask;
        }

        protected virtual Task GetTradesImpl()
        {
            return Task.CompletedTask;
        }

        protected virtual IObservable<PriceTicker> ObserveTickers123()
        {
            return Observable.Empty<PriceTicker>();
        }

        protected virtual IObservable<PublicTrade> ObserveTrades(string market)
        {
            return Observable.Empty<PublicTrade>();
        }

        protected virtual Task GetDepthImpl()
        {
            return Task.CompletedTask;
        }

        protected virtual Task GetOpenOrdersImpl()
        {
            return Task.CompletedTask;
        }
        protected virtual Task GetOrdersHistoryImpl()
        {
            return Task.CompletedTask;
        }
        protected virtual Task GetTradesHistoryImpl()
        {
            return Task.CompletedTask;
        }
        protected virtual Task GetDepositsImpl()
        {
            return Task.CompletedTask;
        }
        protected virtual Task GetWithdrawalsImpl()
        {
            return Task.CompletedTask;
        }
        protected virtual Task GetBalanceImpl()
        {
            return Task.CompletedTask;
        }
        protected virtual async Task GetTradesStatisticsImpl()
        {
            var totalBuy = CurrentAccountViewModel.TradesHistory.Where(x => x.Side == TradeSide.Buy).Sum(x => x.Total);
            var totalSell = CurrentAccountViewModel.TradesHistory.Where(x => x.Side == TradeSide.Sell).Sum(x => x.Total);
            if (totalBuy > decimal.Zero)
            {
                var si = await GetFullSymbolInformation();
                totalSell += si.BaseAssetBalance.Total * si.PriceTicker.Bid.GetValueOrDefault();
                var diff = totalSell - totalBuy;
                var diffPercent = (totalSell / totalBuy);
                await Alert.Handle($"Profit: {diff:N4} BTC, {diffPercent:p2}");
            }
            //return Task.CompletedTask;
        }
        protected virtual Task<bool> CancelOrderImpl(Order order)
        {
            return Task.FromResult(false);
        }

        protected virtual Task<bool> SubmitOrderImpl(NewOrder order)
        {
            return Task.FromResult(false);
        }

        protected void SetTickersSubscription(bool isEnabled)
        {
            if (HasMarketSummariesPush)
            {
                SetTickersSubscriptionWebSocket(isEnabled);
                return;
            }

            if (isEnabled)
                getTickersSubscription.Disposable =
                    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(2))
                              .Select(x => Unit.Default)
                              .ObserveOnDispatcher()
                              .InvokeCommand(GetTickers);
            else if (getTickersSubscription.Disposable != null)
                getTickersSubscription.Disposable = null;
        }

        protected virtual void SetTickersSubscriptionWebSocket(bool isEnabled)
        {
            if (isEnabled)
                getTickersSubscription.Disposable = ObserveTickers123()
                                        .Subscribe(OnRefreshMarketSummary2);
            else if (getTickersSubscription.Disposable != null)
                getTickersSubscription.Disposable = null;
        }

        protected void SetTradesSubscriptionWebSocket(bool isEnabled)
        {
            if (isEnabled)
                getTradesSubscription = ObserveTrades(CurrentSymbol)
                                        .Subscribe(OnPublicTrade);
            else if (getTradesSubscription != null)
                getTradesSubscription.Dispose();
        }

        protected void OnPublicTrade(PublicTrade x)
        {
            RecentTradesCache.AddOrUpdate(x);
        }

        protected void SetTradesSubscription(bool isEnabled)
        {
            if (HasTradesPush)
            {
                SetTradesSubscriptionWebSocket(isEnabled);
                return;
            }

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

        protected virtual Task<Balance> GetAssetBalance(string asset)
        {
            return Task.FromResult(new Balance(asset));
        }

        protected async void ParseAndRun(string cmd)
        {
            try
            {
                var parts = cmd.ToUpper().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
                var market = parts.Length > 1 ? parts[1] : null;
                var si = market != null ? await GetFullSymbolInformation(market) : null;
                var response = string.Empty;
                switch (parts[0])
                {
                    case "/BALANCE":
                        response = $"[{ExchangeName}] balance is: {CurrentAccount.BalanceManager.TotalBtc} BTC, {CurrentAccount.BalanceManager.TotalUsd} USD";
                        break;
                    case "/PRICE":
                        if (si != null)
                        {
                            response = $"[{ExchangeName}] {si.BaseAsset} price is: {si.PriceTicker?.LastPrice} {si.QuoteAsset}";
                        }
                        break;
                    case "/VOLUME":
                        if (si != null)
                        {
                            response = $"[{ExchangeName}] {si.Caption} volume is: {si.PriceTicker?.Volume:N0} {si.BaseAsset} / {si.PriceTicker?.QuoteVolume:N0} {si.QuoteAsset}";
                        }
                        break;
                }
                if (!string.IsNullOrWhiteSpace(response))
                {
                    TelegramNotifier.Notify(response);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }


        public virtual string[] OrderTypes => new string[] { };

        public NewOrder NewOrder { get; set; }
        [Reactive] public string RuleType { get; set; }

        protected IEnumerable<SymbolInformation> ValidPairs => marketsMapping.Values.Where(IsValidMarket);

        SerialDisposable getTickersSubscription = new SerialDisposable();
        IDisposable getTradesSubscription;
        IDisposable getDepthSubscription;
        IDisposable getPrivateDataSubscription;

#if !GTK
        public ViewModelActivator Activator => viewModelActivator;
#endif
    }

    public class TradingRule
    {
        [JsonIgnore]
        public string Id { get; set; } = DateTime.Now.Ticks.ToString();
        public string Exchange { get; set; }
        public string Market { get; set; }
        public decimal ThresholdRate { get; set; }
        public ThresholdType Property { get; set; }
        public ThresholdOperator Operator { get; set; }
        public TradeSide OrderSide { get; set; }
        public string OrderType { get; set; }
        public decimal OrderRate { get; set; } // Sell or Buy using this rate. 0 for Market Order.
        public decimal OrderVolume { get; set; }
        public decimal RemainingVolume { get; set; }
        public int RequiredConfirmations { get; set; } = 5;

        public Order Order { get; set; } // <null> if not yet placed.
        public bool IsActive { get; set; } = true;

        public virtual bool IsApplicable(PriceTicker ticker, SymbolInformation si)
        {
            if (!Market.Equals(ticker.Symbol, StringComparison.CurrentCultureIgnoreCase))
                return false;
            if (!IsActive)
                return false;
            if (Order != null)
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

        public void Delete()
        {
            File.Delete(Path.ChangeExtension(Id.ToString(), GetFileExtension));
        }
        public void Save()
        {
            var json = JsonConvert.SerializeObject(this);
            File.WriteAllText(Path.ChangeExtension(Id, GetFileExtension), json);
        }

        public static TradingRule Load(string path)
        {
            var json = File.ReadAllText(path);
            var isTTP = path.EndsWith(".ttp");
            var rule = isTTP ?
                JsonConvert.DeserializeObject<TrailingTakeProfit>(json) :
                JsonConvert.DeserializeObject<TradingRule>(json);
            rule.Id = Path.ChangeExtension(path, null);
            rule.Initialize();
            return rule;
        }
        protected virtual void Initialize()
        {
        }

        protected virtual string GetFileExtension => ".rule";
    }

    public class TrailingTakeProfit : TradingRule
    {
        public decimal TakeProfitPrice { get; set; }
        public decimal TrailingPercent { get; set; }

        public override bool IsApplicable(PriceTicker ticker, SymbolInformation si)
        {
            // if current Ask/Bid/LastPrice >= TakeProfitPrice then
            // activate Trailing.
            // TrailingPrice starts with TakeProfitPrice - (TakeProfitPrice * TrailingPercent)
            decimal? value = GetPropertyValue(ticker);
            value = value + value * TrailingPercent;
            value = si.ClampPrice(value.Value);
            if (!trailingActivated)
            {
                if (base.IsApplicable(ticker, si))
                {
                    // we reached TakeProfitPrice. Activate Trailing.
                    trailingActivated = true;
                    //Operator = TrailingPercent > 0 ? ThresholdOperator.Greater : ThresholdOperator.Less;
                    Operator = TrailingPercent > 0 ? ThresholdOperator.GreaterOrEqual : ThresholdOperator.LessOrEqual;
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
                return base.IsApplicable(ticker, si);
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
            OrderSide = TradeSide.Sell;
            Initialize();
        }

        protected override void Initialize()
        {
            Operator = TrailingPercent > 0 ? ThresholdOperator.LessOrEqual : ThresholdOperator.GreaterOrEqual;
            ThresholdRate = TakeProfitPrice;
            OrderRate = TakeProfitPrice;
        }

        protected override string GetFileExtension => ".rule.ttp";
        private bool trailingActivated;
    }

    public class TradingRuleProxy : ReactiveObject
    {
        TradingRule rule;
        public SymbolInformation SymbolInformation { get; }

        public TradingRuleProxy(TradingRule rule, SymbolInformation si)
        {
            this.rule = rule;
            this.SymbolInformation = si;
        }

        public int Confirmations { get; set; }
        [Reactive] public string Status { get; set; }

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
    }

    public enum RuleType
    {
        Alert,      // no orders, no confirmations, just notify
        StopLoss,   // pass 5 confirmations before execute order
        Trailing,
        Regular
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

    public static class ReactiveListExtensions
    {
        public static void EnableThreadSafety<T>(this ReactiveList<T> list)
        {
            BindingOperations.EnableCollectionSynchronization(list, (list as ICollection).SyncRoot);
        }
    }
}
