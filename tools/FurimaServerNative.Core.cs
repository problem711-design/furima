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
using System.Drawing.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal partial class FurimaServerNative
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
    private static HttpListener activeListener = null;
    private static volatile bool isShuttingDown = false;
    private static int cleanupExecuted = 0;
    private static ServerLogWriter logWriter = null;
    private static long dataRevision = 1;
    private static long settingsRevision = 1;
    private static long lastClaudeWatchContactAt = 0;
    private static readonly string serverBootId = Guid.NewGuid().ToString("N").Substring(0, 8);
    private static string activeListenerMode = "unknown";
    private static int activeActualPort = 0;
    private static long lastFullPhotoScanTicks = 0;
    private static long lastExpirySweepTicks = 0;
    private static long lastMissingCountTicks = 0;
    private static int cachedMissingCount = 0;
    private static int missingCountDirty = 1;
    private static int missingRefreshInProgress = 0;
    // claudeRevision の実体は ClaudeJobStore 側で永続化する (フェーズ 6-1a)。
    // サーバー再起動後も単調増加を引き継ぐため、メモリ保持にはしない。
    // Build 13 (13-2): 結果・エラーの登録を直列化するロック。
    // ガードB: 必ず最外層にのみ置く (claudeResultWriteLock → lockObj → _jobLock の順)。
    // lockObj や _jobLock を保持した内側からこのロックを取得する経路は作らないこと。
    private static readonly object claudeResultWriteLock = new object();

    private class PhotoFileCacheEntry
    {
        public long length;
        public long lastWriteUtcTicks;
        public string md5;
    }

    private class PendingAutoImportPhoto
    {
        public string filePath;
        public int itemId;
        public string md5;
        public long length;
        public long lastWriteUtcTicks;
    }

    private static Dictionary<string, PhotoFileCacheEntry> photoStatCache = new Dictionary<string, PhotoFileCacheEntry>(StringComparer.OrdinalIgnoreCase);
    private static FileSystemWatcher folderWatcher = null;
    private static System.Threading.Timer watcherFlushTimer = null;
    private static readonly object watcherQueueLock = new object();
    private static readonly Dictionary<string, long> pendingWatchFiles = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> pendingWatchRetries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private static int watcherFlushRunning = 0;
    private static int watcherRescanRunning = 0;

    private class ServerLogWriter : TextWriter
    {
        private readonly object writeLock = new object();
        private StreamWriter fileWriter;
        private readonly StringBuilder lineBuffer = new StringBuilder();

        public ServerLogWriter(string logPath)
        {
            try
            {
                if (File.Exists(logPath))
                {
                    FileInfo fi = new FileInfo(logPath);
                    if (fi.Length > 2 * 1024 * 1024)
                    {
                        try { File.Delete(logPath); } catch { }
                    }
                }
                FileStream fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                fileWriter = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch
            {
                fileWriter = null;
            }
        }

        public override Encoding Encoding { get { return Encoding.UTF8; } }

        public override void Write(char value)
        {
            lock (writeLock)
            {
                if (fileWriter == null) return;
                if (value == '\r') return;
                if (value == '\n')
                {
                    FlushLineLocked();
                }
                else
                {
                    lineBuffer.Append(value);
                }
            }
        }

        public override void WriteLine(string value)
        {
            lock (writeLock)
            {
                if (fileWriter == null) return;
                if (lineBuffer.Length > 0)
                {
                    lineBuffer.Append(value ?? "");
                    FlushLineLocked();
                }
                else
                {
                    try
                    {
                        fileWriter.WriteLine(DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss] ") + (value ?? ""));
                    }
                    catch { }
                }
            }
        }

        private void FlushLineLocked()
        {
            try
            {
                fileWriter.WriteLine(DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss] ") + lineBuffer.ToString());
            }
            catch { }
            lineBuffer.Length = 0;
        }

        protected override void Dispose(bool disposing)
        {
            lock (writeLock)
            {
                if (disposing && fileWriter != null)
                {
                    if (lineBuffer.Length > 0) FlushLineLocked();
                    try { fileWriter.Flush(); } catch { }
                    try { fileWriter.Dispose(); } catch { }
                    fileWriter = null;
                }
            }
            base.Dispose(disposing);
        }
    }

    private static long BumpDataRevision()
    {
        Interlocked.Exchange(ref missingCountDirty, 1);
        return Interlocked.Increment(ref dataRevision);
    }

    private static long BumpSettingsRevision()
    {
        return Interlocked.Increment(ref settingsRevision);
    }

    private static long GetDataRevision()
    {
        return Interlocked.Read(ref dataRevision);
    }

    private static long GetSettingsRevision()
    {
        return Interlocked.Read(ref settingsRevision);
    }

    private static long BumpClaudeRevision()
    {
        return ClaudeJobStore.IncrementRevision();
    }

    private static long GetClaudeRevision()
    {
        return ClaudeJobStore.GetRevision();
    }

    internal static long GetLastClaudeWatchContactAt()
    {
        return Interlocked.Read(ref lastClaudeWatchContactAt);
    }

    internal static void RecordClaudeWatchContact()
    {
        Interlocked.Exchange(ref lastClaudeWatchContactAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    internal static void RecordClaudePatrolContact()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SyncStore.SetLastClaudePatrolContactAt(now);
    }

    private static string EnsureItemSyncUuid(Dictionary<string, object> item)
    {
        if (item == null) return "";
        if (item.ContainsKey("syncUuid") && item["syncUuid"] != null)
        {
            string existing = item["syncUuid"].ToString().Trim();
            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }
        }
        string created = Guid.NewGuid().ToString();
        item["syncUuid"] = created;
        return created;
    }



    [STAThread]
    static void Main(string[] args)
    {
        // 二重起動防止 (名前付き Mutex)
        bool createdNew;
        mutex = new Mutex(true, "FurimaServerNative_SingleInstance_Mutex", out createdNew);
        if (!createdNew)
        {
            try { mutex.Close(); } catch { }
            mutex = null;
            MessageBox.Show("母艦サーバーは既に起動しています。二重起動を防止するため終了します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            logWriter = new ServerLogWriter(Path.Combine(baseDir, "server.log"));
            Console.SetOut(logWriter);
            Console.SetError(logWriter);

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
            SyncStore.Initialize(baseDir, ref authToken);
            // プロセスがサーバーフォルダのディレクトリハンドルをロックし続けないようカレントディレクトリを退避
            try { Environment.CurrentDirectory = Path.GetTempPath(); } catch { }
            syncDir = SyncStore.SyncDir;
            itemsJsonFile = Path.Combine(syncDir, "items_data.json");

            Console.WriteLine("[INFO] ワンタイム認証トークン: " + authToken);

            Console.WriteLine("[INFO] 保存フォルダ: " + syncDir + " (storeId: " + SyncStore.StoreId + ")");

            Console.WriteLine("既存写真のMD5ハッシュをインデックス化中...");
            ScanAndIndexLocalPhotos();

            Console.WriteLine("商品メタデータ (items_data.json) をロード中...");
            LoadItemsDbFromDisk();

            ClaudeJobStore.CleanupOnStartup();
            if (ClaudeJobStore.WakeOnStartupIfExecutable())
            {
                Console.WriteLine("[INFO] 実行可能な Muse ジョブが残っているため起動時ウェイクを発行しました (claudeRevision=" + GetClaudeRevision() + ")");
            }
            SweepReferenceOrphans();

            StartFolderAutoImportWatcher();

            HttpListener listener = null;
            int actualPort = desiredPort;
            string boundUrl = "";
            string listenerMode = "none";

            for (int pNum = desiredPort; pNum < desiredPort + 50; pNum++)
            {
                // P5: 既定ポート (8080等) でのクイック再起動時に直前プロセスのソケット解放を短時間待つ (300ms x 5回)
                int maxAttempts = (pNum == desiredPort) ? 5 : 1;
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    listener = TryStartHttpListener(pNum, out boundUrl, out listenerMode);
                    if (listener != null) break;
                    if (attempt + 1 < maxAttempts) Thread.Sleep(300);
                }
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

            activeListenerMode = listenerMode;
            activeActualPort = actualPort;

            Console.WriteLine("-------------------------------------------------");
            Console.WriteLine(" サーバーが正常起動しました: " + boundUrl);
            if (listenerMode == "all")
            {
                Console.WriteLine(" 待ち受けモード: 全アドレス許可 (http://+:" + actualPort + "/ / Tailscale・LAN 可)");
            }
            else
            {
                Console.WriteLine(" 待ち受けモード: localhost のみ (http://localhost:" + actualPort + "/ / 外部IP・Tailscale 不可)");
            }
            Console.WriteLine("-------------------------------------------------");

            activeListener = listener;

            ThreadPool.QueueUserWorkItem((o) =>
            {
                while (!isShuttingDown && listener != null && listener.IsListening)
                {
                    try
                    {
                        var ctx = listener.GetContext();
                        if (isShuttingDown)
                        {
                            try { ctx.Response.Abort(); } catch { }
                            break;
                        }
                        ThreadPool.QueueUserWorkItem((state) => HandleRequest(ctx));
                    }
                    catch
                    {
                        if (isShuttingDown) break;
                    }
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
                ShutdownServerResources();
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
            ShutdownServerResources();
        }
        Environment.Exit(0);
    }

    private static void ShutdownServerResources()
    {
        isShuttingDown = true;
        if (Interlocked.Exchange(ref cleanupExecuted, 1) != 0) return;

        StopFolderAutoImportWatcher();

        HttpListener l = activeListener;
        activeListener = null;
        if (l != null)
        {
            try { if (l.IsListening) l.Stop(); } catch { }
            try { l.Close(); } catch { }
            try { l.Abort(); } catch { }
        }

        KillCloudflaredProcessTree();

        if (mutex != null)
        {
            try { mutex.ReleaseMutex(); } catch { }
            try { mutex.Dispose(); } catch { }
            mutex = null;
        }

        if (logWriter != null)
        {
            try { Console.WriteLine("[INFO] サーバーを正常終了しました。"); } catch { }
            try { Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null); } catch { }
            try { logWriter.Dispose(); } catch { }
            logWriter = null;
        }
    }

    private static void KillCloudflaredProcessTree()
    {
        Process proc = cloudflaredProcess;
        cloudflaredProcess = null;
        if (proc == null) return;

        try { proc.CancelOutputRead(); } catch { }
        try { proc.CancelErrorRead(); } catch { }

        int pid = 0;
        bool alive = false;
        try
        {
            alive = !proc.HasExited;
            if (alive) pid = proc.Id;
        }
        catch { }

        if (alive && pid > 0)
        {
            try
            {
                ProcessStartInfo tkPsi = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/F /T /PID " + pid,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath()
                };
                using (Process tk = Process.Start(tkPsi))
                {
                    if (tk != null) tk.WaitForExit(1500);
                }
            }
            catch { }

            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill();
                    proc.WaitForExit(1500);
                }
            }
            catch { }
        }

        try { proc.Dispose(); } catch { }
    }

    private static void StartCloudflared(int port)
    {
        ThreadPool.QueueUserWorkItem((o) =>
        {
            if (isShuttingDown) return;
            try
            {
                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloudflared.exe");
                if (!File.Exists(exePath))
                {
                    Console.WriteLine("[cloudflared] cloudflared.exe が見つかりません。");
                    if (!isShuttingDown && qrForm != null) qrForm.UpdateTunnelUrl("");
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "tunnel --url http://127.0.0.1:" + port,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath()
                };

                Process proc = new Process { StartInfo = psi };
                DataReceivedEventHandler handler = (sender, e) =>
                {
                    if (isShuttingDown || string.IsNullOrEmpty(e.Data)) return;
                    if (e.Data.Contains("trycloudflare.com"))
                    {
                        var match = Regex.Match(e.Data, @"https://[a-zA-Z0-9\-]+\.trycloudflare\.com");
                        if (match.Success)
                        {
                            tunnelUrl = match.Value;
                            Console.WriteLine("[cloudflared] トンネルURL発行完了: " + tunnelUrl);
                            if (!isShuttingDown && qrForm != null) qrForm.UpdateTunnelUrl(tunnelUrl);
                        }
                    }
                };

                proc.OutputDataReceived += handler;
                proc.ErrorDataReceived += handler;

                cloudflaredProcess = proc;
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (isShuttingDown)
                {
                    KillCloudflaredProcessTree();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[cloudflared] 起動エラー: " + ex.Message);
                if (!isShuttingDown && qrForm != null) qrForm.UpdateTunnelUrl("");
            }
        });
    }

    private static HttpListener TryStartHttpListener(int port, out string boundUrl, out string listenerMode)
    {
        boundUrl = "";
        listenerMode = "none";
        HttpListener listener = null;

        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://+:" + port + "/");
            listener.Start();
            boundUrl = "http://localhost:" + port + "/";
            listenerMode = "all";
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
            listenerMode = "localhost";
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
            p.StartInfo.WorkingDirectory = Path.GetTempPath();
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



    private static void HandleRequest(HttpListenerContext ctx)
    {
        if (ctx == null) return;
        HttpListenerResponse res = null;
        try
        {
            HttpListenerRequest req = ctx.Request;
            res = ctx.Response;

            // CORS ヘッダー付与
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Photo-Hash, X-Claude-Job-Token, X-Source-Md5, X-File-Name");

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

            string rawPath = req.Url != null ? req.Url.AbsolutePath : "/";

            if (rawPath != "/api/server/heartbeat" && rawPath != "/api/claude/revision")
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] 受信リクエスト: " + req.HttpMethod + " " + rawPath);
            }
            if (rawPath == "/")
            {
                ServeDashboardHtml(res);
            }
            else if (rawPath == "/api/items" && req.HttpMethod == "GET")
            {
                bool isPhoneClient = string.Equals(req.Headers["X-Client-Type"], "phone", StringComparison.OrdinalIgnoreCase);
                string jsonResponse;
                lock (lockObj)
                {
                    if (isPhoneClient)
                    {
                        jsonResponse = JsonConvert.SerializeObject(itemsDb);
                        foreach (var item in itemsDb)
                        {
                            if (item != null) item["syncedToPhone"] = true;
                        }
                    }
                    else
                    {
                        var pcItems = new List<Dictionary<string, object>>(itemsDb.Count);
                        foreach (var item in itemsDb)
                        {
                            if (item == null) continue;
                            var copyItem = new Dictionary<string, object>(item);
                            if (copyItem.ContainsKey("photos") && copyItem["photos"] != null)
                            {
                                var rawPhotos = SyncStore.NormalizePhotosList(copyItem["photos"]);
                                var pcPhotos = new List<Dictionary<string, object>>(rawPhotos.Count);
                                foreach (var p in rawPhotos)
                                {
                                    if (p == null) continue;
                                    var pCopy = new Dictionary<string, object>(p);
                                    string h = pCopy.ContainsKey("md5Hash") && pCopy["md5Hash"] != null
                                        ? pCopy["md5Hash"].ToString().Trim().ToLower()
                                        : "";
                                    string fName = "";
                                    string fPath;
                                    if (!string.IsNullOrEmpty(h) && hashToFilePathMap.TryGetValue(h, out fPath) && !string.IsNullOrEmpty(fPath))
                                    {
                                        fName = Path.GetFileName(fPath);
                                    }
                                    pCopy["fileName"] = fName;
                                    pcPhotos.Add(pCopy);
                                }
                                copyItem["photos"] = pcPhotos;
                            }
                            pcItems.Add(copyItem);
                        }
                        jsonResponse = JsonConvert.SerializeObject(pcItems);
                    }
                }
                if (isPhoneClient)
                {
                    lastHeartbeat = DateTime.Now;
                    if (qrForm != null) qrForm.OnAppConnected();
                }
                byte[] bytes = Encoding.UTF8.GetBytes(jsonResponse);
                res.ContentType = "application/json; charset=utf-8";
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
                res.OutputStream.Close();
            }
            else if (rawPath == "/api/items" && req.HttpMethod == "POST")
            {
                HandleCreateItem(req, res);
            }
            else if (rawPath == "/api/claude/revision" && req.HttpMethod == "GET")
            {
                // フェーズ 6-3: Muse 側の見張り用に変化カウンタだけを公開する。
                // lastHeartbeat は更新しない (ポーリングでスマホ接続表示が点灯し続けないように)。
                if (IsTrustedClaudeIp(req))
                {
                    RecordClaudeWatchContact();
                }
                SendJson(res, new Dictionary<string, object>
                {
                    { "claudeRevision", GetClaudeRevision() },
                    { "executableCount", ClaudeJobStore.CountExecutableJobs() },
                    { "serverBootId", serverBootId }
                });
            }
            else if (rawPath == "/api/server/trusted-muse-ip" && req.HttpMethod == "GET")
            {
                // フェーズ 6-3 (案2 / レビュー 23-2): 許可アドレスはサーバー専用設定。
                // このエンドポイント自体は通常認証 (Bearer / ループバック) の対象で、Muse 免除には含めない。
                SendJson(res, new Dictionary<string, object>
                {
                    { "trustedMuseIp", SyncStore.GetTrustedMuseIp() }
                });
            }
            else if (rawPath == "/api/server/trusted-muse-ip" && req.HttpMethod == "PUT")
            {
                HandleUpdateTrustedMuseIp(req, res);
            }
            else if (rawPath == "/api/server/status")
            {
                long nowUtcTicks = DateTime.UtcNow.Ticks;
                long prevSweep = Interlocked.Read(ref lastExpirySweepTicks);
                if (prevSweep == 0 || (nowUtcTicks - prevSweep) >= 15L * TimeSpan.TicksPerSecond)
                {
                    if (Interlocked.CompareExchange(ref lastExpirySweepTicks, nowUtcTicks, prevSweep) == prevSweep)
                    {
                        foreach (var sweepJob in ClaudeJobStore.GetJobsForExpirySweep())
                        {
                            ApplyApprovalPendingExpiry(sweepJob);
                            ApplyTokenExpiry(sweepJob);
                        }
                    }
                }
                bool isConnected = (DateTime.Now - lastHeartbeat).TotalSeconds < 25;
                int missingCount;
                int trashCount = 0;
                int indexedPhotosCount = 0;
                int totalItemsCount = 0;
                List<KeyValuePair<string, string>> missingSnapshot = null;
                long prevMissingTicks = Interlocked.Read(ref lastMissingCountTicks);
                bool dirtyTriggered = Interlocked.Exchange(ref missingCountDirty, 0) == 1;
                bool needMissingRefresh = false;
                if (dirtyTriggered
                    || prevMissingTicks == 0
                    || (nowUtcTicks - prevMissingTicks) >= 30L * TimeSpan.TicksPerSecond)
                {
                    if (Interlocked.CompareExchange(ref missingRefreshInProgress, 1, 0) == 0)
                    {
                        needMissingRefresh = true;
                    }
                    else if (dirtyTriggered)
                    {
                        Interlocked.Exchange(ref missingCountDirty, 1);
                    }
                }
                try
                {
                    lock (lockObj)
                    {
                        indexedPhotosCount = hashToFilePathMap.Count;
                        totalItemsCount = itemsDb.Count;
                        if (needMissingRefresh)
                        {
                            missingSnapshot = SyncStore.SnapshotPhotoPathsForMissingCheck(itemsDb, hashToFilePathMap);
                        }
                        foreach (var item in itemsDb)
                        {
                            if (IsItemTrashed(item)) trashCount++;
                        }
                    }
                    if (missingSnapshot != null)
                    {
                        missingCount = SyncStore.CountMissingFilesFromSnapshot(missingSnapshot);
                        Interlocked.Exchange(ref cachedMissingCount, missingCount);
                        Interlocked.Exchange(ref lastMissingCountTicks, DateTime.UtcNow.Ticks);
                    }
                    else
                    {
                        missingCount = Interlocked.CompareExchange(ref cachedMissingCount, 0, 0);
                    }
                }
                finally
                {
                    if (needMissingRefresh)
                    {
                        Interlocked.Exchange(ref missingRefreshInProgress, 0);
                    }
                }

                var dict = new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "appConnected", isConnected },
                    { "lastHeartbeatSec", lastHeartbeat == DateTime.MinValue ? -1 : (int)(DateTime.Now - lastHeartbeat).TotalSeconds },
                    { "indexedPhotosCount", indexedPhotosCount },
                    { "missingFileCount", missingCount },
                    { "storeId", SyncStore.StoreId },
                    { "syncDir", SyncStore.SyncDir },
                    { "totalItemsCount", totalItemsCount },
                    { "trashCount", trashCount },
                    { "dataRevision", GetDataRevision() },
                    { "settingsRevision", GetSettingsRevision() },
                    { "claudeRevision", GetClaudeRevision() },
                    { "labelSeenRevision", ClaudeJobStore.GetLabelSeenRevision() },
                    { "lastClaudeWatchContactAt", GetLastClaudeWatchContactAt() },
                    { "lastClaudePatrolContactAt", SyncStore.GetLastClaudePatrolContactAt() },
                    { "trustedMuseIpConfigured", !string.IsNullOrEmpty(SyncStore.GetTrustedMuseIp()) },
                    { "listenerMode", activeListenerMode },
                    { "actualPort", activeActualPort },
                    { "permanentlyDeletedUuids", SyncStore.GetPermanentlyDeletedUuids() }
                };
                SendJson(res, dict);
            }
            else if (rawPath == "/api/server/heartbeat")
            {
                lastHeartbeat = DateTime.Now;
                if (qrForm != null) qrForm.OnHeartbeatReceived();
                SendJson(res, new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "timestamp", DateTime.Now.Ticks },
                    { "dataRevision", GetDataRevision() },
                    { "settingsRevision", GetSettingsRevision() },
                    { "claudeRevision", GetClaudeRevision() },
                    { "labelSeenRevision", ClaudeJobStore.GetLabelSeenRevision() }
                });
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
            else if (rawPath == "/api/settings" && req.HttpMethod == "GET")
            {
                SendJson(res, SyncStore.GetDefaultSettings());
            }
            else if (rawPath == "/api/settings" && req.HttpMethod == "PUT")
            {
                HandleUpdateSettings(req, res);
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
            else if (rawPath == "/api/items/batch-trash" && req.HttpMethod == "POST")
            {
                HandleBatchTrashItems(req, res);
            }
            else if (rawPath == "/api/items/batch-restore" && req.HttpMethod == "POST")
            {
                HandleBatchRestoreItems(req, res);
            }
            else if (rawPath == "/api/items/batch-permanent" && req.HttpMethod == "DELETE")
            {
                HandleBatchPermanentDeleteItems(req, res);
            }
            else if (rawPath == "/api/items/batch-update" && req.HttpMethod == "POST")
            {
                HandleBatchUpdateItems(req, res);
            }
            else if (rawPath == "/api/trash/empty" && req.HttpMethod == "POST")
            {
                HandleEmptyTrash(res);
            }
            else if (rawPath.StartsWith("/api/items/") && rawPath.EndsWith("/unmerge") && req.HttpMethod == "POST")
            {
                HandleUnmergeItem(res, rawPath);
            }
            else if (rawPath.StartsWith("/api/items/") && rawPath.EndsWith("/trash") && req.HttpMethod == "POST")
            {
                HandleTrashItem(res, rawPath);
            }
            else if (rawPath.StartsWith("/api/items/") && rawPath.EndsWith("/restore") && req.HttpMethod == "POST")
            {
                HandleRestoreItem(res, rawPath);
            }
            else if (rawPath.StartsWith("/api/items/") && rawPath.EndsWith("/permanent") && req.HttpMethod == "DELETE")
            {
                HandlePermanentDeleteItem(res, rawPath);
            }
            else if (rawPath.StartsWith("/api/items/") && rawPath.EndsWith("/photos/add") && req.HttpMethod == "POST")
            {
                HandleAddPhoto(req, res, rawPath);
            }
            else if (rawPath.StartsWith("/api/items/") && rawPath.EndsWith("/photos/scan") && req.HttpMethod == "POST")
            {
                HandleScanItemFolderPhotos(res, rawPath);
            }
            else if (rawPath.StartsWith("/api/items/") && rawPath.EndsWith("/photos/delete") && req.HttpMethod == "POST")
            {
                HandleDeletePhoto(req, res, rawPath);
            }
            else if (rawPath == "/api/photos/trash-batch" && req.HttpMethod == "POST")
            {
                HandleTrashPhotosBatch(req, res);
            }
            else if (rawPath == "/api/photo-trash" && req.HttpMethod == "GET")
            {
                HandleGetPhotoTrash(res);
            }
            else if (rawPath.StartsWith("/api/photo-trash/") && rawPath.EndsWith("/image") && req.HttpMethod == "GET")
            {
                string trashId = rawPath.Substring("/api/photo-trash/".Length);
                trashId = trashId.Substring(0, trashId.Length - "/image".Length).Trim();
                ServePhotoTrashImage(req, res, trashId);
            }
            else if (rawPath == "/api/photo-trash/restore" && req.HttpMethod == "POST")
            {
                HandleRestorePhotoTrash(req, res);
            }
            else if (rawPath == "/api/photo-trash/delete" && req.HttpMethod == "POST")
            {
                HandleDeletePhotoTrashPermanent(req, res);
            }
            else if (rawPath == "/api/photo-trash/empty" && req.HttpMethod == "POST")
            {
                HandleEmptyPhotoTrash(res);
            }
            else if (rawPath == "/api/claude/jobs" && req.HttpMethod == "POST")
            {
                HandleCreateClaudeJob(req, res);
            }
            else if (rawPath == "/api/claude/jobs" && req.HttpMethod == "GET")
            {
                HandleGetClaudeJobs(req, res);
            }
            else if (rawPath == "/api/claude/wake" && req.HttpMethod == "POST")
            {
                HandleWakeClaudePatrol(req, res);
            }
            else if (rawPath == "/api/claude/labels/seen" && req.HttpMethod == "POST")
            {
                HandleMarkClaudeLabelSeen(req, res);
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && rawPath.EndsWith("/cancel") && req.HttpMethod == "POST")
            {
                string cancelJobId = rawPath.Substring("/api/claude/jobs/".Length);
                cancelJobId = cancelJobId.Substring(0, cancelJobId.Length - "/cancel".Length).Trim();
                HandleCancelClaudeJob(res, cancelJobId);
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && rawPath.EndsWith("/request-approval") && req.HttpMethod == "POST")
            {
                string raJobId = rawPath.Substring("/api/claude/jobs/".Length);
                raJobId = raJobId.Substring(0, raJobId.Length - "/request-approval".Length).Trim();
                HandleRequestClaudeApproval(res, raJobId);
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && rawPath.EndsWith("/approval") && req.HttpMethod == "POST")
            {
                string apJobId = rawPath.Substring("/api/claude/jobs/".Length);
                apJobId = apJobId.Substring(0, apJobId.Length - "/approval".Length).Trim();
                HandleClaudeApproval(req, res, apJobId);
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && rawPath.Contains("/photos/") && req.HttpMethod == "GET")
            {
                string photoRest = rawPath.Substring("/api/claude/jobs/".Length);
                string[] photoParts = photoRest.Split('/');
                if (photoParts.Length == 3 && photoParts[1] == "photos")
                {
                    HandleGetClaudeJobPhoto(req, res, photoParts[0].Trim(), photoParts[2].Trim());
                }
                else
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Not found" } });
                }
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && rawPath.EndsWith("/reference") && req.HttpMethod == "GET")
            {
                string refJobId = rawPath.Substring("/api/claude/jobs/".Length);
                refJobId = refJobId.Substring(0, refJobId.Length - "/reference".Length).Trim();
                HandleGetClaudeJobReference(req, res, refJobId);
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && rawPath.EndsWith("/errors") && req.HttpMethod == "POST")
            {
                string erJobId = rawPath.Substring("/api/claude/jobs/".Length);
                erJobId = erJobId.Substring(0, erJobId.Length - "/errors".Length).Trim();
                HandlePostClaudeJobError(req, res, erJobId);
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && rawPath.EndsWith("/results") && req.HttpMethod == "POST")
            {
                string rsJobId = rawPath.Substring("/api/claude/jobs/".Length);
                rsJobId = rsJobId.Substring(0, rsJobId.Length - "/results".Length).Trim();
                HandlePostClaudeJobResult(req, res, rsJobId);
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && rawPath.EndsWith("/complete") && req.HttpMethod == "POST")
            {
                string cpJobId = rawPath.Substring("/api/claude/jobs/".Length);
                cpJobId = cpJobId.Substring(0, cpJobId.Length - "/complete".Length).Trim();
                HandleCompleteClaudeJob(req, res, cpJobId);
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && rawPath.EndsWith("/ack") && req.HttpMethod == "POST")
            {
                string ackJobId = rawPath.Substring("/api/claude/jobs/".Length);
                ackJobId = ackJobId.Substring(0, ackJobId.Length - "/ack".Length).Trim();
                HandleAckClaudeJob(res, ackJobId);
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && rawPath.EndsWith("/move") && req.HttpMethod == "POST")
            {
                string moveJobId = rawPath.Substring("/api/claude/jobs/".Length);
                moveJobId = moveJobId.Substring(0, moveJobId.Length - "/move".Length).Trim();
                HandleMoveClaudeJob(req, res, moveJobId);
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && rawPath.EndsWith("/dismiss-error") && req.HttpMethod == "POST")
            {
                string deJobId = rawPath.Substring("/api/claude/jobs/".Length);
                deJobId = deJobId.Substring(0, deJobId.Length - "/dismiss-error".Length).Trim();
                HandleDismissClaudeJobError(res, deJobId);
            }
            else if (rawPath.StartsWith("/api/claude/jobs/") && req.HttpMethod == "GET")
            {
                string detailJobId = rawPath.Substring("/api/claude/jobs/".Length).Trim();
                HandleGetClaudeJob(res, detailJobId);
            }
            else if (rawPath == "/api/claude/references" && req.HttpMethod == "GET")
            {
                HandleGetClaudeReferences(res);
            }
            else if (rawPath == "/api/claude/references/from-item" && req.HttpMethod == "POST")
            {
                HandleRegisterReferenceFromItem(req, res);
            }
            else if (rawPath == "/api/claude/references/open-folder" && req.HttpMethod == "POST")
            {
                HandleOpenReferenceFolder(res);
            }
            else if (rawPath.StartsWith("/api/claude/references/") && rawPath.EndsWith("/thumb") && req.HttpMethod == "GET")
            {
                string thumbMd5 = rawPath.Substring("/api/claude/references/".Length);
                thumbMd5 = thumbMd5.Substring(0, thumbMd5.Length - "/thumb".Length).Trim().ToLowerInvariant();
                ServeReferenceThumb(res, thumbMd5);
            }
            else if (rawPath.StartsWith("/api/claude/references/") && req.HttpMethod == "GET")
            {
                string refMd5 = rawPath.Substring("/api/claude/references/".Length).Trim().ToLowerInvariant();
                ServeReferenceOriginal(res, refMd5);
            }
            else if (rawPath.StartsWith("/api/claude/references/") && req.HttpMethod == "DELETE")
            {
                string delMd5 = rawPath.Substring("/api/claude/references/".Length).Trim().ToLowerInvariant();
                HandleDeleteClaudeReference(res, delMd5);
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
                ServePhotoByHash(req, res, hash);
            }
            else
            {
                res.StatusCode = 404;
                res.Close();
            }
        }
        catch (HttpListenerException)
        {
            try { if (res != null) res.Abort(); } catch { }
        }
        catch (ObjectDisposedException)
        {
            // サーバー停止中
        }
        catch (Exception ex)
        {
            Console.WriteLine("リクエスト処理エラー: " + ex.Message);
            if (res != null)
            {
                try { res.StatusCode = 500; } catch { }
                try { res.Close(); } catch { }
            }
        }
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
