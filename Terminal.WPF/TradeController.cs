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
                if (task.Status == TradeTaskStatus.Running)
                {
                    if (result)
                        await Lifecycle(task, pt);
                }
                else
                {
                    var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == task);
                    ttvm.Status = BuildStatusString(task.Status);
                }
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
            if (!tt.Jobs.Any())
            {
                // Shutdown the task.
                tt.Status = TradeTaskStatus.Finished;
                tt.Updated = DateTime.Now;
                tt.Events.Add(tt.Updated, "Task finished.");
                TradeTaskViewModel.SerializeModel(tt);
                return;
            }

            bool isLimitOrdersAllowed = true;

            // NOTE: this routine relies on condition that
            // LIMIT orders are always on TOP of MARKET orders in list of jobs.
            foreach (var job in tt.Jobs.ToList())
            {
                if (job.ExchangeOrder != null)
                {
                    switch (job.ExchangeOrder.Status)
                    {
                        case OrderStatus.Cancelled:
                            tt.Status = TradeTaskStatus.Stopped;
                            tt.Updated = DateTime.Now;
                            tt.Events.Add(tt.Updated, "Task stopped due to order was cancelled outside.");
                            TradeTaskViewModel.SerializeModel(tt);
                            return;
                        case OrderStatus.Filled:
                            if (job.Kind == OrderKind.StopLoss)
                            {
                                // Shutdown the task.
                                tt.Status = TradeTaskStatus.Finished;
                                tt.Updated = DateTime.Now;
                                tt.Events.Add(tt.Updated, "Task finished by stop loss.");
                            }
                            else if (job.Kind  == OrderKind.PanicSell)
                            {
                                tt.Status = TradeTaskStatus.PanicSell;
                                tt.Updated = DateTime.Now;
                                tt.Events.Add(tt.Updated, "Task stopped by panic sell.");
                            }
                            tt.FinishedJobs.Enqueue(job);
                            tt.Jobs.Remove(job);
                            TradeTaskViewModel.SerializeModel(tt);
                            return;
                        case OrderStatus.Active:
                        case OrderStatus.PartiallyFilled:
                            // THIS STATE IS ONLY POSSIBLE FOR LIMIT ORDERS OR PANIC SELL!
                            // so allow ONLY MARKET order to check conditions and execute
                            isLimitOrdersAllowed = false;
                            break;
                    }
                }
                else
                {
                    bool applicable = false;
                    bool isLimitOrder = (job.Type == OrderType.LIMIT || job.Type == OrderType.STOP_LIMIT);
                    // check condition
                    if (job.Kind == OrderKind.Buy)
                    {
                        applicable = (tt.StopLoss.Price < ticker.Ask) && (ticker.Ask < tt.TakeProfit.First().Price);
                        if (!applicable)
                        {
                            var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == tt);
                            ttvm.Status = "Цена вне зоны покупки";
                        }
                    }
                    else if (job.Kind == OrderKind.PanicSell)
                        applicable = true;
                    else
                        applicable = (job.Type != OrderType.MARKET && job.Type != OrderType.TRAILING) || (ticker.Bid >= job.Price); // NOTE: last part is wrong -- could be market STOP_LOSS, so ticker.Bid <= job.Price is valid for this case

                    // !!!ALLOW ONLY 1 LIMIT ORDER AT A TIME FOR NOW!!!
                    if (applicable && (isLimitOrdersAllowed || !isLimitOrder) && (tt.Qty > 0 || job.Side == TradeSide.Buy))
                    {
                        var result = await CancellAllOrders(tt);
                        if (job.Side == TradeSide.Sell)
                        {
                            // Do not sell more then you have :D
                            job.Quantity = Math.Min(tt.Qty, job.Quantity);
                        }

                        job.ExchangeOrder = await ExecuteOrder(job);
                        job.OrderId = job.ExchangeOrder.OrderId;
                        tt.Updated = DateTime.Now;
                        tt.Events.Add(tt.Updated, $"Created order {job.OrderId}.");
                        TradeTaskViewModel.SerializeModel(tt);
                        job.ExchangeOrder.Fills.ForEach(x => tt.RegisterTrade(x));

                        var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == tt);
                        ttvm.Status = BuildStatusString(tt, job);
                    }
                    return; // exit for
                }
            }
        }

        private async Task<bool> GetOrders(TradeTask tt)
        {
            foreach (var job in tt.FinishedJobs.Where(IsActiveJob))
            {
                var result = await GetOrder(job);
                job.ExchangeOrder = result;
                result.Fills.ForEach(x => tt.RegisterTrade(x));
            }

            if (tt.LastGetOrder.AddSeconds(5) <= DateTime.Now)
            {
                foreach (var job in tt.Jobs.Where(IsActiveJob))
                {
                    var result = await GetOrder(job);
                    if (job.ExchangeOrder == null || result.Updated > job.ExchangeOrder?.Updated)
                    {
                        job.ExchangeOrder = result;
                        tt.Updated = DateTime.Now;
                        TradeTaskViewModel.SerializeModel(tt);
                        var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == tt);
                        ttvm.Status = BuildStatusString(tt, job);
                        result.Fills.ForEach(x => tt.RegisterTrade(x));
                    }
                    tt.LastGetOrder = DateTime.Now;
                }
            }
            return true;
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
                tt.Jobs.ForEach(x => tt.FinishedJobs.Enqueue(x));
                tt.Jobs.Clear();
                if (tt.Qty > 0)
                {
                    var sellJob = new OrderTask()
                    {
                        Symbol = tt.Symbol,
                        Type = OrderType.MARKET,
                        Kind = OrderKind.PanicSell,
                        Side = TradeSide.Sell,
                        Quantity = tt.Qty
                    };
                    tt.Jobs.Insert(0, sellJob);
                }
                TradeTaskViewModel.SerializeModel(tt);
                return result;
            }
            finally
            {
                tt.locker.Release();
            }
        }

        // Покупка 123 XRP по 0.00123 выставлена
        // Стоп-лосс 123 XRP по 0.0100 выставлен
        // Тейк-профит 60 XRP по 0.00140 выставлен
        // Завершено
        // Остановлено
        // Паник селл

        private string BuildStatusString(TradeTask tt, OrderTask job)
        {
            string action = "Остановлено";
            switch (job.Kind)
            {
                case OrderKind.Buy:
                    action = "Покупка";
                    break;
                case OrderKind.StopLoss:
                    action = "Стоп лосс";
                    break;
                case OrderKind.TakeProfit:
                    action = "Тейк профит";
                    break;
                case OrderKind.PanicSell:
                    action = "Паник селл";
                    break;
            }
            var si = GetSymbolInformation(tt.Symbol);
            return $"{action} {job.Quantity} {si.BaseAsset} по {job.Price} {si.QuoteAsset} {OrderStatusToDisplayStringRus(job.ExchangeOrder.Status)}";
        }

        private string BuildStatusString(TradeTaskStatus status)
        {
            string statusString = "Остановлено";
            switch (status)
            {
                case TradeTaskStatus.Finished:
                    statusString = "Завершено";
                    break;
                case TradeTaskStatus.Stopped:
                    statusString = "Остановлено";
                    break;
                case TradeTaskStatus.PanicSell:
                    statusString = "Паник селл";
                    break;
            }
            return statusString;
        }
        //protected virtual Task<Order> GetOrder(Order order)
        //{
        //    return Task.FromResult<Order>(null);
        //}

        protected virtual Task<Order> GetOrder(OrderTask job)
        {
            return Task.FromResult<Order>(null);
        }

        protected virtual Task<IReadOnlyCollection<OrderTrade>> GetOrderTrades(Order order)
        {
            return Task.FromResult(new List<OrderTrade>() as IReadOnlyCollection<OrderTrade>);
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
                .Transform(x => new TradeTaskViewModel(GetSIWithTicker(x.Symbol), x))
                .ObserveOnDispatcher()
                .Bind(out _data)
                .Subscribe();
        }
        private SymbolInformation GetSIWithTicker(string symbol)
        {
            var result = GetSymbolInformation(symbol);
            result.PriceTicker = GetPriceTicker(symbol);
            return result;
        }

        protected virtual Task<bool> CancelOrder(OrderTask order)
        {
            return Task.FromResult(false);
        }

        protected virtual Task<Order> ExecuteOrder(OrderTask tt)
        {
            return Task.FromResult<Order>(null);
        }

        private async Task<bool> CancellAllOrders(TradeTask tt)
        {
            var result = false;
            foreach (var job in tt.Jobs.Where(IsActiveJob).ToList())
            {
                result = await CancelOrder(job);
                tt.FinishedJobs.Enqueue(job);
                tt.Jobs.Remove(job);
                TradeTaskViewModel.SerializeModel(tt);
            }
            return result;
        }

        SourceList<TradeTask> tradeTasks = new SourceList<TradeTask>();
        ReadOnlyObservableCollection<TradeTaskViewModel> _data;
        IDisposable _cleanup;
    }
}
