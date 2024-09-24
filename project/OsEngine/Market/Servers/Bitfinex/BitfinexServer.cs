
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market.Servers.Bitfinex.Json;
using OsEngine.Market.Servers.Entity;
using RestSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Threading;
using WebSocket4Net;
using BitfinexSecurity = OsEngine.Market.Servers.Bitfinex.Json.BitfinexSecurity;
using Candle = OsEngine.Entity.Candle;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;
using JsonSerializer = System.Text.Json.JsonSerializer;
using MarketDepth = OsEngine.Entity.MarketDepth;
using Method = RestSharp.Method;
using Order = OsEngine.Entity.Order;
using Security = OsEngine.Entity.Security;
using Side = OsEngine.Entity.Side;
using Timer = System.Timers.Timer;
using Trade = OsEngine.Entity.Trade;
using WebSocket = WebSocket4Net.WebSocket;
using WebSocketState = WebSocket4Net.WebSocketState;





namespace OsEngine.Market.Servers.Bitfinex
{
    public class BitfinexServer : AServer
    {
        public BitfinexServer()
        {
            BitfinexServerRealization realization = new BitfinexServerRealization();
            ServerRealization = realization;

            CreateParameterString(OsLocalization.Market.ServerParamPublicKey, "");
            CreateParameterPassword(OsLocalization.Market.ServerParamSecretKey, "");
        }

        public List<Candle> GetCandleHistory(string nameSec, TimeSpan tf)
        {
            return ((BitfinexServer)ServerRealization).GetCandleHistory(nameSec, tf);
        }
    }

    public class BitfinexServerRealization : IServerRealization
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
                string _apiPath = "v2/platform/status";

                RestClient client = new RestClient(_baseUrl);
                RestRequest request = new RestRequest(_apiPath, Method.GET);
                request.AddHeader("accept", "application/json");

