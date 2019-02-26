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
        public IObservableList<TradeTask> TradeTasks1 => tradeTasks.AsObservableList();
        public ReadOnlyObservableCollection<TradeTaskViewModel> TradeTasksList => _data;

        public ReactiveCommand<TradeTask, Unit> DoLifecycle { get; private set; }

        private async Task DoLifecycleImpl(TradeTask task)
        {
            if (task == null) return;

            try
            {
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
        }

        // before running the lifecycle, update ALL task orders
        // with status = ACTIVE | PARTIALLY_FILLED.

        private async Task Lifecycle(TradeTask tt, PriceTicker ticker)
        {
            if (tt.StopLoss.ExchangeOrder != null)
            {
                if (tt.StopLoss.ExchangeOrder.Status == OrderStatus.Filled)
                {
                    tt.FinishedJobs.Enqueue(tt.StopLoss);
                    // Shutdown the task.
                    return;
                }
            }

            if (!tt.Jobs.Any() && tt.StopLoss.ExchangeOrder == null)
            {
                // Shutdown the task.
                return;
            }

            var job = tt.Jobs.Peek();
            if (job.ExchangeOrder != null)
            {
                switch (job.ExchangeOrder.Status)
                {
                    case OrderStatus.Cancelled:
                        // Shutdown the task.
                        return;
                    case OrderStatus.Filled:
                        // Job is done, proceed to next.
                        // Update status, save state.
                        tt.Jobs.Dequeue();
                        tt.FinishedJobs.Enqueue(job);
                        break;
                }
            }
            else
            {
                bool applicable = false;
                // check condition
                if (job.Side == TradeSide.Buy)
                    applicable = (tt.StopLoss.Price < ticker.Ask) && (ticker.Ask < tt.TakeProfit.First().Price);
                else
                    applicable = (job.OrderType != OrderType.MARKET && job.OrderType != OrderType.TRAILING) || (ticker.Bid >= job.Price);

                if (applicable)
                {
                    if (tt.StopLoss.ExchangeOrder != null)
                    {
                        var result = await CancelOrder(tt.StopLoss);
                        if (result)
                        {
                            tt.StopLoss.ExchangeOrder = null;
                            tt.StopLoss.OrderId = null;
                            tt.Updated = DateTime.Now;
                            TradeTaskViewModel.SerializeModel(tt);
                        }
                    }

                    job.ExchangeOrder = await ExecuteOrder(job);
                    job.OrderId = job.ExchangeOrder.OrderId;
                    tt.Updated = DateTime.Now;

                    if (job.OrderKind == OrderKind.StopLoss)
                    {
                        tt.StopLoss.ExchangeOrder = job.ExchangeOrder;
                        tt.StopLoss.OrderId = job.ExchangeOrder.OrderId;
                        tt.Jobs.Dequeue();
                        var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == tt);
                        ttvm.Status = $"Стоп-лосс ордер {OrderStatusToDisplayStringRus(job.ExchangeOrder.Status)}";
                    }
                    else
                    {
                        var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == tt);
                        ttvm.Status = $"Ордер на {(job.Side == TradeSide.Buy ? "покупку" : "продажу")} {OrderStatusToDisplayStringRus(job.ExchangeOrder.Status)}";
                    }

                    TradeTaskViewModel.SerializeModel(tt);
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
            // there could be BUY or SL orders running. cancell all them first.
            var result = await CancellAllOrders(tt);
            var sellJob = new OrderTask()
            {
                Symbol = tt.Symbol,
                OrderType = OrderType.MARKET,
                Side = TradeSide.Sell,
                Quantity = tt.Qty
            };
            var order = await ExecuteOrder(sellJob);
            tt.Jobs.Clear();
            TradeTaskViewModel.SerializeModel(tt);
            return result;
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
                var job = tt.Jobs.Any() ? tt.Jobs.Peek() : null;
                if (IsActiveJob(job))
                {
                    var result = await GetOrder(job);
                    if (job.ExchangeOrder == null || result.Updated > job.ExchangeOrder?.Updated)
                    {
                        job.ExchangeOrder = result;
                        tt.Updated = DateTime.Now;
                        TradeTaskViewModel.SerializeModel(tt);
                        var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == tt);
                        ttvm.Status = $"Ордер на {(job.Side == TradeSide.Buy ? "покупку" : "продажу")} {OrderStatusToDisplayStringRus(job.ExchangeOrder.Status)}";
                        result.Fills.ToList().ForEach(x => tt.RegisterTrade(x));
                    }
                    tt.LastGetOrder = DateTime.Now;
                }
                if (IsActiveJob(tt.StopLoss))
                {
                    var result = await GetOrder(tt.StopLoss);
                    if (tt.StopLoss.ExchangeOrder == null || result.Updated > tt.StopLoss.ExchangeOrder?.Updated)
                    {
                        tt.StopLoss.ExchangeOrder = result;
                        tt.Updated = DateTime.Now;
                        TradeTaskViewModel.SerializeModel(tt);
                        var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == tt);
                        ttvm.Status = $"Стоп-лосс ордер {OrderStatusToDisplayStringRus(tt.StopLoss.ExchangeOrder.Status)}";
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
            DoLifecycle =  ReactiveCommand.CreateFromTask<TradeTask>(DoLifecycleImpl);
            _cleanup = TradeTasks1.Connect().Transform(x => new TradeTaskViewModel(GetSymbolInformation(x.Symbol), x))
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
            var job = tt.Jobs.Any() ? tt.Jobs.Peek() : null;
            if (IsActiveJob(tt.StopLoss))
            {
                result = await CancelOrder(tt.StopLoss);
                tt.StopLoss.ExchangeOrder = null;
                tt.StopLoss.OrderId = null;
            }
            if (IsActiveJob(job))
            {
                result = await CancelOrder(job) && result;
                job.ExchangeOrder = null;
            }
            return result;
        }

        SourceList<TradeTask> tradeTasks = new SourceList<TradeTask>();
        ReadOnlyObservableCollection<TradeTaskViewModel> _data;
        IDisposable _cleanup;
    }
}
