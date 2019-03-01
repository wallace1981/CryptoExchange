using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Exchange.Net;

namespace Terminal.WPF
{
    public class OrderStatusToBrushConverter : IValueConverter
    {
        public Brush DefaultBrush { get; set; } = new SolidColorBrush(Colors.Transparent);
        public Brush FilledBush { get; set; } = new SolidColorBrush(Colors.Green);
        public Brush PartiallyFilledBush { get; set; } = new SolidColorBrush(Colors.LightGreen) { Opacity = 0.5 };
        public Brush CancelledBrush { get; set; } = new SolidColorBrush(Colors.Red);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            OrderStatus status;
            if (Enum.TryParse(value?.ToString(), out status))
            {
                switch (status)
                {
                    case OrderStatus.Cancelled:
                        return CancelledBrush;
                    case OrderStatus.PartiallyFilled:
                        return PartiallyFilledBush;
                    case OrderStatus.Filled:
                        return FilledBush;
                }
            }
            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
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
            if (value?.GetType() != typeof(decimal))
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
            //if (v?.GetType() != typeof(decimal) && v?.GetType() != typeof(decimal?))
            //    return v;
            //return ((decimal)v).ToString(fmt);
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