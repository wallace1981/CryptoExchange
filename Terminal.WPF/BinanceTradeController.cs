using System;
using System.Linq;
using System.Threading.Tasks;

namespace Exchange.Net
{
    public partial class BinanceViewModel
    {
        protected async override Task<Order> GetOrder(Order order)
        {
            // don't fuck the brain!
            if (order.Status == OrderStatus.Filled)
                return order;

            var apiResult = await client.QueryOrderAsync(order.SymbolInformation.Symbol, long.Parse(order.OrderId)).ConfigureAwait(false);
            if (apiResult.Success)
            {
                var x = apiResult.Data;
                var result = new Order(GetSymbolInformation(x.symbol))
                {
                    OrderId = x.orderId.ToString(),
                    Price = x.price,
                    Quantity = x.origQty,
                    Side = x.side == Binance.TradeSide.BUY.ToString() ? TradeSide.Buy : TradeSide.Sell,
                    StopPrice = x.stopPrice,
                    Created = x.time.FromUnixTimestamp(),
                    Updated = x.updateTime.FromUnixTimestamp(),
                    Status = Convert(x.status),
                    Type = x.type
                };
                if (result.Updated > order.Updated)
                {
                    result.Fills = await GetOrderTrades(result);
                }
                return result;
            }
            else
            {
                throw new ApiException(apiResult.Error);
            }
        }

        protected async override Task<OrderTrade[]> GetOrderTrades(Order order)
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
                }).ToArray();
            }
            else
            {
                throw new ApiException(result.Error);
            }
        }

        protected override async Task<Order> ExecuteBuy(TradeTask tt)
        {
            var result = await client.PlaceOrderAsync(
                tt.Symbol,
                Binance.TradeSide.BUY,
                tt.Buy.OrderType == OrderType.LIMIT ? Binance.OrderType.LIMIT : Binance.OrderType.MARKET,
                tt.Buy.Quantity,
                tt.Buy.OrderType == OrderType.LIMIT ? tt.Buy.Price : default(decimal?)
            );
            if (result.Success)
            {
                var x = result.Data;
                var si = GetSymbolInformation(x.symbol);
                tt.Buy.ExchangeOrder = new Order(si)
                {
                    OrderId = x.orderId.ToString(),
                    Price = x.price,
                    Quantity = x.origQty,
                    ExecutedQuantity = x.executedQty,
                    Side = x.side == "BUY" ? TradeSide.Buy : TradeSide.Sell,
                    Created = x.transactTime.FromUnixTimestamp(),
                    Updated = x.transactTime.FromUnixTimestamp(),
                    Status = Convert(x.status),
                    Type = x.type
                };
                if (x.fills?.Length > 0)
                {
                    tt.Buy.ExchangeOrder.Fills = x.fills
                        .Select(t => new OrderTrade(si)
                        {
                            Id = t.tradeId.ToString(),
                            OrderId = x.orderId.ToString(),
                            Comission = t.comission,
                            ComissionAsset = t.comissionAsset,
                            Price = t.price,
                            Quantity = t.qty
                        }).ToArray();
                }
                return tt.Buy.ExchangeOrder;
            }
            else
            {
                throw new ApiException(result.Error);
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
