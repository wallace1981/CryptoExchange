using DynamicData;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Exchange.Net
{
    internal enum TradeTaskState
    {
        IDLE,
        EXECUTE_BUY,
        EXECUTE_SL,
        EXECUTE_TP,
        EXECUTE_PANIC_SL,
        EXECUTE_PANIC_TP,
        STOP
    }

    public partial class ExchangeViewModel
    {
        public ReadOnlyObservableCollection<TradeTaskViewModel> TradeTasksList => _data;

        public ReactiveCommand<TradeTask, Unit> TradeTaskLifecycle { get; private set; }

        private async Task DoLifecycleImpl(TradeTask task)
        {
            if (task == null || task.IsBusy) return;

            try
            {
                await task.locker.WaitAsync();
                var pt = GetPriceTicker(task.Symbol);
                var result = await GetOrders(task);
                if (result)
                    await Lifecycle(task, pt);
            }
            catch (ApiException ex)
            {
                var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == task);
                if (ttvm != null)
                {
                    ttvm.LastError = ex.Error;
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                task.locker.Release();
            }
        }

        // before running the lifecycle, update ALL task orders
        // with status = ACTIVE | PARTIALLY_FILLED.

        private async Task Lifecycle(TradeTask tt, PriceTicker ticker)
        {
            if (!tt.Jobs.Any() && tt.Current == null)
            {
                // Shutdown the task.
                return;
            }

            if (tt.Current != null)
            {
                Debug.Assert(tt.Current.ExchangeOrder != null);
                switch (tt.Current.ExchangeOrder.Status)
                {
                    case OrderStatus.Cancelled:
                        if (tt.Current.OrderKind != OrderKind.StopLoss)
                        {
                            // Shutdown the task.
                            return;
                        }
                        else
                        {
                            // Place stop order again
                            tt.Current.OrderId = null;
                            tt.Current.ExchangeOrder = null;
                            tt.Jobs.Enqueue(tt.Current);
                            tt.Current = null;
                            TradeTaskViewModel.SerializeModel(tt);
                            break;
                        }
                    case OrderStatus.Filled:
                        if (tt.Current.OrderKind == OrderKind.StopLoss)
                        {
                            // Shutdown the task.
                        }
                        tt.FinishedJobs.Enqueue(tt.Current);
                        tt.Current = null;
                        TradeTaskViewModel.SerializeModel(tt);
                        return;
                    case OrderStatus.Active:
                    case OrderStatus.PartiallyFilled:
                        if (tt.Current.OrderKind != OrderKind.StopLoss)
                        {
                            // do not allow to procceed if any other than SL is placed.
                            return;
                        }
                        break;
                }
            }

            if (tt.Jobs.Any())
            {
                var job = tt.Jobs.Peek();

                bool applicable = false;
                // check condition
                if (job.Side == TradeSide.Buy)
                    applicable = (tt.StopLoss.Price < ticker.Ask) && (ticker.Ask < tt.TakeProfit.First().Price);
                else
                    applicable = (job.OrderType != OrderType.MARKET && job.OrderType != OrderType.TRAILING) || (ticker.Bid >= job.Price);

                if (applicable)
                {
                    if (tt.Current?.ExchangeOrder != null)
                    {
                        var result = await CancelOrder(tt.Current);
                        if (result)
                        {
                            tt.Current = null;
                            tt.Updated = DateTime.Now;
                            TradeTaskViewModel.SerializeModel(tt);
                        }
                    }

                    job.ExchangeOrder = await ExecuteOrder(job);
                    job.OrderId = job.ExchangeOrder.OrderId;
                    tt.Current = job;
                    tt.Jobs.Dequeue();
                    tt.Updated = DateTime.Now;
                    TradeTaskViewModel.SerializeModel(tt);
                    job.ExchangeOrder.Fills.ToList().ForEach(x => tt.RegisterTrade(x));

                    var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == tt);
                    if (job.OrderKind == OrderKind.StopLoss)
                        ttvm.Status = $"Стоп-лосс ордер {OrderStatusToDisplayStringRus(job.ExchangeOrder.Status)}";
                    else
                        ttvm.Status = $"Ордер на {(job.Side == TradeSide.Buy ? "покупку" : "продажу")} {OrderStatusToDisplayStringRus(job.ExchangeOrder.Status)}";

                }
            }
        }

        private string OrderStatusToDisplayStringRus(OrderStatus status)
        {
            switch (status)
            {
                case OrderStatus.Filled:
                    return "исполнен";
                case OrderStatus.PartiallyFilled:
                    return "частично исполнен";
                case OrderStatus.Cancelled:
                    return "отменен";
                case OrderStatus.Rejected:
                    return "отвергнут";
                case OrderStatus.Active:
                    return "выставлен";
                default:
                    return "статус неизвестен";
            }
        }

        protected async Task<bool> PanicSell(TradeTask tt)
        {
            try
            {
                await tt.locker.WaitAsync();
                // there could be BUY or SL orders running. cancell all them first.
                var result = await CancellAllOrders(tt);
                var sellJob = new OrderTask()
                {
                    Symbol = tt.Symbol,
                    OrderType = OrderType.MARKET,
                    OrderKind = OrderKind.TakeProfit,
                    Side = TradeSide.Sell,
                    Quantity = tt.Qty
                };
                //var order = await ExecuteOrder(sellJob);
                tt.Jobs.Clear();
                tt.Jobs.Enqueue(sellJob);
                TradeTaskViewModel.SerializeModel(tt);
                return result;
            }
            finally
            {
                tt.locker.Release();
            }
        }

        private async Task<bool> GetOrders(TradeTask tt)
        {
            foreach (var job in tt.FinishedJobs)
            {
                if (job.ExchangeOrder == null)
                {
                    var result = await GetOrder(job);
                    job.ExchangeOrder = result;
                    result.Fills.ToList().ForEach(x => tt.RegisterTrade(x));
                }
            }

            if (tt.LastGetOrder.AddSeconds(5) <= DateTime.Now)
            {
                var job = tt.Current;
                if (IsActiveJob(job))
                {
                    var result = await GetOrder(job);
                    if (job.ExchangeOrder == null || result.Updated > job.ExchangeOrder?.Updated)
                    {
                        job.ExchangeOrder = result;
                        tt.Updated = DateTime.Now;
                        TradeTaskViewModel.SerializeModel(tt);
                        var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == tt);
                        if (job.OrderKind == OrderKind.StopLoss)
                            ttvm.Status = $"Стоп-лосс ордер {OrderStatusToDisplayStringRus(job.ExchangeOrder.Status)}";
                        else
                            ttvm.Status = $"Ордер на {(job.Side == TradeSide.Buy ? "покупку" : "продажу")} {OrderStatusToDisplayStringRus(job.ExchangeOrder.Status)}";
                        result.Fills.ToList().ForEach(x => tt.RegisterTrade(x));
                    }
                    tt.LastGetOrder = DateTime.Now;
                }
            }
            return true;
        }

        //protected virtual Task<Order> GetOrder(Order order)
        //{
        //    return Task.FromResult<Order>(null);
        //}

        protected virtual Task<Order> GetOrder(OrderTask job)
        {
            return Task.FromResult<Order>(null);
        }

        protected virtual Task<OrderTrade[]> GetOrderTrades(Order order)
        {
            return Task.FromResult<OrderTrade[]>(null);
        }

        private static bool IsActiveJob(OrderTask x)
        {
            if (x == null) return false;
            return (x.ExchangeOrder?.Status == OrderStatus.Active || x.ExchangeOrder?.Status == OrderStatus.PartiallyFilled) || (x.OrderId != null && x.ExchangeOrder == null);
        }

        public void InitializeTradeController()
        {
            TradeTaskLifecycle =  ReactiveCommand.CreateFromTask<TradeTask>(DoLifecycleImpl);
            _cleanup = tradeTasks
                .Connect()
                .Transform(x => new TradeTaskViewModel(GetSymbolInformation(x.Symbol), x))
                .ObserveOnDispatcher()
                .Bind(out _data)
                .Subscribe();
        }


        protected virtual Task<bool> CancelOrder(OrderTask order)
        {
            return Task.FromResult(false);
        }

        protected virtual Task<Order> ExecuteOrder(OrderTask tt)
        {
            return Task.FromResult<Order>(null);
        }

        protected async Task<bool> CancellAllOrders(TradeTask tt)
        {
            var result = false;
            var job = tt.Current;
            if (IsActiveJob(job))
            {
                result = await CancelOrder(job);
                if (job.ExchangeOrder.Fills?.Length > 0)
                {
                    // NOTE: if this order HAS any fills, put it into history.
                    tt.FinishedJobs.Enqueue(job);
                }
                tt.Current = null;
                TradeTaskViewModel.SerializeModel(tt);
            }
            return result;
        }

        SourceList<TradeTask> tradeTasks = new SourceList<TradeTask>();
        ReadOnlyObservableCollection<TradeTaskViewModel> _data;
        IDisposable _cleanup;
    }
}
