using Exchange.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Terminal.WPF
{
    class GateIoViewModel : ExchangeViewModel
    {
        public override string ExchangeName => "Gate.IO";

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
            var resultMarketInfo = client.GetMarketInfoAsync();
            var resultMarketList = client.GetMarketListAsync();
            await Task.WhenAll(resultMarketInfo, resultMarketList).ConfigureAwait(false);
            var info = resultMarketInfo.Result;
            var list = resultMarketList.Result;
            if (info.Success && list.Success)
            {
                return list.Data.data.Join(info.Data.pairs, x => x.pair, y => y.Key, (x, y) => Convert(x, y.Value)).ToList();
            }
            else
                return Enumerable.Empty<SymbolInformation>();
        }

        private SymbolInformation Convert(GateIo.Market x, GateIo.Pair y)
        {
            var cmcEntry = GetCmcEntry(x.curr_a + x.curr_b);
            return new SymbolInformation()
            {
                BaseAsset = x.curr_a,
                MaxPrice = decimal.MaxValue,
                MinPrice = decimal.Zero,
                MinQuantity = y.min_amount,
                QuantityDecimals = y.decimal_places,
                PriceDecimals = y.decimal_places,
                QuoteAsset = x.curr_b,
                Symbol = x.pair,
                Status = y.trade_disabled != 0 ? "TRADING" : "BREAK",
                CmcId = cmcEntry != null ? cmcEntry.id : -1,
                CmcName = cmcEntry != null ? cmcEntry.name : x.name,
                CmcSymbol = cmcEntry != null ? cmcEntry.symbol : x.curr_a + x.curr_b
            };
        }

        private PriceTicker Convert(KeyValuePair<string, GateIo.Ticker> x)
        {
            return new PriceTicker()
            {
                HighPrice = x.Value.high24hr,
                LastPrice = x.Value.last,
                LowPrice = x.Value.low24hr,
                PriceChange = 0m,
                PriceChangePercent = x.Value.percentChange,
                QuoteVolume = x.Value.quoteVolume,
                Symbol = x.Key,
                SymbolInformation = GetSymbolInformation(x.Key),
                Volume = x.Value.baseVolume
            };
        }


        protected override Task<IEnumerable<OrderBookEntry>> GetOrderBookAsync(string market, int limit)
        {
            throw new NotImplementedException();
        }

        protected override Task<IEnumerable<PublicTrade>> GetPublicTradesAsync(string market, int limit)
        {
            throw new NotImplementedException();
        }

        protected override async Task<IEnumerable<PriceTicker>> GetTickersAsync()
        {
            var result = await client.GetTickersAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(Convert);
            else
                return Enumerable.Empty<PriceTicker>();
        }

        GateIoApiClient client = new GateIoApiClient();
    }
}
