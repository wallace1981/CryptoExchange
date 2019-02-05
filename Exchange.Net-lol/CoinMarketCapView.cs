using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Gtk;
using Exchange.Net;
using Newtonsoft.Json;
using LiteDB;

namespace CryptoExchange
{
    [System.ComponentModel.ToolboxItem(true)]
    public partial class CoinMarketCapView : Gtk.Bin
    {
        Gdk.Color bearColor = new Gdk.Color(226, 101, 101);
        Gdk.Color bullColor = new Gdk.Color(82, 204, 84);

        public CoinMarketCapView()
        {
            this.Build();
            BuildNodeView();
        }

        public void BuildNodeView()
        {
            var rankColumn = CreateColumn(nodeview1, "#", new CellRendererText(), RenderRank, 0);
            var symbolColumn = CreateColumn(nodeview1, "Name", new CellRendererText(), RenderSymbol, 1);
            var priceColumn = CreateColumn(nodeview1, "Price", new CellRendererText() { Xalign = (float)1.0 }, RenderPrice, -1);
            var changeColumn = CreateColumn(nodeview1, "Rank Change", new CellRendererText(), RenderChange, 3);
            var lastUpdatedColumn = CreateColumn(nodeview1, "Last Update", new CellRendererText(), RenderLastUpdated, 3);
            nodeview1.SearchColumn = 0;
            nodeview1.EnableSearch = true;
            nodeview1.SearchEqualFunc = HandleTreeViewSearchEqualFunc;
            nodeview1.Model = store;
            nodeview1.EnableGridLines = TreeViewGridLines.Horizontal;
        }
    
        static TreeViewColumn CreateColumn(TreeView view, string title, CellRenderer cell, TreeCellDataFunc dataFunc, int sortOrderId)
        {
            //var col = view.AppendColumn(title, cell, dataFunc);
            var col = new TreeViewColumn() { Resizable = true, Clickable = true, Title = title, SortColumnId = sortOrderId };
            col.PackStart(cell, true);
            view.AppendColumn(col);
            col.SetCellDataFunc(cell, dataFunc);
            return col;
        }
    
    
        void RenderRank(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as Ticker;
            if (ticker != null)
            {
                (cell as Gtk.CellRendererText).Text = ticker.Data.Rank.ToString();
            }
        }

        void RenderSymbol(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as Ticker;
            if (ticker != null)
            {
                (cell as Gtk.CellRendererText).Text = ticker.Name;
            }
        }

        void RenderPrice(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as Ticker;
            if (ticker != null)
            {
                (cell as Gtk.CellRendererText).Text = ticker.Data.PriceUsd.ToString("N2");
                //if (ticker.quotes.Count > 0)
                //{
                //    (cell as Gtk.CellRendererText).Text = ticker.quotes.First().Value.price.ToString("N2");
                //}
            }
        }

        void RenderChange(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as Ticker;
            if (ticker != null)
            {
                var diff = ticker.OldRank > 0 ? ticker.Data.Rank - ticker.OldRank : 0;
                if (diff < 0)
                {
                    (cell as CellRendererText).ForegroundGdk = bullColor;
                }
                else if (diff > 0)
                {
                    (cell as CellRendererText).ForegroundGdk = bearColor;
                }
                else
                {
                    (cell as CellRendererText).Foreground = null;
                }
                (cell as Gtk.CellRendererText).Text = diff != 0 ? diff.ToString() : "--";
            }
        }

        void RenderLastUpdated(Gtk.TreeViewColumn column, Gtk.CellRenderer cell, TreeModel model, Gtk.TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as Ticker;
            if (ticker != null)
            {
                (cell as Gtk.CellRendererText).Text = ticker.Data.LastUpdated.ToString();
            }
        }

        private bool HandleTreeViewSearchEqualFunc(TreeModel model, int column, string key, TreeIter iter)
        {
            var ticker = model.GetValue(iter, 0) as Ticker;
            return !ticker.Symbol.Equals(key, StringComparison.CurrentCultureIgnoreCase);
        }

