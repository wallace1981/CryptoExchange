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
using Telerik.Windows.Controls;

namespace Terminal.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();
            //var vm = new BinanceViewModel();
            //DataContext = vm;


            tab.ItemsSource = new object[] { new BinanceViewModel(), new BittrexViewModel(), new DsxViewModel() };//, new CryptopiaViewModel(), new HuobiViewModel(), new HitBtcViewModel(), new OKexViewModel() };

            //var ctl = new DsxTerminalView();
            //tab.Items.Add(new RadTabItem { Header = "DSX", Content = ctl });
            //ctl.DataContext = new DsxTerminalViewModel();
        }

        public void Dispose()
        {
            tab.ItemsSource = null;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Dispose();
            Application.Current.Shutdown();
        }
    }

    }


