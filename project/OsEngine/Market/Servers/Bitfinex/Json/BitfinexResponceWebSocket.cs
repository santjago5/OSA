using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{


    public class BitfinexMessage
    {
        [JsonPropertyName("event")]
        public string Event { get; set; }

        [JsonPropertyName("channel")]
        public string Channel { get; set; }

        [JsonPropertyName("chanId")]
        public int ChanId { get; set; }

        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("pair")]
        public string Pair { get; set; }
    }

    public class TradeMessage
    {
        [JsonPropertyName("0")]
        public int ChanId { get; set; }

        [JsonPropertyName("1")]
        public JsonElement Data { get; set; }
    }

    //public class BitfinexTrade
    //{
    //    [JsonPropertyName("0")]
    //    public long Timestamp { get; set; }

    //    [JsonPropertyName("1")]
    //    public long TradeId { get; set; }

    //    [JsonPropertyName("2")]
    //    public double Amount { get; set; }

    //    [JsonPropertyName("3")]
    //    public double Price { get; set; }
    //}



    public class BitfinexResponceWebSocketTicker
    {
        public string Event { get; set; }
        public string Channel { get; set; }
        public string ChanId { get; set; }
        public string Symbol { get; set; }
        public string Pair { get; set; }

        //"event":"subscribed","channel":"ticker","chanId":224555,"symbol":"tBTCUSD","pair":"BTCUSD"
    }

 
    public class BitfinexResponceWebSocketMyTrades//BitfinexTrade
    {
        public string Id { get; set; }
        public string Symbol { get; set; }
        public string Timestamp { get; set; }
        public string OrderId { get; set; }
        public string Amount { get; set; }
        public string Price { get; set; }
        public string OrderType { get; set; }
        public string OrderPrice { get; set; }
        public string Maker { get; set; }
        public string Fee { get; set; }  // Nullable for 'te' events
        public string FeeCurrency { get; set; }  // Nullable for 'te' events
        public string ClientOrderId { get; set; }
    }

    public class BitfinexWebSocketMyTradeMessage
    {
        public string ChannelId { get; set; }
        public string MsgType { get; set; }
        public List<object> TradeArray { get; set; }
    }



    public class BitfinexResponseTrade
    {
        public string Event { get; set; }
        public string Channel { get; set; }
        public string ChanId { get; set; }
        public string Symbol { get; set; }
        public string Pair { get; set; }
    }

    public class BitfinexTradeMessage
    {
        public string ChannelId { get; set; }
        public string MsgType { get; set; }
        public List<object> TradeArray { get; set; }
    }

    //public class BitfinexTrade
    //{
    //    public string Id { get; set; }
    //    public string Timestamp { get; set; }
    //    public string Amount { get; set; }
    //    public decimal Price { get; set; }
    //}








    //public class BitfinexResponceWebSocketBooks
    //{
    //    public string @event { get; set; }
    //    public string channel { get; set; }
    //    public string chanId { get; set; }
    //    public string symbol { get; set; }
    //    public string prec { get; set; }
    //    public string freq { get; set; }
    //    public string len { get; set; }
    //    public string subId { get; set; }
    //    public string pair { get; set; }

    //    // "event":"subscribe","channel":"book","symbol":"tBTCUSD","prec":"P0","freq":"F0","len":"25","subId": 123
    //}


    public class BitfinexResponseDepth
    {
        public string Event { get; set; }
        public string Channel { get; set; }
        public string ChanId { get; set; }
        public string Symbol { get; set; }
        public string Pair { get; set; }
    }



   


    public class BitfinexBookEntry
    {
        public string Price { get; set; }
        public string Count { get; set; }
        public string Amount { get; set; }
    }

    public class BitfinexBookSnapshot
    {
        public int ChannelId { get; set; }
        public List<BitfinexBookEntry> BookEntries { get; set; }
    }

    public class BitfinexBookUpdate
    {
        public int ChannelId { get; set; }
        public BitfinexBookEntry BookEntry { get; set; }
    }



    public class BitfinexResponceWebSocketDepth
    {
       
        public string Event { get; set; }
   
        public string Channel { get; set; }
        
        public int ChanId { get; set; }

        public string Symbol { get; set; }
        [JsonPropertyName("prec")]
        public string Precision { get; set; }
        [JsonPropertyName("freq")]
        public string Frequency { get; set; }
        [JsonPropertyName("len")]
        public string Length { get; set; }
        [JsonPropertyName("subId")]
        public string SubscriptionId { get; set; }     
        public string Pair { get; set; }

        // "event":"subscribe","channel":"book","symbol":"tBTCUSD","prec":"P0","freq":"F0","len":"25","subId": 123
    }


    public class BitfinexTrade
    {
        [JsonPropertyName("0")]
        public long Timestamp { get; set; }

        [JsonPropertyName("1")]
        public long TradeId { get; set; }

        [JsonPropertyName("2")]
        public double Amount { get; set; }

        [JsonPropertyName("3")]
        public double Price { get; set; }

       
    }

    public class BitfinexResponseTrades
    {

        public string Event { get; set; }
        public string Channel { get; set; }
        public string ChanId { get; set; }
        public string Symbol { get; set; }
        public string Pair { get; set; }


        //[JsonPropertyName("0")]
        //public string Timestamp { get; set; }

        //[JsonPropertyName("1")]
        //public string TradeId { get; set; }

        //[JsonPropertyName("2")]
        //public string Amount { get; set; }

        //[JsonPropertyName("3")]
        //public string Price { get; set; }


    }




    //public class BitfinexResponceWebSocketDepth
    //{
    //    [JsonProperty("event")]
    //    public string Event { get; set; }
    //    [JsonProperty("channel")]
    //    public string Channel { get; set; }
    //    [JsonProperty("chanId")]
    //    public int ChannelId { get; set; }
    //    [JsonProperty("symbol")]
    //    public string Symbol { get; set; }
    //    [JsonProperty("prec")]
    //    public string Precision { get; set; }
    //    [JsonProperty("freq")]
    //    public string Frequency { get; set; }
    //    [JsonProperty("len")]
    //    public string Length { get; set; }
    //    [JsonProperty("subId")]
    //    public int SubscriptionId { get; set; }
    //    [JsonProperty("pair")]
    //    public string Pair { get; set; }
    //     "event":"subscribe","channel":"book","symbol":"tBTCUSD","prec":"P0","freq":"F0","len":"25","subId": 123
    //}


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
