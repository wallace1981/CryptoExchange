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

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            //MessageBox.Show(e.Exception.ToString());
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


    public class OrderSideToBrushConverter : IValueConverter
    {
        public Brush BuyOrderBrush { get; set; }
        public Brush SellOrderBrush { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value)
            {
                case TradeSide.Buy:
                    return BuyOrderBrush;
                case TradeSide.Sell:
                    return SellOrderBrush;
                default:
                    return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class OrderSideToColorConverter : IValueConverter
    {
        public Color BuyOrderColor { get; set; }
        public Color SellOrderColor { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value)
            {
                case TradeSide.Buy:
                    return BuyOrderColor;
                case TradeSide.Sell:
                    return SellOrderColor;
                default:
                    return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PriceDiffToBrushConverter : IValueConverter
    {
        public Brush DefaultBrush { get; set; } = new SolidColorBrush(Colors.Black);
        public Brush PositiveDiffBrush { get; set; }
        public Brush NegativeDiffBrush { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value?.GetType() != typeof (decimal))
                return parameter ?? DefaultBrush;
            if ((decimal)value == decimal.Zero)
                return parameter ?? DefaultBrush;
            return ((decimal)value < decimal.Zero) ? NegativeDiffBrush : PositiveDiffBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PercentToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var percent = (decimal)value;
            return new GridLength((parameter as string == "-") ? 100.0 - (double)percent : (double)percent, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class DoubleToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var dbl = Math.Abs((double)value);
            return new GridLength(dbl, parameter as string == "*" ? GridUnitType.Star : GridUnitType.Pixel);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class SymbolToBrushConverter : IValueConverter
    {
        public Brush DefaultBrush { get; set; } = new SolidColorBrush(Colors.Black);
        public Brush DisabledBrush { get; set; } = new SolidColorBrush(Colors.Red);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value?.GetType() != typeof(SymbolInformation))
                return DefaultBrush;
            var si = value as SymbolInformation;
            if (si.Status == "BREAK")
                return DisabledBrush;
            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EqualityToVisibilityConverter : IMultiValueConverter
    {
        public object EqualValue { get; set; }
        public object NotEqualValue { get; set; }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null) return NotEqualValue;
            if (values.Length != 2) return NotEqualValue;
            if (values[0].Equals(values[1])) return EqualValue;
            return NotEqualValue;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class DecimalValueFormatConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var v = values[0];
            var fmt = values[1] as string;
            return v.ToString();
            if (v?.GetType() != typeof(decimal) && v?.GetType() != typeof(decimal?))
                return v;
            return ((decimal)v).ToString(fmt);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class BalanceToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((decimal)value > 0.001m)
                return Visibility.Visible;
            else
                return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class PercentToGradientConverter : IValueConverter
    {
        public Color Color { get; set; } = Colors.Transparent;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var maxIntensity = double.Parse(parameter?.ToString());
            var percentage = (double)(decimal)value;
            var newBrush = new SolidColorBrush(Color) { Opacity = percentage / 100.0 };
            if (newBrush.CanFreeze)
                newBrush.Freeze();
            return newBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NullableDecimalToDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var src = value as decimal?;
            return src == null ? null : new double?((double)src.Value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var src = value as double?;
            return src == null ? null : new decimal?((decimal)src.Value);
        }
    }

    public class MultiplierConvertor : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (decimal)value * decimal.Parse(parameter?.ToString());
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class PercentToPointConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var width = values[0];
            var prcnt = values[1];
            var shift = values[2];
            if (width.GetType() != typeof(double) && prcnt.GetType() != typeof(double))
                return 0.0;
            return (double)width * (double)prcnt - (double)shift;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
