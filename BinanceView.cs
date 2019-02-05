using System;
using Exchange.Net;

namespace CryptoExchange
{
    [System.ComponentModel.ToolboxItem(true)]
    public partial class BinanceView : ExchangeView
    {
        public BinanceView()
        {
            this.Build();
            Initialize(new BinanceViewModel());
        }
    }
}
