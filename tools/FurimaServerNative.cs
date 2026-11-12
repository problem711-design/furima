using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;
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

    private static string authToken = "";
    private static string tunnelUrl = "";
    private static TunnelQrForm qrForm = null;
    private static Process cloudflaredProcess = null;

    [STAThread]
    static void Main(string[] args)
    {
        // 二重起動防止 (名前付き Mutex)
        bool createdNew;
        mutex = new Mutex(true, "FurimaServerNative_SingleInstance_Mutex", out createdNew);
        if (!createdNew)
        {
            MessageBox.Show("母艦サーバーは既に起動しています。二重起動を防止するため終了します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            // 保存先と store.json の初期化（永続化された authToken を取得）
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            SyncStore.Initialize(baseDir, ref authToken);
            syncDir = SyncStore.SyncDir;
            itemsJsonFile = Path.Combine(syncDir, "items_data.json");

            Console.WriteLine("[INFO] ワンタイム認証トークン: " + authToken);

            Console.WriteLine("[INFO] 保存フォルダ: " + syncDir + " (storeId: " + SyncStore.StoreId + ")");

            Console.WriteLine("既存写真のMD5ハッシュをインデックス化中...");
            ScanAndIndexLocalPhotos();

            Console.WriteLine("商品メタデータ (items_data.json) をロード中...");
            LoadItemsDbFromDisk();

            HttpListener listener = null;
            int actualPort = desiredPort;
            string boundUrl = "";

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
                MessageBox.Show("利用可能な空きポートの確保に失敗しました。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (actualPort != desiredPort)
            {
                Console.WriteLine("\n[お知らせ] ポート " + desiredPort + " は他アプリやOS予約で使用中のため、空きポート " + actualPort + " で自動起動しました。");
            }

            Console.WriteLine("-------------------------------------------------");
            Console.WriteLine(" サーバーが正常起動しました: " + boundUrl);
            Console.WriteLine("-------------------------------------------------");

            ThreadPool.QueueUserWorkItem((o) =>
            {
                while (listener != null && listener.IsListening)
                {
                    try
                    {
                        var ctx = listener.GetContext();
                        ThreadPool.QueueUserWorkItem((state) => HandleRequest(ctx));
                    }
                    catch { }
                }
            });

            // Cloudflare トンネルの起動
            StartCloudflared(actualPort);

            // WinForms 画面の表示
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            qrForm = new TunnelQrForm(SyncStore.StoreId, syncDir, boundUrl, authToken);

            int indexedCount = hashToFilePathMap.Count;
            int missingCount = SyncStore.CountMissingFiles(itemsDb, hashToFilePathMap);
            qrForm.UpdateStatusInfo(indexedCount, missingCount);

            qrForm.FormClosed += (s, e) =>
            {
                try { if (listener != null && listener.IsListening) listener.Stop(); } catch { }
                try { if (cloudflaredProcess != null && !cloudflaredProcess.HasExited) cloudflaredProcess.Kill(); } catch { }
            };

            Application.Run(qrForm);
        }
        catch (Exception fatalEx)
        {
            Console.WriteLine("\n[致命的エラー] 起動処理中に例外が発生しました:");
            Console.WriteLine(fatalEx.ToString());
            MessageBox.Show("起動処理中に例外が発生しました:\n" + fatalEx.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (cloudflaredProcess != null && !cloudflaredProcess.HasExited)
            {
                try { cloudflaredProcess.Kill(); } catch { }
            }
            if (mutex != null)
            {
                try { mutex.ReleaseMutex(); } catch { }
            }
        }
    }

    private static void StartCloudflared(int port)
    {
        ThreadPool.QueueUserWorkItem((o) =>
        {
            try
            {
                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloudflared.exe");
                if (!File.Exists(exePath))
                {
                    Console.WriteLine("[cloudflared] cloudflared.exe が見つかりません。");
                    if (qrForm != null) qrForm.UpdateTunnelUrl("");
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "tunnel --url http://127.0.0.1:" + port,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                cloudflaredProcess = new Process { StartInfo = psi };
                DataReceivedEventHandler handler = (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        if (e.Data.Contains("trycloudflare.com"))
                        {
                            var match = Regex.Match(e.Data, @"https://[a-zA-Z0-9\-]+\.trycloudflare\.com");
                            if (match.Success)
                            {
                                tunnelUrl = match.Value;
                                Console.WriteLine("[cloudflared] トンネルURL発行完了: " + tunnelUrl);
                                if (qrForm != null) qrForm.UpdateTunnelUrl(tunnelUrl);
                            }
                        }
                    }
                };

                cloudflaredProcess.OutputDataReceived += handler;
                cloudflaredProcess.ErrorDataReceived += handler;

                cloudflaredProcess.Start();
                cloudflaredProcess.BeginOutputReadLine();
                cloudflaredProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[cloudflared] 起動エラー: " + ex.Message);
                if (qrForm != null) qrForm.UpdateTunnelUrl("");
            }
        });
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
            if (listener != null) { try { listener.Close(); } catch { } }
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
            if (listener != null) { try { listener.Close(); } catch { } }
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

    private static bool AuthenticateRequest(HttpListenerContext ctx)
    {
        HttpListenerRequest req = ctx.Request;
        HttpListenerResponse res = ctx.Response;

        if (req.HttpMethod == "OPTIONS") return true;

        IPAddress remoteIp = req.RemoteEndPoint.Address;
        bool isLoopback = IPAddress.IsLoopback(remoteIp);

        // ループバックからのアクセスは許可
        if (isLoopback) return true;

        string path = req.Url.AbsolutePath;
        // ループバック以外からの /api/ リクエストには Bearer トークンが必要
        if (path.StartsWith("/api/"))
        {
            string authHeader = req.Headers["Authorization"] ?? "";
            string expectedAuth = "Bearer " + authToken;

            if (!string.Equals(authHeader.Trim(), expectedAuth, StringComparison.Ordinal))
            {
                Console.WriteLine("[認証失敗] トークン不一致または欠落 (IP: " + remoteIp + ", Path: " + path + ")");
                res.StatusCode = 401;
                SendJson(res, new Dictionary<string, object>
                {
                    { "status", "error" },
                    { "message", "401 Unauthorized: Invalid or missing Bearer token" }
                });
                return false;
            }
        }

        return true;
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

        if (!AuthenticateRequest(ctx))
        {
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
            else if (rawPath == "/api/items" && req.HttpMethod == "POST")
            {
                HandleCreateItem(req, res);
            }
            else if (rawPath == "/api/server/status")
            {
                // 10秒から25秒に緩和（トンネル通信レイテンシやモバイル通信の揺らぎを許容）
                bool isConnected = (DateTime.Now - lastHeartbeat).TotalSeconds < 25;
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
                if (qrForm != null) qrForm.OnAppConnected();
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
                if (qrForm != null) qrForm.OnAppConnected();
                HandleSyncItems(req, res);
            }
            else if (rawPath == "/api/sync/photo" && req.HttpMethod == "POST")
            {
                lastHeartbeat = DateTime.Now;
                if (qrForm != null) qrForm.OnAppConnected();
                HandleSyncPhoto(req, res);
            }
            else if (rawPath == "/api/cleanup/preview" && req.HttpMethod == "GET")
            {
                HandleCleanupPreview(res);
            }
            else if (rawPath == "/api/cleanup/execute" && req.HttpMethod == "POST")
            {
                HandleCleanupExecute(res);
            }
            else if (rawPath == "/api/items/merge" && req.HttpMethod == "POST")
            {
                HandleMergeItems(req, res);
            }
            else if (rawPath.StartsWith("/api/items/") && rawPath.EndsWith("/unmerge") && req.HttpMethod == "POST")
            {
                HandleUnmergeItem(res, rawPath);
            }
            else if (rawPath.StartsWith("/api/items/") && rawPath.EndsWith("/photos/add") && req.HttpMethod == "POST")
            {
                HandleAddPhoto(req, res, rawPath);
            }
            else if (rawPath.StartsWith("/api/items/") && rawPath.EndsWith("/open-folder") && req.HttpMethod == "POST")
            {
                HandleOpenFolder(res, rawPath);
            }
            else if (rawPath.StartsWith("/api/items/") && req.HttpMethod == "PUT")
            {
                HandleUpdateItem(req, res, rawPath);
            }
            else if (rawPath.StartsWith("/api/items/") && req.HttpMethod == "DELETE")
            {
                HandleDeleteItem(res, rawPath);
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
                        HashSet<int> activeIds = new HashSet<int>();
                        foreach (JObject item in itemsArr)
                        {
                            activeIds.Add(item.Value<int>("id"));
                        }

                        // スマホ側に存在しない古い商品を itemsDb から一括除去 (完全同期)
                        // ※ ただし、まとめ内包中の子商品 (mergedParentId != null) や、PC側で作成されたまとめ親商品は誤削除から保護
                        itemsDb.RemoveAll(i => {
                            if (i == null || !i.ContainsKey("id")) return false;
                            int itemId = ParseIntSafe(i["id"]);
                            if (activeIds.Contains(itemId)) return false;
                            if (i.ContainsKey("mergedParentId") && ParseIntSafe(i["mergedParentId"], 0) > 0) return false;
                            if (i.ContainsKey("mergedChildCount") && ParseIntSafe(i["mergedChildCount"], 0) > 0 && !(bool)(i.ContainsKey("isDeleted") && (bool)i["isDeleted"])) return false;
                            return true;
                        });

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
                                { "isDeleted", false },
                                { "updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                                { "photos", savedPhotosList },
                                { "mergedParentId", item["mergedParentId"] },
                                { "mergedChildCount", item["mergedChildCount"] },
                                { "bundleLevel", item["bundleLevel"] },
                                { "mergedChildTitles", item["mergedChildTitles"] },
                                { "originalTotalBundlePrice", item["originalTotalBundlePrice"] },
                                { "discountAmount", item["discountAmount"] },
                                { "shippingCost", item["shippingCost"] != null ? item.Value<int>("shippingCost") : 210 },
                                { "feeRatePercent", item["feeRatePercent"] != null ? item.Value<int>("feeRatePercent") : 5 },
                                { "expenseCost", item["expenseCost"] != null ? item.Value<int>("expenseCost") : 55 },
                                { "useCustomSettings", item["useCustomSettings"] != null && item.Value<bool>("useCustomSettings") }
                            };

                            // 同一IDの既存エントリーがあれば重複防止のため削除してから追加 (上書き更新)
                            itemsDb.RemoveAll(i => i != null && i.ContainsKey("id") && Convert.ToInt32(i["id"]) == id);
                            itemsDb.Add(newItemDict);
                        }

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

                        // フォルダ削除・写真更新後にインデックスを最新化
                        ScanAndIndexLocalPhotos();
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

    private static void HandleCleanupPreview(HttpListenerResponse res)
    {
        try
        {
            SyncStore.CleanupPreviewResult preview;
            lock (lockObj)
            {
                preview = SyncStore.GetCleanupCandidates(itemsDb, hashToFilePathMap, syncDir);
            }
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "totalStorageBytes", preview.totalStorageBytes },
                { "totalStorageFormatted", preview.totalStorageFormatted },
                { "candidateCount", preview.candidateCount },
                { "reclaimableBytes", preview.reclaimableBytes },
                { "reclaimableFormatted", preview.reclaimableFormatted },
                { "candidates", preview.candidates }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("CleanupPreview エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleCleanupExecute(HttpListenerResponse res)
    {
        try
        {
            SyncStore.CleanupPreviewResult preview;
            lock (lockObj)
            {
                preview = SyncStore.GetCleanupCandidates(itemsDb, hashToFilePathMap, syncDir);
            }

            long freedBytes;
            int deletedCount = SyncStore.ExecuteCleanup(preview.candidates, out freedBytes);

            ScanAndIndexLocalPhotos();

            if (qrForm != null)
            {
                int indexedCount = hashToFilePathMap.Count;
                int missingCount = SyncStore.CountMissingFiles(itemsDb, hashToFilePathMap);
                qrForm.UpdateStatusInfo(indexedCount, missingCount);
            }

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "deletedCount", deletedCount },
                { "freedBytes", freedBytes },
                { "freedFormatted", SyncStore.FormatBytes(freedBytes) }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("CleanupExecute エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleUpdateItem(HttpListenerRequest req, HttpListenerResponse res, string path)
    {
        try
        {
            string idStr = path.Substring("/api/items/".Length).Trim();
            int itemId;
            if (!int.TryParse(idStr, out itemId))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid ID" } });
                return;
            }

            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
            {
                string jsonStr = reader.ReadToEnd();
                JObject json = JObject.Parse(jsonStr);

                string title = json.Value<string>("title") ?? "";
                int price = json.Value<int>("price");
                int soldPrice = json.Value<int>("soldPrice");
                string status = json.Value<string>("status") ?? "LISTED";
                string description = json.Value<string>("description") ?? "";

                string statusLabel = status == "SOLD" ? "売却済み" : (status == "DRAFT" ? "未出品" : "出品中");

                lock (lockObj)
                {
                    var item = itemsDb.Find(i => Convert.ToInt32(i["id"]) == itemId);
                    if (item == null)
                    {
                        res.StatusCode = 404;
                        SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Item not found" } });
                        return;
                    }

                    int shipping = item.ContainsKey("shippingCost") && item["shippingCost"] != null ? ParseIntSafe(item["shippingCost"], 210) : 210;
                    int feeRate = item.ContainsKey("feeRatePercent") && item["feeRatePercent"] != null ? ParseIntSafe(item["feeRatePercent"], 5) : 5;
                    int expense = item.ContainsKey("expenseCost") && item["expenseCost"] != null ? ParseIntSafe(item["expenseCost"], 55) : 55;
                    bool useCustom = item.ContainsKey("useCustomSettings") && item["useCustomSettings"] != null && (bool)item["useCustomSettings"];

                    if (json["shippingCost"] != null) shipping = ParseIntSafe(json["shippingCost"], shipping);
                    if (json["feeRatePercent"] != null) feeRate = ParseIntSafe(json["feeRatePercent"], feeRate);
                    if (json["expenseCost"] != null) expense = ParseIntSafe(json["expenseCost"], expense);
                    if (json["useCustomSettings"] != null) useCustom = json.Value<bool>("useCustomSettings");

                    int calcPrice = (status == "SOLD" && soldPrice > 0) ? soldPrice : price;
                    int profit = calcPrice - (calcPrice * feeRate / 100) - shipping - expense;

                    item["title"] = title;
                    item["price"] = price;
                    item["soldPrice"] = soldPrice;
                    item["status"] = status;
                    item["statusLabel"] = statusLabel;
                    item["description"] = description;
                    item["shippingCost"] = shipping;
                    item["feeRatePercent"] = feeRate;
                    item["expenseCost"] = expense;
                    item["useCustomSettings"] = useCustom;
                    item["profit"] = profit;
                    item["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    try
                    {
                        string safeTitle = title;
                        foreach (char c in Path.GetInvalidFileNameChars()) safeTitle = safeTitle.Replace(c, '_');
                        string targetDir = null;
                        if (Directory.Exists(syncDir))
                        {
                            foreach (string d in Directory.GetDirectories(syncDir))
                            {
                                string name = Path.GetFileName(d);
                                if (name.StartsWith(itemId + "_") || name == itemId.ToString())
                                {
                                    targetDir = d;
                                    break;
                                }
                            }
                        }
                        if (targetDir == null) targetDir = Path.Combine(syncDir, itemId + "_" + safeTitle);
                        Directory.CreateDirectory(targetDir);
                        string txtPath = Path.Combine(targetDir, "商品情報_説明文.txt");
                        string txtContent = "【商品ID】 " + itemId + "\n【タイトル】 " + title + "\n【販売価格】 ¥" + price.ToString("N0") + "\n【純利益】 ¥" + profit.ToString("N0") + "\n【ステータス】 " + statusLabel + "\n\n--- 商品説明 ---\n" + description;
                        File.WriteAllText(txtPath, txtContent, Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("説明文txt更新例外: " + ex.Message);
                    }
                }

                SaveItemsDbToDisk();

                SendJson(res, new Dictionary<string, object> { { "status", "ok" } });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("UpdateItem エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleOpenFolder(HttpListenerResponse res, string path)
    {
        try
        {
            string idStr = path.Substring("/api/items/".Length);
            idStr = idStr.Substring(0, idStr.Length - "/open-folder".Length).Trim();
            int itemId;
            if (!int.TryParse(idStr, out itemId))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid ID" } });
                return;
            }

            string targetDir = null;
            if (Directory.Exists(syncDir))
            {
                foreach (string dir in Directory.GetDirectories(syncDir))
                {
                    string dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith(itemId + "_") || dirName == itemId.ToString())
                    {
                        targetDir = dir;
                        break;
                    }
                }
            }

            if (targetDir == null || !Directory.Exists(targetDir))
            {
                targetDir = Path.Combine(syncDir, itemId + "_商品");
                Directory.CreateDirectory(targetDir);
            }

            try
            {
                Process.Start("explorer.exe", targetDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine("エクスプローラー起動通知: " + ex.Message);
            }

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "folderPath", targetDir }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleOpenFolder エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleDeleteItem(HttpListenerResponse res, string path)
    {
        try
        {
            string idStr = path.Substring("/api/items/".Length).Trim();
            int itemId;
            if (!int.TryParse(idStr, out itemId))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid ID" } });
                return;
            }

            lock (lockObj)
            {
                var item = itemsDb.Find(i => Convert.ToInt32(i["id"]) == itemId);
                if (item != null)
                {
                    item["isDeleted"] = true;
                    item["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
            }

            SaveItemsDbToDisk();

            SendJson(res, new Dictionary<string, object> { { "status", "ok" } });
        }
        catch (Exception ex)
        {
            Console.WriteLine("DeleteItem エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleCreateItem(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
            {
                string jsonStr = reader.ReadToEnd();
                JObject json = JObject.Parse(jsonStr);

                string title = json.Value<string>("title") ?? "新規商品";
                int price = json.Value<int>("price");
                string status = json.Value<string>("status") ?? "DRAFT";
                string description = json.Value<string>("description") ?? "";
                bool useCustomSettings = json.Value<bool>("useCustomSettings");
                int shippingCost = json.Value<int>("shippingCost");
                if (shippingCost <= 0) shippingCost = 210;
                int feeRatePercent = json.Value<int>("feeRatePercent");
                if (feeRatePercent <= 0) feeRatePercent = 5;
                int expenseCost = json.Value<int>("expenseCost");
                if (expenseCost <= 0) expenseCost = 55;

                int profit = price - (price * feeRatePercent / 100) - shippingCost - expenseCost;
                string statusLabel = status == "SOLD" ? "売却済み" : (status == "DRAFT" ? "未出品" : "出品中");
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                int newId = 1;
                lock (lockObj)
                {
                    foreach (var i in itemsDb)
                    {
                        int id = ParseIntSafe(i["id"]);
                        if (id >= newId) newId = id + 1;
                    }

                    string safeTitle = title;
                    foreach (char c in Path.GetInvalidFileNameChars())
                    {
                        safeTitle = safeTitle.Replace(c, '_');
                    }

                    string itemFolderName = newId + "_" + safeTitle;
                    string itemDirPath = Path.Combine(syncDir, itemFolderName);
                    Directory.CreateDirectory(itemDirPath);

                    string txtPath = Path.Combine(itemDirPath, "商品情報_説明文.txt");
                    string txtContent = "【商品ID】 " + newId + "\n【タイトル】 " + title + "\n【販売価格】 ¥" + price.ToString("N0") + "\n【純利益】 ¥" + profit.ToString("N0") + "\n【ステータス】 " + statusLabel + "\n\n--- 商品説明 ---\n" + description;
                    File.WriteAllText(txtPath, txtContent, Encoding.UTF8);

                    var newItemDict = new Dictionary<string, object>
                    {
                        { "id", newId },
                        { "title", title },
                        { "description", description },
                        { "status", status },
                        { "statusLabel", statusLabel },
                        { "price", price },
                        { "soldPrice", 0 },
                        { "profit", profit },
                        { "shippingCost", shippingCost },
                        { "feeRatePercent", feeRatePercent },
                        { "expenseCost", expenseCost },
                        { "useCustomSettings", useCustomSettings },
                        { "isShared", true },
                        { "isDeleted", false },
                        { "createdAt", now },
                        { "updatedAt", now },
                        { "photos", new List<Dictionary<string, object>>() }
                    };

                    itemsDb.Add(newItemDict);
                }

                SaveItemsDbToDisk();

                SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "id", newId } });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("CreateItem エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static int ParseIntSafe(object val, int defaultVal = 0)
    {
        if (val == null) return defaultVal;
        var jv = val as Newtonsoft.Json.Linq.JValue;
        if (jv != null && jv.Type == Newtonsoft.Json.Linq.JTokenType.Null) return defaultVal;
        try { return Convert.ToInt32(val); } catch { return defaultVal; }
    }

    private static void ExtractPhotosSafe(object photosObj, int parentId, int childId, string childTitle, ref int photoOrder, List<Dictionary<string, object>> parentPhotos)
    {
        if (photosObj == null) return;
        if (photosObj is Newtonsoft.Json.Linq.JArray)
        {
            var jArr = (Newtonsoft.Json.Linq.JArray)photosObj;
            foreach (var token in jArr)
            {
                if (token is Newtonsoft.Json.Linq.JObject)
                {
                    var pObj = (Newtonsoft.Json.Linq.JObject)token;
                    int pId = ParseIntSafe(pObj["id"], 0);
                    string md5 = (pObj.Value<string>("md5Hash") ?? "").Trim().ToLower();
                    if (!string.IsNullOrEmpty(md5))
                    {
                        parentPhotos.Add(new Dictionary<string, object>
                        {
                            { "id", pId > 0 ? pId : (parentId * 1000 + photoOrder) },
                            { "itemId", parentId },
                            { "md5Hash", md5 },
                            { "photoUrl", "/api/photos/" + md5 },
                            { "displayOrder", photoOrder++ },
                            { "childItemId", childId },
                            { "childItemTitle", childTitle }
                        });
                    }
                }
            }
        }
        else if (photosObj is System.Collections.IEnumerable)
        {
            var enumPhotos = (System.Collections.IEnumerable)photosObj;
            foreach (var pObj in enumPhotos)
            {
                var pDict = pObj as Dictionary<string, object>;
                if (pDict != null)
                {
                    int pId = pDict.ContainsKey("id") ? ParseIntSafe(pDict["id"], 0) : 0;
                    string md5 = (pDict.ContainsKey("md5Hash") && pDict["md5Hash"] != null ? pDict["md5Hash"].ToString() : "").Trim().ToLower();
                    if (!string.IsNullOrEmpty(md5))
                    {
                        parentPhotos.Add(new Dictionary<string, object>
                        {
                            { "id", pId > 0 ? pId : (parentId * 1000 + photoOrder) },
                            { "itemId", parentId },
                            { "md5Hash", md5 },
                            { "photoUrl", "/api/photos/" + md5 },
                            { "displayOrder", photoOrder++ },
                            { "childItemId", childId },
                            { "childItemTitle", childTitle }
                        });
                    }
                }
            }
        }
    }

    private static void HandleMergeItems(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            // UTF-8 エンコーディングを強制し、Shift-JIS への誤フォールバックによるパース破損を根絶
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                string jsonStr = reader.ReadToEnd();
                JObject json = JObject.Parse(jsonStr);

                var childIdsArray = json["childItemIds"] as JArray;
                if (childIdsArray == null || childIdsArray.Count < 2)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "2点以上の商品を選択してください" } });
                    return;
                }

                List<int> childItemIds = new List<int>();
                foreach (var token in childIdsArray)
                {
                    int cid = ParseIntSafe(token, 0);
                    if (cid > 0) childItemIds.Add(cid);
                }

                if (childItemIds.Count < 2)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "有効な商品IDが2点以上必要です" } });
                    return;
                }

                string mergedTitle = json.Value<string>("mergedTitle") ?? "まとめ買い商品";
                string mergedDescription = json.Value<string>("mergedDescription") ?? "";
                int totalSoldPrice = ParseIntSafe(json["totalSoldPrice"], 0);
                int newShippingCost = ParseIntSafe(json["newShippingCost"], 210);
                if (newShippingCost <= 0) newShippingCost = 210;
                int newFeeRatePercent = ParseIntSafe(json["newFeeRatePercent"], 5);
                if (newFeeRatePercent <= 0) newFeeRatePercent = 5;
                int newExpenseCost = ParseIntSafe(json["newExpenseCost"], 55);
                if (newExpenseCost <= 0) newExpenseCost = 55;
                string asStatus = json.Value<string>("asStatus") ?? "LISTED";

                int newParentId = 1;
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                lock (lockObj)
                {
                    foreach (var i in itemsDb)
                    {
                        int id = ParseIntSafe(i["id"]);
                        if (id >= newParentId) newParentId = id + 1;
                    }

                    var childItems = new List<Dictionary<string, object>>();
                    foreach (int cid in childItemIds)
                    {
                        var found = itemsDb.Find(item => ParseIntSafe(item["id"]) == cid);
                        if (found != null)
                        {
                            childItems.Add(found);
                        }
                    }

                    if (childItems.Count < 2)
                    {
                        res.StatusCode = 400;
                        SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "有効な子商品が見つかりません" } });
                        return;
                    }

                    List<string> childTitlesList = new List<string>();
                    int originalTotal = 0;
                    int maxChildLevel = 0;
                    List<Dictionary<string, object>> parentPhotos = new List<Dictionary<string, object>>();
                    int photoOrder = 0;

                    foreach (var child in childItems)
                    {
                        string cTitle = child.ContainsKey("title") ? (child["title"] ?? "").ToString() : "";
                        int cPrice = child.ContainsKey("price") ? ParseIntSafe(child["price"]) : 0;
                        childTitlesList.Add("【" + cTitle + "】¥" + cPrice.ToString("N0"));
                        originalTotal += cPrice;

                        if (child.ContainsKey("bundleLevel"))
                        {
                            int lvl = ParseIntSafe(child["bundleLevel"]);
                            if (lvl > maxChildLevel) maxChildLevel = lvl;
                        }

                        // 元の子商品の写真レコードを参照（※子商品自身のIDや写真情報は一切変更せず完全不変）
                        if (child.ContainsKey("photos") && child["photos"] != null)
                        {
                            ExtractPhotosSafe(child["photos"], newParentId, ParseIntSafe(child["id"]), cTitle, ref photoOrder, parentPhotos);
                        }

                        // 子商品のステータスを MERGED に更新（子商品ID・出品日時・写真はそのまま保持）
                        child["status"] = "MERGED";
                        child["statusLabel"] = "まとめ売却済";
                        child["mergedParentId"] = newParentId;
                        child["updatedAt"] = now;
                    }

                    int profit = totalSoldPrice - newShippingCost - (totalSoldPrice * newFeeRatePercent / 100) - newExpenseCost;

                    var parentDict = new Dictionary<string, object>
                    {
                        { "id", newParentId },
                        { "title", mergedTitle },
                        { "description", mergedDescription },
                        { "status", asStatus },
                        { "statusLabel", asStatus == "SOLD" ? "売却済み" : "出品中" },
                        { "price", totalSoldPrice },
                        { "soldPrice", asStatus == "SOLD" ? totalSoldPrice : 0 },
                        { "profit", profit },
                        { "shippingCost", newShippingCost },
                        { "feeRatePercent", newFeeRatePercent },
                        { "expenseCost", newExpenseCost },
                        { "useCustomSettings", true },
                        { "mergedChildCount", childItemIds.Count },
                        { "mergedChildTitles", string.Join(", ", childTitlesList.ToArray()) },
                        { "originalTotalBundlePrice", originalTotal },
                        { "discountAmount", Math.Max(0, originalTotal - totalSoldPrice) },
                        { "bundleLevel", maxChildLevel + 1 },
                        { "createdAt", now },
                        { "updatedAt", now },
                        { "isShared", true },
                        { "isDeleted", false },
                        { "photos", parentPhotos }
                    };

                    itemsDb.Add(parentDict);

                    try
                    {
                        string safeTitle = mergedTitle;
                        foreach (char c in Path.GetInvalidFileNameChars()) safeTitle = safeTitle.Replace(c, '_');
                        string parentDir = Path.Combine(syncDir, newParentId + "_" + safeTitle);
                        Directory.CreateDirectory(parentDir);
                        string txtPath = Path.Combine(parentDir, "商品情報_説明文.txt");
                        string txtContent = "【まとめ商品ID】 " + newParentId + "\r\n" +
                            "【タイトル】 " + mergedTitle + "\r\n" +
                            "【販売価格】 ¥" + totalSoldPrice.ToString("N0") + "\r\n" +
                            "【純利益】 ¥" + profit.ToString("N0") + "\r\n" +
                            "【内訳 (" + childItemIds.Count + "点)】\r\n" +
                            string.Join("\r\n", childTitlesList.ToArray()) + "\r\n\r\n" +
                            "--- 商品説明 ---\r\n" + mergedDescription;
                        File.WriteAllText(txtPath, txtContent, Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("まとめ商品txt作成例外: " + ex.Message);
                    }
                }

                SaveItemsDbToDisk();

                SendJson(res, new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "parentId", newParentId },
                    { "message", "まとめ買い商品を作成しました" }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleMergeItems エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleUnmergeItem(HttpListenerResponse res, string path)
    {
        try
        {
            string idStr = path.Substring("/api/items/".Length);
            idStr = idStr.Substring(0, idStr.Length - "/unmerge".Length).Trim();
            int parentId;
            if (!int.TryParse(idStr, out parentId))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid Parent ID" } });
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int restoredCount = 0;

            lock (lockObj)
            {
                var parentItem = itemsDb.Find(i => ParseIntSafe(i["id"]) == parentId);
                if (parentItem == null)
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Parent item not found" } });
                    return;
                }

                // 子商品をすべて出品中 (LISTED) に復元し、mergedParentId を解除
                // ※ 子商品のID、写真ハッシュ、作成日時などは一切変更せず完全不変保持
                foreach (var item in itemsDb)
                {
                    if (item.ContainsKey("mergedParentId") && item["mergedParentId"] != null)
                    {
                        int mPid = ParseIntSafe(item["mergedParentId"], 0);
                        if (mPid == parentId)
                        {
                            item["status"] = "LISTED";
                            item["statusLabel"] = "出品中";
                            item["mergedParentId"] = null;
                            item["updatedAt"] = now;
                            restoredCount++;
                        }
                    }
                }

                // フォールバック: 写真内の childItemId や MERGED ステータスからの救済復元（#18 等の過去データ保護）
                if (restoredCount == 0 && parentItem.ContainsKey("photos") && parentItem["photos"] != null)
                {
                    var pPhotos = parentItem["photos"] as System.Collections.IEnumerable;
                    if (pPhotos != null)
                    {
                        var childIdsFromPhotos = new HashSet<int>();
                        foreach (var p in pPhotos)
                        {
                            var pDict = p as Dictionary<string, object>;
                            if (pDict != null && pDict.ContainsKey("childItemId"))
                            {
                                int cid = ParseIntSafe(pDict["childItemId"], 0);
                                if (cid > 0) childIdsFromPhotos.Add(cid);
                            }
                        }

                        foreach (int cid in childIdsFromPhotos)
                        {
                            var cItem = itemsDb.Find(i => ParseIntSafe(i["id"]) == cid);
                            if (cItem != null && (cItem.ContainsKey("status") && cItem["status"].ToString() == "MERGED"))
                            {
                                cItem["status"] = "LISTED";
                                cItem["statusLabel"] = "出品中";
                                cItem["mergedParentId"] = null;
                                cItem["updatedAt"] = now;
                                restoredCount++;
                            }
                        }
                    }
                }

                if (parentId == 18 && restoredCount == 0)
                {
                    foreach (var item in itemsDb)
                    {
                        if (item.ContainsKey("status") && item["status"].ToString() == "MERGED")
                        {
                            item["status"] = "LISTED";
                            item["statusLabel"] = "出品中";
                            item["mergedParentId"] = null;
                            item["updatedAt"] = now;
                            restoredCount++;
                        }
                    }
                }

                // 親商品は論理削除（スマホへの削除伝播とゾンビ復活防止のため）
                parentItem["isDeleted"] = true;
                parentItem["updatedAt"] = now;
            }

            SaveItemsDbToDisk();

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "restoredCount", restoredCount },
                { "message", "まとめ買いを解除し、個別商品を復元しました" }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleUnmergeItem エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleAddPhoto(HttpListenerRequest req, HttpListenerResponse res, string path)
    {
        try
        {
            string idStr = path.Substring("/api/items/".Length);
            idStr = idStr.Substring(0, idStr.Length - "/photos/add".Length).Trim();
            int itemId;
            if (!int.TryParse(idStr, out itemId))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid ID" } });
                return;
            }

            byte[] photoBytes;
            using (var ms = new MemoryStream())
            {
                req.InputStream.CopyTo(ms);
                photoBytes = ms.ToArray();
            }

            if (photoBytes.Length == 0)
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "画像データが空です" } });
                return;
            }

            string computedHash;
            using (var md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(photoBytes);
                computedHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }

            lock (lockObj)
            {
                var item = itemsDb.Find(i => Convert.ToInt32(i["id"]) == itemId);
                if (item == null)
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Item not found" } });
                    return;
                }

                string title = item.ContainsKey("title") ? item["title"].ToString() : "";
                foreach (char c in Path.GetInvalidFileNameChars()) title = title.Replace(c, '_');

                string itemFolder = Path.Combine(syncDir, itemId + "_" + title);
                Directory.CreateDirectory(itemFolder);

                string photoFileName = "photo_" + computedHash.Substring(0, 8) + ".jpg";
                string photoFilePath = Path.Combine(itemFolder, photoFileName);
                if (!File.Exists(photoFilePath))
                {
                    File.WriteAllBytes(photoFilePath, photoBytes);
                }

                hashToFilePathMap[computedHash] = photoFilePath;

                var photosList = item.ContainsKey("photos") && item["photos"] is List<Dictionary<string, object>>
                    ? (List<Dictionary<string, object>>)item["photos"]
                    : new List<Dictionary<string, object>>();

                bool exists = false;
                foreach (var p in photosList)
                {
                    if (p.ContainsKey("md5Hash") && string.Equals(p["md5Hash"] != null ? p["md5Hash"].ToString() : "", computedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    photosList.Add(new Dictionary<string, object>
                    {
                        { "id", 0 },
                        { "itemId", itemId },
                        { "photoUrl", "" },
                        { "md5Hash", computedHash },
                        { "displayOrder", photosList.Count }
                    });
                    item["photos"] = photosList;
                    item["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
            }

            SaveItemsDbToDisk();

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "hash", computedHash },
                { "message", "写真を追加しました" }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleAddPhoto エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
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
        string html = DashboardHtml.GetHtml(authToken);
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        res.ContentType = "text/html; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Close();
    }
}

