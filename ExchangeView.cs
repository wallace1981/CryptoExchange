using System;
using Exchange.Net;

namespace CryptoExchange
{
    public partial class ExchangeView : Gtk.Bin
    {
        public ExchangeView()
        {
            this.Build();
        }

        public virtual void Initialize(ExchangeViewModel vm)
        {
            viewModel = vm;
            marketsummary1.Initialize(viewModel);
            publictrades1.Initialize(viewModel);
            orderbook1.Initialize(viewModel);
            privatetrades1.Initialize(viewModel);
            transfersDeposits.Initialize(viewModel, TransferType.Deposit);
            transfersWithdrawals.Initialize(viewModel, TransferType.Withdrawal);
            funds1.Initialize(viewModel);
        }

        public ExchangeViewModel viewModel { get; private set; }
    }
}
