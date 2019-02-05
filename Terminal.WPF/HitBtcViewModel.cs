using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;

namespace Exchange.Net
{
    public class HitBtcViewModel : ExchangeViewModel
    {
        public HitBtcViewModel()
        {
        }

        public override string ExchangeName => "HitBTC";

        protected override string DefaultMarket => "BTC";
        //protected override bool HasMarketSummariesPull => true;
        //protected override bool HasMarketSummariesPush => true;
        //protected override bool HasTradesPush => true;
        //protected override bool HasOrderBookPull => true;

        public override bool FilterByAsset(string symbol, string asset)
        {
            throw new NotImplementedException();
        }

        public override bool FilterByMarket(string symbol, string market)
        {
            throw new NotImplementedException();
        }

        protected async override Task<IEnumerable<SymbolInformation>> GetMarketsAsync()
        {
            var result = await client.GetSymbolsAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(CreateSymbolInformation);
            else
                return Enumerable.Empty<SymbolInformation>();
        }

        protected async override Task<IEnumerable<PriceTicker>> GetTickersAsync()
        {
            var result = await client.GetTickersAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(ToPriceTicker);
            else
                return Enumerable.Empty<PriceTicker>();
        }

        protected async override Task<IEnumerable<PublicTrade>> GetPublicTradesAsync(string market, int limit)
        {
            var result = await client.GetTradesAsync(market).ConfigureAwait(false);
            if (result.Success)
            {
                var si = GetSymbolInformation(market);
                var trades = result.Data.Select(x => ToPublicTrade(x, si)).ToList();
                return trades;
            }
            else
                return Enumerable.Empty<PublicTrade>();
        }

        protected async override Task<IEnumerable<OrderBookEntry>> GetOrderBookAsync(string market, int limit)
        {
            var result = await client.GetDepthAsync(market, limit).ConfigureAwait(false);
            if (result.Success)
            {
                var depth = result.Data;
                var asks = depth.ask.Select(a => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = a.price, Quantity = a.size, Side = TradeSide.Sell });
                var bids = depth.bid.Select(b => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = b.price, Quantity = b.size, Side = TradeSide.Buy });
                return asks.Take(limit).Reverse().Concat(bids.Take(limit));
            }
            else
                return Enumerable.Empty<OrderBookEntry>();
        }

        HitBtcApiClient client = new HitBtcApiClient();

        internal SymbolInformation CreateSymbolInformation(HitBtc.Symbol x)
        {
            var cmcEntry = GetCmcEntry(x.baseCurrency);
            return new SymbolInformation
            {
                BaseAsset = x.baseCurrency,
                PriceDecimals = 10,
                QuantityDecimals = 8,
                QuoteAsset = x.quoteCurrency,
                StepSize = x.quantityIncrement,
                Symbol = x.id,
                TickSize = x.tickSize,
                CmcId = cmcEntry != null ? cmcEntry.id : -1,
                CmcName = cmcEntry != null ? cmcEntry.name : x.baseCurrency.ToUpper(),
                CmcSymbol = cmcEntry != null ? cmcEntry.symbol : x.id
            };
        }

        internal PriceTicker ToPriceTicker(HitBtc.Ticker ticker)
        {
            return new PriceTicker()
            {
                HighPrice = ticker.high.GetValueOrDefault(),
                LowPrice = ticker.low.GetValueOrDefault(),
                LastPrice = ticker.last.GetValueOrDefault(),
                PriceChange = ticker.last.GetValueOrDefault() - ticker.open.GetValueOrDefault(),
                PriceChangePercent = CalcChangePercent(ticker.last.GetValueOrDefault(), ticker.open.GetValueOrDefault()),
                QuoteVolume = ticker.volumeQuote,
                Symbol = ticker.symbol,
                SymbolInformation = GetSymbolInformation(ticker.symbol),
                Volume = ticker.volumeQuote
            };
        }

        internal IEnumerable<PublicTrade> ToPublicTrades(HitBtc.WsTrades trades)
        {
            var si = GetSymbolInformation(trades.symbol);
            return trades.data.Select(trade =>
                new PublicTrade(si)
                {
                    Id = trade.id,
                    Price = trade.price,
                    Quantity = trade.quantity,
                    Side = trade.side == "buy" ? TradeSide.Buy : TradeSide.Sell,
                    Time = trade.timestamp.ToLocalTime()
                });
        }

        internal static PublicTrade ToPublicTrade(HitBtc.Trade trade, SymbolInformation si)
        {
            return new PublicTrade(si)
            {
                Id = trade.id,
                Price = trade.price,
                Quantity = trade.quantity,
                Side = trade.side == "buy" ? TradeSide.Buy : TradeSide.Sell,
                Time = trade.timestamp.ToLocalTime()
            };
        }

        internal bool FilterInvalidTicker(HitBtc.Ticker ticker)
        {
            return ticker.last.HasValue && ticker.open.HasValue;
        }
    }
}
