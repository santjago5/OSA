
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
using System.Text;
using System.Text.Json;
using System.Threading;
using WebSocket4Net;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;
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
                _apiPath = "v2/platform/status";


                // // Отправляем запрос на сервер Bitfinex и получаем ответ
                //// var response = ExecuteRequest(_apiPath);


                // // Если ответ успешный, обновляем позиции
                // if (response.StatusCode == HttpStatusCode.OK)
                // {
                //     // Выводим тело ответа
                //     string responseBody = response.Content;
                //     CreateWebSocketConnection();

                // }


                RestClient client = new RestClient(_baseUrl);
                RestRequest request = new RestRequest(_apiPath, Method.GET);
                request.AddHeader("accept", "application/json");

                IRestResponse response = client.Execute(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string responseBody = response.Content; //надо или нет
                    if (responseBody.Contains("1"))
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

        private string _apiPath;

        private string _baseUrl = "https://api.bitfinex.com";


       // string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).ToString(); //берет время сервера без учета локального

        #endregion



        /// <summary>
        /// Запрос доступных для подключения бумаг у подключения. 
        /// </summary>
        #region 3 Securities

        private RateGate _rateGateGetsecurity = new RateGate(1, TimeSpan.FromMilliseconds(200));

        private RateGate _rateGatePositions = new RateGate(1, TimeSpan.FromMilliseconds(200));

        private List<Security> _securities = new List<Security>();

        public event Action<List<Security>> SecurityEvent;

        public void GetSecurities()
        {
            _rateGateGetsecurity.WaitToProceed();

            try
            {// string sec= "tBTCUSD";
             //_apiPath = $"v2/ticker/{sec}";

                _apiPath = "v2/tickers?symbols=ALL";

                RestClient client = new RestClient(_baseUrl);
                RestRequest request = new RestRequest(_apiPath, Method.GET);
                request.AddHeader("accept", "application/json");

                IRestResponse response = client.Execute(request);


                // var response = ExecuteRequest(_apiPath);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonResponse = response.Content;

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

                        var item = securityList[i];

                        BitfinexSecurity ticker = new BitfinexSecurity
                        {
                            Symbol = item[0].ToString(),
                            Bid = ConvertScientificNotation(item[1].ToString()),
                            BidSize = ConvertScientificNotation(item[2].ToString()),
                            Ask = ConvertScientificNotation(item[3].ToString()),
                            AskSize = ConvertScientificNotation(item[4].ToString()),
                            DailyChange = ConvertScientificNotation(item[5].ToString()),
                            DailyChangeRelative = ConvertScientificNotation(item[6].ToString()),
                            LastPrice = ConvertScientificNotation(item[7].ToString()),
                            Volume = ConvertScientificNotation(item[8].ToString()),
                            High = ConvertScientificNotation(item[9].ToString()),
                            Low = ConvertScientificNotation(item[10].ToString())

                        };

                        security.Add(ticker);

                    }
                    Thread.Sleep(2000);
                    UpdateSecurity(jsonResponse);////////надо или нет
                }


                //else
                //{
                //    //SendLogMessage($"Result: LogMessageType.Error\n"
                //    //    + $"Message: {stateResponse.message}", LogMessageType.Error);
                //}


                else
                {
                    SendLogMessage("Securities /*request exception.*/ Status: " + response.Content, LogMessageType.Error);

                }

            }
            catch (Exception exception)
            {
                SendLogMessage("/*Securities request */exception" + exception.ToString(), LogMessageType.Error);

            }
        }


        // Метод для преобразования строки в decimal с учетом научной нотации
        private string ConvertScientificNotation(string value)
        {
            // Преобразование строки в decimal с учетом научной нотации
            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            {
                return result.ToString(CultureInfo.InvariantCulture);
            }
            return value;
        }

        private void UpdateSecurity(string json)
        {
            // Десериализация ответа от Bitfinex API
            var response = JsonConvert.DeserializeObject<List<List<object>>>(json);

            List<Security> securities = new List<Security>();

            // Проходим по каждому элементу в ответе
            // for (int i = 0; i < 3; i++)
            for (int i = 0; i < response.Count; i++)
            {
                var item = response[i];

                // Символ актива (например, "tBTCUSD")
                string symbol = item[0]?.ToString();


                // Определяем тип актива (например, если символ начинается с 't', это может быть торговая пара)
                SecurityType securityType = GetSecurityType(symbol);

                // Пропускаем активы, если не удается определить их тип
                if (securityType == SecurityType.None)
                {
                    continue;
                }

                // Создаем объект Security
                Security newSecurity = new Security
                {
                    Exchange = ServerType.Bitfinex.ToString(),  // Указываем биржу Bitfinex
                    Name = symbol,          // Название актива
                    NameFull = symbol,      // Полное название актива
                    NameClass = symbol.StartsWith("f") ? "Futures" : "CurrencyPair",   // Класс актива, напримерSpot"
                    NameId = symbol,        // Идентификатор актива
                    SecurityType = securityType, // Тип актива
                    Lot = 1,      // Предполагаем, что это контрактный размер или лот
                    State = SecurityStateType.Activ//: SecurityStateType.Close, // Состояние актива

                };
                newSecurity.PriceStep = newSecurity.Decimals.GetValueByDecimals();
                newSecurity.PriceStepCost = newSecurity.PriceStep;
                newSecurity.DecimalsVolume = newSecurity.Decimals;



                // Добавляем актив в список
                securities.Add(newSecurity);
            }

            // Вызываем событие обновления списка активов
            SecurityEvent(securities);

        }


        private SecurityType GetSecurityType(string type)
        {
            // Инициализируем переменную с значением по умолчанию
            SecurityType _securityType;

            // Проверяем, начинается ли строка с символа 't' и назначаем соответствующий тип
            if (type.StartsWith("t"))
            {
                _securityType = SecurityType.CurrencyPair;
            }
            else
            {
                _securityType = SecurityType.Futures;
            }

            // Возвращаем определённый тип
            return _securityType;
        }


        /// <summary>
        /// Запрос доступных портфелей у подключения. 
        /// </summary>
        #region 4 Portfolios

        private List<Portfolio> _portfolios = new List<Portfolio>();

        public event Action<List<Portfolio>> PortfolioEvent;

        private RateGate _rateGatePortfolio = new RateGate(1, TimeSpan.FromMilliseconds(200));


        public void GetPortfolios()
        {
            if (_portfolios.Count != 0)
            {
                PortfolioEvent?.Invoke(_portfolios);
            }

            CreateQueryPortfolio();

        }
        // Метод для создания запроса на получение портфеля
        private void CreateQueryPortfolio()
        {
            // Ожидаем перед выполнением запроса для соблюдения лимитов запросов
            _rateGatePortfolio.WaitToProceed();


            try
            {
                _apiPath = "v2/auth/r/wallets";

              string nonce = GetNonce();
                Thread.Sleep(2000);
                // var nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();
                // Отправляем запрос на сервер Bitfinex и получаем ответ
                //  var response = ExecuteRequest(_apiPath, nonce());
                //  var response = ExecuteRequest(_apiPath);

                //Создаем nonce как текущее время в миллисекундах
                // string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();
               
                // Создаем строку для подписи
                string signature = $"/api/{_apiPath}{nonce}";
                //Создаем строку для подписи



                // Вычисляем подпись с использованием HMACSHA384
                string sig = ComputeHmacSha384(_secretKey, signature);

                // Создаем клиента RestSharp
                var client = new RestClient(_baseUrl);

                // Создаем запрос типа POST
                var request = new RestRequest(_apiPath, Method.POST);


                // Добавляем заголовки
                request.AddHeader("accept", "application/json");
                request.AddHeader("bfx-nonce", nonce);
                request.AddHeader("bfx-apikey", _publicKey);
                request.AddHeader("bfx-signature", sig);

                var response = client.Execute(request);

                // Если ответ успешный, обновляем портфель и запрашиваем позиции
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Выводим тело ответа
                    string responseBody = response.Content;

                    UpdatePortfolio(responseBody); // Обновляем портфель
                                                   /////надо или нет
                    CreateQueryPosition(); // запрос позиций
                }
                else
                {
                    // Логируем ошибку, если запрос не удался
                    SendLogMessage($"Error Query Portfolio: {response.Content}", LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                // Логируем исключение в случае ошибки
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        // Метод для обновления портфеля на основе полученных данных
        private void UpdatePortfolio(string json)
        {
            // Десериализуем ответ от Bitfinex в объект портфеля
            List<List<object>> response = JsonConvert.DeserializeObject<List<List<object>>>(json);

            Portfolio portfolio = new Portfolio();
            portfolio.Number = "BitfinexPortfolio"; // Устанавливаем идентификатор портфеля
            portfolio.ValueBegin = response[0][2].ToString().ToDecimal();
            portfolio.ValueCurrent = response[0][4].ToString().ToDecimal();

            // Проходимся по всем полученным данным о портфеле
            for (int i = 0; i < response.Count; i++)
            {
                List<object> wallet = response[i];

                if (wallet[0].ToString() == "exchange")
                {

                    // Создаем позицию на основе данных из ответа
                    PositionOnBoard pos = new PositionOnBoard()
                    {
                        PortfolioName = "BitfinexPortfolio",
                        SecurityNameCode = wallet[1].ToString(), // Код валюты (например, "USD", "BTC")
                        ValueBegin = wallet[2].ToString().ToDecimal(), // Начальное значение
                        ValueCurrent = (wallet[4]).ToString().ToDecimal(), // Текущая стоимость
                        ValueBlocked = (wallet[2]).ToString().ToDecimal() - (wallet[4]).ToString().ToDecimal() // Заблокированное значение
                    };

                    // Добавляем новую позицию в портфель
                    portfolio.SetNewPosition(pos);
                }
            }

            // Вызываем событие обновления портфеля с новыми данными
            PortfolioEvent(new List<Portfolio> { portfolio });
        }

        // Метод для создания запроса на получение позиций
        private void CreateQueryPosition()
        {
            _rateGatePositions.WaitToProceed(); // Ожидаем перед выполнением запроса для соблюдения лимитов запросов

            string nonce= GetNonce();

            // ПОЛУЧЕНИЕ АКТИВНЫХ ПОЗИЦИЙ
            ///*https://api.bitfinex.com/v2/auth/r/orders */                      

            try
            {
                _apiPath = "v2/auth/r/positions";// нулевой массив
                                                
                Thread.Sleep(2000);
             

                //Создаем строку для подписи
                string signature = $"/api/{_apiPath}{nonce}";


                // Вычисляем подпись с использованием HMACSHA384
                string sig = ComputeHmacSha384(_secretKey, signature);

                // // Создаем клиента RestSharp
                var client = new RestClient(_baseUrl);

                //// Создаем запрос типа POST
                var request = new RestRequest(_apiPath, Method.POST);

                //// Добавляем заголовки
                request.AddHeader("accept", "application/json");
                request.AddHeader("bfx-nonce", nonce);
                request.AddHeader("bfx-apikey", _publicKey);
                request.AddHeader("bfx-signature", sig);


                // Отправляем запрос и получаем ответ
                var response = client.Execute(request);

                // Если ответ успешный, обновляем позиции
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Выводим тело ответа
                    string responseBody = response.Content;
                    UpdatePosition(responseBody); // Обновляем позиции  приходит пустой массив
                }
                else
                {
                    // Логируем ошибку, если запрос не удался
                    SendLogMessage($"Create Query Position: {response.Content}", LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                // Логируем исключение в случае ошибки
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
            Thread.Sleep(2000);
        }

        // Метод для обновления позиций на основе полученных данных
        private void UpdatePosition(string json)
        {
            // Десериализуем ответ от Bitfinex в объект списка позиций
            List<List<object>> response = JsonConvert.DeserializeObject<List<List<object>>>(json);

            Portfolio portfolio = new Portfolio();
            portfolio.Number = "BitfinexPortfolio"; // Устанавливаем идентификатор портфеля
            portfolio.ValueBegin = 1;
            portfolio.ValueCurrent = 1;

            // Проходимся по всем полученным данным о позициях
            for (int i = 0; i < response.Count; i++)
            {
                List<object> position = response[i];

                // Создаем позицию на основе данных из ответа
                PositionOnBoard pos = new PositionOnBoard()
                {
                    PortfolioName = "BitfinexPortfolio",
                    SecurityNameCode = position[3].ToString(), // Код инструмента (например, "tBTCUSD")&&&&&&&&&&&&
                    ValueBegin = position[7].ToString().ToDecimal(), // Начальное значение
                    ValueBlocked = (position[7]).ToString().ToDecimal() - (position[6]).ToString().ToDecimal(), // Заблокированное значение
                    ValueCurrent = (position[6]).ToString().ToDecimal()// 

                };
                portfolio.SetNewPosition(pos);
            }

            // Вызываем событие обновления портфеля с новыми данными
            PortfolioEvent(new List<Portfolio> { portfolio });
        }

        // PositionOnBoard position = new PositionOnBoard
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




        #endregion


        /// <summary>
        /// Запросы данных по свечкам и трейдам. 
        /// </summary>
        #region 5 Data 

        private readonly RateGate _rateGateCandleHistory = new RateGate(1, TimeSpan.FromSeconds(300));//70

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
                    return null; // нет данных

                // rangeCandles.Reverse();

                candles.InsertRange(0, rangeCandles);

                if (candles.Count != 0)
                {
                    endTime = candles[0].TimeStart;
                }

                needToLoadCandles -= limit;

            }

            while (needToLoadCandles > 0);

            return candles;
        }



        public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)/////string
        {

            //if (startTime != actualTime)
            //{
            //    startTime = actualTime;
            //}
            if (actualTime < startTime)
            {
                startTime = actualTime;
            }

            //if (actualTime > endTime)
            //{
            //    return null;
            //}

            if (startTime > endTime)
            {
                return null;
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
            // return tf.Minutes != 0 ? $"{tf.Minutes}m" : $"{tf.Hours}h";
            if (tf.Minutes != 0)
            {
                return $"{tf.Minutes}m";
            }
            else
            {
                return $"{tf.Hours}h";
            }
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


            string startTime = Convert.ToInt64((timeFrom - yearBegin).TotalMilliseconds).ToString();
            string endTime = Convert.ToInt64((timeTo - yearBegin).TotalMilliseconds).ToString();






            ///
            //string section = timeFrom !=DateTime.Today ?"hist":"last";////////////

            //string section = startTime != DateTime.Today ? "hist" : "last";



            //string candle = $"trade:30m:{nameSec}";

            string candle = $"trade:{tf}:{nameSec}";

            _apiPath = $"/v2/candles/{candle}/hist";//?start={startTime}&end={endTime}";

            //  var response = ExecuteRequest(_apiPath);

          //  string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            //Создаем строку для подписи
            //string signature = $"/api/{_apiPath}{nonce}";


            // Вычисляем подпись с использованием HMACSHA384
          //  string sig = ComputeHmacSha384(_secretKey, signature);

            // // Создаем клиента RestSharp
            var client = new RestClient(_baseUrl);

            //// Создаем запрос типа POST
            // var request = new RestRequest(_apiPath, Method.POST);
            var request = new RestRequest(_apiPath, Method.GET);
            //// Добавляем заголовки
            request.AddHeader("accept", "application/json");
            //request.AddHeader("bfx-nonce", nonce);
            //request.AddHeader("bfx-apikey", _publicKey);
            //request.AddHeader("bfx-signature", sig);



            // Отправляем запрос и получаем ответ
            var response = client.Execute(request);

            try
            {

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Выводим тело ответа
                    string jsonResponse = response.Content;

                    List<List<object>> candles = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);

                    if (candles == null || candles.Count == 0)
                    {
                        return null;
                    }

                    List<BitfinexCandle> candleList = new List<BitfinexCandle>();

                    for (int i = 0; i < candles.Count; i++) ////////////////////////
                    //for (int i = 0; i < 2; i++)
                    {
                        var candleData = candles[i];// Получаем данные по текущей свече.

                        // var candleData = (List<object>)candles[i];

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

                        // Создание объекта класса BitfinexCandle и присвоение значений полям

                        BitfinexCandle newCandle = new BitfinexCandle

                        {
                            Mts = candleData[0].ToString(),
                            Open = candleData[1].ToString(),
                            Close = candleData[2].ToString(),
                            High = candleData[3].ToString(),
                            Low = candleData[4].ToString(),
                            Volume = candleData[5].ToString()


                            //// Преобразование данных в нужные типы
                            //Mts = (candleData[0]),
                            //Open = Convert.ToDecimal(candleData[1]).ToString(),
                            //Close = Convert.ToDecimal(candleData[2]).ToString(),
                            //High = Convert.ToDecimal(candleData[3]).ToString(),
                            //Low = Convert.ToDecimal(candleData[4]).ToString(),
                            //Volume = Convert.ToDecimal(candleData[5]).ToString()

                        };


                        candleList.Add(newCandle); // Добавляем объект свечи в список.
                    }

                    return ConvertToCandles(candleList); // Преобразуем список BitfinexCandle в список Candle.
                }

            }
            catch
            {
                { SendLogMessage("Http request error", LogMessageType.Error); }/////////
            }
            // Возвращаем пустой список в случае ошибки.
            return null;
        }

        private List<Candle> ConvertToCandles(List<BitfinexCandle> candleList)
        {
            List<Candle> candles = new List<Candle>();// Список для хранения окончательных свечей.

            try
            {
                for (int i = 0; i < candleList.Count; i++)
                {
                    var candle = candleList[i];// Получаем текущую свечу из списка.

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

                        Candle newCandle = new Candle
                        {
                            State = CandleState.Finished,
                            TimeStart = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(candle.Mts)),
                            Open = (candle.Open).ToString().ToDecimal(),
                            Close = (candle.Close).ToString().ToDecimal(),
                            High = (candle.High).ToString().ToDecimal(),
                            Low = (candle.Low).ToString().ToDecimal(),
                            Volume = (candle.Volume).ToString().ToDecimal()


                        };

                        candles.Add(newCandle);
                    }
                    catch (FormatException exception)
                    {
                        SendLogMessage($"Format exception: {exception.Message}", LogMessageType.Error);
                    }
                }
                candles.Reverse();//////
                return candles;// Возвращаем окончательный список свечей.
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
        private Timer _pingTimer;
        // Set up a timer to send pings every 15 seconds

        private void CreateWebSocketConnection()
        {
            try
            {
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

        public event Action<MarketDepth> MarketDepthEvent;/////////где



        #endregion


        /// <summary>
        /// Обработка входящих сообщений от вёбсокета. И что важно в данном конкретном случае, Closed и Opened методы обязательно должны находиться здесь,
        /// </summary>
        #region  7 WebSocket events

        private bool _socketPublicIsActive;

        private bool _socketPrivateIsActive;

        private void WebSocketPublic_Opened(object sender, EventArgs e)
        {
            Thread.Sleep(3000);

            _socketPublicIsActive = true;//отвечает за соединение

            CheckActivationSockets();
            //  _pingTimer.Start();////////////////////////
            SendLogMessage("Websocket public Bitfinex Opened", LogMessageType.System);

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



        public long tradeChannelId;
        public long bookChannelId;

        // Снимок(snapshot) : Структура данных содержит массив массивов, где каждый внутренний массив представляет собой запись в стакане(book entry).
        //Обновление(update) : Структура данных содержит только один массив, представляющий одну запись в стакане(book entry).

        MarketDepth marketDepth = new MarketDepth();

        // Инициализируем списки для ask и bid уровней
        List<MarketDepthLevel> asks = new List<MarketDepthLevel>();
        List<MarketDepthLevel> bids = new List<MarketDepthLevel>();


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
                    var entryElement = data[i];

                    var price = entryElement[0].ToString().ToDecimal();
                    var count = entryElement[1].ToString().ToDecimal(); // количество
                    var amount = entryElement[2].ToString().ToDecimal(); // объём

                    if (amount > 0)
                    {
                        // Добавление уровня бидов
                        var bidLevel = new MarketDepthLevel
                        {
                            Price = price,
                            Bid = amount
                        };
                        bids.Add(bidLevel);
                    }
                    else
                    {
                        // Добавление уровня асков
                        var askLevel = new MarketDepthLevel
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
                var price = data[0].ToString().ToDecimal();//цена
                var count = data[1].ToString().ToDecimal(); // количество
                var amount = data[2].ToString().ToDecimal();                                 // var amount = decimal.Parse(data[2].GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture); // объём

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
                    var level = new MarketDepthLevel
                    {
                        Price = price,
                        Bid = amount > 0 ? amount : 0,
                        Ask = amount < 0 ? Math.Abs(amount) : 0
                    };

                    if (amount > 0)
                    {
                        // Обновление или добавление уровня в биды
                        var existingBid = marketDepth.Bids.FirstOrDefault(b => b.Price == price);
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
                        var existingAsk = marketDepth.Asks.FirstOrDefault(a => a.Price == price);
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
            CheckActivationSockets();
            SendLogMessage("Connection to private data is Open", LogMessageType.System);

        }


        private void GenerateAuthenticate()
        {
            //  string nonce = Convert.ToString((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);

            string nonce=GetNonce();

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


            Thread.Sleep(2000);
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

                WebSocketPrivateMessage.Enqueue(e.Message); // Добавление полученного сообщения в очередь

            }

            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }


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

        /// <summary>
        /// Подписка на бумагу.С обязательным контролем скорости и кол-ву запросов к методу Subscrible через rateGate.
        /// </summary>
        #region  8 Security subscrible ( или WebSocket security subscrible)

        private RateGate _rateGateSecurity = new RateGate(1, TimeSpan.FromMilliseconds(250));

        List<Security> _subscribledSecurities = new List<Security>();

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
        #region  9 WebSocket parsing the messages



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
                            // if (chanelId == bookChannelId)////посмотреть тут
                            {
                                UpdateDepth(message, bookChannelId, _currentSymbol);
                            }

                        }
                        if (root[1].ValueKind == JsonValueKind.String)
                        {
                            string messageType = root[1].ToString();

                            if (messageType == "te" || messageType == "tu" && chanelId != 0)
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
                    Thread.Sleep(2000);
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

                // Проверка, что это массив и есть минимум два элемента
                if (root.ValueKind == JsonValueKind.Array)
                {
                    //// Извлечение channel_id
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




        private void UpdateTrade(string jsonMessage)//[10098,\"tu\",[1657561837,1726071091967,-28.61178052,0.1531]]"
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
                BitfinexUpdateTrades tradeUpdate = new BitfinexUpdateTrades
                {
                    ChannelId = root[0].ToString(),
                    Type = root[1].ToString(),
                    Data = new TradeData
                    {
                        Id = root[2][0].ToString(),
                        Mts = root[2][1].ToString(),
                        Amount = root[2][2].ToString(),
                        Price = root[2][3].ToString()
                    }
                };
                //tradeList.Add(tradeUpdate);

                Trade newTrade = new Trade
                {
                    // Создание объекта для хранения информации о сделке
                    SecurityNameCode = _currentSymbol,                      // Название инструмента
                    Id = tradeUpdate.Data.Id.ToString(),               // Присваиваем TradeId из tradeUpdate
                    Price = tradeUpdate.Data.Price.ToDecimal(),                         // Присваиваем цену
                    Volume = Math.Abs(tradeUpdate.Data.Amount.ToDecimal()),             // Присваиваем объем (сделка может быть отрицательной для продаж)
                    Side = (tradeUpdate.Data.Amount).ToDecimal() > 0 ? Side.Buy : Side.Sell, // Определяем сторону сделки
                    Time = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(tradeUpdate.Data.Mts))
                };
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


                    var jsonDocument = JsonDocument.Parse(message);
                    var root = jsonDocument.RootElement;
                    var eventType = root.GetProperty("event").GetString();

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
                    var item = tradyList[i];

                    BitfinexMyTrade response = new BitfinexMyTrade { };


                    var myTrade = new MyTrade
                    {

                        Time = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(response.MtsCreate)),
                        SecurityNameCode = response.Symbol,
                        NumberOrderParent = response.Cid,//что тут должно быть
                        Price = (response.OrderPrice).ToDecimal(),
                        NumberTrade = response.OrderId,//что тут должно быт
                        Side = response.ExecAmount.Contains("-") ? Side.Sell : Side.Buy,
                        // Side = response.Amount > 0 ? Side.Buy : Side.Sell;
                        // Volume = (response.Amount).ToString().ToDecimal(),

                    };


                    // при покупке комиссия берется с монеты и объем уменьшается и появляются лишние знаки после запятой
                    decimal preVolume = myTrade.Side == Side.Sell ? response.ExecAmount.ToDecimal() : response.ExecAmount.ToDecimal() - response.Fee.ToDecimal();

                    myTrade.Volume = GetVolumeForMyTrade(response.Symbol, preVolume);


                    MyTradeEvent?.Invoke(myTrade);

                    SendLogMessage(myTrade.ToString(), LogMessageType.Trade);////надо или нет
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        //округление объемом
        private Dictionary<string, int> _decimalVolume = new Dictionary<string, int>();
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

                var rootArray = JsonConvert.DeserializeObject<List<object>>(message);

                // Извлекаем третий элемент, который является массивом
                var thirdElement = rootArray[2].ToString();

                // Десериализуем третий элемент как объект с типами, соответствующими структуре
                var response = JsonConvert.DeserializeObject<BitfinexResponseOrder>(thirdElement);

                // Теперь можно получить данные ордера
                var orderResponse = response.OrderData;

                // Десериализация сообщения в объект BitfinexOrderData
                // var response = JsonConvert.DeserializeObject<List<BitfinexOrderData>>(message);

                if (response == null)
                {
                    return;
                }

                // Перебор всех ордеров в ответе
                for (int i = 0; i < orderResponse.Count; i++)
                {
                    var orderData = orderResponse[i];

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
                    var updateOrder = new Order
                    {
                        SecurityNameCode = orderData.Symbol,
                        TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(orderData.MtsCreate)),
                        NumberUser = Convert.ToInt32(orderData.Cid),
                        NumberMarket = orderData.Id,
                        Side = orderData.Amount.Equals("-") ? Side.Sell : Side.Buy, // Продаем, если количество отрицательное
                        State = stateType,
                        TypeOrder = orderData.OrderType.Equals("EXCHANGE MARKET", StringComparison.OrdinalIgnoreCase) ? OrderPriceType.Market : OrderPriceType.Limit,
                        Volume = Math.Abs(orderData.Amount.ToDecimal()), // Абсолютное значение объема
                        Price = orderData.Price.ToDecimal(),
                        ServerType = ServerType.Bitfinex,
                        PortfolioNumber = "BitfinexPortfolio"
                    };

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
            OrderStateType stateType;

            switch (orderStateResponse)
            {
                case ("ACTIVE"):
                    stateType = OrderStateType.Activ;
                    break;
                case ("EXECUTED"):
                    stateType = OrderStateType.Done;
                    break;
                case ("REJECTED"):
                    stateType = OrderStateType.Fail;
                    break;
                case ("CANCELED"):
                    stateType = OrderStateType.Cancel;
                    break;
                case "PARTIALLY FILLED":
                    stateType = OrderStateType.Patrial;
                    break;
                default:
                    stateType = OrderStateType.None;
                    break;
            }

            return stateType;
        }


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



        #endregion


        /// <summary>
        /// посвящённый торговле. Выставление ордеров, отзыв и т.д
        /// </summary>
        #region  11 Trade

        private RateGate _rateGateSendOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));//уточнить задержку  Таймфрейм в формате отрезка времени. TimeSpan.

        private RateGate _rateGateCancelOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));

        public void SendOrder(Order order)
        {
            _rateGateSendOrder.WaitToProceed();


            _apiPath = "v2/auth/w/order/submit";


            string typeOrder; // Переменная для хранения типа ордера

            string orderSide = "";

            // Условие для определения типа ордера
            if (order.TypeOrder.ToString() == "Limit")
            {
                typeOrder = "EXCHANGE LIMIT";
            }
            else
            {
                typeOrder = "EXCHANGE MARKET";
            }


            BitfinexOrderData data = new BitfinexOrderData
            {
                Cid = order.NumberUser.ToString(),
                Symbol = order.SecurityNameCode,
                Amount = order.Volume.ToString().Replace(",", "."),
                OrderType = order.TypeOrder.ToString().ToUpper(),
                Price = order.TypeOrder == OrderPriceType.Market ? null : order.Price.ToString().Replace(",", "."),
                MtsCreate = order.TimeCreate.ToString(),
                Status = order.State.ToString(),
                MtsUpdate = order.TimeDone.ToString(),
                // Id= order.NumberMarket
            };

            decimal amount;

            if (decimal.TryParse(data.Amount, out amount))
            {
                // Изменение знака на отрицательный 

                if (order.Side.ToString() == "Sell")
                {// Преобразование обратно в строку
                    orderSide = (-amount).ToString();
                }

                else
                {
                    orderSide = (data.Amount); // Для остальных случаев используем положительное значение
                }
            }
            // Создание анонимного объекта с использованием переменной `type`
            var body = new
            {
                type = typeOrder,
                symbol = data.Symbol,
                price = data.Price,
                amount = orderSide
            };


            // Сериализуем объект тела в JSON

            string bodyJson = JsonSerializer.Serialize(body);
          
            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            //Создаем строку для подписи
            string signature = $"/api/{_apiPath}{nonce}{bodyJson}";


            // Вычисляем подпись с использованием HMACSHA384
            string sig = ComputeHmacSha384(_secretKey, signature);

            // // Создаем клиента RestSharp
            var client = new RestClient(_baseUrl);

            //// Создаем запрос типа POST
            var request = new RestRequest(_apiPath, Method.POST);

            //// Добавляем заголовки
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            // Добавляем тело запроса в формате JSON
            request.AddJsonBody(body); //


            // Отправляем запрос и получаем ответ
            var response = client.Execute(request);


            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                { // Выводим тело ответа
                    string responseBody = response.Content;

                    // Десериализация ответа как массив объектов

                    // string responseBody = "[1723557977,\"on-req\",null,null,[[167966185075,null,1723557977011,\"tTRXUSD\",1723557977011,1723557977011,22,22,\"EXCHANGE LIMIT\",null,null,null,0,\"ACTIVE\",null,null,0.12948,0,0,0,null,null,null,0,0,null,null,null,\"API>BFX\",null,null,{}]],null,\"SUCCESS\",\"Submitting 1 orders.\"]";

                    // Десериализация верхнего уровня в список объектов
                    var responseArray = JsonConvert.DeserializeObject<List<object>>(responseBody);

                    // Проверка размера массива перед извлечением значений
                    if (responseArray == null)
                    {
                        return;
                    }
                    //

                    // Парсим JSON-строку в JArray (массив JSON)
                    JArray jsonArray = JArray.Parse(responseBody);

                    //// Путь к статусу "ACTIVE" в JSON структуре
                    //string status = (string)jsonArray[4][0][13];

                    //if (responseArray.Contains("on-req"))
                    //{
                    //    // Извлечение нужных элементов
                    var dataJson = responseArray[4].ToString();
                    // string status = jsonArray[4][0][13].ToString();

                    //string status = responseArray[6].ToString();
                    string text = responseArray[7].ToString();

                    // Десериализация dataJson в список заказов
                    var ordersArray = JsonConvert.DeserializeObject<List<List<object>>>(dataJson);
                    var orders = ordersArray[0]; // Получаем первый заказ из массива

                    // Создание объекта BitfinexOrderData
                    BitfinexOrderData orderData = new BitfinexOrderData
                    {
                        //Cid = Convert.ToString(orders[2]),
                        Id = orders[0].ToString(),
                        Symbol = orders[3].ToString(),
                        Status = orders[13].ToString()
                    };
                    OrderStateType stateType = GetOrderState(orderData.Status);

                    order.NumberMarket = orderData.Id;

                    order.State = stateType; ///////////надо или нет


                    SendLogMessage($"Order num {order.NumberMarket} on exchange.{text}", LogMessageType.Trade);
                    //  SendLogMessage($"Order num {order.NumberMarket} on exchange.{order.State},{text}", LogMessageType.System);

                    // }
                }
                else
                {
                    //Content "[\"error\",10001,\"Invalid order: minimum size for TRXUSD is 22\"]"    

                    CreateOrderFail(order);
                    SendLogMessage($"Error Order exception {response.Content}", LogMessageType.Error);

                }

                MyOrderEvent?.Invoke(order);

            }
            catch (Exception exception)
            {
                // Обрабатываем исключения и выводим сообщение
                CreateOrderFail(order);
                SendLogMessage("Order send exception " + exception.ToString(), LogMessageType.Error);
            }

        }
        private void CreateOrderFail(Order order)
        {
            order.State = OrderStateType.Fail;

            MyOrderEvent?.Invoke(order);
        }

        public void CancelAllOrders()
        {
            rateGateCancelAllOrder.WaitToProceed();

            _apiPath = "v2/auth/w/order/cancel/multi";

            var body = new
            {
                all = 1  //1 отменить все ордера Идентификатор ордера для отмены
            };


            string bodyJson = JsonSerializer.Serialize(body);


            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            //Создаем строку для подписи
            string signature = $"/api/{_apiPath}{nonce}{bodyJson}";


            // Вычисляем подпись с использованием HMACSHA384
            string sig = ComputeHmacSha384(_secretKey, signature);

            // // Создаем клиента RestSharp
            var client = new RestClient(_baseUrl);

            //// Создаем запрос типа POST
            var request = new RestRequest(_apiPath, Method.POST);

            //// Добавляем заголовки
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            // Добавляем тело запроса в формате JSON
            request.AddJsonBody(body); //


            // Отправляем запрос и получаем ответ
            var response = client.Execute(request);



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
                    var responseJson = JsonConvert.DeserializeObject<List<object>>(responseBody);

                    if (responseJson.Contains("oc_multi-req"))
                    {

                        SendLogMessage($"All active orders canceled: {response.Content}", LogMessageType.Trade);

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

        }

        private RateGate rateGateCancelAllOrder = new RateGate(1, TimeSpan.FromMilliseconds(350));

        public void CancelOrder(Order order)
        {
            _rateGateCancelOrder.WaitToProceed();

            _apiPath = "v2/auth/w/order/cancel";

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

            // Сериализуем объект тела в JSON

            // string nonce = GetNonce();

            string bodyJson = JsonSerializer.Serialize(body);

            //var response = ExecuteRequest(_apiPath, nonce,bodyJson);
            // var response = ExecuteRequest(_apiPath, bodyJson);

            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            //Создаем строку для подписи
            string signature = $"/api/{_apiPath}{nonce}{bodyJson}";


            // Вычисляем подпись с использованием HMACSHA384
            string sig = ComputeHmacSha384(_secretKey, signature);

            // // Создаем клиента RestSharp
            var client = new RestClient(_baseUrl);

            //// Создаем запрос типа POST
            var request = new RestRequest(_apiPath, Method.POST);

            //// Добавляем заголовки
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            // Добавляем тело запроса в формате JSON
            request.AddJsonBody(body); //


            // Отправляем запрос и получаем ответ
            var response = client.Execute(request);

            try
            // [0,"n",[1575291219660,"oc-req",null,null,[1185815100,null,1575289350475,"tETHUSD",1575289351944,1575289447644,-3,-3,"LIMIT","LIMIT",null,null,0,"ACTIVE",null,null,240,0,0,0,null,null,null,0,0,null,null,null,"API>BFX",null,null,null],null,"SUCCESS","Submitted for cancellation; waiting for confirmation (ID: 1185815100)."]]

            //[0,"oc",[1185815100,null,1575289350475,"tETHUSD",1575289351944,1575291219663,-3,-3,"LIMIT","LIMIT",null,null,0,"CANCELED",null,null,240,0,0,0,null,null,null,0,0,null,null,null,"API>BFX",null,null,null]]
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Выводим тело ответа
                    string responseBody = response.Content;
                    var responseJson = JsonConvert.DeserializeObject<List<object>>(responseBody);


                    //if (responseJson.Contains("oc"))
                    //{
                    //if (responseJson.Contains("CANCELED"))
                    //{
                    SendLogMessage($"Order canceled Successfully. Order ID:{order.NumberMarket} {response.Content}", LogMessageType.Trade);
                    order.State = OrderStateType.Cancel;
                    MyOrderEvent(order);
                    // }
                    //} State = OrderStateType.Active если ордер активный
                    SendLogMessage($"Order canceled - {response.Content}, {response.ErrorMessage}", LogMessageType.Error);
                }

                else
                {

                    CreateOrderFail(order);
                    SendLogMessage($" Error Order cancellation:  {response.Content}, {response.ErrorMessage}", LogMessageType.Error);
                }

                // MyOrderEvent(order);////// надо или нет

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

        private RateGate rateGateChangePriceOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));


        public void ChangeOrderPrice(Order order, decimal Oldprice)
        {
            // ou - req", //TYPE
            // post https://api.bitfinex.com/v2/auth/w/order/update/////// НЕ ТО?

            rateGateChangePriceOrder.WaitToProceed();
            try
            {
                // Проверка типа ордера
                if (order.TypeOrder == OrderPriceType.Market)
                {
                    SendLogMessage("Can't change price for  Order Market", LogMessageType.Error);
                    return;
                }

                _apiPath = "v2/auth/w/order/update";

                var body = new
                {
                    id = order.NumberMarket,  // Идентификатор ордера
                    price = Oldprice.ToString(), // Новая цена
                                                 // amount = order.Volume.ToString()// новый объем
                };
                //var response = ExecuteRequest(_apiPath, nonce,bodyJson);
                //  string nonce = GetNonce();
                string bodyJson = JsonSerializer.Serialize(body);

                string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

                //Создаем строку для подписи
                string signature = $"/api/{_apiPath}{nonce}{bodyJson}";


                // Вычисляем подпись с использованием HMACSHA384
                string sig = ComputeHmacSha384(_secretKey, signature);

                // // Создаем клиента RestSharp
                var client = new RestClient(_baseUrl);

                //// Создаем запрос типа POST
                var request = new RestRequest(_apiPath, Method.POST);

                //// Добавляем заголовки
                request.AddHeader("accept", "application/json");
                request.AddHeader("bfx-nonce", nonce);
                request.AddHeader("bfx-apikey", _publicKey);
                request.AddHeader("bfx-signature", sig);

                // Добавляем тело запроса в формате JSON
                request.AddJsonBody(body); //


                // Отправляем запрос и получаем ответ
                var response = client.Execute(request);





                //  var response = ExecuteRequest(_apiPath, nonce,bodyJson);
                // var response = ExecuteRequest(_apiPath, bodyJson);

                // Количество оставшегося объема ордера
                int qty = Convert.ToInt32(order.Volume - order.VolumeExecute);

                // Проверка, что ордер активен и есть неисполненный объем
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
                    //order.Price = newPrice;


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

        /// <summary>
        /// Место расположение HTTP запросов.
        /// </summary>_httpPublicClient
        #region  12 Queries



        public void CancelAllOrdersToSecurity(Security security)
        {
            throw new NotImplementedException();
        }


        public List<Order> GetAllOrdersFromExchange()
        {
            // post https://api.bitfinex.com/v2/auth/r/orders

            List<Order> orders = new List<Order>();


            // Логика получения данных и заполнения orderFromExchange

            // string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            //  string nonce = GetNonce();
            _apiPath = "v2/auth/r/orders";
            // var response = ExecuteRequest(_apiPath, nonce);


            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            //Создаем строку для подписи
            string signature = $"/api/{_apiPath}{nonce}";


            // Вычисляем подпись с использованием HMACSHA384
            string sig = ComputeHmacSha384(_secretKey, signature);

            // // Создаем клиента RestSharp
            var client = new RestClient(_baseUrl);

            //// Создаем запрос типа POST
            var request = new RestRequest(_apiPath, Method.POST);

            //// Добавляем заголовки
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);




            // Отправляем запрос и получаем ответ
            var response = client.Execute(request);

            try
            {

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Выводим тело ответа
                    string responseBody = response.Content;
                    // Десериализация полученных данных
                    //var listOrders = JsonConvert.DeserializeObject<List<List<BitfinexOrderData>>>(response.Content);
                    var listOrders = JsonConvert.DeserializeObject<List<List<object>>>(response.Content);
                    //  доступ к первому элементу


                    var activeOrders = new List<BitfinexOrderData>();

                    if (orders != null && orders.Count > 0)
                    {
                        for (int i = 0; i < orders.Count; i++)
                        {
                            Order newOrder = new Order();

                            newOrder.TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(long.Parse(activeOrders[i].MtsUpdate));
                            newOrder.TimeCreate = TimeManager.GetDateTimeFromTimeStamp(long.Parse(activeOrders[i].MtsCreate));
                            newOrder.ServerType = ServerType.Bitfinex;
                            newOrder.SecurityNameCode = activeOrders[i].Symbol;

                            newOrder.NumberUser = Convert.ToInt32(activeOrders[i].Cid);

                            newOrder.NumberMarket = activeOrders[i].Id;
                            newOrder.Side = activeOrders[i].Amount.Equals("-") ? Side.Sell : Side.Buy;
                            newOrder.State = GetOrderState(activeOrders[i].Status);
                            newOrder.Volume = activeOrders[i].Amount.ToDecimal();
                            newOrder.Price = activeOrders[i].Price.ToDecimal();
                            newOrder.PortfolioNumber = "BitfinexPortfolio";

                            orders.Add(newOrder);

                            //orders[i].TimeCreate = orders[i].TimeCallBack;

                            MyOrderEvent?.Invoke(orders[i]);


                        }

                    }

                }
                else
                {

                    SendLogMessage($" Can't get all orders. Http State Code: {response.Content}", LogMessageType.Error);
                }

            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

            return orders;  // Возвращаем найденный ордер

        }

        //public void GetOrderStatus(Order order)
        //{
        //    throw new NotImplementedException();
        //}
        public void GetOrderStatus(Order order)
        {
            // Получаем ордер с биржи по рыночному номеру ордера
            Order orderFromExchange = GetOrderFromExchange(order.NumberMarket);

            // Если ордер не найден, выходим из метода
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
                    if (MyTradeEvent != null)
                    {
                        MyTradeEvent(tradesByMyOrder[i]);
                    }
                }
            }
        }

        private Order GetOrderFromExchange(string numberMarket)
        {
            Order newOrder = new Order();

            _apiPath = "v2/auth/r/orders";////////////
            var body = new
            {
                id = numberMarket

            };
            // string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            //  string nonce = GetNonce();
            // Сериализуем объект тела в JSON

            // string bodyJson = JsonConvert.SerializeObject(body);
            string bodyJson = JsonSerializer.Serialize(body);


            // Отправляем запрос на сервер Bitfinex и получаем ответ
            //  var response = ExecuteRequest(_apiPath,nonce,bodyJson);
            // var response = ExecuteRequest(_apiPath,  bodyJson);

            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            //Создаем строку для подписи
            string signature = $"/api/{_apiPath}{nonce}{bodyJson}";


            // Вычисляем подпись с использованием HMACSHA384
            string sig = ComputeHmacSha384(_secretKey, signature);

            // // Создаем клиента RestSharp
            var client = new RestClient(_baseUrl);

            //// Создаем запрос типа POST
            var request = new RestRequest(_apiPath, Method.POST);

            //// Добавляем заголовки
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            // Добавляем тело запроса в формате JSON
            request.AddJsonBody(body); //


            // Отправляем запрос и получаем ответ
            var response = client.Execute(request);

            // Выводим тело ответа
            string responseBody = response.Content;

            // Десериализуем ответ
            var response1 = JsonConvert.DeserializeObject<List<List<object>>>(responseBody);
            // Если ответ успешный, обновляем портфель и запрашиваем позиции
            List<BitfinexOrderData> listOrder = new List<BitfinexOrderData>();
            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    for (int i = 0; i < listOrder.Count; i++)
                    {
                        var order = listOrder[i];


                        // Заполняем данные ордера
                        newOrder.TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(order.MtsUpdate));
                        newOrder.TimeCreate = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(order.MtsCreate));
                        newOrder.ServerType = ServerType.Bitfinex;
                        newOrder.SecurityNameCode = order.Symbol;
                        newOrder.NumberMarket = numberMarket;
                        newOrder.Side = (order.Amount).Equals("-") ? Side.Sell : Side.Buy;////// или 
                        newOrder.State = OrderStateType.Activ; // Статус ордера (например, активный)
                        newOrder.Volume = (order.Amount).ToDecimal();
                        newOrder.Price = (order.Price).ToDecimal();
                        newOrder.PortfolioNumber = "BitfinexPortfolio";
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

                _apiPath = $"v2/auth/r/trades/{security}";////////////
                var body = new
                {
                    id = orderId,
                    symbol = security
                };

                //  string nonce = GetNonce();
                //  string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()* 1000).ToString();

                // Сериализуем объект тела в JSON

                // string bodyJson = JsonConvert.SerializeObject(body);

                string bodyJson = JsonSerializer.Serialize(body);

                string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

                //Создаем строку для подписи
                string signature = $"/api/{_apiPath}{nonce}{bodyJson}";


                // Вычисляем подпись с использованием HMACSHA384
                string sig = ComputeHmacSha384(_secretKey, signature);

                // // Создаем клиента RestSharp
                var client = new RestClient(_baseUrl);

                //// Создаем запрос типа POST
                var request = new RestRequest(_apiPath, Method.POST);

                //// Добавляем заголовки
                request.AddHeader("accept", "application/json");
                request.AddHeader("bfx-nonce", nonce);
                request.AddHeader("bfx-apikey", _publicKey);
                request.AddHeader("bfx-signature", sig);

                // Добавляем тело запроса в формате JSON
                request.AddJsonBody(body); //


                // Отправляем запрос и получаем ответ
                var response = client.Execute(request);


                // Отправляем запрос на сервер Bitfinex и получаем ответ
                // var response = ExecuteRequest(_apiPath,nonce,bodyJson);

                // Выводим тело ответа
                string responseBody = response.Content;

                // Если ответ успешный, обновляем портфель и запрашиваем позиции
                if (response.StatusCode == HttpStatusCode.OK)
                {

                    CreateQueryPosition(); // Обновляем позиции
                }
                else
                {
                    // Логируем ошибку, если запрос не удался
                    SendLogMessage($" {responseBody}", LogMessageType.Error);
                }
                return new List<MyTrade>();   // Возвращаем пустой список (пример
            }
            catch (Exception exception)
            {
                // Логируем исключение в случае ошибки
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }


            return null;
        }

        public List<Candle> GetCandleDataToSecurity(string security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            throw new NotImplementedException();
        }



        // Метод для выполнения запросов GET или POST в зависимости от параметра
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
        //        $"/api/{apiPath}{nonce}" :
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

        #endregion
        /// <summary>
        /// Логирование.
        /// </summary>
        #region 13 Log

        public event Action<string, LogMessageType> LogMessageEvent;


        private void SendLogMessage(string message, LogMessageType messageType)
        {
            LogMessageEvent(message, messageType);
        }



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

                if (MyOrderEvent != null)
                {
                    MyOrderEvent(orders[i]);
                }
            }
        }




        // Начальное значение nonce на основе текущего времени
        private long _lastNonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()*100 ;

        private readonly object _lock = new object();




        public string GetNonce()
        {

            lock (_lock)
            {

                var currentNonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()*10000 ;//если ставить 1000 не работает

                // Если новое значение меньше или равно предыдущему, увеличиваем его
                if (currentNonce <= _lastNonce)
                {
                    currentNonce = _lastNonce + 1;
                }

                // Сохраняем новое значение
                _lastNonce = currentNonce;

                // Возвращаем значение в виде строки
                return currentNonce.ToString();

            }

        }


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


        #endregion


    }
}


#endregion


