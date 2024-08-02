using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{
    //public class BitfinexMyTrade
    //{
    //    // POST https://api.bitfinex.com/v2/auth/r/trades/hist
    //    [JsonProperty("ID")]
    //    public string TradeId;                   //402088407, //ID

    //    [JsonProperty("SYMBOL")]
    //    public string Symbol;               //tETHUST", //SYMBOL

    //    [JsonProperty("MTS")]
    //    public string Time;                  //1574963975602, //MTS

    //    [JsonProperty("ORDER_ID")]
    //    public string OrderId;             //34938060782, //ORDER_ID

    //    [JsonProperty("EXEC_AMOUNT")] 
    //    public string Amount;          //-0.2, //EXEC_AMOUNT
        
    //    [JsonProperty("EXEC_PRICE")] 
    //    public string Price;           //153.57, //EXEC_PRICE

    //    [JsonProperty("ORDER_TYPE")] 
    //    public string OrderType;           //MARKET, //ORDER_TYPE

    //    [JsonProperty("ORDER_PRICE")]
    //    public string OrderPrice;          //0, //ORDER_PRICE
       
    //    [JsonProperty("MAKER")] 
    //    public string Maker;                //-1, //MAKER
       
    //    [JsonProperty("FEE")] 
    //    public string Fee;                  //-0.061668, //FEE
       
    //    [JsonProperty("FEE_CURRENCY")] 
    //    public string FeeCurrency;         //USD, //FEE_CURRENCY
       
    //    [JsonProperty("CID")] 
    //    public string ClientOrderId;                  //1234 //CID
    //}
}



//Класс для десериализации ответа на подписку
public class BitfinexSubscriptionResponse
{
    public string Event { get; set; }
    public string Channel { get; set; }
    public int ChanId { get; set; }
    public string Symbol { get; set; }
    public string Pair { get; set; }
}

//Класс для десериализации трейдовых сообщений
public class BitfinexTradeMessage
{
    public int ChannelId { get; set; }
    public string MsgType { get; set; }
    public BitfinexTradeDetails TradeDetails { get; set; }
}

public class BitfinexTradeDetails
{
    public long Id { get; set; }
    public string Symbol { get; set; }
    public long MtsCreate { get; set; }
    public long OrderId { get; set; }
    public float ExecAmount { get; set; }
    public float ExecPrice { get; set; }
    public string OrderType { get; set; }
    public float OrderPrice { get; set; }
    public int Maker { get; set; }
    public float? Fee { get; set; }
    public string FeeCurrency { get; set; }
    public long Cid { get; set; }
}

//Класс для десериализации снимка трейдов (snapshot)
public class BitfinexTradeSnapshot
{
    public int ChannelId { get; set; }
    public List<BitfinexTradeDetails> Trades { get; set; }
}



