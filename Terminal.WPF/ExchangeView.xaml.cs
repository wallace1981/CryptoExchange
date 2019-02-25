using Exchange.Net;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Terminal.WPF
{
    /// <summary>
    /// Interaction logic for ExchangeView.xaml
    /// </summary>
    public partial class ExchangeView : UserControl, IViewFor<ExchangeViewModel>
    {
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
                disposables(this.OneWayBind(this.ViewModel, x => x, x => x.priceTicker.DataContext));
                disposables(this
                    .ViewModel
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
                                Title = "Create Task",
                                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                WindowStyle = WindowStyle.ToolWindow
                            };
                            return Observable.Start(() =>
                            {
                                var result = wnd.ShowDialog();
                                interaction.SetOutput(result.GetValueOrDefault());
                            }, RxApp.MainThreadScheduler);
                        }));
                disposables(this
                    .ViewModel
                    .ShowException
                    .RegisterHandler(
                        interaction =>
                        {
                            MessageBox.Show(Application.Current.MainWindow, interaction.Input.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            interaction.SetOutput(Unit.Default);
                        }));
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

    }
}

