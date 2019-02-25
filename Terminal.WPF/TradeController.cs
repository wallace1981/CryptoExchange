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
            try
            {
                var pt = GetPriceTicker(task.Symbol);
                var result = await GetOrders(task);
                if (result)
                    await Lifecycle(task, pt.LastPrice.GetValueOrDefault());
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

        private async Task Lifecycle(TradeTask tt, decimal priceLevel)
        {
            var state = GetState(tt, priceLevel);
            switch (state)
            {
                case TradeTaskState.EXECUTE_BUY:
                    {
                        var result = await ExecuteBuy(tt);
                        var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == tt);
                        ttvm.Status = $"Ордер на покупку {OrderStatusToDisplayStringRus(result.Status)}";
                        TradeTaskViewModel.SerializeModel(tt); // WRONG PLACE!
                    }
                    break;
                case TradeTaskState.EXECUTE_SL:
                    {
                        var result = await ExecuteStopLoss(tt);
                    }
                    break;
                case TradeTaskState.EXECUTE_TP:
                    {
                        var result = await ExecuteTakeProfit(tt, priceLevel);
                    }
                    break;
                case TradeTaskState.EXECUTE_PANIC_SL:
                    break;
                case TradeTaskState.EXECUTE_PANIC_TP:
                    break;
                case TradeTaskState.STOP:
                    break;
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

        private TradeTaskState GetState(TradeTask tt, decimal priceLevel)
        {
            // NOTE: when cancelling SL, put NULL into tt.StopLoss.ExchangeOrder,
            // so it won't be treated as cancelled from outside.
            if (tt.StopLoss.ExchangeOrder != null)
            {
                switch (tt.Buy.ExchangeOrder.Status)
                {
                    case OrderStatus.Filled:
                        return TradeTaskState.STOP;
                    case OrderStatus.Active:
                        // order placed but no fills yet.
                        break;
                    case OrderStatus.PartiallyFilled:
                        break;
                    default:
                        // order is expired, cancelled or rejected.
                        return TradeTaskState.STOP;
                        // place it again.
                        //if (CanExecuteStopLoss(tt, priceLevel))
                        //    return TradeTaskState.EXECUTE_SL;
                        //break;
                }
                return TradeTaskState.IDLE;
            }

            if (tt.Buy.ExchangeOrder != null)
            {
                switch (tt.Buy.ExchangeOrder.Status)
                {
                    case OrderStatus.Filled:
                        // buy order completed.
                        if (CanExecuteStopLoss(tt, priceLevel))
                            return TradeTaskState.EXECUTE_SL;
                        else if (CanExecuteTakeProfit(tt, priceLevel))
                            return TradeTaskState.EXECUTE_TP;
                        break;
                    case OrderStatus.Active:
                        // order placed but no fills yet.
                        if (priceLevel >= tt.TakeProfit.First().Price)
                            return TradeTaskState.STOP;
                        break;
                    case OrderStatus.PartiallyFilled:
                        // price could raise to TP. if TP is market order, cancel BUY and sell.
                        if (priceLevel >= tt.TakeProfit.First().Price)
                            return TradeTaskState.EXECUTE_PANIC_TP;
                        break;
                    default:
                        // order is expired, cancelled or rejected.
                        // STOP THE TASK. CANCEL ALL PENDING ORDERS.
                        return TradeTaskState.STOP;
                }
                return TradeTaskState.IDLE;
            }
            else
            {
                // put buy order if not already.
                if (CanExecuteBuy(tt, priceLevel))
                    return TradeTaskState.EXECUTE_BUY;
                else
                    return TradeTaskState.STOP;
            }
        }

        private async Task<bool> GetOrders(TradeTask tt)
        {
            if (IsActiveOrder(tt.Buy))
            {
                var result = await GetOrder(tt.Buy.ExchangeOrder);
                if (result.Updated > tt.Buy.ExchangeOrder.Updated)
                {
                    tt.Buy.ExchangeOrder = result;
                    var ttvm = TradeTasksList.SingleOrDefault(x => x.Model == tt);
                    ttvm.Status = $"Ордер на покупку {OrderStatusToDisplayStringRus(result.Status)}";
                    TradeTaskViewModel.SerializeModel(tt); // WRONG PLACE!
                }
            }
            //if (IsActiveOrder(tt.StopLoss))
            //{
            //    var result = await GetOrder(tt.Symbol, tt.StopLoss.ExchangeOrder.OrderId);
            //    if (result != null)
            //        tt.StopLoss.ExchangeOrder = result;
            //    else
            //        return false;
            //}
            return true;
        }

        protected virtual Task<Order> GetOrder(Order order)
        {
            return Task.FromResult<Order>(null);
        }

        protected virtual Task<OrderTrade[]> GetOrderTrades(Order order)
        {
            return Task.FromResult<OrderTrade[]>(null);
        }

        private static bool IsActiveOrder(OrderTask x)
        {
            return (x.ExchangeOrder?.Status == OrderStatus.Active || x.ExchangeOrder?.Status == OrderStatus.PartiallyFilled);
        }

        public void InitializeTradeController()
        {
            DoLifecycle =  ReactiveCommand.CreateFromTask<TradeTask>(DoLifecycleImpl);
            _cleanup = TradeTasks1.Connect().Transform(x => new TradeTaskViewModel(GetSymbolInformation(x.Symbol), x))
                .ObserveOnDispatcher()
                .Bind(out _data)
                .Subscribe();
        }

        private bool CanExecuteTakeProfit(TradeTask tt, decimal priceLevel)
        {
            foreach (var tp in tt.TakeProfit)
            {
                if (tp.ExchangeOrder == null)
                {
                    if (tp.OrderType == OrderType.LIMIT)
                        return true; // always place limit order
                    else if (priceLevel >= tp.Price)
                        return true; // place market order only if market price is below stop price
                }
            }
            return false;
        }


        private Task<bool> PlaceStopLossOrTakeProfits()
        {
            return Task.FromResult(false);
        }

        protected virtual Task<bool> CancelOrder(OrderTask order)
        {
            return Task.FromResult(false);
        }

        private bool CanExecuteBuy(TradeTask tt, decimal priceLevel)
        {
            return priceLevel >= tt.StopLoss.Price && priceLevel <= tt.TakeProfit.First().Price;
        }

        private bool CanExecuteStopLoss(TradeTask tt, decimal priceLevel)
        {
            if (tt.StopLoss.ExchangeOrder == null)
            {
                if (tt.StopLoss.OrderType == OrderType.LIMIT)
                    return true; // always place limit order
                else if (priceLevel <= tt.StopLoss.Price)
                    return true; // place market order only if market price is below stop price
            }
            return false;
        }

        private async Task<bool> ExecuteStopLoss(TradeTask tt)
        {
            var result = await CancellAllTakeProfits(tt).ConfigureAwait(false);
            result = result & await CancelAllBuys(tt).ConfigureAwait(false);
            //result = result & await ExecuteStopLoss(tt.StopLoss);
            return result;
        }

        private async Task<bool> ExecuteTakeProfit(TradeTask tt, decimal priceLevel, bool panic = false)
        {
            // if SL placed, cancel it first.
            if (tt.StopLoss.ExchangeOrder != null)
            {
                var result = await CancelOrder(tt.StopLoss.ExchangeOrder.OrderId);
                if (result)
                    tt.StopLoss.ExchangeOrder = null;
            }
            // For now we will assume that all TP are market orders.
            var tp = tt.TakeProfit.LastOrDefault(x => priceLevel >= x.Price);
            if (tp == null)
                return false;


            //// if BUY is still placed and panic is true, cancel BUY.
            //if (tt.Buy.ExchangeOrder.Status == OrderStatus.PartiallyFilled && panic)
            //{
            //    var result = await CancelOrder(tt.Buy.ExchangeOrder.OrderId);
            //}
            return false;
        }

        protected virtual Task<Order> ExecuteBuy(TradeTask tt)
        {
            return Task.FromResult<Order>(null);
        }

        protected virtual Task<bool> CancellAllTakeProfits(TradeTask tt)
        {
            return Task.FromResult(false);
        }

        protected virtual Task<bool> CancelAllBuys(TradeTask tt)
        {
            return Task.FromResult(true);
        }

        SourceList<TradeTask> tradeTasks = new SourceList<TradeTask>();
        ReadOnlyObservableCollection<TradeTaskViewModel> _data;
        IDisposable _cleanup;
    }
}
