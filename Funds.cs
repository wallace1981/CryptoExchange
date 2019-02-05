using System;
using Gtk;
using Exchange.Net;
using System.Collections.Specialized;
using System.Collections.Generic;

namespace CryptoExchange
{
    [System.ComponentModel.ToolboxItem(true)]
    public partial class Funds : Gtk.Bin
    {
        ExchangeViewModel viewModel { get; set; }
        ListStore store = new ListStore(typeof(Balance));
        TreeModelFilter filter;
        TreeViewColumn colTotalBtc, colTotalUsd;

        public Funds()
        {
            this.Build();
            BuildFundsView();
        }

        public void Initialize(ExchangeViewModel vm)
        {
            viewModel = vm;
            filter = new TreeModelFilter(store, null) { VisibleFunc = FilterSmallAssets };
            nodeview1.Model = filter;
            viewModel.BalanceManager.Balances.CollectionChanged += Balances_CollectionChanged;
            GLib.Timeout.Add(2000, () => 
            {
                TreeIter iter;
                var result = store.GetIterFirst(out iter);
                while (result)
                {
                    var value = store.GetValue(iter, 0);
                    store.SetValue(iter, 0, value);
                    result = store.IterNext(ref iter);
                }
                if (viewModel.BalanceManager.TotalBtc > decimal.Zero)
                {
                    colTotalBtc.Title = $"BTC ({viewModel.BalanceManager.TotalBtc.ToString("N3")})";
                    colTotalUsd.Title = $"USD ({viewModel.BalanceManager.TotalUsd.ToString("N0")})";
                }
                return true;
            });
        }

        private void Balances_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var coll = sender as ICollection<Balance>;
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

        private void BuildFundsView()
        {
            CreateColumn(nodeview1, "Asset", new CellRendererText(), RenderFundsAsset, 0);
            var colTotal = CreateColumn(nodeview1, "Total", new CellRendererText() { Xalign = (float)1.0 }, RenderFundsTotal, 1);
            var colFree = CreateColumn(nodeview1, "Available", new CellRendererText() { Xalign = (float)1.0 }, RenderFundsAvailable, 2);
            var colLocked = CreateColumn(nodeview1, "In Order", new CellRendererText() { Xalign = (float)1.0 }, RenderFundsLocked, 3);
            colTotalBtc = CreateColumn(nodeview1, "BTC Value", new CellRendererText() { Xalign = (float)1.0 }, RenderFundsTotalBtc, 4);
            colTotalUsd = CreateColumn(nodeview1, "USD Value", new CellRendererText() { Xalign = (float)1.0 }, RenderFundsTotalUsd, 5);
            var colPercents = CreateColumn(nodeview1, "% of Total", new CellRendererText() { Xalign = (float)1.0 }, RenderFundsPercents, 6);
            var colDummy = CreateColumn(nodeview1, "", new CellRendererText() { Xalign = (float)1.0 }, null, -1);
            colTotal.Alignment = (float)1.0;
            colFree.Alignment = (float)1.0;
            colLocked.Alignment = (float)1.0;
            colLocked.Visible = false;
            colTotalBtc.Alignment = (float)1.0;
            colTotalUsd.Alignment = (float)1.0;
        }

        static TreeViewColumn CreateColumn(TreeView view, string title, CellRenderer cell, TreeCellDataFunc dataFunc, int sortOrderId)
        {
            var col = view.AppendColumn(title, cell, dataFunc);
            col.Sizing = TreeViewColumnSizing.GrowOnly;
            return col;
        }

        private void RenderFundsAsset(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var balance = model.GetValue(iter, 0) as Balance;
            if (balance != null)
            {
                (cell as CellRendererText).Text = balance.Asset;
            }
        }

        private void RenderFundsTotal(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var balance = model.GetValue(iter, 0) as Balance;
            if (balance != null)
            {
                (cell as CellRendererText).Text = balance.Total.ToString("N8");
            }
        }

        private void RenderFundsAvailable(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var balance = model.GetValue(iter, 0) as Balance;
            if (balance != null)
            {
                (cell as CellRendererText).Text = balance.Free.ToString("N8");
            }
        }

        private void RenderFundsLocked(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var balance = model.GetValue(iter, 0) as Balance;
            if (balance != null)
            {
                (cell as CellRendererText).Text = balance.Locked.ToString("N8");
            }
        }

        private void RenderFundsTotalBtc(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var balance = model.GetValue(iter, 0) as Balance;
            if (balance != null)
            {
                var intensity = (byte)((200 - balance.Percentage * 2));
                (cell as CellRendererText).ForegroundGdk = new Gdk.Color(intensity, intensity, intensity);
                (cell as CellRendererText).Text = balance.TotalBtc.ToString("N8");
            }
        }

        private void RenderFundsTotalUsd(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var balance = model.GetValue(iter, 0) as Balance;
            if (balance != null)
            {
                (cell as CellRendererText).Text = balance.TotalUsd.ToString("N2");
            }
        }

        private void RenderFundsPercents(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var balance = model.GetValue(iter, 0) as Balance;
            if (balance != null)
            {
                (cell as CellRendererText).Text = balance.Percentage.ToString("N2") + "%";
            }
        }

        bool FilterSmallAssets(TreeModel model, Gtk.TreeIter iter)
        {
            var balance = model.GetValue(iter, 0) as Balance;
            if (balance == null)
                return true;
            if (balance.TotalBtc >= 0.001m)
                return true;
            else
                return false;
        }
    }
}
