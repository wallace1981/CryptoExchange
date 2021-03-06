﻿using DynamicData;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Exchange.Net
{

    //
    //  BUY Cond:   SL > LastPrice < TP1
    //  TP  Cond:   Bid >= TP && Bid Qty >= TP Qty
    //

    public enum TradeTaskStatus
    {
        Running,
        Finished,
        Stopped,
        PanicSell
    }

    public enum OrderKind
    {
        Buy,
        StopLoss,
        TakeProfit,
        StopLossOrTakeProfit,
        PanicSell
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
        [JsonConverter(typeof(StringEnumConverter))]
        public TradeSide Side;

        [JsonProperty("SYMBOL")]
        public string Symbol;

        [JsonProperty("ORDER_TYPE")]
        [JsonConverter(typeof(StringEnumConverter))]
        public OrderType Type;

        [JsonProperty("ORDER_KIND")]
        [JsonConverter(typeof(StringEnumConverter))]
        public OrderKind Kind;

        [JsonProperty("PRICE")]
        public decimal Price;

        [JsonProperty("QTY")]
        public decimal Quantity;

        [JsonProperty("QTY_PRCNT", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public double QuantityPercent;

        [JsonProperty("TRAILING_PRCNT", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public double TrailingPercent;

        [JsonProperty("ORDERID", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string OrderId;

        public bool IsCompleted { get; }

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

        [JsonProperty("STATUS")]
        public TradeTaskStatus Status;

        [JsonProperty("QUEUE")]
        public List<OrderTask> Jobs { get; set; }

        [JsonProperty("PROCESSED")]
        public Queue<OrderTask> FinishedJobs { get; set; }

        [JsonProperty("EVENTS")]
        public IDictionary<DateTime, string> Events { get; set; }

        public IEnumerable<OrderTask> ActiveJobs => Jobs.Where(x => !string.IsNullOrWhiteSpace(x.OrderId));

        public OrderTask Buy => AllJobs.FirstOrDefault(x => x.Kind == OrderKind.Buy);
        public OrderTask StopLoss => AllJobs.FirstOrDefault(x => x.Kind == OrderKind.StopLoss);
        public IList<OrderTask> TakeProfit => AllJobs.Where(x => x.Kind == OrderKind.TakeProfit).ToList();

        public DateTime LastGetOrder = DateTime.MinValue;
        public SemaphoreSlim locker = new SemaphoreSlim(1, 1);
        public bool IsBusy => locker.CurrentCount == 0;

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

        protected IEnumerable<OrderTask> AllJobs => Jobs.Concat(FinishedJobs);

        public TradeTask(IEnumerable<OrderTrade> exchangeTrades = null)
        {
            Trades = new SourceCache<OrderTrade, string>(x => x.Id);
            Events = new Dictionary<DateTime, string>();
            if (exchangeTrades != null)
                Trades.Edit(innerList => innerList.AddOrUpdate(exchangeTrades));
            Jobs = new List<OrderTask>();
            FinishedJobs = new Queue<OrderTask>();
            Events = new Dictionary<DateTime, string>();
        }

        public void RegisterTrade(OrderTrade trade)
        {
            Trades.AddOrUpdate(trade);
        }

        public TradeTask LoadFromJson(string json)
        {
            var tt = new TradeTask();
            JsonConvert.PopulateObject(json, tt);
            //Trades.Edit(innerList => innerList.AddOrUpdate(Buy.ExchangeOrder.Fills));
            //Trades.Edit(innerList => innerList.AddOrUpdate(StopLoss.ExchangeOrder.Fills));
            //foreach (var ot in TakeProfit)
            //    Trades.Edit(innerList => innerList.AddOrUpdate(ot.ExchangeOrder.Fills));
            return tt;
        }
    }

    public class OrderTaskViewModel : ReactiveObject
    {
        public OrderTask Model { get; }

        [Reactive] public decimal Price { get; set; }
        [Reactive] public decimal Quantity { get; set; }
        [ObservableAsProperty] public decimal Total { get; }

        public OrderTaskViewModel(TradeTaskViewModel tt)
        {
            Model = new OrderTask();
            TradeTask = tt;
            // TODO: OBPH
            this.WhenAnyValue(x => x.Price, x => x.Quantity, (rate, qty) => rate * qty)
                .ToPropertyEx(this, vm => vm.Total);
            this.WhenAnyValue(x => x.Price)
                .Subscribe(x => Model.Price = x);
            this.WhenAnyValue(x => x.Quantity)
                .Subscribe(x => Model.Quantity = x);
        }

        public OrderTaskViewModel(TradeTaskViewModel tt, OrderTask model)
        {
            Model = model;
            TradeTask = tt;
            Price = model.Price;
            Quantity = model.Quantity;
            this.WhenAnyValue(x => x.Price, x => x.Quantity, (rate, qty) => rate * qty)
                .ToPropertyEx(this, vm => vm.Total);
            this.WhenAnyValue(x => x.Price)
                .Subscribe(x => Model.Price = x);
            this.WhenAnyValue(x => x.Quantity)
                .Subscribe(x => Model.Quantity = x);
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
            ConnectProperties();
        }

        public TakeProfitViewModel(TradeTaskViewModel tt, OrderTask model, TakeProfitViewModel prev = null) : base(tt, model)
        {
            Previous = prev;
            if (Previous != null)
            {
                qtyPercentStart = Previous.QuantityPercentEnd;
            }
            qtyPercentEnd = qtyPercentStart + model.QuantityPercent;
            ConnectProperties();
        }

        public string Caption { get; set; }

        public double QuantityPercentStart
        {
            get { return qtyPercentStart; }
            set
            {   // UGLY SHIT
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
            {   // UGLY SHIT
                this.RaiseAndSetIfChanged(ref qtyPercentEnd, Math.Round(value, 2));
                //this.RaisePropertyChanged(nameof(QuantityPercent));
                this.RaiseAndSetIfChanged(ref Model.QuantityPercent, QuantityPercent, nameof(QuantityPercent));
            }
        }

        public double QuantityPercent => Math.Round(QuantityPercentEnd - QuantityPercentStart, 2);

        [ObservableAsProperty] public double ProfitPercent { get; }
        public double ProfitPercentRelative => ProfitPercent / TradeTask.ProfitPercent;

        public TakeProfitViewModel Previous { get; }

        private void ConnectProperties()
        {
            this.WhenAnyValue(x => x.Price, x => x.TradeTask.Buy.Price, (profit, buy) => (double)(profit / buy) - 1.0).DistinctUntilChanged().ToPropertyEx(this, x => x.ProfitPercent);
        }
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
        [ObservableAsProperty] public decimal LastPriceLoss { get; }
        [ObservableAsProperty] public decimal LastPriceProfit { get; }

        public SymbolInformation SymbolInformation { get; }
        [ObservableAsProperty] public double LossPercent { get; }
        public double ProfitPercent => TakeProfitCollection.Last().ProfitPercent;
        public decimal ProfitPrice => TakeProfitCollection.Last().Price;
        public decimal QuoteBalance { get; }
        [Reactive] public decimal Total { get; set; }
        [Reactive] public bool IsMarketBuy { get; set; }
        [Reactive] public bool IsLimitStop { get; set; } = true; // NOTE: Only if Exchange supports stop-limit?
        [Reactive] public double QuoteBalancePercent { get; set; }
        [Reactive] public double TrailingPercent { get; set; } = 0.01;

        public decimal AvgBuyPrice => Model.AvgBuyPrice;
        public decimal AvgSellPrice => Model.AvgSellPrice;
        public decimal Qty => Model.Qty;
        public decimal TotalQuoteBuy => Model.TotalQuoteBuy;
        public decimal TotalQuoteSell => Model.TotalQuoteSell;
        public decimal Profit => Model.TotalQuoteBuy > 0 ? (TotalQuoteSell + Qty * LastPrice) / TotalQuoteBuy - 1 : 0;

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
            Buy = new OrderTaskViewModel(this) { Price = si.PriceTicker.Ask.GetValueOrDefault() };
            StopLoss = new OrderTaskViewModel(this) { Price = si.ClampPrice(Buy.Price * 0.95m) };
            TakeProfit = new ObservableCollection<TakeProfitViewModel>();
            var DEF_TAKE_PRCNTS = new double[] { 0.6, 0.4 };
            for (int ind = 1; ind <= 2; ++ind)
            {
                var tp = new TakeProfitViewModel(this, DEF_TAKE_PRCNTS[ind-1], ind > 1 ? TakeProfit[ind - 2] : null) { Price = Math.Round(Buy.Price * (1.05m + 0.05m * ind), si.PriceDecimals), Caption = $"Тейк {ind}" };
                TakeProfit.Add(tp);
            }
            LastPrice = Buy.Price;
            AddTakeProfitCommand = ReactiveCommand.Create<object>(AddTakeProfitExecute);
            SubmitCommand = ReactiveCommand.CreateFromTask<string, bool>(SubmitImpl);
            ConnectTrades(edit: true);
        }

        public TradeTaskViewModel(SymbolInformation si, TradeTask model)
        {
            SymbolInformation = si;
            Model = model;
            Buy = new OrderTaskViewModel(this, model.Buy);
            StopLoss = new OrderTaskViewModel(this, model.StopLoss);
            TakeProfit = new ObservableCollection<TakeProfitViewModel>();
            for (int ind = 1; ind <= model.TakeProfit.Count(); ++ind)
            {
                var tp = new TakeProfitViewModel(this, model.TakeProfit[ind - 1], ind > 1 ? TakeProfit[ind - 2] : null);
                TakeProfit.Add(tp);
            }
            LastPrice = si.PriceTicker.LastPrice.GetValueOrDefault();
            ConnectTrades();
        }

        private void ConnectTrades(bool edit = false)
        {
            this.WhenAnyValue(x => x.LastPrice, x => (double)x / (double)Buy.Price - 1.0).ToPropertyEx(this, x => x.Distance);
            this.WhenAnyValue(x => x.StopLoss.Price, x => x.Buy.Price, (sl, buy) => (double)(sl / buy) - 1.0).DistinctUntilChanged().ToPropertyEx(this, x => x.LossPercent);
            if (edit)
            {
                this.WhenAnyValue(x => x.QuoteBalancePercent).DistinctUntilChanged().Subscribe(y => CalcQuantity());
                this.WhenAnyValue(x => x.Buy.Quantity, x => x.Buy.Price).DistinctUntilChanged().Subscribe(x => CalcBalancePercent());
            }

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
            this.WhenAnyValue(vm => vm.LastPrice)
                .Subscribe(x => this.RaisePropertyChanged(nameof(Profit)));
            this.WhenAnyValue(x => x.LastPrice).Select(x => Math.Max(Math.Min(Buy.Price, x), StopLoss.Price)).ToPropertyEx(this, x => x.LastPriceLoss);
            this.WhenAnyValue(x => x.LastPrice).Select(x => Math.Min(ProfitPrice * 0.99m, Math.Max(x, Buy.Price))).ToPropertyEx(this, x => x.LastPriceProfit);
            //.DisposeWith(disposables);
        }

        private async Task<bool> SubmitImpl(string param)
        {
            Buy.Model.Symbol = SymbolInformation.Symbol;
            Buy.Model.Side = TradeSide.Buy;
            Buy.Model.Kind = OrderKind.Buy;
            Buy.Model.Type = IsMarketBuy ? OrderType.MARKET : OrderType.LIMIT;

            StopLoss.Model.Symbol = SymbolInformation.Symbol;
            StopLoss.Model.Side = TradeSide.Sell;
            StopLoss.Model.Kind = OrderKind.StopLoss;
            StopLoss.Model.Type = IsLimitStop ? OrderType.STOP_LIMIT : OrderType.MARKET;
            StopLoss.Quantity = Buy.Quantity;

            Debug.Assert(Buy.Model.Quantity >= SymbolInformation.MinQuantity);
            Debug.Assert((Buy.Model.Price * Buy.Model.Quantity) >= SymbolInformation.MinNotional);
            Debug.Assert((StopLoss.Model.Price * StopLoss.Model.Quantity) >= SymbolInformation.MinNotional);

            foreach (var tp in TakeProfit)
            {
                tp.Model.Symbol = SymbolInformation.Symbol;
                tp.Model.Side = TradeSide.Sell;
                tp.Model.Kind = OrderKind.TakeProfit;
                tp.Model.Type = OrderType.MARKET;
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

            Model.Jobs.Add(Buy.Model);
            foreach (var tp in TakeProfitCollection)
            {
                // NOTE: if SL is TRAILING by STEPS,
                // then create new object with corresponding price
                // for every TP.
                Model.Jobs.Add(StopLoss.Model);
                Model.Jobs.Add(tp.Model);
            }

            Model.Created = DateTime.Now;
            Model.Updated = DateTime.Now;
            Model.Events.Add(Model.Created, "Task created.");

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

        private void CalcBalancePercent()
        {
            Total = Buy.Price * Buy.Quantity;
            if (Total > QuoteBalance)
            {
                Total = QuoteBalance;
            }
            QuoteBalancePercent = QuoteBalance > 0 ? (double)(Total / QuoteBalance) : 0;
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

        public static void Delete(TradeTask model)
        {
            File.Delete(Path.ChangeExtension(model.Id.ToString(), ".json"));
        }

        private decimal buyTotal;
        public TradeTask Model { get; }
    }



    public class NewOrder : OrderBookEntry
    {
        public string OrderType { get; set; }
        public decimal Balance => Side == TradeSide.Buy ? SymbolInformation.QuoteAssetBalance.Free : SymbolInformation.BaseAssetBalance.Free;
        [Reactive] public double BalancePercent { get; set; }
        public string TotalAsset => (Side == TradeSide.Buy) ? SymbolInformation.QuoteAsset : SymbolInformation.BaseAsset;

        public NewOrder(SymbolInformation si, OrderBookEntry entry = null) : base(si)
        {
            if (entry != null)
            {
                Price = entry.Price;
                Quantity = entry.Quantity;
                Side = (entry.Side == TradeSide.Buy) ? TradeSide.Sell : TradeSide.Buy;
            }
            else
            {
                Price = si.PriceTicker.LastPrice.GetValueOrDefault();
            }
            this.WhenAnyValue(x => x.Side).Subscribe(OrderSideDependantActions);
            this.WhenAnyValue(x => x.BalancePercent).Subscribe(y => CalcQuantity());
            BalancePercent = 0.05;
        }

        private void CalcQuantity()
        {
            var total = Math.Round(Balance * (decimal)BalancePercent, 8);
            Quantity = SymbolInformation.ClampQuantity(total / Price);
            this.RaisePropertyChanged(nameof(Total));
        }

        private void OrderSideDependantActions(TradeSide side)
        {
            this.RaisePropertyChanged(nameof(Balance));
            this.RaisePropertyChanged(nameof(TotalAsset));
        }
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