                IRestResponse response = client.Execute(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string responseBody = response.Content;

                    if (responseBody.Contains("1"))
                    {
                        CreateWebSocketConnection();
                    }
                }
            }

            catch (Exception exception)
            {
                SendLogMessage("Connection cannot be open. Bitfinex. exception:" + exception.ToString(), LogMessageType.Error);
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

        public ServerType ServerType => ServerType.Bitfinex;

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


        private readonly string _baseUrl = "https://api.bitfinex.com";

        #endregion


        #region 3 Securities

        private readonly RateGate _rateGateGetsecurity = new RateGate(1, TimeSpan.FromMilliseconds(200));

        private readonly RateGate _rateGatePositions = new RateGate(1, TimeSpan.FromMilliseconds(200));

        private readonly List<Security> _securities = new List<Security>();

        public event Action<List<Security>> SecurityEvent;

        public void GetSecurities()
        {
            //_apiPath = $"v2/ticker/{sec}";

            _rateGateGetsecurity.WaitToProceed();

            try
            {
                string _apiPath = "v2/tickers?symbols=ALL";

                RestClient client = new RestClient(_baseUrl);
                RestRequest request = new RestRequest(_apiPath, Method.GET);
                request.AddHeader("accept", "application/json");

                IRestResponse response = client.Execute(request);


                // var response = ExecuteRequest(_apiPath);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonResponse = response.Content;
                    // ["tIOTBTC",0.00000222,1517572.08488724,0.00000223,1425314.28282404,-1e-8,-0.00448431,0.00000222,351586.6296365,0.00000224,0.00000216],

                    List<List<object>> securityList = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);


                    if (securityList == null)
                    {
                        SendLogMessage("Deserialization resulted in null", LogMessageType.Error);
                        return;
                    }


                    if (securityList.Count > 0)

                    {
                        SendLogMessage("Securities loaded. Count: " + securityList.Count, LogMessageType.System);
                        SecurityEvent?.Invoke(_securities);
                    }

                    List<BitfinexSecurity> security = new List<BitfinexSecurity>();


                    for (int i = 0; i < securityList.Count; i++)
                    {
                        List<object> item = securityList[i];

                        BitfinexSecurity ticker = new BitfinexSecurity();

                        ticker.Symbol = item[0].ToString();
                        ticker.Bid = ConvertScientificNotation(item[1].ToString());
                        ticker.BidSize = ConvertScientificNotation(item[2].ToString());
                        ticker.Ask = ConvertScientificNotation(item[3].ToString());
                        ticker.AskSize = ConvertScientificNotation(item[4].ToString());
                        ticker.DailyChange = ConvertScientificNotation(item[5].ToString());
                        ticker.DailyChangeRelative = ConvertScientificNotation(item[6].ToString()); //double.Parse(item[7].ToString(), System.Globalization.NumberStyles.Float).ToString(),
                        ticker.LastPrice = ConvertScientificNotation(item[7].ToString());
                        ticker.Volume = ConvertScientificNotation(item[8].ToString());
                        ticker.High = ConvertScientificNotation(item[9].ToString());
                        ticker.Low = ConvertScientificNotation(item[10].ToString());

                        security.Add(ticker);
                    }
                  
                    UpdateSecurity(jsonResponse);
                }

                else
                {
                    SendLogMessage("Securities request exception. Status: " + response.Content, LogMessageType.Error);

                }

            }
            catch (Exception exception)
            {
                SendLogMessage("Securities request exception" + exception.ToString(), LogMessageType.Error);

            }
        }


        // Метод для преобразования строки в decimal с учетом научной нотации
        private string ConvertScientificNotation(string value)
        {
            // Преобразование строки в decimal с учетом научной нотации
            return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal result)
                ? result.ToString(CultureInfo.InvariantCulture)
                : value;

        }

        private void UpdateSecurity(string json)
        {

            List<List<object>> response = JsonConvert.DeserializeObject<List<List<object>>>(json);

            List<Security> securities = new List<Security>();

            for (int i = 0; i < response.Count; i++)
            {
                List<object> item = response[i];
                string symbol = item[0]?.ToString();

                SecurityType securityType = GetSecurityType(symbol);

                if (securityType == SecurityType.None)
                {
                    continue;
                }

                Security newSecurity = new Security();

                newSecurity.Exchange = ServerType.Bitfinex.ToString();
                newSecurity.Name = symbol;
                newSecurity.NameFull = symbol;
                newSecurity.NameClass = symbol.StartsWith("f") ? "Futures" : "CurrencyPair";
                newSecurity.NameId = symbol;
                newSecurity.SecurityType = securityType;
                newSecurity.Lot = 1;
                newSecurity.State = SecurityStateType.Activ;
                newSecurity.PriceStep = newSecurity.Decimals.GetValueByDecimals();
                newSecurity.PriceStepCost = newSecurity.PriceStep;
                newSecurity.DecimalsVolume = newSecurity.Decimals;

                securities.Add(newSecurity);
            }

            SecurityEvent(securities);
        }

        private SecurityType GetSecurityType(string type)
        {
            SecurityType _securityType = type.StartsWith("t") ? SecurityType.CurrencyPair : SecurityType.Futures;

            return _securityType;
        }
        #endregion

        /// <summary>
        /// Запрос доступных портфелей у подключения. 
        /// </summary>
        #region 4 Portfolios

        private readonly List<Portfolio> _portfolios = new List<Portfolio>();

        public event Action<List<Portfolio>> PortfolioEvent;

        private readonly RateGate _rateGatePortfolio = new RateGate(1, TimeSpan.FromMilliseconds(200));


        public void GetPortfolios()
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
                string _apiPath = "v2/auth/r/wallets";

                 //string nonce = GetNonce();

                //string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

                //string signature = $"/api/{_apiPath}{nonce}";

                //string sig = ComputeHmacSha384(_secretKey, signature);

                //RestClient client = new RestClient(_baseUrl);

                //RestRequest request = new RestRequest(_apiPath, Method.POST);

                //request.AddHeader("accept", "application/json");
                //request.AddHeader("bfx-nonce", nonce);
                //request.AddHeader("bfx-apikey", _publicKey);
                //request.AddHeader("bfx-signature", sig);

                //IRestResponse response = client.Execute(request);

                IRestResponse response = ExecuteRequest(_apiPath);


                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string responseBody = response.Content;

                    UpdatePortfolio(responseBody);

                    CreateQueryPosition();
                }
                else
                {
                    SendLogMessage($"Error Query Portfolio: {response.Content}", LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void UpdatePortfolio(string json)
        {
            List<List<object>> response = JsonConvert.DeserializeObject<List<List<object>>>(json);

            Portfolio portfolio = new Portfolio();

            portfolio.Number = "BitfinexPortfolio";
            portfolio.ValueBegin = response[0][2].ToString().ToDecimal();
            portfolio.ValueCurrent = response[0][4].ToString().ToDecimal();


            for (int i = 0; i < response.Count; i++)
            {
                List<object> wallet = response[i];

                if (wallet[0].ToString() == "exchange")
                {
                    PositionOnBoard position = new PositionOnBoard();

                    position.PortfolioName = "BitfinexPortfolio";
                    position.SecurityNameCode = wallet[1].ToString();
                    position.ValueBegin = wallet[2].ToString().ToDecimal();
                    position.ValueCurrent = wallet[4].ToString().ToDecimal();
                    position.ValueBlocked = wallet[2].ToString().ToDecimal() - wallet[4].ToString().ToDecimal();

                    portfolio.SetNewPosition(position);
                }
            }
            PortfolioEvent(new List<Portfolio> { portfolio });
        }

        private void CreateQueryPosition()
        {
            _rateGatePositions.WaitToProceed();
          //  string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();
            //  string nonce = GetNonce();

            // ПОЛУЧЕНИЕ АКТИВНЫХ ПОЗИЦИЙ
            ///*https://api.bitfinex.com/v2/auth/r/orders */                      

            try
            {
                string _apiPath = "v2/auth/r/positions";// нулевой массив
                IRestResponse response = ExecuteRequest(_apiPath);


                //string signature = $"/api/{_apiPath}{nonce}";

                //string sig = ComputeHmacSha384(_secretKey, signature);

                //RestClient client = new RestClient(_baseUrl);

                //RestRequest request = new RestRequest(_apiPath, Method.POST);

                //request.AddHeader("accept", "application/json");
                //request.AddHeader("bfx-nonce", nonce);
                //request.AddHeader("bfx-apikey", _publicKey);
                //request.AddHeader("bfx-signature", sig);

                //IRestResponse response = client.Execute(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string responseBody = response.Content;
                    UpdatePosition(responseBody); // Обновляем позиции  приходит пустой массив
                }
                else
                {
                    SendLogMessage($"Create Query Position: {response.Content}", LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

        }

        private void UpdatePosition(string json)
        {

            List<List<object>> response = JsonConvert.DeserializeObject<List<List<object>>>(json);

            Portfolio portfolio = new Portfolio();
            
                portfolio.Number = "BitfinexPortfolio";
                portfolio.ValueBegin = 1;
                portfolio.ValueCurrent = 1;
            

            for (int i = 0; i < response.Count; i++)
            {
                List<object> position = response[i];


                PositionOnBoard pos = new PositionOnBoard();

                pos.PortfolioName = "BitfinexPortfolio";
                pos.SecurityNameCode = position[3].ToString();
                pos.ValueBegin = position[7].ToString().ToDecimal();
                pos.ValueBlocked = position[7].ToString().ToDecimal() - position[6].ToString().ToDecimal();
                pos.ValueCurrent = position[6].ToString().ToDecimal();

                portfolio.SetNewPosition(pos);
            }

            PortfolioEvent(new List<Portfolio> { portfolio });
        }


        #endregion


        /// <summary>
        /// Запросы данных по свечкам и трейдам. 
        /// </summary>
        #region 5 Data Candles
       
        private readonly RateGate _rateGateCandleHistory = new RateGate(1, TimeSpan.FromSeconds(300));

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

        public List<Candle> GetCandleHistory(string nameSec, TimeSpan tf, int CountToLoad, DateTime endTime)
        {
           

            int needToLoadCandles = CountToLoad;

            List<Candle> candles = new List<Candle>();
            DateTime fromTime = endTime - TimeSpan.FromMinutes(tf.TotalMinutes * CountToLoad);//перепрыгивает на сутки назад

            do
            {

                // ограничение Bitfinex: For each query, the system would return at most 1500 pieces of data. To obtain more data, please page the data by time.
                int maxCandleCountToLoad = 10000;
                int limit = Math.Min(needToLoadCandles, maxCandleCountToLoad);

                List<Candle> rangeCandles; //= new List<Candle>(); //////не нужен новый список 

                rangeCandles = CreateQueryCandles(nameSec, GetStringInterval(tf), fromTime, endTime);

                if (rangeCandles == null)
                {
                    return null; // нет данных
                }

                // rangeCandles.Reverse();

                candles.InsertRange(0, rangeCandles);

                if (candles.Count != 0)
                {
                    endTime = candles[0].TimeStart;////gthtltkfnm
                }

                needToLoadCandles -= limit;

            }

            while (needToLoadCandles > 0);

            return candles;
        }

        public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)/////string
        {
            //if (endTime > DateTime.Now ||
            //   startTime >= endTime ||
            //   startTime >= DateTime.Now ||
            //   actualTime > endTime ||
            //   actualTime > DateTime.Now)
            //{
            //    return null;
            //}
            //if (startTime != actualTime)
            //{
            //    startTime = actualTime;
            //}
            if (actualTime < startTime)
            {
                startTime = actualTime;
            }

            if (endTime > DateTime.UtcNow)
            {
                endTime = DateTime.UtcNow;
            }

            int countNeedToLoad = GetCountCandlesFromSliceTime(startTime, endTime, timeFrameBuilder.TimeFrameTimeSpan);

            return GetCandleHistory(security.NameFull, timeFrameBuilder.TimeFrameTimeSpan, countNeedToLoad, endTime);
        }



        private int GetCountCandlesFromSliceTime(DateTime startTime, DateTime endTime, TimeSpan tf)
        {
            TimeSpan TimeSlice = endTime - startTime;
            return tf.Hours != 0 ? Convert.ToInt32(TimeSlice.TotalHours / tf.TotalHours) : Convert.ToInt32(TimeSlice.TotalMinutes / tf.Minutes);
        }

        private string GetStringInterval(TimeSpan tf)
        {
            // Type of candlestick patterns: 1min, 3min, 5min, 15min, 30min, 1hour, 3hour, 6hour,  12hour, 1day, 1week, 14 days,1 month
            // return tf.Minutes != 0 ? $"{tf.Minutes}m" : $"{tf.Hours}h";
            return tf.Minutes != 0 ? $"{tf.Minutes}m" : $"{tf.Hours}h";
        }

        // private List<Candle> CreateQueryCandles(string nameSec, string tf, DateTime timeFrom, DateTime timeTo)//////////TimeSpan interval

        private List<Candle> CreateQueryCandles(string nameSec, string tf, DateTime timeFrom, DateTime timeTo)
        {
            _rateGateCandleHistory.WaitToProceed();


            DateTime yearBegin = new DateTime(1970, 1, 1);

            //var timeStampStart = timeFrom - yearBegin;
            //var startTimeMilliseconds = timeStampStart.TotalMilliseconds;
            // string startTime = Convert.ToInt64(startTimeMilliseconds).ToString();

            //var timeStampEnd = timeTo - yearBegin;
            //var endTimeMilliseconds = timeStampEnd.TotalMilliseconds;
            //string endTime = Convert.ToInt64(endTimeMilliseconds).ToString();


            //Convert.ToInt64((timeFrom - yearBegin).TotalMilliseconds).ToString();
            //Convert.ToInt64((timeTo - yearBegin).TotalMilliseconds).ToString();


            ///
            //string section = timeFrom !=DateTime.Today ?"hist":"last";////////////

            //string section = startTime != DateTime.Today ? "hist" : "last";

            //string candle = $"trade:30m:{nameSec}";

            string candle = $"trade:{tf}:{nameSec}";

            string _apiPath = $"/v2/candles/{candle}/hist";//?start={startTime}&end={endTime}";
            IRestResponse response = ExecuteRequest(_apiPath);

            //RestClient client = new RestClient(_baseUrl);
            //RestRequest request = new RestRequest(_apiPath, Method.GET);
            //request.AddHeader("accept", "application/json");

            //IRestResponse response = client.Execute(request);

            try
            {

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonResponse = response.Content;

                    List<List<object>> candles = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);

                    if (candles == null || candles.Count == 0)
                    {
                        return null;
                    }

                    List<BitfinexCandle> candleList = new List<BitfinexCandle>();

                    for (int i = 0; i < candles.Count; i++)

                    {
                        List<object> candleData = candles[i];

                        if (candles[i] == null || candles.Count < 6)
                        {
                            SendLogMessage("Candle data is incomplete", LogMessageType.Error);
                            continue;
                        }

                        if (candleData == null)
                        //if (candleData[0] == null || candleData[1] == null ||
                        //    candleData[2] == null ||  candleData[3] == null ||
                        //    candleData[4] == null || candleData[5] == null)

                        {
                            SendLogMessage("Candle data contains null values", LogMessageType.Error);

                            continue;
                        }
                        //// .ToString().ToDecimal()
                        //if (Convert.ToDecimal(candleData[1]) == 0 ||
                        //    Convert.ToDecimal(candleData[2]) == 0 ||
                        //    Convert.ToDecimal(candleData[3]) == 0 ||
                        //    Convert.ToDecimal(candleData[4]) == 0 ||
                        //    Convert.ToDecimal(candleData[5]) == 0)
                        //{
                        //    SendLogMessage("Candle data contains zero values", LogMessageType.Error);

                        //    continue;
                        //}

                        BitfinexCandle newCandle = new BitfinexCandle();

                        newCandle.Mts = candleData[0].ToString();
                        newCandle.Open = candleData[1].ToString();
                        newCandle.Close = candleData[2].ToString();
                        newCandle.High = candleData[3].ToString();
                        newCandle.Low = candleData[4].ToString();
                        newCandle.Volume = candleData[5].ToString();
                        
                        candleList.Add(newCandle);
                    }

                    return ConvertToCandles(candleList);
                }
            }
            catch (Exception exception)
            {
                SendLogMessage($"Request error{exception.Message}", LogMessageType.Error);
            }

            return null;
        }

        private List<Candle> ConvertToCandles(List<BitfinexCandle> candleList)
        {
            List<Candle> candles = new List<Candle>();

            try
            {
                for (int i = 0; i < candleList.Count; i++)
                {
                    BitfinexCandle candle = candleList[i];

                    try
                    {
                        if (string.IsNullOrEmpty(candle.Mts) || string.IsNullOrEmpty(candle.Open) ||
                            string.IsNullOrEmpty(candle.Close) || string.IsNullOrEmpty(candle.High) ||
                            string.IsNullOrEmpty(candle.Low) || string.IsNullOrEmpty(candle.Volume))
                        {
                            SendLogMessage("Candle data contains null or empty values", LogMessageType.Error);
                            continue;
                        }

                        if (Convert.ToDecimal(candle.Open) == 0 || Convert.ToDecimal(candle.Close) == 0 ||
                            Convert.ToDecimal(candle.High) == 0 || Convert.ToDecimal(candle.Low) == 0 ||
                            Convert.ToDecimal(candle.Volume) == 0)
                        {
                            SendLogMessage("Candle data contains zero values", LogMessageType.Error);
                            continue;
                        }

                        Candle newCandle = new Candle();

                        newCandle.State = CandleState.Finished;
                        newCandle.TimeStart = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(candle.Mts));
                        newCandle.Open = candle.Open.ToString().ToDecimal();
                        newCandle.Close = candle.Close.ToString().ToDecimal();
                        newCandle.High = candle.High.ToString().ToDecimal();
                        newCandle.Low = candle.Low.ToString().ToDecimal();
                        newCandle.Volume = candle.Volume.ToString().ToDecimal();

                        candles.Add(newCandle);
                    }
                    catch (FormatException exception)
                    {
                        SendLogMessage($"Format exception: {exception.Message}", LogMessageType.Error);
                    }
                }
                candles.Reverse();
                return candles;
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
                return null;
            }
        }

        #endregion

        /// <summary>
        /// Создание вебсокет соединения. 
        /// </summary>
        #region  6 WebSocket creation
        // ConcurrentQueue<string>;
        private WebSocket _webSocketPublic;
        private WebSocket _webSocketPrivate;

        private readonly string _webSocketPublicUrl = "wss://api-pub.bitfinex.com/ws/2";
        private readonly string _webSocketPrivateUrl = "wss://api.bitfinex.com/ws/2";
     


        private void CreateWebSocketConnection()
        {
            try
            {
                if (_webSocketPublic != null)
                {
                    return;
                }

                //_socketPublicIsActive = false;
                // _socketPrivateIsActive = false;

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

        #endregion


        /// <summary>
        /// Обработка входящих сообщений от вёбсокета. И что важно в данном конкретном случае, Closed и Opened методы обязательно должны находиться здесь,
        /// </summary>
        #region  7 WebSocket events

        private bool _socketPublicIsActive;

        private bool _socketPrivateIsActive;

        private void WebSocketPublic_Opened(object sender, EventArgs e)
        {
            Thread.Sleep(2000);

            _socketPublicIsActive = true;//отвечает за соединение

            CheckActivationSockets();
            //  _pingTimer.Start();////////////////////////
            SendLogMessage("Websocket public Bitfinex Opened", LogMessageType.System);

        }

        private void WebSocketPublic_Closed(object sender, EventArgs e)
        {
            try
            {
                if (ServerStatus != ServerConnectStatus.Disconnect)
                {

                    ServerStatus = ServerConnectStatus.Disconnect;
                    DisconnectEvent();
                }
                SendLogMessage("WebSocket Public сlosed by Bitfinex.", LogMessageType.Error);
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
                    //continue;
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


        public event Action<MarketDepth> MarketDepthEvent;
        public long tradeChannelId;
        public long bookChannelId;

        // Снимок(snapshot) : Структура данных содержит массив массивов, где каждый внутренний массив представляет собой запись в стакане(book entry).
        //Обновление(update) : Структура данных содержит только один массив, представляющий одну запись в стакане(book entry).

        private readonly MarketDepth marketDepth = new MarketDepth();

        // Инициализируем списки для ask и bid уровней
        private readonly List<MarketDepthLevel> asks = new List<MarketDepthLevel>();
        private readonly List<MarketDepthLevel> bids = new List<MarketDepthLevel>();


        public void SnapshotDepth(string jsonResponse, long bookChannelId, string symbol)
        {
            JsonDocument document = JsonDocument.Parse(jsonResponse);
            JsonElement root = document.RootElement;

            long channelId = root[0].GetInt64();
            JsonElement data = root[1];

            if (channelId == bookChannelId)
            {
                marketDepth.SecurityNameCode = symbol;

                // Очистка старых данных и добавление новых уровней
                bids.Clear();
                asks.Clear();

                // Итерация по массиву снапшота
                for (int i = 0; i < data.GetArrayLength(); i++)
                {
                    JsonElement entryElement = data[i];

                    decimal price = entryElement[0].ToString().ToDecimal();
                    decimal count = entryElement[1].ToString().ToDecimal();
                    decimal amount = entryElement[2].ToString().ToDecimal();

                    if (amount > 0)
                    {
                        // Добавление уровня бидов
                        MarketDepthLevel bidLevel = new MarketDepthLevel
                        {
                            Price = price,
                            Bid = amount
                        };
                        bids.Add(bidLevel);
                    }
                    else
                    {
                        // Добавление уровня асков
                        MarketDepthLevel askLevel = new MarketDepthLevel
                        {
                            Price = price,
                            Ask = Math.Abs(amount)
                        };
                        asks.Add(askLevel);
                    }
                }

                // Сортировка бидов и асков
                marketDepth.Bids = bids.OrderByDescending(b => b.Price).ToList();
                marketDepth.Asks = asks.OrderBy(a => a.Price).ToList();

                marketDepth.Time = DateTime.UtcNow;

                MarketDepthEvent(marketDepth);
            }
        }

        public void UpdateDepth(string jsonResponse, long bookChannelId, string _currentSymbol)
        {
            JsonDocument document = JsonDocument.Parse(jsonResponse);
            JsonElement root = document.RootElement;

            long channelId = root[0].GetInt64();
            JsonElement data = root[1];

            if (channelId == bookChannelId)
            {
                decimal price = data[0].ToString().ToDecimal();
                decimal count = data[1].ToString().ToDecimal();
                decimal amount = data[2].ToString().ToDecimal();                                 // var amount = decimal.Parse(data[2].GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture); // объём

                if (count == 0)
                {
                    // Удаление уровня
                    if (amount > 0)
                    {
                        // Удаление из бидов
                        marketDepth.Bids.RemoveAll(b => b.Price == price);
                    }
                    else
                    {
                        // Удаление из асков
                        marketDepth.Asks.RemoveAll(a => a.Price == price);
                    }
                }
                else
                {
                    // Обновление или добавление уровня
                    MarketDepthLevel level = new MarketDepthLevel
                    {
                        Price = price,
                        Bid = amount > 0 ? amount : 0,
                        Ask = amount < 0 ? Math.Abs(amount) : 0
                    };

                    if (amount > 0)
                    {
                        // Обновление или добавление уровня в биды
                        MarketDepthLevel existingBid = marketDepth.Bids.FirstOrDefault(b => b.Price == price);
                        if (existingBid != null)
                        {
                            existingBid.Bid = amount;
                        }
                        else
                        {
                            marketDepth.Bids.Add(level);
                        }
                    }
                    else
                    {
                        // Обновление или добавление уровня в аски
                        MarketDepthLevel existingAsk = marketDepth.Asks.FirstOrDefault(a => a.Price == price);
                        if (existingAsk != null)
                        {
                            existingAsk.Ask = Math.Abs(amount);
                        }
                        else
                        {
                            marketDepth.Asks.Add(level);
                        }
                    }
                }

                // Сортировка бидов и асков
                marketDepth.Bids = marketDepth.Bids.OrderByDescending(b => b.Price).ToList();
                marketDepth.Asks = marketDepth.Asks.OrderBy(a => a.Price).ToList();

                marketDepth.Time = DateTime.UtcNow;

                if (marketDepth.Asks.Count == 0 || marketDepth.Bids.Count == 0)
                {
                    return;
                }

                MarketDepthEvent(marketDepth);
            }
        }

        private void WebSocketPrivate_Opened(object sender, EventArgs e)
        {
            GenerateAuthenticate();
            _socketPrivateIsActive = true;//отвечает за соединение
            //CheckActivationSockets();/////// надо или нет
            SendLogMessage("Connection to private data is Open", LogMessageType.System);
        }

        private void GenerateAuthenticate()
        {
             //string nonce = GetNonce();
            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ).ToString();

            string payload = $"AUTH{nonce}";

            string signature = ComputeHmacSha384(payload, _secretKey);

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
                    _webSocketPublic.State == WebSocketState.Open && _webSocketPrivate.State == WebSocketState.Open)
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
                SendLogMessage("Connection Closed by Bitfinex. WebSocket Private сlosed ", LogMessageType.Error);

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
                if (e.Exception != null)
                {
                    SendLogMessage(e.Exception.ToString(), LogMessageType.Error);
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

                if (WebSocketPrivateMessage == null)
                {
                    return;
                }

                WebSocketPrivateMessage.Enqueue(e.Message);
            }

            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        #endregion

        /// <summary>
        /// Подписка на бумагу.С обязательным контролем скорости и кол-ву запросов к методу Subscrible через rateGate.
        /// </summary>
        #region  8 Security subscrible 

        private readonly RateGate _rateGateSecurity = new RateGate(1, TimeSpan.FromMilliseconds(250));
        private readonly List<Security> _subscribledSecurities = new List<Security>();

        public void Subscrible(Security security)
        {
            try
            {
                CreateSubscribleMessageWebSocket(security);

                Thread.Sleep(200);
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void CreateSubscribleMessageWebSocket(Security security)
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

                _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"book\",\"symbol\":\"{security.Name}\",\"prec\":\"P0\",\"freq\":\"F0\",\"len\":\"25\"}}");//стакан

                _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"trades\",\"symbol\":\"{security.Name}\"}}"); //трейды


            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }


        #endregion


        #region  9 WebSocket parsing the messages


        public event Action<Trade> NewTradesEvent;//трейды

        public event Action<Order> MyOrderEvent;//новые мои ордера

        public event Action<MyTrade> MyTradeEvent;// мои трйды

        private readonly ConcurrentQueue<string> WebSocketPublicMessage = new ConcurrentQueue<string>();

        private readonly ConcurrentQueue<string> WebSocketPrivateMessage = new ConcurrentQueue<string>();


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
                    //message "[72,\"hb\"]"   string

                    if (message.Contains("info") || message.Contains("hb") || message.Contains("auth"))
                    {
                        continue;
                    }

                    // message "{\"event\":\"subscribed\",\"channel\":\"trades\",\"chanId\":35,\"symbol\":\"tBTCUSD\",\"pair\":\"BTCUSD\"}"   
                    if (message.Contains("trades"))
                    {
                        SubscriptionResponseTrade responseTrade = JsonConvert.DeserializeObject<SubscriptionResponseTrade>(message);
                        tradeChannelId = Convert.ToInt64(responseTrade.ChanId);
                    }

                    //"{\"event\":\"subscribed\",\"channel\":\"book\",\"chanId\":40121,\"symbol\":\"tBTCUSD\",\"prec\":\"P0\",\"freq\":\"F0\",\"len\":\"25\",\"pair\":\"BTCUSD\"}"
                    if (message.Contains("book"))
                    {
                        BitfinexResponceWebSocketDepth responseDepth = JsonConvert.DeserializeObject<BitfinexResponceWebSocketDepth>(message);
                        _currentSymbol = responseDepth.Symbol;
                        bookChannelId = Convert.ToInt64(responseDepth.ChanId);

                    }
                    //"[35,[[1658091709,1726239678068,0.00004,58553],[1658091708,1726239678067,0.00004,58553],[1658091707,1726239676073,0.000121,58553],[1658091706,1726239676063,0.000121,58553],[1658091705,1726239676053,0.000121,58553],[1658091704,1726239676010,0.001,58553],[1658091703,1726239675969,0.000607,58553],[1658091702,1726239675931,0.003047,58553],[1658091701,1726239672699,0.015304,58553],[1658091700,1726239672653,0.034157,58553],[1658091699,1726239672616,0.034157,58553],[1658091698,1726239672599,0.034157,58553],[1658091697,1726239672575,0.034157,58553],[1658091696,1726239671690,0.034157,58553],[1658091695,1726239671641,0.034157,58553],[1658091694,1726239671610,0.034157,58553],[1658091693,1726239671599,0.034157,58553],[1658091692,1726239671588,0.034157,58553],[1658091691,1726239671156,0.00914,58553],[1658091690,1726239671156,0.00883,58553],[1658091689,1726239670761,0.00387081,58553],[1658091688,1726239670706,0.034157,58553],[1658091687,1726239670695,0.034157,58553],[1658091686,1726239670633,0.034157,58553],[1658091685,1726..."   string
                    //"[40121,[[58552,12,3.48429776],[58551,4,1.16820404],[58549,1,0.00164],[58548,2,0.2454627],[58547,1,0.16981157],[58542,4,1.24858194],[58541,18,5.94246829],[58538,2,0.31315935],[58533,1,0.00164],[58528,3,0.78039044],[58526,2,1.34182435],[58525,1,0.08594077],[58522,2,0.26984404],[58520,2,1.8550382],[58514,1,0.31995795],[58512,1,0.23144615],[58511,1,0.00164],[58510,1,0.7798],[58508,1,0.25619348],[58506,1,0.0988],[58505,1,0.068477],[58500,1,0.01],[58498,1,0.35635557],[58497,2,0.33551305],[58496,1,0.001642],[58553,5,-2.06513149],[58555,1,-1.7538],[58559,2,-0.004648],[58563,1,-0.000821],[58574,1,-0.25619348],[58575,1,-0.6717],[58576,1,-0.003827],[58578,2,-0.36432],[58583,1,-0.000821],[58585,1,-0.03],[58586,1,-0.26820404],[58589,1,-0.25619348],[58591,1,-0.000821],[58592,4,-0.062],[58594,2,-0.004647],[58596,1,-0.0032765],[58597,1,-0.03422],[58599,1,-0.009812],[58602,1,-0.4665],[58603,1,-0.00082],[58604,1,-0.25619348],[58608,1,-0.00082],[58609,1,-0.26820404],[58611,3,-0.083777],[58612,2,-0.18072]]]"
                    // "[40121,[58547,0,1]]"   

                    if (message.Contains("["))
                    {
                        //парсим сообщение   
                        JsonDocument jsonDocument = JsonDocument.Parse(message);//[38345,[[1658254420,1726304785797,-36.15002414,0.14842],[1658254339,1726302043810,486.248103,0.14829],[1658254334,1726301335422,6395.24198918,0.14816],[1658254332,1726301088782,6470.90142709,0.14824],[1658254331,1726301068362,6550.23201483,0.14824],[1658254330,1726301016323,6630.53561074,0.14823],[1658254328,1726300937156,6712.39334448,0.14821],[1658254327,1726300912137,6827.56700025,0.14812],[1658254326,1726300893414,6911.36658231,0.14811],[1658254322,1726300293385,5899.26717132,0.14797],[1658254321,1726300293124,10481,0.14797],[1658254317,1726300181585,6409.66976268,0.14797],[1658254269,1726298185033,-40.5975202,0.14803],[1658254251,1726297885002,29.40042961,0.14804],[1658254231,1726296832328,2178.526943,0.14829],[1658254225,1726296733915,-8707.754358,0.14809],[1658214155,1726296213768,295.91558,0.1481],[1658214107,1726295484736,23.63344422,0.14838],[1658214063,1726294344673,6522.3374386,0.14814],[1658214033,1726293672806,-1039.673644,0.14811],[1658213913,1726292556004,6625.60193646,0.14822],[1658213799,1726291093240,565.9363,0.14815],[1658213790,1726290970066,6794.83628027,0.14806],[1658213776,1726290321276,100,0.14829],[1658213757,1726289484029,28.43184588,0.14821],[1658213738,1726287683859,-42.94446732,0.14843],[1658213706,1726286183711,-31.82420431,0.14834],[1658213705,1726285883685,43.59615823,0.1485],[1658213663,1726284983581,-41.00480058,0.14822],[1658213652,1726284790156,182,0.1482]]]

                        // Получаем корневой элемент
                        JsonElement root = jsonDocument.RootElement;//"[38345,[[1658254420,1726304785797,-36.15002414,0.14842],[1658254339,1726302043810,486.248103,0.14829],[1658254334,1726301335422,6395.24198918,0.14816],[1658254332,1726301088782,6470.90142709,0.14824],[1658254331,1726301068362,6550.23201483,0.14824],[1658254330,1726301016323,6630.53561074,0.14823],[1658254328,1726300937156,6712.39334448,0.14821],[1658254327,1726300912137,6827.56700025,0.14812],[1658254326,1726300893414,6911.36658231,0.14811],[1658254322,1726300293385,5899.26717132,0.14797],[1658254321,1726300293124,10481,0.14797],[1658254317,1726300181585,6409.66976268,0.14797],[1658254269,1726298185033,-40.5975202,0.14803],[1658254251,1726297885002,29.40042961,0.14804],[1658254231,1726296832328,2178.526943,0.14829],[1658254225,1726296733915,-8707.754358,0.14809],[1658214155,1726296213768,295.91558,0.1481],[1658214107,1726295484736,23.63344422,0.14838],[1658214063,1726294344673,6522.3374386,0.14814],[1658214033,1726293672806,-1039.673644,0.14811],[1658213913,1726292556004,6625.60193646,0.1...

                        long chanelId = root[0].GetInt64();
                        JsonElement oo = root[1];

                        if (root[1].ValueKind == JsonValueKind.Array)
                        {
                            // Проверяем, что корневой элемент - массив и у него есть вложенные элементы
                            if (root.ValueKind == JsonValueKind.Array && root[1].GetArrayLength() > 5)
                            {
                                // Получаем второй элемент (индекс 1) — это массив массивов
                                //  JsonElement nestedArray = root[1];

                                // Проверяем, что это массив
                                if (root[1].ValueKind == JsonValueKind.Array && root[1].GetArrayLength() > 2)
                                {
                                    // Получаем второй подмассив (индекс 1)
                                    // JsonElement subArray = nestedArray[1];//"[1658254446,1726305953677,-269.91712,0.14855]"


                                    // Проверяем, что массив содержит ровно 4 элемента (один CHANNEL_ID и три подмассива)
                                    if (chanelId == tradeChannelId)
                                    {
                                        SnapshotTrade(message, tradeChannelId);
                                    }

                                    if (chanelId == bookChannelId)
                                    {
                                        SnapshotDepth(message, bookChannelId, _currentSymbol);
                                    }
                                }
                            }
                            else

                            {
                                UpdateDepth(message, bookChannelId, _currentSymbol);
                            }

                        }
                        if (root[1].ValueKind == JsonValueKind.String)
                        {
                            string messageType = root[1].ToString();

                            if (messageType == "te" || (messageType == "tu" && chanelId != 0))
                            {
                                UpdateTrade(message);
                            }
                            else
                            {
                                UpdateMyTrade(message);
                            }
                        }
                    }

                    //"[40121,[58547,0,1]]"	
                    ////"[11086,\"te\",[1657562091,1726074346694,38.96758011,0.15345]]"

                    // "[11086,"tu",[1657562091,1726074346694,38.96758011,0.15345]]"   

                }
                catch (Exception exception)
                {

                    SendLogMessage(exception.ToString(), LogMessageType.Error);
                }
            }
        }

        private void SnapshotTrade(string message, long tradeChannelId)
        {

            try
            {
                // Парсинг JSON
                JsonDocument document = JsonDocument.Parse(message);
                JsonElement root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {

                    int channelId = root[0].GetInt32();

                    if (channelId == tradeChannelId)
                    {
                        JsonElement tradesArray = root[1]; // Массив трейдов

                        // Проверка, что snapshot это массив массивов (список трейдов)
                        if (tradesArray.ValueKind == JsonValueKind.Array)
                        {
                            List<TradeSnapshot> trades = new List<TradeSnapshot>();
                            //                        [
                            //                          17470, // ChannelId
                            //                          [
                            //                            [401597393, 1574694475039, 0.005, 7244.9], // Trade 1
                            //                            [401597394, 1574694475040, 0.010, 7245.1]  // Trade 2
                            //                          ]
                            //                        ]

                            // Создаем объект для хранения данных о трейдах
                            TradeSnapshot tradeSnapshot = new TradeSnapshot
                            {
                                ChannelId = root[0].GetInt32().ToString(),  // Получаем ChannelId
                                Trades = new List<BitfinexTrades>()  // Инициализируем список трейдов
                            };

                            // Перебираем массив трейдов
                            for (int i = 0; i < tradesArray.GetArrayLength(); i++)
                            {
                                JsonElement trade = tradesArray[i];

                                BitfinexTrades newTrade = new BitfinexTrades
                                {
                                    Id = trade[0].ToString(),         // ID трейда
                                    Mts = trade[1].ToString(),        // Время создания
                                    Amount = trade[2].ToString(),
                                    Price = trade[3].ToString()     // Цена
                                };

                                tradeSnapshot.Trades.Add(newTrade);  // Добавляем трейд в список
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {

                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

        }


        private void UpdateTrade(string jsonMessage)//[10098,\"tu\",[1657561837,1726071091967,-28.61178052,0.1531]]"/    jsonMessage	"[171733,\"te\",[1660221249,1727123813028,0.001652,63473]]"	string

        {
            // Логика обновления трейда

            try
            {
                // Парсим полученные данные из JSON
                JsonDocument document = JsonDocument.Parse(jsonMessage); //ValueKind = Array : "[35,"tu",[1658091711,1726239686802,0.001,58553]]"


                // Получаем корневой элемент массива
                JsonElement root = document.RootElement;// ValueKind = Array : "[35,"tu",[1658091711,1726239686802,0.001,58553]]"

                List<BitfinexUpdateTrades> tradeList = new List<BitfinexUpdateTrades>();

                // Создаем объект BitfinexUpdateTrades и заполняем его данными
                BitfinexUpdateTrades tradeUpdate = new BitfinexUpdateTrades();

                tradeUpdate.ChannelId = root[0].ToString();
                tradeUpdate.Type = root[1].ToString();
                // Данные о торговой операции находятся в массиве по индексу 2
                JsonElement tradeDataArray = root[2];
                TradeData data = new TradeData();

                data.Id = root[2][0].ToString();
                data.Mts = root[2][1].ToString();
                data.Amount = root[2][2].ToString();
                data.Price = root[2][3].ToString();

                //tradeList.Add(tradeUpdate);
                tradeUpdate.Data = data;

                Trade newTrade = new Trade();

                // Создание объекта для хранения информации о сделке
                newTrade.SecurityNameCode = _currentSymbol;                     // Название инструмента
                newTrade.Id = tradeUpdate.Data.Id.ToString();               // Присваиваем TradeId из tradeUpdate
                newTrade.Price = tradeUpdate.Data.Price.ToDecimal();                         // Присваиваем цену
                newTrade.Volume = Math.Abs(tradeUpdate.Data.Amount.ToDecimal());             // Присваиваем объем (сделка может быть отрицательной для продаж)
                newTrade.Side = tradeUpdate.Data.Amount.ToDecimal() > 0 ? Side.Buy : Side.Sell; // Определяем сторону сделки
                newTrade.Time = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(tradeUpdate.Data.Mts));
               
                ServerTime = newTrade.Time;  // Присваиваем временную метку сделки

                NewTradesEvent?.Invoke(newTrade);

            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
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

                    if (message.Contains("pong") || message.Contains("info") || message.Contains("auth"))
                    {
                        continue;
                    }

                    JsonDocument jsonDocument = JsonDocument.Parse(message);
                    JsonElement root = jsonDocument.RootElement;
                    string eventType = root.GetProperty("event").GetString();

                    //if(message.Contains("CHAN_ID = 0") chanelId == 0)

                    if (message.Contains("ou"))//("n")
                    {
                        UpdateOrder(message);
                        // continue;
                    }
                    if (message.Contains("te") /*&& chanelId == 0*/)///////какой евент выбирать?
                    {
                        UpdateMyTrade(message);
                        //continue;
                    }
                    if (message.Contains("ws"))
                    {
                        UpdatePortfolio(message);
                    }

                }
                catch (Exception exception)
                {

                    SendLogMessage(exception.ToString(), LogMessageType.Error);

                }
            }
        }

        private void UpdateMyTrade(string message)
        {
            try
            {

                List<List<object>> tradyList = JsonConvert.DeserializeObject<List<List<object>>>(message);

                if (tradyList == null)
                {
                    return;
                }

                List<BitfinexMyTrade> ListMyTrade = new List<BitfinexMyTrade>(); ///надо или нет.,


                //for (int i = 0; i < 3; i++)
                for (int i = 0; i < tradyList.Count; i++)
                {
                    List<object> item = tradyList[i];

                    BitfinexMyTrade response = new BitfinexMyTrade();//{}


                    MyTrade myTrade = new MyTrade();

                    myTrade.Time = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(response.MtsCreate));
                    myTrade.SecurityNameCode = response.Symbol;
                    myTrade.NumberOrderParent = response.Cid;//что тут должно быть
                    myTrade.Price = response.OrderPrice.ToDecimal();
                    myTrade.NumberTrade = response.OrderId;//что тут должно быт
                    myTrade.Side = response.ExecAmount.Contains("-") ? Side.Sell : Side.Buy;
                    // myTrade.Side = response.Amount > 0 ? Side.Buy : Side.Sell;
                    // myTrade.Volume = (response.Amount).ToString().ToDecimal(),


                    // при покупке комиссия берется с монеты и объем уменьшается и появляются лишние знаки после запятой
                    decimal preVolume = myTrade.Side == Side.Sell ? response.ExecAmount.ToDecimal() : response.ExecAmount.ToDecimal() - response.Fee.ToDecimal();

                    myTrade.Volume = GetVolumeForMyTrade(response.Symbol, preVolume);


                    MyTradeEvent?.Invoke(myTrade);

                    SendLogMessage(myTrade.ToString(), LogMessageType.Trade);
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        //округление объемом
        private readonly Dictionary<string, int> _decimalVolume = new Dictionary<string, int>();
        // метод для округления знаков после запятой
        private decimal GetVolumeForMyTrade(string symbol, decimal preVolume)
        {
            int forTruncate = 1;

            Dictionary<string, int>.Enumerator enumerator = _decimalVolume.GetEnumerator();

            while (enumerator.MoveNext())
            {
                string key = enumerator.Current.Key;
                int value = enumerator.Current.Value;

                if (key.Equals(symbol))
                {
                    if (value != 0)
                    {
                        for (int i = 0; i < value; i++)
                        {
                            forTruncate *= 10;
                        }
                    }
                    return Math.Truncate(preVolume * forTruncate) / forTruncate; // при округлении может получиться больше доступного объема, поэтому обрезаем
                }
            }
            return preVolume;
        }

        private void UpdateOrder(string message)
        {
            try
            {//[0,"n",[1575289447641,"ou-req",null,null,[1185815100,null,1575289350475,"tETHUSD",1575289351944,1575289351948,-3,-3,"LIMIT",null,null,null,0,"ACTIVE",null,null,240,0,0,0,null,null,null,0,0,null,null,null,"API>BFX",null,null,null],null,"SUCCESS","Submitting update to limit sell order for 3 ETH."]]
             // [0,"ou",[1185815100,null,1575289350475,"tETHUSD",1575289351944,1575289447644,-3,-3,"LIMIT","LIMIT",null,null,0,"ACTIVE",null,null,240,0,0,0,null,null,null,0,0,null,null,null,"API>BFX",null,null,null]]

                List<object> rootArray = JsonConvert.DeserializeObject<List<object>>(message);

                // Извлекаем третий элемент, который является массивом
                string thirdElement = rootArray[2].ToString();

                // Десериализуем третий элемент как объект с типами, соответствующими структуре
                BitfinexResponseOrder response = JsonConvert.DeserializeObject<BitfinexResponseOrder>(thirdElement);

                // Теперь можно получить данные ордера
                List<BitfinexOrderData> orderResponse = response.OrderData;

                // Десериализация сообщения в объект BitfinexOrderData
                // var response = JsonConvert.DeserializeObject<List<BitfinexOrderData>>(message);

                if (response == null)
                {
                    return;
                }

                // Перебор всех ордеров в ответе
                for (int i = 0; i < orderResponse.Count; i++)
                {
                    BitfinexOrderData orderData = orderResponse[i];

                    if (string.IsNullOrEmpty(orderData.Cid))
                    {
                        continue; // Пропускаем ордера без Cid ИЛИ RETURN?
                    }

                    // Определение состояния ордера
                    OrderStateType stateType = GetOrderState(orderData.Status);

                    // Игнорируем ордера типа "EXCHANGE MARKET" и активные
                    if (orderData.OrderType.Equals("EXCHANGE LIMIT", StringComparison.OrdinalIgnoreCase) && stateType == OrderStateType.Activ)
                    {
                        continue;
                    }

                    // Создаем новый объект ордера
                    Order updateOrder = new Order();

                    updateOrder.SecurityNameCode = orderData.Symbol;
                    updateOrder.TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(orderData.MtsCreate));
                    updateOrder.NumberUser = Convert.ToInt32(orderData.Cid);
                    updateOrder.NumberMarket = orderData.Id;
                    updateOrder.Side = orderData.Amount.Equals("-") ? Side.Sell : Side.Buy; // Продаем, если количество отрицательное
                    updateOrder.State = stateType;
                    updateOrder.TypeOrder = orderData.OrderType.Equals("EXCHANGE MARKET", StringComparison.OrdinalIgnoreCase) ? OrderPriceType.Market : OrderPriceType.Limit;
                    updateOrder.Volume = Math.Abs(orderData.Amount.ToDecimal()); // Абсолютное значение объема
                    updateOrder.Price = orderData.Price.ToDecimal();
                    updateOrder.ServerType = ServerType.Bitfinex;
                    updateOrder.VolumeExecute = orderData.AmountOrig.ToDecimal();////////////////////
                    updateOrder.PortfolioNumber = "BitfinexPortfolio";


                    // Если ордер исполнен или частично исполнен, обновляем сделку
                    if (stateType == OrderStateType.Done || stateType == OrderStateType.Patrial)// Partial  
                    {
                        UpdateMyTrade(message);
                    }

                    // Вызываем событие для обновленного ордера
                    MyOrderEvent?.Invoke(updateOrder);

                }
            }
            catch (Exception exception)
            {
                // Логируем ошибку
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }


        private OrderStateType GetOrderState(string orderStateResponse)
        {
            // Объявляем переменную для хранения состояния ордера
            OrderStateType stateType = OrderStateType.None; // Начальное состояние

            switch (orderStateResponse)
            {
                case "ACTIVE":
                    stateType = OrderStateType.Activ; // Активный ордер
                    break;

                case "EXECUTED":
                    stateType = OrderStateType.Done; // Исполненный ордер
                    break;

                case "REJECTED":
                    stateType = OrderStateType.Fail; // Отклонённый ордер
                    break;

                case "CANCELED":
                    stateType = OrderStateType.Cancel; // Отменённый ордер
                    break;

                case "PARTIALLY FILLED":
                    stateType = OrderStateType.Patrial; // Частично исполненный ордер
                    break;

                default:
                    stateType = OrderStateType.None; // Неопределённое состояние
                    break;
            }

            return stateType;
        }

        #endregion


        /// <summary>
        /// посвящённый торговле. Выставление ордеров, отзыв и т.д
        /// </summary>
        #region  10 Trade

        private readonly RateGate _rateGateSendOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));//уточнить задержку  Таймфрейм в формате отрезка времени. TimeSpan.

        private readonly RateGate _rateGateCancelOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));

        public void SendOrder(Order order)
        {
            _rateGateSendOrder.WaitToProceed();


            string _apiPath = "v2/auth/w/order/submit";


            string typeOrder; // Переменная для хранения типа ордера

            string orderSide = "";

            // Условие для определения типа ордера
            typeOrder = order.TypeOrder.ToString() == "Limit" ? "EXCHANGE LIMIT" : "EXCHANGE MARKET";

            BitfinexOrderData newOrder = new BitfinexOrderData();

            newOrder.Cid = order.NumberUser.ToString();
            newOrder.Symbol = order.SecurityNameCode;
            newOrder.Amount = order.Volume.ToString().Replace(",", ".");
            newOrder.OrderType = order.TypeOrder.ToString().ToUpper();
            newOrder.Price = order.TypeOrder == OrderPriceType.Market ? null : order.Price.ToString().Replace(",", ".");
            newOrder.MtsCreate = order.TimeCreate.ToString();
            newOrder.Status = order.State.ToString();
            newOrder.MtsUpdate = order.TimeDone.ToString();
                // Id= order.NumberMarket
            

            if (decimal.TryParse(newOrder.Amount, out decimal amount))
            {
                // Изменение знака на отрицательный 

                if (order.Side.ToString() == "Sell")
                {// Преобразование обратно в строку
                    orderSide = (-amount).ToString();
                }

                else
                {
                    orderSide = newOrder.Amount; // Для остальных случаев используем положительное значение
                }
            }
            // Создание анонимного объекта с использованием переменной `type`
            var body = new
            {
                type = typeOrder,
                symbol = newOrder.Symbol,
                price = newOrder.Price,
                amount = orderSide
            };

            // Сериализуем объект тела в JSON

            string bodyJson = JsonSerializer.Serialize(body);
            IRestResponse response = ExecuteRequest(_apiPath, bodyJson);
            //string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();
           //  string nonce = GetNonce();

            //string signature = $"/api/{_apiPath}{nonce}{bodyJson}";

            //string sig = ComputeHmacSha384(_secretKey, signature);

            //RestClient client = new RestClient(_baseUrl);

            //RestRequest request = new RestRequest(_apiPath, Method.POST);

            ////// Добавляем заголовки
            //request.AddHeader("accept", "application/json");
            //request.AddHeader("bfx-nonce", nonce);
            //request.AddHeader("bfx-apikey", _publicKey);
            //request.AddHeader("bfx-signature", sig);

            //request.AddJsonBody(body);

            //IRestResponse response = client.Execute(request);

            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                { // Выводим тело ответа
                    string responseBody = response.Content;

                    // Десериализация ответа как массив объектов

                    // string responseBody = "[1723557977,\"on-req\",null,null,[[167966185075,null,1723557977011,\"tTRXUSD\",1723557977011,1723557977011,22,22,\"EXCHANGE LIMIT\",null,null,null,0,\"ACTIVE\",null,null,0.12948,0,0,0,null,null,null,0,0,null,null,null,\"API>BFX\",null,null,{}]],null,\"SUCCESS\",\"Submitting 1 orders.\"]";

                    // Десериализация верхнего уровня в список объектов
                    List<object> responseArray = JsonConvert.DeserializeObject<List<object>>(responseBody);

                    // Проверка размера массива перед извлечением значений
                    if (responseArray == null)
                    {
                        return;
                    }


                    // Парсим JSON-строку в JArray (массив JSON)
                    JArray jsonArray = JArray.Parse(responseBody);

                    //// Путь к статусу "ACTIVE" в JSON структуре
                    //string status = (string)jsonArray[4][0][13];

                    //if (responseArray.Contains("on-req"))
                    //{
                    //    // Извлечение нужных элементов
                    string dataJson = responseArray[4].ToString();
                    // string status = jsonArray[4][0][13].ToString();

                    //string status = responseArray[6].ToString();
                    string text = responseArray[7].ToString();

                    // Десериализация dataJson в список заказов
                    List<List<object>> ordersArray = JsonConvert.DeserializeObject<List<List<object>>>(dataJson);
                    List<object> orders = ordersArray[0]; // Получаем первый заказ из массива

                    // Создание объекта BitfinexOrderData
                    BitfinexOrderData orderData = new BitfinexOrderData();


                    //Cid = Convert.ToString(orders[2]),
                    orderData.Id = orders[0].ToString();
                    orderData.Symbol = orders[3].ToString();
                    orderData.Status = orders[13].ToString();
                    
                    OrderStateType stateType = GetOrderState(orderData.Status);

                    order.NumberMarket = orderData.Id;

                    order.State = stateType;


                    SendLogMessage($"Order num {order.NumberMarket} on exchange.{text}", LogMessageType.Trade);
                  ;

                    PortfolioEvent?.Invoke(_portfolios);///////////////

                    //UpdatePortfolio(_portfolios);

                    //если ордер исполнен, вызываем MyTradeEvent
                    if (order.State == OrderStateType.Done
                        || order.State == OrderStateType.Patrial)
                    {
                        // UpdateMyTrade(message);
                    }

                    GetPortfolios();
                }
                else
                {
                    //Content "[\"error\",10001,\"Invalid order: minimum size for TRXUSD is 22\"]"    

                    CreateOrderFail(order);
                    SendLogMessage($"Error Order exception {response.Content}", LogMessageType.Error);

                }
            }
            catch (Exception exception)
            {
                // Обрабатываем исключения и выводим сообщение
                CreateOrderFail(order);
                SendLogMessage("Order send exception " + exception.ToString(), LogMessageType.Error);
            }
            MyOrderEvent?.Invoke(order);
        }
        private void CreateOrderFail(Order order)
        {
            order.State = OrderStateType.Fail;

            MyOrderEvent?.Invoke(order);
        }

        private readonly RateGate rateGateCancelAllOrder = new RateGate(1, TimeSpan.FromMilliseconds(350));
        public void CancelAllOrders()
        {
            rateGateCancelAllOrder.WaitToProceed();

            string _apiPath = "v2/auth/w/order/cancel/multi";

            var body = new
            {
                all = 1  //1 отменить все ордера Идентификатор ордера для отмены
            };

            string bodyJson = JsonSerializer.Serialize(body);

            IRestResponse response = ExecuteRequest(_apiPath, bodyJson);

            //string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

           // string nonce = GetNonce();

            //Создаем строку для подписи
            //string signature = $"/api/{_apiPath}{nonce}{bodyJson}";


            //// Вычисляем подпись с использованием HMACSHA384
            //string sig = ComputeHmacSha384(_secretKey, signature);

            //// // Создаем клиента RestSharp
            //RestClient client = new RestClient(_baseUrl);

            ////// Создаем запрос типа POST
            //RestRequest request = new RestRequest(_apiPath, Method.POST);

            ////// Добавляем заголовки
            //request.AddHeader("accept", "application/json");
            //request.AddHeader("bfx-nonce", nonce);
            //request.AddHeader("bfx-apikey", _publicKey);
            //request.AddHeader("bfx-signature", sig);

            //// Добавляем тело запроса в формате JSON
            //request.AddJsonBody(body); //


            //// Отправляем запрос и получаем ответ
            //IRestResponse response = client.Execute(request);


            if (response == null)
            {
                return;
            }
            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string responseBody = response.Content;

                    // Десериализация верхнего уровня в список объектов
                    List<object> responseJson = JsonConvert.DeserializeObject<List<object>>(responseBody);

                    if (responseJson.Contains("oc_multi-req"))
                    {
                        SendLogMessage($"All active orders canceled: {response.Content}", LogMessageType.Trade);
                        GetPortfolios();
                    }

                    else
                    {
                        SendLogMessage($" {response.Content}", LogMessageType.Error);
                    }
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

            /////////////////////////
        }


        public void CancelOrder(Order order)
        {
            _rateGateCancelOrder.WaitToProceed();

            string _apiPath = "v2/auth/w/order/cancel";

            //если ордер уже отменен ничего не делаем
            if (order.State == OrderStateType.Cancel)//если ордер активный можно снять
            {
                return;
            }

            long orderId = Convert.ToInt64(order.NumberMarket);

            // Формирование тела запроса с указанием ID ордера
            var body = new
            {
                id = orderId // Идентификатор ордера для отмены
            };

            string bodyJson = JsonSerializer.Serialize(body);
            IRestResponse response  = ExecuteRequest(_apiPath, bodyJson);

            //string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();
            // string nonce = GetNonce();

            //string signature = $"/api/{_apiPath}{nonce}{bodyJson}";

            //string sig = ComputeHmacSha384(_secretKey, signature);

            //RestClient client = new RestClient(_baseUrl);

            //RestRequest request = new RestRequest(_apiPath, Method.POST);

            //request.AddHeader("accept", "application/json");
            //request.AddHeader("bfx-nonce", nonce);
            //request.AddHeader("bfx-apikey", _publicKey);
            //request.AddHeader("bfx-signature", sig);

            //request.AddJsonBody(body);

            //IRestResponse response = client.Execute(request);

            try
            // [0,"n",[1575291219660,"oc-req",null,null,[1185815100,null,1575289350475,"tETHUSD",1575289351944,1575289447644,-3,-3,"LIMIT","LIMIT",null,null,0,"ACTIVE",null,null,240,0,0,0,null,null,null,0,0,null,null,null,"API>BFX",null,null,null],null,"SUCCESS","Submitted for cancellation; waiting for confirmation (ID: 1185815100)."]]

            //[0,"oc",[1185815100,null,1575289350475,"tETHUSD",1575289351944,1575291219663,-3,-3,"LIMIT","LIMIT",null,null,0,"CANCELED",null,null,240,0,0,0,null,null,null,0,0,null,null,null,"API>BFX",null,null,null]]
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Выводим тело ответа
                    string responseBody = response.Content;
                    //[1727028393049,"oc-req",null,null,[174095780264,null,1727015028116,"tTRXUSD",1727015028117,1727020800228,22,22,"EXCHANGE LIMIT",null,null,null,0,"ACTIVE",null,null,0.10084,0,0,0,null,null,null,0,0,null,null,null,"API>BFX",null,null,{ }],null,"SUCCESS","Submitted for cancellation; waiting for confirmation (ID: 174095780264)."]
                    List<object> responseJson = JsonConvert.DeserializeObject<List<object>>(responseBody);

                    //if (responseJson.Contains("oc"))
                    //{
                    //if (responseJson.Contains("CANCELED"))
                    //{
                    SendLogMessage($"Order canceled Successfully. Order ID:{order.NumberMarket} {response.Content}", LogMessageType.Trade);
                    order.State = OrderStateType.Cancel;
                    MyOrderEvent(order);

                    GetPortfolios();
                }

                else
                {

                    CreateOrderFail(order);
                    SendLogMessage($" Error Order cancellation:  {response.Content}, {response.ErrorMessage}", LogMessageType.Error);
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

            using HMACSHA384 hmac = new HMACSHA384(Encoding.UTF8.GetBytes(apiSecret));
            byte[] output = hmac.ComputeHash(Encoding.UTF8.GetBytes(signature));
            return BitConverter.ToString(output).Replace("-", "").ToLower();
        }

        private readonly RateGate rateGateChangePriceOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));


        public void ChangeOrderPrice(Order order, decimal Oldprice)
        {
            // ou - req", //TYPE
            // post https://api.bitfinex.com/v2/auth/w/order/update/////// 

            rateGateChangePriceOrder.WaitToProceed();
            try
            {
                // Проверка типа ордера
                if (order.TypeOrder == OrderPriceType.Market)
                {
                    SendLogMessage("Can't change price for  Order Market", LogMessageType.Error);
                    return;
                }

                string _apiPath = "v2/auth/w/order/update";

                var body = new
                {
                    id = order.NumberMarket,  // Идентификатор ордера
                    price = Oldprice.ToString(), // Новая цена
                                                 // amount = order.Volume.ToString()// новый объем
                };
               
               // string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();
               //  string nonce = GetNonce();
                string bodyJson = JsonSerializer.Serialize(body);
                var response = ExecuteRequest(_apiPath, bodyJson);
                //string signature = $"/api/{_apiPath}{nonce}{bodyJson}";

                //string sig = ComputeHmacSha384(_secretKey, signature);

                //RestClient client = new RestClient(_baseUrl);

                //RestRequest request = new RestRequest(_apiPath, Method.POST);

                //request.AddHeader("accept", "application/json");
                //request.AddHeader("bfx-nonce", nonce);
                //request.AddHeader("bfx-apikey", _publicKey);
                //request.AddHeader("bfx-signature", sig);

                //request.AddJsonBody(body);

                //IRestResponse response = client.Execute(request);


                int qty = Convert.ToInt32(order.Volume - order.VolumeExecute);

                if (qty <= 0 || order.State != OrderStateType.Activ)
                {
                    SendLogMessage("Can't change price for the order. It is not in Active state", LogMessageType.Error);
                    return;
                }
                if (order.State == OrderStateType.Cancel)//если ордер активный можно снять
                {
                    return;
                }

                // if(order.State == OrderStateType.Activ)

                if (response.StatusCode == HttpStatusCode.OK)
                {  // Выводим тело ответа
                    string responseBody = response.Content;

                    string newPrice = responseBody;
                    // ПЕРЕДЕЛАТЬ!!!!!!!!!
                    order.Price = newPrice.ToDecimal();/////////////////


                    //SendLogMessage("Order change price. New price: " + newPrice
                    //  + "  " + order.SecurityNameCode, LogMessageType.Trade);//LogMessageType.System

                }
                else
                {
                    SendLogMessage("Change price order Fail. Status: "
                                + response.Content + "  " + order.SecurityNameCode, LogMessageType.Error);

                    if (response.Content != null)
                    {
                        SendLogMessage("Fail reasons: "
                      + response.Content, LogMessageType.Error);
                    }
                }
                // Вызов события изменения ордера
                MyOrderEvent?.Invoke(order);
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);

            }
        }


        #endregion


        #region  11 Queries

        public void CancelAllOrdersToSecurity(Security security)
        {
            throw new NotImplementedException();
        }


        public List<Order> GetAllOrdersFromExchange()
        {
            // post https://api.bitfinex.com/v2/auth/r/orders

            List<Order> orders = new List<Order>();

            string _apiPath = "v2/auth/r/orders";
            IRestResponse response = ExecuteRequest(_apiPath);
           // string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();
            // string nonce = GetNonce();
            //string signature = $"/api/{_apiPath}{nonce}";
            //string sig = ComputeHmacSha384(_secretKey, signature);
            //RestClient client = new RestClient(_baseUrl);
            //RestRequest request = new RestRequest(_apiPath, Method.POST);

            //request.AddHeader("accept", "application/json");
            //request.AddHeader("bfx-nonce", nonce);
            //request.AddHeader("bfx-apikey", _publicKey);
            //request.AddHeader("bfx-signature", sig);

            //IRestResponse response = client.Execute(request);

            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {

                    string responseBody = response.Content;

                    //var listOrders = JsonConvert.DeserializeObject<List<List<BitfinexOrderData>>>(response.Content);
                    List<List<object>> listOrders = JsonConvert.DeserializeObject<List<List<object>>>(response.Content);

                    List<BitfinexOrderData> activeOrders = new List<BitfinexOrderData>();

                    if (orders != null && orders.Count > 0)
                    {
                        for (int i = 0; i < orders.Count; i++)
                        {
                            Order activOrder = new Order();

                            activOrder.TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(long.Parse(activeOrders[i].MtsUpdate));
                            activOrder.TimeCreate = TimeManager.GetDateTimeFromTimeStamp(long.Parse(activeOrders[i].MtsCreate));
                            activOrder.ServerType = ServerType.Bitfinex;
                            activOrder.SecurityNameCode = activeOrders[i].Symbol;
                            activOrder.NumberUser = Convert.ToInt32(activeOrders[i].Cid);
                            activOrder.NumberMarket = activeOrders[i].Id;
                            activOrder.Side = activeOrders[i].Amount.Equals("-") ? Side.Sell : Side.Buy;
                            activOrder.State = GetOrderState(activeOrders[i].Status);
                            activOrder.Volume = activeOrders[i].Amount.ToDecimal();
                            activOrder.Price = activeOrders[i].Price.ToDecimal();
                            activOrder.VolumeExecute = activeOrders[i].AmountOrig.ToDecimal();
                            activOrder.PortfolioNumber = "BitfinexPortfolio";
       
                            orders.Add(activOrder);

                            //orders[i].TimeCreate = orders[i].TimeCallBack;

                            MyOrderEvent?.Invoke(orders[i]);
                        }
                    }
                }
                else
                {
                    SendLogMessage($" Can't get all orders. State Code: {response.Content}", LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

            return orders;
        }
        public void GetOrderStatus(Order order)
        {
            // Получаем ордер с биржи по рыночному номеру ордера
            Order orderFromExchange = GetOrderFromExchange(order.NumberMarket);

            if (orderFromExchange == null)
            {
                return;
            }
            // Объявляем переменную для хранения ордера на рынке
            Order orderOnMarket = null;

            // Если пользовательский номер ордера (NumberUser) и номер ордера с биржи (NumberUser) совпадают, сохраняем ордер с биржи
            if (order.NumberUser != 0 && orderFromExchange.NumberUser != 0 && orderFromExchange.NumberUser == order.NumberUser)
            {
                orderOnMarket = orderFromExchange;
            }

            // Если рыночный номер ордера (NumberMarket) совпадает, также сохраняем ордер с биржи
            if (!string.IsNullOrEmpty(order.NumberMarket) && order.NumberMarket == orderFromExchange.NumberMarket)
            {
                orderOnMarket = orderFromExchange;
            }

            // Если ордер на рынке не найден, выходим из метода
            if (orderOnMarket == null)
            {
                return;
            }

            // Если ордер на рынке найден и существует обработчик события, вызываем событие MyOrderEvent
            if (orderOnMarket != null && MyOrderEvent != null)
            {
                MyOrderEvent(orderOnMarket);
            }

            // Проверяем состояние ордера: если ордер выполнен (Done) или частично выполнен (Patrial)
            if (orderOnMarket.State == OrderStateType.Done || orderOnMarket.State == OrderStateType.Patrial)
            {
                // Получаем список сделок по номеру ордера
                List<MyTrade> tradesBySecurity = GetMyTradesBySecurity(order.SecurityNameCode, order.NumberMarket);

                // Если сделки не найдены, выходим из метода
                if (tradesBySecurity == null)
                {
                    return;
                }

                // Объявляем список для хранения сделок, связанных с данным ордером
                List<MyTrade> tradesByMyOrder = new List<MyTrade>();

                // Используем цикл for для перебора всех сделок в списке tradesBySecurity
                for (int i = 0; i < tradesBySecurity.Count; i++)
                {
                    // Если сделка связана с данным ордером (по совпадению родительского номера ордера), добавляем её в список
                    if (tradesBySecurity[i].NumberOrderParent == orderOnMarket.NumberMarket)
                    {
                        tradesByMyOrder.Add(tradesBySecurity[i]);
                    }
                }

                // Используем цикл for для обработки всех найденных сделок по ордеру
                for (int i = 0; i < tradesByMyOrder.Count; i++)
                {
                    // Если существует обработчик события MyTradeEvent, вызываем его для каждой сделки
                    MyTradeEvent?.Invoke(tradesByMyOrder[i]);
                }
            }
        }

        private Order GetOrderFromExchange(string numberMarket)
        {
            string _apiPath = "v2/auth/r/orders";
            var body = new
            {
                id = numberMarket

            };
           // string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();
           // string nonce = GetNonce();

            string bodyJson = JsonSerializer.Serialize(body);

            IRestResponse response = ExecuteRequest(_apiPath, bodyJson);

            //string signature = $"/api/{_apiPath}{nonce}{bodyJson}";

            //string sig = ComputeHmacSha384(_secretKey, signature);

            //RestClient client = new RestClient(_baseUrl);

            //RestRequest request = new RestRequest(_apiPath, Method.POST);

            //request.AddHeader("accept", "application/json");
            //request.AddHeader("bfx-nonce", nonce);
            //request.AddHeader("bfx-apikey", _publicKey);
            //request.AddHeader("bfx-signature", sig);

            //request.AddJsonBody(body);

            //IRestResponse response = client.Execute(request);

            // Десериализуем ответ
            // var response1 = JsonConvert.DeserializeObject<List<List<object>>>(responseBody);

            List<BitfinexOrderData> listOrder = new List<BitfinexOrderData>();
            Order newOrder = new Order();
            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string responseBody = response.Content;

                    for (int i = 0; i < listOrder.Count; i++)
                    {
                        BitfinexOrderData order = listOrder[i];

                       
                        newOrder.TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(order.MtsUpdate));
                        newOrder.TimeCreate = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(order.MtsCreate));
                        newOrder.ServerType = ServerType.Bitfinex;
                        newOrder.SecurityNameCode = order.Symbol;
                        newOrder.NumberMarket = numberMarket;
                        newOrder.Side = order.Amount.Equals("-") ? Side.Sell : Side.Buy;
                        newOrder.State = OrderStateType.Activ;
                        newOrder.Volume = order.Amount.ToDecimal();
                        newOrder.Price = order.Price.ToDecimal();
                        newOrder.PortfolioNumber = "BitfinexPortfolio";
                        newOrder.VolumeExecute = order.AmountOrig.ToDecimal();
                    }
                }
                else
                {
                    SendLogMessage($"GetOrderState. Http State Code: {response.Content}", LogMessageType.Error);
                }
            }

            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

            return newOrder;
        }

        private List<MyTrade> GetMyTradesBySecurity(string security, string orderId)
        {
            try
            {
                // https://api.bitfinex.com/v2/auth/r/trades/{symbol}/hist

                string _apiPath = $"v2/auth/r/trades/{security}";

                var body = new
                {
                    id = orderId,
                    symbol = security
                };
               // string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

                // string nonce = GetNonce();

                string bodyJson = JsonSerializer.Serialize(body);
                IRestResponse response = ExecuteRequest(_apiPath, bodyJson);

                //string signature = $"/api/{_apiPath}{nonce}{bodyJson}";

                //string sig = ComputeHmacSha384(_secretKey, signature);

                //RestClient client = new RestClient(_baseUrl);

                //RestRequest request = new RestRequest(_apiPath, Method.POST);

                //request.AddHeader("accept", "application/json");
                //request.AddHeader("bfx-nonce", nonce);
                //request.AddHeader("bfx-apikey", _publicKey);
                //request.AddHeader("bfx-signature", sig);

                //request.AddJsonBody(body);

                //IRestResponse response = client.Execute(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    //string responseBody = response.Content;

                    CreateQueryPosition();
                }
                else
                {
                    SendLogMessage($" {response.Content}", LogMessageType.Error);
                }

                return new List<MyTrade>();
            }
            catch (Exception exception)
            {

                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

            return null;
        }
        public List<Candle> GetCandleDataToSecurity(string security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            throw new NotImplementedException();
        }



        #endregion
        /// <summary>
        /// Логирование.
        /// </summary>

        public void GetAllActivOrders()
        {

            List<Order> orders = GetAllOrdersFromExchange();

            for (int i = 0; orders != null && i < orders.Count; i++)
            {
                if (orders[i] == null)
                {
                    continue;
                }

                //if (orders[i].State != OrderStateType.Activ
                //    && orders[i].State != OrderStateType.Patrial
                //    && orders[i].State != OrderStateType.Pending)
                //{
                //    continue;
                //}

                orders[i].TimeCreate = orders[i].TimeCallBack;

                MyOrderEvent?.Invoke(orders[i]);
            }
        }

        //// Начальное значение nonce на основе текущего времени
        //private long _lastNonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ;

        //private readonly object _lock = new object();

        //public string GetNonce()
        //{
        //    lock (_lock)
        //    {

        //        long currentNonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 10000;//если * 1000 и меньше,то не работает. Работает если *10000,но происходит переполнение

        //        //// Если новое значение меньше или равно предыдущему, увеличиваем его
        //        if (currentNonce <= _lastNonce)
        //        {
        //            currentNonce = _lastNonce + 1;
        //        }

        //        // Сохраняем новое значение
        //        _lastNonce = currentNonce;

        //        //// Возвращаем значение в виде строки
        //        return currentNonce.ToString();
        //    }
        //}


        #region 12 Log

        public event Action<string, LogMessageType> LogMessageEvent;
        private void SendLogMessage(string message, LogMessageType messageType)
        {
            LogMessageEvent(message, messageType);
        }
        #endregion


        #region Методы,которые, могут пригодиться

        //  string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();
        // NONCE-	постоянно увеличивающееся число int максимально содержащее 9007199254740991.


        // var response = ExecuteRequest(_apiPath, bodyJson);

        // Метод для выполнения запросов GET или POST в зависимости от параметра




        // Глобальный счетчик nonce
        private static long _lastNonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); // Начальное значение - текущее время в миллисекундах
        private static readonly object _nonceLock = new object();
        private const long MaxNonceValue = 9007199254740991; // Максимальное допустимое значение nonce

        // Метод для получения следующего nonce
        public static string GetNextNonce()
        {
            lock (_nonceLock)
            {
                // Текущее время в миллисекундах
                long currentNonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // Если текущее значение nonce меньше или равно предыдущему, увеличиваем его
                if (currentNonce <= _lastNonce)
                {
                    _lastNonce = _lastNonce + 1;
                }
                else
                {
                    _lastNonce = currentNonce; // Обновляем последнее значение nonce на текущее время
                }

                // Если значение nonce превышает максимальное значение, сбрасываем
                if (_lastNonce >= MaxNonceValue)
                {
                    _lastNonce = 1; // Сбрасываем на минимальное значение
                }

                return _lastNonce.ToString(); // Возвращаем nonce как строку
            }
        }



        // Метод для выполнения запроса
        public IRestResponse ExecuteRequest(string apiPath, string body = null)
        {
            // Генерация уникального nonce
            string nonce = GetNextNonce();

            // Определение метода запроса (POST для авторизованных запросов, GET для других)
            Method method = apiPath.ToLower().Contains("auth") ? Method.POST : Method.GET;

            // Формирование строки для подписи
            string signature = method == Method.GET ?
                $"/api/{apiPath}" :
                $"/api/{apiPath}{nonce}{body}";

            // Генерация подписи HMAC-SHA384
            string sig = ComputeHmacSha384(_secretKey, signature);

            // Создаем клиента RestSharp
            var client = new RestClient(_baseUrl);

            // Выбираем тип запроса
            var request = new RestRequest(apiPath, method);

            // Добавляем заголовки
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            // Если это POST запрос, добавляем тело запроса
            if (method == Method.POST && body != null)
            {
                request.AddJsonBody(body);
            }

            // Выполняем запрос и возвращаем ответ
            return client.Execute(request);
        }




        //public IRestResponse ExecuteRequest(string apiPath, string nonce = null,string body = null)
        //{
        //    string method;
        //    // Генерация уникального идентификатора запроса (nonce)
        //    //string nonce = GetNonce();

        //    // nonce = GetNonce();

        //    if (nonce == null)
        //    {
        //       // nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

        //        nonce = GetNonce();
        //    }

        //    // Если apiPath содержит слово "auth", то устанавливаем метод  POST
        //    if (apiPath.ToLower().Contains("auth"))
        //    {
        //        method = "POST";
        //    }
        //    else
        //    {
        //        // Иначе метод будет GET
        //        method = "GET";
        //    }

        //    //if (body != null)
        //    //{
        //    //    body = JsonSerializer.Serialize(body);////это правильно или нет
        //    //}
        //    // Формирование строки для подписи в зависимости от типа запроса
        //    string signature = method.Equals("GET", StringComparison.OrdinalIgnoreCase) ?
        //        $"/api/{apiPath}" :
        //        $"/api/{apiPath}{nonce}{body}";


        //    // Генерация подписи HMAC-SHA384
        //    string sig = ComputeHmacSha384(_secretKey, signature);


        //    // Создаем клиента RestSharp
        //    var client = new RestClient(_baseUrl);

        //    // Выбираем тип запроса
        //    RestRequest request = new RestRequest(apiPath, method.Equals("GET", StringComparison.OrdinalIgnoreCase) ? Method.GET : Method.POST);

        //    // Добавляем заголовки
        //    request.AddHeader("accept", "application/json");
        //    request.AddHeader("bfx-nonce", nonce);
        //    request.AddHeader("bfx-apikey", _publicKey);
        //    request.AddHeader("bfx-signature", sig);

        //    // Если это POST запрос, добавляем тело запроса
        //    if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) && body != null)
        //    {
        //        request.AddJsonBody(body);
        //    }

        //    // Выполняем запрос и возвращаем ответ
        //    return client.Execute(request);
        //}

        ////////////////////////////////////////////////////////////отписаться от всех подписок
        //public class BitfinexUnsubscribe
        //{
        //    // Список идентификаторов активных каналов
        //    private List<int> channelIds = new List<int>();

        //    // Метод для отписки от всех событий
        //    public void UnsubscribeAll()
        //    {
        //        try
        //        {
        //            // Проходим по каждому идентификатору канала и отправляем команду отписки
        //            for (int i = 0; i < channelIds.Count; i++)
        //            {
        //                int channelId = channelIds[i];

        //                // Формируем команду отписки для канала
        //                string unsubscribeMessage = $"{{\"event\": \"unsubscribe\", \"chanId\": {channelId}}}";

        //                // Отправляем команду через WebSocket
        //                SendWebSocketMessage(unsubscribeMessage);
        //            }

        //            // Очищаем список после отписки от всех каналов
        //            channelIds.Clear();
        //        }
        //        catch (Exception exception)
        //        {
        //            SendLogMessage(exception.ToString(), LogMessageType.Error);
        //        }
        //    }


        ////////////////////////////////////////////////////////

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

        //public void UnsubscribeFromChannel(int channelId)//отписаться от канала
        //{
        //    var unsubscribeMessage = new
        //    {
        //        @event = "unsubscribe",
        //        chanId = channelId
        //    };

        //    string message = JsonConvert.SerializeObject(unsubscribeMessage);

        //    // Отправка сообщения через WebSocket
        //    webSocket.Send(message);
        //}


        //private OrderStateType GetOrderState(string orderStateResponse)
        //{
        //    var stateType = orderStateResponse switch
        //    {
        //        "ACTIVE" => OrderStateType.Activ,
        //        "EXECUTED" => OrderStateType.Done,
        //        "REJECTED" => OrderStateType.Fail,
        //        "CANCELED" => OrderStateType.Cancel,
        //        "PARTIALLY FILLED" => OrderStateType.Patrial,
        //        _ => OrderStateType.None,
        //    };
        //    return stateType;
        //}

        //else if (baseMessage.status == "CANCELED")
        //{
        //    lock (_changePriceOrdersArrayLocker)
        //    {
        //        DateTime now = DateTime.UtcNow;
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

        //if (securityList[i].Contains("e") || securityList[i].Contains("E"))
        //              {

        //    // Преобразуем строку в число, используя double.Parse
        //    double scientificNumber = double.Parse(item[i].ToString(), System.Globalization.NumberStyles.Float);

        // }



        ///////////////////////////////////////////////////////////////
        ///  // PositionOnBoard position = new PositionOnBoard
        //            {
        //                PortfolioName = portfolios[i].Type,
        //                ValueBegin = availableBalance,
        //                ValueCurrent = availableBalance,
        //                ValueBlocked = unsettledInterest,
        //                SecurityNameCode = portfolios[i].Currency
        //            };




        //private void UpdatePortfolio(string message/*bool isUpdateValueBeginList<BitfinexPortfolioRest> portfolios*/)
        //{
        //    try
        //    {
        //        var jsonDoc = JsonDocument.Parse(message);
        //        var root = jsonDoc.RootElement;

        //        List<BitfinexPortfolioRest> response = JsonConvert.DeserializeObject<List<BitfinexPortfolioRest>>(message);

        //         if (root.ValueKind == JsonValueKind.Array)
        //        {
        //            // Handle data messages
        //            int channelId = root[0].GetInt32();
        //            string msgType = root[1].GetString();

        //            if (channelId == 0)
        //            {
        //                // Wallet messages
        //                switch (msgType)
        //                {
        //                    case "ws":
        //                        SendLogMessage("Received wallet snapshot", LogMessageType.System);
        //                       // HandleWalletSnapshot(root[2]);
        //                        break;
        //                    case "wu":
        //                        SendLogMessage("Received wallet update", LogMessageType.System);
        //                       // HandleWalletUpdate(root[2], response);
        //                        break;
        //                }
        //            }
        //        }


        /// <summary>
        /// Проверка вёбсокета на работоспособность путём отправки ему пингов.
        /// </summary>
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
        //       ("WebSocket is not open. Ping not sent.");
        //    }


        //}



        #endregion



    }
}





