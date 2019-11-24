using Exchange.Net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

            //fdBase = new Telerik.Windows.Data.FilterDescriptor();
            //fdBase.Member = "SymbolInformation.BaseAsset";
            //fdBase.Operator = Telerik.Windows.Data.FilterOperator.StartsWith;
            //fdBase.IsCaseSensitive = false;
            //// In most cases the data engine will discover this automatically so you do not need to set it.
            //fdBase.MemberType = typeof(string);
            //gv.FilterDescriptors.Add(fdBase);

            //fdQuote = new Telerik.Windows.Data.FilterDescriptor();
            //fdQuote.Member = "SymbolInformation.QuoteAsset";
            //fdQuote.Operator = Telerik.Windows.Data.FilterOperator.IsEqualTo;
            //fdQuote.IsCaseSensitive = false;
            //// In most cases the data engine will discover this automatically so you do not need to set it.
            //fdQuote.MemberType = typeof(string);
            //gv.FilterDescriptors.Add(fdQuote);

            //var fdStatus = new Telerik.Windows.Data.FilterDescriptor();
            //fdStatus.Member = "SymbolInformation.Status";
            //fdStatus.Operator = Telerik.Windows.Data.FilterOperator.IsNotEqualTo;
            //fdStatus.IsCaseSensitive = false;
            //// In most cases the data engine will discover this automatically so you do not need to set it.
            //fdStatus.MemberType = typeof(string);
            //fdStatus.Value = "BREAK";
            ////gv.FilterDescriptors.Add(fdStatus);

            ////rbBtc.IsChecked = true;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            //fdBase.Value = (sender as TextBox).Text;
            MarketSummaries.Refresh();
            var binding = grdMarketSummaries.GetBindingExpression(DataGrid.SelectedItemProperty);
            binding.UpdateTarget();
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            //fdQuote.Value = (sender as Telerik.Windows.Controls.RadToggleButton).CommandParameter;
        }

        public ExchangeViewModel ViewModel
        {
            get { return (ExchangeViewModel)DataContext; }
        }

        private void RadComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //fdQuote.Value = (sender as ComboBox).SelectedValue;
            MarketSummaries.Refresh();
        }

        private void CollectionViewSource_Filter(object sender, FilterEventArgs e)
        {
            var ticker = e.Item as PriceTicker;
            e.Accepted =
                ticker.SymbolInformation.Status != "BREAK" &&
                ticker.SymbolInformation.QuoteAsset == cmbQuoteAsset.SelectedValue as string &&
                (!chkAllowMargin.IsChecked.HasValue || ticker.SymbolInformation.IsMarginTradingAllowed == chkAllowMargin.IsChecked.Value) &&
                IsPairMatch(ticker.SymbolInformation, tb.Text.Trim()) && 
                FilterByPrice(ticker, tbPriceFilter.Tag as string, tbPriceFilter.Text.Trim()) &&
                FilterByPrice(ticker, tbPriceUsdFilter.Tag as string, tbPriceUsdFilter.Text.Trim()) &&
                FilterByPrice(ticker, tbVolumeFilter.Tag as string, tbVolumeFilter.Text.Trim());
                //(e.Item as PriceTicker).SymbolInformation.BaseAsset.StartsWith(tb.Text, StringComparison.CurrentCultureIgnoreCase);
        }

        private bool IsPairMatch(SymbolInformation market, string filter)
        {
            if (filter.All(ch => char.IsLetterOrDigit(ch)))
            {
                return market.BaseAsset.StartsWith(tb.Text, StringComparison.CurrentCultureIgnoreCase);
            }
            else
            {
                try
                {
                    var re = new Regex(filter, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    return re.IsMatch(market.BaseAsset);
                }
                catch (ArgumentException e)
                {
                    return true;
                }
            }
        }

        private bool FilterByPrice(PriceTicker ticker, string property, string filter)
        {
            var opers = new char[] { '<', '>', '=' };
            // filter could be:
            // LastPrice  = 0.45
            // LastPrice <  0.45
            // LastPrice >= 0.45
            if (string.IsNullOrWhiteSpace(filter) || string.IsNullOrWhiteSpace(property) || ticker == null)
                return true;
            var oper = new string(filter.Where(ch => opers.Contains(ch)).ToArray());
            if (string.IsNullOrWhiteSpace(oper))
                oper = "=";
            decimal filterValue = decimal.Zero;
            if (!decimal.TryParse(filter.Replace(oper, string.Empty), out filterValue))
                return true;
            var value = GetPropertyValue<decimal>(ticker, property);
            switch (oper)
            {
                case ">=":
                    return value >= filterValue;
                case "<=":
                    return value <= filterValue;
                case ">":
                    return value > filterValue;
                case "<":
                    return value < filterValue;
                case "=":
                    return value == filterValue;
                default:
                    return true;
            }
        }

        private T GetPropertyValue<T>(object obj, string property)
        {
            var props = property.Split('.');
            foreach(var p in props.Take(props.Length-1))
            {
                obj = obj.GetType().GetProperty(p).GetValue(obj);
            }
            return (T) obj.GetType().GetProperty(props.Last()).GetValue(obj);
        }

        private ICollectionView MarketSummaries => (Resources["csvMarketSummaries"] as CollectionViewSource).View;

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                var str = string.Join(Environment.NewLine, grdMarketSummaries.Columns.Select(x => x.ActualWidth));
                MessageBox.Show(str, "DEBUG");
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            MarketSummaries.Refresh();
        }

        private void chkAllowMargin_Click(object sender, RoutedEventArgs e)
        {
            //MarketSummaries.Refresh();
        }
    }

}