        protected async void OnButton1Clicked(object sender, EventArgs e)
        {
            store.Clear();
            var result = await client.GetTickerAsync();

            // Save snapshot to database.
            var filename = $"coinmarketcap-{DateTime.Now.Year}-{DateTime.Now.Month.ToString("00")}.db";
            using (var db = new LiteDatabase(filename))
            {
                var tickers = db.GetCollection<TickerDB>("tickers");
                //tickers.EnsureIndex(x => x.AssetId);
                //tickers.EnsureIndex(x => x.LastUpdated);
                tickers.Insert(result.Select(x => x.Data));
            }
            // Lookup for most recent snapshot in selected period.
            // Intervals: 1d, 3d, 7d, 14d, 1m
            DateTime[] periods = {
                DateTime.Now.AddDays(-1),
                DateTime.Now.AddDays(-3),
                DateTime.Now.AddDays(-7),
                DateTime.Now.AddDays(-14),
                DateTime.Now.AddMonths(-1)
            };
            var lookback = periods[combobox1.Active];
            filename = $"coinmarketcap-{lookback.Year}-{lookback.Month.ToString("00")}.db";
            using (var db = new LiteDatabase(filename))
            {
                var tickers = db.GetCollection<TickerDB>("tickers");
                tickers.EnsureIndex(x => x.AssetId);
                tickers.EnsureIndex(x => x.LastUpdated);
                var query = tickers.Find(x => x.LastUpdated <= lookback);
                // Update current tickers with rank changes.
                foreach (var q in query)
                {
                    var ticker = result.SingleOrDefault(x => x.Data.AssetId == q.AssetId);
                    if (ticker != null)
                    {
                        ticker.OldRank = q.Rank;
                    }
                }
            }

            // Render UI.
            foreach (var ticker in result)
                store.AppendValues(ticker);
        }

        //private List<string> ListJsonFiles()
        //{
        //    return System.IO.Directory.EnumerateFiles("", "coinmarketcap-*.json").OrderByDescending((x) => x).ToList();
        //}

        //private List<CoinMarketCap.PublicAPI.Ticker> LoadJson(string filename)
        //{
        //    string json = System.IO.File.ReadAllText(filename);
        //    var result = JsonConvert.DeserializeObject<List<CoinMarketCap.PublicAPI.Ticker>>(json);
        //    return result;
        //}

        ListStore store = new ListStore(typeof(CoinMarketCap.PublicAPI.Ticker));
        CoinMarketCapApiClient client = new CoinMarketCapApiClient();
        //LiteDatabase db = new LiteDatabase("cointmarketcap.db");
    }

}

namespace Exchange.Net
{

    public class CoinMarketCapApiClient : ExchangeApiCore
    {
        
        const string PublicAPIv2Url = "https://api.coinmarketcap.com/v1/";


