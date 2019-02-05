using System;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>
    /// Interaction logic for TradingViewChart.xaml
    /// </summary>
    public partial class TradingViewChart : UserControl
    {
        public TradingViewChart()
        {
            InitializeComponent();
        }

        public string Symbol 
        {
            get => GetValue(SymbolProperty) as string;
            set => SetValue(SymbolProperty, value);
        }

        public string Exchange
        {
            get => GetValue(ExchangeProperty) as string;
            set => SetValue(ExchangeProperty, value);
        }

        private static void OnExchangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue != null)
            {
                var exchange = e.NewValue.ToString();
                var view = d as TradingViewChart;
                view.wb.NavigateToString(view.LoadHtml(view.Symbol, exchange, "30"));
            }
        }

        private static void OnSymbolChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue != null)
            {
                var symbol = e.NewValue.ToString();
                var view = d as TradingViewChart;
                view.wb.NavigateToString(view.LoadHtml(symbol, view.Exchange, "30"));
            }
        }

        private string LoadHtml(string symbol, string exchange, string period)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return "about:blank";
            if (string.IsNullOrWhiteSpace(exchange)) return "about:blank";
            try
            {
                var html = File.ReadAllText("Chart.html");
                return html.Replace("{SYMBOL}", symbol.ToUpper()).Replace("{EXCHANGE}", exchange.ToUpper());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return "about:blank";
            }
        }

        private static readonly DependencyProperty SymbolProperty = DependencyProperty.Register("Symbol",
                           typeof(string), typeof(TradingViewChart), new FrameworkPropertyMetadata(null, OnSymbolChanged));

        private static readonly DependencyProperty ExchangeProperty = DependencyProperty.Register("Exchange",
                           typeof(string), typeof(TradingViewChart), new FrameworkPropertyMetadata(null, OnExchangeChanged));
    }
}
