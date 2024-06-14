using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OsEngine.Market.Servers.BingX.BingXSpot.Entity;
using OsEngine.Market.Servers.Bitfinex.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{



    public class bitf
    {
        // public List<List<BitfinexSecurity>> MyArray { get; set; }
        // public List<BitfinexSecurity> MyArray { get; set; }
        public BitfinexSecurity[] bitfinexSecurities { get; set; }
    }


    public class BitfinexSecurity/*: List<BitfinexSecurity>*/
    {
        // GET https://api-pub.bitfinex.com/v2/tickers?symbols=ALL

        [JsonProperty("SYMBOL")]
        public string Symbol { get; set; }                    //tBTCUSD //SYMBOL


        [JsonProperty("BID")]
        public string Bid { get; set; }                       //10645 //BID


        [JsonProperty("BID_SIZE")]
        public string BidSize { get; set; }                  //73.93854271 //BID_SIZE


        [JsonProperty("ASK")]
        public string Ask { get; set; }                       //10647 //ASK


        [JsonProperty("ASK_SIZE")]
        public string AskSize { get; set; }                  //75.22266119 //ASK_SIZE


        [JsonProperty("DAILY_CHANGE")]
        public string DailyChange { get; set; }              //731.60645389 //DAILY_CHANGE


        [JsonProperty("DAILY_CHANGE_RELATIVE")]
        public string DailyChangeRelative { get; set; }     //0.0738 //DAILY_CHANGE_RELATIVE


        [JsonProperty("LAST_PRICE")]
        public string LastPrice { get; set; }                //10644.00645389 //LAST_PRICE


        [JsonProperty("VOLUME")]
        public string Volume { get; set; }                    //14480.89849423 //VOLUME


        [JsonProperty("HIGH")]
        public string High { get; set; }                      //10766 //HIGH


        [JsonProperty("LOW")]
        public string Low { get; set; }                       //9889.1449809 //LOW
    }
}






//// Include namespace for Newtonsoft.Json NuGet package
//using Newtonsoft.Json;

//// JSON data
//string jsonData = "{\"name\":\"John Doe\",\"age\":30,\"city\":\"New York\"}";

//// C# class
//public class Person
//{
//    public string Name { get; set; }
//    public int Age { get; set; }
//    public string City { get; set; }
//}

//// Deserialize JSON data into C# object
//Person person = JsonConvert.DeserializeObject<Person>(jsonData);

//var customer = JsonSerializer.Deserialize<BitfinexSecurity>(content);



