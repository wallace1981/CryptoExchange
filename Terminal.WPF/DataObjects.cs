using DynamicData;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Exchange.Net
{
    public enum TradeSide
    {
        Buy,
        Sell
    }

    public class Balance : ReactiveObject
    {
        public const string BTC = "BTC";
        public const string ETH = "ETH";
        public const string USD = "USD";    // NOTE: Some exchanges has true USD instead of Tether USDT.
        public const string USDT = "USDT";  // NOTE: Some exchanges has true USD instead of Tether USDT.

        // NOTE
        // Special cases are: Asset = { BTC, ETH, USD[T] }
        // Rules:
        //   Asset = BTC     BtcPrice = 1.0              EthPrice = 1.0 / EthBtc     UsdPrice = BTCUSD[T]
        //   Asset = ETH     BtcPrice = EthBtc           EthPrice = 1.0              UsdPrice = ETHUSD[T]
        //   Asset = USD[T]  BtcPrice = 1.0 / BtcUsd[t]  EthPrice = 1.0 / EthUsd[T]  UsdPrice = 1.0

        public string Asset { get; }
        public bool HasUsdPair { get;  }
        public decimal Free { get => this.free; set => this.RaiseAndSetIfChanged(ref this.free, value); }
        public decimal Locked { get => this.locked; set => this.RaiseAndSetIfChanged(ref this.locked, value); }
        public decimal Total { get => Free + Locked; }
        public decimal TotalBtc { get => Total * PriceBtc; }
        public decimal TotalEth { get => Total * PriceEth; }
        public decimal FreeUsd { get => Free * PriceUsd; }
        public decimal LockedUsd { get => Locked * PriceUsd; }
        public decimal TotalUsd { get => Total * PriceUsd; }

        public decimal Percentage
        {
            get => this.percentage;
            set => this.RaiseAndSetIfChanged(ref this.percentage, value);
        }

        public decimal PriceBtc
        {
            get => this.priceBtc;
            set
            {
                switch (Asset)
                {
                    case BTC:
                        return;
                    case ETH:
                        // Assuming EthBtc
                        break;
                    case USD:
                    case USDT:
                        // Assuming BtcUsd
                        value = 1M / value;
                        break;
                    default:
                        // Assiming AnyBtc
                        break;
                }
                this.RaiseAndSetIfChanged(ref this.priceBtc, value);
            }
        }

        public decimal PriceEth
        {
            get => this.priceEth;
            set
            {
                switch (Asset)
                {
                    case BTC:
                        // Assuming EthBtc
                        value = 1M / value;
                        break;
                    case ETH:
                        // Assuming EthBtc
                        break;
                    case USD:
                    case USDT:
                        // Assuming EthUsd
                        value = 1M / value;
                        break;
                    default:
                        // Assiming AnyBtc
                        break;
                }
                this.RaiseAndSetIfChanged(ref this.priceEth, value);
            }
        }

        public decimal PriceUsd
        {
            get => this.priceUsd;
            set
            {
                switch (Asset)
                {
                    case BTC:
                        // Assuming BtcUsd
                        break;
                    case ETH:
                        // Assuming EthUsd
                        break;
                    case USD:
                    case USDT:
                        // Assuming EthUsd
                        return;
                    default:
                        // Assiming AnyUsd
                        break;
                }
                this.RaiseAndSetIfChanged(ref this.priceUsd, value);
            }
        }

        // NOTE
        // Calling side should decide update this field or not
        // regarding avaialbe exchange pairs.
        // For ex. if exchange has LTCUSD, then for LTC asset BtcUsd should never be updated.
        public decimal BtcUsd
        {
            get => this.btcUsd;
            set => this.RaiseAndSetIfChanged(ref this.btcUsd, value);
        }

        public Balance(string asset, bool hasUsdPair = false)
        {
            this.Asset = asset;
            this.HasUsdPair = asset == USD || asset == USDT || hasUsdPair;
            switch (asset)
            {
                case BTC:
                    priceBtc = 1M;
                    break;
                case ETH:
                    priceEth = 1M;
                    break;
                case USD:
                case USDT:
                    priceUsd = 1M;
                    break;
            }

            this.ObservableForProperty(m => m.BtcUsd).Subscribe(x => PriceUsd = PriceBtc * x.Value);
            this.ObservableForProperty(m => m.PriceUsd).Subscribe(
                x =>
                {
                    this.RaisePropertyChanged(nameof(FreeUsd));
                    this.RaisePropertyChanged(nameof(LockedUsd));
                    this.RaisePropertyChanged(nameof(TotalUsd));
                });
            this.ObservableForProperty(m => m.PriceEth).Subscribe(
                x => 
                {
                    this.RaisePropertyChanged(nameof(TotalEth));
                });
            this.ObservableForProperty(m => m.PriceBtc).Subscribe(
                x =>
                {
                    this.RaisePropertyChanged(nameof(TotalBtc));
                    if (!HasUsdPair) PriceUsd = x.Value * this.btcUsd;
                });
            this.WhenAnyValue(m => m.Locked, m => m.Free).Subscribe(
                x =>
                {
                    this.RaisePropertyChanged(nameof(Total));
                    this.RaisePropertyChanged(nameof(TotalBtc));
                    this.RaisePropertyChanged(nameof(TotalEth));
                    this.RaisePropertyChanged(nameof(LockedUsd));
                    this.RaisePropertyChanged(nameof(TotalUsd));
                });
        }

        public void UpdatePercentage(decimal totalWalletBtc)
        {
            Percentage = Math.Round((TotalBtc / totalWalletBtc) * 100M, 2);
        }

        private decimal free;
        private decimal locked;
        private decimal btcUsd;
        private decimal priceUsd;
        private decimal priceBtc;
        private decimal priceEth;
        private decimal percentage;
    }

    public class BalanceManager : ReactiveObject
    {
        // TODO: expose only IObservableCache
        public SourceCache<Balance, string> Balances { get; } = new SourceCache<Balance, string>(x => x.Asset);
        //public IObservable<IChangeSet<Balance,string>> Stream { get; }

        public decimal TotalBtc { get => Math.Round(Balances.Items.Sum(b => b.TotalBtc), 8); }
        public decimal TotalUsd { get => Math.Round(Balances.Items.Sum(b => b.TotalUsd), 2); }
        public decimal BtcUsd
        {
            get => btcUsd;
            set => this.RaiseAndSetIfChanged(ref this.btcUsd, value);
        }

        public BalanceManager()
        {

            //Stream = Balances.Connect();
            this.ObservableForProperty(m => m.BtcUsd).Subscribe(
                x =>
                {
                    Balances.Items.ToList().ForEach(b => { if (!b.HasUsdPair) b.BtcUsd = x.Value; });
                });
        }

        public void AddUpdateBalance(Balance balance)
        {
            //Balances.AddOrUpdate(balance);
            var b = Balances.Lookup(balance.Asset);
            if (b.HasValue)
            {
                b.Value.Free = balance.Free;
                b.Value.Locked = balance.Locked;
            }
            else
            {
                Balances.AddOrUpdate(balance);
            }
            UpdatePercentages();
            this.RaisePropertyChanged(nameof(TotalBtc));
            this.RaisePropertyChanged(nameof(TotalUsd));
        }

        private void UpdatePercentages()
        {
            var copyTotalBtc = TotalBtc;
            if (copyTotalBtc > Decimal.Zero)
            {
                foreach (var balance in Balances.Items.Where(b => b.Total > decimal.Zero))
                {
                    balance.UpdatePercentage(copyTotalBtc);
                }
            }
        }

        public bool UpdateWithLastPrice(string symbol, decimal price)
        {
            // NOTE
            // In case symbol is BTCUSD[T] we need to update both Assets:
            // BTC with UsdPrice, USD[T] with BtcPrice.
            bool result = false;
            if ((symbol == Balance.BTC + Balance.USD) || (symbol == Balance.BTC + Balance.USDT))
            {
                BtcUsd = price;
            }
            if (symbol.EndsWith(Balance.BTC, StringComparison.CurrentCultureIgnoreCase))
            {
                var asset = symbol.Replace(Balance.BTC, String.Empty);
                var balance = Balances.Lookup(asset);
                if (balance.HasValue)
                {
                    balance.Value.PriceBtc = price;
                    result = true;
                    UpdatePercentages();
                }
            }
            if (symbol.EndsWith(Balance.ETH, StringComparison.CurrentCultureIgnoreCase))
            {
                var asset = symbol.Replace(Balance.ETH, String.Empty);
                var balance = Balances.Lookup(asset);
                if (balance.HasValue)
                {
                    balance.Value.PriceEth = price;
                    result = true;
                }
            }
            if (symbol.EndsWith(Balance.USD, StringComparison.CurrentCultureIgnoreCase) ||
                symbol.EndsWith(Balance.USDT, StringComparison.CurrentCultureIgnoreCase))
            {
                var asset = symbol.Replace(Balance.USDT, String.Empty).Replace(Balance.USD, String.Empty);
                var balance = Balances.Lookup(asset);
                if (balance.HasValue)
                {
                    balance.Value.PriceUsd = price;
                    result = true;
                }
                if (asset == Balance.BTC)
                {
                    balance = Balances.Lookup("USD");
                    if (balance.HasValue) balance.Value.PriceBtc = price;
                    balance = Balances.Lookup("USDT");
                    if (balance.HasValue) balance.Value.PriceBtc = price;
                }
            }
            if (symbol.EndsWith("PAX"))
            {
                var asset = symbol.Replace("PAX", string.Empty);
                if (asset == "BTC")
                {
                    var b = Balances.Lookup("PAX");
                    if (b.HasValue)
                        b.Value.PriceBtc = 1m / price;
                }
            }
            if (symbol.EndsWith("EUR"))
            {
                var asset = symbol.Replace("EUR", string.Empty);
                if (asset == "BTC")
                {
                    var b = Balances.Lookup("EUR");
                    if (b.HasValue)
                        b.Value.PriceBtc = 1m / price;
                }
            }
            this.RaisePropertyChanged(nameof(TotalBtc));
            this.RaisePropertyChanged(nameof(TotalUsd));
            return result;
        }

        private decimal btcUsd;
    }

    public class SymbolInformation
    {
        public long CmcId { get; set; }
        public string CmcName { get; set; }
        public string CmcSymbol { get; set; }
        public string Symbol { get; set; }
        public string BaseAsset { get; set; }
        public string QuoteAsset { get; set; }
        public Balance BaseAssetBalance { get; set; }
        public Balance QuoteAssetBalance { get; set; }
        public PriceTicker PriceTicker { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal TickSize { get; set; }
        public decimal MinQuantity { get; set; }
        public decimal MaxQuantity { get; set; }
        public decimal StepSize { get; set; }
        public int PriceDecimals { get; set; }
        public int QuantityDecimals { get; set; }
        public decimal MinNotional { get; set; }
        public decimal TotalDecimals { get; set; }
        public string PriceFmt => $"N{PriceDecimals}";
        public string QuantityFmt => $"N{QuantityDecimals}";
        public string TotalFmt => $"N{TotalDecimals}";
        //public string ImageUrl { get => $"https://raw.githubusercontent.com/cjdowner/cryptocurrency-icons/master/128/color/{BaseAsset?.ToLower()}.png"; }
        //public string ProperSymbol { get => (BaseAsset + QuoteAsset).ToUpper(); }
        public string ImageUrl => $"https://s2.coinmarketcap.com/static/img/coins/32x32/{CmcId}.png";
        public string ProperSymbol => (BaseAsset + QuoteAsset).ToUpper();
        public string Caption => $"{BaseAsset}/{QuoteAsset}";
		public string Status { get; set; }

        public decimal ClampQuantity(decimal quantity)
        {
            quantity = Math.Min(MaxQuantity, quantity);
            quantity = Math.Max(MinQuantity, quantity);
            quantity -= quantity % StepSize;
            quantity = Floor(quantity);
            return quantity;
        }

        public decimal ClampPrice(decimal price)
        {
            price = Math.Min(MaxPrice, price);
            price = Math.Max(MinPrice, price);
            price -= price % TickSize;
            price = Floor(price);
            return price;
        }

        private static decimal Floor(decimal number)
        {
            return Math.Floor(number * 100000000) / 100000000;
        }
    }

    public class PublicTrade : ReactiveObject
    {
        private decimal _Price;
        private decimal _Quantity;
        private decimal _QuantityPercentage;

        public SymbolInformation SymbolInformation { get; }
        public long Id { get; set; }
        public DateTime Time { get; set; }
        public decimal Price
        {
            get { return _Price; }
            set { this.RaiseAndSetIfChanged(ref _Price, value); }
        }
        public decimal Quantity
        {
            get { return _Quantity; }
            set { this.RaiseAndSetIfChanged(ref _Quantity, value); }
        }
        public TradeSide Side { get; set; }
        public decimal Total { get => Price * Quantity;  }
        public decimal QuantityPercentage
        {
            get => _QuantityPercentage;
            set => this.RaiseAndSetIfChanged(ref _QuantityPercentage, value);
        }
        public string PriceFmt => SymbolInformation.PriceFmt;
        public string QuantityFmt => SymbolInformation.QuantityFmt;
        public string TotalFmt => SymbolInformation.TotalFmt;

        public PublicTrade(SymbolInformation si)
        {
            SymbolInformation = si;
        }
    }

    public class PriceTicker : ReactiveObject
    {
        private string _symbol;
        private decimal? _lastPrice;
        private decimal? _prevLastPrice;
        private decimal? _priceChangePercent;
        private decimal? _volume;
        private decimal? _quoteVolume;
        private decimal _buyVolume;
        private bool _IsLastPriceUpdated;
        private decimal _WeightedAveragePrice;
        private decimal _LastPriceUsd;
        private decimal _HighPrice;
        private decimal _LowPrice;
        private decimal _PriceChange;
        private decimal? _bid, _ask;
        private CancellationTokenSource ctsLastPrice;

        public string Symbol { get => _symbol; set => this.RaiseAndSetIfChanged(ref _symbol, value); }
        public decimal WeightedAveragePrice { get => _WeightedAveragePrice; set => this.RaiseAndSetIfChanged(ref _WeightedAveragePrice, value); }
        public decimal LastPriceUsd { get => _LastPriceUsd; set => this.RaiseAndSetIfChanged(ref _LastPriceUsd, value); }
        public decimal HighPrice { get => _HighPrice; set => this.RaiseAndSetIfChanged(ref _HighPrice, value); }
        public decimal LowPrice { get => _LowPrice; set => this.RaiseAndSetIfChanged(ref _LowPrice, value); }
        public decimal PriceChange { get => _PriceChange; set => this.RaiseAndSetIfChanged(ref _PriceChange, value); }

        public decimal? LastPrice
        {
            get => _lastPrice;
            set
            {
                PrevLastPrice = _lastPrice;
                if (value != PrevLastPrice)
                {
                    this.RaiseAndSetIfChanged(ref _lastPrice, value);
                    if (ctsLastPrice != null)
                    {
                        ctsLastPrice.Cancel();
                        ctsLastPrice.Dispose();
                    }
                    ctsLastPrice = new CancellationTokenSource();
                    IsLastPriceUpdated = true;
                    Task.Delay(500, ctsLastPrice.Token).ContinueWith((x) => IsLastPriceUpdated = false);
                }
                this.RaisePropertyChanged(nameof(PriceDiff));
            }
        }
        public decimal? Bid { get => _bid; set => this.RaiseAndSetIfChanged(ref _bid, value); }
        public decimal? Ask { get => _ask; set => this.RaiseAndSetIfChanged(ref _ask, value); }
        public decimal? PrevLastPrice { get => _prevLastPrice; set { if (value != decimal.Zero) _prevLastPrice = value; } }
        public decimal? PriceChangePercent { get => _priceChangePercent; set => this.RaiseAndSetIfChanged(ref _priceChangePercent, value); }
        public decimal? Volume { get => _volume; set => this.RaiseAndSetIfChanged(ref _volume, value); }
        public decimal? QuoteVolume { get => _quoteVolume; set => this.RaiseAndSetIfChanged(ref _quoteVolume, value); }
        public bool IsPriceChanged => PrevLastPrice != null && LastPrice != PrevLastPrice;
        public bool IsLastPriceUpdated { get => _IsLastPriceUpdated; set => this.RaiseAndSetIfChanged(ref _IsLastPriceUpdated, value);}
        public decimal BuyVolume { get => _buyVolume; set => this.RaiseAndSetIfChanged(ref _buyVolume, value); }
        public SymbolInformation SymbolInformation { get; set; }
        public decimal PriceDiff => PrevLastPrice != null && LastPrice != null ? LastPrice.Value - PrevLastPrice.Value : decimal.Zero;

        [Reactive] public Candle Candle1m { get; set; }
        [Reactive] public Candle Candle5m { get; set; }
        [Reactive] public Candle Candle15m { get; set; }

    }

    public class Candle
    {
        public string Symbol { get; set; }
        public DateTime OpenTime { get; set; }
        public DateTime CloseTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public decimal QuoteVolume { get; set; }
        public decimal BuyVolume { get; set; }
        public decimal BuyQuoteVolume { get; set; }
    }

    public enum TransferType
    {
        Deposit,
        Withdrawal
    }

    public enum TransferStatus
    {
        EmailSent,
        Pending,
        Cancelled,
        AwaitingApproval,
        Rejected,
        Processing,
        Failed,
        Completed,
        Undefined
    }

    public class Transfer
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public TransferType Type { get; set; }
        public string Asset { get; set; }
        public decimal Quantity { get; set; }
        public string Address { get; set; }
        public decimal Comission { get; set; }
        public TransferStatus Status { get; set; }
    }

    public class OrderBookEntry : ReactiveObject
    {
        //public OrderBookEntry(int priceDecimals, int qtyDecimals)
        //{
        //    this.priceDecimals = priceDecimals;
        //    this.qtyDecimals = qtyDecimals;
        //}

        public OrderBookEntry(SymbolInformation si)
        {
            SymbolInformation = si;
        }

        public SymbolInformation SymbolInformation { get; }
        [Reactive] public TradeSide Side { get; set; }
        public decimal Price
        {
            get { return _Price; }
            set { this.RaiseAndSetIfChanged(ref _Price, value); this.RaisePropertyChanged(nameof(Total)); }
        }
        public virtual decimal Quantity
        {
            get { return _Quantity; }
            set { this.RaiseAndSetIfChanged(ref _Quantity, value); this.RaisePropertyChanged(nameof(Total)); }
        }
		public virtual decimal Total => Price * Quantity;
        public virtual decimal TotalCumulative
        {
            get { return _TotalCumulative; }
            set { this.RaiseAndSetIfChanged(ref _TotalCumulative, value); }
        }
        public decimal QuantityPercentage
        {
            get { return _QuantityPercentage; }
            set { this.RaiseAndSetIfChanged(ref _QuantityPercentage, value); }
        }

        public virtual bool HasPrice(decimal price)
        {
            return Price == price;
        }

        public virtual decimal RemoveQuantity(decimal price)
        {
            return price == Price ? decimal.Zero : Quantity;
        }

        public virtual void UpdateQuantity(OrderBookEntry e)
        {
            if (e.Price == Price)
                Quantity = e.Quantity;
        }

        public string PriceFmt => SymbolInformation.PriceFmt;
        public string QuantityFmt => SymbolInformation.QuantityFmt;
        public string TotalFmt => SymbolInformation.TotalFmt;

        private decimal _Price;
        private decimal _Quantity;
        private decimal _TotalCumulative;
        private decimal _QuantityPercentage;
    }

    public class OrderBook : ReactiveObject
    {
        public int MergeDecimals { get; set; }
        public SymbolInformation SymbolInformation { get; set; }
        public ReactiveList<OrderBookEntry> Bids { get; }
        public ReactiveList<OrderBookEntry> Asks { get; }
        public decimal Spread
        {
            get { return CalcSpread(); }

        }
        public decimal SpreadPercentage
        {
            get { return CalcSpreadPercentage(); }
        }

        public long lastUpdateId = 0;

        public bool IsEmpty => Bids.IsEmpty && Asks.IsEmpty;

        public OrderBook(SymbolInformation si)
        {
            SymbolInformation = si;
            Bids = new ReactiveList<OrderBookEntry>();
            Asks = new ReactiveList<OrderBookEntry>();
        }

        public void Assign(IEnumerable<OrderBookEntry> depth)
        {
            if (MergeDecimals != SymbolInformation.PriceDecimals)
            {
                factor = CalcFactor(MergeDecimals);
                Merge(depth);
            }
            else
            {
                factor = decimal.Zero;
                Replace(depth.Where(x => x.Side == TradeSide.Sell), depth.Where(x => x.Side == TradeSide.Buy));
            }
        }

        public void Update(IEnumerable<OrderBookEntry> bookUpdates)
        {
            // 1. merge
            // 2. apply update
            // 3. calc agg total
            if (MergeDecimals != SymbolInformation.PriceDecimals)
            {
                bookUpdates = bookUpdates.GroupBy(x => x.MergePrice(factor), x => x,
                    (priceLevel, entries) => new OrderBookEntry(SymbolInformation) { Price = priceLevel, Quantity = entries.Sum(y => y.Quantity), Side = entries.First().Side });
            }
            if (IsEmpty)
            {
                Assign(bookUpdates);
                return;
            }
            foreach (var e in Bids.ToList())
            {
                if (!bookUpdates.Any(x => x.Side == e.Side && x.Price == e.Price))
                    Bids.Remove(e);
            }
            foreach (var e in Asks.ToList())
            {
                if (!bookUpdates.Any(x => x.Side == e.Side && x.Price == e.Price))
                    Asks.Remove(e);
            }
            foreach (var e in bookUpdates)
            {
                AddOrUpdate(e);
            }
            CalcAggregatedTotal();
            this.RaisePropertyChanged(nameof(Spread));
            this.RaisePropertyChanged(nameof(SpreadPercentage));
        }

        public void UpdateIncremental(IEnumerable<OrderBookEntry> bookUpdates)
        {
            if (IsEmpty)
            {
                Assign(bookUpdates);
            }
            else
            {
                foreach (var e in bookUpdates)
                    Update(e);
                CalcAggregatedTotal();
            }
        }

        public void Update(OrderBookEntry e)
        {
            if (e.Quantity == decimal.Zero)
            {
                RemoveQuantity(e.Price);
            }
            else
            {
                AddOrUpdate(e);
            }
        }

        public void Clear()
        {
            Asks.Clear();
            Bids.Clear();
            lastUpdateId = 0;
        }

        public int PriceDecimals => MergeDecimals;
        public int QuantityDecimals => SymbolInformation.QuantityDecimals;

        private void CalcAggregatedTotal()
        {
            decimal aggTotal = decimal.Zero;
            decimal? qmin = Asks.IsEmpty ? (decimal?)null : Asks.Select(x => x.Total).Min();
            decimal? qmax = Asks.IsEmpty ? (decimal?)null : Asks.Select(x => x.Total).Max();
            //Debug.Print($"Min: {qmin}; Max: {qmax}.");
            foreach (var ask in Asks.Reverse().ToList())
            {
                aggTotal += ask.Total;
                ask.TotalCumulative = aggTotal;
                ask.QuantityPercentage = CalcPercent(qmin, qmax, ask.Total);
            }
            aggTotal = 0;
            qmin = Bids.IsEmpty ? (decimal?)null : Bids.Select(x => x.Total).Min();
            qmax = Bids.IsEmpty ? (decimal?)null : Bids.Select(x => x.Total).Max();
            foreach (var bid in Bids.ToList())
            {
                aggTotal += bid.Total;
                bid.TotalCumulative = aggTotal;
                bid.QuantityPercentage = CalcPercent(qmin, qmax, bid.Total);
            }
            this.RaisePropertyChanged(nameof(Spread));
            this.RaisePropertyChanged(nameof(SpreadPercentage));
        }

        public static decimal CalcPercent(decimal? min, decimal? max, decimal value)
        {
            if (min != null && max != null)
            {
                decimal onePercent = max.Value / 100m;
                //System.Diagnostics.Debug.Assert((value / onePercent) <= 100m);
                return Math.Round(value / onePercent, 2);
            }
            return decimal.Zero;
        }

        private void RemoveQuantity(decimal priceLevel)
        {
            var removalBids = Bids.Where(x => x.RemoveQuantity(priceLevel) == decimal.Zero).ToList();
            var removalAsks = Asks.Where(x => x.RemoveQuantity(priceLevel) == decimal.Zero).ToList();
            Asks.RemoveAll(removalAsks);
            Bids.RemoveAll(removalBids);
        }

        private void AddOrUpdate(OrderBookEntry e)
        {
            var bidsOrAsks = e.Side == TradeSide.Buy ? Bids : Asks;
            var priceLevel = e.MergePrice(factor);
            var item = bidsOrAsks.LastOrDefault(x => priceLevel <= x.Price);
            if (item != null)
            {
                if (priceLevel == item.Price)
                    item.UpdateQuantity(e);
                else
                {
                    var idx = bidsOrAsks.IndexOf(item);
                    Insert(e, idx + 1, priceLevel);
                }
            }
            else
            {
                Insert(e, 0, priceLevel);
            }
        }

        private void Insert(OrderBookEntry e, int idx, decimal priceLevel)
        {
            if (MergeDecimals != SymbolInformation.PriceDecimals)
            {
                e = new OrderBookEntryGroup(SymbolInformation, e.Side, priceLevel, Enumerable.Repeat(e, 1));
            }
            if (e.Side == TradeSide.Sell)
                Asks.Insert(idx, e);
            else
                Bids.Insert(idx, e);
        }

        private void Merge(IEnumerable<OrderBookEntry> depth)
        {
            var tmpAsks = depth.Where(x => x.Side == TradeSide.Sell).GroupBy(x => x.MergePrice(factor), x => x,
                (priceLevel, entries) => new OrderBookEntryGroup(SymbolInformation, TradeSide.Sell, priceLevel, entries)).ToList();
            var tmpBids = depth.Where(x => x.Side == TradeSide.Buy).GroupBy(x => x.MergePrice(factor), x => x,
                (priceLevel, entries) => new OrderBookEntryGroup(SymbolInformation, TradeSide.Buy, priceLevel, entries)).ToList();
            Replace(tmpAsks, tmpBids);
        }

        private void Replace(IEnumerable<OrderBookEntry> asks, IEnumerable<OrderBookEntry> bids)
        {
            Asks.Clear();
            Asks.AddRange(asks);
            Bids.Clear();
            Bids.AddRange(bids);
            CalcAggregatedTotal();
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

        protected decimal CalcSpread()
        {
            if (Bids.Count < 1 || Asks.Count < 1)
                return 0;
            return Asks.Last().Price - Bids.First().Price;
        }

        protected decimal CalcSpreadPercentage()
        {
            if (Bids.Count < 1 || Asks.Count < 1)
                return 0;
            return Asks.Last().Price / Bids.First().Price - 1m;
        }

        private decimal factor;
    }

    public class OrderBookEntryGroup : OrderBookEntry
    {
        private List<OrderBookEntry> entries;

        public OrderBookEntryGroup(SymbolInformation si, TradeSide side, decimal priceLevel, IEnumerable<OrderBookEntry> entries) : base(si)
        {
            Price = priceLevel;
            Side = side;
            this.entries = new List<OrderBookEntry>(entries);
        }

        //public override bool HasPrice(decimal price)
        //{
        //    return entries.Any(x => x.HasPrice(price));
        //}

        public override decimal RemoveQuantity(decimal price)
        {
            var e = entries.SingleOrDefault(x => x.Price == price);
            if (e != null)
            {
                entries.Remove(e);
                this.RaisePropertyChanged(nameof(Quantity));
                this.RaisePropertyChanged(nameof(Total));
            }
            return Quantity;
        }

        public override void UpdateQuantity(OrderBookEntry e)
        {
            var item = entries.SingleOrDefault(x => e.Price == x.Price);
            if (item != null)
                item.UpdateQuantity(e);
            else
                entries.Add(e);
            this.RaisePropertyChanged(nameof(Quantity));
            this.RaisePropertyChanged(nameof(Total));
        }

        public override decimal Quantity
        {
            get { return entries.Sum(x => x.Quantity); }
        }

        public override decimal Total => entries.Sum(x => x.Total);

    }

    public static class OrderBookEntryExtensions
    {
        public static decimal MergePrice(this OrderBookEntry entry, decimal factor)
        {
            if (factor == decimal.Zero)
                return entry.Price;
            if (entry.Side == TradeSide.Buy)
            {
                return Math.Truncate(entry.Price * factor) / factor;
            }
            else
            {
                return Math.Ceiling(entry.Price * factor) / factor;
            }
        }
    }

    public enum OrderStatus
    {
        Active,
        Filled,
        PartiallyFilled,
        Rejected,
        Cancelled,
        Cancelling,
        Undefined
    }

    public class Order : OrderBookEntry
    {
        public Order(SymbolInformation si) : base(si)
        {
            this.Fills = new List<OrderTrade>();
            this.ObservableForProperty(x => x.LastPrice).Subscribe(y => this.RaisePropertyChanged(nameof(Profit)));
        }

        public string Type { get; set; }
        public decimal StopPrice { get; set; }
        public DateTime Created { get; set; }
        public DateTime Updated { get; set; }
        public string OrderId { get; set; }
        public decimal LastPrice
        {
            get { return lastPrice; }
            set { this.RaiseAndSetIfChanged(ref lastPrice, value); }
        }
        public decimal ExecutedQuantity
        {
            get { return executedQuantity; }
            set { this.RaiseAndSetIfChanged(ref executedQuantity, value); }
        }
        public decimal Profit => (LastPrice - Price) * Quantity * (Side == TradeSide.Sell ? -1m : 1m);
        public OrderStatus Status { get; set; }
        public List<OrderTrade> Fills { get; }

        private decimal lastPrice;
        private decimal executedQuantity;
    }

    public class OrderTrade : OrderBookEntry
    {
        public string Id { get; set; }
        public string OrderId { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Comission { get; set; }
        public string ComissionAsset { get; set; }

        public OrderTrade(SymbolInformation si) : base(si)
        {
        }
    }
}
