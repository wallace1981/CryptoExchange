using Exchange.Net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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

namespace Terminal.WPF
{
    public partial class PriceTickerView : UserControl
    {
        private Telerik.Windows.Data.FilterDescriptor fdBase;
        private Telerik.Windows.Data.FilterDescriptor fdQuote;

        public PriceTickerView()
        {
            InitializeComponent();
            fdBase = new Telerik.Windows.Data.FilterDescriptor();
            fdBase.Member = "SymbolInformation.BaseAsset";
            fdBase.Operator = Telerik.Windows.Data.FilterOperator.StartsWith;
            fdBase.IsCaseSensitive = false;
            // In most cases the data engine will discover this automatically so you do not need to set it.
            fdBase.MemberType = typeof(string);
            gv.FilterDescriptors.Add(fdBase);

            fdQuote = new Telerik.Windows.Data.FilterDescriptor();
            fdQuote.Member = "SymbolInformation.QuoteAsset";
            fdQuote.Operator = Telerik.Windows.Data.FilterOperator.IsEqualTo;
            fdQuote.IsCaseSensitive = false;
            // In most cases the data engine will discover this automatically so you do not need to set it.
            fdQuote.MemberType = typeof(string);
            gv.FilterDescriptors.Add(fdQuote);

            var fdStatus = new Telerik.Windows.Data.FilterDescriptor();
            fdStatus.Member = "SymbolInformation.Status";
            fdStatus.Operator = Telerik.Windows.Data.FilterOperator.IsNotEqualTo;
            fdStatus.IsCaseSensitive = false;
            // In most cases the data engine will discover this automatically so you do not need to set it.
            fdStatus.MemberType = typeof(string);
            fdStatus.Value = "BREAK";
            //gv.FilterDescriptors.Add(fdStatus);

            //rbBtc.IsChecked = true;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            fdBase.Value = (sender as TextBox).Text;
            MarketSummaries.Refresh();
            var binding = grdMarketSummaries.GetBindingExpression(DataGrid.SelectedItemProperty);
            binding.UpdateTarget();
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            fdQuote.Value = (sender as Telerik.Windows.Controls.RadToggleButton).CommandParameter;
        }

        public ExchangeViewModel ViewModel
        {
            get { return (ExchangeViewModel)DataContext; }
        }

        private void RadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            fdQuote.Value = (sender as Telerik.Windows.Controls.RadComboBox).SelectedValue;
            MarketSummaries.Refresh();
        }

        private void CollectionViewSource_Filter(object sender, FilterEventArgs e)
        {
            e.Accepted =
                (e.Item as PriceTicker).SymbolInformation.Status != "BREAK" &&
                (e.Item as PriceTicker).SymbolInformation.QuoteAsset == cmbQuoteAsset.SelectedValue as string &&
                (e.Item as PriceTicker).SymbolInformation.BaseAsset.StartsWith(tb.Text, StringComparison.CurrentCultureIgnoreCase);
        }

        private ICollectionView MarketSummaries => (Resources["csvMarketSummaries"] as CollectionViewSource).View;
    }

}


