using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using log4net;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.OrderManagement.Storage
{
    public class OrderStorageManager
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(OrderStorageManager));
        private readonly string _storageDirectory;
        private readonly object _fileLock = new object();
        private readonly JsonSerializerOptions _jsonOptions;

        public OrderStorageManager(string storageDirectory = "Orders_Data")
        {
            _storageDirectory = storageDirectory;
            if (!Directory.Exists(_storageDirectory))
            {
                Directory.CreateDirectory(_storageDirectory);
            }

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        private string GetFilePath(string? dateStr = null)
        {
            string date = string.IsNullOrEmpty(dateStr) ? DateTime.Today.ToString("yyyyMMdd") : dateStr;
            return Path.Combine(_storageDirectory, $"Orders_{date}.json");
        }

        /// <summary>
        /// 讀取指定日期 (預設為今日) 的所有委託與回補歷史紀錄。
        /// </summary>
        public List<OrderInfo> LoadOrders(string? dateStr = null)
        {
            lock (_fileLock)
            {
                string filePath = GetFilePath(dateStr);
                if (!File.Exists(filePath))
                {
                    return new List<OrderInfo>();
                }

                try
                {
                    string json = File.ReadAllText(filePath);
                    var orders = JsonSerializer.Deserialize<List<OrderInfo>>(json, _jsonOptions);
                    log.Info($"[OrderStorage] 成功自 {filePath} 載入 {orders?.Count ?? 0} 筆委託單紀錄");
                    return orders ?? new List<OrderInfo>();
                }
                catch (Exception ex)
                {
                    log.Error($"[OrderStorage] 載入委託單失敗 ({filePath}): {ex.Message}", ex);
                    return new List<OrderInfo>();
                }
            }
        }

        /// <summary>
        /// 批量儲存委託單 (全量寫入)。
        /// </summary>
        public void SaveOrders(IEnumerable<OrderInfo> orders, string? dateStr = null)
        {
            lock (_fileLock)
            {
                string filePath = GetFilePath(dateStr);
                try
                {
                    var list = orders.ToList();
                    string json = JsonSerializer.Serialize(list, _jsonOptions);
                    File.WriteAllText(filePath, json);
                }
                catch (Exception ex)
                {
                    log.Error($"[OrderStorage] 儲存委託單失敗 ({filePath}): {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// 增量更新或儲存單筆委託單。
        /// </summary>
        public void SaveOrder(OrderInfo order, string? dateStr = null)
        {
            if (order == null) return;

            lock (_fileLock)
            {
                string filePath = GetFilePath(dateStr);
                try
                {
                    List<OrderInfo> orders;
                    if (File.Exists(filePath))
                    {
                        string existingJson = File.ReadAllText(filePath);
                        orders = JsonSerializer.Deserialize<List<OrderInfo>>(existingJson, _jsonOptions) ?? new List<OrderInfo>();
                    }
                    else
                    {
                        orders = new List<OrderInfo>();
                    }

                    int idx = orders.FindIndex(o => o.OrderId == order.OrderId || 
                        (!string.IsNullOrEmpty(o.Identity?.Cnt) && o.Identity?.Cnt == order.Identity?.Cnt) ||
                        (!string.IsNullOrEmpty(o.Identity?.OrdNo) && o.Identity?.OrdNo == order.Identity?.OrdNo));

                    if (idx >= 0)
                    {
                        orders[idx] = order;
                    }
                    else
                    {
                        orders.Add(order);
                    }

                    string json = JsonSerializer.Serialize(orders, _jsonOptions);
                    File.WriteAllText(filePath, json);
                }
                catch (Exception ex)
                {
                    log.Error($"[OrderStorage] 儲存單筆委託單失敗 ({order.OrderId}): {ex.Message}", ex);
                }
            }
        }
    }
}
