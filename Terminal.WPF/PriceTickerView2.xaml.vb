Imports Exchange.Net
    Public Class PriceTickerView
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
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            fdQuote.Value = (sender as Telerik.Windows.Controls.RadToggleButton).CommandParameter;
        }

        public ExchangeViewModel ViewModel
        {
            get { return (ExchangeViewModel)DataContext; }
        }
End Class

