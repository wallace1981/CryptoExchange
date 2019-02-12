using Exchange.Net;
using Newtonsoft.Json;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Terminal.WPF
{
    public enum OrderType
    {
        LIMIT,      // could be STOP-LIMIT as well, if supported by exchange.
        MARKET,
        TRAILING    // market order with moving target ;)
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class OrderTaskModel
    {
        [JsonProperty("ORDER_TYPE")]
        public OrderType OrderType;

        [JsonProperty("PRICE")]
        public decimal Price;

        [JsonProperty("QTY")]
        public decimal Quantity;

        [JsonProperty("QTY_PRCNT", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public double QuantityPercent;

        [JsonProperty("TRAILING_PRCNT", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public double TrailingPercent;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class TradeTaskModel
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
        public OrderTaskModel Buy;

        [JsonProperty("SL")]
        public OrderTaskModel StopLoss;

        [JsonProperty("TP")]
        public OrderTaskModel[] TakeProfit;
    }

    public class OrderTaskViewModel : ReactiveObject
    {
        public OrderTaskModel Model { get; }

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
            Model = new OrderTaskModel();
            TradeTask = tt;
            this.WhenAnyValue(x => x.Price, y => y.Quantity).Subscribe(z => this.RaisePropertyChanged(nameof(Total)));
        }

        public OrderTaskViewModel(TradeTaskViewModel tt, OrderTaskModel model)
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

        public TakeProfitViewModel(TradeTaskViewModel tt, OrderTaskModel model, TakeProfitViewModel prev = null) : base(tt, model)
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

        public TakeProfitViewModel Previous { get; }
    }

    public class TradeTaskViewModel : ReactiveObject, ISupportsActivation
    {
        public OrderTaskViewModel Buy { get; }
        public OrderTaskViewModel StopLoss { get; }
        public ReactiveList<TakeProfitViewModel> TakeProfit { get; }
        public IEnumerable<TakeProfitViewModel> TakeProfitCollection => TakeProfit.Where(x => x.QuantityPercent > 0.01);

        public string Status { get; }
        public ApiError LastError { get; }
        public SymbolInformation SymbolInformation { get; }
        public double LossPercent => (double)(StopLoss.Price / Buy.Price) - 1.0;
        public double ProfitPercent => TakeProfitCollection.Last().ProfitPercent;
        public decimal QuoteBalance { get; }
        public double QuoteBalancePercent { get => qtyBalancePercent; set => this.RaiseAndSetIfChanged(ref qtyBalancePercent, value); }
        public decimal Total { get => buyTotal; set => this.RaiseAndSetIfChanged(ref buyTotal, value); }
        public bool IsMarketBuy { get; set; }
        public bool IsLimitStop { get; set; } = true; // NOTE: Only if Exchange supports stop-limit?

        public ICommand AddTakeProfitCommand { get; }
        public ICommand SubmitCommand { get; }

        public Interaction<string, bool> Confirm { get; } = new Interaction<string, bool>();

        public ViewModelActivator Activator => viewModelActivator;
        readonly ViewModelActivator viewModelActivator = new ViewModelActivator();

        public TradeTaskViewModel(SymbolInformation si, string exchangeName)
        {
            model = new TradeTaskModel();
            model.Id = Guid.NewGuid();
            model.Symbol = si.Symbol;
            model.Exchange = exchangeName;
            SymbolInformation = si;
            QuoteBalance = si.QuoteAssetBalance.Free;
            QuoteBalancePercent = 0.05;
            Buy = new OrderTaskViewModel(this) { Price = si.PriceTicker.LastPrice.Value };
            StopLoss = new OrderTaskViewModel(this) { Price = si.ClampPrice(Buy.Price * 0.95m) };
            TakeProfit = new ReactiveList<TakeProfitViewModel>();
            var DEF_TAKE_PRCNTS = new double[] { 0.2, 0.2, 0.2, 0.15, 0.15, 0.1 };
            for (int ind = 1; ind <= 6; ++ind)
            {
                var tp = new TakeProfitViewModel(this, DEF_TAKE_PRCNTS[ind-1], ind > 1 ? TakeProfit[ind - 2] : null) { Price = Math.Round(Buy.Price * (1.0m + 0.05m * ind), si.PriceDecimals), Caption = $"Тейк {ind}" };
                TakeProfit.Add(tp);
            }
            AddTakeProfitCommand = ReactiveCommand.Create<object>(AddTakeProfitExecute);
            SubmitCommand = ReactiveCommand.CreateFromTask<string>(SubmitImpl);
            this.WhenAnyValue(x => x.QuoteBalancePercent).Subscribe(y => CalcQuantity());
        }

        public TradeTaskViewModel(SymbolInformation si, TradeTaskModel model)
        {
            SymbolInformation = si;
            Buy = new OrderTaskViewModel(this, model.Buy);
            StopLoss = new OrderTaskViewModel(this, model.StopLoss);
            TakeProfit = new ReactiveList<TakeProfitViewModel>();
            for (int ind = 1; ind <= model.TakeProfit.Length; ++ind)
            {
                var tp = new TakeProfitViewModel(this, model.TakeProfit[ind - 1], ind > 1 ? TakeProfit[ind - 2] : null);
                TakeProfit.Add(tp);
            }
        }

        private async Task SubmitImpl(string param)
        {
            Buy.Model.OrderType = IsMarketBuy ? OrderType.MARKET : OrderType.LIMIT;
            StopLoss.Model.OrderType = IsLimitStop ? OrderType.LIMIT : OrderType.MARKET;
            StopLoss.Quantity = Buy.Quantity;

            foreach (var tp in TakeProfit)
            {
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

            if (tpQuantityLeft >= 0m)
            {
                //System.Windows.MessageBox.Show($"Остаток: {tpQuantityLeft} {SymbolInformation.BaseAsset}");
                var ok = await Confirm.Handle($"Остаток: {tpQuantityLeft} {SymbolInformation.BaseAsset}");
            }

            model.Buy = Buy.Model;
            model.StopLoss = StopLoss.Model;
            model.TakeProfit = TakeProfitCollection.Select(x => x.Model).ToArray();

            var json = JsonConvert.SerializeObject(model);
            File.WriteAllText(Path.ChangeExtension(model.Id.ToString(), ".json"), json);
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

        public static TradeTaskModel DeserializeModel(string json)
        {
            var model = JsonConvert.DeserializeObject<TradeTaskModel>(json);
            return model;
        }

        private decimal buyTotal;
        private double qtyBalancePercent;
        private TradeTaskModel model;
    }

}
