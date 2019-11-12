using Exchange.Net;
using Microsoft.Win32;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Telerik.Windows.Controls;

namespace Terminal.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Dispatcher.UnhandledException += Dispatcher_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            SetIEFeature(IE_FEATURE_BROWSER_EMULATION, 0x2AF8);
            //CrystalPalette.Palette.AccentPressedColor = Colors.Bisque;
            //CrystalPalette.Palette.MouseOverHighColor = Colors.Bisque;
            //CrystalPalette.Palette.FontFamily = new FontFamily("Segoe UI");
            //CrystalPalette.Palette.FontFamily = Application.Current.Resources["RobotoCondensed"] as FontFamily;
            //CrystalPalette.Palette.FontSize = 12;
            //CrystalPalette.Palette.CornerRadius = new CornerRadius(0);

            //FluentPalette.Palette.FontFamily = Application.Current.Resources["RobotoCondensed"] as FontFamily;
            //FluentPalette.Palette.FontSize = 13;
            
            //Office2013Palette.Palette.FontSizeL = 12;
            //Office2013Palette.Palette.FontSizeXL = 12;
            //Office2013Palette.Palette.FontFamily = new FontFamily("Roboto");

            Locator.CurrentMutable.Register(() => new ExchangeView(), typeof(IViewFor<BinanceViewModel>));
            Locator.CurrentMutable.Register(() => new ExchangeView(), typeof(IViewFor<DsxViewModel>));
            Locator.CurrentMutable.Register(() => new ExchangeView(), typeof(IViewFor<BittrexViewModel>));
            //Locator.CurrentMutable.Register(() => new ExchangeView(), typeof(IViewFor<HuobiViewModel>));
            //Locator.CurrentMutable.Register(() => new ExchangeView(), typeof(IViewFor<CryptopiaViewModel>));
            //Locator.CurrentMutable.Register(() => new ExchangeView(), typeof(IViewFor<HitBtcViewModel>));
            //Locator.CurrentMutable.Register(() => new ExchangeView(), typeof(IViewFor<OKexViewModel>));
            Locator.CurrentMutable.Register(() => new CreateTrade(), typeof(IViewFor<TradeTaskViewModel>));
            Locator.CurrentMutable.Register(() => new CreateExchangeAccount(), typeof(IViewFor<CreateExchangeAccountViewModel>));

            //orig:ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls | SecurityProtocolType.Ssl3;

            TelegramNotifier.Initialize();
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.ToString());
        }

        private void Dispatcher_UnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.ToString());
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.ExceptionObject.ToString());
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.ToString());
            Debug.Print(e.Exception.Message);
            e.Handled = true;
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
        }

        static void SetIEFeature(string feature, uint value)
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                var regFeatureControl = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Internet Explorer\Main\FeatureControl");
                var regFeatureKey = regFeatureControl.CreateSubKey(feature);
                regFeatureKey.SetValue(proc.MainModule.ModuleName, value, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
            }
        }

        const string IE_FEATURE_BROWSER_EMULATION = "FEATURE_BROWSER_EMULATION";
    }


}
