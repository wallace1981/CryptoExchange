using Exchange.Net;
using ReactiveUI;
using ReactiveUI.Legacy;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Terminal.WPF
{
    class DsxTerminalViewModel : ReactiveObject
    {
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

        public SymbolInformation CurrentSymbol
        {
            get { return this.currentSymbol; }
            set { if (value != null) this.RaiseAndSetIfChanged(ref this.currentSymbol, value); }
        }

        public int TradesMaxItemCount = 50;

        public ICommand GetExchangeInfoCommand => getExchangeInfoCommand;
        public ICommand GetTickersCommand => getTickersCommand;
        public ICommand GetTickersSubscribeCommand => getTickersSubscribeCommand;
        public ICommand GetTradesCommand => getTradesCommand;
        public ICommand GetTradesSubscribeCommand => getTradesSubscribeCommand;
        public ICommand GetDepthCommand => getDepthCommand;
        public ICommand GetDepthSubscribeCommand => getDepthSubscribeCommand;

        private ConcurrentDictionary<string, SymbolInformation> marketsMapping = new ConcurrentDictionary<string, SymbolInformation>();
        private ConcurrentDictionary<string, int> tickersMapping = new ConcurrentDictionary<string, int>(); // maps SYMBOL to index of ticker in Tickers.

        public ReactiveList<PriceTicker> Tickers { get; set; } = new ReactiveList<PriceTicker>();
        public ReactiveList<PublicTrade> RecentTrades { get; set; } = new ReactiveList<PublicTrade>();
        public ReactiveList<OrderBookEntry> Depth { get; set; } = new ReactiveList<OrderBookEntry>();

        public string Status
        {
            get { return _status; }
            set { this.RaiseAndSetIfChanged(ref _status, value); }
        }

        public DsxTerminalViewModel()
        {
            Initialize();
        }

        protected void Initialize()
        {
            getExchangeInfoCommand = ReactiveCommand.CreateFromTask(GetExchangeInfo);
            getTickersCommand = ReactiveCommand.CreateFromTask(GetTickers);
            getTickersSubscribeCommand = ReactiveCommand.Create<bool>(GetTickersSubscription);
            getTradesCommand = ReactiveCommand.Create(GetTrades);
            getTradesSubscribeCommand = ReactiveCommand.Create<bool>(GetTradesSubscription);
            getDepthCommand = ReactiveCommand.CreateFromTask(GetDepth);
        }

        protected async Task GetExchangeInfo()
        {
            var resultExchangeInfo = await client.GetExchangeInfoAsync().ConfigureAwait(false);
            if (resultExchangeInfo.Success)
            {
                GetExchangeInfoElapsed = resultExchangeInfo.ElapsedMilliseconds;
                var symbols = resultExchangeInfo.Data.pairs.Select(CreateSymbolInformation);
                ProcessExchangeInfo(symbols);
                var exchangeInfoLastUpdated = resultExchangeInfo.Data.server_time.FromUnixSeconds();
                Status = $"Exchange info updated: {exchangeInfoLastUpdated}; Numer of trading pairs: {symbols.Count()}";
            }
            else
            {
                Status = $"GetExchangeInfo: {resultExchangeInfo.Error.ToString()}";
            }
        }

        protected async Task GetTickers()
        {
            var pairs = string.Join("-", ValidPairs.Select(x => x.Symbol));
            if (pairs.Count() < 1)
                return;
            var resultTickers = await client.GetTickerAsync(pairs).ConfigureAwait(true);
            if (resultTickers.Success)
            {
                GetTickersElapsed = resultTickers.ElapsedMilliseconds;
                var tickers = resultTickers.Data.Select(ToPriceTicker);
                ProcessPriceTicker(tickers);
            }
            else
            {
                Status = $"GetTickers: {resultTickers.Error.ToString()}";
            }
        }

        protected async Task GetTrades()
        {
            if (CurrentSymbol == null)
                return;
            var si = CurrentSymbol;
            var resultTrades = await client.GetTradesAsync(si.Symbol, TradesMaxItemCount).ConfigureAwait(true);
            if (resultTrades.Success)
            {
                GetTradesElapsed = resultTrades.ElapsedMilliseconds;
                if (resultTrades.Data.ContainsKey(si.Symbol))
                {
                    var trades = resultTrades.Data[si.Symbol].Select(x => ToPublicTrade(x, si)).ToList();// pair is <symbol,trades>
                    ProcessPublicTrades(trades);
                }
            }
            else
            {
                Status = $"GetTrades: {resultTrades.Error.ToString()}";
            }
        }

        protected async Task GetDepth()
        {
            const int limit = 15;
            if (CurrentSymbol == null)
                return;
            var si = CurrentSymbol;
            var resultDepth = await client.GetDepthAsync(si.Symbol).ConfigureAwait(true);
            if (resultDepth.Success)
            {
                GetDepthElapsed = resultDepth.ElapsedMilliseconds;
                if (resultDepth.Data.ContainsKey(si.Symbol))
                {
                }
            }
            else
            {
                Status = $"GetDepth: {resultDepth.Error.ToString()}";
            }
        }

        protected void GetTickersSubscription(bool isEnabled)
        {
            if (isEnabled)
                getTickersSubscription =
                    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(2))
                              .Select(x => Unit.Default)
                              .ObserveOnDispatcher()
                              .InvokeCommand(getTickersCommand);
            else if (getTickersSubscription != null)
                getTickersSubscription.Dispose();
        }

        protected void GetTradesSubscription(bool isEnabled)
        {
            if (isEnabled)
                getTradesSubscription =
                    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(2))
                              .Select(x => Unit.Default)
                              .ObserveOnDispatcher()
                              .InvokeCommand(getTradesCommand);
            else if (getTradesSubscription != null)
                getTradesSubscription.Dispose();
        }

        protected SymbolInformation CreateSymbolInformation(KeyValuePair<string, DSX.Pair> p)
        {
            return new SymbolInformation()
            {
                BaseAsset = p.Value.base_currency,
                MaxPrice = p.Value.max_price,
                MinPrice = p.Value.min_price,
                MinQuantity = p.Value.min_amount,
                QuantityDecimals = p.Value.amount_decimal_places,
                PriceDecimals = p.Value.decimal_places,
                QuoteAsset = p.Value.quoted_currency,
                Symbol = p.Key,
                CmcId =  -1,
                CmcName = p.Value.base_currency,
                CmcSymbol = p.Key
            };
        }

        protected PriceTicker ToPriceTicker(KeyValuePair<string, DSX.Ticker> p)
        {
            return new PriceTicker()
            {
                HighPrice = p.Value.high,
                LowPrice = p.Value.low,
                WeightedAveragePrice = p.Value.avg,
                LastPrice = p.Value.last,
                Bid = p.Value.buy,
                Ask = p.Value.sell,
                PriceChangePercent = 0,
                Symbol = p.Key,
                SymbolInformation = GetSymbolInformation(p.Key),
                Volume = p.Value.vol,
                QuoteVolume = p.Value.vol_cur
            };
        }

        protected PublicTrade ToPublicTrade(DSX.Trade t, SymbolInformation si)
        {
            return new PublicTrade(si)
            {
                Id = t.tid,
                Price = t.price,
                Quantity = t.amount,
                Side = t.type == "bid" ? TradeSide.Buy : TradeSide.Sell,
                Time = t.timestamp.FromUnixSeconds()
            };
        }

        protected SymbolInformation GetSymbolInformation(string symbol)
        {
            SymbolInformation market = null;
            return marketsMapping.TryGetValue(symbol, out market) ? market : null;
        }

        protected void ProcessExchangeInfo(IEnumerable<SymbolInformation> markets)
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
            //marketAssets.AddRange(markets.Select(x => x.QuoteAsset).Distinct().Except(marketAssets));
        }

        protected void ProcessPriceTicker(IEnumerable<PriceTicker> priceTicker)
        {
            var sw = Stopwatch.StartNew();
            if (Tickers.IsEmpty)
            {
                var idCache = new HashSet<string>(marketsMapping.Values.Where(IsValidMarket).Select(si => si.Symbol));
                Tickers.AddRange(priceTicker.Where(t => idCache.Contains(t.Symbol)));
                for (int idx = 0; idx < Tickers.Count; idx += 1)
                {
                    var ticker = Tickers[idx];
                    Debug.Assert(tickersMapping.TryAdd(ticker.Symbol, idx));
                }
            }
            else
            {
                var iter = Tickers.GetEnumerator();
                foreach (var ticker in priceTicker)
                {
                    OnRefreshMarketSummary2(ticker);
                }
            }
            Debug.Print($"ProcessPriceTicker took {sw.ElapsedMilliseconds}ms.");
        }

        protected void ProcessPublicTrades(IEnumerable<PublicTrade> trades)
        {
            var tradesList = trades as List<PublicTrade> ?? trades.ToList();
            if (RecentTrades.All(x => x.SymbolInformation.Symbol != CurrentSymbol.Symbol))
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

        protected void OnRefreshMarketSummary2(PriceTicker ticker)
        {
            if (tickersMapping.TryGetValue(ticker.Symbol, out int idx))
            {
                PriceTicker oldTicker = Tickers[idx];
                Debug.Assert(oldTicker.Symbol == ticker.Symbol);
                bool tickerChanged = IsTickerChanged(oldTicker, ticker);
#if GTK
                ticker.PrevLastPrice = oldTicker.LastPrice;
                marketSummaries[idx] = ticker;
#else
                oldTicker.HighPrice = ticker.HighPrice;
                //oldTicker.LastPriceUsd = CalcUsdPrice(ticker.LastPrice.GetValueOrDefault(), ticker.SymbolInformation);
                oldTicker.LowPrice = ticker.LowPrice;
                oldTicker.PriceChange = ticker.PriceChange;
                oldTicker.QuoteVolume = ticker.QuoteVolume;
                oldTicker.WeightedAveragePrice = ticker.WeightedAveragePrice;
                oldTicker.LastPrice = ticker.LastPrice;
                oldTicker.Bid = ticker.Bid;
                oldTicker.Ask = ticker.Ask;
                if (oldTicker.Volume != null) oldTicker.Volume = ticker.Volume;
                if (oldTicker.PriceChangePercent != null) oldTicker.PriceChangePercent = ticker.PriceChangePercent;
#endif
                Debug.Assert(marketsMapping.TryGetValue(ticker.Symbol, out SymbolInformation market));
                //BalanceManager.UpdateWithLastPrice(market.ProperSymbol, ticker.LastPrice.GetValueOrDefault());
                //if (tickerChanged)
                    //await ProcessTradingRules(ticker);
            }
            else if (marketsMapping.ContainsKey(ticker.Symbol))
            {
                // new PriceTicker?
                if (IsValidMarket(GetSymbolInformation(ticker.Symbol)))
                    AddMarketSummary(ticker);
            }
        }

        protected void AddMarketSummary(PriceTicker ticker)
        {
            if (tickersMapping.TryAdd(ticker.Symbol, Tickers.Count))
                Tickers.Add(ticker);
        }

        protected bool IsTickerChanged(PriceTicker oldTicker, PriceTicker newTicker)
        {
            return oldTicker.LastPrice != newTicker.LastPrice ||
                oldTicker.Bid != newTicker.Bid ||
                oldTicker.Ask != newTicker.Ask ||
                oldTicker.QuoteVolume != newTicker.QuoteVolume;
        }

        protected IEnumerable<SymbolInformation> ValidPairs => marketsMapping.Values.Where(IsValidMarket);
        protected bool IsValidMarket(SymbolInformation si)
        {
            return 
                si.QuoteAsset == "USD" || si.QuoteAsset == "EUR";
        }

        DsxApiClient client = new DsxApiClient();
        private string _status;
        private double _GetExchangeInfoElapsed;
        private double _GetTickersElapsed;
        private double _GetTradesElapsed;
        private double _GetDepthElapsed;
        private ICommand getExchangeInfoCommand;
        private ICommand getTickersCommand;
        private ICommand getTradesCommand;
        private ICommand getDepthCommand;
        IDisposable getTickersSubscription;
        IDisposable getTradesSubscription;
        private ICommand getTickersSubscribeCommand;
        private SymbolInformation currentSymbol;
        private ICommand getTradesSubscribeCommand;
        private ICommand getDepthSubscribeCommand;
    }
}
