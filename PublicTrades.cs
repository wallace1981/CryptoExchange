using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Linq;
using System.Threading;
using Exchange.Net;
using Gtk;

namespace CryptoExchange
{
    [System.ComponentModel.ToolboxItem(true)]
    public partial class PublicTrades : Bin
    {
        Gdk.Color bearColor = new Gdk.Color(226, 101, 101);
        Gdk.Color bullColor = new Gdk.Color(82, 204, 84);

        public PublicTrades()
        {
            this.Build();
			BuildTradesView();
        }

        private TreeViewColumn colPrice, colQty, colTotal;

		private void BuildTradesView()
        {
            var idColumn = CreateColumn(nodeview1, "Id", new CellRendererText(), RenderTradeId, -1);
            idColumn.Visible = false;
            colPrice = CreateColumn(nodeview1, "Price", new CellRendererText(), RenderTradePrice, -1);
            colQty = CreateColumn(nodeview1, "Amount", new CellRendererText() { Xalign = (float)1.0 }, RenderTradeQuantity, -1);
            colTotal = CreateColumn(nodeview1, "Total", new CellRendererText() { Foreground = "gray", Xalign = (float)1.0 }, RenderTradeTotal, -1);
            var colTime = CreateColumn(nodeview1, "Time", new CellRendererText() { Foreground = "gray", Xalign = (float)1.0 }, RenderTradeTime, -1);
            colQty.Alignment = (float)1.0;
            colTotal.Alignment = (float)1.0;
            colTime.Alignment = (float)1.0;
        }

        static TreeViewColumn CreateColumn(TreeView view, string title, CellRenderer cell, TreeCellDataFunc dataFunc, int sortOrderId)
        {
            var col = view.AppendColumn(title, cell, dataFunc);
            col.Sizing = TreeViewColumnSizing.GrowOnly;
            return col;
        }

        private void RenderTradeId(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var trade = model.GetValue(iter, 0) as PublicTrade;
            if (trade != null)
            {
                (cell as CellRendererText).Text = trade.Id.ToString();
            }
        }

		private void RenderTradeQuantity(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var trade = model.GetValue(iter, 0) as PublicTrade;
            if (trade != null)
            {
                var market = viewModel.GetSymbolInformation(trade.Symbol);
                (cell as CellRendererText).Text = trade.Quantity.ToString(market.QuantityFmt);

                if ((market.QuoteAsset == "BTC" && trade.Total >= 0.5m) ||
                    (market.QuoteAsset == "USDT" && trade.Total >= 3300m))
                {
                    if (trade.Side == TradeSide.Sell)
                    {
                        (cell as CellRendererText).BackgroundGdk = bullColor;
                        (cell as CellRendererText).Foreground = "white";
                    }
                    else
                    {
                        (cell as CellRendererText).BackgroundGdk = bearColor;
                        (cell as CellRendererText).Foreground = "white";
                    }
                }
                else
                {
                    (cell as CellRendererText).Background = null;
                    (cell as CellRendererText).Foreground = null;
                }
            }
        }

        private void RenderTradePrice(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var trade = model.GetValue(iter, 0) as PublicTrade;
            if (trade != null)
            {
                var market = viewModel.GetSymbolInformation(trade.Symbol);
                if (trade.Side == TradeSide.Sell)
                {
                    (cell as CellRendererText).ForegroundGdk = bullColor;
                }
                else
                {
                    (cell as CellRendererText).ForegroundGdk = bearColor;
                }
                (cell as CellRendererText).Text = trade.Price.ToString(market.PriceFmt);
            }
        }

        private void RenderTradeTime(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var trade = model.GetValue(iter, 0) as PublicTrade;
            if (trade != null)
            {
				var cellRenderer = cell as CellRendererText;
                cellRenderer.Text = trade.Timestamp.TimestampToString();
            }
        }

        private void RenderTradeTotal(TreeViewColumn tree_column, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var trade = model.GetValue(iter, 0) as PublicTrade;
            if (trade != null)
            {
                var market = viewModel.GetSymbolInformation(trade.Symbol);
                (cell as CellRendererText).Text = trade.Total.ToString(market.PriceFmt);
            }
        }

		ExchangeViewModel viewModel { get; set; }
		ListStore store = new ListStore(typeof(PublicTrade));

		protected void OnNodeview1Shown(object sender, EventArgs e)
		{
		}

