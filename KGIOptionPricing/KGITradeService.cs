using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Intelligence;
using Package;
using Smart;

namespace KGIOptionPricing
{
    public class TradePositionItem
    {
        public string BrokerId { get; set; } = string.Empty;
        public string Account { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Trader { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string ComType { get; set; } = string.Empty; // "F" or "O"
        public string Commodity { get; set; } = string.Empty; // e.g. TXO, TX4, TXY
        public string SettleMonth { get; set; } = string.Empty; // e.g. 202606
        public double StrikePrice { get; set; }
        public string CallPut { get; set; } = string.Empty; // "C" or "P"
        public string BuySell { get; set; } = string.Empty; // "B" or "S"
        public int OpenInterest { get; set; }
        public double AveragePrice { get; set; }
        public double MarketPrice { get; set; }
    }

    public class KGITradeService
    {
        private TaiFexCom? _tfcom;
        private readonly string _host;
        private readonly ushort _port;
        private readonly string _userId;
        private readonly string _password;
        private readonly string _account;
        private readonly string _borkerID;

        public event Action<string>? OnLogMessage;
        public event Action<string, bool>? OnStatusChanged; // (StatusText, IsConnected)
        public event Action<List<TradePositionItem>>? OnPositionsUpdated;

        public List<TradePositionItem> CurrentPositions { get; private set; } = new List<TradePositionItem>();
        public bool IsLoggedIn { get; private set; } = false;

        public KGITradeService(string host, ushort port, string userId, string password,string brokerID,string account)
        {
            _host = host;
            _port = port;
            _userId = userId;
            _password = password;
            _borkerID = brokerID;
            _account = account;
        }

        public void Start()
        {
            try
            {
                OnLogMessage?.Invoke($"[TradeCom] 正在初始化連線至 {_host}:{_port}...");
                _tfcom = new TaiFexCom(_host, _port, "API", Language.Chinese);
                _tfcom.OnRcvMessage += OnRcvMessage;
                _tfcom.OnGetStatus += OnGetStatus;

                _tfcom.AutoSubReport = true;
                _tfcom.AutoRecoverReport = true;

                OnLogMessage?.Invoke($"[TradeCom] 發送 LoginDirect 帳號: {_userId}");
                _tfcom.LoginDirect(_host, _port, $"{_userId},,{_password}");
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"[TradeCom] 初始化失敗: {ex.Message}");
                OnStatusChanged?.Invoke("連線失敗", false);
            }
        }

        public void Stop()
        {
            try
            {
                if (_tfcom != null)
                {
                    try { _tfcom.Logout(); } catch { }
                    try { _tfcom.Dispose(); } catch { }
                    try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_tfcom); } catch { }
                    _tfcom = null;
                }
                IsLoggedIn = false;
                OnStatusChanged?.Invoke("已離線", false);
            }
            catch { }
        }

        public void RequestPositions()
        {
            if (_tfcom != null && IsLoggedIn)
            {
                OnLogMessage?.Invoke("[TradeCom] 請求部位查詢 RetrivePositionSum...");
                long rtn = _tfcom.RetrivePositionSum("I", _borkerID, _account, "", "");
                if(rtn < 0)
                {
                    Console.WriteLine(  rtn);
                }
                //_tfcom.RetrivePositionSum("I", "", "", "", "");
            }
        }

        private void OnGetStatus(object sender, COM_STATUS status, byte[] msg)
        {
            string msgText = Encoding.UTF8.GetString(msg);
            switch (status)
            {
                case COM_STATUS.LOGIN_READY:
                    IsLoggedIn = true;
                    OnLogMessage?.Invoke("[TradeCom] 登入成功！");
                    OnStatusChanged?.Invoke("已登入", true);
                    RequestPositions();
                    break;

                case COM_STATUS.LOGIN_FAIL:
                    IsLoggedIn = false;
                    OnLogMessage?.Invoke($"[TradeCom] 登入失敗: {msgText}");
                    OnStatusChanged?.Invoke("登入失敗", false);
                    break;

                case COM_STATUS.CONNECT_READY:
                    OnLogMessage?.Invoke($"[TradeCom] 伺服器連線成功: {msgText}");
                    OnStatusChanged?.Invoke("連線中", false);
                    break;

                case COM_STATUS.CONNECT_FAIL:
                case COM_STATUS.DISCONNECTED:
                    IsLoggedIn = false;
                    OnLogMessage?.Invoke($"[TradeCom] 連線中斷: {msgText}");
                    OnStatusChanged?.Invoke("離線", false);
                    break;
            }
        }

        private void OnRcvMessage(object sender, PackageBase package)
        {
            if (package == null || _tfcom == null) return;

            switch ((DT)package.DT)
            {
                case DT.LOGIN:
                    P001503 p1503 = (P001503)package;
                    if (p1503.Code != 0)
                        OnLogMessage?.Invoke("登入失敗 CODE = " + p1503.Code + " " + _tfcom.GetMessageMap(p1503.Code));
                    else
                    {
                        OnLogMessage?.Invoke("登入成功 ");
                        OnLogMessage?.Invoke(p1503.ToLog());
                    }

                    break;
            }
              
     
            if ((DT)package.DT == DT.INVENTORY_TRADER) // 1616
            {
                P001616 p1616 = (P001616)package;
                OnLogMessage?.Invoke($"[TradeCom] 收到 1616 庫存回報，筆數: {p1616.Rows}");

                var newPositions = new List<TradePositionItem>();
                if (p1616.Rows > 0 && p1616.p001616_2 != null)
                {
                    foreach (P001616_2 p in p1616.p001616_2)
                    {
                        int.TryParse(p.OTQty, out int qty);
                        double.TryParse(p.StrikePrice, out double strike);
                        double.TryParse(p.TrdPrice, out double trdPrice);
                        double.TryParse(p.MPrice, out double mPrice);

                        newPositions.Add(new TradePositionItem
                        {
                            BrokerId = p.BrokerId ?? "",
                            Account = p.Account ?? "",
                            Group = p.Group ?? "",
                            Trader = p.Trader ?? "",
                            Exchange = p.Exchange ?? "",
                            ComType = p.ComType ?? "",
                            Commodity = p.ComID ?? "",
                            SettleMonth = p.ComYM ?? "",
                            StrikePrice = strike,
                            CallPut = p.CP ?? "",
                            BuySell = p.BS ?? "",
                            OpenInterest = qty,
                            AveragePrice = trdPrice,
                            MarketPrice = mPrice
                        });
                    }
                }

                CurrentPositions = newPositions;
                OnPositionsUpdated?.Invoke(CurrentPositions);
            }
        }
    }
}
