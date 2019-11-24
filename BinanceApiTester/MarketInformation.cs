using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace BinanceApiTester
{
    public class MarketInformation
    {
        public string Id { get; set; } // unique symbol per exchange
        public string Caption { get; set; }
        public IObservable<decimal> LastPrice => this.lastPrice;
        public IObservable<decimal> Bid => this.bid;
        public IObservable<decimal> Ask => this.ask;

        public MarketInformation(string id, string caption)
        {
            Id = id;
            Caption = caption;
            lastPrice = new Subject<decimal>();
            bid = new Subject<decimal>();
            ask = new Subject<decimal>();
        }

        public void Update(decimal lastPrice)
        {
            this.lastPrice.OnNext(lastPrice);
        }

        public void Update(decimal ask, decimal bid)
        {
            this.bid.OnNext(ask);
            this.bid.OnNext(bid);
        }

        private Subject<decimal> lastPrice;
        private Subject<decimal> bid;
        private Subject<decimal> ask;
    }



}