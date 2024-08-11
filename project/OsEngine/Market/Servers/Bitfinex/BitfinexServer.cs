
using Newtonsoft.Json;
using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market.Servers.Bitfinex.Json;
using OsEngine.Market.Servers.Entity;
using RestSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using WebSocket4Net;
using System.Timers;
using Order = OsEngine.Entity.Order;
using Security = OsEngine.Entity.Security;
using BitfinexSecurity = OsEngine.Market.Servers.Bitfinex.Json.BitfinexSecurity;
using BitfinexOrder = OsEngine.Market.Servers.Bitfinex.Json.BitfinexOrder;
using Candle = OsEngine.Entity.Candle;
using Method = RestSharp.Method;
using SuperSocket.ClientEngine;
using Trade = OsEngine.Entity.Trade;
using MarketDepth = OsEngine.Entity.MarketDepth;
using System.Text.Json;
using System.Globalization;
using Timer = System.Timers.Timer;
using System.Linq;










namespace OsEngine.Market.Servers.Bitfinex
{
    public class BitfinexServer : AServer//Класс с конечной логикой коннектора
    {
        public BitfinexServer()
        {
            BitfinexServerRealization realization = new BitfinexServerRealization();// Место создания реализации IServerRealization.
            ServerRealization = realization;

            CreateParameterString(OsLocalization.Market.ServerParamPublicKey, "");
            CreateParameterPassword(OsLocalization.Market.ServerParamSecretKey, "");

        }

        public List<Candle> GetCandleHistory(string nameSec, TimeSpan tf)
        {
            return ((BitfinexServer)ServerRealization).GetCandleHistory(nameSec, tf);
        }
    }

    public class BitfinexServerRealization : IServerRealization //Чтобы AServer мог спокойно через единый интерфейс обращаться к различным вариантам IServerRealization.
    {
        #region 1 Constructor, Status, Connection

        public BitfinexServerRealization()
        {
            ServerStatus = ServerConnectStatus.Disconnect;

            Thread threadForPublicMessages = new Thread(PublicMessageReader)
            {
                IsBackground = true,
                Name = "PublicMessageReaderBitfinex"
            };
            threadForPublicMessages.Start();

            Thread threadForPrivateMessages = new Thread(PrivateMessageReader)
            {
                IsBackground = true,
                Name = "PrivateMessageReaderBitfinex"
            };
            threadForPrivateMessages.Start();

        }

        public DateTime ServerTime { get; set; }


        public void Connect()
        {
            try
            {
                _securities.Clear();
                _portfolios.Clear();


                SendLogMessage("Start Bitfinex Connection", LogMessageType.System);

                _publicKey = ((ServerParameterString)ServerParameters[0]).Value;
                _secretKey = ((ServerParameterPassword)ServerParameters[1]).Value;

                if (string.IsNullOrEmpty(_publicKey) || string.IsNullOrEmpty(_secretKey))

                {
                    SendLogMessage("Connection failed. Authorization exception", LogMessageType.Error);
                    ServerStatus = ServerConnectStatus.Disconnect;
                    DisconnectEvent();
                    return;
                }
                string apiPath = "v2/platform/status";

                RestClient client = new RestClient(_getUrl);
                RestRequest request = new RestRequest(apiPath, Method.GET);
                request.AddHeader("accept", "application/json");

                IRestResponse response = client.Execute(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    //WebSocketPublicMessage = new ConcurrentQueue<string>();
                    //WebSocketPrivateMessage = new ConcurrentQueue<string>();
                    CreateWebSocketConnection();
                }
            }

            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
                SendLogMessage("Connection cannot be open. Bitfinex. Error request", LogMessageType.Error);
                ServerStatus = ServerConnectStatus.Disconnect;
                DisconnectEvent();
            }

        }

        public void Dispose()
        {
            try
            {
                _securities.Clear();
                _portfolios.Clear();

                DeleteWebSocketConnection();
            }
            catch (Exception exception)
            {
                SendLogMessage("Connection closed by Bitfinex. WebSocket Data Closed Event" + exception.ToString(), LogMessageType.System);
            }



            if (ServerStatus != ServerConnectStatus.Disconnect)
            {
                ServerStatus = ServerConnectStatus.Disconnect;
                DisconnectEvent();
            }
        }

        public ServerType ServerType
        {
            get { return ServerType.Bitfinex; }
        }

        public event Action ConnectEvent;

        public event Action DisconnectEvent;



        #endregion


        /// <summary>
        /// настройки коннектора
        /// </summary>
        #region 2 Properties 
        public List<IServerParameter> ServerParameters { get; set; }
        public ServerConnectStatus ServerStatus { get; set; }

        private string _publicKey = "";

        private string _secretKey = "";

        private string _getUrl = "https://api-pub.bitfinex.com";
       
         private string _postUrl = "https://api.bitfinex.com";

        private HttpClient _httpClient = new HttpClient();