        public async Task<List<Ticker>> GetTickerAsync()
        {
            const string endpoint = "ticker/?limit=0";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<List<CoinMarketCap.PublicAPI.Ticker>>(request);

            //string filename = "coinmarketcap-ticker-" + DateTime.Now.ToString("yyyy-MM-dd");
            //System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(filename, ".json"), response.Content);

            if (response.IsSuccessful)
            {
                // TODO: check for response.Data.metadata.error
                var result = response.Data;
                return result.Select((arg) => new Ticker {
                    Name = arg.name,
                    Symbol = arg.symbol,
                    Data = new TickerDB
                    {
                        AssetId = arg.id,
                        LastUpdated = DateTimeOffset.FromUnixTimeSeconds(arg.last_updated).DateTime.ToLocalTime(),
                        MarketCapacityUsd = arg.market_cap_usd,
                        PriceBtc = arg.price_btc,
                        PriceUsd = arg.price_usd,
                        Rank = arg.rank,
                        Volume24hUsd = arg.volume_usd
                    }
                }).ToList();
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public async Task<List<CoinMarketCap.PublicAPI.Ticker>> GetTickerV2Async()
        {
            const string endpoint = "ticker";
            var request = new RestSharp.RestRequest(endpoint, RestSharp.Method.GET);
            var response = await client.ExecuteTaskAsync<CoinMarketCap.PublicAPI.ResponseWrapper<Dictionary<string, CoinMarketCap.PublicAPI.Ticker>>>(request);

            //string filename = "coinmarketcap-ticker-" + ToUnixTimestamp(DateTime.Now).ToString();
            //System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(filename, ".json"), response.Content);

            if (response.IsSuccessful)
            {
                // TODO: check for response.Data.metadata.error
                var result = response.Data;
                return result.data.Values.ToList();
            }
            else
            {
                throw new Exception(response.ErrorMessage);
            }
        }

        public static async Task<List<CoinMarketCap.PublicAPI.Listing>> GetListingsAsync()
        {
            var content = System.IO.File.ReadAllText("cmc-assets.json");
            await Task.Delay(10);
            var result = JsonConvert.DeserializeObject<CoinMarketCap.PublicAPI.ResponseWrapper<List<CoinMarketCap.PublicAPI.Listing>>>(content);
            return result.data;
        }

        public static List<CoinMarketCap.PublicAPI.Listing> GetListings()
        {
            var content = System.IO.File.ReadAllText("cmc-assets.json");
            var result = JsonConvert.DeserializeObject<CoinMarketCap.PublicAPI.ResponseWrapper<List<CoinMarketCap.PublicAPI.Listing>>>(content);
            return result.data;
        }

        RestSharp.RestClient client = new RestSharp.RestClient(PublicAPIv2Url);
    }

    // This is database record.
    public class TickerDB
    {
        public ObjectId _id { get; set; }
        public string AssetId { get; set; }
        public long Rank { get; set; }
        public decimal PriceUsd { get; set; }
        public decimal PriceBtc { get; set; }
        public decimal Volume24hUsd { get; set; }
        public decimal MarketCapacityUsd { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    // This is for UI.
    public class Ticker
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public TickerDB Data { get; set; }
        public long OldRank { get; set; }
    }
}

// https://coinmarketcap.com/api/
// https://coinmarketcap.com/api/documentation/v1/

namespace CoinMarketCap.PublicAPI
{
    public class Ticker
    {
        public string id { get; set; }
        public string name { get; set; }    // "Bitcoin"
        public string symbol { get; set; }  // "BTC"
        public long rank { get; set; }
        public decimal price_usd { get; set; }
        public decimal price_btc { get; set; }
        public decimal volume_usd { get; set; }
        public decimal market_cap_usd { get; set; }
        public decimal available_supply { get; set; }
        public decimal total_supply { get; set; }
        public decimal percent_change_1h { get; set; }
        public decimal percent_change_24h { get; set; }
        public decimal percent_change_7d { get; set; }
        public long last_updated { get; set; } // 1537693529
    }

    public class TickerV2
    {
        public string id { get; set; }
        public string name { get; set; }    // "Bitcoin"
        public string symbol { get; set; }  // "BTC"
        public long rank { get; set; }
        public decimal circulating_supply { get; set; }
        public decimal total_supply { get; set; }
        public decimal max_supply { get; set; }
        public Dictionary<string, Quote> quotes { get; set; }
        public long last_updated { get; set; } // 1537693529
    }

    public class Quote
    {
        public decimal price { get; set; }
        public decimal volume_24h { get; set; }
        public decimal market_cap { get; set; }
        public decimal percent_change_1h { get; set; }
        public decimal percent_change_24h { get; set; }
        public decimal percent_change_7d { get; set; }
    }

    public class ResponseWrapper<T>
    {
        public T data { get; set; }
        public Metadata metadata { get; set; }
    }

    public class Metadata
    {
        public long timestamp { get; set; }
        public long num_cryptocurrencies { get; set; }
        public string error { get; set; }
    }

    public class Listing
    {
        public long id { get; set; }
        public string name { get; set; }
        public string symbol { get; set; }
        public string website_slug { get; set; }
    }
}

namespace CoinMarketCap.ProfessionalAPI
{
    public class Ticker
    {
        public string id { get; set; }
        public string name { get; set; }
        public string symbol { get; set; }
        public long cmc_rank { get; set; }
        public decimal circulating_supply { get; set; }
        public decimal total_supply { get; set; }
        public decimal max_supply { get; set; }
        public string last_updated { get; set; }    // "2018-06-02T22:51:28.209Z"
        public string date_added { get; set; }      // "2013-04-28T00:00:00.000Z"
        public Dictionary<string, Quote> quotes { get; set; }
    }

    public class Quote
    {
        public decimal price { get; set; }
        public decimal volume_24h { get; set; }
        public decimal market_cap { get; set; }
        public decimal percent_change_1h { get; set; }
        public decimal percent_change_24h { get; set; }
        public decimal percent_change_7d { get; set; }
        public string last_updated { get; set; }    // "2018-08-09T22:53:32.000Z"
    }

    public class ResponseWrapper<T>
    {
        public T data { get; set; }
        public ResponseStatus status { get; set; }
    }

    public class ResponseStatus
    {
        public string timestamp { get; set; }      // "2013-04-28T00:00:00.000Z"
        public long error_code { get; set; }
        public string error_message { get; set; }
        public long elapsed { get; set; }
        public long credit_count { get; set; }
    }
}
