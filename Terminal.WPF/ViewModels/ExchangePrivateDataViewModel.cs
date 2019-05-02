using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using Exchange.Net;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Exchange.Net
{
    public class ExchangeAccount
    {
        // NOTE: this should have it's own API client with account specific auth info.

        public ExchangeAccount(string name, ExchangeApiCore client)
        {
            Name = name;
            Client = client;
            OpenOrders = new SourceCache<Order, string>(x => x.OrderId);
            OrdersHistory = new SourceCache<Order, string>(x => x.OrderId);
            Deposits = new SourceCache<Transfer, string>(x => x.Id);
            Withdrawals = new SourceCache<Transfer, string>(x => x.Id);
            BalanceManager = new BalanceManager();
        }

        public string Name { get; }
        public SourceCache<Transfer, string> Deposits { get; }
        public SourceCache<Transfer, string> Withdrawals { get; }
        public SourceCache<Order, string> OpenOrders { get; }
        public SourceCache<Order, string> OrdersHistory { get; }
        public BalanceManager BalanceManager { get; }   

        public ExchangeApiCore Client { get; }
    }

    public class ExchangeAccountViewModel : ReactiveObject, IDisposable
    {
        public ExchangeAccount Account { get; }

        public ReadOnlyObservableCollection<Transfer> Deposits => deposits;
        public ReadOnlyObservableCollection<Transfer> Withdrawals => withdrawals;
        public ReadOnlyObservableCollection<Order> OpenOrders => openOrders;
        public ReadOnlyObservableCollection<Order> OrdersHistory => ordersHistory;
        public ReadOnlyObservableCollection<Balance> Balances => balances;

        [ObservableAsProperty] public decimal TotalBtc { get; }
        [ObservableAsProperty] public decimal TotalUsd { get; }

        public ExchangeAccountViewModel(ExchangeAccount acc)
        {
            Account = acc;
            Account.Deposits.Connect()
                .Sort(SortExpressionComparer<Transfer>.Descending(x => x.Timestamp))
                .ObserveOnDispatcher()
                .Bind(out deposits)
                .Subscribe()
                .DisposeWith(disposables);

            Account.Withdrawals.Connect()
                .Sort(SortExpressionComparer<Transfer>.Descending(x => x.Timestamp))
                .ObserveOnDispatcher()
                .Bind(out withdrawals)
                .Subscribe()
                .DisposeWith(disposables);

            Account.OpenOrders.Connect()
                .Sort(SortExpressionComparer<Order>.Descending(x => x.OrderId))
                .ObserveOnDispatcher()
                .Bind(out openOrders)
                .Subscribe()
                .DisposeWith(disposables);

            Account.OrdersHistory.Connect()
                .ObserveOnDispatcher()
                .Bind(out ordersHistory)
                .Subscribe()
                .DisposeWith(disposables);

            Account.BalanceManager.Balances.Connect()
                .Sort(SortExpressionComparer<Balance>.Ascending(x => x.Asset))
                .ObserveOnDispatcher()
                .Bind(out balances)
                .Subscribe()
                .DisposeWith(disposables);

            this.WhenAnyValue(x => x.Account.BalanceManager.TotalBtc)
                .ToPropertyEx(this, vm => vm.TotalBtc);
            this.WhenAnyValue(x => x.Account.BalanceManager.TotalUsd)
                .ToPropertyEx(this, vm => vm.TotalUsd);
        }

        private ReadOnlyObservableCollection<Transfer> deposits;
        private ReadOnlyObservableCollection<Transfer> withdrawals;
        private ReadOnlyObservableCollection<Order> openOrders;
        private ReadOnlyObservableCollection<Order> ordersHistory;
        private ReadOnlyObservableCollection<Balance> balances;
        private CompositeDisposable disposables = new CompositeDisposable();

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
