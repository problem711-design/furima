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

            // 認証トークンの生成 (英数字12桁)
            authToken = Guid.NewGuid().ToString("N").Substring(0, 12);
            Console.WriteLine("[INFO] ワンタイム認証トークン: " + authToken);

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
                        itemsDb.RemoveAll(i => i != null && i.ContainsKey("id") && !activeIds.Contains(Convert.ToInt32(i["id"])));

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
                                { "photos", savedPhotosList }
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

public class TunnelQrForm : Form
{
    private Label lblStatus;
    private Label lblSyncDir;
    private Label lblStoreId;
    private TextBox txtLocalUrl;
    private TextBox txtTunnelUrl;
    private TextBox txtToken;
    private PictureBox picQrCode;
    private Label lblInfo;

    private NotifyIcon notifyIcon;
    private ContextMenuStrip trayMenu;
    private bool isExiting = false;
    private bool hasPromptedBrowser = false;

    private string currentStoreId;
    private string currentSyncDir;
    private string currentLocalUrl;
    private string currentTunnelUrl = "";
    private string currentToken;

    public TunnelQrForm(string storeId, string syncDir, string localUrl, string token)
    {
        this.currentStoreId = storeId;
        this.currentSyncDir = syncDir;
        this.currentLocalUrl = localUrl;
        this.currentToken = token;

        InitializeComponent();
        InitializeTrayIcon();
        UpdateDisplay();
    }

