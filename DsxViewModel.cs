﻿using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Exchange.Net
{
    class DsxViewModel : ExchangeViewModel
    {
        public DsxViewModel()
        {
        }

        protected override string DefaultMarket { get => "USD"; }
        public override string ExchangeName { get => "DSX"; }
        protected override bool HasMarketSummariesPush => true;

        public override async Task<List<PublicTrade>> GetTrades(string symbol, int limit = 50)
        {
            var trades = await client.GetTradesAsync(symbol, limit);
            return trades.Select(
                t => new PublicTrade()
                {
                    Id = t.tid,
                    Price = t.price,
                    Quantity = t.amount,
                    Side = t.type == "bid" ? TradeSide.Buy : TradeSide.Sell,
                    Symbol = symbol,
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(t.timestamp).DateTime.ToLocalTime()
                }).ToList();
        }

		public override async Task<List<SymbolInformation>> GetMarkets()
        {
            var tmp = await client.GetExchangeInfoAsync();
            var pairs = tmp.pairs;
            return pairs.Where(
                p =>
                    p.Value.quoted_currency != "EUR" && p.Value.quoted_currency != "EURS" &&
                    p.Value.quoted_currency != "GBP" && p.Value.quoted_currency != "RUB"
                ).Select(
                p => new SymbolInformation()
                {
                    BaseAsset = p.Value.base_currency,
                    MaxPrice = p.Value.max_price,
                    MinPrice = p.Value.min_price,
                    MinQuantity = p.Value.min_amount,
                    QuantityDecimals = p.Value.amount_decimal_places,
                    PriceDecimals = p.Value.decimal_places,
                    QuoteAsset = p.Value.quoted_currency,
                    Symbol = p.Key
                }).OrderBy(x => x.Symbol).ToList();
			//Markets.AddRange(result.OrderBy(x => x.Symbol));
        }

		public override async Task<List<PriceTicker>> GetMarketSummaries()
        {
            var allmarkets = string.Join("-", Markets.Select(x => x.Symbol));
            var tmp = await client.GetTickerAsync(allmarkets);
            return tmp.Select(
                p => new PriceTicker()
                {
                    LastPrice = p.Value.last,
                    PriceChangePercent = 0,
                    Symbol = p.Key,
                    Volume = p.Value.vol
                }).ToList();
        }

		public override IObservable<PriceTicker> SubscribeMarketSummaries(IEnumerable<string> symbols, string period)
        {
            var allmarkets = string.Join("-", symbols);
            return Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).SelectMany<long, PriceTicker>(selector => client.GetTicker(allmarkets).Select(
                x => new PriceTicker()
                {
                    Symbol = x.Key,
                    LastPrice = x.Value.last,
                    Volume = x.Value.vol_cur,
                    PriceChangePercent = decimal.Zero
                }));
        }

		public override async Task<List<Balance>> GetBalances()
        {
            var tmp = await client.GetAccountInfoAsync();
            return tmp.funds.Select(
                p => new Balance(p.Key, UsdAssets.Contains(p.Key))
                {
                    Free = p.Value.available,
                    Locked = p.Value.total - p.Value.available
                }).ToList();

            //await Task.Delay(50);
            //return new List<Balance>();
        }

		public override bool FilterByMarket(string symbol, string market)
        {
            return symbol.EndsWith(market, StringComparison.CurrentCultureIgnoreCase);
        }

		public override bool FilterByAsset(string symbol, string asset)
        {
            return symbol.StartsWith(asset, StringComparison.CurrentCultureIgnoreCase);
        }

        public override async Task<List<Transfer>> GetDeposits(string asset = null)
        {
            //await Task.Delay(50);
            //return new List<Transfer>();
            var tmp = await client.GetDepositsAsync(asset);
            return tmp.Select(
                x => new Transfer()
                {
                    Address = x.address,
                    Comission = x.comission,
                    Asset = x.currency,
                    Quantity = x.amount,
                    Status = Code2TransferStatus(x.status),
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(x.timestamp).DateTime.ToLocalTime(),
                    Type = TransferType.Deposit
                }).ToList();
        }

        public override async Task<List<Transfer>> GetWithdrawals(string asset = null)
        {
            var tmp = await client.GetWithdrawalsAsync(asset);
            return tmp.Select(
                x => new Transfer()
                {
                    Address = x.address,
                    Comission = x.comission,
                    Asset = x.currency,
                    Quantity = x.amount,
                    Status = Code2TransferStatus(x.status),
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(x.timestamp).DateTime.ToLocalTime(),
                    Type = TransferType.Withdrawal
                }).ToList();
        }

        public async override Task<List<Order>> GetOpenOrders()
        {
            await Task.Delay(50);
            return new List<Order>();
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

        public override OrderBook GetOrderBook(string symbol, int limit)
        {
            var tmp = client.GetDepth(symbol); // limit is not supported by DSX.
            var book = new OrderBook();
            foreach (var p in tmp)
            {
                book.Asks.AddRange(p.Value.asks.Take(limit).Select(x => new OrderBookEntry() { Price = x[0], Quantity = x[1], Side = TradeSide.Sell }));
                book.Bids.AddRange(p.Value.bids.Take(limit).Select(x => new OrderBookEntry() { Price = x[0], Quantity = x[1], Side = TradeSide.Buy }));
            }
            return book;
        }

        DsxApiClient client = new DsxApiClient();
    }
}
