using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;

public class StoreConfig
{
    public string storeId { get; set; }
    public string path { get; set; }
    public string createdAt { get; set; }
}

public static class SyncStore
{
    private static string _storeId = "";
    private static string _syncDir = "";

    public static string StoreId { get { return _storeId; } }
    public static string SyncDir { get { return _syncDir; } }

    public static void Initialize(string baseDir)
    {
        string storeJsonPath = Path.Combine(baseDir, "store.json");

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
                    if (!Directory.Exists(_syncDir))
                    {
                        Directory.CreateDirectory(_syncDir);
                    }
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
        Directory.CreateDirectory(_syncDir);

        StoreConfig newCfg = new StoreConfig
        {
            storeId = _storeId,
            path = _syncDir,
            createdAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK")
        };

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
}
