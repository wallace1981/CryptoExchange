using Exchange.Net;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using Telerik.Windows.Controls;

namespace Terminal.WPF
{
    /// <summary>
    /// Interaction logic for ExchangeView.xaml
    /// </summary>
    public partial class ExchangeView : UserControl, IViewFor<ExchangeViewModel>
    {
        RadDesktopAlertManager alertManager = new RadDesktopAlertManager();

        public ExchangeView()
        {
            InitializeComponent();
            this.WhenActivated(disposables =>
            {
                //d(this.OneWayBind(this.ViewModel, vm => vm.RefreshMarketSummariesElapsed, v => v.lblRefreshMarketSummariesElapsed.Content));
                //d(this.OneWayBind(this.ViewModel, vm => vm.RefreshTradesElapsed, v => v.lblRefreshTradesElapsed.Content));
                //d(this.OneWayBind(this.ViewModel, vm => vm.RefreshDepositsElapsed, v => v.lblRefreshDepositsElapsed.Content));
                //d(this.OneWayBind(this.ViewModel, x => x.Markets, x => x.cmbMarkets.ItemsSource));
                //d(this.Bind(this.ViewModel, x => x.CurrentSymbol, x => x.cmbMarkets.SelectedValue));
                //d(this.OneWayBind(this.ViewModel, x => x.MarketsByAsset, x => x.pnlMarkets.ItemsSource));
                //d(this.OneWayBind(this.ViewModel, x => x.MarketsByAsset, x => x.pnlTickers.ItemsSource));
                //d(this.BindCommand(this.ViewModel, x => x.GetRecentTradesCommand, x => x.btnGetTrades));
                //d(this.OneWayBind(this.ViewModel, x => x.RecentTrades, x => x.grdTrades.ItemsSource));
                //d(this.OneWayBind(this.ViewModel, vm => vm.OrderBook, x => x.grdOrderBook.ItemsSource));
                //d(this.OneWayBind(this.ViewModel, x => x.PriceTicker, x => x.grdOrderBook.ItemsSource));
                //d(this.OneWayBind(this.ViewModel, vm => vm.Deposits, v => v.grdDeposits.ItemsSource));
                //d(this.OneWayBind(this.ViewModel, vm => vm.Withdrawals, v => v.grdWithdrawals.ItemsSource));
                //d(this.Bind(this.ViewModel, vm => vm.MarketFilter, v => v.txtMarketFilter.Text));
                //d(this.OneWayBind(this.ViewModel, vm => vm, v => v.brdFunds.DataContext));
                this.OneWayBind(this.ViewModel, x => x, x => x.priceTicker.DataContext).DisposeWith(disposables);
                this.ViewModel
                    .CreateTask
                    .RegisterHandler(
                        interaction =>
                        {
                            var wnd = new Window
                            {
                                Content = interaction.Input,
                                ContentTemplate = Application.Current.Resources["rxuiViewModelHostTemplate"] as DataTemplate,
                                Owner = Application.Current.MainWindow,
                                ShowInTaskbar = false,
                                ShowActivated = true,
                                SizeToContent = SizeToContent.WidthAndHeight,
                                Title = "Новый трейд",
                                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                WindowStyle = WindowStyle.ToolWindow
                            };
                            return Observable.Start(() =>
                            {
                                var result = wnd.ShowDialog();
                                interaction.SetOutput(result.GetValueOrDefault());
                            }, RxApp.MainThreadScheduler);
                        }).DisposeWith(disposables);
                this.ViewModel
                    .Confirm
                    .RegisterHandler(
                        interaction =>
                        {
                            var result = MessageBox.Show(Application.Current.MainWindow, interaction.Input, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            interaction.SetOutput(result == MessageBoxResult.Yes);
                        }).DisposeWith(disposables);
                this.ViewModel
                    .ShowException
                    .RegisterHandler(
                        interaction =>
                        {
                            MessageBox.Show(Application.Current.MainWindow, interaction.Input.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            interaction.SetOutput(Unit.Default);
                        }).DisposeWith(disposables);
                this.ViewModel
                    .Alert
                    .RegisterHandler(
                        interaction =>
                        {
                            var alert = new RadDesktopAlert() {
                                CanAutoClose = false,
                                Content = interaction.Input,
                                Header = "Trading Bot Alert"
                            };
                            Dispatcher.Invoke(() => alertManager.ShowAlert(alert));
                            //MessageBox.Show(Application.Current.MainWindow, interaction.Input, ViewModel.ExchangeName, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                            interaction.SetOutput(Unit.Default);
                        }).DisposeWith(disposables);
            });
        }

        public ExchangeViewModel ViewModel
        {
            get { return (ExchangeViewModel)GetValue(ViewModelProperty); }
            set { SetValue(ViewModelProperty, value); }
        }

        object IViewFor.ViewModel
        {
            get { return ViewModel; }
            set { ViewModel = (ExchangeViewModel)value; }
        }

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register("ViewModel", typeof(ExchangeViewModel), typeof(ExchangeView));

        private void OrderBookView_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}

