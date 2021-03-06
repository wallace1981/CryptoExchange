﻿using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;


namespace Exchange.Net
{
    public abstract class ExchangeViewModel : ReactiveObject
    {

        // NOTE: below props should be NORMAL props since they has to be changed from UI.
        public int OrderBookMaxItemCount { get; set; } = 20;
        public TimeSpan OrderBookRetrieveInterval => TimeSpan.FromSeconds(3);
        public int TradesMaxItemCount { get; set; } = 500;
        public TimeSpan TradesRetrieveInterval => TimeSpan.FromSeconds(1);
        public string PriceTickerFilter { get; set; }
        public TimeSpan PriceTickerRetrieveInterval => TimeSpan.FromSeconds(1);

        public string Status { get; protected set; }

        public string CurrentSymbol
        {
            get { return this.currentSymbol; }
            set { this.RaiseAndSetIfChanged(ref this.currentSymbol, value); }
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

        public ReactiveList<PublicTrade> RecentTrades => this.recentTrades;
        public ReactiveList<SymbolInformation> Markets => this.markets;
        public ReactiveList<Transfer> Deposits => this.deposits;
        public ReactiveList<Transfer> Withdrawals => this.withdrawals;
        public ReactiveList<PriceTicker> MarketSummaries => this.marketSummaries;
        public ReactiveList<MarketGroup> MarketsByAsset => this.marketsByAsset;
        public BalanceManager BalanceManager => this.balanceManager;
        public ReactiveList<OrderBookEntry> OrderBook { get; }
        public ReactiveList<Order> OpenOrders => this.openOrders;

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
        ReactiveList<SymbolInformation> markets;
        ReactiveList<Transfer> deposits;
        ReactiveList<Transfer> withdrawals;
        ReactiveList<PriceTicker> marketSummaries;
        ReactiveList<MarketGroup> marketsByAsset;
        ReactiveList<Order> openOrders;
        Dictionary<string, PriceTicker> marketSummariesHash;
        BalanceManager balanceManager;
        string currentSymbol;
        SymbolInformation currentSymbolInformation;
        string currentMarketSummariesPeriod = "30m";
        string currentMarket;
        string marketFilter;
        readonly ViewModelActivator viewModelActivator = new ViewModelActivator();

        TimeSpan refreshMarketSummariesElapsed;
        TimeSpan refreshTradesElapsed;
        TimeSpan refreshDepositsElapsed;

        private readonly ReactiveCommand<Int64, Unit> getTradesCommand;
        private readonly ReactiveCommand<Int64, Unit> getOrderBookCommand;
        private readonly ReactiveCommand<Int64, Unit> getMarketSummariesCommand;
        private readonly ReactiveCommand<string, Unit> setCurrentMarketCommand;

        public ExchangeViewModel()
        {
            currentMarket = DefaultMarket;

            markets = new ReactiveList<SymbolInformation>();
            marketsByAsset = new ReactiveList<MarketGroup>();
            marketSummaries = new ReactiveList<PriceTicker>();
            marketSummariesHash = new Dictionary<string, PriceTicker>();
            recentTrades = new ReactiveList<PublicTrade>();
            deposits = new ReactiveList<Transfer>();
            withdrawals = new ReactiveList<Transfer>();
            balanceManager = new BalanceManager();
            OrderBook = new ReactiveList<OrderBookEntry>();
            openOrders = new ReactiveList<Order>();

            var canGetTrades = this.WhenAny(x => x.CurrentSymbol, (x) => x.Value != null).DistinctUntilChanged();
            var canGetMarketSummaries = this.WhenAny(x => x.Markets.Count, (x) => x.Value > 0).DistinctUntilChanged();
            getTradesCommand = ReactiveCommand.CreateFromTask<Int64>((x) => RefreshTrades(CurrentSymbol), canGetTrades);
            getOrderBookCommand = ReactiveCommand.Create<Int64>((x) => RefreshOrderBook(CurrentSymbol), canGetTrades);
            getMarketSummariesCommand = ReactiveCommand.CreateFromTask<Int64>((x) => RefreshMarketSummaries(), canGetMarketSummaries);
            setCurrentMarketCommand = ReactiveCommand.Create<string>((x) => CurrentMarket = x);

            //this.WhenActivated(registerDisposable =>
            //{
            //    if (HasMarketSummariesPush)
            //    {
            //        // TODO: make as a command with condition.
            //        var sub = canGetMarketSummaries.Where(x => x == true).Take(1).Subscribe(
            //            x =>
            //            {
            //                var subs = SubscribeMarketSummaries(Markets.Select(m => m.ProperSymbol));
            //                var onMarketSummaries = subs.ObserveOnDispatcher(System.Windows.Threading.DispatcherPriority.Background).Subscribe(OnRefreshMarketSummary); // NOTE: Throttle works ONLY on CurrentThread.
            //                var onMarketSummariesUpdateBalance = subs.TimeInterval().Subscribe(ti => { if (ti.Interval > TimeSpan.FromSeconds(0.5)) UpdateBalances(); });
            //                registerDisposable(onMarketSummaries);
            //                registerDisposable(onMarketSummariesUpdateBalance);
            //            });
            //        registerDisposable(sub);
            //    }
            //    else if (HasMarketSummariesPull)
            //    {
            //        var getMarketSummariesSubscr = Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(3)).ObserveOnDispatcher().InvokeCommand(getMarketSummariesCommand);
            //        registerDisposable(getMarketSummariesSubscr);
            //    }
                //if (HasTradesPush)
                //{
                //    var sub = canGetTrades.Where(x => x == true).Take(1).Subscribe(
                //        x =>
                //        {
                //            var onTrade = SubscribeTrades(currentSymbol).ObserveOnDispatcher(System.Windows.Threading.DispatcherPriority.Background).Subscribe(OnTrade); // NOTE: Throttle works ONLY on CurrentThread.
                //            registerDisposable(onTrade);
                //        });
                //    registerDisposable(sub);
                //}
                //else if (HasTradesPull)
                //{
                //    var getTradesSubscr = Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).ObserveOnDispatcher().InvokeCommand(getTradesCommand);
                //    registerDisposable(getTradesSubscr);
                //}

            //    if (HasOrderBookPush)
            //    {

            //    }
            //    else if (HasOrderBookPull)
            //    {
            //        var getOrderBookCommand = ReactiveCommand.Create<long>(x => RefreshOrderBook(CurrentSymbol), canGetTrades);
            //        var getOrderBookSubscr = Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).ObserveOnDispatcher().InvokeCommand(getOrderBookCommand);
            //        registerDisposable(getOrderBookSubscr);
            //        registerDisposable(getOrderBookCommand);
            //    }

            //    registerDisposable(getTradesCommand);
            //    registerDisposable(getMarketSummariesCommand);
            //    registerDisposable(setCurrentMarketCommand);
            //    registerDisposable(this.WhenAnyValue(x => x.CurrentSymbol).DistinctUntilChanged().Subscribe(y => recentTrades.Clear()));
            //    this.RefreshMarkets();
            //    this.RefreshDeposits();
            //    this.RefreshWithdrawals();
            //    this.RefreshBalances();
            //});
        }

        public void Activate(bool active = true)
		{
		    if (HasMarketSummariesPush)
            {
                // TODO: make as a command with condition.
				var sub = getMarketSummariesCommand.CanExecute.Where(x => x == true).Take(1).Subscribe(
    				(x) =>
                    {
                        //var subs = SubscribeMarketSummaries(Markets.Select(m => m.ProperSymbol), CurrentMarketSummariesPeriod);
                        //getMarketSummariesSubscr = subs.Subscribe(OnRefreshMarketSummary);
                        //var onMarketSummariesUpdateBalance = subs.TimeInterval().Subscribe(ti => { if (ti.Interval > TimeSpan.FromSeconds(0.5)) UpdateBalances(); });
                        //registerDisposable(onMarketSummaries);
                        //registerDisposable(onMarketSummariesUpdateBalance);
                        var periodChangeSubscr = this.WhenAnyValue(vm => vm.CurrentMarketSummariesPeriod).Where(sym => sym != null).Subscribe(
                            (period) =>
                            {
                                if (getMarketSummariesSubscr != null)
                                {
                                    getMarketSummariesSubscr.Dispose();
                                    ResetMarketSummaries();
                                }
                                var subs = SubscribeMarketSummaries(Markets.Where(FilterSymbol).Select(m => m.ProperSymbol), period);
                                getMarketSummariesSubscr = subs.Subscribe(OnRefreshMarketSummary);
                            });
                    });
                //registerDisposable(sub);
            }
            else if (HasMarketSummariesPull)
            {
                getMarketSummariesSubscr = Observable.Timer(TimeSpan.Zero, PriceTickerRetrieveInterval).InvokeCommand(getMarketSummariesCommand);
                //registerDisposable(getMarketSummariesSubscr);
            }
			if (HasTradesPush)
            {
                var symbolChangeSubscr = this.WhenAnyValue(vm => vm.CurrentSymbol).Where(sym => sym != null).Subscribe(
                //var subscr = getTradesCommand.CanExecute.Where(x => x == true).Take(1).Subscribe(
					async (x) =>
                    {
                        if (getTradesSubscr != null)
                            getTradesSubscr.Dispose();
                        recentTrades.Clear();
						await RefreshTrades(CurrentSymbol);
                        getTradesSubscr = SubscribeTrades().Subscribe(OnTrade);
                        //registerDisposable(onTrade);
                    });
                //registerDisposable(sub);
            }
            else if (HasTradesPull)
            {
                if (getTradesSubscr != null)
                    getTradesSubscr.Dispose();
                getTradesSubscr = Observable.Timer(TimeSpan.Zero, TradesRetrieveInterval).InvokeCommand(getTradesCommand);
                //registerDisposable(getTradesSubscr);
            }
            if (HasOrderBookPull)
            {
                if (getOrderBookSubscr != null)
                    getOrderBookSubscr.Dispose();
                getOrderBookSubscr = Observable.Timer(TimeSpan.Zero, OrderBookRetrieveInterval).InvokeCommand(getOrderBookCommand);
            }
		    
			RefreshMarkets();

            this.RefreshDeposits();
            this.RefreshWithdrawals();
            this.RefreshBalances();
            this.RefreshOpenOrders();
        }

        IDisposable getMarketSummariesSubscr, getTradesSubscr, getOrderBookSubscr;

        public SymbolInformation GetSymbolInformation(string symbol)
		{
			return markets.SingleOrDefault(x => x.Symbol == symbol);
		}

        #region Commands
        public ICommand GetRecentTradesCommand => this.getTradesCommand;
        public ICommand SetCurrentMarketCommand => this.setCurrentMarketCommand;
        #endregion

        private async Task RefreshTrades(string symbol)
        {
            var stamp = DateTime.Now;
            var trades = await GetTrades(symbol, TradesMaxItemCount);
            RefreshTradesElapsed = DateTime.Now - stamp;
            if (recentTrades.All(x => x.Symbol != symbol))
                recentTrades.Clear();
            long? lastId = recentTrades.FirstOrDefault()?.Id;
            if (lastId != null)
                trades.RemoveAll(x => x.Id <= lastId);
            //using (recentTrades.SuppressChangeNotifications())
            {
                if (trades.Count > 0)
                {
                    recentTrades.InsertRange(0, trades);
                    if (recentTrades.Count > TradesMaxItemCount)
                    {
                        recentTrades.RemoveRange(TradesMaxItemCount, Math.Abs(TradesMaxItemCount - recentTrades.Count));
                    }
                }
            }
        }

        private void RefreshOrderBook(string symbol)
        {
            var book = GetOrderBook(symbol, OrderBookMaxItemCount);
            using (OrderBook.SuppressChangeNotifications())
            {
                OrderBook.Clear();
                OrderBook.AddRange(book.Asks.AsEnumerable().Reverse().Concat(book.Bids));
            }
        }

        private void OnTrade(PublicTrade trade)
        {
            recentTrades.Insert(0, trade);
            if (recentTrades.Count > TradesMaxItemCount)
            {
                recentTrades.RemoveRange(TradesMaxItemCount, Math.Abs(TradesMaxItemCount - recentTrades.Count));
            }
        }

        private async void RefreshMarkets()
        {
            var tmp = await GetMarkets();
            using (markets.SuppressChangeNotifications())
            {
                markets.Clear();
                markets.AddRange(tmp.OrderBy(x => x.Symbol));
                using (marketSummaries.SuppressChangeNotifications())
                {
                    this.marketSummaries.AddRange(tmp.Where(FilterSymbol).Select(x => new PriceTicker { Symbol = x.Symbol }));
                    this.marketSummariesHash = this.marketSummaries.ToDictionary(key => key.Symbol);
                    UsdAssets = Markets.Where(m => m.QuoteAsset == Balance.USD || m.QuoteAsset == Balance.USDT).Select(m => m.BaseAsset).ToList();
                    if (HasMarketSummariesPush)
                    {
                        if (marketsByAsset.Count == 0)
                        {
                            using (marketsByAsset.SuppressChangeNotifications())
                            {
                                var groups = Markets.Select(y => y.QuoteAsset).Distinct();
                                foreach (var g in groups)
                                {
                                    var mg = new MarketGroup() { MarketName = g.ToUpper() };
                                    mg.Tickers = MarketSummaries.CreateDerivedCollection(x => x, x => FilterByMarket(x.Symbol, g), DefaultTickerOrderer);
                                    marketsByAsset.Add(mg);
                                }
                            }
                        }
                    }
                }
            }
        }

        private bool FilterSymbol(SymbolInformation si)
        {
            return (si.QuoteAsset.Equals("BTC", StringComparison.CurrentCultureIgnoreCase) ||
                    si.QuoteAsset.Equals("USD", StringComparison.CurrentCultureIgnoreCase) ||
                    si.QuoteAsset.Equals("USDT", StringComparison.CurrentCultureIgnoreCase));
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

        private async void RefreshDeposits()
        {
            try
            {
                var stamp = DateTime.Now;
                var tmp = await GetDeposits();
                RefreshDepositsElapsed = DateTime.Now - stamp;
                using (Deposits.SuppressChangeNotifications())
                {
                    Deposits.Clear();
                    Deposits.AddRange(tmp);
                }
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
            }
        }

        private async void RefreshWithdrawals()
        {
            try
            {
                var stamp = DateTime.Now;
                var tmp = await GetWithdrawals();
                RefreshDepositsElapsed = DateTime.Now - stamp;
                using (Withdrawals.SuppressChangeNotifications())
                {
                    Withdrawals.Clear();
                    Withdrawals.AddRange(tmp);
                }
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
            }
        }

        private async void RefreshOpenOrders()
        {
            try
            {
                var stamp = DateTime.Now;
                var orders = await GetOpenOrders();
                //RefreshDepositsElapsed = DateTime.Now - stamp;
                using (OpenOrders.SuppressChangeNotifications())
                {
                    OpenOrders.Clear();
                    OpenOrders.AddRange(orders);
                }
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
            }
        }

        private async Task RefreshMarketSummaries()
        {
            var stamp = DateTime.Now;
            var tmp = await GetMarketSummaries();
            RefreshMarketSummariesElapsed = DateTime.Now - stamp;
            if (MarketSummaries.Count == 0)
            {
                MarketSummaries.AddRange(tmp);
                if (marketsByAsset.Count == 0)
                {
                    using (marketsByAsset.SuppressChangeNotifications())
                    {
                        var groups = Markets.Select(y => y.QuoteAsset).Distinct();
                        foreach (var g in groups)
                        {
                            var mg = new MarketGroup() { MarketName = g.ToUpper() };
                            mg.Tickers = MarketSummaries.CreateDerivedCollection(x => x, x => FilterByMarket(x.Symbol, g), DefaultTickerOrderer);
                            marketsByAsset.Add(mg);
                        }
                        //marketsByAsset.AddRange(markets.Select(y => y.QuoteAsset).Distinct());
                    }
                }
                if (BalanceManager.Balances.Count > 0)
                {
                    UpdateBalances();
                }
            }
            else
            {
                foreach (var ticker in tmp)
                {
                    var pt = MarketSummaries.SingleOrDefault(x => x.Symbol == ticker.Symbol);
                    if (pt != null)
                    {
                        //pt.LastPrice = sum.LastPrice;
                        //if (sum.Volume != null) pt.Volume = sum.Volume;
                        //if (sum.PriceChangePercent != null) pt.PriceChangePercent = sum.PriceChangePercent;
                        var idx = marketSummaries.IndexOf(pt);
                        var old = marketSummaries[idx];
                        ticker.PrevLastPrice = old.LastPrice;
                        marketSummaries[idx] = ticker;
                    }
                }
                UpdateBalances();
            }
        }

		private void OnRefreshMarketSummary(PriceTicker ticker)
		{
			var pt = MarketSummaries.SingleOrDefault(x => x.Symbol == ticker.Symbol);
			if (pt != null)
			{
				var idx = marketSummaries.IndexOf(pt);
				var old = marketSummaries[idx];
				ticker.PrevLastPrice = old.LastPrice;
				marketSummaries[idx] = ticker;
                var market = Markets.SingleOrDefault(x => x.Symbol == ticker.Symbol);
                BalanceManager.UpdateWithLastPrice(market.ProperSymbol, ticker.LastPrice);
			}
            //UpdateBalances();
			return;
            if (marketSummariesHash.ContainsKey(ticker.Symbol))
            {
                pt = marketSummariesHash[ticker.Symbol];
                pt.LastPrice = ticker.LastPrice;
                if (ticker.Volume != null) pt.Volume = ticker.Volume;
                if (ticker.PriceChangePercent != null) pt.PriceChangePercent = ticker.PriceChangePercent;
            }
            else
            {
                //Trace.WriteLine($"Added ticker for {ticker.Symbol}");
                //MarketSummaries.Add(ticker);
                //marketSummariesHash.Add(ticker.Symbol, ticker);
            }
            //UpdateBalances();
        }

        private void UpdateBalances()
        {
            foreach (var sum in marketSummariesHash.Values)
            {
                if (sum.LastPrice > decimal.Zero && sum.IsPriceChanged)
                {
                    var si = Markets.SingleOrDefault(x => x.Symbol == sum.Symbol);
                    BalanceManager.UpdateWithLastPrice(si.ProperSymbol, sum.LastPrice);
                }
            }
        }

        private async void RefreshBalances()
        {
            try
            {
                var stamp = DateTime.Now;
                var tmp = await GetBalances();
                RefreshMarketSummariesElapsed = DateTime.Now - stamp;
                if (BalanceManager.Balances.Count == 0)
                {
                    using (BalanceManager.Balances.SuppressChangeNotifications())
                    {
                        BalanceManager.Balances.AddRange(tmp.OrderBy(x => x.Asset));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
            }
        }

        private void CreateFilteredOrderedTickerCollection(string filter)
        {
            var mg = MarketsByAsset.SingleOrDefault(m => m.MarketName == this.CurrentMarket);
            if (mg == null) return;
            mg.Tickers = MarketSummaries.CreateDerivedCollection(x => x, x => FilterByMarket(x.Symbol, CurrentMarket) && (string.IsNullOrWhiteSpace(filter) || FilterByAsset(x.Symbol, filter)), DefaultTickerOrderer);
        }

        private static int DefaultTickerOrderer(PriceTicker x, PriceTicker y)
        {
            return decimal.Compare(y.Volume.GetValueOrDefault(), x.Volume.GetValueOrDefault());
        }

        public abstract Task<List<PublicTrade>> GetTrades(string symbol, int limit = 50);
		public abstract Task<List<SymbolInformation>> GetMarkets();
		public abstract Task<List<PriceTicker>> GetMarketSummaries();
		public abstract Task<List<Transfer>> GetDeposits(string asset = null);
		public abstract Task<List<Transfer>> GetWithdrawals(string asset = null);
		public abstract Task<List<Balance>> GetBalances();
        public virtual Task<List<Order>> GetOpenOrders()
        {
            throw new NotImplementedException();
        }

        public virtual OrderBook GetOrderBook(string symbol, int limit = 25)
		{
			throw new NotImplementedException();
		}

		public virtual IObservable<PriceTicker> SubscribeMarketSummaries(IEnumerable<string> symbols, string period)
        {
            throw new NotSupportedException();
        }

        public virtual IObservable<PublicTrade> SubscribeTrades(string symbol, int limit = 25)
        {
            throw new NotSupportedException();
        }

		public virtual IObservable<PublicTrade> SubscribeTrades() // for CurrentSymbol.
        {
            throw new NotSupportedException();
        }

		public virtual IObservable<OrderBookEntry> SubscribeOrderBook(string symbol)
        {
            throw new NotSupportedException();
        }

		public abstract bool FilterByMarket(string symbol, string market);
		public abstract bool FilterByAsset(string symbol, string asset);
    }

    public class MarketGroup
    {
        public string MarketName { get; set; }
        public IReactiveDerivedList<PriceTicker> Tickers { get; set; }
    }
}