    private void InitializeComponent()
    {
        this.Text = "フリマ母艦サーバー - 接続情報 & QRコード";
        this.Size = new Size(520, 720);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Icon = SystemIcons.Application;

        TableLayoutPanel mainLayout = new TableLayoutPanel();
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Padding = new Padding(15);
        mainLayout.RowCount = 9;
        mainLayout.ColumnCount = 2;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        // タイトル / ステータス
        lblStatus = new Label();
        lblStatus.Text = "● サーバー稼働中 (接続待機中)";
        lblStatus.Font = new Font("Meiryo UI", 11F, FontStyle.Bold);
        lblStatus.ForeColor = Color.DarkGreen;
        lblStatus.AutoSize = true;
        mainLayout.Controls.Add(lblStatus, 0, 0);
        mainLayout.SetColumnSpan(lblStatus, 2);

        // 保存先
        Label l1 = new Label() { Text = "保存フォルダ:", Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F) };
        lblSyncDir = new Label() { Text = currentSyncDir, Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F, FontStyle.Regular) };
        mainLayout.Controls.Add(l1, 0, 1);
        mainLayout.Controls.Add(lblSyncDir, 1, 1);

        // Store ID
        Label l2 = new Label() { Text = "Store ID:", Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F) };
        lblStoreId = new Label() { Text = currentStoreId, Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 8.5F, FontStyle.Bold) };
        mainLayout.Controls.Add(l2, 0, 2);
        mainLayout.Controls.Add(lblStoreId, 1, 2);

        // ローカルURL
        Label l3 = new Label() { Text = "ローカル URL:", Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F) };
        txtLocalUrl = new TextBox() { Text = currentLocalUrl, ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
        mainLayout.Controls.Add(l3, 0, 3);
        mainLayout.Controls.Add(txtLocalUrl, 1, 3);

        // トンネルURL
        Label l4 = new Label() { Text = "トンネル URL:", Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F, FontStyle.Bold) };
        txtTunnelUrl = new TextBox() { Text = "取得中...", ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
        mainLayout.Controls.Add(l4, 0, 4);
        mainLayout.Controls.Add(txtTunnelUrl, 1, 4);

        // 認証トークン
        Label l5 = new Label() { Text = "ワンタイムトークン:", Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F) };
        txtToken = new TextBox() { Text = currentToken, ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F, FontStyle.Bold) };
        mainLayout.Controls.Add(l5, 0, 5);
        mainLayout.Controls.Add(txtToken, 1, 5);

        // QRコード画像
        picQrCode = new PictureBox();
        picQrCode.Size = new Size(240, 240);
        picQrCode.SizeMode = PictureBoxSizeMode.Zoom;
        picQrCode.Anchor = AnchorStyles.None;
        picQrCode.BorderStyle = BorderStyle.FixedSingle;
        mainLayout.Controls.Add(picQrCode, 0, 6);
        mainLayout.SetColumnSpan(picQrCode, 2);

        // インフォメーション
        lblInfo = new Label();
        lblInfo.Text = "スマホのフリマアプリ「PC連携」画面から上記QRコードを読み取ってください。";
        lblInfo.TextAlign = ContentAlignment.MiddleCenter;
        lblInfo.Dock = DockStyle.Fill;
        lblInfo.Font = new Font("Meiryo UI", 8.5F);
        mainLayout.Controls.Add(lblInfo, 0, 7);
        mainLayout.SetColumnSpan(lblInfo, 2);

        // 手動ブラウザ起動・トレイ格納ボタンエリア
        FlowLayoutPanel btnPanel = new FlowLayoutPanel();
        btnPanel.Dock = DockStyle.Fill;
        btnPanel.FlowDirection = FlowDirection.LeftToRight;
        btnPanel.Padding = new Padding(0, 5, 0, 0);

        Button btnOpenBrowser = new Button();
        btnOpenBrowser.Text = "🌐 PC管理画面を開く";
        btnOpenBrowser.Font = new Font("Meiryo UI", 9F, FontStyle.Bold);
        btnOpenBrowser.Size = new Size(160, 32);
        btnOpenBrowser.Click += (s, e) => { ShowBrowserSelectionDialog(); };
        btnPanel.Controls.Add(btnOpenBrowser);

        Button btnMinimizeTray = new Button();
        btnMinimizeTray.Text = "📥 タスクトレイへ格納";
        btnMinimizeTray.Font = new Font("Meiryo UI", 9F);
        btnMinimizeTray.Size = new Size(160, 32);
        btnMinimizeTray.Click += (s, e) => { MinimizeToTray(); };
        btnPanel.Controls.Add(btnMinimizeTray);

        mainLayout.Controls.Add(btnPanel, 0, 8);
        mainLayout.SetColumnSpan(btnPanel, 2);

        this.Controls.Add(mainLayout);
        this.FormClosing += TunnelQrForm_FormClosing;
    }

    private void InitializeTrayIcon()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("ウィンドウを表示(&S)", null, (s, e) => { RestoreFromTray(); });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("PC管理画面を開く (Edge)", null, (s, e) => { LaunchBrowser("edge"); });
        trayMenu.Items.Add("PC管理画面を開く (Chrome)", null, (s, e) => { LaunchBrowser("chrome"); });
        trayMenu.Items.Add("PC管理画面を開く (デフォルト)", null, (s, e) => { LaunchBrowser("default"); });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("母艦サーバーを終了(&X)", null, (s, e) => { ExitApp(); });

        notifyIcon = new NotifyIcon();
        notifyIcon.Text = "フリマ母艦サーバー (稼働中)";
        notifyIcon.Icon = SystemIcons.Application;
        notifyIcon.ContextMenuStrip = trayMenu;
        notifyIcon.Visible = true;
        notifyIcon.DoubleClick += (s, e) => { RestoreFromTray(); };
    }

    private void TunnelQrForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (!isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            MinimizeToTray();
        }
    }

    public void MinimizeToTray()
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action(MinimizeToTray));
            return;
        }

        this.Hide();
        notifyIcon.ShowBalloonTip(2000, "フリマ母艦サーバー", "バックグラウンドで動作中です。トレイアイコンのダブルクリックで再表示できます。", ToolTipIcon.Info);
    }

    public void RestoreFromTray()
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action(RestoreFromTray));
            return;
        }

        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.Activate();
    }

    public void ExitApp()
    {
        isExiting = true;
        if (notifyIcon != null)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }
        this.Close();
    }

    /// <summary>
    /// スマホアプリからの接続検知時に呼び出される処理
    /// </summary>
    public void OnAppConnected()
    {
        if (this.InvokeRequired)
        {
            this.BeginInvoke(new Action(OnAppConnected));
            return;
        }

        lblStatus.Text = "● スマホアプリ接続中";
        lblStatus.ForeColor = Color.DarkGreen;

        if (hasPromptedBrowser) return;
        hasPromptedBrowser = true;

        // ウィンドウを前面に復元
        RestoreFromTray();

        // ブラウザ選択ダイアログを表示し、選択後に自動トレイ格納
        ShowBrowserSelectionDialog();
    }

    public void ShowBrowserSelectionDialog()
    {
        using (BrowserSelectDialog dlg = new BrowserSelectDialog())
        {
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                LaunchBrowser(dlg.SelectedBrowser);
                MinimizeToTray();
            }
        }
    }

    public void LaunchBrowser(string browserType)
    {
        string url = currentLocalUrl;
        try
        {
            if (browserType == "edge")
            {
                try { Process.Start("msedge.exe", url); return; } catch { }
            }
            else if (browserType == "chrome")
            {
                try { Process.Start("chrome.exe", url); return; } catch { }
            }
            Process.Start(url);
        }
        catch (Exception ex)
        {
            MessageBox.Show("ブラウザ起動に失敗しました: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void UpdateTunnelUrl(string tunnelUrl)
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action<string>(UpdateTunnelUrl), tunnelUrl);
            return;
        }

        this.currentTunnelUrl = tunnelUrl;
        if (!string.IsNullOrEmpty(tunnelUrl))
        {
            txtTunnelUrl.Text = tunnelUrl;
            if (!lblStatus.Text.Contains("接続中"))
            {
                lblStatus.Text = "● トンネル接続完了 (QRコード準備完了)";
                lblStatus.ForeColor = Color.Blue;
            }
        }
        else
        {
            txtTunnelUrl.Text = "(トンネル接続失敗 - ローカル接続のみ)";
            if (!lblStatus.Text.Contains("接続中"))
            {
                lblStatus.Text = "● ローカル接続のみ可";
                lblStatus.ForeColor = Color.DarkOrange;
            }
        }

        UpdateDisplay();
    }

    public void UpdateStatusInfo(int indexedCount, int missingCount)
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action<int, int>(UpdateStatusInfo), indexedCount, missingCount);
            return;
        }

        lblInfo.Text = string.Format("スマホアプリ「PC連携」でQRを読込んでください。\n索引写真: {0}枚 | 原本欠落: {1}枚", indexedCount, missingCount);
    }

    private void UpdateDisplay()
    {
        string activeUrl = string.IsNullOrEmpty(currentTunnelUrl) ? currentLocalUrl : currentTunnelUrl;
        string qrText = string.Format("furima:{0}|{1}|{2}", activeUrl, currentToken, currentStoreId);

        try
        {
            Bitmap qrBmp = SimpleQrEncoder.GenerateQrBitmap(qrText, 240, 240);
            if (picQrCode.Image != null) picQrCode.Image.Dispose();
            picQrCode.Image = qrBmp;
        }
        catch (Exception ex)
        {
            Console.WriteLine("QRコード生成例外: " + ex.Message);
        }
    }
}

