using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class FurimaServerNative
{
    private static Mutex mutex = null;
    private static string syncDir = "";
    private static string itemsJsonFile = "";
    private static DateTime lastHeartbeat = DateTime.MinValue;
    private static readonly object lockObj = new object();
    private static Dictionary<string, string> hashToFilePathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static List<Dictionary<string, object>> itemsDb = new List<Dictionary<string, object>>();

    static void Main(string[] args)
    {
        // 二重起動防止 (名前付き Mutex)
        bool createdNew;
        mutex = new Mutex(true, "FurimaServerNative_SingleInstance_Mutex", out createdNew);
        if (!createdNew)
        {
            Console.WriteLine("[エラー] 母艦サーバーは既に起動しています。二重起動を防止するため終了します。");
            return;
        }

        try
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("  フリマ出品・商品PC母艦サーバー (v2.1 Native) ");
            Console.WriteLine("=================================================");

            int desiredPort = 8080;
            int inputPort;
            if (args.Length > 0 && int.TryParse(args[0], out inputPort))
            {
                desiredPort = inputPort;
            }

            // 保存先と store.json の初期化
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            SyncStore.Initialize(baseDir);
            syncDir = SyncStore.SyncDir;
            itemsJsonFile = Path.Combine(syncDir, "items_data.json");

            // 起動ログの1行目に絶対パスと storeId を出力
            Console.WriteLine("[INFO] 保存フォルダ: " + syncDir + " (storeId: " + SyncStore.StoreId + ")");

            Console.WriteLine("既存写真のMD5ハッシュをインデックス化中...");
            ScanAndIndexLocalPhotos();

            Console.WriteLine("商品メタデータ (items_data.json) をロード中...");
            LoadItemsDbFromDisk();

            HttpListener listener = null;
            int actualPort = desiredPort;
            string boundUrl = "";

            // 実動作テスト型ポート自動探索 (8080 -> 8081 -> 8082...)
            for (int pNum = desiredPort; pNum < desiredPort + 50; pNum++)
            {
                listener = TryStartHttpListener(pNum, out boundUrl);
                if (listener != null)
                {
                    actualPort = pNum;
                    break;
                }
            }

            if (listener == null)
            {
                Console.WriteLine("\n[エラー] 利用可能な空きポートの確保に失敗しました。");
                PrintOccupiedPortInfo(desiredPort);
                Console.WriteLine("\n何かキーを押すと終了します...");
                Console.ReadLine();
                return;
            }

            if (actualPort != desiredPort)
            {
                Console.WriteLine("\n[お知らせ] ポート " + desiredPort + " は他アプリやOS予約で使用中のため、空きポート " + actualPort + " で自動起動しました。");
                PrintOccupiedPortInfo(desiredPort);
            }

            Console.WriteLine("-------------------------------------------------");
            Console.WriteLine(" サーバーが正常起動しました: " + boundUrl);
            Console.WriteLine(" スマホアプリからの接続を待機しています...");
            Console.WriteLine("-------------------------------------------------");

            try
            {
                Process.Start(new ProcessStartInfo(boundUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine("ブラウザ自動起動警告: " + ex.Message);
            }

            ThreadPool.QueueUserWorkItem((o) =>
            {
                while (listener.IsListening)
                {
                    try
                    {
                        var ctx = listener.GetContext();
                        ThreadPool.QueueUserWorkItem((state) => HandleRequest(ctx));
                    }
                    catch { }
                }
            });

            Console.WriteLine("\nサーバー稼働中 [終了するには Enter キーを押してください]");
            Console.ReadLine();
            try { listener.Stop(); } catch {}
        }
        catch (Exception fatalEx)
        {
            Console.WriteLine("\n[致命的エラー] 起動処理中に例外が発生しました:");
            Console.WriteLine(fatalEx.ToString());
            Console.WriteLine("\n何かキーを押すと終了します...");
            Console.ReadLine();
        }
        finally
        {
            if (mutex != null)
            {
                try { mutex.ReleaseMutex(); } catch {}
            }
        }
    }

    private static HttpListener TryStartHttpListener(int port, out string boundUrl)
    {
        boundUrl = "";
        HttpListener listener = null;

        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://+:" + port + "/");
            listener.Start();
            boundUrl = "http://localhost:" + port + "/";
            return listener;
        }
        catch
        {
            if (listener != null) { try { listener.Close(); } catch {} }
            listener = null;
        }

        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:" + port + "/");
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.Start();
            boundUrl = "http://localhost:" + port + "/";
            return listener;
        }
        catch
        {
            if (listener != null) { try { listener.Close(); } catch {} }
            listener = null;
        }

        return null;
    }

    private static void PrintOccupiedPortInfo(int port)
    {
        try
        {
            Process p = new Process();
            p.StartInfo.FileName = "cmd.exe";
            p.StartInfo.Arguments = "/c netstat -ano | findstr :" + port;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            if (!string.IsNullOrEmpty(output))
            {
                Console.WriteLine("[ポート " + port + " 占有状況 (netstat)]:");
                Console.WriteLine(output.Trim());
            }
        }
        catch { }
    }

    private static void ScanAndIndexLocalPhotos()
    {
        lock (lockObj)
        {
            hashToFilePathMap.Clear();
            if (!Directory.Exists(syncDir)) return;

            var files = Directory.GetFiles(syncDir, "*.jpg", SearchOption.AllDirectories);
            using (var md5 = MD5.Create())
            {
                foreach (var file in files)
                {
                    try
                    {
                        using (var stream = File.OpenRead(file))
                        {
                            byte[] hashBytes = md5.ComputeHash(stream);
                            string hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                            if (!hashToFilePathMap.ContainsKey(hashStr))
                            {
                                hashToFilePathMap[hashStr] = file;
                            }
                        }
                    }
                    catch { }
                }
            }
            Console.WriteLine("インデックス完了: " + hashToFilePathMap.Count + "件のユニーク写真ハッシュをロードしました。");
        }
    }

    private static void LoadItemsDbFromDisk()
    {
        lock (lockObj)
        {
            itemsDb.Clear();
            if (File.Exists(itemsJsonFile))
            {
                try
                {
                    string json = File.ReadAllText(itemsJsonFile, Encoding.UTF8);
                    var loaded = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
                    if (loaded != null)
                    {
                        itemsDb = loaded;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("items_data.json ロード例外: " + ex.Message);
                }
            }
            Console.WriteLine("ロード完了: " + itemsDb.Count + "件の商品メタデータを保持しています。");
        }
    }

    private static void SaveItemsDbToDisk()
    {
        lock (lockObj)
        {
            try
            {
                SyncStore.BackupItemsJson(itemsJsonFile);
                string json = JsonConvert.SerializeObject(itemsDb, Formatting.Indented);
                File.WriteAllText(itemsJsonFile, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine("items_data.json 保存例外: " + ex.Message);
            }
        }
    }

    private static void HandleRequest(HttpListenerContext ctx)
    {
        HttpListenerRequest req = ctx.Request;
        HttpListenerResponse res = ctx.Response;

        // CORS ヘッダー付与
        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Photo-Hash");

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = 200;
            res.Close();
            return;
        }

        string rawPath = req.Url.AbsolutePath;
        if (rawPath != "/api/server/heartbeat")
        {
            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] 受信リクエスト: " + req.HttpMethod + " " + rawPath);
        }

        try
        {
            if (rawPath == "/")
            {
                ServeDashboardHtml(res);
            }
            else if (rawPath == "/api/items" && req.HttpMethod == "GET")
            {
                lock (lockObj)
                {
                    SendJson(res, itemsDb);
                }
            }
            else if (rawPath == "/api/server/status")
            {
                bool isConnected = (DateTime.Now - lastHeartbeat).TotalSeconds < 10;
                int missingCount;
                lock (lockObj)
                {
                    missingCount = SyncStore.CountMissingFiles(itemsDb, hashToFilePathMap);
                }

                var dict = new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "appConnected", isConnected },
                    { "lastHeartbeatSec", (int)(DateTime.Now - lastHeartbeat).TotalSeconds },
                    { "indexedPhotosCount", hashToFilePathMap.Count },
                    { "missingFileCount", missingCount },
                    { "storeId", SyncStore.StoreId },
                    { "syncDir", SyncStore.SyncDir },
                    { "totalItemsCount", itemsDb.Count }
                };
                SendJson(res, dict);
            }
            else if (rawPath == "/api/server/heartbeat")
            {
                lastHeartbeat = DateTime.Now;
                SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "timestamp", DateTime.Now.Ticks } });
            }
            else if (rawPath == "/api/sync/hashes")
            {
                List<string> validHashes = new List<string>();
                lock (lockObj)
                {
                    foreach (var pair in hashToFilePathMap)
                    {
                        if (!string.IsNullOrEmpty(pair.Value) && File.Exists(pair.Value))
                        {
                            validHashes.Add(pair.Key);
                        }
                    }
                }
                SendJson(res, new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "totalCount", validHashes.Count },
                    { "hashes", validHashes }
                });
            }
            else if (rawPath == "/api/sync/items" && req.HttpMethod == "POST")
            {
                lastHeartbeat = DateTime.Now;
                HandleSyncItems(req, res);
            }
            else if (rawPath == "/api/sync/photo" && req.HttpMethod == "POST")
            {
                lastHeartbeat = DateTime.Now;
                HandleSyncPhoto(req, res);
            }
            else if (rawPath.StartsWith("/api/photos/"))
            {
                string hash = rawPath.Substring("/api/photos/".Length).Trim();
                ServePhotoByHash(res, hash);
            }
            else
            {
                res.StatusCode = 404;
                res.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("リクエスト処理エラー: " + ex.Message);
            res.StatusCode = 500;
            res.Close();
        }
    }

    private static void HandleSyncItems(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
            {
                string jsonStr = reader.ReadToEnd();
                JObject json = JObject.Parse(jsonStr);

                string reqStoreId = json.Value<string>("storeId") ?? "";
                JArray itemsArr = json["items"] as JArray;

                List<string> syncedHashes = new List<string>();
                List<string> neededHashes = new List<string>();

                if (itemsArr != null)
                {
                    lock (lockObj)
                    {
                        foreach (JObject item in itemsArr)
                        {
                            int id = item.Value<int>("id");
                            string title = item.Value<string>("title") ?? "商品";
                            string description = item.Value<string>("description") ?? "";
                            int price = item.Value<int>("price");
                            int profit = item.Value<int>("profit");
                            string statusLabel = item.Value<string>("statusLabel") ?? "";

                            string safeTitle = title;
                            foreach (char c in Path.GetInvalidFileNameChars())
                            {
                                safeTitle = safeTitle.Replace(c, '_');
                            }

                            string itemFolderName = id + "_" + safeTitle;
                            string itemDirPath = Path.Combine(syncDir, itemFolderName);
                            Directory.CreateDirectory(itemDirPath);

                            // 説明文.txt 保存
                            string txtPath = Path.Combine(itemDirPath, "商品情報_説明文.txt");
                            string txtContent = "【商品ID】 " + id + "\n【タイトル】 " + title + "\n【販売価格】 ¥" + price.ToString("N0") + "\n【純利益】 ¥" + profit.ToString("N0") + "\n【ステータス】 " + statusLabel + "\n\n--- 商品説明 ---\n" + description;
                            File.WriteAllText(txtPath, txtContent, Encoding.UTF8);

                            JArray photosArr = item["photos"] as JArray;
                            List<Dictionary<string, object>> savedPhotosList = new List<Dictionary<string, object>>();

                            if (photosArr != null)
                            {
                                foreach (JObject photo in photosArr)
                                {
                                    int pId = photo.Value<int>("id");
                                    string md5Hash = (photo.Value<string>("md5Hash") ?? "").Trim().ToLower();

                                    bool existsOnServer = !string.IsNullOrEmpty(md5Hash) &&
                                                          hashToFilePathMap.ContainsKey(md5Hash) &&
                                                          File.Exists(hashToFilePathMap[md5Hash]);

                                    if (existsOnServer)
                                    {
                                        if (!syncedHashes.Contains(md5Hash)) syncedHashes.Add(md5Hash);
                                    }
                                    else if (!string.IsNullOrEmpty(md5Hash))
                                    {
                                        if (!neededHashes.Contains(md5Hash)) neededHashes.Add(md5Hash);
                                    }

                                    savedPhotosList.Add(new Dictionary<string, object>
                                    {
                                        { "id", pId },
                                        { "md5Hash", md5Hash },
                                        { "photoUrl", "/api/photos/" + md5Hash }
                                    });
                                }
                            }

                            var existingItem = itemsDb.Find(i => Convert.ToInt32(i["id"]) == id);
                            var newItemDict = new Dictionary<string, object>
                            {
                                { "id", id },
                                { "title", title },
                                { "description", description },
                                { "status", item.Value<string>("status") ?? "LISTED" },
                                { "price", price },
                                { "soldPrice", item.Value<int>("soldPrice") },
                                { "profit", profit },
                                { "statusLabel", statusLabel },
                                { "isShared", true },
                                { "photos", savedPhotosList }
                            };

                            if (existingItem != null) itemsDb.Remove(existingItem);
                            itemsDb.Add(newItemDict);
                        }

                        // 不要フォルダの自動削除 Guard
                        bool shouldCleanup = !string.IsNullOrEmpty(reqStoreId) && string.Equals(reqStoreId, SyncStore.StoreId, StringComparison.OrdinalIgnoreCase);
                        JArray activeFolders = json["activeFolderNames"] as JArray;

                        if (shouldCleanup && activeFolders != null)
                        {
                            var activeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var f in activeFolders) activeSet.Add(f.ToString());

                            if (Directory.Exists(syncDir))
                            {
                                foreach (var subDir in Directory.GetDirectories(syncDir))
                                {
                                    string dirName = Path.GetFileName(subDir);
                                    if (!activeSet.Contains(dirName))
                                    {
                                        try
                                        {
                                            Directory.Delete(subDir, true);
                                            Console.WriteLine("  -> [クリーンアップ] 不要フォルダ削除: " + dirName);
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                    }

                    SaveItemsDbToDisk();

                    SendJson(res, new Dictionary<string, object>
                    {
                        { "status", "ok" },
                        { "protocolVersion", 3 },
                        { "storeId", SyncStore.StoreId },
                        { "syncedHashes", syncedHashes },
                        { "neededHashes", neededHashes }
                    });
                    return;
                }
            }

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "protocolVersion", 3 },
                { "storeId", SyncStore.StoreId },
                { "message", "No items" }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("SyncItems エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleSyncPhoto(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string expectedHash = (req.Headers["X-Photo-Hash"] ?? "").Trim().ToLower();
            if (string.IsNullOrEmpty(expectedHash))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "stored", false }, { "message", "X-Photo-Hash header is missing" } });
                return;
            }

            byte[] buffer = new byte[req.ContentLength64 > 0 ? req.ContentLength64 : 1024 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                req.InputStream.CopyTo(ms);
                byte[] photoBytes = ms.ToArray();

                using (var md5 = MD5.Create())
                {
                    byte[] computedBytes = md5.ComputeHash(photoBytes);
                    string computedHash = BitConverter.ToString(computedBytes).Replace("-", "").ToLower();

                    if (!string.Equals(expectedHash, computedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("[警告] 写真ハッシュ不一致! 申告: " + expectedHash + ", 計算: " + computedHash);
                        res.StatusCode = 400;
                        SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "stored", false }, { "message", "Hash mismatch" } });
                        return;
                    }

                    // 対象商品フォルダの特定
                    string targetDirPath = syncDir;
                    lock (lockObj)
                    {
                        foreach (var item in itemsDb)
                        {
                            if (item.ContainsKey("photos") && item["photos"] != null)
                            {
                                var photosList = item["photos"] as System.Collections.IEnumerable;
                                if (photosList != null)
                                {
                                    foreach (var pObj in photosList)
                                    {
                                        var p = pObj as Dictionary<string, object>;
                                        if (p != null && p.ContainsKey("md5Hash") && string.Equals(p["md5Hash"].ToString(), computedHash, StringComparison.OrdinalIgnoreCase))
                                        {
                                            int id = Convert.ToInt32(item["id"]);
                                            string title = item.ContainsKey("title") ? item["title"].ToString() : "";
                                            foreach (char c in Path.GetInvalidFileNameChars()) title = title.Replace(c, '_');
                                            targetDirPath = Path.Combine(syncDir, id + "_" + title);
                                            Directory.CreateDirectory(targetDirPath);
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    string photoFileName = "photo_" + computedHash.Substring(0, 8) + ".jpg";
                    string photoPath = Path.Combine(targetDirPath, photoFileName);
                    File.WriteAllBytes(photoPath, photoBytes);

                    lock (lockObj)
                    {
                        hashToFilePathMap[computedHash] = photoPath;
                    }

                    Console.WriteLine("  -> [写真保存完了] MD5: " + computedHash + " パス: " + photoPath);

                    SendJson(res, new Dictionary<string, object>
                    {
                        { "status", "ok" },
                        { "stored", true },
                        { "hash", computedHash }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("SyncPhoto エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "stored", false }, { "message", ex.Message } });
        }
    }

    private static void ServePhotoByHash(HttpListenerResponse res, string hash)
    {
        lock (lockObj)
        {
            string filePath;
            if (hashToFilePathMap.TryGetValue(hash, out filePath) && File.Exists(filePath))
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                res.ContentType = "image/jpeg";
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
                res.OutputStream.Close();
                return;
            }
        }
        res.StatusCode = 404;
        res.Close();
    }

    private static void SendJson(HttpListenerResponse res, object obj)
    {
        string json = JsonConvert.SerializeObject(obj);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Close();
    }

    private static void ServeDashboardHtml(HttpListenerResponse res)
    {
        string html = DashboardHtml.GetHtml();
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        res.ContentType = "text/html; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Close();
    }
}
