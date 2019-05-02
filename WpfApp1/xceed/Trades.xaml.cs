using DynamicData;
using DynamicData.Aggregation;
using DynamicData.Binding;
using Exchange.Net;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WpfApp1.xceed
{
    /// <summary>
    /// Interaction logic for Trades.xaml
    /// </summary>
    public partial class Trades : UserControl
    {
        public Trades()
        {
            InitializeComponent();
            //DataContext = new TestViewModel("SNMBTC");
        }
    }

    public class TestViewModel : ReactiveObject
    {
        public string Symbol { get; }
        private SourceCache<PublicTrade, long> RecentTradesCache = new SourceCache<PublicTrade, long>(x => x.Id);
        public ReadOnlyObservableCollection<PublicTrade> RecentTradesView => recentTradesView;
        ReadOnlyObservableCollection<PublicTrade> recentTradesView;
        [ObservableAsProperty] public decimal TotalBuy { get; }
        [ObservableAsProperty] public decimal TotalSell { get; }
        [Reactive] public DateTime StartTime { get; set; }
        [Reactive] public decimal QuoteVolume1m { get; set; }
        [Reactive] public decimal QuoteBuyVolume1m { get; set; }
        public ReactiveCommand<Unit, Unit> Clear { get; }
        public ReactiveCommand<Unit, Unit> Finish { get; }

        public TestViewModel(string symbol)
        {
            Symbol = symbol;

            RecentTradesCache
                .Connect()
                .LimitSizeTo(20)
                .Sort(SortExpressionComparer<PublicTrade>.Descending(t => t.Id))
                .ObserveOnDispatcher()
                .Bind(out recentTradesView)
                .Subscribe()
                .DisposeWith(dispoables);

            RecentTradesCache
                .Connect()
                .Filter(x => x.Side == TradeSide.Buy)
                .Sum(x => x.Quantity)
                .ToPropertyEx(this, x => x.TotalBuy)
                .DisposeWith(dispoables);

            RecentTradesCache
                .Connect()
                .Filter(x => x.Side == TradeSide.Sell)
                .Sum(x => x.Quantity)
                .ToPropertyEx(this, x => x.TotalSell)
                .DisposeWith(dispoables);

            StartSequence(Symbol);

            Clear = ReactiveCommand
                .Create(ClearImpl)
                .DisposeWith(dispoables);
            Finish = ReactiveCommand
                .Create(FinishImpl)
                .DisposeWith(dispoables);
            StartTime = DateTime.Now;
        }

        public void FinishImpl()
        {
            dispoables.Dispose();
        }

        private void ClearImpl()
        {
            RecentTradesCache.Clear();
            StartTime = DateTime.Now;
        }

        private void StartSequence(string symbol)
        {
            var client = new BinanceApiClient();
            var obs = client.SubscribePublicTradesAsync(symbol).Select(x => new PublicTrade(null)
            {
                Id = x.tradeId,
                Side = !x.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
                Price = x.price,
                Quantity = x.quantity,
                Time = x.tradeTime.FromUnixTimestamp(),
            });
            var obs2 = GetRecentTrades2(symbol).Publish();
            obs.Merge(obs2).Subscribe(x => { /*obs2.Connect();*/ RecentTradesCache.AddOrUpdate(x); }, OnException)
                .DisposeWith(dispoables);
            client.SubscribeKlinesAsync(new[] { symbol }, "1m").Subscribe(OnKline).DisposeWith(dispoables);
            //obs2.Subscribe(x => { RecentTradesCache.AddOrUpdate(x); });
        }

        private void OnKline(Binance.WsCandlestick candle)
        {
            QuoteVolume1m = candle.kline.quoteVolume;
            QuoteBuyVolume1m = candle.kline.takerBuyQuoteVolume;
        }

        private void OnException(Exception ex)
        {
            MessageBox.Show(ex.ToString());
        }

        private IObservable<PublicTrade> GetRecentTrades(string symbol)
        {
            var client = new BinanceApiClient();
            var result = client.GetRecentTradesAsync(symbol, 5).Result;
            return result.Data.Select(x => new PublicTrade(null)
            {
                Id = x.id,
                Side = x.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
                Price = x.price,
                Quantity = x.qty,
                Time = x.time.FromUnixTimestamp(),
            }).ToObservable();
        }

        private IObservable<PublicTrade> GetRecentTrades2(string symbol)
        {
            var client = new BinanceApiClient();
            var obs = Observable.FromAsync(() => client.GetRecentTradesAsync(symbol, 5));
            return obs.Select(x => x.Success ? x.Data : Enumerable.Empty<Binance.Trade>()).SelectMany(x => x.ToObservable()).Select(x => new PublicTrade(null)
            {
                Id = x.id,
                Side = !x.isBuyerMaker ? TradeSide.Buy : TradeSide.Sell,
                Price = x.price,
                Quantity = x.qty,
                Time = x.time.FromUnixTimestamp(),
            });
        }

        private CompositeDisposable dispoables = new CompositeDisposable();
    }
}