/// <summary>
/// スマホ接続検知時に起動ブラウザを選択させるダイアログフォーム
/// </summary>
public class BrowserSelectDialog : Form
{
    public string SelectedBrowser { get; private set; }

    public BrowserSelectDialog()
    {
        this.SelectedBrowser = "default";
        this.Text = "スマホ接続検知 - 管理画面ブラウザ選択";
        this.Size = new Size(420, 260);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ShowInTaskbar = false;

        TableLayoutPanel layout = new TableLayoutPanel();
        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(20);
        layout.RowCount = 4;
        layout.ColumnCount = 1;

        Label lblMsg = new Label();
        lblMsg.Text = "スマホアプリからの接続を確認しました！\nPC側で管理画面を開くブラウザを選択してください。";
        lblMsg.Font = new Font("Meiryo UI", 9.5F, FontStyle.Bold);
        lblMsg.ForeColor = Color.Navy;
        lblMsg.AutoSize = true;
        lblMsg.Dock = DockStyle.Fill;
        lblMsg.TextAlign = ContentAlignment.TopCenter;
        layout.Controls.Add(lblMsg);

        Button btnEdge = new Button();
        btnEdge.Text = "🌐 Microsoft Edge で開く";
        btnEdge.Font = new Font("Meiryo UI", 9.5F, FontStyle.Bold);
        btnEdge.Height = 36;
        btnEdge.Dock = DockStyle.Fill;
        btnEdge.Click += (s, e) => { SelectedBrowser = "edge"; this.DialogResult = DialogResult.OK; };
        layout.Controls.Add(btnEdge);

        Button btnChrome = new Button();
        btnChrome.Text = "🌐 Google Chrome で開く";
        btnChrome.Font = new Font("Meiryo UI", 9.5F, FontStyle.Bold);
        btnChrome.Height = 36;
        btnChrome.Dock = DockStyle.Fill;
        btnChrome.Click += (s, e) => { SelectedBrowser = "chrome"; this.DialogResult = DialogResult.OK; };
        layout.Controls.Add(btnChrome);

        Button btnDefault = new Button();
        btnDefault.Text = "💻 標準ブラウザで開く / キャンセル";
        btnDefault.Font = new Font("Meiryo UI", 9F);
        btnDefault.Height = 32;
        btnDefault.Dock = DockStyle.Fill;
        btnDefault.Click += (s, e) => { SelectedBrowser = "default"; this.DialogResult = DialogResult.OK; };
        layout.Controls.Add(btnDefault);

        this.Controls.Add(layout);
    }
}

