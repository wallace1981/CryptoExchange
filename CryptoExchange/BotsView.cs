﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Exchange.Net;
using Gtk;

namespace CryptoExchange
{
    [System.ComponentModel.ToolboxItem(true)]
    public partial class BotsView : Gtk.Bin
    {
        Gdk.Color bearColor = new Gdk.Color(226, 101, 101);
        Gdk.Color bullColor = new Gdk.Color(82, 204, 84);

        BinanceApiClient binance = new BinanceApiClient();

        public BotsView()
        {
            this.Build();
            BuildNodeView();
        }

        private void BuildNodeView()
        {
            var symbolColumn = CreateColumn(nodeview1, "Symbol", new CellRendererText(), RenderSymbol, 0);
            var priceColumn = CreateColumn(nodeview1, "Last Price", new CellRendererText(), RenderPrice, 1);
            var sideColumn = CreateColumn(nodeview1, "Side", new CellRendererSpin(), RenderSide, 2);
            var targetColumn = CreateColumn(nodeview1, "Target Price", new CellRendererSpin(), RenderTargetPrice, 3);
            var distance = CreateColumn(nodeview1, "Distance %", new CellRendererText() { Xalign = (float)1.0 }, RenderDistance, 4);
            nodeview1.Selection.Changed += OnNodeview1CursorChanged;
            nodeview1.EnableSearch = false;
        }

        public void Initialize(ExchangeViewModel vm)
        {
            viewModel = vm;
            sorted = new TreeModelSort(store);
            sorted.SetSortFunc(0, SortBySymbol);
            sorted.SetSortFunc(4, SortByDistance);
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
                    Gtk.Application.Invoke(delegate
                    {
                        Update(e.NewItems[0] as PriceTicker);
                    });
                    break;
                case NotifyCollectionChangedAction.Reset:
                    break;
            }
        }

        void Update(PriceTicker ticker)
        {
            TreeIter iter;
            var result = store.GetIterFirst(out iter);
            while (result)
            {
                var bot = store.GetValue(iter, 0) as MonitoringBot;
                if (bot.Symbol == ticker.Symbol)
                {
                    bot.UpdateLastPrice(ticker.LastPrice);
                    store.SetValue(iter, 0, bot); // Force UI to refresh it.
                    return;
                }
                result = store.IterNext(ref iter);
            }
        }

        void Update(Binance.WsCandlestick ticker)
        {
            TreeIter iter;
            var result = store.GetIterFirst(out iter);
            while (result)
            {
                var bot = store.GetValue(iter, 0) as MonitoringBot;
                if (bot.Symbol == ticker.symbol)
                {
                    bot.UpdateLastPrice(ticker.kline.closePrice, ticker.kline.lowPrice);
                    store.SetValue(iter, 0, bot); // Force UI to refresh it.
                    return;
                }
                result = store.IterNext(ref iter);
            }
        }

