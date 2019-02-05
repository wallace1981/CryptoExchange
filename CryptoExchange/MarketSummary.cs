using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using Exchange.Net;
using Gtk;

namespace CryptoExchange
{
    [System.ComponentModel.ToolboxItem(true)]
    public partial class MarketSummary : Gtk.Bin
    {
        ExchangeViewModel viewModel { get; set; }
        ListStore store = new ListStore(typeof(PriceTicker));
        TreeModelFilter filter;
        TreeModelSort sorted;
        IList<string> visibleMarkets = new List<string>();

        Gdk.Color bearColor = new Gdk.Color(226, 101, 101);
        Gdk.Color bullColor = new Gdk.Color(82, 204, 84);

        public MarketSummary()
        {
            this.Build();
            BuildMarketSummary();
        }

        public void Initialize(ExchangeViewModel vm)
        {
            viewModel = vm;
            filter = new TreeModelFilter(store, null) { VisibleFunc = FilterSymbols };
            sorted = new TreeModelSort(filter);
            sorted.SetSortFunc(0, SortBySymbol);
            sorted.SetSortFunc(2, SortByPrice);
            sorted.SetSortFunc(3, SortByPriceChangePercent);
            sorted.SetSortFunc(4, SortByVolume);
            sorted.SetSortColumnId(4, SortType.Ascending);
            nodeview1.Model = sorted;
            viewModel.MarketSummaries.CollectionChanged += MarketSummaries_CollectionChanged;
        }

