using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public ReactiveList<Balance> Balances { get; } = new ReactiveList<Balance>();

        public decimal TotalBtc { get => Math.Round(Balances.Sum(b => b.TotalBtc), 8); }
        public decimal TotalUsd { get => Math.Round(Balances.Sum(b => b.TotalUsd), 2); }
        public decimal BtcUsd
        {
            get => btcUsd;
            set => this.RaiseAndSetIfChanged(ref this.btcUsd, value);
        }

        public BalanceManager()
        {
            this.ObservableForProperty(m => m.BtcUsd).Subscribe(
                x =>
                {
                    Balances.ToList().ForEach(b => { if (!b.HasUsdPair) b.BtcUsd = x.Value; });
                });
        }

        public void AddUpdateBalance(Balance balance)
        {
            if (Balances.Any(b => b.Asset == balance.Asset))
            {
                // update
                var current = Balances.SingleOrDefault(b => b.Asset == balance.Asset);
                current.Free = balance.Free;
                current.Locked = balance.Locked;
            }
            else
            {
                // add
                Balances.Add(balance);
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
                foreach (var balance in Balances.Where(b => b.Total > decimal.Zero))
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
                var balance = Balances.SingleOrDefault(b => b.Asset == asset);
                if (balance != null)
                {
                    balance.PriceBtc = price;
                    result = true;
                    UpdatePercentages();
                }
            }
            if (symbol.EndsWith(Balance.ETH, StringComparison.CurrentCultureIgnoreCase))
            {
                var asset = symbol.Replace(Balance.ETH, String.Empty);
                var balance = Balances.SingleOrDefault(b => b.Asset == asset);
                if (balance != null)
                {
                    balance.PriceEth = price;
                    result = true;
                }
            }
            if (symbol.EndsWith(Balance.USD, StringComparison.CurrentCultureIgnoreCase) ||
                symbol.EndsWith(Balance.USDT, StringComparison.CurrentCultureIgnoreCase))
            {
                var asset = symbol.Replace(Balance.USDT, String.Empty).Replace(Balance.USD, String.Empty);
                var balance = Balances.SingleOrDefault(b => b.Asset == asset);
                if (balance != null)
                {
                    balance.PriceUsd = price;
                    result = true;
                }
                if (asset == Balance.BTC)
                {
                    balance = Balances.SingleOrDefault(b => b.Asset == "USD");
                    if (balance != null) balance.PriceBtc = price;
                    balance = Balances.SingleOrDefault(b => b.Asset == "USDT");
                    if (balance != null) balance.PriceBtc = price;
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
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal TickSize { get; set; }
        public decimal MinQuantity { get; set; }
        public decimal MaxQuantity { get; set; }
        public decimal StepSize { get; set; }
        public int PriceDecimals { get; set; }
        public int QuantityDecimals { get; set; }
        public decimal MinNotional { get; set; }
        public string PriceFmt { get => "N" + PriceDecimals.ToString(); }
        public string QuantityFmt { get => "N" + QuantityDecimals.ToString(); }
        //public string ImageUrl { get => $"https://raw.githubusercontent.com/cjdowner/cryptocurrency-icons/master/128/color/{BaseAsset?.ToLower()}.png"; }
        //public string ProperSymbol { get => (BaseAsset + QuoteAsset).ToUpper(); }
        public string ImageUrl { get => $"https://s2.coinmarketcap.com/static/img/coins/32x32/{CmcId}.png"; }
        public string ProperSymbol { get => Symbol.ToUpper(); }
		public string Status { get; set; }
    }

    public class PublicTrade
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public string Symbol { get; set; }
        public TradeSide Side { get; set; }
        public decimal Total { get => Price * Quantity;  }
    }

    public class PriceTicker : ReactiveObject
    {
        private string _symbol;
        private decimal _lastPrice;
        private decimal? _prevLastPrice;
        private decimal? _priceChangePercent;
        private decimal? _volume;
        private decimal _buyVolume;

        public string Symbol { get => _symbol; set => this.RaiseAndSetIfChanged(ref _symbol, value); }
        public decimal LastPrice { get => _lastPrice; set { PrevLastPrice = _lastPrice; this.RaiseAndSetIfChanged(ref _lastPrice, value); } }
        public decimal? PrevLastPrice { get => _prevLastPrice; set { if (value != decimal.Zero) _prevLastPrice = value; } }
        public decimal? PriceChangePercent { get => _priceChangePercent; set => this.RaiseAndSetIfChanged(ref _priceChangePercent, value); }
        public decimal? Volume { get => _volume; set => this.RaiseAndSetIfChanged(ref _volume, value); }
        public bool IsPriceChanged => LastPrice != PrevLastPrice.GetValueOrDefault();
        public decimal BuyVolume { get => _buyVolume; set => this.RaiseAndSetIfChanged(ref _buyVolume, value); }
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
        public DateTime Timestamp { get; set; }
        public TransferType Type { get; set; }
        public string Asset { get; set; }
        public decimal Quantity { get; set; }
        public string Address { get; set; }
        public decimal Comission { get; set; }
        public TransferStatus Status { get; set; }
    }

    public class OrderBookEntry
    {
        public TradeSide Side { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
		public decimal Total => Price * Quantity;
    }

    public class OrderBook
    {
        public List<OrderBookEntry> Bids { get; }
        public List<OrderBookEntry> Asks { get; }

        public OrderBook()
        {
            Bids = new List<OrderBookEntry>();
            Asks = new List<OrderBookEntry>();
        }
    }

    public class Order : OrderBookEntry
    {
        public string Symbol { get; set; }
        public string Type { get; set; }
        public decimal StopPrice { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