        private void OnNodeview1CursorChanged(object sender, EventArgs e)
        {
            //throw new NotImplementedException();
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

        void RenderSymbol(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var bot = model.GetValue(iter, 0) as MonitoringBot;
            if (bot != null)
            {
                var market = viewModel.GetSymbolInformation(bot.Symbol);
                if (market?.QuoteAsset == "BTC")
                {
                    (cell as Gtk.CellRendererText).Foreground = "orange";
                }
                else if (market?.QuoteAsset == "USD")
                {
                    (cell as Gtk.CellRendererText).Foreground = "lightgreen";
                }
                else if (market?.QuoteAsset == "USDT")
                {
                    (cell as Gtk.CellRendererText).Foreground = "green";
                }
                (cell as Gtk.CellRendererText).Text = market?.BaseAsset + " / " + market?.QuoteAsset;
            }
        }

        void RenderPrice(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var bot = model.GetValue(iter, 0) as MonitoringBot;
            if (bot != null)
            {
                var market = viewModel.GetSymbolInformation(bot.Symbol);
                if (bot?.LastPrice > bot?.PrevLastPrice.GetValueOrDefault())
                {
                    (cell as Gtk.CellRendererText).ForegroundGdk = bullColor;
                }
                else if (bot?.LastPrice < bot?.PrevLastPrice.GetValueOrDefault())
                {
                    (cell as Gtk.CellRendererText).ForegroundGdk = bearColor;
                }
                else
                {
                    (cell as Gtk.CellRendererText).Background = null;
                    (cell as Gtk.CellRendererText).Foreground = null;
                }
                (cell as Gtk.CellRendererText).Text = bot?.LastPrice.ToString(market?.PriceFmt);
            }
        }

        void RenderTargetPrice(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var bot = model.GetValue(iter, 0) as MonitoringBot;
            if (bot != null)
            {
                var market = viewModel.GetSymbolInformation(bot.Symbol);
                (cell as Gtk.CellRendererText).Text = bot?.TargetPrice.ToString(market?.PriceFmt);
            }
        }

        void RenderDistance(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var bot = model.GetValue(iter, 0) as MonitoringBot;
            if (bot != null)
            {
                //var market = viewModel.GetSymbolInformation(bot.Symbol);
                (cell as Gtk.CellRendererText).Text = bot?.Distance.ToString("N2") + "%";
            }
        }

        void RenderSide(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var bot = model.GetValue(iter, 0) as MonitoringBot;
            if (bot != null)
            {
                //var market = viewModel.GetSymbolInformation(bot.Symbol);
                (cell as Gtk.CellRendererText).Text = bot?.Side.ToString();
            }
        }

        int SortBySymbol(TreeModel model, TreeIter a, TreeIter b)
        {
            var bot1 = model.GetValue(a, 0) as MonitoringBot;
            var bot2 = model.GetValue(b, 0) as MonitoringBot;
            return string.Compare(bot1?.Symbol, bot2?.Symbol);
        }

        int SortByDistance(TreeModel model, TreeIter a, TreeIter b)
        {
            var bot1 = model.GetValue(a, 0) as MonitoringBot;
            var bot2 = model.GetValue(b, 0) as MonitoringBot;
            if (bot1 == null || bot2 == null)
                return 0;
            return decimal.Compare(bot1.Distance, bot2.Distance);
        }

        ExchangeViewModel viewModel { get; set; }
        ListStore store = new ListStore(typeof(MonitoringBot));
        ListStore symstore = new ListStore(typeof(string));
        TreeModelSort sorted;

        protected void OnAddActionActivated(object sender, EventArgs e)
        {
            IEnumerable<string> symbols = viewModel.Markets.Where(x => x.QuoteAsset == "BTC").Select(x => x.Symbol);
            var obs = binance.ObserveKlineCandlesticks(symbols, "1M");
            obs.Subscribe(candle =>
            {
                Gtk.Application.Invoke(delegate
                {
                    Update(candle);
                });
            });
            foreach (var sym in symbols)
                store.AppendValues(new MonitoringBot() { Symbol = sym, TargetPrice = 0m });

            //store.AppendValues(new TestTradingBot() { Symbol = "RVNBTC", BuyPrice = 0.00000490m, SellPrice = 0.00000505m, Volume = 10000m });
            //store.AppendValues(new TestTradingBot() { Symbol = "GOBTC", BuyPrice = 0.00000750m, SellPrice = 0.00000745m, Volume = 1 });

            //store.AppendValues(new MonitoringBot() { Symbol = "BTCUSDT", TargetPrice = 5750m });
            //store.AppendValues(new MonitoringBot() { Symbol = "XLMBTC", TargetPrice = 0.00002933m });
            //store.AppendValues(new MonitoringBot() { Symbol = "EOSBTC", TargetPrice = 0.0006914m });
            //store.AppendValues(new MonitoringBot() { Symbol = "BCCBTC", TargetPrice = 0.065732m });
            //store.AppendValues(new MonitoringBot() { Symbol = "IOTABTC", TargetPrice = 0.00006726m });
            //store.AppendValues(new MonitoringBot() { Symbol = "ZRXBTC", TargetPrice = 0.00008023m });
            //store.AppendValues(new MonitoringBot() { Symbol = "DASHBTC", TargetPrice = 0.021000m });
            //store.AppendValues(new MonitoringBot() { Symbol = "LINKBTC", TargetPrice = 0.00002276m });
            //store.AppendValues(new MonitoringBot() { Symbol = "XMRBTC", TargetPrice = 0.013100m });
            //store.AppendValues(new MonitoringBot() { Symbol = "IOSTBTC", TargetPrice = 0.00000163m });
            //store.AppendValues(new MonitoringBot() { Symbol = "WPRBTC", TargetPrice = 0.00000242m });
            //store.AppendValues(new MonitoringBot() { Symbol = "NANOBTC", TargetPrice = 0.0001270m });
            //store.AppendValues(new MonitoringBot() { Symbol = "WANBTC", TargetPrice = 0.0000971m });
            //store.AppendValues(new MonitoringBot() { Symbol = "BNBBTC", TargetPrice = 0.0014002m });
            //store.AppendValues(new MonitoringBot() { Symbol = "NEOBTC", TargetPrice = 0.0023400m });
            //store.AppendValues(new MonitoringBot() { Symbol = "CNDBTC", TargetPrice = 0.00000194m });
            //store.AppendValues(new MonitoringBot() { Symbol = "TRXBTC", TargetPrice = 0.00000259m });
            //store.AppendValues(new MonitoringBot() { Symbol = "ICXBTC", TargetPrice = 0.0000767m });
            //store.AppendValues(new MonitoringBot() { Symbol = "ONTBTC", TargetPrice = 0.0001756m });
            //store.AppendValues(new MonitoringBot() { Symbol = "ZILBTC", TargetPrice = 0.00000391m });
            //store.AppendValues(new MonitoringBot() { Symbol = "XVGBTC", TargetPrice = 0.00000165m });
            //store.AppendValues(new MonitoringBot() { Symbol = "BCDBTC", TargetPrice = 0.000252m });
            //store.AppendValues(new MonitoringBot() { Symbol = "NCASHBTC", TargetPrice = 0.00000069m });
            //store.AppendValues(new MonitoringBot() { Symbol = "SNMBTC", TargetPrice = 0.00000606m });
            //store.AppendValues(new MonitoringBot() { Symbol = "STORMBTC", TargetPrice = 0.00000095m });
            //store.AppendValues(new MonitoringBot() { Symbol = "WAVESBTC", TargetPrice = 0.0002374m });
            //store.AppendValues(new MonitoringBot() { Symbol = "KMDBTC", TargetPrice = 0.0001435m });
            //store.AppendValues(new MonitoringBot() { Symbol = "GOBTC", TargetPrice = 0.00000521m });
            //store.AppendValues(new MonitoringBot() { Symbol = "LUNBTC", TargetPrice = 0.0003337m });
            //store.AppendValues(new MonitoringBot() { Symbol = "DGDBTC", TargetPrice = 0.005045m });
            //store.AppendValues(new MonitoringBot() { Symbol = "PIVXBTC", TargetPrice = 0.0001262m });
        }
    }

