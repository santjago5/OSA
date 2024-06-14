using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{


    public class BitfinexResponseWebSocketMessage<T>
    {

        public string code { get; set; }
        public string msg { get; set; }
        public string topic { get; set; }
        public T data { get; set; }

        [JsonProperty("STATUS")]
        public string status { get; set; }//SUCCESS, ERROR, FAILURE, .
        public string TEXT { get; set; }
       

    }


  
    public class BitfinexResponceWebSocketTicker
    {
        public string @event { get; set; }
        public string channel { get; set; }
        public string chanId { get; set; }
        public string symbol { get; set; }
        public string pair { get; set; }

        //"event":"subscribed","channel":"ticker","chanId":224555,"symbol":"tBTCUSD","pair":"BTCUSD"
    }

    public class BitfinexResponceWebSocketTrades
    {
        public string @event { get; set; }
        public string channel { get; set; }
        public string chanId { get; set; }
        public string symbol { get; set; }
        public string pair { get; set; }

        //"event":"subscribed","channel":"trades","chanId":19111,"symbol":"tBTCUSD","pair":"BTCUSD"
    }


    public class BitfinexResponceWebSocketBooks
    {
        public string @event { get; set; }
        public string channel { get; set; }
        public string chanId { get; set; }
        public string symbol { get; set; }
        public string prec { get; set; }
        public string freq { get; set; }
        public string len { get; set; }
        public string subId { get; set; }
        public string pair { get; set; }

        // "event":"subscribe","channel":"book","symbol":"tBTCUSD","prec":"P0","freq":"F0","len":"25","subId": 123
    }



    public class BitfinexResponceWebSocketCandles
    {
        public string @event { get; set; }
        public string channel { get; set; }
        public string chanId { get; set; }
        public string key { get; set; }

        //"event":"subscribed","channel":"candles","chanId":343351,"key":"trade:1m:tBTCUSD"
    }

    public class BitfinexResponceWebSocketAccountInfo
    {

        public string @event { get; set; }
        public string apiKey { get; set; }
        public string authSig { get; set; }
        public string authPayload{ get; set; }
        public string authNonce{ get; set; }
        public string calc{ get; set; }


        //event: "auth",
        //apiKey: api_key,
        //authSig: signature,
        //authPayload: payload,
        //authNonce: authNonce,
        //calc: 1

    }

}



//    public class BitfinexResponceWebSocketTicker
//    {
//        event: "subscribe", 
//        channel: "ticker", 
//        symbol: SYMBOL

//   // response - trading
//   event: "subscribed",
//   channel: "ticker",
//   chanId: CHANNEL_ID,
//   symbol: SYMBOL,
//   pair: PAIR

//          "event":"subscribed","channel":"ticker","chanId":224555,"symbol":"tBTCUSD","pair":"BTCUSD"
//    }
//        public class BitfinexResponceWebSocketTrades
//        {

//            // request

//            event: "subscribe", 
//  channel: "trades", 
//  symbol: SYMBOL


//// response Trading

//  event: "subscribed",
//  channel: "trades",
//  chanId: CHANNEL_ID,
//  symbol: "tBTCUSD"
//  pair: "BTCUSD"


//"event":"subscribed","channel":"trades","chanId":19111,"symbol":"tBTCUSD","pair":"BTCUSD"
//            }


//            public class BitfinexResponceWebSocketBooks
//            {
//                // request

//                event: 'subscribe',
//	channel: 'book',
//	symbol: SYMBOL,
//	prec: PRECISION,
//	freq: FREQUENCY,
//	len: LENGTH,
//	subId: SUBID
//                }

//"event":"subscribe","channel":"book","symbol":"tBTCUSD","prec":"P0","freq":"F0","len":"25","subId": 123

//// response

//	event: 'subscribed',
//	channel: 'book',
//	chanId: CHANNEL_ID,
//	symbol: SYMBOL,
//	prec: PRECISION,
//	freq: FREQUENCY,
//	len: LENGTH,
//	subId: SUBID,
//	pair: PAIR


//"event":"subscribed","channel":"book","chanId":10092,"symbol":"tETHUSD","prec":"P0","freq":"F0","len":"25","subId":123,"pair":"ETHUSD"
//            }

//                public class BitfinexResponceWebSocketCandles
//                {

//// request

//   event: "subscribe",
//   channel: "candles",
//   key: "trade:1m:tBTCUSD"


//// response

//  event: "subscribed",
//  channel: "candles",
//  chanId": CHANNEL_ID,
//  key: "trade:1m:tBTCUSD"


//"event":"subscribed","channel":"candles","chanId":343351,"key":"trade:1m:tBTCUSD"
//            }
//        }