        string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()*1000).ToString(); //берет время сервера без учета локального

        #endregion



        /// <summary>
        /// Запрос доступных для подключения бумаг у подключения. 
        /// </summary>
        #region 3 Securities
        RateGate _rateGateGetsecurity = new RateGate(2, TimeSpan.FromMilliseconds(1000));
        private List<Security> _securities = new List<Security>();

        public event Action<List<Security>> SecurityEvent;

        public void GetSecurities()
        {
            _rateGateGetsecurity.WaitToProceed();

            try
            {
                RestClient client = new RestClient(_getUrl);
                RestRequest request = new RestRequest("v2/tickers?symbols=ALL");
                request.AddHeader("accept", "application/json");
                IRestResponse response = client.Get(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonResponse = response.Content;


                    List<List<object>> securityList = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);

                    if (securityList == null)
                    {
                        SendLogMessage("Deserialization resulted in null", LogMessageType.Error);
                        return;
                    }

                    List<BitfinexSecurity> security = new List<BitfinexSecurity>();

                    for (int i = 0; i < 3; i++)
                  //  for (int i = 0; i < securityList.Count; i++)
                    {
                        var item = securityList[i];

                        BitfinexSecurity ticker = new BitfinexSecurity

                        {

                            Symbol = item[0].ToString(),
                            Bid = (item[1]).ToString(),
                            BidSize = (item[2]).ToString(),
                            Ask = (item[3]).ToString(),
                            AskSize = (item[4]).ToString(),
                            DailyChange = (item[5]).ToString(),
                            DailyChangeRelative = (item[6]).ToString(),
                            LastPrice = (item[7]).ToString(),
                            Volume = (item[8]).ToString(),
                            High = (item[9]).ToString(),
                            Low = (item[10]).ToString()
                        };

                        security.Add(ticker);

                    }

                    UpdateSecurity(security);

                }

                //else
                //{
                //    //SendLogMessage($"Result: LogMessageType.Error\n"
                //    //    + $"Message: {stateResponse.message}", LogMessageType.Error);
                //}


                else
                {
                    SendLogMessage("Securities request exception. Status: " + response.StatusCode, LogMessageType.Error);

                }

                if (_securities.Count > 0)

                {
                    SendLogMessage("Securities loaded. Count: " + _securities.Count, LogMessageType.System);

                    SecurityEvent?.Invoke(_securities);
                }
            }
            catch (Exception exception)
            {
                SendLogMessage("Securities request exception" + exception.ToString(), LogMessageType.Error);

            }
        }

        private void UpdateSecurity(List<BitfinexSecurity> security)
        {


            try
            {
                if (security == null || security.Count == 0)
                {
                    return;
                }

                for (int i = 0; i < security.Count; i++)
                {
                    var securityData = security[i];


                    Security newSecurity = new Security();

                    newSecurity.Name = securityData.Symbol;
                    newSecurity.NameFull = securityData.Symbol;
                    newSecurity.NameClass = "Spot";
                    newSecurity.NameId = Convert.ToString(securityData.Symbol);
                    newSecurity.Exchange = ServerType.Bitfinex.ToString();

                    newSecurity.Lot = 1;

                    newSecurity.SecurityType = SecurityType.CurrencyPair;

                    newSecurity.State = SecurityStateType.Activ;

                    newSecurity.PriceStep = newSecurity.Decimals.GetValueByDecimals();

                    newSecurity.PriceStepCost = newSecurity.PriceStep;

                    //newSecurity.Decimals = Convert.ToInt32(securityData.LastPrice);////////////////


                    // newSecurity.DecimalsVolume = newSecurity.Decimals;


                    _securities.Add(newSecurity);////////////////////////;




                    SecurityEvent(_securities);
                }


            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

            SecurityEvent?.Invoke(_securities);
        }

        private SecurityType GetSecurityType(string type)
        {
            SecurityType _securityType = SecurityType.None;

            switch (type)
            {
                case "f":
                    _securityType = SecurityType.Futures;
                    break;
                case "t":
                    _securityType = SecurityType.CurrencyPair;
                    break;
            }
            return _securityType;
        }
        #endregion


        /// <summary>
        /// Запрос доступных портфелей у подключения. 
        /// </summary>
        #region 4 Portfolios

        private List<Portfolio> _portfolios = new List<Portfolio>();

        public event Action<List<Portfolio>> PortfolioEvent;

        private RateGate _rateGatePortfolio = new RateGate(1, TimeSpan.FromMilliseconds(200));

        public void GetPortfolios()///////////////////////////
        {
            if (_portfolios.Count != 0)
            {
                PortfolioEvent?.Invoke(_portfolios);
            }

            CreateQueryPortfolio();
        }




        private void CreateQueryPortfolio()
        {
            _rateGatePortfolio.WaitToProceed();

            try
            {

                string apiPath = "v2/auth/r/wallets";
                // string apiPath = "/auth/r/wallets";
                string signature = $"/api/{apiPath}{nonce}";

                string sig = ComputeHmacSha384(_secretKey, signature);
                string body = "";
                var client = new RestClient("https://api.bitfinex.com/v2/auth/r/wallets");

                var request = new RestRequest("", Method.POST);
                request.AddHeader("bfx-nonce", nonce);
                request.AddHeader("bfx-apikey", _publicKey);
                request.AddHeader("bfx-signature", sig);

                request.AddJsonBody(body);

                request.AddParameter("accept", "application/json");

                try
                {

                    IRestResponse response = client.Execute(request);


                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonResponse = response.Content;


                        List<List<object>> walletList = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);

                        if (walletList == null)
                        {
                            SendLogMessage("walletList is null", LogMessageType.Error);
                            return;
                        }

                        List<BitfinexPortfolioRest> wallets = new List<BitfinexPortfolioRest>();


                        for (int i = 0; i < walletList.Count; i++)
                        {
                            var walletData = walletList[i];

                            BitfinexPortfolioRest wallet = new BitfinexPortfolioRest
                            {
                                PortfolioName = walletData[0]?.ToString(),
                                Currency = walletData[1]?.ToString(),
                                Balance = Convert.ToDecimal(walletData[2]).ToString().ToDecimal(),////??????????????????
                                UnsettledInterest = Convert.ToDecimal(walletData[3]),
                                AvailableBalance = Convert.ToDecimal(walletData[4]),
                                LastChange = walletData[5]?.ToString()


                            };
                            wallets.Add(wallet);
                        }
                        UpdatePortfolio(wallets);

                    }
                    else
                    {
                        Console.WriteLine($"Error Status code {response.StatusCode}: {response.Content}");
                    }

                }
                catch (Exception exception)
                {
                    SendLogMessage(exception.ToString(), LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void UpdatePortfolio(List<BitfinexPortfolioRest> assets)
        {
            try
            {
                Portfolio portfolio = new Portfolio
                {
                    Number = "Bitfinex",
                    ValueBegin = 1,
                    ValueCurrent = 1
                };


                if (assets == null || assets.Count == 0)
                {
                    return;
                }


                for (int i = 0; i < assets.Count; i++)
                {
                    if (decimal.TryParse(assets[i].AvailableBalance.ToString(), out decimal availableBalance) &&
                        decimal.TryParse(assets[i].UnsettledInterest.ToString(), out decimal unsettledInterest))
                    {


                        PositionOnBoard position = new PositionOnBoard
                        {
                            PortfolioName = assets[i].PortfolioName,
                            ValueBegin = availableBalance,
                            ValueCurrent = availableBalance,
                            ValueBlocked = unsettledInterest,
                            SecurityNameCode = assets[i].Currency
                        };


                        portfolio.SetNewPosition(position);
                    }
                    else
                    {
                        SendLogMessage($"Failed to parse balance or interest for asset {assets[i].Currency}", LogMessageType.Error);
                    }
                }
                PortfolioEvent?.Invoke(new List<Portfolio> { portfolio });

            }
            catch (Exception exception)
            {
                SendLogMessage($"{exception.Message} {exception.StackTrace}", LogMessageType.Error);
            }
        }



        #endregion


        /// <summary>
        /// Запросы данных по свечкам и трейдам. 
        /// </summary>
        #region 5 Data 

        private readonly RateGate _rateGateCandleHistory = new RateGate(700, TimeSpan.FromSeconds(30));

        public List<Trade> GetTickDataToSecurity(Security security, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            return null;
        }
        public List<Candle> GetLastCandleHistory(Security security, TimeFrameBuilder timeFrameBuilder, int candleCount)
        {
            DateTime startTime = DateTime.UtcNow - TimeSpan.FromMinutes(timeFrameBuilder.TimeFrameTimeSpan.Minutes * candleCount);
            DateTime endTime = DateTime.UtcNow.AddMilliseconds(-10);
            DateTime actualTime = startTime;

            return GetCandleDataToSecurity(security, timeFrameBuilder, startTime, endTime, actualTime);////////////

        }



        public List<Candle> GetCandleHistory(string nameSec, TimeSpan tf, int CountToLoad, DateTime timeEnd)
        {

            int needToLoadCandles = CountToLoad;


            List<Candle> candles = new List<Candle>();
            DateTime fromTime = timeEnd - TimeSpan.FromMinutes(tf.TotalMinutes * CountToLoad);//перепрыгивает на сутки назад


            do
            {

                // ограничение Bitfinex: For each query, the system would return at most 1500 pieces of data. To obtain more data, please page the data by time.
                int maxCandleCountToLoad = 10000;
                int limit = Math.Min(needToLoadCandles, maxCandleCountToLoad);

                List<Candle> rangeCandles; //= new List<Candle>(); //////не нужен новый список 

                rangeCandles = CreateQueryCandles(nameSec, GetStringInterval(tf), fromTime, timeEnd);

                if (rangeCandles == null)
                    return null; // нет данных

                rangeCandles.Reverse();

                candles.InsertRange(0, rangeCandles);

                if (candles.Count != 0)
                {
                    timeEnd = candles[0].TimeStart;
                }

                needToLoadCandles -= limit;

            }

            while (needToLoadCandles > 0);

            return candles;
        }



        public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)/////string
        {

            if (startTime != actualTime)
            {
                startTime = actualTime;
            }

            if (actualTime > endTime)
            {
                return null;
            }

            if (startTime > endTime)
            {
                return null;
            }

            if (endTime > DateTime.Now)
            {
                endTime = DateTime.Now;
            }


            int countNeedToLoad = GetCountCandlesFromSliceTime(startTime, endTime, timeFrameBuilder.TimeFrameTimeSpan);

            return GetCandleHistory(security.NameFull, timeFrameBuilder.TimeFrameTimeSpan, countNeedToLoad, endTime);
        }



        private int GetCountCandlesFromSliceTime(DateTime startTime, DateTime endTime, TimeSpan tf)
        {
            TimeSpan TimeSlice = endTime - startTime;
            if (tf.Hours != 0)
            {


                return Convert.ToInt32(TimeSlice.TotalHours / tf.TotalHours);
            }
            else
            {

                return Convert.ToInt32(TimeSlice.TotalMinutes / tf.Minutes);
            }
        }

        private string GetStringInterval(TimeSpan tf)
        {
            // Type of candlestick patterns: 1min, 3min, 5min, 15min, 30min, 1hour, 3hour, 6hour,  12hour, 1day, 1week, 14 days,1 month
            // return tf.Minutes != 0 ? $"{tf.Minutes}min" : $"{tf.Hours}hour";
            if (tf.Minutes != 0)
            {
                return $"{tf.Minutes}min";
            }
            else
            {
                return $"{tf.Hours}hour";
            }
        }



        private List<Candle> CreateQueryCandles(string nameSec, string tf, DateTime timeFrom, DateTime timeTo)//////////TimeSpan interval
        {


            //string exchangeType =  BitfinexExchangeType.SpotExchange.ToString();
            //if (nameSec.StartsWith("t"))
            //{
            //    exchangeType = BitfinexExchangeType.SpotExchange.ToString();
            //}
            //if (nameSec.StartsWith("f"))
            //{
            //    exchangeType = BitfinexExchangeType.FuturesExchange.ToString();
            //}
            //else
            //{
            //    exchangeType = BitfinexExchangeType.MarginExchange.ToString();
            //}



            _rateGateCandleHistory.WaitToProceed(100);



            DateTime yearBegin = new DateTime(1970, 1, 1);

            //var timeStampStart = timeFrom - yearBegin;
            //var startTimeMilliseconds = timeStampStart.TotalMilliseconds;
            // string startTime = Convert.ToInt64(startTimeMilliseconds).ToString();

            //var timeStampEnd = timeTo - yearBegin;
            //var endTimeMilliseconds = timeStampEnd.TotalMilliseconds;
            //string endTime = Convert.ToInt64(endTimeMilliseconds).ToString();


            string startTime = Convert.ToInt64((timeFrom - yearBegin).TotalMilliseconds).ToString();
            string endTime = Convert.ToInt64((timeTo - yearBegin).TotalMilliseconds).ToString();

            // string section = timeFrom !=DateTime.Today ?"hist":"last";////////////



            RestClient client = new RestClient($"https://api-pub.bitfinex.com/v2/candles/trade:{1m}:{nameSec}/hist?start={startTime}&end={endTime}");//////TF 

            var request = new RestRequest("", Method.GET);//поменяла строки

            //var request = new RestRequest("");

            request.AddHeader("accept", "application/json");

            var response = client.Get(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                string jsonResponse = response.Content;

                // List<List<BitfinexCandle>> candles = JsonConvert.DeserializeObject<List<List<BitfinexCandle>>>(jsonResponse);//////////////////////////////
                List<List<object>> candles = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);


                if (candles == null || candles.Count == 0)
                {
                    return null;
                }

                List<BitfinexCandle> candleList = new List<BitfinexCandle>();

                //for (int i = 0; i < candles.Count; i++) ////////////////////////
                for (int i = 0; i < 2; i++)
                {
                    var candleData = candles[i];

                    if (candleData[0] == null ||
                        candleData[1] == null ||
                        candleData[2] == null ||
                        candleData[3] == null ||
                        candleData[4] == null ||
                        candleData[5] == null)
                    {
                        SendLogMessage("Candle data contains null values", LogMessageType.Error);

                        continue;
                    }

                    if (Convert.ToDecimal(candleData[1]) == 0 ||
                        Convert.ToDecimal(candleData[2]) == 0 ||
                        Convert.ToDecimal(candleData[3]) == 0 ||
                        Convert.ToDecimal(candleData[4]) == 0 ||
                        Convert.ToDecimal(candleData[5]) == 0)
                    {
                        SendLogMessage("Candle data contains zero values", LogMessageType.Error);

                        continue;
                    }



                    BitfinexCandle newCandle = new BitfinexCandle

                    {
                        Time = candleData[0].ToString(),
                        Open = candleData[1].ToString(),
                        Close = candleData[2].ToString(),
                        High = candleData[3].ToString(),
                        Low = candleData[4].ToString(),
                        Volume = candleData[5].ToString()
                    };

                    candleList.Add(newCandle);
                }

                return ConvertToCandles(candleList);
            }

            else
            {
                SendLogMessage($"Http State Code: {response.StatusCode}", LogMessageType.Error);
            }
            return null;
        }

        private List<Candle> ConvertToCandles(List<BitfinexCandle> candleList)
        {
            List<Candle> candles = new List<Candle>();

            for (int i = 0; i < candleList.Count; i++)
            {
                var candle = candleList[i];
                try
                {

                    if (string.IsNullOrEmpty(candle.Time) || string.IsNullOrEmpty(candle.Open) ||
                        string.IsNullOrEmpty(candle.Close) || string.IsNullOrEmpty(candle.High) ||
                        string.IsNullOrEmpty(candle.Low) || string.IsNullOrEmpty(candle.Volume))
                    {
                        SendLogMessage("Candle data contains null or empty values", LogMessageType.Error);
                        continue;
                    }

                    //if (Convert.ToDecimal(candle.Open) == 0 || Convert.ToDecimal(candle.Close) == 0 || Convert.ToDecimal(candle.High) == 0 || Convert.ToDecimal(candle.Low) == 0 || Convert.ToDecimal(candle.Volume) == 0)
                    //{
                    //    SendLogMessage("Candle data contains zero values", LogMessageType.Error);
                    //    continue;
                    //}

                    Candle newCandle = new Candle
                    {
                        State = CandleState.Finished,
                        TimeStart = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(candle.Time)),
                        Open = Convert.ToDecimal(candle.Open),
                        Close = Convert.ToDecimal(candle.Close),
                        High = Convert.ToDecimal(candle.High),
                        Low = Convert.ToDecimal(candle.Low),
                        Volume = Convert.ToDecimal(candle.Volume)
                    };

                    candles.Add(newCandle);
                }
                catch (FormatException ex)
                {
                    SendLogMessage($"Format exception: {ex.Message}", LogMessageType.Error);
                }

            }
            return candles;
        }


        #endregion


        /// <summary>
        /// Создание вёбсокет соединения. 
        /// </summary>
        #region  6 WebSocket creation



        private WebSocket _webSocketPublic;
        private WebSocket _webSocketPrivate;



        private readonly string _webSocketPublicUrl = "wss://api-pub.bitfinex.com/ws/2";
        private readonly string _webSocketPrivateUrl = "wss://api.bitfinex.com/ws/2";
        private Timer _pingTimer;
        // Set up a timer to send pings every 15 seconds


        private void CreateWebSocketConnection()
        {
            try
            {


                //_subscriptionsPublic.Clear();
                //_subscriptionsPrivate.Clear();

                if (_webSocketPublic != null)
                {
                    return;
                }

                //_socketPublicIsActive = false;////////////
                //  _socketPrivateIsActive = false;////////////////


                _webSocketPublic = new WebSocket(_webSocketPublicUrl)
                {
                    EnableAutoSendPing = true,
                    AutoSendPingInterval = 15
                };

                _webSocketPublic.Opened += WebSocketPublic_Opened;
                _webSocketPublic.Closed += WebSocketPublic_Closed;
                _webSocketPublic.MessageReceived += WebSocketPublic_MessageReceived;
                _webSocketPublic.Error += WebSocketPublic_Error;

                _webSocketPublic.Open();


                _webSocketPrivate = new WebSocket(_webSocketPrivateUrl)
                {
                    EnableAutoSendPing = true,
                    AutoSendPingInterval = 15
                };

                _webSocketPrivate.Opened += WebSocketPrivate_Opened;
                _webSocketPrivate.Closed += WebSocketPrivate_Closed;
                _webSocketPrivate.MessageReceived += WebSocketPrivate_MessageReceived;
                _webSocketPrivate.Error += WebSocketPrivate_Error;

                _webSocketPrivate.Open();


                //_pingTimer = new Timer(25000); // 25 seconds
                //_pingTimer.Elapsed += SendPing;
                //_pingTimer.AutoReset = true;



            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }


        private void DeleteWebSocketConnection()
        {
            try
            {
                if (_webSocketPublic != null)
                {
                    try
                    {
                        _webSocketPublic.Close();
                    }
                    catch (Exception exception)
                    {
                        SendLogMessage(exception.ToString(), LogMessageType.System);
                    }

                    _webSocketPublic.Opened -= WebSocketPublic_Opened;
                    _webSocketPublic.Closed -= WebSocketPublic_Closed;
                    _webSocketPublic.MessageReceived -= WebSocketPublic_MessageReceived;
                    _webSocketPublic.Error -= WebSocketPublic_Error;
                    _webSocketPublic = null;

                }
                if (_webSocketPrivate != null)
                {

                    try
                    {
                        _webSocketPrivate.Close();
                    }
                    catch (Exception exception)
                    {
                        SendLogMessage(exception.ToString(), LogMessageType.System);
                    }


                    _webSocketPrivate.Opened -= WebSocketPrivate_Opened;
                    _webSocketPrivate.Closed -= WebSocketPrivate_Closed;
                    _webSocketPrivate.MessageReceived -= WebSocketPrivate_MessageReceived;
                    _webSocketPrivate.Error -= WebSocketPrivate_Error;
                    _webSocketPrivate = null;

                }

            }

            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.System);
            }

        }


        private bool _socketPublicIsActive;

        private bool _socketPrivateIsActive;

        private bool IsDispose;
        public event Action<MarketDepth> MarketDepthEvent;



        #endregion

        //private List<MarketDepth> _depths;

        /// <summary>
        /// Обработка входящих сообщений от вёбсокета. И что важно в данном конкретном случае, Closed и Opened методы обязательно должны находиться здесь,
        /// </summary>
        #region  7 WebSocket events

        private static WebSocket websocket;



        private void WebSocketPublic_Opened(object sender, EventArgs e)
        {


            _socketPublicIsActive = true;//отвечает за соединение

            CheckActivationSockets();
            //  _pingTimer.Start();////////////////////////
            SendLogMessage("Connection to public data is Open", LogMessageType.System);

        }


        private void WebSocketPublic_Closed(object sender, EventArgs e)
        {
            try
            {
                // _pingTimer.Stop();

                if (ServerStatus != ServerConnectStatus.Disconnect)
                {

                    ServerStatus = ServerConnectStatus.Disconnect;
                    DisconnectEvent();
                }
                SendLogMessage("Connection Closed by Bitfinex.WebSocket Public сlosed Event ", LogMessageType.Error);


            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void WebSocketPublic_Error(object sender, ErrorEventArgs e)
        {
            try
            {
                ErrorEventArgs error = e;

                if (error.Exception != null)
                {
                    SendLogMessage(error.Exception.ToString(), LogMessageType.Error);
                }
                if (e == null)
                {
                    return;
                }

                if (string.IsNullOrEmpty(e.ToString()))
                {
                    return;
                }

                if (WebSocketPublicMessage == null)
                {
                    return;
                }


            }
            catch (Exception exception)
            {
                SendLogMessage("Data socket exception" + exception.ToString(), LogMessageType.Error);
            }
        }

        private string _currentSymbol;

        private void WebSocketPublic_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {

                if (e == null)
                {
                    return;
                }
                if (string.IsNullOrEmpty(e.Message))
                {
                    return;
                }

                if (WebSocketPublicMessage == null)
                {
                    return;
                }

                if (ServerStatus == ServerConnectStatus.Disconnect)
                {
                    return;
                }


                WebSocketPublicMessage.Enqueue(e.Message);


            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }




        private List<BitfinexBookEntry> depthBids = new List<BitfinexBookEntry>();
        private List<BitfinexBookEntry> depthAsks = new List<BitfinexBookEntry>();
        public long tradeChannelId;
        public long bookChannelId;



        /// <summary>
        /// //77777777777777777777777777777777777777777
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>



        // Снимок(snapshot) : Структура данных содержит массив массивов, где каждый внутренний массив представляет собой запись в стакане(book entry).
        //Обновление(update) : Структура данных содержит только один массив, представляющий одну запись в стакане(book entry).

        public void ProcessOrderBookResponse(string jsonResponse, int chanelId) //string symbol)
        {
            // Десериализация JSON-ответа в JsonElement
            var jsonDocument = JsonDocument.Parse(jsonResponse);
            var root = jsonDocument.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                var eventType = root.GetProperty("event").GetString();
                if (eventType == "info" || eventType == "auth" || eventType == "hb")
                {
                    return;
                }
            }


            // Извлечение Channel ID
            int channelId = root[0].GetInt32();

            // Извлечение основного содержимого
            var secondElement = root[1];




            // Проверка, является ли это снимком или обновлением
            if ((secondElement.ValueKind == JsonValueKind.Array && secondElement[0].ValueKind == JsonValueKind.Array) && (chanelId == channelId))
            {
                // Это снимок (snapshot)
                var bookEntries = new List<BitfinexBookEntry>();

                // Перебор всех записей в снимке с использованием цикла for
                for (int i = 0; i < secondElement.GetArrayLength(); i++)
                {
                    var entryElement = secondElement[i]; // Извлечение текущей записи

                    // Создание нового объекта BitfinexBookEntry и заполнение его полей
                    var bookEntry = new BitfinexBookEntry
                    {
                        Price = (entryElement[0]).ToString(),//.ToString().ToDecimal() // Извлечение и установка значения Price
                        Count = (entryElement[1]).ToString(),  // Извлечение и установка значения Count
                        Amount = (entryElement[2]).ToString() // Извлечение и установка значения Amount
                    };

                    // Добавление объекта в список bookEntries
                    bookEntries.Add(bookEntry);
                }

                // Обработка снимка
                ProcessSnapshot(bookEntries);
            }
            else
            {
                // Это обновление (update)
                var bookEntry = new BitfinexBookEntry
                {
                    Price = (secondElement[0]).ToString(), // Извлечение и установка значения Price
                    Count = (secondElement[1]).ToString(),  // Извлечение и установка значения Count
                    Amount = (secondElement[2]).ToString() // Извлечение и установка значения Amount
                };

                // Обработка обновления
                ProcessUpdate(bookEntry);
            }
        }



        private void ProcessSnapshot(List<BitfinexBookEntry> bookEntries)
        {
            // Обновление внутреннего состояния стакана
            depthBids.Clear(); // Очистка текущих записей о bid'ах
            depthAsks.Clear(); // Очистка текущих записей о ask'ах

            // Перебор всех записей в снимке с использованием цикла for
            for (int i = 0; i < bookEntries.Count; i++)
            {
                var entry = bookEntries[i]; // Извлечение текущей записи

                // Если количество больше 0, это bid

                // if (Convert.ToDecimal(entry.Amount) > 0)
                if (entry.Amount.Contains("-"))
                {
                    // Добавление новой записи в список ask'ов
                    depthAsks.Add(new BitfinexBookEntry
                    {
                        Price = entry.Price, // Преобразование цены в строку
                        Count = entry.Count, // Преобразование количества в строку
                        Amount = entry.Amount // Преобразование объема в строку
                    });
                }
                else // В противном случае, это bid
                {
                    // Добавление новой записи в список bid'ов
                    depthBids.Add(new BitfinexBookEntry
                    {
                        Price = entry.Price, // Преобразование цены 
                        Count = entry.Count, // Преобразование количества 
                        Amount = entry.Amount // Преобразование объема 
                    });
                }
            }

            // Вызов обновления стакана
            UpdateOrderBook(); // Вызов метода для обновления стакана
        }
        private void ProcessUpdate(BitfinexBookEntry entry)////не понятно что тут
        {
            // Логика обновления стакана, аналогичная приведенной выше
            var bookEntry = new BitfinexBookEntry
            {
                Price = entry.Price.ToString(),
                Count = entry.Count.ToString(),
                Amount = entry.Amount.ToString()
            };

            // Обновление или удаление записи в стакане
            if (Convert.ToUInt32(entry.Count) == 0)
            {
                // if (Convert.ToDecimal(entry.Amount )> 0)

                if (entry.Amount.Contains("-"))
                {
                    depthAsks.RemoveAll(ask => ask.Price == bookEntry.Price);
                }
                else
                {
                    depthBids.RemoveAll(bid => bid.Price == bookEntry.Price);
                }
            }
            else

            {

                // if (Convert.ToUInt32(entry.Amount )> 0)
                if (entry.Amount.Contains("-"))
                {
                    bool updated = false;
                    for (int i = 0; i < depthAsks.Count; i++)
                    {
                        if (depthAsks[i].Price == bookEntry.Price)
                        {
                            depthAsks[i] = bookEntry;
                            updated = true;
                            break;
                        }
                    }
                    if (!updated)
                    {
                        depthAsks.Add(bookEntry);
                    }
                }
                else
                {
                    bool updated = false;
                    for (int i = 0; i < depthBids.Count; i++)
                    {
                        if (depthBids[i].Price == bookEntry.Price)
                        {
                            depthBids[i] = bookEntry;
                            updated = true;
                            break;
                        }
                    }
                    if (!updated)
                    {
                        depthBids.Add(bookEntry);
                    }
                }
            }

            // Вызов обновления стакана
            UpdateOrderBook();
        }

        private void UpdateOrderBook()
        {
            // Создание нового объекта MarketDepth для обновления стакана
            MarketDepth newMarketDepth = new MarketDepth
            {
                SecurityNameCode = _currentSymbol,
                Time = DateTime.UtcNow,
                Asks = new List<MarketDepthLevel>(),
                Bids = new List<MarketDepthLevel>()
            };

            // Перебор всех bid уровней и добавление их в новый стакан
            for (int i = 0; i < depthBids.Count; i++)
            {
                var bid = depthBids[i];

                try
                {
                    decimal bidPrice = Convert.ToDecimal(bid.Price, CultureInfo.InvariantCulture);
                    decimal bidAmount = Convert.ToDecimal(bid.Amount, CultureInfo.InvariantCulture);

                    newMarketDepth.Bids.Add(new MarketDepthLevel
                    {
                        Price = bidPrice,
                        Bid = bidAmount
                    });
                }
                catch (FormatException)
                {
                    // Логирование или обработка ошибки при неверном формате
                    Console.WriteLine($"Invalid bid format: Price={bid.Price}, Amount={bid.Amount}");
                }
                catch (Exception ex)
                {
                    // Логирование или обработка других исключений
                    Console.WriteLine($"Unexpected error: {ex.Message}");
                }
            }

            // Перебор всех ask уровней и добавление их в новый стакан
            for (int i = 0; i < depthAsks.Count; i++)
            {
                var ask = depthAsks[i];
                try
                {
                    decimal askPrice = Convert.ToDecimal(ask.Price, CultureInfo.InvariantCulture);
                    decimal askAmount = Convert.ToDecimal(ask.Amount, CultureInfo.InvariantCulture);

                    newMarketDepth.Asks.Add(new MarketDepthLevel
                    {
                        Price = askPrice,
                        Ask = Math.Abs(askAmount)
                    });
                }
                catch (FormatException)
                {
                    // Логирование или обработка ошибки при неверном формате
                    Console.WriteLine($"Invalid ask format: Price={ask.Price}, Amount={ask.Amount}");
                }
                catch (Exception ex)
                {
                    // Логирование или обработка других исключений
                    Console.WriteLine($"Unexpected error: {ex.Message}");
                }

            }

            // Вызов события обновления стакана
            MarketDepthEvent?.Invoke(newMarketDepth);
        }



        private void WebSocketPrivate_Opened(object sender, EventArgs e)
        {

            GenerateAuthenticate();
            _socketPrivateIsActive = true;//отвечает за соединение
            CheckActivationSockets();
            SendLogMessage("Connection to private data is Open", LogMessageType.System);

        }

        private void GenerateAuthenticate()
        {


            string payload = $"AUTH{nonce}";
            string signature = ComputeHmacSha384(payload, _secretKey);

            //string signature;

            //using (var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(_apiSecret)))
            //{
            //    signature = BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "").ToLower();
            //}
            // Create the payload
            var authMessage = new
            {
                @event = "auth",
                apiKey = _publicKey,
                authSig = signature,
                authPayload = payload,
                authNonce = nonce

            };
            string authMessageJson = JsonConvert.SerializeObject(authMessage);

            _webSocketPrivate.Send(authMessageJson);

        }

        private void CheckActivationSockets()
        {

            if (_socketPublicIsActive == false)
            {
                return;
            }

            if (_socketPrivateIsActive == false)
            {
                return;
            }

            try
            {
                if (ServerStatus != ServerConnectStatus.Connect &&
                    _webSocketPublic != null && _webSocketPrivate != null &&
                    _webSocketPublic.State == WebSocketState.Open && _webSocketPrivate.State == WebSocketState.Open) //добавлена проверка

                {
                    ServerStatus = ServerConnectStatus.Connect;
                    ConnectEvent();
                }
                SendLogMessage("All sockets activated.", LogMessageType.System);

            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

        }

        private void WebSocketPrivate_Closed(object sender, EventArgs e)
        {
            try
            {
                SendLogMessage("Connection Closed by Bitfinex. WebSocket Private сlosed Event", LogMessageType.Error);

                if (ServerStatus != ServerConnectStatus.Disconnect)
                {
                    ServerStatus = ServerConnectStatus.Disconnect;
                    DisconnectEvent();
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void WebSocketPrivate_Error(object sender, ErrorEventArgs e)
        {
            try
            {
                var error = e;

                if (error.Exception != null)
                {
                    SendLogMessage(error.Exception.ToString(), LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void WebSocketPrivate_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                if (ServerStatus == ServerConnectStatus.Disconnect)
                {
                    return;
                }

                if (e == null || string.IsNullOrEmpty(e.Message))
                {
                    return;
                }

                if (e.Message.Contains("hb"))
                {
                    return;
                }

                if (WebSocketPrivateMessage == null)
                {
                    return;
                }

                WebSocketPrivateMessage.Enqueue(e.Message); // Добавление полученного сообщения в очередь


            }

            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }
        //private void SendPing(object sender, EventArgs e)// ElapsedEventArgs e)
        //{
        //    // Проверяем, инициализирован ли _webSocketPublic и открыт ли он
        //    if (_webSocketPublic != null && _webSocketPublic.State == WebSocketState.Open)
        //    {
        //        string Ping = "{\"event\":\"ping\",\"cid\":1234}";

        //        _webSocketPublic.Send(Ping);
        //    }
        //    else
        //    {
        //        Console.WriteLine("WebSocket is not open. Ping not sent.");
        //    }


        //}




        #endregion

        /// <summary>
        /// Проверка вёбсокета на работоспособность путём отправки ему пингов.
        /// </summary>
        #region  8 WebSocket check alive


        #endregion



        /// <summary>
        /// Подписка на бумагу.С обязательным контролем скорости и кол-ву запросов к методу Subscrible через rateGate.
        /// </summary>
        #region  9 Security subscrible ( или WebSocket security subscrible)

        private RateGate _rateGateSecurity = new RateGate(1, TimeSpan.FromMilliseconds(250));

        List<Security> _subscribledSecurities = new List<Security>();

        public void Subscrible(Security security)
        {
            try
            {

                CreateSubscribleSecurityMessageWebSocket(security);

                Thread.Sleep(200);
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

        }

        private void CreateSubscribleSecurityMessageWebSocket(Security security)
        {

            try
            {
                _rateGateSecurity.WaitToProceed();


                if (ServerStatus == ServerConnectStatus.Disconnect)
                {
                    return;
                }

                for (int i = 0; i < _subscribledSecurities.Count; i++)
                {
                    if (_subscribledSecurities[i].Name == security.Name &&
                        _subscribledSecurities[i].NameClass == security.NameClass)

                    {
                        return;
                    }
                }


                _subscribledSecurities.Add(security);

                //Subscribing to account info
                //_webSocketPrivate.Send($"{{\"event\":\"auth\",\"apiKey\":\"{_publicKey}\",\"authSig\":{signature}\",\"authPayload\":\"{ payload}\",\" authNonce\":{authNonce}\",\"calc\": 1\"}}"); 

                ////tiker websocket-event: "subscribe", channel: "ticker",symbol: SYMBOL 
                // _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"ticker\",\"symbol\":{security.Name}\"}}");


                ////candle websocket  //event: "subscribe",//channel: "candles", //key: "trade:1m:tBTCUSD"
                //  _webSocketPublic.Send($"{{\"event\": \"subscribe\", \"channel\": \"candles\", \"key\": \"trade:1m:{security.Name}\"}}");


                _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"book\",\"symbol\":\"{security.Name}\",\"prec\":\"P0\",\"freq\":\"F0\",\"len\":\"25\"}}");//стакан

                _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"trades\",\"symbol\":\"{security.Name}\"}}"); //трейды


            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }




        //private void UnsubscribeFromAllWebSockets()
        //{
        //    if (_webSocketPublic == null || _webSocketPrivate == null)
        //    { return; }

        //    for (int i = 0; i < _subscribedSecurities.Count; i++)
        //    {
        //        string securityName = _subscribedSecurities[i];

        //        _webSocketPublic.Send($"{{\"event\":\"unsubscribe\", \"chanId\": \"{chanId}\"}}"); 

        //        _webSocketPrivate.Send($"{{\"event\":\"unsubscribe\", \"chanId\": \"{chanId}\"}}");
        //    }
        //}



        #endregion


        /// <summary>
        ///Разбор сообщений от сокета и отправка их наверх
        /// </summary>
        #region  10 WebSocket parsing the messages



        public event Action<Trade> NewTradesEvent;

        public event Action<Order> MyOrderEvent;//новые мои ордера

        public event Action<MyTrade> MyTradeEvent;//новые мои сделки

        private ConcurrentQueue<string> WebSocketPublicMessage = new ConcurrentQueue<string>();

        private ConcurrentQueue<string> WebSocketPrivateMessage = new ConcurrentQueue<string>();


        private void PublicMessageReader()
        {
            Thread.Sleep(1000);

            while (true)
            {
                try
                {
                    if (ServerStatus != ServerConnectStatus.Connect)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    if (WebSocketPublicMessage.IsEmpty)
                    {
                        Thread.Sleep(1);
                        continue;
                    }


                    WebSocketPublicMessage.TryDequeue(out string message);


                    if (message == null)
                    {
                        continue;
                    }


                    if (message.Contains("info") || message.Contains("hb"))
                    {
                        continue;
                    }
                    if (message.Contains("book"))
                    {


                        BitfinexResponceWebSocketDepth responseDepth = JsonConvert.DeserializeObject<BitfinexResponceWebSocketDepth>(message);
                        _currentSymbol = responseDepth.Symbol;
                        bookChannelId = Convert.ToInt64(responseDepth.ChanId);
                        continue;


                    }

                    if (message.Contains("trade"))
                    {
                        SubscriptionResponseTrade responseTrade = JsonConvert.DeserializeObject<SubscriptionResponseTrade>(message);
                        tradeChannelId = Convert.ToInt64(responseTrade.ChanId);
                        continue;

                    }

                    if (message.StartsWith("["))
                    {
                        var jsonDocument = JsonDocument.Parse(message);
                        var root = jsonDocument.RootElement;

                        if (root.ValueKind == JsonValueKind.Object)
                        {
                            var eventType = root.GetProperty("event").GetString();
                            if (eventType == "info" || eventType == "auth" || eventType == "hb")
                            {
                                continue;
                            }
                        }
                        if (root[0].ValueKind != JsonValueKind.Number)
                        {
                            SendLogMessage("Неверный формат Channel ID", LogMessageType.Error);
                            return;
                        }
                        int chanelId = root[0].GetInt32();



                        // if(root[0].ValueKind == JsonValueKind.Array && root[1].ValueKind == JsonValueKind.Array)
                        if (root[0].ValueKind == JsonValueKind.Number && root[1].ValueKind == JsonValueKind.Array)
                        {


                            //ProcessOrderBookResponse(message, _currentSymbol);
                            ProcessOrderBookResponse(message, chanelId);
                        }


                        // Извлечение Channel ID


                        // Извлечение основного содержимого


                        if (root[1].ValueKind == JsonValueKind.String)
                        {
                            string messageTypeString = root[1].GetString();


                            if (messageTypeString == "tu" || messageTypeString == "te")
                            {
                                ProcessTradeResponse(message);

                            }
                        }


                        if (root[0].ValueKind != JsonValueKind.Number)
                        {
                            SendLogMessage("Неверный формат Channel ID", LogMessageType.Error);
                            return;
                        }

                    }



                }

                catch (Exception exception)
                {
                    Thread.Sleep(2000);
                    SendLogMessage(exception.ToString(), LogMessageType.Error);
                }
            }
        }


        private void ProcessTradeResponse(string message)
        {
            // Десериализация JSON-ответа в JsonDocument
            var jsonDocument = JsonDocument.Parse(message);
            var root = jsonDocument.RootElement;

            // Извлечение Channel ID и MSG_TYPE
            int channelId = root[0].GetInt32(); // CHANNEL_ID
            var secondElement = root[1];

            // Проверка, является ли второй элемент массивом или строкой
            if (secondElement.ValueKind == JsonValueKind.String)
            {
                string msgType = secondElement.GetString(); // MSG_TYPE

                // Проверка типа сообщения (msgType должно быть "te" или "tu" для обновления трейдов)
                if (msgType == "te" || msgType == "tu")
                {
                    // Извлечение данных трейда
                    var tradeDataElement = root[2];

                    // Создание объекта BitfinexTradeUpdate из данных
                    var trade = new BitfinexTradeUpdate
                    {
                        Id = tradeDataElement[0].ToString(),
                        Timestamp = tradeDataElement[1].ToString(),
                        Amount = tradeDataElement[2].ToString(),
                        Price = tradeDataElement[3].ToString()
                    };

                    // Обработка обновления трейда
                    UpdateTrade(trade);
                }
                else
                {
                    SendLogMessage("Неизвестный тип сообщения: " + msgType, LogMessageType.Error);
                }
            }
            else if (secondElement.ValueKind == JsonValueKind.Array)
            {
                // Это снимок (snapshot)
                var tradeArray = secondElement.EnumerateArray().ToList();

                // Подсчёт количества элементов в массиве
                int tradeCount = tradeArray.Count;

                // Использование цикла for для итерации по массиву
                List<BitfinexTradeUpdate> tradeList = new List<BitfinexTradeUpdate>();

                for (int i = 0; i < tradeCount; i++)
                {
                    var tradeElement = tradeArray[i];

                    // Создание объекта BitfinexTradeUpdate из данных
                    var trade = new BitfinexTradeUpdate
                    {
                        Id = tradeElement[0].ToString(),
                        Timestamp = tradeElement[1].ToString(),
                        Amount = tradeElement[2].ToString(),
                        Price = tradeElement[3].ToString()
                    };

                    tradeList.Add(trade);
                }

                // Обработка снимка
                ProcessSnapshot(tradeList);
            }
            else
            {
                SendLogMessage("Неизвестный формат сообщения", LogMessageType.Error);
            }
        }


        //private void ProcessTradeResponse(string message)
        //{

        //    // Десериализация JSON-ответа в JsonDocument
        //    var jsonDocument = JsonDocument.Parse(message);
        //    var root = jsonDocument.RootElement;

        //    // Извлечение Channel ID и MSG_TYPE
        //    int channelId = root[0].GetInt32(); // CHANNEL_ID
        //    string msgType = root[1].GetString(); // MSG_TYPE

        //    // Проверка типа сообщения (msgType должно быть "te" для обновления трейдов)
        //    if (msgType == "te" || msgType == "tu")
        //    {
        //        // Извлечение данных трейда
        //        string tradeDataJson = root[2].GetRawText();

        //        // Десериализация данных трейда как списка объектов
        //        var tradeList = JsonConvert.DeserializeObject<List<object>>(tradeDataJson);



        //        // Создание объекта BitfinexTradeUpdate из списка
        //        var trade = new BitfinexTradeUpdate
        //        {
        //            Id = tradeList[0].ToString(),
        //            Timestamp = tradeList[1].ToString(),
        //            Amount = tradeList[2].ToString(),
        //            Price = tradeList[3].ToString()
        //        };

        //        // Обработка обновления трейда
        //        UpdateTrade(trade);
        //    }


        //     else if (root[1].ValueKind == JsonValueKind.Array)
        //    {
        //        // Это снимок (snapshot)
        //        var tradeArray = root[1].EnumerateArray();

        //        // Перебор всех записей в снимке
        //        var tradeList = new List<BitfinexTradeUpdate>();
        //        foreach (var tradeElement in tradeArray)
        //        {
        //            // Десериализация данных трейда как списка объектов
        //            var trade = new BitfinexTradeUpdate
        //            {
        //                Id = tradeElement[0].ToString(),
        //                Timestamp = tradeElement[1].ToString(),
        //                Amount = tradeElement[2].ToString(),
        //                Price = tradeElement[3].ToString()
        //            };

        //            tradeList.Add(trade);
        //        }

        //        // Обработка снимка
        //        ProcessSnapshot(tradeList);
        //    }
        //    else
        //    {
        //        SendLogMessage("Неизвестный формат сообщения", LogMessageType.Error);
        //    }
        //}

        private void ProcessSnapshot(List<BitfinexTradeUpdate> tradeList)
        {
            // Логика обработки снимка трейдов
            for (int i = 0; i < tradeList.Count; i++)
            {
                // Извлечение текущего трейда из списка
                var trade = tradeList[i];

                // Пример обработки каждого трейда в снимке
                SendLogMessage($"Обработка трейда: ID = {trade.Id}, Timestamp = {trade.Timestamp}, Amount = {trade.Amount}, Price = {trade.Price}", LogMessageType.System);
            }
        }

        private void UpdateTrade(BitfinexTradeUpdate tradeUpdate)
        {
            try
            {
                // Проверка и конвертация поля Price
                decimal price;
                try
                {
                    price = decimal.Parse(tradeUpdate.Price, NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    throw new FormatException($"Неверный формат цены: {tradeUpdate.Price}");
                }

                // Проверка и конвертация поля Amount
                decimal amount;
                try
                {
                    amount = decimal.Parse(tradeUpdate.Amount, NumberStyles.Float, CultureInfo.InvariantCulture);//Параметр NumberStyles.Float указывает, что строка может содержать числа в экспоненциальной нотации и/или использовать плавающую точку.
                                                                                                                 //Он позволяет строке содержать символы +, -, E, e, . (точка), а также цифры.
                                                                                                                 // Параметр CultureInfo.InvariantCulture указывает, что при преобразовании строки в число нужно использовать инвариантную культуру.Инвариантная культура основана на английском языке и используется для стандартного форматирования чисел и дат.
                                                                                                                 //  CultureInfo.InvariantCulture гарантирует, что при анализе строки используется стандартное форматирование чисел, независимо от региональных настроек компьютера.
                }
                catch (FormatException)
                {
                    throw new FormatException($"Неверный формат количества: {tradeUpdate.Amount}");
                }

                // Проверка и конвертация поля Timestamp
                long timestamp;
                try
                {
                    timestamp = Convert.ToInt64(tradeUpdate.Timestamp);
                }
                catch (FormatException)
                {
                    throw new FormatException($"Неверный формат метки времени: {tradeUpdate.Timestamp}");
                }

                Trade newTrade = new Trade
                {
                    SecurityNameCode = _currentSymbol,
                    Price = price,
                    Id = tradeUpdate.Id,
                    Time = TimeManager.GetDateTimeFromTimeStamp(timestamp),
                    Volume = Math.Abs(amount),
                    Side = amount > 0 ? Side.Buy : Side.Sell
                };

                ServerTime = newTrade.Time;
                NewTradesEvent?.Invoke(newTrade);
            }
            catch (FormatException ex)
            {
                // Логирование ошибки при конвертации
                SendLogMessage($"Ошибка формата при обновлении трейда: {ex.Message}", LogMessageType.Error);
            }
            catch (Exception ex)
            {
                // Логирование всех других ошибок
                SendLogMessage($"Ошибка при обновлении трейда: {ex.Message}", LogMessageType.Error);
            }
        }





        private void PrivateMessageReader()
        {
            Thread.Sleep(1000);

            while (true)
            {
                try
                {
                    if (WebSocketPrivateMessage.IsEmpty)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    if (ServerStatus != ServerConnectStatus.Connect)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    WebSocketPrivateMessage.TryDequeue(out string message);

                    if (message == null)
                    {
                        continue;
                    }

                    if (message.Contains("pong"))
                    {
                        continue;
                    }

                    if (message.Contains("auth"))
                    {
                        return;
                        //GenerateAuthenticate();////////////////
                        //continue;
                    }
                    if (message.Contains("info"))
                    {
                        return;
                        // continue;
                    }

                    var jsonDocument = JsonDocument.Parse(message);
                    var root = jsonDocument.RootElement;
                    var eventType = root.GetProperty("event").GetString();

                    //if (root.ValueKind == JsonValueKind.Object)

                    //{
                    //  'os'(снимок заказа), 'on'(новый заказ), 'ou'(обновление заказа) и 'oc'(отмена заказа).

                    //if (eventType == "os" || eventType == "on" || eventType == "ou" || eventType == "oc")
                    //{
                    //    UpdateOrder(message);
                    //    continue;
                    //}
                    // }

                    //if (message.Contains(""))
                    //{
                    //    UpdatePortfolio(message);
                    //    continue;
                    //}

                    //jsonDocument = JsonDocument.Parse(message);
                    //root = jsonDocument.RootElement;
                    //var json = JToken.Parse(message);

                    //if (json[1].Value<string>() == "on" || json[1].Value<string>() == "ou")
                    //{
                    //    // Это сообщение о новом ордере или об обновлении ордера
                    //    Console.WriteLine("Order update received: " + json);
                    //}
                    // Проверка, что это массив и имеет хотя бы 2 элемента
                    if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 1)
                    {
                        // Извлечение второго элемента массива (тип события)
                        // string eventType = root[1].GetString();

                        // Проверка на тип события "on" или "ou"
                        if (eventType == "on" || eventType == "ou")
                        {

                            UpdateOrder(message);
                            continue;

                        }
                    }
                    if (root[0].ValueKind == JsonValueKind.Array) //&& root[0].ValueKind = JsonValueKind.Number)
                    {
                        int chanelId = root[0].GetInt32();

                        if (root.ValueKind == JsonValueKind.Object)
                        {
                            if (eventType == "tu" || eventType == "te")
                            {
                                if (chanelId == 0)
                                {
                                    UpdateMyTrade(message);
                                    continue;
                                }
                            }
                        }
                    }

                }
                catch (Exception exception)
                {
                    Thread.Sleep(5000);
                    SendLogMessage(exception.ToString(), LogMessageType.Error);

                }
            }
        }





        private void UpdateMyTrade(string message)
        {
            var response = JsonConvert.DeserializeObject<BitfinexMyTrade>(message);



            var item = response;
            var time = Convert.ToInt64(item.Time);

            var myTrade = new MyTrade
            {
                Time = TimeManager.GetDateTimeFromTimeStamp(time),
                SecurityNameCode = item.Symbol,
                NumberOrderParent = item.OrderId,
                Price = Convert.ToDecimal(item.OrderPrice),
                NumberTrade = item.TradeId,
                NumberPosition = item.OrderId,
                Side = item.Amount.Contains("-") ? Side.Sell : Side.Buy,
                Volume = Convert.ToDecimal(item.Amount)
            };

            MyTradeEvent?.Invoke(myTrade);
        }
        private void UpdateOrder(string message)
        {
            var response = JsonConvert.DeserializeObject<BitfinexOrder>(message);

            if (response == null)
            {
                return;
            }

            if (response.Equals("EXCHANGE MARKET")) /*&& (response.Status == OrderStateType.Activ)*/
            {
                return;
            }

            var newOrder = new Order
            {
                SecurityNameCode = response.Symbol,
                TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(response.MtsCreate)),
                NumberUser = Convert.ToInt32(response.Cid),
                NumberMarket = response.Id,
                Side = response.Amount.Equals("-") ? Side.Sell : Side.Buy,
                //State =item.status,
                //State = (OrderStateType)Enum.Parse(typeof(OrderStateType), item.status),
                TypeOrder = response.Equals("EXCHANGE MARKET") ? OrderPriceType.Market : OrderPriceType.Limit,
                Volume = response.Status.Equals("OPEN") ? Convert.ToDecimal(response.Amount) : Convert.ToDecimal(response.AmountOrig),
                Price = Convert.ToDecimal(response.Price),
                ServerType = ServerType.Bitfinex,
                PortfolioNumber = "Bitfinex"
            };

            MyOrderEvent?.Invoke(newOrder);
        }


        //ACTIVE - На исполнении
        //        EXECUTED - Исполнена
        //        CANCELED - Отменена


        //                    if (baseMessage.status == "ACTIVE")
        //                    {
        //                        order.State = OrderStateType.Activ;
        //                    }
        //                    else if (baseMessage.status == "EXECUTED")
        //                    {
        //                        order.State = OrderStateType.Done;
        //                    }
        //                    else if (baseMessage.status == "PARTIALLY FILLED")
        //{
        //    order.State = OrderStateType.Patrial;
        //}

        //else if (baseMessage.status == "CANCELED")
        //{
        //    lock (_changePriceOrdersArrayLocker)
        //    {
        //        DateTime now = DateTime.Now;
        //        for (int i = 0; i < _changePriceOrders.Count; i++)
        //        {
        //            if (_changePriceOrders[i].TimeChangePriceOrder.AddSeconds(2) < now)
        //            {
        //                _changePriceOrders.RemoveAt(i);
        //                i--;
        //                continue;
        //            }

        //            //if (_changePriceOrders[i].MarketId == order.NumberMarket)
        //            //{
        //            //    return null;
        //            //}
        //        }
        //    }

        //    if (string.IsNullOrEmpty(baseMessage.amount))
        //    {
        //        order.State = OrderStateType.Cancel;
        //    }
        //    else if (baseMessage.amount == "0")
        //    {
        //        order.State = OrderStateType.Cancel;
        //    }
        //    else
        //    {
        //        try
        //        {
        //            decimal volFilled = baseMessage.amount.ToDecimal();

        //            if (volFilled > 0)
        //            {
        //                order.State = OrderStateType.Done;
        //            }
        //            else
        //            {
        //                order.State = OrderStateType.Cancel;
        //            }
        //        }
        //        catch
        //        {
        //            order.State = OrderStateType.Cancel;
        //        }
        //    }
        //}

        //return order;
        //                }



        #endregion


        /// <summary>
        /// посвящённый торговле. Выставление ордеров, отзыв и т.д
        /// </summary>
        #region  11 Trade

        private RateGate _rateGateSendOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));//уточнить задержку  Таймфрейм в формате отрезка времени. TimeSpan.

        private readonly RateGate _rateGateCancelOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));

        private RateGate _rateGateChangePriceOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));

        private string subscriptionResponse;



        public void SendOrder(Order order)
        {
            _rateGateSendOrder.WaitToProceed();

            BitfinexOrder data = new BitfinexOrder
            {
                Cid = order.NumberUser.ToString(),
                Symbol = order.SecurityNameCode,
                Amount = order.Volume.ToString().Replace(",", "."),
                OrderType = order.TypeOrder.ToString().ToUpper(),
                Price = order.TypeOrder == OrderPriceType.Market ? null : order.Price.ToString().Replace(",", "."),
                MtsCreate = order.TimeCreate.ToString(),
                Status = order.State.ToString()
            };


           // string apiPath = "/auth/w/order/submit";
            string apiPath = "v2/auth/w/order/submit";

            // Создаем объект тела запроса
            var body = new
            {
                type = data.OrderType,
                symbol = data.Symbol,
                price = data.Price,
                amount = data.Amount
            };

            // Сериализуем объект тела в JSON
            string bodyJson = System.Text.Json.JsonSerializer.Serialize(body);

            // Создаем строку для подписи
            string signature = $"/api/{apiPath}{nonce}{bodyJson}";
           // string signature = $"/v2/auth/w/order/submit{nonce}{bodyJson}";
            //string signature = $"/auth/w/order/submit{nonce}{bodyJson}";
            // Создаем клиента RestSharp
            var client = new RestClient(_postUrl);

            // Создаем запрос типа POST
            var request = new RestRequest(apiPath, Method.POST);
            string sig = ComputeHmacSha384(_secretKey, signature);

            // Добавляем заголовки
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            // Добавляем тело запроса в формате JSON
            request.AddJsonBody(body); //

     
            try
            {
                // Отправляем запрос и получаем ответ
                var response = client.Execute(request);

                // Выводим тело ответа
                string responseBody = response.Content;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var jsonResponse = response.Content;
                    BitfinexOrder stateResponse = JsonConvert.DeserializeObject<BitfinexOrder>(jsonResponse);

                    SendLogMessage($"Order num {order.NumberUser} on exchange.", LogMessageType.Trade);

                    order.State = OrderStateType.Activ;
                    order.NumberMarket = stateResponse.Cid;
                   

                    MyOrderEvent?.Invoke(order);
                }
                else
                {
                    CreateOrderFail(order);
                    SendLogMessage("Order Fail", LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                CreateOrderFail(order);
                SendLogMessage("Order send exception " + exception.ToString(), LogMessageType.Error);
            }
        }

        //public void SendOrder(Order order)//Submit
        //{
        //    _rateGateSendOrder.WaitToProceed();


        //    BitfinexOrder data = new BitfinexOrder();

        //    data.Cid = order.NumberUser.ToString();
        //    data.Symbol = order.SecurityNameCode;

        //    data.Amount = (order.Side == Side.Buy ? "" : "-").ToString().ToLower();
        //    data.TypePrev = order.TypeOrder.ToString().ToUpper();//надо с большой буквы или нет
        //    data.Price = order.TypeOrder == OrderPriceType.Market ? null : order.Price.ToString().Replace(",", ".");

        //    // data.Amount = order.Volume.ToString().Replace(",", ".");
        //    data.Amount = order.Volume.ToString().Replace(",", ".");
        //    data.MtsCreate = order.TimeCreate.ToString();
        //    data.Status = order.State.ToString();



        //    JsonSerializerSettings dataSerializerSettings = new JsonSerializerSettings();
        //    dataSerializerSettings.NullValueHandling = NullValueHandling.Ignore;// если маркет-ордер, то игнорим параметр цены

        //    string jsonRequest = JsonConvert.SerializeObject(data, dataSerializerSettings);

        //     /
        //     

        //    string apiPath = "/auth/w/order/submit";
        //    string signature = $"{_postUrl}{apiPath}{nonce}";
        //    string sig = ComputeHmacSha384(_secretKey, signature);

        //    RestClient client = new RestClient(_postUrl);
        //    RestRequest request = new RestRequest(apiPath, Method.POST);
        //    request.AddHeader("accept", "application/json");
        //    request.AddHeader("bfx-nonce", nonce);
        //    request.AddHeader("bfx-apikey", _publicKey);
        //    request.AddHeader("bfx-signature", sig);
        //    //request.AddJsonBody($"{{\"type\":\"{order.TypeOrder}\",\"symbol\":\"{order.SecurityNameCode}\",\"amount\":\"{order.Volume}\",\"price\":\"{order.Price}\"}}");
        //    request.AddJsonBody($"{{\"type\":\"{data.TypePrev}\",\"symbol\":\"{data.Symbol}\",\"amount\":\"{data.Amount}\",\"price\":\"{data.Price}\"}}");

        //    IRestResponse response = client.Execute(request);

        //    try
        //    {
        //        if (response.StatusCode == HttpStatusCode.OK)
        //        {
        //            var jsonResponse = response.Content;
        //            BitfinexOrder stateResponse = JsonConvert.DeserializeObject<BitfinexOrder>(jsonResponse);

        //            SendLogMessage($"Order num {order.NumberUser} on exchange.", LogMessageType.Trade);

        //            order.State = OrderStateType.Activ;
        //            order.NumberMarket = stateResponse.Cid;

        //            SendLogMessage($"Order num {order.NumberUser} on exchange.", LogMessageType.Trade);


        //            MyOrderEvent?.Invoke(order);
        //        }

        //        else
        //        {
        //            CreateOrderFail(order);
        //            //order.State = OrderStateType.Fail;

        //            SendLogMessage("Order Fail", LogMessageType.Error);


        //            //if (response.Content != null)
        //            //{
        //            //    SendLogMessage("Fail reasons: "
        //            //  + response.Content, LogMessageType.Error);
        //            //}

        //        }
        //    }
        //    catch (Exception exception)
        //    {
        //        CreateOrderFail(order);
        //        SendLogMessage("Order send exception " + exception.ToString(), LogMessageType.Error);
        //    }

        //    MyOrderEvent.Invoke(order);
        //}



        private void CreateOrderFail(Order order)
        {
            order.State = OrderStateType.Fail;

            MyOrderEvent?.Invoke(order);
        }

        public void CancelAllOrders()
        {

            string apiPath = "v2/auth/w/order/cancel/multi";
            string signature = $"{_postUrl}{apiPath}{nonce}";// {bodyJson}";
            string sig = ComputeHmacSha384(_secretKey, signature);

            //post https://api.bitfinex.com/v2/auth/w/order/cancel/multi
            var client = new RestClient(_postUrl);
            var request = new RestRequest(apiPath);

            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);
            //request.AddJsonBody("{\"all\":1}", false); //all\":1 отменить все ордера
            IRestResponse response = client.Execute(request);
            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    if (response != null)
                    {
                        SendLogMessage($"Code: {response.StatusCode}"
                            , LogMessageType.Error);
                    }

                    else
                    {

                        SendLogMessage($" {response}", LogMessageType.Error);
                    }
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

        }


        private RateGate rateGateCancelAllOrder = new RateGate(1, TimeSpan.FromMilliseconds(350));
        public void CancelAllOrdersToSecurity(Security security)
        {
            rateGateCancelAllOrder.WaitToProceed();

            string apiPath = "https://api.bitfinex.com/v2/auth/r/orders/security";// посмотреть
            string signature = $"{_postUrl}{apiPath}{nonce}";
            string sig = ComputeHmacSha384(_secretKey, signature);
            RestClient client = new RestClient(_postUrl);
            RestRequest request = new RestRequest(apiPath, Method.POST);
            request.AddHeader("accept", "application/json");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);
            IRestResponse response = client.Execute(request);
            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {



                    if (response != null)
                    {
                        SendLogMessage($"Code: {response.StatusCode}"
                            , LogMessageType.Error);
                    }

                    else
                    {

                        SendLogMessage($" {response}", LogMessageType.Error);
                    }
                }
            }
            catch (Exception exception)
            {


                SendLogMessage(exception.ToString(), LogMessageType.Error);


            }

        }


        public void CancelOrder(Order order)
        {
            _rateGateCancelOrder.WaitToProceed();

            string apiPath = "/auth/w/order/cancel";
            string signature = $"/api/{apiPath}{nonce}";//{body};
            string sig = ComputeHmacSha384(_secretKey, signature);
            var client = new RestClient(_postUrl);
            var request = new RestRequest(apiPath, Method.POST);

            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            // request.AddJsonBody($"{{\"id\":{order.id}}}", false);

            IRestResponse response = client.Execute(request);

            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    SendLogMessage($"Order canceled", LogMessageType.Error); // ignore
                }

                else
                {
                    CreateOrderFail(order);
                    SendLogMessage($" {response}", LogMessageType.Error);
                }

            }
            catch (Exception exception)
            {
                CreateOrderFail(order);
                SendLogMessage(exception.ToString(), LogMessageType.Error);

            }

        }

        private string ComputeHmacSha384(string apiSecret, string signature)
        {

            using (var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(apiSecret)))
            {
                byte[] output = hmac.ComputeHash(Encoding.UTF8.GetBytes(signature));
                return BitConverter.ToString(output).Replace("-", "").ToLower();
            }
        }

        public void ChangeOrderPrice(Order order, decimal newPrice)
        {
            try
            {

            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
                SendLogMessage("Conection cannot be open.Bitfinex.Error request", LogMessageType.Error);
                //ServerStatus = ServerConnectStatus.Disconnect;
                //DisconnectEvent();
            }
        }


        #endregion

        /// <summary>
        /// Место расположение HTTP запросов.
        /// </summary>_httpPublicClient
        #region  12 Queries

        public void GetAllActivOrders()
        {
            throw new NotImplementedException();
        }

        public void GetOrderStatus(Order order)
        {
            throw new NotImplementedException();
        }

        public List<Candle> GetCandleDataToSecurity(string security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            throw new NotImplementedException();
        }

        #endregion
        /// <summary>
        /// Логирование.
        /// </summary>
        #region 13 Log

        public event Action<string, LogMessageType> LogMessageEvent;


        private void SendLogMessage(string message, LogMessageType messageType)
        {
            LogMessageEvent?.Invoke(message, messageType);
        }

        #endregion





        public enum BitfinexSubType
        {
            Trades,
            MarketDepth,
            Porfolio,
            Positions,
            Orders,
            MyTrades
        }
    }
}

