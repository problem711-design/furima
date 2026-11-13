using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;

public class FeePresetConfig
{
    public string id { get; set; }
    public string name { get; set; }
    public int feeRatePercent { get; set; }
    public int shippingCost { get; set; }
    public int expenseCost { get; set; }
}

public class DefaultSettingsConfig
{
    public int defaultFeeRatePercent { get; set; }
    public int defaultShippingCost { get; set; }
    public int defaultExpenseCost { get; set; }
    public string activePresetId { get; set; }
    public List<FeePresetConfig> presets { get; set; }

    public static DefaultSettingsConfig CreateDefault()
    {
        return new DefaultSettingsConfig
        {
            defaultFeeRatePercent = 5,
            defaultShippingCost = 210,
            defaultExpenseCost = 55,
            activePresetId = "yahoo_furima",
            presets = new List<FeePresetConfig>
            {
                new FeePresetConfig { id = "yahoo_furima", name = "Yahoo!フリマ (基本)", feeRatePercent = 5, shippingCost = 210, expenseCost = 55 },
                new FeePresetConfig { id = "mercari", name = "メルカリ (ネコポス/ゆうパケット)", feeRatePercent = 10, shippingCost = 210, expenseCost = 55 },
                new FeePresetConfig { id = "rakuma", name = "楽天ラクマ (かんたんラクマパック)", feeRatePercent = 10, shippingCost = 180, expenseCost = 55 },
                new FeePresetConfig { id = "yupaket_mini", name = "ゆうパケットポストmini", feeRatePercent = 5, shippingCost = 160, expenseCost = 20 }
            }
        };
    }
}

public class StoreConfig
{
    public string storeId { get; set; }
    public string path { get; set; }
    public string authToken { get; set; }
    public string createdAt { get; set; }
    public DefaultSettingsConfig defaultSettings { get; set; }
}

public static class SyncStore
{
    private static readonly object _configLock = new object();
    private static string _storeJsonPath = "";
    private static StoreConfig _currentConfig = null;
    private static string _storeId = "";
    private static string _syncDir = "";
    private static string _authToken = "";

    public static string StoreId { get { return _storeId; } }
    public static string SyncDir { get { return _syncDir; } }
    public static string AuthToken { get { return _authToken; } }

    public static DefaultSettingsConfig GetDefaultSettings()
    {
        lock (_configLock)
        {
            if (_currentConfig != null && _currentConfig.defaultSettings != null)
            {
                return _currentConfig.defaultSettings;
            }
            return DefaultSettingsConfig.CreateDefault();
        }
    }

