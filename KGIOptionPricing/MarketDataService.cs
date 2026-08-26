using System;
using System.Collections.Generic;
using System.Timers;

namespace KGIOptionPricing
{
    /// <summary>
    /// 市場資料服務：定時驅動報價更新、計算 Greeks 與 IV 曲線平滑。
    /// </summary>
    public class MarketDataService
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(MarketDataService));

        private System.Timers.Timer _timer;
        private KGIQuoteService _quoteService;
        private IMPVStorageManager _storageManager;
        
        public KGIQuoteService QuoteService => _quoteService;
        public Dictionary<string, OptionChain> OptionChains => _quoteService.OptionChains;
        public double CurrentFuturesPrice { get; set; } = 45000; // Default fallback price, updated from KGI Quote
        public DateTime CurrentDate => DateTime.Now;

        public event Action<Dictionary<string, OptionChain>>? OnOptionsUpdated;

        public MarketDataService(KGIQuoteService quoteService, IMPVStorageManager storageManager)
        {
            _quoteService = quoteService;
            _storageManager = storageManager;

            // Timer runs every 15 seconds
            _timer = new System.Timers.Timer(15000);
            _timer.Elapsed += OnTimerElapsed;
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _timer.Dispose();
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                // Fetch the latest Bid/Ask, calculate IMPV, update Greeks, and smooth curves
                _quoteService.FetchLatestQuotesIntoChains(CurrentFuturesPrice, CurrentDate);

                // Note: Option Chains are created or retrieved from KGIQuoteService.
                // KGIQuoteService.OnOptionsDataUpdated fires, or we can invoke here.
            }
            catch (Exception ex)
            {
                log.Error($"[MarketDataService Error] {ex.Message}", ex);
            }
        }
    }
}