//kraken
//public static string ConvertToSocketSecName(string nameInRest)
//{
//    string result = "";
//    //XXBTZEUR = "XBT/EUR"

//    if (nameInRest == "XZECXXBT")
//    {
//        return "ZEC/XBT";
//    }
//    if (nameInRest == "XZECZEUR")
//    {
//        return "ZEC/EUR";
//    }
//    if (nameInRest == "XZECZUSD")
//    {
//        return "ZEC/USD";
//    }

//    if (nameInRest.StartsWith("XX"))
//    {
//        result = nameInRest.Replace("XX", "X");
//        result = result.Replace("Z", "/");
//        return result;
//    }
//    if (nameInRest.StartsWith("XETH"))
//    {
//        result = nameInRest.Replace("XX", "X");
//        result = result.Replace("Z", "/");

//        return result;
//    }
//    if (nameInRest.EndsWith("ETH"))
//    {
//        string pap = nameInRest.Replace("ETH", "");

//        if (pap.Length == 3)
//        {
//            result = pap + "/" + "ETH";
//        }
//        else
//        {
//            result = pap + "/" + "ETH";
//        }

//    }
//    if (nameInRest.EndsWith("EUR"))
//    {
//        string pap = nameInRest.Replace("EUR", "");
//        if (pap.Length == 3)
//        {
//            result = pap + "/" + "EUR";
//        }
//        else
//        {
//            result = pap + "/" + "EUR";
//        }

