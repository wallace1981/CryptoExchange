using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Exchange.Net
{
    public class BittrexViewModel : ExchangeViewModel
    {
        protected string ServerStatus;
        protected string ClientStatus => $"Weight is {client.Weight}, expiration in {client.WeightReset.TotalSeconds} secs.";

        protected void UpdateStatus(string serverStatus, string clientMsg = null)
        {
            Status = string.Join("  ", serverStatus, clientMsg ?? ClientStatus);
            ServerStatus = serverStatus;
        }

        protected override async Task GetExchangeInfo()
        {
            var resultExchangeInfo = await client.GetMarketsAsync().ConfigureAwait(true);
            if (resultExchangeInfo.Success)
            {
                GetExchangeInfoElapsed = resultExchangeInfo.ElapsedMilliseconds;
                var symbols = resultExchangeInfo.Data.Select(CreateSymbolInformation);
                ProcessExchangeInfo(symbols);
                UpdateStatus($"Number of trading pairs: {symbols.Count()}");
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetExchangeInfo: {resultExchangeInfo.Error.ToString()}");
            }
        }

        protected override async Task GetTickers()
        {
            if (Markets.Count() < 1)
                return;
            var resultTickers = await client.GetMarketSummariesAsync().ConfigureAwait(true);
            if (resultTickers.Success)
            {
                GetTickersElapsed = resultTickers.ElapsedMilliseconds;
                var tickers = resultTickers.Data.Select(ToPriceTicker);
                ProcessPriceTicker(tickers);
                UpdateStatus(ServerStatus);
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetTickers: {resultTickers.Error.ToString()}");
            }
        }

        protected override async Task GetTrades()
        {
            if (CurrentSymbol == null)
                return;
            var si = CurrentSymbolInformation;
            var resultTrades = await client.GetMarketHistoryAsync(si.Symbol).ConfigureAwait(true);
            if (resultTrades.Success)
            {
                GetTradesElapsed = resultTrades.ElapsedMilliseconds;
                    var trades = resultTrades.Data.Select(x => ToPublicTrade(x, si)).ToList();// pair is <symbol,trades>
                    ProcessPublicTrades(trades);
                    UpdateStatus(ServerStatus);
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetTrades: {resultTrades.Error.ToString()}");
            }
        }

        protected override async Task GetDepth()
        {
            if (CurrentSymbol == null)
                return;
            var si = CurrentSymbolInformation;
            var resultDepth = await client.GetOrderBookAsync(si.Symbol).ConfigureAwait(true);
            if (resultDepth.Success)
            {
                GetDepthElapsed = resultDepth.ElapsedMilliseconds;

                var orderBook = resultDepth.Data;
                var asks = orderBook.sell.Select(a => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = a.Rate, Quantity = a.Quantity, Side = TradeSide.Sell });
                var bids = orderBook.buy.Select(b => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = b.Rate, Quantity = b.Quantity, Side = TradeSide.Buy });
                var depth = asks.Take(OrderBookMaxItemCount).Reverse().Concat(bids.Take(OrderBookMaxItemCount));
                ProcessOrderBook(depth);
                UpdateStatus(ServerStatus);
            }
            else
            {
                UpdateStatus(ServerStatus, $"GetDepth: {resultDepth.Error.ToString()}");
            }
        }









        public BittrexViewModel()
        {
        }

        protected async override Task<IEnumerable<SymbolInformation>> GetMarketsAsync()
        {
            var result = await client.GetMarketsAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(CreateSymbolInformation);
            else
                return Enumerable.Empty<SymbolInformation>();
        }

        protected async override Task<IEnumerable<PriceTicker>> GetTickersAsync()
        {
            var result = await client.GetMarketSummariesAsync().ConfigureAwait(false);
            if (result.Success)
                return result.Data.Select(ToPriceTicker);
            else
                return Enumerable.Empty<PriceTicker>();
        }

        protected async override Task<IEnumerable<PublicTrade>> GetPublicTradesAsync(string market, int limit)
        {
            var result = await client.GetMarketHistoryAsync(market).ConfigureAwait(false);
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
            var result = await client.GetOrderBookAsync(market).ConfigureAwait(false);
            if (result.Success)
            {
                var depth = result.Data;
                var asks = depth.sell.Select(a => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = a.Rate, Quantity = a.Quantity, Side = TradeSide.Sell });
                var bids = depth.buy.Select(b => new OrderBookEntry(OrderBook.PriceDecimals, OrderBook.QuantityDecimals) { Price = b.Rate, Quantity = b.Quantity, Side = TradeSide.Buy });
                return asks.Take(limit).Reverse().Concat(bids.Take(limit));
            }
            else
                return Enumerable.Empty<OrderBookEntry>();
        }

        protected override void SubscribeMarketData()
        {
            base.SubscribeMarketData();
            //var sub24hrPriceTicker = client.ObserveMarketSummaries().Publish();
            //sub24hrPriceTicker.Subscribe(
            //    (ApiResult<IList<Bittrex.MarketSummary>> result) =>
            //    {
            //        if (result.Success)
            //        {
            //            ProcessPriceTicker(result.Data.Select(ToPriceTicker));
            //        }
            //    }).DisposeWith(Disposables);
            //sub24hrPriceTicker.Connect().DisposeWith(Disposables);
        }

        protected override string DefaultMarket => "BTC";
        public override string ExchangeName => "Bittrex";

        public override bool FilterByMarket(string symbol, string market)
        {
            return symbol.StartsWith(market);
        }
        public override bool FilterByAsset(string symbol, string asset)
        {
            // UGLY!
            return symbol.ToUpper().Contains($"-{asset}".ToUpper());
        }

        internal SymbolInformation CreateSymbolInformation(Bittrex.Market x)
        {
            var cmcEntry = GetCmcEntry(x.MarketCurrency);
            return new SymbolInformation()
            {
                BaseAsset = x.MarketCurrency,
                MaxPrice = decimal.MaxValue,
                MinPrice = decimal.Zero,
                MinQuantity = x.MinTradeSize,
                QuantityDecimals = 8,
                PriceDecimals = 8,
                QuoteAsset = x.BaseCurrency,
                Symbol = x.MarketName,
                Status = x.IsActive ? "TRADING" : "BREAK",
                CmcId = cmcEntry != null ? cmcEntry.id : -1,
                CmcName = cmcEntry != null ? cmcEntry.name : x.MarketCurrency,
                CmcSymbol = cmcEntry != null ? cmcEntry.symbol : x.MarketName
            };
        }

        internal PriceTicker ToPriceTicker(Bittrex.MarketSummary x)
        {
            return new PriceTicker()
            {
                HighPrice = x.High,
                LastPrice = x.Last,
                LowPrice = x.Low,
                PriceChange = x.Last - x.PrevDay,
                PriceChangePercent = CalcChangePercent(x.Last, x.PrevDay),
                QuoteVolume = x.BaseVolume,
                Symbol = x.MarketName,
                SymbolInformation = GetSymbolInformation(x.MarketName),
                Volume = x.Volume
            };
        }

        internal PublicTrade ToPublicTrade(Bittrex.Trade t, SymbolInformation si)
        {
            return new PublicTrade(si)
            {
                Id = t.Id,
                Price = t.Price,
                Quantity = t.Quantity,
                Side = t.OrderType == "BID" ? TradeSide.Buy : TradeSide.Sell,
                Time = t.TimeStamp.ToLocalTime()
            };
        }

        BittrexApiClient client = new BittrexApiClient();
    }
}