        void MarketSummaries_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var coll = sender as ICollection<PriceTicker>;
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Replace:
                    Debug.Assert(e.NewItems.Count == 1);
                    Debug.Assert(e.NewStartingIndex == e.OldStartingIndex);
                    //Debug.Print($"MarketSummaries: Replaced {e.NewItems.Count} item(s).");
                    Gtk.Application.Invoke(delegate
                    {
                        TreeIter iter;
                        store.GetIterFromString(out iter, e.OldStartingIndex.ToString());
                        store.SetValue(iter, 0, e.NewItems[0]);
                    });
                    break;
                case NotifyCollectionChangedAction.Remove:
                    Debug.Assert(e.OldItems.Count == 1);
                    //Debug.Print($"MarketSummaries: Removed {e.OldItems.Count} item(s).");
                    Gtk.Application.Invoke(delegate
                    {
                        TreeIter iter;
                        store.GetIterFromString(out iter, e.OldStartingIndex.ToString());
                        store.Remove(ref iter);
                    });
                    break;
                case NotifyCollectionChangedAction.Reset:
                    Debug.Print($"MarketSummaries: Reset. {coll.Count} item(s).");
                    Gtk.Application.Invoke(delegate
                    {
                        store.Clear();
                        foreach (var item in coll)
                            store.AppendValues(item);
                        BuildMarkets();
                    });
                    break;
            }
        }

        private void BuildMarketSummary()
        {
            var symbolColumn = CreateColumn(nodeview1, "Pair", new CellRendererText(), RenderSymbol, 0);
            var statusColumn = CreateColumn(nodeview1, "Status", new CellRendererText(), RenderStatus, 1);
            var priceColumn = CreateColumn(nodeview1, "Price", new CellRendererText(), RenderPrice, -1);
            var changeColumn = CreateColumn(nodeview1, "Change", new CellRendererText() { Xalign = (float)1.0 }, RenderChange, 3);
            changeColumn.Alignment = (float)1.0;
            var volumeColumn = CreateColumn(nodeview1, "Volume", new CellRendererText() { Xalign = (float)1.0 }, RenderVolume, 4);
            volumeColumn.Alignment = (float)1.0;
            var buyVolumeColumn = CreateColumn(nodeview1, "Buy Volume", new CellRendererText() { Xalign = (float)1.0 }, RenderBuyVolume, -1);
            buyVolumeColumn.Alignment = (float)1.0;
            nodeview1.Selection.Changed += OnNodeview1CursorChanged;
            nodeview1.SearchColumn = 0;
            nodeview1.EnableSearch = true;
            nodeview1.SearchEqualFunc = HandleTreeViewSearchEqualFunc;
            statusColumn.Visible = false;
        }

        private void BuildMarkets()
        {
            if (hbox1.Children.Length > 2)
                return;
            foreach (var asset in viewModel.Markets.Where(x => viewModel.MarketSummaries.Any(y => x.Symbol == y.Symbol)).Select(x => x.QuoteAsset).Distinct())
            {
                var tgl = new CheckButton(asset) { Active = true };
                hbox1.PackStart(tgl, false, false, 0);
                tgl.Show();
                tgl.Toggled += (object sender, EventArgs e) =>
                {
                    //visibleMarkets = GetVisibleMarkets();
                    filter.Refilter();
                };
            }
            //visibleMarkets = GetVisibleMarkets();
        }

        protected void OnNodeview1CursorChanged(object sender, EventArgs e)
        {
            TreeSelection selection = nodeview1.Selection;
            TreeModel model;
            TreeIter iter;
            if (selection.GetSelected(out model, out iter))
            {
                var ticker = model.GetValue(iter, 0) as PriceTicker;
                if (ticker != null)
                {
                    var market = viewModel.GetSymbolInformation(ticker.Symbol);
                    viewModel.CurrentSymbolInformation = market;
                    viewModel.CurrentSymbol = market.Symbol;
                }
            }
        }

        protected void OnEntry1Changed(object sender, EventArgs e)
        {
            if (filter != null)
                filter.Refilter();
        }

        static TreeViewColumn CreateColumn(TreeView view, string title, CellRenderer cell, TreeCellDataFunc dataFunc, int sortOrderId)
        {
            //var col = view.AppendColumn(title, cell, dataFunc);
            var col = new TreeViewColumn() { Resizable = true, Clickable = true, Title = title, SortColumnId = sortOrderId };
            col.PackStart(cell, true);
            view.AppendColumn(col);
            col.SetCellDataFunc(cell, dataFunc);
            return col;
        }

        void RenderStatus(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as PriceTicker;
            if (ticker != null)
            {
                var market = viewModel.GetSymbolInformation(ticker.Symbol);
                if (market.Status == "BREAK")
                {
                    (cell as Gtk.CellRendererText).Foreground = "red";
                }
                else
                {
                    (cell as Gtk.CellRendererText).Foreground = null;
                }
                (cell as Gtk.CellRendererText).Text = market.Status;
            }
        }

        void RenderSymbol(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as PriceTicker;
            if (ticker != null)
            {
                var market = viewModel.GetSymbolInformation(ticker.Symbol);
                if (market.QuoteAsset.Equals("BTC", StringComparison.CurrentCultureIgnoreCase))
                {
                    (cell as Gtk.CellRendererText).Foreground = "orange";
                }
                else if (market.QuoteAsset.Equals("ETH", StringComparison.CurrentCultureIgnoreCase))
                {
                    (cell as Gtk.CellRendererText).Foreground = "darkgray";
                }
                else if (market.QuoteAsset.Equals("BNB", StringComparison.CurrentCultureIgnoreCase))
                {
                    (cell as Gtk.CellRendererText).Foreground = "brown";
                }
                else if (market.QuoteAsset.Equals("USD", StringComparison.CurrentCultureIgnoreCase))
                {
                    (cell as Gtk.CellRendererText).Foreground = "lightgreen";
                }
                else if (market.QuoteAsset.Equals("USDT", StringComparison.CurrentCultureIgnoreCase))
                {
                    (cell as Gtk.CellRendererText).Foreground = "green";
                }
                else
                {
                    (cell as Gtk.CellRendererText).Foreground = null;
                }
                if (market.Status != "TRADING")
                {
                    (cell as Gtk.CellRendererText).Foreground = "red";
                }
                (cell as Gtk.CellRendererText).Text = market.BaseAsset.ToUpper() + "/" + market.QuoteAsset.ToUpper();
            }
        }

        void RenderPrice(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as PriceTicker;
            if (ticker != null)
            {
                var market = viewModel.GetSymbolInformation(ticker.Symbol);
                if (ticker?.LastPrice > ticker?.PrevLastPrice.GetValueOrDefault((decimal)ticker?.LastPrice))
                {
                    //(cell as Gtk.CellRendererText).Foreground = "white";
                    (cell as Gtk.CellRendererText).ForegroundGdk = bullColor;
                    //GLib.Timeout.Add(200, () => LOL(cell as CellRendererText, bullColor) );
                }
                else if (ticker?.LastPrice < ticker?.PrevLastPrice.GetValueOrDefault((decimal)ticker?.LastPrice))
                {
                    //(cell as Gtk.CellRendererText).Foreground = "white";
                    (cell as Gtk.CellRendererText).ForegroundGdk = bearColor;
                    //GLib.Timeout.Add(200, () => { return LOL(cell as CellRendererText, bearColor); } );
                }
                else
                {
                    (cell as Gtk.CellRendererText).Background = null;
                    (cell as Gtk.CellRendererText).Foreground = null;
                }
                (cell as Gtk.CellRendererText).Text = ticker?.LastPrice.ToString(market.PriceFmt);
            }
        }

        private bool LOL(CellRendererText cell, Gdk.Color color)
        {
            cell.ForegroundGdk = color;
            cell.Background = "white";
            cell.Text = "LOL";
            return false;
        }

        void RenderChange(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as PriceTicker;
            if (ticker != null)
            {
                var market = viewModel.GetSymbolInformation(ticker.Symbol);
                if (ticker?.PriceChangePercent > 0M)
                {
                    (cell as Gtk.CellRendererText).ForegroundGdk = bullColor;
                }
                else if (ticker?.PriceChangePercent < 0M)
                {
                    (cell as Gtk.CellRendererText).ForegroundGdk = bearColor;
                }
                else
                {
                    (cell as Gtk.CellRendererText).Foreground = "gray";
                }
                (cell as Gtk.CellRendererText).Text = ticker?.PriceChangePercent?.ToString("N2") + "%";
            }
        }

        void RenderVolume(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as PriceTicker;
            if (ticker != null)
            {
                var market = viewModel.GetSymbolInformation(ticker.Symbol);
                (cell as Gtk.CellRendererText).Text = ticker?.Volume?.ToString("N0");
            }
        }

        void RenderBuyVolume(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as PriceTicker;
            if (ticker != null)
            {
                var market = viewModel.GetSymbolInformation(ticker.Symbol);
                if (ticker?.BuyVolume * 2m > ticker?.Volume.GetValueOrDefault())
                {
                    (cell as Gtk.CellRendererText).ForegroundGdk = bullColor;
                }
                else if (ticker?.BuyVolume * 2m < ticker?.Volume.GetValueOrDefault())
                {
                    (cell as Gtk.CellRendererText).ForegroundGdk = bearColor;
                }
                else
                {
                    (cell as Gtk.CellRendererText).Foreground = null;
                }
                (cell as Gtk.CellRendererText).Text = ticker?.BuyVolume.ToString("N0");
            }
        }

        int SortBySymbol(TreeModel model, TreeIter a, TreeIter b)
        {
            var ticker1 = model.GetValue(a, 0) as PriceTicker;
            var ticker2 = model.GetValue(b, 0) as PriceTicker;
            return string.Compare(ticker1?.Symbol, ticker2?.Symbol);
        }

        int SortByPrice(TreeModel model, TreeIter a, TreeIter b)
        {
            var ticker1 = model.GetValue(a, 0) as PriceTicker;
            var ticker2 = model.GetValue(b, 0) as PriceTicker;
            return decimal.Compare(ticker2.LastPrice, ticker1.LastPrice);
        }

        int SortByPriceChangePercent(TreeModel model, TreeIter a, TreeIter b)
        {
            var ticker1 = model.GetValue(a, 0) as PriceTicker;
            var ticker2 = model.GetValue(b, 0) as PriceTicker;
            return decimal.Compare(ticker2.PriceChangePercent.GetValueOrDefault(), ticker1.PriceChangePercent.GetValueOrDefault());
        }

        int SortByVolume(TreeModel model, TreeIter a, TreeIter b)
        {
            var ticker1 = model.GetValue(a, 0) as PriceTicker;
            var ticker2 = model.GetValue(b, 0) as PriceTicker;
            return decimal.Compare(ticker2?.Volume == null ? decimal.Zero : ticker2.Volume.Value, ticker1?.Volume == null ? decimal.Zero : ticker1.Volume.Value);
        }

        bool FilterSymbols(TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as PriceTicker;
            if (ticker == null) return true;
            var market = viewModel.GetSymbolInformation(ticker.Symbol);
            //if (!visibleMarkets.Contains(market.QuoteAsset, StringComparer.CurrentCultureIgnoreCase))
            //return false;
            var filterstr = entry1.Text.Trim();
            if (string.IsNullOrWhiteSpace(filterstr) || market.Symbol.StartsWith(filterstr, StringComparison.CurrentCultureIgnoreCase))
                return true;
            else
                return false;
        }

        IList<string> GetVisibleMarkets()
        {
            return hbox1.Children.Cast<ToggleButton>().Where(b => b.Active).Select(b => b.Label).ToList();
        }

        private bool HandleTreeViewSearchEqualFunc(TreeModel model, int column, string key, TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as PriceTicker;
            var market = viewModel.GetSymbolInformation(ticker.Symbol);
            return !market.BaseAsset.Equals(key, StringComparison.CurrentCultureIgnoreCase);
        }

        protected void OnCombobox1Changed(object sender, EventArgs e)
        {
            viewModel.CurrentMarketSummariesPeriod = combobox1.ActiveText;
        }
    }
}
