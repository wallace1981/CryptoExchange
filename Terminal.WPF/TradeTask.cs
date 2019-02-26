using DynamicData;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Legacy;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Exchange.Net
{

    //
    //  BUY Cond:   SL > LastPrice < TP1
    //  TP  Cond:   Bid >= TP && Bid Qty >= TP Qty
    //

    public enum OrderKind
    {
        Buy,
        StopLoss,
        TakeProfit,
        StopLossOrTakeProfit
    }

    public enum OrderType
    {
        LIMIT,      // could be STOP-LIMIT as well, if supported by exchange.
        STOP_LIMIT,
        MARKET,
        TRAILING    // market order with moving target ;)
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class OrderTask
    {

        [JsonProperty("SIDE")]
        public TradeSide Side;

        [JsonProperty("SYMBOL")]
        public string Symbol;

        [JsonProperty("ORDER_TYPE")]
        public OrderType OrderType;

        [JsonProperty("ORDER_KIND")]
        public OrderKind OrderKind;

        [JsonProperty("PRICE")]
        public decimal Price;

        [JsonProperty("QTY")]
        public decimal Quantity;

        [JsonProperty("QTY_PRCNT", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public double QuantityPercent;

        [JsonProperty("TRAILING_PRCNT", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public double TrailingPercent;

        [JsonProperty("ORDERID")]
        public string OrderId;

        public Order ExchangeOrder;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class TradeTask
    {
        [JsonProperty("ID")]
        public Guid Id;

        [JsonProperty("SYMBOL")]
        public string Symbol;

        [JsonProperty("EXCHANGE")]
        public string Exchange;

        [JsonProperty("CREATED")]
        public DateTime Created;

        [JsonProperty("UPDATED")]
        public DateTime Updated;

        [JsonProperty("BUY")]
        public OrderTask Buy;

        [JsonProperty("SL")]
        public OrderTask StopLoss;

        [JsonProperty("TP")]
        public OrderTask[] TakeProfit;

        public DateTime LastGetOrder = DateTime.MinValue;

        public bool IsInPosition => BuyQty > 0m;
        public bool IsBuyPlaced { get; }

        public decimal AvgBuyPrice => BuyQty > 0 ? TotalQuoteBuy / BuyQty : 0;
        public decimal AvgSellPrice => SellQty > 0 ? TotalQuoteSell / SellQty : 0;
        public decimal Qty => BuyQty - SellQty;
        public decimal TotalQuoteBuy => BuyTrades.Sum(x => x.Total);
        public decimal TotalQuoteSell => SellTrades.Sum(x => x.Total);
        public decimal Profit => TotalQuoteBuy > 0 ? TotalQuoteSell / TotalQuoteBuy - 1 : 0;

        public IObservableCache<OrderTrade, string> TradesStream => Trades.AsObservableCache();

        protected SourceCache<OrderTrade, string> Trades { get; }
        protected IEnumerable<OrderTrade> BuyTrades => Trades.Items.Where(x => x.Side == TradeSide.Buy);
        protected IEnumerable<OrderTrade> SellTrades => Trades.Items.Where(x => x.Side == TradeSide.Sell);

        public decimal BuyQty => BuyTrades.Sum(x => x.Quantity);
        public decimal SellQty => SellTrades.Sum(x => x.Quantity);

        public TradeTask(IEnumerable<OrderTrade> exchangeTrades = null)
        {
            Trades = new SourceCache<OrderTrade, string>(x => x.Id);
            if (exchangeTrades != null)
                Trades.Edit(innerList => innerList.AddOrUpdate(exchangeTrades));
            Jobs = new Queue<OrderTask>();
            FinishedJobs = new Queue<OrderTask>();
        }

        public void RegisterTrade(OrderTrade trade)
        {
            Trades.AddOrUpdate(trade);
        }

        public TradeTask LoadFromJson(string json)
        {
            var tt = new TradeTask();
            JsonConvert.PopulateObject(json, tt);
            Trades.Edit(innerList => innerList.AddOrUpdate(Buy.ExchangeOrder.Fills));
            Trades.Edit(innerList => innerList.AddOrUpdate(StopLoss.ExchangeOrder.Fills));
            foreach (var ot in TakeProfit)
                Trades.Edit(innerList => innerList.AddOrUpdate(ot.ExchangeOrder.Fills));
            return tt;
        }

        [JsonProperty("QUEUE")]
        public Queue<OrderTask> Jobs { get; set; }

        [JsonProperty("PROCESSED")]
        public Queue<OrderTask> FinishedJobs { get; set; }
    }

    public class OrderTaskViewModel : ReactiveObject
    {
        public OrderTask Model { get; }

        public decimal Price
        {
            get { return Model.Price; }
            set { this.RaiseAndSetIfChanged(ref Model.Price, value); }
        }

        public decimal Quantity
        {
            get { return Model.Quantity; }
            set { this.RaiseAndSetIfChanged(ref Model.Quantity, value);  }
        }

        public decimal Total
        {
            get => Quantity * Price;
        }

        public OrderTaskViewModel(TradeTaskViewModel tt)
        {
            Model = new OrderTask();
            TradeTask = tt;
            this.WhenAnyValue(x => x.Price, y => y.Quantity).Subscribe(z => this.RaisePropertyChanged(nameof(Total)));
        }

        public OrderTaskViewModel(TradeTaskViewModel tt, OrderTask model)
        {
            Model = model;
            TradeTask = tt;
            this.WhenAnyValue(x => x.Price, y => y.Quantity).Subscribe(z => this.RaisePropertyChanged(nameof(Total)));
        }

        public TradeTaskViewModel TradeTask { get; }
    }

    public class TakeProfitViewModel : OrderTaskViewModel
    {
        private double qtyPercentStart;
        private double qtyPercentEnd;

        public TakeProfitViewModel(TradeTaskViewModel tt, double initialQtyPrcnt, TakeProfitViewModel prev = null) : base(tt)
        {
            Previous = prev;
            if (Previous != null)
            {
                QuantityPercentStart = Previous.QuantityPercentEnd;
                this.ObservableForProperty(x => x.Previous.QuantityPercentEnd).Subscribe(x => QuantityPercentStart = x.Value);
            }
            QuantityPercentEnd = QuantityPercentStart + initialQtyPrcnt;
        }

        public TakeProfitViewModel(TradeTaskViewModel tt, OrderTask model, TakeProfitViewModel prev = null) : base(tt, model)
        {
            Previous = prev;
            if (Previous != null)
            {
                qtyPercentStart = Previous.QuantityPercentEnd;
            }
            qtyPercentEnd = qtyPercentStart + model.QuantityPercent;
        }

        public string Caption { get; set; }

        public double QuantityPercentStart
        {
            get { return qtyPercentStart; }
            set
            {
                if (Previous != null)
                {
                    this.RaiseAndSetIfChanged(ref qtyPercentStart, Math.Round(value, 2));
                    Previous.QuantityPercentEnd = qtyPercentStart;
                    if (Previous.QuantityPercentStart >= Previous.QuantityPercentEnd)
                        Previous.QuantityPercentStart = Previous.QuantityPercentEnd;
                    if (QuantityPercentStart >= QuantityPercentEnd)
                        QuantityPercentEnd = QuantityPercentStart;
                    this.RaiseAndSetIfChanged(ref Model.QuantityPercent, QuantityPercent, nameof(QuantityPercent));
                }
            }
        }

        public double QuantityPercentEnd
        {
            get { return qtyPercentEnd; }
            set
            {
                this.RaiseAndSetIfChanged(ref qtyPercentEnd, Math.Round(value, 2));
                //this.RaisePropertyChanged(nameof(QuantityPercent));
                this.RaiseAndSetIfChanged(ref Model.QuantityPercent, QuantityPercent, nameof(QuantityPercent));
            }
        }

        public double QuantityPercent => Math.Round(QuantityPercentEnd - QuantityPercentStart, 2);

        public double ProfitPercent => (double)(Price / TradeTask.Buy.Price) - 1.0;
        public double ProfitPercentRelative => ProfitPercent / TradeTask.ProfitPercent;

        public TakeProfitViewModel Previous { get; }
    }

    public class TradeTaskViewModel : ReactiveObject, ISupportsActivation
    {
        public OrderTaskViewModel Buy { get; }
        public OrderTaskViewModel StopLoss { get; }
        public ObservableCollection<TakeProfitViewModel> TakeProfit { get; }
        public IEnumerable<TakeProfitViewModel> TakeProfitCollection => TakeProfit.Where(x => x.QuantityPercent > 0.01);

        [Reactive] public decimal LastPrice { get; set; }
        [Reactive] public bool IsEnabled { get; set; }
        [Reactive] public string Status { get; set; }
        [Reactive] public ApiError LastError { get; set; }
        [ObservableAsProperty] public double Distance { get; }

        public SymbolInformation SymbolInformation { get; }
        public double LossPercent => (double)(StopLoss.Price / Buy.Price) - 1.0;
        public double ProfitPercent => TakeProfitCollection.Last().ProfitPercent;
        public decimal ProfitPrice => TakeProfitCollection.Last().Price;
        public decimal QuoteBalance { get; }
        public double QuoteBalancePercent { get => qtyBalancePercent; set => this.RaiseAndSetIfChanged(ref qtyBalancePercent, value); }
        public decimal Total { get => buyTotal; set => this.RaiseAndSetIfChanged(ref buyTotal, value); }
        public bool IsMarketBuy { get; set; }
        public bool IsLimitStop { get; set; } = true; // NOTE: Only if Exchange supports stop-limit?

        public decimal AvgBuyPrice => Model.AvgBuyPrice;
        public decimal AvgSellPrice => Model.AvgSellPrice;
        public decimal Qty => Model.Qty;
        public decimal TotalQuoteBuy => Model.TotalQuoteBuy;
        public decimal TotalQuoteSell => Model.TotalQuoteSell;
        public decimal Profit => Model.Profit;

        public ICommand AddTakeProfitCommand { get; }
        public ReactiveCommand<string, bool> SubmitCommand { get; }

        public Interaction<string, bool> Confirm { get; } = new Interaction<string, bool>();

        public ViewModelActivator Activator { get; } = new ViewModelActivator();

        public TradeTaskViewModel(SymbolInformation si, string exchangeName)
        {
            Model = new TradeTask();
            Model.Id = Guid.NewGuid();
            Model.Symbol = si.Symbol;
            Model.Exchange = exchangeName;
            SymbolInformation = si;
            QuoteBalance = si.QuoteAssetBalance.Free;
            QuoteBalancePercent = 0.05;
            Buy = new OrderTaskViewModel(this) { Price = si.PriceTicker.LastPrice.Value };
            StopLoss = new OrderTaskViewModel(this) { Price = si.ClampPrice(Buy.Price * 0.95m) };
            TakeProfit = new ObservableCollection<TakeProfitViewModel>();
            var DEF_TAKE_PRCNTS = new double[] { 0.2, 0.2, 0.2, 0.15, 0.15, 0.1 };
            for (int ind = 1; ind <= 6; ++ind)
            {
                var tp = new TakeProfitViewModel(this, DEF_TAKE_PRCNTS[ind-1], ind > 1 ? TakeProfit[ind - 2] : null) { Price = Math.Round(Buy.Price * (1.0m + 0.05m * ind), si.PriceDecimals), Caption = $"Тейк {ind}" };
                TakeProfit.Add(tp);
            }
            LastPrice = Buy.Price;
            AddTakeProfitCommand = ReactiveCommand.Create<object>(AddTakeProfitExecute);
            SubmitCommand = ReactiveCommand.CreateFromTask<string, bool>(SubmitImpl);
            this.WhenAnyValue(x => x.QuoteBalancePercent).Subscribe(y => CalcQuantity());
            ConnectTrades();
        }

        public TradeTaskViewModel(SymbolInformation si, TradeTask model)
        {
            SymbolInformation = si;
            Model = model;
            Buy = new OrderTaskViewModel(this, model.Buy);
            StopLoss = new OrderTaskViewModel(this, model.StopLoss);
            TakeProfit = new ObservableCollection<TakeProfitViewModel>();
            for (int ind = 1; ind <= model.TakeProfit.Length; ++ind)
            {
                var tp = new TakeProfitViewModel(this, model.TakeProfit[ind - 1], ind > 1 ? TakeProfit[ind - 2] : null);
                TakeProfit.Add(tp);
            }
            LastPrice = Buy.Price;
            ConnectTrades();
        }

        private void ConnectTrades()
        {
            this.WhenAnyValue(x => x.LastPrice).Select(x => (double)x / (double)Buy.Price - 1.0).ToPropertyEx(this, x => x.Distance);

            var connection = this.Model.TradesStream.Connect();
            connection.Subscribe(
                changeSet =>
                {
                    this.RaisePropertyChanged(nameof(AvgBuyPrice));
                    this.RaisePropertyChanged(nameof(AvgSellPrice));
                    this.RaisePropertyChanged(nameof(Qty));
                    this.RaisePropertyChanged(nameof(TotalQuoteBuy));
                    this.RaisePropertyChanged(nameof(TotalQuoteSell));
                    this.RaisePropertyChanged(nameof(Profit));
                });
            //connection.DisposeWith(disposables);

        }

        private async Task<bool> SubmitImpl(string param)
        {
            Buy.Model.Symbol = SymbolInformation.Symbol;
            Buy.Model.Side = TradeSide.Buy;
            Buy.Model.OrderKind = OrderKind.Buy;
            Buy.Model.OrderType = IsMarketBuy ? OrderType.MARKET : OrderType.LIMIT;

            StopLoss.Model.Symbol = SymbolInformation.Symbol;
            StopLoss.Model.Side = TradeSide.Sell;
            StopLoss.Model.OrderKind = OrderKind.StopLoss;
            StopLoss.Model.OrderType = IsLimitStop ? OrderType.STOP_LIMIT : OrderType.MARKET;
            StopLoss.Quantity = Buy.Quantity;

            Debug.Assert(Buy.Model.Quantity >= SymbolInformation.MinQuantity);
            Debug.Assert((Buy.Model.Price * Buy.Model.Quantity) >= SymbolInformation.MinNotional);
            Debug.Assert((StopLoss.Model.Price * StopLoss.Model.Quantity) >= SymbolInformation.MinNotional);

            foreach (var tp in TakeProfit)
            {
                tp.Model.Symbol = SymbolInformation.Symbol;
                tp.Model.Side = TradeSide.Sell;
                tp.Model.OrderKind = OrderKind.TakeProfit;
                tp.Model.OrderType = OrderType.MARKET;
                if (tp.QuantityPercent > 0.01)
                {
                    tp.Quantity = SymbolInformation.ClampQuantity(Buy.Quantity * (decimal)tp.QuantityPercent);
                    Debug.Assert(tp.Quantity >= SymbolInformation.MinQuantity);
                    Debug.Assert(tp.Total >= SymbolInformation.MinNotional);
                }
                else
                    tp.Quantity = 0m;
            }

            var tpQuantityTotal = TakeProfit.Sum(tp => tp.Quantity);
            var tpQuantityPercent = TakeProfit.Sum(tp => tp.QuantityPercent);
            if (tpQuantityPercent > 0.98)
                tpQuantityPercent = 1.00;
            var tpValidQuantity = SymbolInformation.ClampQuantity(Buy.Quantity * (decimal)tpQuantityPercent);

            var tpQuantityLeft = tpValidQuantity - tpQuantityTotal;
            while (tpQuantityLeft >= SymbolInformation.StepSize)
            {
                foreach (var tp in TakeProfit.Where(x => x.QuantityPercent >= 0.01))
                {
                    if (tpQuantityLeft >= SymbolInformation.StepSize)
                    {
                        tp.Quantity += SymbolInformation.StepSize;
                        tpQuantityLeft -= SymbolInformation.StepSize;
                    }
                }
                tpQuantityLeft = Buy.Quantity - TakeProfit.Sum(tp => tp.Quantity);
            }

            Debug.Assert(Buy.Quantity >= TakeProfit.Sum(tp => tp.Quantity));

            if (tpQuantityLeft > 0m)
            {
                //System.Windows.MessageBox.Show($"Остаток: {tpQuantityLeft} {SymbolInformation.BaseAsset}");
                var ok = await Confirm.Handle($"Остаток: {tpQuantityLeft} {SymbolInformation.BaseAsset}");
                if (!ok)
                    return false;
            }

            Model.Buy = Buy.Model;
            Model.StopLoss = StopLoss.Model;
            Model.TakeProfit = TakeProfitCollection.Select(x => x.Model).ToArray();

            Model.Jobs.Enqueue(Model.Buy);
            Model.Jobs.Enqueue(Model.StopLoss);
            TakeProfit.ToList().ForEach(tp => Model.Jobs.Enqueue(tp.Model));

            Model.Created = DateTime.Now;
            Model.Updated = DateTime.Now;

            SerializeModel(Model);

            return true;
        }

        private void AddTakeProfitExecute(object param)
        {
            int ind = TakeProfit.Count + 1;
            var tpQuantityPercent = TakeProfit.Sum(x => x.QuantityPercent);
            var tp = new TakeProfitViewModel(this, 1.00 - tpQuantityPercent, ind > 1 ? TakeProfit[ind - 2] : null) { Price = Math.Round(Buy.Price * (1.0m + 0.05m * ind), SymbolInformation.PriceDecimals), Caption = $"Тейк {ind}" };
            TakeProfit.Add(tp);
        }

        private void CalcQuantity()
        {
            Total = Math.Round(QuoteBalance * (decimal)QuoteBalancePercent, 8);
            Buy.Quantity = SymbolInformation.ClampQuantity(Total / Buy.Price);
        }

        public static TradeTask DeserializeModel(string json)
        {
            var model = JsonConvert.DeserializeObject<TradeTask>(json);
            return model;
        }

        public static void SerializeModel(TradeTask model)
        {
            var json = JsonConvert.SerializeObject(model, Formatting.Indented);
            File.WriteAllText(Path.ChangeExtension(model.Id.ToString(), ".json"), json);
        }

        private decimal buyTotal;
        private double qtyBalancePercent;
        public TradeTask Model { get; }
    }

    public class TradeModel
    {
        public decimal AvgBuyPrice => BuyQty > 0 ? TotalQuoteBuy / BuyQty : 0;
        public decimal AvgSellPrice => SellQty > 0 ? TotalQuoteSell / SellQty : 0;
        public decimal Qty => BuyQty - SellQty;
        public decimal TotalQuoteBuy => BuyTrades.Sum(x => x.Total);
        public decimal TotalQuoteSell => SellTrades.Sum(x => x.Total);
        public decimal Profit => TotalQuoteBuy > 0 ? TotalQuoteSell / TotalQuoteBuy : 0;

        public IObservableList<ExchangeTrade> TradesStream => Trades.AsObservableList();

        protected SourceList<ExchangeTrade> Trades { get; }
        protected IEnumerable<ExchangeTrade> BuyTrades => Trades.Items.Where(x => x.Side == TradeSide.Buy);
        protected IEnumerable<ExchangeTrade> SellTrades => Trades.Items.Where(x => x.Side == TradeSide.Sell);

        public decimal BuyQty => BuyTrades.Sum(x => x.Qty);
        public decimal SellQty => SellTrades.Sum(x => x.Qty);

        public TradeModel(IEnumerable<ExchangeTrade> exchangeTrades = null)
        {
            Trades = new SourceList<ExchangeTrade>();
            tradeObservable = Trades.Connect();
            if (exchangeTrades != null)
                Trades.Edit(innerList => innerList.AddRange(exchangeTrades));
        }

        public void RegisterTrade(ExchangeTrade trade)
        {
            Trades.Add(trade);
        }

        IObservable<IChangeSet<ExchangeTrade>> tradeObservable;
    }

    public class ExchangeTrade
    {
        public TradeSide Side { get; }
        public decimal Price { get; }
        public decimal Qty { get; }
        public decimal Total => Price * Qty;
        public ExchangeTrade(TradeSide side, decimal rate, decimal amount)
        {
            Side = side;
            Price = rate;
            Qty = amount;
        }
    }

    // TODO
    // Have own disposable object and register it on activation.
    // Register all own and model disposals in it.
    public class TradeViewModel : ReactiveObject
    {
        public decimal AvgBuyPrice => Model.AvgBuyPrice;
        public decimal AvgSellPrice => Model.AvgSellPrice;
        public decimal Qty => Model.Qty;
        public decimal TotalQuoteBuy => Model.TotalQuoteBuy;
        public decimal TotalQuoteSell => Model.TotalQuoteSell;
        public decimal Profit => Model.Profit;

        public TradeViewModel(TradeModel model)
        {
            this.Model = model;
            var connection = this.Model.TradesStream.Connect();
            connection.Subscribe(
                changeSet =>
                {
                    this.RaisePropertyChanged(nameof(AvgBuyPrice));
                    this.RaisePropertyChanged(nameof(AvgSellPrice));
                    this.RaisePropertyChanged(nameof(Qty));
                    this.RaisePropertyChanged(nameof(TotalQuoteBuy));
                    this.RaisePropertyChanged(nameof(TotalQuoteSell));
                    this.RaisePropertyChanged(nameof(Profit));
                });
            //connection.DisposeWith(disposables);
        }

        protected TradeModel Model { get; }
    }
}
