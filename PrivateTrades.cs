using System;
using Gtk;
using Exchange.Net;
using System.Collections.Specialized;
using System.Collections.Generic;

namespace CryptoExchange
{
    [System.ComponentModel.ToolboxItem(true)]
    public partial class PrivateTrades : Gtk.Bin
    {
        Gdk.Color bearColor = new Gdk.Color(226, 101, 101);
        Gdk.Color bullColor = new Gdk.Color(82, 204, 84);

        ExchangeViewModel viewModel { get; set; }
        ListStore store = new ListStore(typeof(Balance));
        //TreeModelFilter filter;

        public PrivateTrades()
        {
            this.Build();
            BuildTradesView();
        }

        public void Initialize(ExchangeViewModel vm)
        {
            viewModel = vm;
            nodeview1.Model = store;
            viewModel.OpenOrders.CollectionChanged += OpenOrders_CollectionChanged;
        }

        private void OpenOrders_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var coll = sender as ICollection<Order>;
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    break;
                case NotifyCollectionChangedAction.Move:
                    break;
                case NotifyCollectionChangedAction.Remove:
                    break;
                case NotifyCollectionChangedAction.Replace:
                    break;
                case NotifyCollectionChangedAction.Reset:
                    Gtk.Application.Invoke(delegate
                    {
                        store.Clear();
                        foreach (var item in coll)
                        {
                            store.AppendValues(item);
                        }
                    });
                    break;
            }
        }
    
        private void BuildTradesView()
        {
            var colTime = CreateColumn(nodeview1, "Time", new CellRendererText(), RenderTradesTime, 0);
            var colSymbol = CreateColumn(nodeview1, "Symbol", new CellRendererText(), RenderTradesSymbol, 1);
            var colSide = CreateColumn(nodeview1, "Side", new CellRendererText(), RenderTradesSide, 2);
            var colPrice = CreateColumn(nodeview1, "Price", new CellRendererText(), RenderTradesPrice, 3);
            var colQty = CreateColumn(nodeview1, "Amount", new CellRendererText() { Xalign = (float)1.0 }, RenderTradesQty, 4);
            //var colStopPrice = CreateColumn(nodeview1, "Stop", new CellRendererText() { Xalign = (float)1.0 }, RenderFundsAvailable, 5);
            var colTotal = CreateColumn(nodeview1, "Total", new CellRendererText() { Xalign = (float)1.0 }, RenderTradesTotal, 6);
            colQty.Alignment = (float)1.0;
            colTotal.Alignment = (float)1.0;
        }

        static TreeViewColumn CreateColumn(TreeView view, string title, CellRenderer cell, TreeCellDataFunc dataFunc, int sortOrderId)
        {
            var col = view.AppendColumn(title, cell, dataFunc);
            col.Sizing = TreeViewColumnSizing.GrowOnly;
            return col;
        }

        private void RenderTradesTime(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var order = model.GetValue(iter, 0) as Order;
            if (order != null)
            {
                (cell as CellRendererText).Text = order.Timestamp.TimestampToString();
            }
        }

        private void RenderTradesSymbol(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var order = model.GetValue(iter, 0) as Order;
            if (order != null)
            {
                var market = viewModel.GetSymbolInformation(order.Symbol);
                (cell as CellRendererText).Text = market.BaseAsset + "/" + market.QuoteAsset;
            }
        }

        private void RenderTradesSide(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var order = model.GetValue(iter, 0) as Order;
            if (order != null)
            {
                if (order.Side == TradeSide.Buy)
                {
                    (cell as CellRendererText).ForegroundGdk = bullColor;
                }
                else
                {
                    (cell as CellRendererText).ForegroundGdk = bearColor;
                }
                (cell as CellRendererText).Text = order.Side.ToString();
            }
        }

        private void RenderTradesPrice(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var order = model.GetValue(iter, 0) as Order;
            if (order != null)
            {
                var market = viewModel.GetSymbolInformation(order.Symbol);
                (cell as CellRendererText).Text = order.Price.ToString(market.PriceFmt);
            }
        }

        private void RenderTradesQty(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var order = model.GetValue(iter, 0) as Order;
            if (order != null)
            {
                var market = viewModel.GetSymbolInformation(order.Symbol);
                (cell as CellRendererText).Text = order.Quantity.ToString(market.QuantityFmt);
            }
        }

        private void RenderTradesTotal(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var order = model.GetValue(iter, 0) as Order;
            if (order != null)
            {
                var market = viewModel.GetSymbolInformation(order.Symbol);
                (cell as CellRendererText).Text = order.Total.ToString(market.PriceFmt) + " " + market.QuoteAsset;
            }
        }
    }
}
