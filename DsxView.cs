using System;
using Exchange.Net;

namespace CryptoExchange
{
    [System.ComponentModel.ToolboxItem(true)]
    public partial class DsxView : ExchangeView
    {
        public DsxView()
        {
            this.Build();
            Initialize(new DsxViewModel());
        }
    }
}
