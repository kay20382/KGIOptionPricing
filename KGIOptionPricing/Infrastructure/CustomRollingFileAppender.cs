using System;
using System.IO;
using System.Text;
using System.Linq;
using log4net.Appender;
using log4net.Core;

namespace KGIOptionPricing
{
    /// <summary>
    /// 自訂的 log4net Appender，完全符合 V{日期} {流水號}.TXT (例如 V20260818 001.TXT) 的需求。
    /// </summary>
    public class CustomRollingFileAppender : AppenderSkeleton
    {
        public string DatePattern { get; set; } = "yyyyMMdd";
        public string BaseDirectory { get; set; } = "Logs";
        
        private string _maxFileSizeStr = "10MB";
        public string MaximumFileSize 
        { 
            get => _maxFileSizeStr; 
            set { _maxFileSizeStr = value; _maxFileSize = ParseSize(value); } 
        }

        private StreamWriter? _writer;
        private DateTime _currentDate;
        private int _currentSequence = 1;
        private long _maxFileSize = 10 * 1024 * 1024; // 10MB
        private readonly object _lockObj = new object();

        protected override void Append(LoggingEvent loggingEvent)
        {
            lock (_lockObj)
            {
                DateTime now = DateTime.Now;
                bool needsNewFile = false;

                if (now.Date != _currentDate.Date || _writer == null)
                {
                    _currentDate = now.Date;
                    _currentSequence = 0; // Will be determined in OpenFile
                    needsNewFile = true;
                }
                else if (_writer.BaseStream.Length > _maxFileSize)
                {
                    _currentSequence++;
                    needsNewFile = true;
                }

                if (needsNewFile)
                {
                    OpenFile();
                }

                if (_writer != null)
                {
                    string message = RenderLoggingEvent(loggingEvent);
                    _writer.Write(message);
                    _writer.Flush();
                }
            }
        }

        private void OpenFile()
        {
            if (_writer != null) 
            { 
                _writer.Close(); 
                _writer = null; 
            }

            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BaseDirectory, _currentDate.ToString(DatePattern));
            if (!Directory.Exists(dir)) 
            {
                Directory.CreateDirectory(dir);
            }

            if (_currentSequence == 0)
            {
                var files = Directory.GetFiles(dir, $"V{_currentDate.ToString(DatePattern)} *.TXT");
                int maxSeq = 1;
                foreach (var f in files)
                {
                    string name = Path.GetFileNameWithoutExtension(f); // V20260818 001
                    string[] parts = name.Split(' ');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int seq))
                    {
                        if (seq >= maxSeq) maxSeq = seq;
                    }
                }
                _currentSequence = maxSeq;

                // Check if the max sequence file is already too large
                string latestFilePath = Path.Combine(dir, $"V{_currentDate.ToString(DatePattern)} {_currentSequence:D3}.TXT");
                if (File.Exists(latestFilePath))
                {
                    FileInfo fi = new FileInfo(latestFilePath);
                    if (fi.Length >= _maxFileSize)
                    {
                        _currentSequence++;
                    }
                }
            }

            string filePath = Path.Combine(dir, $"V{_currentDate.ToString(DatePattern)} {_currentSequence:D3}.TXT");
            _writer = new StreamWriter(filePath, true, Encoding.UTF8);
        }

        protected override void OnClose()
        {
            lock (_lockObj)
            {
                if (_writer != null) 
                { 
                    _writer.Close(); 
                    _writer = null; 
                }
                base.OnClose();
            }
        }

        private long ParseSize(string sizeStr)
        {
            sizeStr = sizeStr.ToUpper().Trim();
            long multiplier = 1;
            if (sizeStr.EndsWith("KB")) { multiplier = 1024; sizeStr = sizeStr.Substring(0, sizeStr.Length - 2); }
            else if (sizeStr.EndsWith("MB")) { multiplier = 1024 * 1024; sizeStr = sizeStr.Substring(0, sizeStr.Length - 2); }
            else if (sizeStr.EndsWith("GB")) { multiplier = 1024 * 1024 * 1024; sizeStr = sizeStr.Substring(0, sizeStr.Length - 2); }

            if (long.TryParse(sizeStr, out long val))
            {
                return val * multiplier;
            }
            return 10 * 1024 * 1024; // Default 10MB
        }
    }
}
