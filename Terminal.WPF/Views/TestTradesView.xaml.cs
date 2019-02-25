using Exchange.Net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
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
    /// Interaction logic for TestTradesView.xaml
    /// </summary>
    public partial class TestTradesView : UserControl
    {

        private TradeModel model;
        private TradeViewModel viewModel;
        private List<ExchangeTrade> trades;
        private int ind = 0;

        public TestTradesView()
        {
            InitializeComponent();

            model = new TradeModel();
            viewModel = new TradeViewModel(model);
            grdTest.ItemsSource = new [] { viewModel };
        }

        List<ExchangeTrade> LoadBinanceTrades(string pathToJson)
        {
            var json = File.ReadAllText(pathToJson);
            var result = JsonConvert.DeserializeObject<Binance.AccountTrade[]>(json);
            return result.Select(t => new ExchangeTrade(t.isBuyer ? TradeSide.Buy : TradeSide.Sell, t.price, t.qty)).ToList();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (trades != null)
                return;
            trades = LoadBinanceTrades(@"C:\Users\wallace\source\repos\CryptoExchange\Terminal.WPF\bin\Debug\binance\accTrades-THETABTC.json");
            Observable.Interval(TimeSpan.FromSeconds(0.1))
                .Subscribe(x =>
                {
                    if (ind < trades.Count)
                        model.RegisterTrade(trades[ind++]);
                });
        }
    }
}