//    }
//    if (nameInRest.EndsWith("USD"))
//    {
//        if (nameInRest == "XETHZUSD")
//        {

//        }
//        string pap = nameInRest.Replace("USD", "");

//        if (pap.StartsWith("X") &&
//            pap.EndsWith("Z"))
//        {
//            pap = pap.Replace("Z", "");
//            pap = pap.Replace("X", "");
//        }

//        if (pap.Length == 3)
//        {
//            result = pap + "/" + "USD";
//        }
//        else
//        {
//            result = pap + "/" + "USD";
//        }
//    }
//    if (nameInRest.EndsWith("USDT"))
//    {
//        string pap = nameInRest.Replace("USDT", "");
//        if (pap.Length == 3)
//        {
//            result = pap + "/" + "USDT";
//        }
//        else
//        {
//            result = pap + "/" + "USDT";
//        }

//    }
//    if (nameInRest.EndsWith("USDC"))
//    {
//        string pap = nameInRest.Replace("USDC", "");
//        if (pap.Length == 3)
//        {
//            result = pap + "/" + "USDC";
//        }
//        else
//        {
//            result = pap + "/" + "USDC";
//        }

//    }
//    if (nameInRest.EndsWith("XBT"))
//    {
//        string pap = nameInRest.Replace("XBT", "");
//        if (pap.Length == 3)
//        {
//            result = pap + "/" + "XBT";
//        }
//        else
//        {
//            result = pap + "/" + "XBT";
//        }
//    }

//    return result;
//}


//    }






