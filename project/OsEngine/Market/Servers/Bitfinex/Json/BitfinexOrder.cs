using Newtonsoft.Json;
using OsEngine.Charts.CandleChart.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace OsEngine.Market.Servers.Bitfinex.Json
{
    public class BitfinexOrder
    {
        [JsonProperty("MTS")]
        public string Time; //1678988263842, //MTS
       
        [JsonProperty("TYPE")]
        public string Type;// "ou-req"order cancel request, //TYPE

        [JsonProperty("MESSAGE_ID")]
        public string MessageId;//MESSAGE_ID

        [JsonProperty("ID")]  
        public string Id;//1747566428, //ID

        [JsonProperty("GID")]
        public string Gid; //GID Group Order ID

        [JsonProperty("CID")]
        public string Cid; //1678987199446, //CID Client Order ID

        [JsonProperty("SYMBOL")]
        public string Symbol;  //"tBTCUSD", //SYMBOL

        [JsonProperty("MTS_CREATE")]
        public string TimeCreate;    //1678987199446, //MTS_CREATE

        [JsonProperty("MTS_UPDATE")]
        public string TimeUpdate;    //1678988263843, //MTS_UPDATE
  
        [JsonProperty("AMOUNT")]
        public string Amount;  // 0.25, //AMOUNT

        [JsonProperty("AMOUNT_ORIG")]
        public string AmountOrig;  //    0.1, //AMOUNT_ORIG
           
        [JsonProperty("ORDER_TYPE")]
        public string OrderType;   //"EXCHANGE LIMIT", //ORDER_TYPE
   
        [JsonProperty("TYPE_PREV")]
        public string TypePrev;  //"EXCHANGE LIMIT", //TYPE_PREV

        [JsonProperty("MTS_TIF")]
        public string TimeTif;  //MTS_TIF

        [JsonProperty("FLAGS")]
        public string Flags;  // 0, //FLAGS

        [JsonProperty("STATUS")]
        public string status;   // "ACTIVE", //STATUS

        [JsonProperty("PRICE")]
        public string Price; // 25000, //PRICE

        [JsonProperty("PRICE_AVG")]
        public string PriceAvg;    // 0,153 //PRICE_AVG

        [JsonProperty("PRICE_TRAILING")]
        public string PriceTrailing;    // 0, //PRICE_TRAILING

        [JsonProperty("PRICE_AUX_LIMIT")]
        public string PriceAuxLimit;   // 0, //PRICE_AUX_LIMIT

        [JsonProperty("NOTIFY")]
        public string Notify;    // 0, //NOTIFY

        [JsonProperty("HIDDEN")]
        public string Hidden;     //0, //HIDDEN

        [JsonProperty("PLACED_ID")]
        public string Placed_id;     // null, //PLACED_ID

        [JsonProperty("ROUTING")]
        public string Routing;    //  "API>BFX", //ROUTING

        [JsonProperty("META")]
        public string Meta;      //null //META

        


        //"Submitting update to exchange limit buy order for 0.1 BTC." //TEXT


        //Available order types are: LIMIT, EXCHANGE LIMIT, MARKET, 
        //EXCHANGE MARKET, STOP, EXCHANGE STOP, STOP LIMIT, EXCHANGE STOP LIMIT,
        //TRAILING STOP, EXCHANGE TRAILING STOP, FOK, EXCHANGE FOK, IOC, 
        //EXCHANGE IOC.

    }

    enum orderType
    {
        limit,
        exchangeLimit,
        market,
        exchangeMarket,
        stop,
        exchangeStop,
        stopLimit,
        exchangeStopLimit,
        trailingStop,
        exchangeTrailingStop,
        fok,
        exchangeFok,
        ioc,
        exchangeIoc
    }





    public class BitfinexResponseOrderRest
    {
        [JsonProperty("MTS")]
        public string mts; //1678988263842, //MTS

        [JsonProperty("TYPE")]
        public string type;// "ou-req"order cancel request, //TYPE

        [JsonProperty("MESSAGE_ID")]
        public string messageId;//MESSAGE_ID

        [JsonProperty("DATA ")]
        public string data;   //DATA 

        [JsonProperty("CODE")]
        public string code;    //null, //CODE

        [JsonProperty("STATUS")]
        public string status; // (SUCCESS, ERROR, FAILURE, ..., //STATUS

        [JsonProperty("TEXT")]
        public string text; //text Submitting exchange limit buy order for 0.1 BTC."
    }
}


