using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Exchange.Net;
using Gtk;

namespace CryptoExchange
{
    [System.ComponentModel.ToolboxItem(true)]
    public partial class OrderBook : Gtk.Bin
    {
        Gdk.Color bearColor = new Gdk.Color(226, 101, 101);
        Gdk.Color bullColor = new Gdk.Color(82, 204, 84);

        private decimal factor = decimal.MinusOne;

        public OrderBook()
        {
            this.Build();
            BuildOrderBookView();
        }

        private TreeViewColumn colPrice, colQty, colTotal;

        private void BuildOrderBookView()
        {
            colPrice = CreateColumn(nodeview1, "Price", new CellRendererText(), RenderTradePrice, -1);
            colQty = CreateColumn(nodeview1, "Amount", new CellRendererText() { Xalign = (float)1.0 }, RenderTradeQuantity, -1);
            colTotal = CreateColumn(nodeview1, "Total", new CellRendererText() { Xalign = (float)1.0 }, RenderTradeTotal, -1);
            colQty.Alignment = (float)1.0;
            colTotal.Alignment = (float)1.0;
        }

        static TreeViewColumn CreateColumn(TreeView view, string title, CellRenderer cell, TreeCellDataFunc dataFunc, int sortOrderId)
        {
            var col = view.AppendColumn(title, cell, dataFunc);
            col.Sizing = TreeViewColumnSizing.GrowOnly;
            return col;
        }

        private void RenderTradeQuantity(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var entry = model.GetValue(iter, 0) as OrderBookEntry;
            if (entry != null)
            {
                var market = viewModel.GetSymbolInformation(viewModel.CurrentSymbol);
                (cell as CellRendererText).Text = entry.Quantity.ToString(market.QuantityFmt);
            }
        }

        private void RenderTradePrice(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var entry = model.GetValue(iter, 0) as OrderBookEntry;
            if (entry != null)
            {
                var market = viewModel.GetSymbolInformation(viewModel.CurrentSymbol);
                if (entry.Side == TradeSide.Buy)
                {
                    (cell as CellRendererText).ForegroundGdk = bullColor;
                }
                else
                {
                    (cell as CellRendererText).ForegroundGdk = bearColor;
                }
                (cell as CellRendererText).Text = entry.Price.ToString(market.PriceFmt);
            }
        }

        private void RenderTradeTotal(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var entry = model.GetValue(iter, 0) as OrderBookEntry;
            if (entry != null)
            {
                var market = viewModel.GetSymbolInformation(viewModel.CurrentSymbol);
                (cell as CellRendererText).Text = entry.Total.ToString(market.PriceFmt);
            }
        }
    
        ExchangeViewModel viewModel { get; set; }
        ListStore store = new ListStore(typeof(PublicTrade));

        public void Initialize(ExchangeViewModel vm)
        {
            viewModel = vm;
            nodeview1.Model = store;
            viewModel.OrderBook.CollectionChanged += OrderBook_CollectionChanged;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateEntriesCountUI(viewModel.OrderBookMaxItemCount);
        }

        void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(viewModel.CurrentSymbolInformation):
                    colPrice.Title = $"Price({viewModel.CurrentSymbolInformation.QuoteAsset})";
                    colQty.Title = $"Amount({viewModel.CurrentSymbolInformation.BaseAsset})";
                    colTotal.Title = $"Total({viewModel.CurrentSymbolInformation.QuoteAsset})";
                    factor = CalcFactor(viewModel.CurrentSymbolInformation.PriceDecimals);
                    UpdateFactorUI(viewModel.CurrentSymbolInformation.PriceDecimals);
                    break;
            }
        }

        private void UpdateFactorUI(int priceDecimals)
        {
            TreeIter iter;
            var result = combobox1.Model.GetIterFirst(out iter);
            while (result)
            {
                var value = combobox1.Model.GetValue(iter, 0) as string;
                if (int.Parse(value) == priceDecimals)
                {
                    combobox1.SetActiveIter(iter);
                    return;
                }
                result = combobox1.Model.IterNext(ref iter);
            }
        }

        private void UpdateEntriesCountUI(int count)
        {
            TreeIter iter;
            var result = combobox2.Model.GetIterFirst(out iter);
            while (result)
            {
                var value = combobox2.Model.GetValue(iter, 0) as string;
                if (int.Parse(value) == count)
                {
                    combobox2.SetActiveIter(iter);
                    return;
                }
                result = combobox2.Model.IterNext(ref iter);
            }
        }

        private void OrderBook_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var coll = sender as ICollection<OrderBookEntry>;
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Debug.Print($"OrderBook: Added {e.NewItems.Count} items.");
                    break;
                case NotifyCollectionChangedAction.Move:
                    break;
                case NotifyCollectionChangedAction.Remove:
                    Debug.Print($"OrderBook: Removed {e.OldItems.Count} items.");
                    break;
                case NotifyCollectionChangedAction.Replace:
                    break;
                case NotifyCollectionChangedAction.Reset:
                    Debug.Print($"OrderBook: Reset. {coll.Count} items.");
                    Gtk.Application.Invoke(delegate
                    {
                        store.Clear();
                        foreach (var item in AggregateOrderBook(coll))
                        {
                            store.AppendValues(item);
                        }
                    });
                    break;
            }
        }

        private IEnumerable<OrderBookEntry> AggregateOrderBook(ICollection<OrderBookEntry> orderBook)
        {
            TradeSide side = TradeSide.Sell;
            decimal priceLevel = decimal.Zero;
            decimal volumeAggregated = decimal.Zero;
            var result = new List<OrderBookEntry>();

            foreach (var entry in orderBook)
            {
                if (priceLevel == decimal.Zero)
                {
                    side = entry.Side;
                    priceLevel = MergePrice(entry.Price, factor, entry.Side);
                    volumeAggregated = entry.Quantity;
                }
                else if (side == entry.Side && priceLevel == MergePrice(entry.Price, factor, entry.Side))
                {
                    volumeAggregated += entry.Quantity;
                }
                else
                {
                    result.Add(new OrderBookEntry() { Price = priceLevel, Quantity = volumeAggregated, Side = side });
                    priceLevel = MergePrice(entry.Price, factor, entry.Side);
                    volumeAggregated = entry.Quantity;
                    side = entry.Side;
                }
            }

            return result;
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

        private static decimal MergePrice2(decimal price, decimal factor, TradeSide side)
        {
            if (side == TradeSide.Buy)
            {
                return Math.Truncate(price / factor) * factor;
            }
            else
            {
                return Math.Truncate((price + (factor-0.000000001m)) / factor) * factor;
            }
        }

        protected void OnCombobox2Changed(object sender, EventArgs e)
        {
            viewModel.OrderBookMaxItemCount = int.Parse(combobox2.ActiveText);
        }

        protected void OnCombobox1Changed(object sender, EventArgs e)
        {
            factor = CalcFactor(int.Parse(combobox1.ActiveText));
        }

        private decimal CalcFactor(int num)
        {
            decimal result = 1m;
            if (num == 10)
                return 0.1m;
            else for (int idx = 1; idx <= num; idx += 1)
                result = result * 10m;
            return result;
        }
    }
}
