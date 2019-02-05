using System;
using Gtk;
using Exchange.Net;
using System.Collections.Specialized;
using System.Collections.Generic;

namespace CryptoExchange
{
    [System.ComponentModel.ToolboxItem(true)]
    public partial class Transfers : Gtk.Bin
    {
        ExchangeViewModel viewModel { get; set; }
        ListStore store = new ListStore(typeof(Transfer));

        public Transfers()
        {
            this.Build();
            BuildTransfersView();
        }

        public void Initialize(ExchangeViewModel vm, TransferType tt)
        {
            viewModel = vm;
            nodeview1.Model = store;
            if (tt == TransferType.Deposit)
                viewModel.Deposits.CollectionChanged += Transfers_CollectionChanged;
            else
                viewModel.Withdrawals.CollectionChanged += Transfers_CollectionChanged;
        }

        private void Transfers_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var coll = sender as ICollection<Transfer>;
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

        private void BuildTransfersView()
        {
            var timeColumn = CreateColumn(nodeview1, "Date", new CellRendererText(), RenderTransferTime, 0);
            CreateColumn(nodeview1, "Asset", new CellRendererText(), RenderTransferAsset, 1);
            var colQty = CreateColumn(nodeview1, "Amount", new CellRendererText() { Xalign = (float)1.0 }, RenderTransferQuantity, 2);
            //CreateColumn(nodeview1, "Fee", new CellRendererText() { Foreground = "gray", Xalign = (float)1.0 }, RenderTransferFee, 3);
            CreateColumn(nodeview1, "Status", new CellRendererText(), RenderTransferStatus, 4);

            colQty.Alignment = (float)1.0;
        }

        static TreeViewColumn CreateColumn(TreeView view, string title, CellRenderer cell, TreeCellDataFunc dataFunc, int sortOrderId)
        {
            var col = view.AppendColumn(title, cell, dataFunc);
            col.Sizing = TreeViewColumnSizing.GrowOnly;
            return col;
        }
    
        private void RenderTransferTime(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var transfer = model.GetValue(iter, 0) as Transfer;
            if (transfer != null)
            {
                (cell as CellRendererText).Text = transfer.Timestamp.TimestampToString();
            }
        }

        private void RenderTransferStatus(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var transfer = model.GetValue(iter, 0) as Transfer;
            if (transfer != null)
            {
                (cell as CellRendererText).Text = transfer.Status.ToString();
            }
        }

        private void RenderTransferAsset(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var transfer = model.GetValue(iter, 0) as Transfer;
            if (transfer != null)
            {
                (cell as CellRendererText).Text = transfer.Asset;
            }
        }

        private void RenderTransferQuantity(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var transfer = model.GetValue(iter, 0) as Transfer;
            if (transfer != null)
            {
                (cell as CellRendererText).Text = transfer.Quantity.ToString("N8");
            }
        }

        private void RenderTransferFee(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var transfer = model.GetValue(iter, 0) as Transfer;
            if (transfer != null)
            {
                (cell as CellRendererText).Text = transfer.Comission.ToString("N8");
            }
        }
    
    }
}