public class BitfinexOrderResponse
{
    public long MTS { get; set; }
    public string TYPE { get; set; }
    public int MESSAGE_ID { get; set; }
    public object[] AdditionalFields { get; set; }
    //public OrderData DATA { get; set; }
    public int CODE { get; set; }
    public string STATUS { get; set; }
    public string TEXT { get; set; }
}

public class BitfinexOrderData
{
    internal object order;

    public int ID { get; set; }
    public int GID { get; set; }
    public int CID { get; set; }
    public string SYMBOL { get; set; }
    public long MTS_CREATE { get; set; }
    public long MTS_UPDATE { get; set; }
    public float AMOUNT { get; set; }
    public float AMOUNT_ORIG { get; set; }
    public string ORDER_TYPE { get; set; }
    public string TYPE_PREV { get; set; }
    public long MTS_TIF { get; set; }
    public int FLAGS { get; set; }
    public string STATUS { get; set; }
    public float PRICE { get; set; }
    public float PRICE_AVG { get; set; }
    public float PRICE_TRAILING { get; set; }
    public float PRICE_AUX_LIMIT { get; set; }
    public int NOTIFY { get; set; }
    public int HIDDEN { get; set; }
    public int PLACED_ID { get; set; }
    public string ROUTING { get; set; }
    public dynamic META { get; set; }
}




//public class OrderSubmitResponse
//{
//    public long MTS { get; set; }
//    public string TYPE { get; set; }
//    public object MESSAGE_ID { get; set; }
//    public object PLACEHOLDER1 { get; set; }
//    public OrderData[] DATA { get; set; }
//    public object CODE { get; set; }
//    public string STATUS { get; set; }
//    public string TEXT { get; set; }
//}

//public class OrderData
//{
//    public int ID { get; set; }
//    public object GID { get; set; }
//    public long CID { get; set; }
//    public string SYMBOL { get; set; }
//    public long MTS_CREATE { get; set; }
//    public long MTS_UPDATE { get; set; }
//    public float AMOUNT { get; set; }
//    public float AMOUNT_ORIG { get; set; }
//    public string ORDER_TYPE { get; set; }
//    public object TYPE_PREV { get; set; }
//    public object MTS_TIF { get; set; }
//    public object PLACEHOLDER2 { get; set; }
//    public int FLAGS { get; set; }
//    public string STATUS { get; set; }
//    public object PLACEHOLDER3 { get; set; }
//    public object PLACEHOLDER4 { get; set; }
//    public float PRICE { get; set; }
//    public float PRICE_AVG { get; set; }
//    public float PRICE_TRAILING { get; set; }
//    public float PRICE_AUX_LIMIT { get; set; }
//    public object PLACEHOLDER5 { get; set; }
//    public object PLACEHOLDER6 { get; set; }
//    public object PLACEHOLDER7 { get; set; }
//    public int NOTIFY { get; set; }
//    public int HIDDEN { get; set; }
//    public object PLACED_ID { get; set; }
//    public object PLACEHOLDER8 { get; set; }
//    public object PLACEHOLDER9 { get; set; }
//    public string ROUTING { get; set; }
//    public object PLACEHOLDER10 { get; set; }
//    public object PLACEHOLDER11 { get; set; }
//    public object PLACEHOLDER12 { get; set; }
//}
