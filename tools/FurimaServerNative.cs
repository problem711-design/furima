using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Security.Cryptography;

class FurimaServerNative
{
    private static string syncDir = "";
    private static string itemsJsonFile = "";
    private static DateTime lastHeartbeat = DateTime.MinValue;
    private static readonly object lockObj = new object();
    private static Dictionary<string, string> hashToFilePathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static List<Dictionary<string, object>> itemsDb = new List<Dictionary<string, object>>();

    static void Main(string[] args)
    {
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

            syncDir = Path.Combine(Directory.GetCurrentDirectory(), "出品写真_PC保存用");
            Directory.CreateDirectory(syncDir);
            itemsJsonFile = Path.Combine(syncDir, "items_data.json");

            Console.WriteLine("保存先フォルダ: " + syncDir);
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
                    var serializer = new JavaScriptSerializer();
                    serializer.MaxJsonLength = int.MaxValue;
                    var loaded = serializer.Deserialize<List<Dictionary<string, object>>>(json);
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
                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                string json = serializer.Serialize(itemsDb);
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
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");

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
                var dict = new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "appConnected", isConnected },
                    { "lastHeartbeatSec", (int)(DateTime.Now - lastHeartbeat).TotalSeconds },
                    { "indexedPhotosCount", hashToFilePathMap.Count },
                    { "totalItemsCount", itemsDb.Count }
                };
                SendJson(res, dict);
            }
            else if (rawPath == "/api/server/heartbeat")
            {
                lastHeartbeat = DateTime.Now;
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] スマホアプリからの接続ハートビートを受信しました");
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
            else if (rawPath == "/api/sync/push" && req.HttpMethod == "POST")
            {
                lastHeartbeat = DateTime.Now;
                HandleSyncPush(req, res);
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

    private static void HandleSyncPush(HttpListenerRequest req, HttpListenerResponse res)
    {
        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] HandleSyncPush 開始 (ContentLength=" + req.ContentLength64 + ")");
        try
        {
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
            {
                string json = reader.ReadToEnd();
                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                var data = serializer.Deserialize<Dictionary<string, object>>(json);

                if (data != null && data.ContainsKey("items"))
                {
                    var itemsList = data["items"] as System.Collections.IEnumerable;
                    if (itemsList != null)
                    {
                        List<string> newlySavedHashes = new List<string>();
                        List<object> list = new List<object>();
                        foreach (var itm in itemsList) list.Add(itm);
                        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] スマホアプリからPUSH同期データを受信しました (対象商品: " + list.Count + "件)");

                        lock (lockObj)
                        {
                            foreach (var itemObj in list)
                            {
                                var item = itemObj as Dictionary<string, object>;
                                if (item == null) continue;

                                int id = Convert.ToInt32(item["id"]);
                                string title = item.ContainsKey("title") && item["title"] != null ? item["title"].ToString() : "商品";
                                string description = item.ContainsKey("description") && item["description"] != null ? item["description"].ToString() : "";
                                int price = item.ContainsKey("price") ? Convert.ToInt32(item["price"]) : 0;
                                int profit = item.ContainsKey("profit") ? Convert.ToInt32(item["profit"]) : 0;
                                string statusLabel = item.ContainsKey("statusLabel") && item["statusLabel"] != null ? item["statusLabel"].ToString() : "";

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

                                // 写真データ (Base64) の保存処理
                                List<Dictionary<string, object>> savedPhotosList = new List<Dictionary<string, object>>();

                                if (item.ContainsKey("photos") && item["photos"] != null)
                                {
                                    var photosList = item["photos"] as System.Collections.IEnumerable;
                                    if (photosList != null)
                                    {
                                        int pIdx = 1;
                                        foreach (var photoObj in photosList)
                                        {
                                            var photo = photoObj as Dictionary<string, object>;
                                            if (photo == null) continue;

                                            string md5Hash = photo.ContainsKey("md5Hash") && photo["md5Hash"] != null ? photo["md5Hash"].ToString() : "";
                                            string base64Data = photo.ContainsKey("base64Data") && photo["base64Data"] != null ? photo["base64Data"].ToString() : "";

                                            if (!string.IsNullOrEmpty(base64Data))
                                            {
                                                byte[] photoBytes = Convert.FromBase64String(base64Data);
                                                string photoFileName = "photo_" + pIdx.ToString("00") + ".jpg";
                                                string photoPath = Path.Combine(itemDirPath, photoFileName);

                                                File.WriteAllBytes(photoPath, photoBytes);

                                                if (!string.IsNullOrEmpty(md5Hash))
                                                {
                                                    hashToFilePathMap[md5Hash] = photoPath;
                                                    newlySavedHashes.Add(md5Hash);
                                                }
                                            }

                                            var photoDict = new Dictionary<string, object>
                                            {
                                                { "id", photo.ContainsKey("id") ? photo["id"] : pIdx },
                                                { "md5Hash", md5Hash },
                                                { "photoUrl", "/api/photos/" + md5Hash }
                                            };
                                            savedPhotosList.Add(photoDict);
                                            pIdx++;
                                        }
                                    }
                                }

                                // DB登録または更新
                                var existingItem = itemsDb.Find(i => Convert.ToInt32(i["id"]) == id);
                                var newItemDict = new Dictionary<string, object>
                                {
                                    { "id", id },
                                    { "title", title },
                                    { "description", description },
                                    { "status", item.ContainsKey("status") ? item["status"] : "LISTED" },
                                    { "price", price },
                                    { "soldPrice", item.ContainsKey("soldPrice") ? item["soldPrice"] : 0 },
                                    { "profit", profit },
                                    { "statusLabel", statusLabel },
                                    { "isShared", true },
                                    { "photos", savedPhotosList }
                                };

                                if (existingItem != null)
                                {
                                    itemsDb.Remove(existingItem);
                                }
                                itemsDb.Add(newItemDict);
                                Console.WriteLine("  -> 保存完了: [" + id + "] " + title + " (写真: " + savedPhotosList.Count + "枚)");
                            }

                            // 双方向クリーンアップ (Garbage Collection): スマホ側に存在しなくなった古いフォルダをPC側から自動削除
                            if (data.ContainsKey("activeFolderNames") && data["activeFolderNames"] != null)
                            {
                                var activeList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                var activeFolders = data["activeFolderNames"] as System.Collections.IEnumerable;
                                if (activeFolders != null)
                                {
                                    foreach (var nameObj in activeFolders)
                                    {
                                        if (nameObj != null) activeList.Add(nameObj.ToString());
                                    }
                                }

                                if (Directory.Exists(syncDir))
                                {
                                    foreach (var subDir in Directory.GetDirectories(syncDir))
                                    {
                                        string dirName = Path.GetFileName(subDir);
                                        if (!activeList.Contains(dirName))
                                        {
                                            try
                                            {
                                                Directory.Delete(subDir, true);
                                                Console.WriteLine("  -> [クリーンアップ] 不要になった古いフォルダを削除しました: " + dirName);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine("  -> [クリーンアップ警告] フォルダ削除失敗 (" + dirName + "): " + ex.Message);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        SaveItemsDbToDisk();

                        Console.WriteLine("-------------------------------------------------");
                        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] 全データ同期完了: " + list.Count + "件の商品, " + newlySavedHashes.Count + "枚の新規写真を受信蓄積しました");
                        Console.WriteLine("-------------------------------------------------");

                        SendJson(res, new Dictionary<string, object>
                        {
                            { "status", "ok" },
                            { "syncedHashes", newlySavedHashes }
                        });
                        return;
                    }
                }
            }

            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "message", "No new items" } });
        }
        catch (Exception ex)
        {
            Console.WriteLine("差分PUSH同期エラー: " + ex.Message);
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
        var serializer = new JavaScriptSerializer();
        string json = serializer.Serialize(obj);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Close();
    }

    private static void ServeDashboardHtml(HttpListenerResponse res)
    {
        string html = @"<!DOCTYPE html>
<html lang='ja'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Remix フリマ出品・商品PC管理 (母艦PCサーバー)</title>
    <script src='https://cdn.tailwindcss.com'></script>
    <link href='https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css' rel='stylesheet'>
    <style>
        .custom-scroll::-webkit-scrollbar { width: 6px; height: 6px; }
        .custom-scroll::-webkit-scrollbar-track { background: #f1f5f9; }
        .custom-scroll::-webkit-scrollbar-thumb { background: #cbd5e1; border-radius: 3px; }
    </style>
</head>
<body class='bg-slate-50 text-slate-800 min-h-screen'>
    <!-- スマホ接続ステータス通知バナー -->
    <div id='status-banner' class='bg-amber-500 text-white px-4 py-2 text-xs font-bold flex items-center justify-between shadow-sm transition-all duration-300 sticky top-0 z-40'>
        <div class='flex items-center gap-2'>
            <i id='status-icon' class='fa-solid fa-triangle-exclamation text-base animate-bounce'></i>
            <span id='status-text'>スマホアプリ未接続（操作・編集ロック中）。スマホアプリの「PC連携」画面から接続を開始してください。</span>
        </div>
        <span class='bg-amber-700/60 px-2 py-0.5 rounded text-[11px] font-mono'>v2.1 (Build 8)</span>
    </div>

    <!-- ナビゲーションバー (スリム＆インライン統合) -->
    <header class='bg-indigo-600 text-white shadow-md sticky top-8 z-30'>
        <div class='max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-14 flex items-center justify-between gap-4'>
            <!-- 左側: タイトル & 接続ステータス -->
            <div class='flex items-center space-x-2.5 shrink-0'>
                <i class='fa-solid fa-boxes-stacked text-xl text-indigo-200'></i>
                <h1 class='font-bold text-base tracking-tight whitespace-nowrap'>フリマ出品・商品PC管理</h1>
                <span class='bg-indigo-700/80 text-indigo-100 text-[11px] px-2 py-0.5 rounded font-mono font-semibold border border-indigo-500'>母艦サーバー</span>
            </div>

            <!-- 中央・右側: ステータスフィルター & 検索ボックス -->
            <div class='flex items-center gap-2.5 flex-1 justify-end overflow-x-auto custom-scroll py-1'>
                <div class='flex items-center gap-1 bg-indigo-700/60 p-1 rounded-lg shrink-0'>
                    <button onclick='setFilter(""ALL"")' id='filter-all' class='filter-btn px-2.5 py-1 text-xs font-bold rounded-md bg-white text-indigo-700 shadow-sm transition'>すべて (<span id='count-all'>0</span>)</button>
                    <button onclick='setFilter(""LISTED"")' id='filter-listed' class='filter-btn px-2.5 py-1 text-xs font-medium rounded-md hover:bg-indigo-500/50 text-indigo-100 transition'>出品中 (<span id='count-listed'>0</span>)</button>
                    <button onclick='setFilter(""UNSHARED"")' id='filter-unshared' class='filter-btn px-2.5 py-1 text-xs font-medium rounded-md hover:bg-indigo-500/50 text-indigo-100 transition'>未送信 (<span id='count-unshared'>0</span>)</button>
                    <button onclick='setFilter(""SOLD"")' id='filter-sold' class='filter-btn px-2.5 py-1 text-xs font-medium rounded-md hover:bg-indigo-500/50 text-indigo-100 transition'>売却済 (<span id='count-sold'>0</span>)</button>
                </div>

                <div class='relative w-44 lg:w-60 shrink-0'>
                    <i class='fa-solid fa-magnifying-glass absolute left-2.5 top-2 text-indigo-300 text-xs'></i>
                    <input type='text' id='search-input' oninput='renderItems()' placeholder='商品名・説明文で検索...' class='w-full pl-8 pr-2 py-1 text-xs bg-indigo-700/80 text-white placeholder-indigo-300 border border-indigo-400/50 rounded-lg focus:outline-none focus:bg-indigo-800 focus:ring-1 focus:ring-white'>
                </div>
            </div>
        </div>
    </header>

    <!-- メインコンテンツ -->
    <main class='max-w-[1600px] mx-auto px-4 sm:px-6 lg:px-8 py-6'>
        <div class='flex flex-col lg:flex-row gap-6 items-start'>
            <!-- 左側: メイン商品グリッド表示エリア -->
            <div class='flex-1 w-full min-w-0'>
                <div id='items-grid' class='grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 gap-4'>
                    <!-- JavaScriptで動的生成 -->
                </div>

                <div id='empty-state' class='hidden text-center py-16 bg-white rounded-xl border border-slate-200'>
                    <i class='fa-solid fa-box-open text-4xl text-slate-300 mb-3'></i>
                    <p class='text-slate-500 font-medium'>該当する商品が見つかりません</p>
                </div>
            </div>

            <!-- 右側: 固定コントロールパネル -->
            <aside class='w-full lg:w-72 xl:w-80 shrink-0 sticky top-28 space-y-4'>
                <div class='bg-white rounded-xl p-4 shadow-sm border border-slate-200 space-y-3'>
                    <h2 class='text-xs font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1.5'>
                        <i class='fa-solid fa-chart-pie text-indigo-500'></i> 実績分析サマリー
                    </h2>
                    <div class='grid grid-cols-2 gap-2.5'>
                        <div class='bg-blue-50/70 p-2.5 rounded-lg border border-blue-100'>
                            <p class='text-[11px] font-medium text-slate-500'>出品中の商品</p>
                            <p id='stat-listed-count' class='text-lg font-bold text-blue-900'>0品</p>
                        </div>
                        <div class='bg-emerald-50/70 p-2.5 rounded-lg border border-emerald-100'>
                            <p class='text-[11px] font-medium text-slate-500'>売却済みの商品</p>
                            <p id='stat-sold-count' class='text-lg font-bold text-emerald-900'>0品</p>
                        </div>
                    </div>
                    <div class='space-y-2 pt-1'>
                        <div class='bg-slate-50 p-3 rounded-lg border border-slate-100 flex items-center justify-between'>
                            <span class='text-xs text-slate-600 font-medium flex items-center gap-1.5'>
                                <i class='fa-solid fa-coins text-indigo-500'></i> 累計売上高
                            </span>
                            <span id='stat-total-sales' class='text-base font-bold text-indigo-600'>¥0</span>
                        </div>
                        <div class='bg-amber-50/60 p-3 rounded-lg border border-amber-100 flex items-center justify-between'>
                            <span class='text-xs text-slate-600 font-medium flex items-center gap-1.5'>
                                <i class='fa-solid fa-hand-holding-dollar text-amber-500'></i> 累計純利益
                            </span>
                            <span id='stat-total-profit' class='text-base font-bold text-amber-600'>¥0</span>
                        </div>
                    </div>
                </div>

                <div class='bg-white rounded-xl p-4 shadow-sm border border-slate-200 space-y-3'>
                    <h2 class='text-xs font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1.5'>
                        <i class='fa-solid fa-sliders text-indigo-500'></i> コントロールパネル
                    </h2>
                    <div class='space-y-2'>
                        <button onclick='fetchItems()' class='w-full bg-slate-100 hover:bg-slate-200 text-slate-700 font-medium py-2 px-3 rounded-xl text-xs flex items-center justify-center gap-2 transition'>
                            <i class='fa-solid fa-rotate-right text-sm'></i> データ手動更新
                        </button>
                    </div>
                </div>
            </aside>
        </div>
    </main>

    <!-- 画像拡大モーダル -->
    <div id='image-viewer-modal' class='hidden fixed inset-0 bg-black/80 z-50 flex items-center justify-center p-4' onclick='closeImageViewer()'>
        <div class='max-w-4xl max-h-[90vh] relative flex flex-col items-center gap-3' onclick='event.stopPropagation()'>
            <button onclick='closeImageViewer()' class='absolute -top-10 right-0 text-white hover:text-slate-300 text-2xl'>
                <i class='fa-solid fa-xmark'></i>
            </button>
            <img id='image-viewer-src' src='' alt='商品写真' class='max-w-full max-h-[80vh] rounded-lg shadow-2xl object-contain'>
        </div>
    </div>

    <!-- トースト通知 -->
    <div id='toast' class='fixed bottom-6 right-6 bg-slate-900 text-white px-4 py-2.5 rounded-xl shadow-xl text-sm font-medium transition-all transform translate-y-16 opacity-0 z-50 flex items-center gap-2'>
        <i class='fa-solid fa-circle-check text-emerald-400'></i>
        <span id='toast-message'></span>
    </div>

    <script>
        let allItems = [];
        let currentFilter = 'ALL';

        document.addEventListener('DOMContentLoaded', () => {
            fetchItems();
            setInterval(fetchItems, 3000);
            setInterval(checkServerStatus, 2500);
            checkServerStatus();
        });

        async function checkServerStatus() {
            try {
                const res = await fetch('/api/server/status');
                const data = await res.json();
                const banner = document.getElementById('status-banner');
                const statusText = document.getElementById('status-text');
                const statusIcon = document.getElementById('status-icon');

                if (data.appConnected) {
                    banner.className = 'bg-emerald-600 text-white px-4 py-2 text-xs font-bold flex items-center justify-between shadow-sm transition-all duration-300 sticky top-0 z-40';
                    statusIcon.className = 'fa-solid fa-circle-check text-base animate-pulse';
                    statusText.innerText = 'スマホアプリ接続中（リアルタイム動相同期が解禁されています）';
                } else {
                    banner.className = 'bg-amber-500 text-white px-4 py-2 text-xs font-bold flex items-center justify-between shadow-sm transition-all duration-300 sticky top-0 z-40';
                    statusIcon.className = 'fa-solid fa-triangle-exclamation text-base animate-bounce';
                    statusText.innerText = 'スマホアプリ未接続（読み取り専用モード）。スマホアプリの「PC連携」から接続を開始してください。';
                }
            } catch (e) { console.error(e); }
        }

        async function fetchItems() {
            try {
                const res = await fetch('/api/items');
                if (!res.ok) return;
                allItems = await res.json();
                updateStats();
                renderItems();
            } catch (err) {
                console.error(err);
            }
        }

        function updateStats() {
            const listed = allItems.filter(i => i.status === 'LISTED');
            const unshared = allItems.filter(i => !i.isShared);
            const sold = allItems.filter(i => i.status === 'SOLD');
            const totalSales = sold.reduce((sum, i) => sum + (i.soldPrice > 0 ? i.soldPrice : i.price), 0);
            const totalProfit = sold.reduce((sum, i) => sum + (i.profit || 0), 0);

            document.getElementById('stat-listed-count').innerText = listed.length + '品';
            document.getElementById('stat-sold-count').innerText = sold.length + '品';
            document.getElementById('stat-total-sales').innerText = '¥' + totalSales.toLocaleString();
            document.getElementById('stat-total-profit').innerText = '¥' + totalProfit.toLocaleString();

            document.getElementById('count-all').innerText = allItems.length;
            document.getElementById('count-listed').innerText = listed.length;
            document.getElementById('count-unshared').innerText = unshared.length;
            document.getElementById('count-sold').innerText = sold.length;
        }

        function setFilter(filter) {
            currentFilter = filter;
            document.querySelectorAll('.filter-btn').forEach(btn => {
                btn.className = 'filter-btn px-2.5 py-1 text-xs font-medium rounded-md hover:bg-indigo-500/50 text-indigo-100 transition';
            });
            if (filter === 'ALL') document.getElementById('filter-all').className = 'filter-btn px-2.5 py-1 text-xs font-bold rounded-md bg-white text-indigo-700 shadow-sm transition';
            if (filter === 'LISTED') document.getElementById('filter-listed').className = 'filter-btn px-2.5 py-1 text-xs font-bold rounded-md bg-white text-indigo-700 shadow-sm transition';
            if (filter === 'UNSHARED') document.getElementById('filter-unshared').className = 'filter-btn px-2.5 py-1 text-xs font-bold rounded-md bg-white text-amber-700 shadow-sm transition';
            if (filter === 'SOLD') document.getElementById('filter-sold').className = 'filter-btn px-2.5 py-1 text-xs font-bold rounded-md bg-white text-emerald-700 shadow-sm transition';
            renderItems();
        }

        function renderItems() {
            const grid = document.getElementById('items-grid');
            const emptyState = document.getElementById('empty-state');
            const search = document.getElementById('search-input').value.toLowerCase().trim();

            let items = allItems.filter(item => {
                if (currentFilter === 'LISTED' && item.status !== 'LISTED') return false;
                if (currentFilter === 'UNSHARED' && item.isShared) return false;
                if (currentFilter === 'SOLD' && item.status !== 'SOLD') return false;
                if (search) {
                    const matchTitle = (item.title || '').toLowerCase().includes(search);
                    const matchDesc = (item.description || '').toLowerCase().includes(search);
                    if (!matchTitle && !matchDesc) return false;
                }
                return true;
            });

            if (items.length === 0) {
                grid.innerHTML = '';
                emptyState.classList.remove('hidden');
                return;
            }

            emptyState.classList.add('hidden');
            grid.innerHTML = items.map(item => {
                const photos = item.photos || [];
                const firstPhoto = photos.length > 0 ? (photos[0].photoUrl || '/api/photos/' + photos[0].md5Hash) : '';
                const statusBadge = item.status === 'SOLD'
                    ? '<span class=""bg-emerald-100 text-emerald-800 text-[10px] font-bold px-2 py-0.5 rounded-full"">売却済</span>'
                    : '<span class=""bg-blue-100 text-blue-800 text-[10px] font-bold px-2 py-0.5 rounded-full"">' + (item.statusLabel || '出品中') + '</span>';

                return `
                <div class=""bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden hover:shadow-md transition flex flex-col"">
                    <div class=""relative h-48 bg-slate-100 cursor-pointer overflow-hidden group"" onclick=""openImageViewer('${firstPhoto}')"">
                        ${firstPhoto ? `<img src=""${firstPhoto}"" class=""w-full h-full object-cover group-hover:scale-105 transition duration-300"">` : '<div class=""w-full h-full flex items-center justify-center text-slate-300""><i class=""fa-solid fa-image text-3xl""></i></div>'}
                        <div class=""absolute top-2 left-2 flex gap-1"">${statusBadge}</div>
                        <div class=""absolute bottom-2 right-2 bg-black/60 text-white text-[10px] px-2 py-0.5 rounded-full font-mono"">
                            <i class=""fa-solid fa-camera""></i> ${photos.length}
                        </div>
                    </div>
                    <div class=""p-3.5 flex-1 flex flex-col justify-between"">
                        <div>
                            <h3 class=""font-bold text-sm text-slate-800 line-clamp-2 mb-1"">${escapeHtml(item.title || '')}</h3>
                            <p class=""text-xs text-slate-500 line-clamp-2 mb-2 font-light"">${escapeHtml(item.description || '')}</p>
                        </div>
                        <div class=""pt-2 border-t border-slate-100 flex items-center justify-between"">
                            <div>
                                <span class=""text-[10px] text-slate-400 block font-medium"">販売価格</span>
                                <span class=""text-base font-bold text-indigo-600"">¥${(item.price || 0).toLocaleString()}</span>
                            </div>
                            <div class=""text-right"">
                                <span class=""text-[10px] text-slate-400 block font-medium"">想定純利益</span>
                                <span class=""text-xs font-bold text-amber-600"">¥${(item.profit || 0).toLocaleString()}</span>
                            </div>
                        </div>
                    </div>
                </div>`;
            }).join('');
        }

        function escapeHtml(str) {
            return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/""/g, '&quot;');
        }

        function openImageViewer(src) {
            if (!src) return;
            document.getElementById('image-viewer-src').src = src;
            document.getElementById('image-viewer-modal').classList.remove('hidden');
        }

        function closeImageViewer() {
            document.getElementById('image-viewer-modal').classList.add('hidden');
        }

        function showToast(msg) {
            const toast = document.getElementById('toast');
            document.getElementById('toast-message').innerText = msg;
            toast.classList.remove('translate-y-16', 'opacity-0');
            setTimeout(() => { toast.classList.add('translate-y-16', 'opacity-0'); }, 3000);
        }
    </script>
</body>
</html>";

        byte[] bytes = Encoding.UTF8.GetBytes(html);
        res.ContentType = "text/html; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Close();
    }
}