    public class MonitoringBot
    {
        public string Symbol { get; set; }
        public TradeSide Side { get; set; } = TradeSide.Buy;
        public decimal TargetPrice { get; set; }
        public decimal Distance
        {
            get
            {
                if (LastPrice == decimal.Zero)
                    return -1;
                if (TargetPrice == decimal.Zero)
                    return -1;
                return 100m * LastPrice / TargetPrice - 100m;
            }
        }
        public decimal LastPrice { get; set; }
        public decimal? PrevLastPrice { get; set; }

        public virtual void UpdateLastPrice(decimal price)
        {
            PrevLastPrice = LastPrice;
            LastPrice = price;
        }

        public virtual void UpdateLastPrice(decimal price, decimal targetPrice)
        {
            this.TargetPrice = targetPrice;
            PrevLastPrice = LastPrice;
            LastPrice = price;
        }

        public MonitoringBot()
        {
        }
    }

    public class TestTradingBot : MonitoringBot
    {
        BinanceApiClient client = new BinanceApiClient();
        SemaphoreSlim locker = new SemaphoreSlim(1, 1);

        public decimal Volume { get; set; }
        public decimal BuyPrice { get; set; }
        public decimal SellPrice { get; set; }

        public TradeSide? CurrentSide { get; set; }

        private bool hasOrder;

        public async override void UpdateLastPrice(decimal price)
        {
            base.UpdateLastPrice(price);

            try
            {
                await locker.WaitAsync();
                if (CurrentSide == null)
                    CurrentSide = Side;
                if (CurrentSide == TradeSide.Buy)
                {
                    if (hasOrder)
                    {
                        CurrentSide = TradeSide.Sell;
                        hasOrder = false;
                    }
                    else
                    {
                        TargetPrice = BuyPrice;
                        if (price <= BuyPrice)
                        {
                            var result = await client.TestPlaceOrderAsync(Symbol, CurrentSide.Value, Binance.OrderType.LIMIT, Volume, price);
                            hasOrder = true;
                        }
                    }
                }
                else
                {
                    if (hasOrder)
                    {
                        CurrentSide = TradeSide.Buy;
                        hasOrder = false;
                    }
                    else
                    {
                        TargetPrice = SellPrice;
                        if (price >= SellPrice)
                        {
                            var result = client.TestPlaceOrderAsync(Symbol, CurrentSide.Value, Binance.OrderType.LIMIT, Volume, price);
                            hasOrder = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
            }
            finally
            {
                locker.Release();
            }
        }
    }
}
