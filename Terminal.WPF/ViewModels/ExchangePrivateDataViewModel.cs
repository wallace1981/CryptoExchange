using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DynamicData;
using Exchange.Net;

namespace Terminal.WPF.ViewModels
{
    public class ExchangePrivateDataViewModel
    {
        // NOTE: this should have it's own API client with account specific auth info.

        public ExchangePrivateDataViewModel(ExchangeApiCore client)
        {
            Client = client;
        }

        public SourceCache<Transfer, string> Deposits { get; }
        public SourceCache<Transfer, string> Withdrawals { get; }
        public SourceCache<Order, string> OpenOrders { get; }
        public SourceCache<Order, string> OrdersHistory { get; }
        public BalanceManager BalanceManager { get; }

        public ExchangeApiCore Client { get; }
    }
}
