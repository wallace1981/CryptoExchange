using System;
using Exchange.Net;

namespace CryptoExchange
{
    [System.ComponentModel.ToolboxItem(true)]
    public partial class HuobiView : ExchangeView
    {
        public HuobiView()
        {
            this.Build();
            Initialize(new HuobiViewModel());
        }
    }
}