/// <summary>
/// 外部DLL依存なしでQRコードBitmapを自前生成する軽量エンコーダクラス
/// </summary>
public static class SimpleQrEncoder
{
    public static Bitmap GenerateQrBitmap(string content, int width, int height)
    {
        bool[,] modules = EncodeToMatrix(content);
        int matrixSize = modules.GetLength(0);

        Bitmap bmp = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;

            float scale = Math.Min((float)width / (matrixSize + 4), (float)height / (matrixSize + 4));
            float offsetX = (width - matrixSize * scale) / 2f;
            float offsetY = (height - matrixSize * scale) / 2f;

            using (SolidBrush blackBrush = new SolidBrush(Color.Black))
            {
                for (int r = 0; r < matrixSize; r++)
                {
                    for (int c = 0; c < matrixSize; c++)
                    {
                        if (modules[r, c])
                        {
                            g.FillRectangle(blackBrush, offsetX + c * scale, offsetY + r * scale, scale + 0.5f, scale + 0.5f);
                        }
                    }
                }
            }
        }
        return bmp;
    }

    private static bool[,] EncodeToMatrix(string content)
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(content);
        int version = 4;
        if (dataBytes.Length > 62) version = 7;
        if (dataBytes.Length > 120) version = 10;

        int size = 17 + version * 4;
        bool[,] matrix = new bool[size, size];
        bool[,] reserved = new bool[size, size];

        PlaceFinderPattern(matrix, reserved, 0, 0);
        PlaceFinderPattern(matrix, reserved, size - 7, 0);
        PlaceFinderPattern(matrix, reserved, 0, size - 7);

        for (int i = 8; i < size - 8; i++)
        {
            matrix[6, i] = (i % 2 == 0);
            reserved[6, i] = true;
            matrix[i, 6] = (i % 2 == 0);
            reserved[i, 6] = true;
        }

        if (version >= 2)
        {
            int alignPos = size - 7;
            PlaceAlignmentPattern(matrix, reserved, alignPos - 2, alignPos - 2);
        }

        List<bool> bits = new List<bool>();
        bits.Add(false); bits.Add(true); bits.Add(false); bits.Add(false);

        int count = dataBytes.Length;
        for (int i = 7; i >= 0; i--) bits.Add(((count >> i) & 1) == 1);

        foreach (byte b in dataBytes)
        {
            for (int i = 7; i >= 0; i--) bits.Add(((b >> i) & 1) == 1);
        }

        for (int i = 0; i < 4; i++) bits.Add(false);

        int bitIdx = 0;
        int direction = -1;
        int x = size - 1;
        while (x > 0)
        {
            if (x == 6) x--;
            for (int y = (direction == -1 ? size - 1 : 0); y >= 0 && y < size; y += direction)
            {
                for (int col = x; col > x - 2; col--)
                {
                    if (!reserved[y, col])
                    {
                        bool val = false;
                        if (bitIdx < bits.Count)
                        {
                            val = bits[bitIdx++];
                        }
                        else
                        {
                            val = ((y + col) % 2 == 0);
                        }
                        matrix[y, col] = val;
                    }
                }
            }
            direction = -direction;
            x -= 2;
        }

        return matrix;
    }

    private static void PlaceFinderPattern(bool[,] matrix, bool[,] reserved, int top, int left)
    {
        for (int r = -1; r <= 7; r++)
        {
            for (int c = -1; c <= 7; c++)
            {
                int row = top + r;
                int col = left + c;
                if (row >= 0 && row < matrix.GetLength(0) && col >= 0 && col < matrix.GetLength(1))
                {
                    reserved[row, col] = true;
                    if (r >= 0 && r <= 6 && c >= 0 && c <= 6)
                    {
                        bool isBlack = (r == 0 || r == 6 || c == 0 || c == 6 || (r >= 2 && r <= 4 && c >= 2 && c <= 4));
                        matrix[row, col] = isBlack;
                    }
                    else
                    {
                        matrix[row, col] = false;
                    }
                }
            }
        }
    }

    private static void PlaceAlignmentPattern(bool[,] matrix, bool[,] reserved, int top, int left)
    {
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                int row = top + r;
                int col = left + c;
                if (row >= 0 && row < matrix.GetLength(0) && col >= 0 && col < matrix.GetLength(1))
                {
                    if (!reserved[row, col])
                    {
                        reserved[row, col] = true;
                        bool isBlack = (r == 0 || r == 4 || c == 0 || c == 4 || (r == 2 && c == 2));
                        matrix[row, col] = isBlack;
                    }
                }
            }
        }
    }
}
