using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Exchange.Net
{
    public partial class BinanceViewModel
    {
        protected async override Task<Order> GetOrder(OrderTask job)
        {
            // don't fuck the brain!
            if (job.ExchangeOrder?.Status == OrderStatus.Filled)
                return job.ExchangeOrder;

            var apiResult = await client.QueryOrderAsync(job.Symbol, long.Parse(job.OrderId)).ConfigureAwait(false);
            if (apiResult.Success)
            {
                var result = Convert(apiResult.Data);
                if (job.ExchangeOrder == null || result.Updated > job.ExchangeOrder.Updated)
                {
                    result.Fills.AddRange(await GetOrderTrades(result));
                }
                return result;
            }
            else
            {
                throw new ApiException(apiResult.Error);
            }
        }


        protected async override Task<IReadOnlyCollection<OrderTrade>> GetOrderTrades(Order order)
        {
            var result = await client.GetAccountTradesAsync(order.SymbolInformation.Symbol, start: order.Created.ToUnixTimestamp()).ConfigureAwait(false);
            if (result.Success)
            {
                return result.Data
                    .Where(x => x.orderId.ToString() == order.OrderId)
                    .Select(x => new OrderTrade(order.SymbolInformation)
                {
                    Id = x.id.ToString(),
                    OrderId = x.orderId.ToString(),
                    Price = x.price,
                    Quantity = x.qty,
                    Timestamp = x.time.FromUnixTimestamp(),
                    Side = x.isBuyer ? TradeSide.Buy : TradeSide.Sell
                }).ToList();
            }
            else
            {
                throw new ApiException(result.Error);
            }
        }

        protected override async Task<Order> ExecuteOrder(OrderTask job)
        {
            var result = await client.PlaceOrderAsync(
                job.Symbol,
                job.Side == TradeSide.Buy ? Binance.TradeSide.BUY : Binance.TradeSide.SELL,
                Convert(job.Type),
                job.Quantity,
                job.Type == OrderType.LIMIT || job.Type == OrderType.STOP_LIMIT ? job.Price : default(decimal?),
                job.Type == OrderType.STOP_LIMIT ? job.Price : default(decimal?)
            ).ConfigureAwait(false);
            if (result.Success)
            {
                return Convert(result.Data);
            }
            else
            {
                throw new ApiException(result.Error);
            }
        }

        protected async override Task<bool> CancelOrder(OrderTask order)
        {
            var result = await client.CancelOrderAsync(order.Symbol, long.Parse(order.ExchangeOrder.OrderId));
            return result.Success;
        }

        private static Binance.OrderType Convert(OrderType x)
        {
            switch (x)
            {
                case OrderType.LIMIT:
                    return Binance.OrderType.LIMIT;
                case OrderType.STOP_LIMIT:
                    return Binance.OrderType.STOP_LOSS_LIMIT;
                default:
                    return Binance.OrderType.MARKET;
            }
        }

        private static OrderStatus Convert(string x)
        {
            var status = Enum.Parse(typeof(Binance.OrderStatus), x, true);
            switch (status)
            {
                case Binance.OrderStatus.CANCELED:
                    return OrderStatus.Cancelled;
                case Binance.OrderStatus.EXPIRED:
                case Binance.OrderStatus.REJECTED:
                    return OrderStatus.Rejected;
                case Binance.OrderStatus.NEW:
                    return OrderStatus.Active;
                case Binance.OrderStatus.PARTIALLY_FILLED:
                    return OrderStatus.PartiallyFilled;
                case Binance.OrderStatus.FILLED:
                    return OrderStatus.Filled;
                default:
                    return OrderStatus.Rejected;
            }
        }
    }
}
