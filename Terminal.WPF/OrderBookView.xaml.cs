using Exchange.Net;
using ReactiveUI;
using ReactiveUI.Legacy;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Terminal.WPF
{
    /// <summary>
    /// Interaction logic for OrderBookView.xaml
    /// </summary>
    public partial class OrderBookView : UserControl
    {
        ReactiveList<OrderBookEntry> orderBook2 = new ReactiveList<OrderBookEntry>() { ChangeTrackingEnabled = true };
        //ReactiveList<OrderBookEntry> bids = new ReactiveList<OrderBookEntry>() { ChangeTrackingEnabled = false };
        //ReactiveList<OrderBookEntry> asks = new ReactiveList<OrderBookEntry>() { ChangeTrackingEnabled = false };
        OrderBook orderBook = new OrderBook(null);
        BinanceApiClient client;// = new BinanceApiClient();
        SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        long lastUpdateId = 0;
        IDisposable subDepth, subKline;
        SymbolInformation si => orderBook?.SymbolInformation;

        public OrderBookView()
        {
            InitializeComponent();
            //dgAsks.ItemsSource = orderBook.Asks;
            //dgBids.ItemsSource = orderBook.Bids;
            //cmbBookSize.ItemsSource = new List<int> { 5, 10, 20, 50, 100, 500, 1000 };
            //cmbBookSize.SelectedValue = 1000;
            ////SubscribeOrderBook();
            //LoadSymbols();

            var scrollViewer = GetDescendantByType(dgAsks, typeof(ScrollViewer)) as ScrollViewer;
            scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            //scrollViewer.SizeChanged += scrollViewer_SizeChanged;
        }

        private void scrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
        }

        private async void LoadSymbols()
        {
            var result = await client.GetExchangeInfoAsync();
            cmbSymbols.ItemsSource = result.Data.symbols;
        }

        private void SubscribeOrderBook(string symbol)
        {
            if (subDepth != null)
            {
                subDepth.Dispose();
                orderBook.Asks.Clear();
                orderBook.Bids.Clear();
            }
            if (subKline != null)
            {
                subKline.Dispose();
            }
            orderBook.MergeDecimals = (int)cmbMergeDecimals.SelectedValue;
            var obs = client.ObserveOrderBook(symbol);
            subDepth = obs.ObserveOnDispatcher().Subscribe(OnOrderBook3);
            var obs2 = client.SubscribeKlinesAsync(Enumerable.Repeat(symbol, 1), "1m");
            subKline = obs2.ObserveOnDispatcher().Subscribe(OnKline);
        }

        public static Visual GetDescendantByType(Visual element, Type type)
        {
            if (element == null)
            {
                return null;
            }
            if (element.GetType() == type)
            {
                return element;
            }
            Visual foundElement = null;
            if (element is FrameworkElement)
            {
                (element as FrameworkElement).ApplyTemplate();
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                Visual visual = VisualTreeHelper.GetChild(element, i) as Visual;
                foundElement = GetDescendantByType(visual, type);
                if (foundElement != null)
                {
                    break;
                }
            }
            return foundElement;
        }

        private async void OnOrderBook(Binance.WsDepth depth)
        {
            try
            {
                await _lock.WaitAsync();
                if (orderBook2.IsEmpty)
                {
                    var result = await client.GetDepthAsync(depth.symbol, 1000);
                    if (result.Success)
                    {
                        orderBook2.AddRange(Convert(result.Data, si));
                        lastUpdateId = result.Data.lastUpdateId;
                        //Debug.Print($"{lastUpdateId}");
                    }
                }

                if (depth.finalUpdateId <= lastUpdateId)
                {
                    //Debug.Print($"{depth.firstUpdateId}  :  {depth.finalUpdateId}  dropped");
                    return;
                }
                if (depth.firstUpdateId <= lastUpdateId + 1 && depth.finalUpdateId >= lastUpdateId + 1)
                {
                    var sw = Stopwatch.StartNew();
                    var bookUpdates = Convert(depth, si);
                    foreach (var e in bookUpdates)
                    {
                        if (e.Quantity == decimal.Zero)
                            orderBook2.RemoveAll(orderBook2.Where(x => x.Price == e.Price).ToList());
                        else
                        {
                            // add or update
                            var item = orderBook2.LastOrDefault(x => e.Side == x.Side && e.Price <= x.Price);
                            if (item != null)
                            {
                                if (e.Price == item.Price)
                                    item.Quantity = e.Quantity;
                                else
                                {
                                    var idx = orderBook2.IndexOf(item);
                                    orderBook2.Insert(idx + 1, e);
                                    //Debug.Print($"Inserting {e.Price} after {item.Price}");
                                }
                            }
                            else
                            {
                                if (e.Side == TradeSide.Sell)
                                    orderBook2.Insert(0, e);
                                else
                                {
                                    item = orderBook2.FirstOrDefault(x => x.Side == e.Side);
                                    var idx = orderBook2.IndexOf(item);
                                    orderBook2.Insert(idx, e);
                                    //Debug.Print($"Inserting {e.Price} before {item.Price}");
                                }
                            }
                        }
                    }
                    {
                        // remove items which are out of size
                        var item = orderBook2.FirstOrDefault(x => x.Side == TradeSide.Buy);
                        var idx = orderBook2.IndexOf(item);
                        if (false)
                        {
                            var count = (int)cmbBookSize.SelectedValue;
                            while (idx + count < orderBook2.Count)
                                orderBook2.Remove(orderBook2.Last());
                            while (idx-- > count)
                                orderBook2.Remove(orderBook2.First());
                        }
                        // calculate agg. totals
                        decimal aggTotal = decimal.Zero;
                        for (var i = idx; i < orderBook2.Count; ++i)
                        {
                            aggTotal += orderBook2[i].Total;
                            orderBook2[i].TotalCumulative = aggTotal;
                        }
                        aggTotal = 0;
                        for (var i = idx-1; i >= 0; --i)
                        {
                            aggTotal += orderBook2[i].Total;
                            orderBook2[i].TotalCumulative = aggTotal;
                        }
                    }
                    lastUpdateId = depth.finalUpdateId;
                    Debug.Print($"Update {lastUpdateId} took {sw.ElapsedMilliseconds}ms.");

                    //if (true)
                    //{
                    //    // remove items which are out of size
                    //    var item = orderBook.FirstOrDefault(x => x.Side == TradeSide.Buy);
                    //    var idx = orderBook.IndexOf(item);
                    //    var count = (int)cmbBookSize.SelectedValue;
                    //    var askCount = idx > count ? count : idx;
                    //    var bidCount = count;

                    //    orderBookCopy.Clear();
                    //    orderBookCopy.AddRange(orderBook.Skip(idx - askCount).Take(askCount + bidCount));
                    //}

                }
                else
                {
                    //Debug.Print($"{depth.firstUpdateId}  :  {depth.finalUpdateId}  dropped");
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private async void OnOrderBook3(Binance.WsDepth depth)
        {
            try
            {
                await _lock.WaitAsync();
                if ((orderBook.Asks.IsEmpty && orderBook.Bids.IsEmpty) ||
                    (orderBook.MergeDecimals != (int)cmbMergeDecimals.SelectedValue))
                {
                    var result = await client.GetDepthAsync(depth.symbol, (int)cmbBookSize.SelectedValue);
                    if (result.Success)
                    {
                        var tmp = Convert(result.Data, si);
                        orderBook.MergeDecimals = (int)cmbMergeDecimals.SelectedValue;
                        orderBook.Assign(tmp);
                        lastUpdateId = result.Data.lastUpdateId;
                        //Debug.Print($"{lastUpdateId}");
                    }
                }
                if (depth.finalUpdateId <= lastUpdateId)
                {
                    Debug.Print($"{depth.firstUpdateId} : {depth.finalUpdateId}  dropped");
                    return;
                }
                if (depth.firstUpdateId <= lastUpdateId + 1 && depth.finalUpdateId >= lastUpdateId + 1)
                {
                    var sw = Stopwatch.StartNew();
                    var bookUpdates = Convert(depth, si);
                    foreach (var e in bookUpdates)
                    {
                        //Debug.Print($"Got {e.Price} : {e.Quantity}.");
                        orderBook.Update(e);
                    }

                    lastUpdateId = depth.finalUpdateId;
                    Debug.Print($"Update {lastUpdateId} took {sw.ElapsedMilliseconds}ms.");

                    //if (true)
                    //{
                    //    // remove items which are out of size
                    //    var item = orderBook.FirstOrDefault(x => x.Side == TradeSide.Buy);
                    //    var idx = orderBook.IndexOf(item);
                    //    var count = (int)cmbBookSize.SelectedValue;
                    //    var askCount = idx > count ? count : idx;
                    //    var bidCount = count;

                    //    orderBookCopy.Clear();
                    //    orderBookCopy.AddRange(orderBook.Skip(idx - askCount).Take(askCount + bidCount));
                    //}

                }
                else
                {
                    Debug.Print($"{depth.firstUpdateId} : {depth.finalUpdateId}  dropped");
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private async void OnOrderBook2(Binance.WsDepth depth)
        {
            try
            {
                await _lock.WaitAsync();
                if ((orderBook.Asks.IsEmpty && orderBook.Bids.IsEmpty) || (orderBook.MergeDecimals != (int)cmbMergeDecimals.SelectedValue))
                {
                    var result = await client.GetDepthAsync(depth.symbol, (int)cmbBookSize.SelectedValue);
                    if (result.Success)
                    {
                        var tmp = Convert(result.Data, si);
                        orderBook.Asks.AddRange(tmp.Where(x => x.Side == TradeSide.Sell));
                        orderBook.Bids.AddRange(tmp.Where(x => x.Side == TradeSide.Buy));
                        lastUpdateId = result.Data.lastUpdateId;
                        //Debug.Print($"{lastUpdateId}");
                    }
                }
                if (depth.finalUpdateId <= lastUpdateId)
                {
                    //Debug.Print($"{depth.firstUpdateId}  :  {depth.finalUpdateId}  dropped");
                    return;
                }
                if (depth.firstUpdateId <= lastUpdateId + 1 && depth.finalUpdateId >= lastUpdateId + 1)
                {
                    var sw = Stopwatch.StartNew();
                    var bookUpdates = Convert(depth, si);
                    foreach (var e in bookUpdates)
                    {
                        //Debug.Print($"Got {e.Price} : {e.Quantity}.");
                        if (e.Quantity == decimal.Zero)
                        {
                            orderBook.Asks.RemoveAll(orderBook.Asks.Where(x => x.Price == e.Price).ToList());
                            orderBook.Bids.RemoveAll(orderBook.Bids.Where(x => x.Price == e.Price).ToList());
                        }
                        else
                        {
                            // add or update
                            var bidsOrAsks = e.Side == TradeSide.Buy ? orderBook.Bids : orderBook.Asks;
                            var item = bidsOrAsks.LastOrDefault(x => e.Price <= x.Price);
                            if (item != null)
                            {
                                if (e.Price == item.Price)
                                    item.Quantity = e.Quantity;
                                else
                                {
                                    var idx = bidsOrAsks.IndexOf(item);
                                    bidsOrAsks.Insert(idx + 1, e);
                                }
                            }
                            else
                            {
                                if (e.Side == TradeSide.Sell)
                                    orderBook.Asks.Insert(0, e);
                                else
                                    orderBook.Bids.Insert(0, e);
                            }
                        }
                    }

                    lastUpdateId = depth.finalUpdateId;
                    Debug.Print($"Update {lastUpdateId} took {sw.ElapsedMilliseconds}ms.");

                    //if (true)
                    //{
                    //    // remove items which are out of size
                    //    var item = orderBook.FirstOrDefault(x => x.Side == TradeSide.Buy);
                    //    var idx = orderBook.IndexOf(item);
                    //    var count = (int)cmbBookSize.SelectedValue;
                    //    var askCount = idx > count ? count : idx;
                    //    var bidCount = count;

                    //    orderBookCopy.Clear();
                    //    orderBookCopy.AddRange(orderBook.Skip(idx - askCount).Take(askCount + bidCount));
                    //}

                }
                else
                {
                    //Debug.Print($"{depth.firstUpdateId}  :  {depth.finalUpdateId}  dropped");
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        SymbolInformation GetSymbolInformation(Binance.Market market)
        {
            var priceFilter = market.filters.SingleOrDefault((f) => f.filterType == Binance.FilterType.PRICE_FILTER.ToString());
            var lotSizeFilter = market.filters.SingleOrDefault((f) => f.filterType == Binance.FilterType.LOT_SIZE.ToString());
            return new SymbolInformation()
            {
                BaseAsset = market.baseAsset,
                QuoteAsset = market.quoteAsset,
                Symbol = market.symbol,
                Status = market.status,
                MinPrice = priceFilter.minPrice,
                MaxPrice = priceFilter.maxPrice,
                TickSize = priceFilter.tickSize,
                PriceDecimals = DigitsCount(priceFilter.tickSize),
                MinQuantity = lotSizeFilter.minQty,
                MaxQuantity = lotSizeFilter.maxQty,
                StepSize = lotSizeFilter.stepSize,
                QuantityDecimals = DigitsCount(lotSizeFilter.stepSize)
            };
        }

        internal static int DigitsCount(decimal value)
        {
            decimal factor = 1m;
            for (int idx = 1; idx <= 8; idx += 1)
            {
                factor = factor * 10m;
                if (factor * value == 1m)
                    return idx;
            }
            return 0;
        }

        private void OnKline(Binance.WsCandlestick candle)
        {
            //lblPrice.Content = candle.kline.closePrice;
            //lblVolume.Content = candle.kline.quoteVolume;
        }

        internal List<OrderBookEntry> Convert(Binance.Depth depth, SymbolInformation si)
        {
            return depth.asks.Select(y => new OrderBookEntry(orderBook.PriceDecimals, orderBook.QuantityDecimals)
            {
                Price = Math.Round(decimal.Parse(y[0]), si.PriceDecimals),
                Quantity = Math.Round(decimal.Parse(y[1]), si.QuantityDecimals),
                Side = TradeSide.Sell
            }).Reverse().Concat(depth.bids.Select(y => new OrderBookEntry(orderBook.PriceDecimals, orderBook.QuantityDecimals)
            {
                Price = Math.Round(decimal.Parse(y[0]), si.PriceDecimals),
                Quantity = Math.Round(decimal.Parse(y[1]), si.QuantityDecimals),
                Side = TradeSide.Buy
            })
            ).ToList();
        }

        internal List<OrderBookEntry> Convert(Binance.WsDepth depth, SymbolInformation si)
        {
            return depth.asks.Select(y => new OrderBookEntry(orderBook.PriceDecimals, orderBook.QuantityDecimals)
            {
                Price = Math.Round(decimal.Parse(y[0]), si.PriceDecimals),
                Quantity = Math.Round(decimal.Parse(y[1]), si.QuantityDecimals),
                Side = TradeSide.Sell
            }).Reverse().Concat(depth.bids.Select(y => new OrderBookEntry(orderBook.PriceDecimals, orderBook.QuantityDecimals)
            {
                Price = Math.Round(decimal.Parse(y[0]), si.PriceDecimals),
                Quantity = Math.Round(decimal.Parse(y[1]), si.QuantityDecimals),
                Side = TradeSide.Buy
            })
            ).ToList();
        }

        private static void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            //Debug.Print($"{e.VerticalChange}, {e.ExtentHeightChange}, {e.VerticalOffset}, {scrollViewer.ScrollableHeight}");
            if (e.ExtentHeightChange > 0 && (scrollViewer.ScrollableHeight - e.VerticalOffset == e.ExtentHeightChange || e.ExtentHeightChange >= scrollViewer.ScrollableHeight))
            {
                scrollViewer.ScrollToEnd();
            }
        }

        private void OrderBook_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var lv = sender as ListView;
            if (lv.SelectedItem == null)
                return;
            var wnd = new Window
            {
                Content = new SubmitOrder() { Margin = new Thickness(6) },
                Owner = Application.Current.MainWindow,
                SizeToContent = SizeToContent.WidthAndHeight,
                Title = "Submit Order",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow
            };
            var viewModel = DataContext as ExchangeViewModel;
            var order = new NewOrder(viewModel.CurrentSymbolInformation, lv.SelectedItem as OrderBookEntry);
            viewModel.NewOrder = order;
            wnd.DataContext = viewModel;
            wnd.ShowDialog();
        }

        private void cmbSymbols_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            orderBook.SymbolInformation = GetSymbolInformation(e.AddedItems[0] as Binance.Market);
            cmbMergeDecimals.ItemsSource = Enumerable.Range(3, si.PriceDecimals - 2);
            cmbMergeDecimals.SelectedValue = si.PriceDecimals;
            SubscribeOrderBook(si.Symbol);
        }
    }

}
