using DynamicData;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Exchange.Net
{
    class DsxViewModel : ExchangeViewModel
    {
        protected string ServerStatus;
        protected string ClientStatus => $"Weight is {client.Weight}, expiration in {client.WeightReset.TotalSeconds} secs.";

        protected void UpdateStatus(string serverStatus, string clientMsg = null)
        {
            Status = string.Join("  ", serverStatus, clientMsg ?? ClientStatus);
            ServerStatus = serverStatus;
        }

        protected override async Task GetExchangeInfoImpl()
        {
            var resultExchangeInfo = await client.GetExchangeInfoAsync().ConfigureAwait(false);
            if (client.IsSigned)
            {
                var resultFees = await client.GetCurrentFeesAsync().ConfigureAwait(false);
                if (resultFees.Success)
                    fees = resultFees.Data;
            }
            if (resultExchangeInfo.Success)
            {
                GetExchangeInfoElapsed = resultExchangeInfo.ElapsedMilliseconds;
                var symbols = resultExchangeInfo.Data.pairs.Select(CreateSymbolInformation);
                ProcessExchangeInfo(symbols);
                var exchangeInfoLastUpdated = resultExchangeInfo.Data.server_time.FromUnixSeconds();
                var status = $"Exchange info updated: {exchangeInfoLastUpdated}; # of trading pairs: {symbols.Count()}";
                if (fees != null)
                {
                    var comms = fees.progressiveCommissions.commissions[fees.progressiveCommissions.indexOfCurrentCommission];
                    status = status + $". Fees: {comms.makerCommission.ToString("P2")}, {comms.takerCommission.ToString("P2")}";
                    status = status + $"; Trading volume: {comms.tradingVolume.ToString("N0")} {fees.progressiveCommissions.currency}.";
                }
                UpdateStatus(status);
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetExchangeInfo: {resultExchangeInfo.Error.ToString()}");
            }
        }

        protected override async Task GetTickersImpl()
        {
            var pairs = string.Join("-", ValidPairs.Select(x => x.Symbol));
            if (pairs.Count() < 1)
                return;
            var resultTickers = await client.GetTickerAsync(pairs).ConfigureAwait(false);
            if (resultTickers.Success)
            {
                GetTickersElapsed = resultTickers.ElapsedMilliseconds;
                var tickers = resultTickers.Data.Select(ToPriceTicker);
                var bars = await Get24hrBars(pairs).ConfigureAwait(false);
                if (bars.Count != 0)
                    tickers = tickers.Join(bars, x => x.Symbol, x => x.pair, Join);
                ProcessPriceTicker(tickers);
                UpdateStatus(ServerStatus);
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetTickers: {resultTickers.Error.ToString()}");
            }
        }

        private PriceTicker Join(PriceTicker ticker, DSX.Bar bar)
        {
            ticker.PriceChange = bar.close - bar.open;
            ticker.PriceChangePercent = CalcChangePercent(bar.close, bar.open);
            return ticker;
        }

        private DateTime next24hrBarsCall = DateTime.MinValue;

        protected async Task<List<DSX.Bar>> Get24hrBars(string pairs)
        {
            var result = new List<DSX.Bar>();
            if (next24hrBarsCall > DateTime.Now)
                return result;
            var resultBars = await client.GetLastBarstAsync(pairs, "h", 24).ConfigureAwait(false);
            if (resultBars.Success)
            {
                var timestamp = DateTime.Now;
                next24hrBarsCall = timestamp.AddHours(1).AddMinutes(-timestamp.Minute);
                foreach (var x in resultBars.Data)
                {
                    if (x.Value.Count == 0)
                    {
                        var bar = new DSX.Bar() { pair = x.Key };
                        result.Add(bar);
                    }
                    else
                    {
                        var bar = x.Value.First();
                        bar.open = x.Value.Last().open;
                        bar.pair = x.Key;
                        result.Add(bar);
                    }
                }
            }
            return result;
        }

        protected override async Task GetTradesImpl()
        {
            if (CurrentSymbol == null)
                return;
            var si = CurrentSymbolInformation;
            var resultTrades = await client.GetTradesAsync(si.Symbol, TradesMaxItemCount).ConfigureAwait(false);
            if (resultTrades.Success)
            {
                GetTradesElapsed = resultTrades.ElapsedMilliseconds;
                if (resultTrades.Data.ContainsKey(si.Symbol))
                {
                    var trades = resultTrades.Data[si.Symbol].Select(x => ToPublicTrade(x, si)).ToList();// pair is <symbol,trades>
                    ProcessPublicTrades(trades);
                    UpdateStatus(ServerStatus);
                }
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetTrades: {resultTrades.Error.ToString()}");
            }
        }

        protected override async Task GetDepthImpl()
        {
            if (CurrentSymbol == null)
                return;
            var si = CurrentSymbolInformation;
            var resultDepth = await client.GetDepthAsync(si.Symbol).ConfigureAwait(false);
            if (resultDepth.Success)
            {
                GetDepthElapsed = resultDepth.ElapsedMilliseconds;
                if (resultDepth.Data.ContainsKey(si.Symbol))
                {
                    var bids = new List<OrderBookEntry>();
                    var asks = new List<OrderBookEntry>();
                    var orderBook = resultDepth.Data[si.Symbol]; // pair is <symbol,orderbook>
                    asks.AddRange(orderBook.asks.Take(OrderBookMaxItemCount).Select(x => new OrderBookEntry(si) { Price = x[0], Quantity = x[1], Side = TradeSide.Sell }));
                    bids.AddRange(orderBook.bids.Take(OrderBookMaxItemCount).Select(x => new OrderBookEntry(si) { Price = x[0], Quantity = x[1], Side = TradeSide.Buy }));
                    var depth = asks.AsEnumerable().Reverse().Concat(bids);
                    ProcessOrderBook(depth);
                    UpdateStatus(ServerStatus);
                }
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetDepth: {resultDepth.Error.ToString()}");
            }
        }

        protected override async Task<bool> CancelOrderImpl(string orderId)
        {
            var result = await client.CancelOrderAsync(long.Parse(orderId)).ConfigureAwait(false);
            if (result.Success)
            {
                UpdateFunds(result.Data.funds);
                CurrentAccount.OpenOrders.Remove(orderId);
                return true;
            }
            return false;
        }

        protected override async Task<bool> SubmitOrder(NewOrder order)
        {
            var result = await client.NewOrderAsync(order.Side.ToString(), order.SymbolInformation.Symbol, order.Price, order.Quantity, order.OrderType);
            if (result.Success)
            {
                UpdateFunds(result.Data.funds);
                var orderId = result.Data.orderId;
                var orderResult = await client.GetOrderAsync(orderId).ConfigureAwait(false);
                if (orderResult.Success)
                {
                    orderResult.Data.id = orderId;
                    CurrentAccount.OpenOrders.AddOrUpdate(Convert(orderResult.Data, order.SymbolInformation));
                }
                return true;
            }
            return false;
        }

        private void UpdateFunds(Dictionary<string, DSX.Balance> funds)
        {
            var balances = funds.Select(x => new Balance(x.Key, true) { Free = x.Value.available, Locked = x.Value.total - x.Value.available }).ToList();
            foreach (var b in balances)
                CurrentAccount.BalanceManager.AddUpdateBalance(b);
        }

        private long? lastHistoryTradeId;
        private long? lastHistoryOrderId;

      






        protected override string DefaultMarket => "USD";
        protected override bool TickersAreMarketListDependable => true;
        public override string ExchangeName => "DSX";
        public override int[] RecentTradesSizeList => new int[] { 5, 10, 20, 50, 100, 500, 1000 };

        public DateTime ServerTime
        {
            get { return this.serverTime; }
            set { this.RaiseAndSetIfChanged(ref this.serverTime, value); }
        }

        public override bool FilterByMarket(string symbol, string market)
        {
            return symbol.EndsWith(market, StringComparison.CurrentCultureIgnoreCase);
        }

		public override bool FilterByAsset(string symbol, string asset)
        {
            return symbol.StartsWith(asset, StringComparison.CurrentCultureIgnoreCase);
        }

        private static TransferStatus Code2TransferStatus(int code)
        {
            switch (code)
            {
                case 1:
                    return TransferStatus.Failed;
                case 2:
                    return TransferStatus.Completed;
                case 3:
                    return TransferStatus.Processing;
                case 4:
                    return TransferStatus.Rejected;
            }
            return TransferStatus.Undefined;
        }

        private static OrderStatus Code2OrderStatus(int code)
        {
            switch (code)
            {
                case 0:
                    return OrderStatus.Active;
                case 1:
                    return OrderStatus.Filled;
                case 2:
                    return OrderStatus.Cancelled;
                case 3:
                    return OrderStatus.Cancelling;
                case 4:
                    return OrderStatus.Rejected;
                default:
                    return OrderStatus.Undefined;
            }
        }

        public DsxViewModel()
        {
            var defaultAccount = new ExchangeAccount("default", client);
            Accounts.AddOrUpdate(defaultAccount);
            CurrentAccount = defaultAccount;
            this.HasSignedAccount = client.IsSigned;
            //AddRule(CreateStopLoss("ltcusd", 28.49m, 28.2m, 9.98m));
            //AddRule(CreateStopLoss("ltcusd", 31.39m, 31.0m, 35.837m));
            //AddRule(CreateStopLoss("ltceur", 25.59m, 25.29m, 10.08m));
            //AddRule(CreateStopLoss("ltceur", 26.29m, 26.29m, 10.08m));
            //AddRule(CreateStopLoss("ltcusd", 31.39m, 31.0m, 50m));
            //AddRule(CreateTakeProfit("ltcusd", 39.99m, 39.49m, 65.97m));
            //AddRule(CreateStopLoss("ethusd", 133.39m, 133.0m, 20m));
            //AddRule(CreateTakeProfit("ethusd", 156.09m, 155.91m, 20m));

            //Initialize();
        }

        private TradingRule CreateStopLoss(string pair, decimal trigger, decimal price, decimal amount)
        {
            var rule = new TradingRule();
            rule.Market = pair;
            rule.Operator = ThresholdOperator.LessOrEqual;
            rule.Property = ThresholdType.BidPrice;
            rule.ThresholdRate = trigger;
            rule.OrderRate = price;
            rule.OrderVolume = amount;
            rule.OrderSide = TradeSide.Sell;
            rule.OrderType = "market";
            return rule;
        }

        private TradingRule CreateTakeProfit(string pair, decimal trigger, decimal price, decimal amount)
        {
            var rule = new TradingRule();
            rule.Market = pair;
            rule.Operator = ThresholdOperator.GreaterOrEqual;
            rule.Property = ThresholdType.BidPrice;
            rule.ThresholdRate = trigger;
            rule.OrderRate = price;
            rule.OrderVolume = amount;
            rule.OrderSide = TradeSide.Sell;
            rule.OrderType = "market";
            return rule;
        }

        protected async void Initialize()
        {
            try
            {
                var yx = await client.GetOrdersAsync();
                var xz = await client.GetOrdersHistoryAsync();
                var xy = await client.GetTradesHistoryAsync();
                var yz = await client.GetAccountInfoAsync();
                var zx = await client.GetTradingVolumeAsync();
                var result = await client.GetCurrentFeesAsync();
                if (result.Success)
                    fees = result.Data;
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
            }
        }

        protected async override Task<IEnumerable<SymbolInformation>> GetMarketsAsync()
        {
            var result = await client.GetExchangeInfoAsync().ConfigureAwait(false);
            if (result.Success)
            {
                ServerTime = result.Data.server_time.FromUnixSeconds();
                return result.Data.pairs.Select(CreateSymbolInformation).Where(IsValidMarket);
            }
            else
                return Enumerable.Empty<SymbolInformation>();
        }

        protected async override Task<IEnumerable<PriceTicker>> GetTickersAsync(IEnumerable<SymbolInformation> markets)
        {
            //'const string pairs = "ethbtc-eursusd-ltcusd-gbpusd-eurusd-ltcbtc-btgusd-ltcusdt-btcusd-usdtusd-bccusd-ethusdt-ethusd-bccbtc-btcusdt-btgbtc-bccusdt";
            IEnumerable<PriceTicker> tickers = Enumerable.Empty<PriceTicker>();
            var pairs = markets.Select(x => x.Symbol);
            var result = await client.GetTickerAsync(string.Join("-", pairs)).ConfigureAwait(false);
            if (result.Success)
                tickers = result.Data.Select(ToPriceTicker);
            //var start = DateTime.Now.AddMinutes(-3599);
            //var resultBars = await client.GetBarsFromMomentAsync(string.Join("-", pairs), "d", DateTime.Now.AddHours(-24)).ConfigureAwait(false);
            //if (resultBars.Success)
            //    ;
            return tickers;
        }

        protected override Task<IEnumerable<PriceTicker>> GetTickersAsync()
        {
            throw new NotSupportedException();
        }

        protected async override Task<IEnumerable<PublicTrade>> GetPublicTradesAsync(string market, int limit)
        {
            var result = await client.GetTradesAsync(market, limit).ConfigureAwait(false);
            var trades = new List<PublicTrade>();
            if (result.Success)
            {
                if (result.Data.ContainsKey(market))
                {
                    var si = GetSymbolInformation(market);
                    trades = result.Data[market].Select(x => ToPublicTrade(x, si)).ToList(); // pair is <symbol,trades>
                }
            }
            return trades;
        }

        protected async override Task<IEnumerable<OrderBookEntry>> GetOrderBookAsync(string market, int limit)
        {
            var result = await client.GetDepthAsync(market).ConfigureAwait(false); // limit is not supported by DSX.
            if (result.Success)
            {
                var bids = new List<OrderBookEntry>();
                var asks = new List<OrderBookEntry>();
                if (result.Data.ContainsKey(market))
                {
                    var si = GetSymbolInformation(market);
                    var orderBook = result.Data[market]; // pair is <symbol,orderbook>
                    asks.AddRange(orderBook.asks.Take(limit).Select(x => new OrderBookEntry(si) { Price = x[0], Quantity = x[1], Side = TradeSide.Sell }));
                    bids.AddRange(orderBook.bids.Take(limit).Select(x => new OrderBookEntry(si) { Price = x[0], Quantity = x[1], Side = TradeSide.Buy }));
                }
                return asks.AsEnumerable().Reverse().Concat(bids);
            }
            else
            {
                return Enumerable.Empty<OrderBookEntry>();
            }
        }

        protected override async Task<Order> PlaceOrder(TradingRule rule)
        {
            // The order type: limit, market, or fill-or-kill
            var result = await client.NewOrderAsync(rule.OrderSide.ToString(), rule.Market, rule.OrderRate, rule.OrderVolume, rule.OrderType).ConfigureAwait(false);
            if (!result.Success)
                throw new ApiException(result.Error);
            var si = GetSymbolInformation(rule.Market);
            //return Convert(result.Data, si);
            throw new NotImplementedException();
        }

        protected override async Task<List<Balance>> GetBalancesAsync()
        {
            var result = await client.GetAccountInfoAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.funds.Select(x => new Balance(x.Key, true) { Free = x.Value.available, Locked = x.Value.total - x.Value.available }).ToList();
            else
                return new List<Balance>();
        }

        protected override async Task<List<Order>> GetOpenOrdersAsync(IEnumerable<string> markets)
        {
            if (!client.IsSigned)
                throw new Exception("API keys not specified.");
            var orders = new List<Order>();
            foreach (var market in markets)
            {
                var si = GetSymbolInformation(market);
                var ordersResult = await client.GetOrdersAsync(market).ConfigureAwait(false);
                if (ordersResult.Success)
                    orders.AddRange(ordersResult.Data.Select(x => Convert(x, si)));
            }
            return orders;
        }

        protected override async Task GetOpenOrdersImpl()
        {
            var ordersResult = await client.GetOrdersAsync().ConfigureAwait(false);
            UpdateStatus();
            if (ordersResult.Success)
            {
                var orders = ordersResult.Data.Select(Convert).OrderByDescending(x => x.Created);
                CurrentAccount.OpenOrders.Clear();
                CurrentAccount.OpenOrders.AddOrUpdate(orders);
            }
        }

        protected override async Task GetBalanceImpl()
        {
            var result = await GetBalancesAsync().ConfigureAwait(false);
            var mngr = CurrentAccount?.BalanceManager;
            foreach (Balance b in result)
            {
                mngr.AddUpdateBalance(b);
            }
            foreach (var ticker in MarketSummaries)
                mngr.UpdateWithLastPrice(ticker.Symbol, ticker.LastPrice.GetValueOrDefault());
        }

        protected override async Task GetDepositsImpl()
        {
            var deposits = await GetDepositsAsync().ConfigureAwait(false);
            CurrentAccount.Deposits.AddOrUpdate(deposits);
            UpdateStatus();
        }

        protected override async Task GetWithdrawalsImpl()
        {
            var withdrawals = await GetWithdrawalsAsync().ConfigureAwait(false);
            CurrentAccount.Withdrawals.AddOrUpdate(withdrawals);
            UpdateStatus();
        }

        protected override async Task GetOrdersHistoryImpl()
        {
            var orders = new List<Order>();
            var ordersResult = await client.GetOrdersHistoryAsync(fromId: lastHistoryOrderId).ConfigureAwait(false);
            var tradesResult = await client.GetTradesHistoryAsync(fromId: lastHistoryTradeId).ConfigureAwait(false);
            UpdateStatus();
            if (ordersResult.Success && tradesResult.Success)
            {
                lastHistoryTradeId = tradesResult.Data.Max(x => x.id); // get latest ID
                lastHistoryOrderId = ordersResult.Data.Max(x => x.id); // get latest ID
                foreach (var data in ordersResult.Data.Where(x => x.status == 1 || x.remainingVolume != x.volume).OrderByDescending(x => x.id))
                {
                    var si = GetSymbolInformation(data.pair);
                    var deals = tradesResult.Data.Where(x => x.orderId == data.id);
                    var order = Convert(data, si, deals);
                    orders.Add(order);
                }
            }
            CurrentAccount.OrdersHistory.AddOrUpdate(orders.OrderByDescending(x => x.Created));
        }

        bool ignoreCancelledOrders = true;

        protected override async Task RefreshPrivateDataExecute()
        {
            var tradesResult = await client.GetTradesHistoryAsync(fromId: lastHistoryTradeId);
            if (!tradesResult.Success)
                return;
            if (ignoreCancelledOrders && lastHistoryTradeId != null && tradesResult.Data.Count < 1)
                return; // nothing new happend since last update.
            if (tradesResult.Data.Count > 0)
            {
                if (lastHistoryTradeId != null)
                {
                    // notify about new trades.
                    foreach (var trade in tradesResult.Data)
                    {
                        var order = CurrentAccount.OpenOrders.Lookup(trade.orderId.ToString());
                        if (order.HasValue)
                            order.Value.ExecutedQuantity += trade.volume;
                    }
                }
                lastHistoryTradeId = tradesResult.Data.Max(x => x.id) + 1;
            }

            var ordersResult = await client.GetOrdersHistoryAsync(fromId: lastHistoryOrderId);
            if (!ordersResult.Success)
                return;
            if (lastHistoryOrderId != null && ordersResult.Data.Count < 1)
                return;
            if (ordersResult.Data.Count > 0)
            {
                var orders = new List<Order>();
                if (lastHistoryOrderId != null)
                {
                    // notify about order status change.
                }
                lastHistoryOrderId = ordersResult.Data.Max(x => x.id) + 1;
                foreach (var data in ordersResult.Data.Where(x => x.status == 1).OrderByDescending(x => x.id))
                {
                    var si = GetSymbolInformation(data.pair);
                    var deals = tradesResult.Data.Where(x => x.orderId == data.id);
                    var order = Convert(data, si, deals);
                    orders.Add(order);
                }
                CurrentAccount.OrdersHistory.AddOrUpdate(orders);
            }
        }

        protected async Task<List<Transfer>> GetDepositsAsync()
        {
            var result = await client.GetDepositsAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(x => Convert(x.Value, x.Key)).ToList();
            return new List<Transfer>();
        }

        protected async Task<List<Transfer>> GetWithdrawalsAsync()
        {
            var result = await client.GetWithdrawalsAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(x => Convert(x.Value, x.Key)).ToList();
            return new List<Transfer>();
        }

        protected override async Task RefreshCommandExecute(int x)
        {
            switch (x)
            {
                case 2: // orders
                    CurrentAccount.OpenOrders.Clear();
                    CurrentAccount.OpenOrders.AddOrUpdate(await GetOpenOrdersAsync(new string[] { "btcusd", "btgusd", "ethusd", "ltcusd", "eurusd" }));
                    break;
                case 3: // orders history
                    await RefreshPrivateDataExecute().ConfigureAwait(false);
                    break;
                case 4: // trades history
                    await RefreshPrivateDataExecute().ConfigureAwait(false);
                    break;
                case 5: // funds
                    var result = await GetBalancesAsync().ConfigureAwait(false);
                    foreach (Balance b in result)
                        CurrentAccount.BalanceManager.AddUpdateBalance(b);
                    break;
                case 6: // deposits
                    CurrentAccount.Deposits.Clear();
                    CurrentAccount.Deposits.AddOrUpdate(await GetDepositsAsync().ConfigureAwait(false));
                    break;
                case 7: // withdrawals
                    CurrentAccount.Withdrawals.Clear();
                    CurrentAccount.Withdrawals.AddOrUpdate(await GetWithdrawalsAsync().ConfigureAwait(false));
                    break;
            }
        }

        protected override bool IsValidMarket(SymbolInformation si)
        {
            return base.IsValidMarket(si) &&
                si.QuoteAsset != "USDT" && si.QuoteAsset != "EURS" &&
                si.QuoteAsset != "GBP" && si.QuoteAsset != "RUB" &&
                si.QuoteAsset != "TRY";
        }

        protected override async Task<SymbolInformation> GetFullSymbolInformation()
        {
            var si = CurrentSymbolInformation;
            var balances = await GetBalancesAsync().ConfigureAwait(false);
            si.BaseAssetBalance = balances.SingleOrDefault(x => x.Asset == si.BaseAsset);
            si.QuoteAssetBalance = balances.SingleOrDefault(x => x.Asset == si.QuoteAsset);
            si.PriceTicker = GetPriceTicker(si.Symbol);
            return si;
        }

        internal void UpdateStatus()
        {
            Status = $"DSX: Noting to say so far.";
        }

        internal SymbolInformation CreateSymbolInformation(KeyValuePair<string, DSX.Pair> p)
        {
            var cmcEntry = GetCmcEntry(p.Value.base_currency);
            return new SymbolInformation()
            {
                BaseAsset = p.Value.base_currency,
                MaxPrice = p.Value.max_price,
                MinPrice = p.Value.min_price,
                TickSize = p.Value.min_amount,
                MinQuantity = p.Value.min_amount,
                MaxQuantity = decimal.MaxValue,
                StepSize = p.Value.min_amount,
                QuantityDecimals = p.Value.amount_decimal_places,
                OrderTypes = this.OrderTypes,
                PriceDecimals = p.Value.decimal_places,
                QuoteAsset = p.Value.quoted_currency,
                Symbol = p.Key,
                CmcId = cmcEntry != null ? cmcEntry.id : -1,
                CmcName = cmcEntry != null ? cmcEntry.name : p.Value.base_currency,
                CmcSymbol = cmcEntry != null ? cmcEntry.symbol : p.Key
            };
        }

        internal PriceTicker ToPriceTicker(KeyValuePair<string, DSX.Ticker> p)
        {
            return new PriceTicker()
            {
                HighPrice = p.Value.high,
                LowPrice = p.Value.low,
                WeightedAveragePrice = p.Value.avg,
                LastPrice = p.Value.last,
                Bid = p.Value.buy,
                Ask = p.Value.sell,
                Symbol = p.Key,
                SymbolInformation = GetSymbolInformation(p.Key),
                Volume = p.Value.vol,
                QuoteVolume = p.Value.vol_cur
            };
        }

        internal static PublicTrade ToPublicTrade(DSX.Trade t, SymbolInformation si)
        {
            return new PublicTrade(si)
            {
                Id = t.tid,
                Price = t.price,
                Quantity = t.amount,
                Side = t.type == "bid" ? TradeSide.Buy : TradeSide.Sell,
                Time = t.timestamp.FromUnixSeconds()
            };
        }

        private Order Convert(DSX.Order x, SymbolInformation si, IEnumerable<DSX.Deal> deals = null)
        {
            var orderPrice = x.rate;
            if (x.orderType == "market" && deals != null)
            {
                // calc. average price using deals.
                decimal totalQuote = 0m;
                foreach (var deal in deals)
                {
                    totalQuote += deal.volume * deal.rate;
                }
                if (totalQuote > decimal.Zero)
                {
                    orderPrice = totalQuote / (x.volume - x.remainingVolume);
                    orderPrice = Math.Round(orderPrice, si.PriceDecimals);
                }
            }
            return new Order(si)
            {
                Price = orderPrice,
                Quantity = x.volume,
                ExecutedQuantity = x.volume - x.remainingVolume,
                Side = x.type == "buy" ? TradeSide.Buy : TradeSide.Sell,
                Status = Code2OrderStatus(x.status),
                Created = x.timestampCreated.FromUnixSeconds(),
                Updated = x.timestampCreated.FromUnixSeconds(),
                Type = x.orderType,
                OrderId = x.id.ToString()
            };
        }

        private Order Convert(DSX.Order x)
        {
            var orderPrice = x.rate;
            SymbolInformation si = GetSymbolInformation(x.pair);
            return new Order(si)
            {
                Price = orderPrice,
                Quantity = x.volume,
                ExecutedQuantity = x.volume - x.remainingVolume,
                Side = x.type == "buy" ? TradeSide.Buy : TradeSide.Sell,
                Status = Code2OrderStatus(x.status),
                Created = x.timestampCreated.FromUnixSeconds(),
                Updated = x.timestampCreated.FromUnixSeconds(),
                Type = x.orderType,
                OrderId = x.id.ToString()
            };
        }

        private Transfer Convert(DSX.Transfer x, long transactionId)
        {
            return new Transfer
            {
                Id = transactionId.ToString(),
                Timestamp = x.timestamp.FromUnixSeconds(),
                Type = x.type == "Incoming" ? TransferType.Deposit : TransferType.Withdrawal,
                Asset = x.currency,
                Quantity = x.amount,
                Address = x.address,
                Comission = x.comission,
                Status = Code2TransferStatus(x.status)
            };
        }

        DsxApiClient client = new DsxApiClient();
        private DateTime serverTime;
        private DSX.Fees fees;
        public override string[] OrderTypes => new[] { "limit", "market", "fill-or-kill" };
    }
}
