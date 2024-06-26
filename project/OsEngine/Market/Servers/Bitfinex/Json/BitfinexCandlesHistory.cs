
using Newtonsoft.Json;
using OsEngine.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{
  

    public class BitfinexCandle
    {
        // GET https://api-pub.bitfinex.com/v2/candles/{candle}/{section}

        // Response with section="last" OR  Response with section="hist"

        [JsonProperty("MTS")]
        public string Time { get; set; }           //1678465320000 //MTS 

        [JsonProperty("OPEN")]
        public string Open { get; set; }        //20097 //OPEN

        [JsonProperty("CLOSE")]
        public string Close { get; set; }         //20094 //CLOSE

        [JsonProperty("HIGH")]
        public string High { get; set; }            //20097 //HIGH

        [JsonProperty("LOW")]
        public string Low { get; set; }           //20094 //LOW

        [JsonProperty("VOLUME")]
        public string Volume { get; set; }          //0.07870586 //VOLUME

    }
}

/*
        [JsonProperty("SYMBOL")]
        public string symbol { get; set; }                   //tBTCUSD //SYMBOL

        [JsonProperty("BID")]
        public string bid { get; set; }                      //10645 //BID

        [JsonProperty("BID_SIZE")]
        public string bidSize { get; set; }                  //73.93854271 //BID_SIZE

        [JsonProperty("ASK")]
        public string ask { get; set; }                      //10647 //ASK

        [JsonProperty("ASK_SIZE")]
        public string askSize { get; set; }                  //75.22266119 //ASK_SIZE

        [JsonProperty("DAILY_CHANGE")]
        public string dailyChange { get; set; }              //731.60645389 //DAILY_CHANGE

        [JsonProperty("DAILY_CHANGE_RELATIVE")]
        public string dailyChangeRelative { get; set; }      //0.0738 //DAILY_CHANGE_RELATIVE

        [JsonProperty("LAST_PRICE")]
        public string lastPrice { get; set; }                //10644.00645389 //LAST_PRICE

        [JsonProperty("VOLUME")]
        public string volume { get; set; }                    //14480.89849423 //VOLUME

        [JsonProperty("HIGH")]
        public string high { get; set; }                      //10766 //HIGH

        [JsonProperty("LOW")]
        public string low { get; set; }                       //9889.1449809 //LOW

        [JsonProperty("MTS")]
        public string mts { get; set; }                       //1708354805000 //MTS
 */
//1m: one minute
//5m : five minutes
//15m : 15 minutes
//30m : 30 minutes
//1h : one hour
//3h : 3 hours
//6h : 6 hours
//12h : 12 hours
//1D : one day
//1W : one week
//14D : two weeks
//1M : one month