        public void Initialize(ExchangeViewModel vm)
		{
			viewModel = vm;
			nodeview1.Model = store;
            viewModel.RecentTrades.CollectionChanged += RecentTrades_CollectionChanged;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
		}

        void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(viewModel.CurrentSymbolInformation):
                    colPrice.Title = $"Price({viewModel.CurrentSymbolInformation.QuoteAsset})";
                    colQty.Title = $"Amount({viewModel.CurrentSymbolInformation.BaseAsset})";
                    colTotal.Title = $"Total({viewModel.CurrentSymbolInformation.QuoteAsset})";
                    DisplayAssetInfo(viewModel.CurrentSymbolInformation.BaseAsset);
                    break;
            }
        }

        internal List<CoinMarketCap.PublicAPI.Listing> cmc_listing;
        internal async void DisplayAssetInfo(string asset)
        {
            const string url = "https://s2.coinmarketcap.com/static/img/coins/32x32/";
            if (cmc_listing == null)
                cmc_listing = await CoinMarketCapApiClient.GetListingsAsync();
            var listing = cmc_listing.FirstOrDefault(x => asset.Equals(x.symbol, StringComparison.CurrentCultureIgnoreCase));
            if (listing != null)
            {
                label5.Text = listing.name;
                var client = new RestSharp.RestClient(url);
                var req = new RestSharp.RestRequest($"{listing.id}.png", RestSharp.Method.GET);
                var resp = await client.ExecuteTaskAsync(req);
                image1.Pixbuf = new Gdk.Pixbuf(resp.RawBytes);
            }
            else
            {
                label5.Text = asset;
                image1.Pixbuf = null;
            }
        }

        int count = 0;
		private void RecentTrades_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			var coll = sender as ICollection<PublicTrade>;
			var tmax = viewModel.TradesMaxItemCount;
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
					Debug.Assert(e.NewItems.Count == 1);
					Debug.Assert(e.NewStartingIndex == 0);
                    Debug.Print($"PublicTrades: Added {e.NewItems.Count} items.");
					Gtk.Application.Invoke(delegate
					{
						if (count < tmax)
						{
							store.Append();
							count += 1;
						}
						// [count-1] point to latest item! we need to copy [count-2] to [count-1].
						TreeIter iter, slctIter = GetSelection();
						for (int ind = count - 1; ind > 0; ind -= 1)
						{
							store.GetIterFromString(out iter, (ind - 1).ToString());
                            var value = store.GetValue(iter, 0);
                            store.GetIterFromString(out iter, ind.ToString());
							store.SetValue(iter, 0, value);
						}
						store.GetIterFirst(out iter);
						store.SetValue(iter, 0, e.NewItems[0]);
                        // try restore selection.
                        if (!slctIter.Equals(TreeIter.Zero))
                        {
                            if (store.IterNext(ref slctIter))
                                nodeview1.Selection.SelectIter(slctIter);
                            else
                                nodeview1.Selection.UnselectAll();
                        }
					});
					break;
				case NotifyCollectionChangedAction.Move:
					break;
				case NotifyCollectionChangedAction.Remove:
                    Debug.Print($"PublicTrades: Removed {e.OldItems.Count} items.");
					//Gtk.Application.Invoke(delegate {
					//TreeIter iter;
					//                  store.GetIterFirst(out iter);
					//                  for (int i = 0; i < e.OldStartingIndex; ++i)
					//                      store.IterNext(ref iter);
					//                  for (int i = 0; i < e.OldItems.Count; ++i)
					//                  {
					//                      //store.Remove(ref iter);
					//                      store.IterNext(ref iter);
					//                  }
					////nodeview1.ScrollToPoint(0, 0);

					//});
					break;
				case NotifyCollectionChangedAction.Replace:
					break;
				case NotifyCollectionChangedAction.Reset:
                    Debug.Print($"PublicTrades: Reset. {coll.Count} items.");
                    Gtk.Application.Invoke(delegate
                    {
                        store.Clear();
                        count = 0;
                        foreach (var item in coll.ToList())
                        {
                            store.AppendValues(item);
                            count += 1;
                        }
                    });
					break;
			}
		}

        private TreeIter GetSelection()
        {
            var slct = nodeview1.Selection;
            TreeModel dummy;
            TreeIter iter;
            slct.GetSelected(out dummy, out iter);
            Debug.Assert(dummy == store);
            return iter;
        }

	}
}
