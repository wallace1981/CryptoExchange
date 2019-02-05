using Exchange.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Terminal.WPF
{
    class OKexViewModel : ExchangeViewModel
    {
        public override string ExchangeName => "OKex";

        protected override string DefaultMarket => "BTC";

        public override bool FilterByAsset(string symbol, string asset)
        {
            throw new NotImplementedException();
        }

        public override bool FilterByMarket(string symbol, string market)
        {
            throw new NotImplementedException();
        }

        protected override async Task<IEnumerable<SymbolInformation>> GetMarketsAsync()
        {
            var result = await client.GetInstrumentsAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(CreateSymbolInformation);
            else
                return Enumerable.Empty<SymbolInformation>();
        }

        protected async override Task<IEnumerable<OrderBookEntry>> GetOrderBookAsync(string market, int limit)
        {
            var result = await client.GetDepthAsync(market);
            if (result.Success)
            {
                var depth = result.Data;
                var asks = depth.asks.Select(a => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = a[0], Quantity = a[1], Side = TradeSide.Sell });
                var bids = depth.bids.Select(b => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = b[0], Quantity = b[1], Side = TradeSide.Buy });
                return asks.Reverse().Concat(bids);
            }
            else
                return Enumerable.Empty<OrderBookEntry>();
        }

        protected override async Task<IEnumerable<PublicTrade>> GetPublicTradesAsync(string market, int limit)
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

        protected override async Task<IEnumerable<PriceTicker>> GetTickersAsync()
        {
            var result = await client.GetTickersAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(ToPriceTicker);
            else
                return Enumerable.Empty<PriceTicker>();
        }

        private SymbolInformation CreateSymbolInformation(OKex.Instrument x)
        {
            var cmcEntry = GetCmcEntry(x.base_currency);
            return new SymbolInformation()
            {
                BaseAsset = x.base_currency,
                MaxPrice = decimal.MaxValue,
                MinPrice = decimal.Zero,
                TickSize = x.tick_size,
                MinQuantity = x.base_min_size,
                QuantityDecimals = DigitsCount(x.base_increment),
                PriceDecimals = DigitsCount(x.quote_increment),
                QuoteAsset = x.quote_currency,
                Symbol = x.instrument_id,
                Status = true ? "TRADING" : "BREAK",
                CmcId = cmcEntry != null ? cmcEntry.id : -1,
                CmcName = cmcEntry != null ? cmcEntry.name : x.base_currency,
                CmcSymbol = cmcEntry != null ? cmcEntry.symbol : x.product_id
            };
        }

        private PriceTicker ToPriceTicker(OKex.Ticker x)
        {
            return new PriceTicker()
            {
                HighPrice = x.high_24h,
                LastPrice = x.last,
                LowPrice = x.low_24h,
                PriceChange = x.last - x.open_24h,
                PriceChangePercent = CalcChangePercent(x.last, x.open_24h),
                QuoteVolume = x.quote_volume_24h,
                Symbol = x.instrument_id,
                SymbolInformation = GetSymbolInformation(x.instrument_id),
                Volume = x.base_volume_24h
            };
        }

        internal PublicTrade ToPublicTrade(OKex.Trade t, SymbolInformation si)
        {
            return new PublicTrade(si)
            {
                Id = t.trade_id,
                Price = t.price,
                Quantity = t.size,
                Side = t.side == "buy" ? TradeSide.Buy : TradeSide.Sell,
                Time = t.timestamp.ToLocalTime()
            };
        }

        OKexApiClient client = new OKexApiClient();
    }
}