    public static void SaveDefaultSettings(DefaultSettingsConfig settings)
    {
        if (settings == null) return;
        lock (_configLock)
        {
            if (_currentConfig == null)
            {
                _currentConfig = new StoreConfig
                {
                    storeId = _storeId,
                    path = _syncDir,
                    authToken = _authToken,
                    createdAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK")
                };
            }
            if (settings.presets == null || settings.presets.Count == 0)
            {
                settings.presets = DefaultSettingsConfig.CreateDefault().presets;
            }
            _currentConfig.defaultSettings = settings;
            if (!string.IsNullOrEmpty(_storeJsonPath))
            {
                try
                {
                    File.WriteAllText(_storeJsonPath, JsonConvert.SerializeObject(_currentConfig, Formatting.Indented), Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[警告] store.json の設定保存に失敗しました: " + ex.Message);
                }
            }
        }
    }

    public static void Initialize(string baseDir, ref string serverAuthToken)
    {
        string storeJsonPath = Path.Combine(baseDir, "store.json");
        _storeJsonPath = storeJsonPath;

        if (File.Exists(storeJsonPath))
        {
            try
            {
                string json = File.ReadAllText(storeJsonPath, Encoding.UTF8);
                StoreConfig cfg = JsonConvert.DeserializeObject<StoreConfig>(json);
                if (cfg != null && !string.IsNullOrEmpty(cfg.storeId) && !string.IsNullOrEmpty(cfg.path))
                {
                    _storeId = cfg.storeId;
                    _syncDir = cfg.path;
                    bool dirty = false;
                    if (string.IsNullOrEmpty(cfg.authToken))
                    {
                        _authToken = !string.IsNullOrEmpty(serverAuthToken) ? serverAuthToken : Guid.NewGuid().ToString("N").Substring(0, 12);
                        cfg.authToken = _authToken;
                        dirty = true;
                    }
                    else
                    {
                        _authToken = cfg.authToken;
                    }
                    if (cfg.defaultSettings == null)
                    {
                        cfg.defaultSettings = DefaultSettingsConfig.CreateDefault();
                        dirty = true;
                    }
                    _currentConfig = cfg;
                    if (dirty)
                    {
                        try
                        {
                            File.WriteAllText(storeJsonPath, JsonConvert.SerializeObject(cfg, Formatting.Indented), Encoding.UTF8);
                        }
                        catch {}
                    }
                    if (!Directory.Exists(_syncDir))
                    {
                        Directory.CreateDirectory(_syncDir);
                    }
                    serverAuthToken = _authToken;
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[警告] store.json の読み込みに失敗しました: " + ex.Message);
            }
        }

        // store.json が無いか無効な場合、新規作成
        _storeId = Guid.NewGuid().ToString();
        _syncDir = Path.Combine(baseDir, "出品写真_PC保存用");
        _authToken = !string.IsNullOrEmpty(serverAuthToken) ? serverAuthToken : Guid.NewGuid().ToString("N").Substring(0, 12);
        Directory.CreateDirectory(_syncDir);

        StoreConfig newCfg = new StoreConfig
        {
            storeId = _storeId,
            path = _syncDir,
            authToken = _authToken,
            createdAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK"),
            defaultSettings = DefaultSettingsConfig.CreateDefault()
        };
        _currentConfig = newCfg;

        serverAuthToken = _authToken;

        try
        {
            string newJson = JsonConvert.SerializeObject(newCfg, Formatting.Indented);
            File.WriteAllText(storeJsonPath, newJson, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[エラー] store.json の作成に失敗しました: " + ex.Message);
        }
    }

    public static void BackupItemsJson(string itemsJsonPath)
    {
        try
        {
            if (File.Exists(itemsJsonPath))
            {
                string dir = Path.GetDirectoryName(itemsJsonPath);
                string prevPath = Path.Combine(dir, "items_data.prev.json");
                File.Copy(itemsJsonPath, prevPath, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[警告] items_data.prev.json バックアップ作成失敗: " + ex.Message);
        }
    }

    public static int CountMissingFiles(List<Dictionary<string, object>> itemsDb, Dictionary<string, string> hashToFilePathMap)
    {
        HashSet<string> missingHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (itemsDb == null) return 0;

        foreach (var item in itemsDb)
        {
            if (item != null && item.ContainsKey("photos") && item["photos"] != null)
            {
                var photosList = item["photos"] as System.Collections.IEnumerable;
                if (photosList != null)
                {
                    foreach (var photoObj in photosList)
                    {
                        var photo = photoObj as Dictionary<string, object>;
                        if (photo != null && photo.ContainsKey("md5Hash") && photo["md5Hash"] != null)
                        {
                            string hash = photo["md5Hash"].ToString().Trim();
                            if (!string.IsNullOrEmpty(hash))
                            {
                                string filePath;
                                bool exists = hashToFilePathMap.TryGetValue(hash, out filePath) && !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
                                if (!exists)
                                {
                                    missingHashes.Add(hash);
                                }
                            }
                        }
                    }
                }
            }
        }
        return missingHashes.Count;
    }

    public static long GetDirectorySizeBytes(string dirPath)
    {
        if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath)) return 0;
        long total = 0;
        try
        {
            var files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                try
                {
                    FileInfo fi = new FileInfo(f);
                    total += fi.Length;
                }
                catch { }
            }
        }
        catch { }
        return total;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
        if (bytes < 1024 * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("F1") + " MB";
        return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F1") + " GB";
    }

    public class CleanupCandidate
    {
        public int itemId { get; set; }
        public string title { get; set; }
        public string status { get; set; }
        public string soldAtText { get; set; }
        public string photoHash { get; set; }
        public string fileName { get; set; }
        public string filePath { get; set; }
        public long sizeBytes { get; set; }
        public string sizeFormatted { get; set; }
    }

    public class CleanupPreviewResult
    {
        public long totalStorageBytes { get; set; }
        public string totalStorageFormatted { get; set; }
        public int candidateCount { get; set; }
        public long reclaimableBytes { get; set; }
        public string reclaimableFormatted { get; set; }
        public List<CleanupCandidate> candidates { get; set; }

        public CleanupPreviewResult()
        {
            candidates = new List<CleanupCandidate>();
        }
    }

    public static CleanupPreviewResult GetCleanupCandidates(List<Dictionary<string, object>> itemsDb, Dictionary<string, string> hashToFilePathMap, string syncDir)
    {
        var result = new CleanupPreviewResult();
        result.totalStorageBytes = GetDirectorySizeBytes(syncDir);
        result.totalStorageFormatted = FormatBytes(result.totalStorageBytes);

        if (itemsDb == null || hashToFilePathMap == null) return result;

        DateTime cutoff = DateTime.Now.AddDays(-30);

        foreach (var item in itemsDb)
        {
            if (item == null) continue;

            string status = item.ContainsKey("status") ? (item["status"] ?? "").ToString() : "";
            string statusLabel = item.ContainsKey("statusLabel") ? (item["statusLabel"] ?? "").ToString() : "";

            bool isSold = string.Equals(status, "SOLD", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(status, "MERGED", StringComparison.OrdinalIgnoreCase) ||
                         statusLabel.Contains("売却");

            if (!isSold) continue;

            // soldAt の解析
            DateTime soldDate = DateTime.MinValue;
            bool hasValidSoldAt = false;

            if (item.ContainsKey("soldAt") && item["soldAt"] != null)
            {
                try
                {
                    long epochMs = Convert.ToInt64(item["soldAt"]);
                    if (epochMs > 0)
                    {
                        soldDate = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).LocalDateTime;
                        hasValidSoldAt = true;
                    }
                }
                catch { }
            }

            if (item.ContainsKey("photos") && item["photos"] != null)
            {
                var photosList = item["photos"] as System.Collections.IEnumerable;
                if (photosList != null)
                {
                    foreach (var photoObj in photosList)
                    {
                        var photo = photoObj as Dictionary<string, object>;
                        if (photo == null || !photo.ContainsKey("md5Hash") || photo["md5Hash"] == null) continue;

                        string hash = photo["md5Hash"].ToString().Trim();
                        if (string.IsNullOrEmpty(hash)) continue;

                        string filePath;
                        if (hashToFilePathMap.TryGetValue(hash, out filePath) && !string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                        {
                            FileInfo fi = new FileInfo(filePath);

                            DateTime targetDate = hasValidSoldAt ? soldDate : fi.LastWriteTime;

                            if (targetDate <= cutoff)
                            {
                                int id = item.ContainsKey("id") ? Convert.ToInt32(item["id"]) : 0;
                                string title = item.ContainsKey("title") ? (item["title"] ?? "無題").ToString() : "無題";

                                result.candidates.Add(new CleanupCandidate
                                {
                                    itemId = id,
                                    title = title,
                                    status = statusLabel,
                                    soldAtText = targetDate.ToString("yyyy/MM/dd"),
                                    photoHash = hash,
                                    fileName = fi.Name,
                                    filePath = filePath,
                                    sizeBytes = fi.Length,
                                    sizeFormatted = FormatBytes(fi.Length)
                                });

                                result.reclaimableBytes += fi.Length;
                            }
                        }
                    }
                }
            }
        }

        result.candidateCount = result.candidates.Count;
        result.reclaimableFormatted = FormatBytes(result.reclaimableBytes);
        return result;
    }

    public static int ExecuteCleanup(List<CleanupCandidate> candidates, out long freedBytes)
    {
        int deletedCount = 0;
        freedBytes = 0;

        if (candidates == null) return 0;

        foreach (var c in candidates)
        {
            try
            {
                if (!string.IsNullOrEmpty(c.filePath) && File.Exists(c.filePath))
                {
                    FileInfo fi = new FileInfo(c.filePath);
                    long size = fi.Length;
                    File.Delete(c.filePath);
                    deletedCount++;
                    freedBytes += size;
                    Console.WriteLine("  -> [30日クリーンアップ] 削除成功: " + fi.Name + " (" + FormatBytes(size) + ")");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[30日クリーンアップ] ファイル削除失敗: " + c.filePath + " (" + ex.Message + ")");
            }
        }

        return deletedCount;
    }
}
