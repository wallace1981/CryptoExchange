using Exchange.Net;
using System;
using System.Collections.Generic;
using System.Linq;
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

namespace WpfApp1.telerik
{
    /// <summary>
    /// Interaction logic for CreateTask.xaml
    /// </summary>
    public partial class CreateTask : UserControl
    {
        public CreateTask()
        {
            InitializeComponent();
            var binance = new BinanceViewModel();
            var markets = binance.GetFullMarketInformationOffline();
            cmbMarkets.ItemsSource = markets.OrderBy(x => x.BaseAsset).ToList();
            cmbMarkets.SelectedItem = markets.SingleOrDefault(x => x.Symbol == "BTCUSDT");
        }
        private void CmbMarkets_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0)
                return;
            var viewModel = new TradeTaskViewModel(e.AddedItems[0] as SymbolInformation, "BINANCE");
            DataContext = viewModel;
        }
    }
}
